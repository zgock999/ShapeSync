// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Reports the caller-visible state of an Atlas page execution operation.</summary>
    public enum AtlasBakerExecutionStatus { Pending, Succeeded, Failed, Cancelled }

    /// <summary>Owns one executed Atlas page until its receiver disposes it.</summary>
    /// <remarks>The concrete backend supplies the release callback. This shared carrier has no UnityEditor dependency.</remarks>
    public sealed class AtlasBakerPageCompletion : IDisposable
    {
        private Action<RenderTexture> release;

        /// <summary>Creates a completion for one semantic page.</summary>
        public AtlasBakerPageCompletion(int pageIndex, AtlasTextureSemantic semantic, RenderTexture texture, Action<RenderTexture> release)
        {
            PageIndex = pageIndex;
            Semantic = semantic;
            Texture = texture;
            this.release = release;
        }

        /// <summary>Gets the solved page index.</summary>
        public int PageIndex { get; }
        /// <summary>Gets the semantic represented by <see cref="Texture"/>.</summary>
        public AtlasTextureSemantic Semantic { get; }
        /// <summary>Gets the linear in-memory page texture until disposal.</summary>
        public RenderTexture Texture { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            RenderTexture owned = Texture;
            Texture = null;
            Action<RenderTexture> callback = release;
            release = null;
            callback?.Invoke(owned);
        }
    }

    /// <summary>Backend seam for one caller-driven Atlas page execution.</summary>
    /// <remarks>The backend owns recipe partitioning, dispatch, fences, and temporary resources. It must expose exactly one completion per accepted page.</remarks>
    public interface IAtlasBakerPageExecutor : IDisposable
    {
        /// <summary>Starts execution for one page-local Core plan.</summary>
        bool Start(AtlasBakerPagePlan page, out StackMachineDiagnostic diagnostic);
        /// <summary>Advances the accepted page without self-scheduling.</summary>
        AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic);
        /// <summary>Transfers the succeeded page completion exactly once.</summary>
        bool TryTakeCompletion(out AtlasBakerPageCompletion completion);
        /// <summary>Cancels the accepted page and releases only backend-owned resources.</summary>
        void Cancel();
    }

    /// <summary>Owns untaken executed Atlas pages until the next compiler phase accepts or disposes them.</summary>
    public sealed class AtlasBakerExecutionResult : IDisposable
    {
        private AtlasBakerPageCompletion[] pages;

        internal AtlasBakerExecutionResult(AtlasBakerPageCompletion[] pages)
        {
            this.pages = pages ?? Array.Empty<AtlasBakerPageCompletion>();
        }

        /// <summary>Gets all completed semantic pages in deterministic Core page order.</summary>
        public IReadOnlyList<AtlasBakerPageCompletion> Pages => Array.AsReadOnly(pages);

        /// <summary>Transfers all page completions once to the candidate mutation phase.</summary>
        public AtlasBakerPageCompletion[] DetachPages()
        {
            AtlasBakerPageCompletion[] value = pages;
            pages = Array.Empty<AtlasBakerPageCompletion>();
            return value;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < pages.Length; i++) pages[i]?.Dispose();
            pages = Array.Empty<AtlasBakerPageCompletion>();
        }
    }

    /// <summary>Owns the named semantic Atlas pages attached to a successful in-memory Humanoid candidate.</summary>
    /// <remarks>Preserves page-group and semantic identity for the later publish transaction without exposing TSM records or backend handles.</remarks>
    public sealed class AtlasBakerCandidatePages : IDisposable
    {
        private AtlasBakerPageCompletion[] pages;

        internal AtlasBakerCandidatePages(AtlasBakerPageCompletion[] pages)
        {
            this.pages = pages ?? Array.Empty<AtlasBakerPageCompletion>();
        }

        /// <summary>Gets the candidate-owned pages in deterministic Core order.</summary>
        public IReadOnlyList<AtlasBakerPageCompletion> Pages => Array.AsReadOnly(pages);

        /// <inheritdoc />
        public void Dispose()
        {
            for (int i = 0; i < pages.Length; i++) pages[i]?.Dispose();
            pages = Array.Empty<AtlasBakerPageCompletion>();
        }
    }

    /// <summary>
    /// Shared caller-pumped execution lifecycle for the logical result of <see cref="AtlasBakerOperation"/>.
    /// It never selects a Texture recipe partition, polls itself, mutates candidate Mesh/Material state, or publishes assets.
    /// </summary>
    public sealed class AtlasBakerExecutionOperation : IDisposable
    {
        private readonly AtlasBakerResult logicalResult;
        private readonly IAtlasBakerPageExecutor executor;
        private readonly List<AtlasBakerPageCompletion> completed = new List<AtlasBakerPageCompletion>();
        private int nextPage;
        private bool started;
        private bool resultTaken;
        private bool disposed;

        /// <summary>Creates an unstarted execution lifecycle for one successful Core result.</summary>
        public AtlasBakerExecutionOperation(AtlasBakerResult logicalResult, IAtlasBakerPageExecutor executor)
        {
            this.logicalResult = logicalResult;
            this.executor = executor;
            Status = AtlasBakerExecutionStatus.Pending;
        }

        /// <summary>Gets the current lifecycle state.</summary>
        public AtlasBakerExecutionStatus Status { get; private set; }
        /// <summary>Gets the terminal structured diagnostic, or null on success/cancel.</summary>
        public StackMachineDiagnostic Diagnostic { get; private set; }

        /// <summary>Starts or advances at most one backend page execution.</summary>
        public AtlasBakerExecutionStatus Pump()
        {
            if (Status != AtlasBakerExecutionStatus.Pending) return Status;
            if (disposed) return Fail("AtlasBakerExecutionDisposed", "Atlas Baker execution operation has already been disposed.");
            if (logicalResult == null) return Fail("AtlasBakerLogicalResultRequired", "Atlas Baker execution requires a successful logical Core result.");
            if (nextPage >= logicalResult.Pages.Count) return Succeed();
            if (executor == null) return Fail("AtlasBakerPageExecutorRequired", "Atlas Baker execution requires a concrete page executor when logical pages exist.");

            if (!started)
            {
                if (!executor.Start(logicalResult.Pages[nextPage], out StackMachineDiagnostic startDiagnostic))
                    return Fail(startDiagnostic ?? StackMachineDiagnostic.CreateDomain("atlas", "AtlasBakerPageStartFailed", "Atlas Baker page executor rejected a page without a diagnostic."));
                started = true;
                return Status;
            }

            AtlasBakerExecutionStatus pageStatus = executor.Pump(out StackMachineDiagnostic diagnostic);
            if (pageStatus == AtlasBakerExecutionStatus.Pending) return Status;
            if (pageStatus == AtlasBakerExecutionStatus.Cancelled)
            {
                started = false;
                DisposeUntakenCompletions();
                Status = AtlasBakerExecutionStatus.Cancelled;
                Diagnostic = null;
                return Status;
            }
            if (pageStatus != AtlasBakerExecutionStatus.Succeeded)
                return Fail(diagnostic ?? StackMachineDiagnostic.CreateDomain("atlas", "AtlasBakerPageExecutionFailed", "Atlas Baker page executor failed without a diagnostic."));
            if (!executor.TryTakeCompletion(out AtlasBakerPageCompletion completion) || completion == null)
                return Fail("AtlasBakerPageCompletionMissing", "Atlas Baker page executor succeeded without a single-take completion.");

            completed.Add(completion);
            nextPage++;
            started = false;
            return nextPage >= logicalResult.Pages.Count ? Succeed() : Status;
        }

        /// <summary>Transfers successful page completions exactly once.</summary>
        public bool TryTakeResult(out AtlasBakerExecutionResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (disposed) return Reject("AtlasBakerExecutionDisposed", "Atlas Baker execution operation has already been disposed.", out diagnostic);
            if (Status != AtlasBakerExecutionStatus.Succeeded) return Reject("AtlasBakerExecutionResultUnavailable", "Atlas Baker execution result is available only after successful completion.", out diagnostic);
            if (resultTaken) return Reject("AtlasBakerExecutionResultAlreadyTaken", "Atlas Baker execution result was already transferred.", out diagnostic);
            resultTaken = true;
            result = new AtlasBakerExecutionResult(completed.ToArray());
            completed.Clear();
            return true;
        }

        /// <summary>Cancels active backend work and disposes every completion not yet transferred.</summary>
        public void Cancel()
        {
            if (disposed || Status != AtlasBakerExecutionStatus.Pending) return;
            if (started) executor?.Cancel();
            DisposeUntakenCompletions();
            Status = AtlasBakerExecutionStatus.Cancelled;
            Diagnostic = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            DisposeUntakenCompletions();
            executor?.Dispose();
            disposed = true;
        }

        private AtlasBakerExecutionStatus Succeed()
        {
            Status = AtlasBakerExecutionStatus.Succeeded;
            Diagnostic = null;
            return Status;
        }

        private AtlasBakerExecutionStatus Fail(string code, string message)
            => Fail(StackMachineDiagnostic.CreateDomain("atlas", code, message));

        private AtlasBakerExecutionStatus Fail(StackMachineDiagnostic diagnostic)
        {
            if (started) executor?.Cancel();
            DisposeUntakenCompletions();
            Diagnostic = diagnostic;
            Status = AtlasBakerExecutionStatus.Failed;
            return Status;
        }

        private void DisposeUntakenCompletions()
        {
            for (int i = 0; i < completed.Count; i++) completed[i]?.Dispose();
            completed.Clear();
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }
    }
}
