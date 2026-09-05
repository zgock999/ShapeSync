// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Host-issued opaque token used to coalesce unsubmitted requests from one Texture consumer.</summary>
    /// <remarks>
    /// A token is created only by its owning <see cref="TextureStackMachineHost"/>. A caller keeps one token for
    /// one logical request series and reuses it when a newer request should replace the previous unsubmitted request.
    /// The host identity is part of equality, so a token from an unloaded or previous scene cannot be accepted by a
    /// different host. The token is a lightweight value and has no explicit release operation; host teardown makes it unusable.
    /// </remarks>
    public readonly struct TextureExecutionOriginKey : IEquatable<TextureExecutionOriginKey>
    {
        private readonly TextureStackMachineHost host;
        private readonly ulong value;

        internal TextureExecutionOriginKey(TextureStackMachineHost host, ulong value)
        {
            this.host = host;
            this.value = value;
        }

        /// <summary>Gets the host-issued sequence value for diagnostics and white-box inspection.</summary>
        public ulong Value => value;
        /// <summary>Gets whether this host-issued token can participate in queue coalescing.</summary>
        public bool IsValid => !ReferenceEquals(host, null) && value != 0;
        internal bool BelongsTo(TextureStackMachineHost candidate) => IsValid && ReferenceEquals(host, candidate);
        /// <inheritdoc />
        public bool Equals(TextureExecutionOriginKey other) => ReferenceEquals(host, other.host) && value == other.value;
        /// <inheritdoc />
        public override bool Equals(object obj) => obj is TextureExecutionOriginKey other && Equals(other);
        /// <inheritdoc />
        public override int GetHashCode() => ((ReferenceEquals(host, null) ? 0 : RuntimeHelpers.GetHashCode(host)) * 397) ^ value.GetHashCode();
        /// <inheritdoc />
        public static bool operator ==(TextureExecutionOriginKey left, TextureExecutionOriginKey right) => left.Equals(right);
        /// <inheritdoc />
        public static bool operator !=(TextureExecutionOriginKey left, TextureExecutionOriginKey right) => !left.Equals(right);
    }

    /// <summary>Owns one exact-edge Texture StackMachine output until it is disposed by its consumer.</summary>
    public sealed class TextureDelivery : IDisposable
    {
        private Action<Texture> release;
        private Action<TextureDelivery> handoff;

        // Retain the two-argument construction seam used by consumer white-box tests and by
        // integrations that do not transfer host admission ownership.
        internal TextureDelivery(Texture texture, Action<Texture> release)
            : this(texture, release, null)
        {
        }

        internal TextureDelivery(Texture texture, Action<Texture> release, Action<TextureDelivery> handoff)
        {
            Texture = texture;
            this.release = release;
            this.handoff = handoff;
        }

        /// <summary>Gets the owned exact-edge Linear RGBAHalf output texture until disposal.</summary>
        public Texture Texture { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Texture == null) return;
            Texture owned = Texture;
            Texture = null;
            Action<Texture> callback = release;
            release = null;
            callback?.Invoke(owned);
        }

        /// <summary>Marks the delivery as caller-owned after its result carrier transfers it.</summary>
        /// <remarks>The creating host still releases it on host destruction, but it no longer consumes transient admission budget.</remarks>
        internal void MarkHandedOff()
        {
            Action<TextureDelivery> callback = handoff;
            handoff = null;
            callback?.Invoke(this);
        }

        internal void Replace(Texture replacement, Action<Texture> replacementRelease)
        {
            Dispose();
            Texture = replacement;
            release = replacementRelease;
            handoff = null;
        }
    }

    /// <summary>Caller-owned retained GPU source halls for one unchanged set of Texture bindings.</summary>
    /// <remarks>A lease is valid only while the same source <see cref="Texture"/> objects remain bound. Content changes inside a retained source are caller-managed and require disposal before re-ingest.</remarks>
    public sealed class TextureSourceLease : IDisposable
    {
        internal sealed class Binding
        {
            internal Binding(Texture texture, TextureHallAllocation hall) { Texture = texture; Hall = hall; }
            internal Texture Texture { get; }
            internal TextureHallAllocation Hall { get; }
        }

        private TextureStackMachineHost host;
        private Dictionary<string, Binding> bindings;
        private int useCount;
        private bool releaseRequested;

        internal TextureSourceLease(TextureStackMachineHost host, Dictionary<string, Binding> bindings)
        {
            this.host = host;
            this.bindings = bindings;
        }

        /// <summary>Gets whether this lease still belongs to a live Texture StackMachine host.</summary>
        public bool IsValid => host != null && bindings != null;

        /// <inheritdoc />
        public void Dispose() { TryDispose(out _); }

        /// <summary>Requests release and reports a structured diagnostic when this lease was already released.</summary>
        public bool TryDispose(out StackMachineDiagnostic diagnostic)
        {
            TextureStackMachineHost owner = host;
            if (owner == null || releaseRequested)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseAlreadyReleased", "The retained source lease was already released or invalidated.");
                return false;
            }
            releaseRequested = true;
            if (useCount == 0) owner.ReleaseSourceLease(this);
            diagnostic = null;
            return true;
        }

        internal bool TryResolve(string logicalName, Texture texture, out TextureHallAllocation hall)
        {
            hall = default;
            return IsValid && bindings.TryGetValue(logicalName, out Binding binding) && binding.Texture == texture && (hall = binding.Hall).IsValid;
        }

        internal bool Matches(TextureDispatchPlan plan, TextureBindingContext context)
        {
            if (!IsValid || plan == null || context == null || bindings.Count != plan.ReadSourceNames.Count) return false;
            foreach (string logicalName in plan.ReadSourceNames)
            {
                if (!context.TryGetBinding(logicalName, out TextureBinding binding) || binding.Kind != TextureBindingKind.SourceTexture || !TryResolve(logicalName, binding.SourceTexture, out _)) return false;
            }
            return true;
        }

        internal bool TryAcquire()
        {
            if (!IsValid || releaseRequested) return false;
            useCount++;
            return true;
        }

        internal bool BelongsTo(TextureStackMachineHost owner) => ReferenceEquals(host, owner);

        internal void ReleaseUse()
        {
            if (useCount == 0) return;
            useCount--;
            if (useCount == 0 && releaseRequested) host?.ReleaseSourceLease(this);
        }

        internal void InvalidateFromHost()
        {
            host = null;
            bindings = null;
            useCount = 0;
            releaseRequested = true;
        }

        internal void ReleaseFromHost(TextureStackMachineHost owner)
        {
            if (!ReferenceEquals(host, owner) || bindings == null) return;
            foreach (Binding binding in bindings.Values) owner.TryReleaseHall(binding.Hall);
            InvalidateFromHost();
        }
    }

    /// <summary>Caller-owned retained GPU output hall for cumulative Texture recipes.</summary>
    public sealed class TextureOutputLease : IDisposable
    {
        private TextureStackMachineHost host;
        private TextureHallAllocation hall;
        private int useCount;
        private bool releaseRequested;

        internal TextureOutputLease(TextureStackMachineHost host, TextureHallAllocation hall)
        {
            this.host = host;
            this.hall = hall;
        }

        /// <summary>Gets whether this lease still owns a hall on its creating host.</summary>
        public bool IsValid => host != null && hall.IsValid;

        /// <summary>Gets the retained output width in texels.</summary>
        public int Width => hall.Width;
        /// <summary>Gets the retained output height in texels.</summary>
        public int Height => hall.Height;

        /// <inheritdoc />
        public void Dispose() { TryDispose(out _); }

        /// <summary>Requests release and reports a structured diagnostic when this lease was already released.</summary>
        public bool TryDispose(out StackMachineDiagnostic diagnostic)
        {
            TextureStackMachineHost owner = host;
            if (owner == null || releaseRequested)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseAlreadyReleased", "The retained output lease was already released or invalidated.");
                return false;
            }
            releaseRequested = true;
            if (useCount == 0) owner.ReleaseOutputLease(this);
            diagnostic = null;
            return true;
        }

        internal bool TryAcquire(int width, int height)
        {
            if (!IsValid || releaseRequested || hall.Width != width || hall.Height != height) return false;
            useCount++;
            return true;
        }

        internal void ReleaseUse()
        {
            if (useCount == 0) return;
            useCount--;
            if (useCount == 0 && releaseRequested) host?.ReleaseOutputLease(this);
        }

        internal TextureHallAllocation Hall => hall;
        internal bool BelongsTo(TextureStackMachineHost owner) => ReferenceEquals(host, owner);
        internal bool MatchesExtent(int width, int height) => IsValid && hall.Width == width && hall.Height == height;

        internal void ReleaseFromHost(TextureStackMachineHost owner)
        {
            if (!ReferenceEquals(host, owner)) return;
            owner.TryReleaseHall(hall);
            host = null;
            hall = default;
            useCount = 0;
            releaseRequested = true;
        }
    }

    /// <summary>Optional caller-owned source lease settings for one Texture execution.</summary>
    public sealed class TextureExecutionOptions
    {
        /// <summary>Creates execution settings.</summary>
        /// <param name="sourceLease">Retained source halls to reuse, or <see langword="null"/> to ingest all sources.</param>
        /// <param name="retainSourceLease">Whether a successful execution transfers newly ingested source halls to its result.</param>
        public TextureExecutionOptions(TextureSourceLease sourceLease = null, TextureOutputLease outputLease = null, bool retainSourceLease = false, bool retainOutputLease = false)
        {
            SourceLease = sourceLease;
            OutputLease = outputLease;
            RetainSourceLease = retainSourceLease;
            RetainOutputLease = retainOutputLease;
        }

        /// <summary>Gets the caller-owned source lease to reuse.</summary>
        public TextureSourceLease SourceLease { get; }
        /// <summary>Gets the caller-owned output hall to use as this recipe's <c>$out</c>, if any.</summary>
        public TextureOutputLease OutputLease { get; }
        /// <summary>Gets whether this execution creates a source lease after successful GPU completion.</summary>
        public bool RetainSourceLease { get; }
        /// <summary>Gets whether this recipe leaves its output in a retained hall instead of publishing a delivery.</summary>
        public bool RetainOutputLease { get; }
    }

    /// <summary>Terminal Texture execution state with a single-transfer delivery seam.</summary>
    public sealed class TextureExecutionResult : IDisposable
    {
        private TextureDelivery delivery;
        private TextureSourceLease sourceLease;
        private TextureOutputLease outputLease;

        internal TextureExecutionResult(TextureDelivery delivery, TextureSourceLease sourceLease = null, TextureOutputLease outputLease = null) { this.delivery = delivery; this.sourceLease = sourceLease; this.outputLease = outputLease; }

        /// <summary>Transfers the output delivery once to the caller.</summary>
        /// <param name="value">Transferred delivery on success; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when an unclaimed delivery was transferred.</returns>
        public bool TryTakeDelivery(out TextureDelivery value)
        {
            value = delivery;
            delivery = null;
            value?.MarkHandedOff();
            return value != null;
        }

        /// <summary>Transfers newly retained source halls once to the caller.</summary>
        /// <param name="value">Transferred source lease on success; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when this execution created an unclaimed source lease.</returns>
        public bool TryTakeSourceLease(out TextureSourceLease value)
        {
            value = sourceLease;
            sourceLease = null;
            return value != null;
        }

        /// <summary>Transfers a newly retained output hall once to the caller.</summary>
        public bool TryTakeOutputLease(out TextureOutputLease value)
        {
            value = outputLease;
            outputLease = null;
            return value != null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            delivery?.Dispose();
            delivery = null;
            sourceLease?.Dispose();
            sourceLease = null;
            outputLease?.Dispose();
            outputLease = null;
        }
    }

    /// <summary>Represents one queued Texture execution until its GPU fence completes.</summary>
    public sealed class TextureExecutionHandle : IDisposable
    {
        private bool completed;
        private StackMachineDiagnostic diagnostic;
        private Action<TextureExecutionHandle> cancel;

        /// <summary>Raised on the Unity main thread when this request reaches a terminal state.</summary>
        public event Action<TextureExecutionHandle> Completed;
        /// <summary>Gets whether execution has reached a terminal state.</summary>
        public bool IsCompleted => completed;
        /// <summary>Gets whether GPU work completed successfully.</summary>
        public bool Succeeded { get; private set; }
        /// <summary>Gets structured failure information, or <see langword="null"/> after success.</summary>
        public StackMachineDiagnostic Diagnostic => diagnostic;
        /// <summary>Gets the terminal result after success. Dispose it when its delivery is not taken.</summary>
        public TextureExecutionResult Result { get; private set; }

        internal void CompleteGpuFence(TextureDelivery delivery, TextureSourceLease sourceLease, TextureOutputLease outputLease)
        {
            if (completed) return;
            completed = true;
            cancel = null;
            Succeeded = true;
            Result = new TextureExecutionResult(delivery, sourceLease, outputLease);
            Completed?.Invoke(this);
        }

        internal void CompleteFailure(StackMachineDiagnostic value)
        {
            if (completed) return;
            completed = true;
            cancel = null;
            Succeeded = false;
            diagnostic = value;
            Completed?.Invoke(this);
        }

        /// <summary>Releases an unclaimed successful delivery owned by this handle.</summary>
        public void Dispose()
        {
            if (!completed) cancel?.Invoke(this);
            Result?.Dispose();
            Result = null;
        }

        internal void SetCancellation(Action<TextureExecutionHandle> value) { cancel = value; }
    }

    /// <summary>Fully compiled executor that validates a Texture recipe and queues it on one scene-local host.</summary>
    public sealed class TextureExecutor : StackMachineDomainExecutorBase
    {
        private readonly TextureStackMachineHost host;
        private TextureExecutionOriginKey executionOrigin;
        private TextureExecutionHandle executionHandle;
        private TextureExecutionOptions executionOptions;

        /// <summary>Creates an executor bound to one explicit scene-local host.</summary>
        /// <param name="host">Scene-local host that owns the fixed grid and GPU queue.</param>
        public TextureExecutor(TextureStackMachineHost host)
        {
            this.host = host;
        }

        /// <inheritdoc />
        public override StackMachineDomainExecutionMode ExecutionMode => StackMachineDomainExecutionMode.FullyCompiled;

        /// <summary>Compiles and queues one non-persistent Texture recipe.</summary>
        /// <param name="stub">In-memory recipe document and bindings to compile.</param>
        /// <param name="origin">Origin token issued by this executor's host.</param>
        /// <param name="handle">Execution handle on success; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic">Validation, compilation, or exact host queue diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the compiled request was accepted by the host queue.</returns>
        /// <remarks>Host rejection diagnostics are propagated unchanged; this executor does not replace GPU budget, grid, or reservation failures with a generic queue-rejected diagnostic.</remarks>
        public bool TryExecute(TextureRecipeStub stub, TextureExecutionOriginKey origin, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
        {
            return TryExecute(stub, origin, null, out handle, out diagnostic);
        }

        /// <summary>Compiles and queues one recipe with optional retained source halls.</summary>
        /// <param name="stub">In-memory recipe document and bindings to compile.</param>
        /// <param name="origin">Origin token issued by this executor's host.</param>
        /// <param name="options">Optional source-lease reuse or retention settings.</param>
        /// <param name="handle">Execution handle on success; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic">Validation, compilation, or exact host queue diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the compiled request was accepted by the host queue.</returns>
        public bool TryExecute(TextureRecipeStub stub, TextureExecutionOriginKey origin, TextureExecutionOptions options, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
        {
            handle = null;
            if (host == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostRequired", "TextureExecutor requires a TextureStackMachineHost.");
                return false;
            }
            if (!origin.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OriginKeyRequired", "Texture execution requires an origin token issued by the TextureStackMachineHost.");
                return false;
            }
            if (!host.TryInitialize(out diagnostic)) return false;
            if (!TextureExecutionPlan.TryCreate(stub, out TextureExecutionPlan plan, out diagnostic)) return false;
            if (options?.SourceLease != null && !options.SourceLease.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "Texture execution received an invalid retained source lease.");
                return false;
            }
            if (options?.OutputLease != null && !options.OutputLease.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "Texture execution received an invalid retained output lease.");
                return false;
            }
            executionOrigin = origin;
            executionHandle = new TextureExecutionHandle();
            executionOptions = options;
            bool accepted = base.TryExecute(plan.DispatchPlan, plan.BindingContext, out StackMachineExecutionResult executionResult);
            handle = executionHandle;
            executionHandle = null;
            executionOrigin = default;
            executionOptions = null;
            if (accepted) return true;
            diagnostic = executionResult.Diagnostic;
            handle = null;
            return false;
        }

        /// <summary>Queues one already compiled Texture plan without recompiling its recipe.</summary>
        /// <remarks>Used by caller-driven backend adapters that receive a shared <see cref="TextureExecutionPlan"/> from Core.</remarks>
        public bool TryExecute(TextureExecutionPlan plan, TextureExecutionOriginKey origin, TextureExecutionOptions options, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
        {
            handle = null;
            if (host == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostRequired", "TextureExecutor requires a TextureStackMachineHost.");
                return false;
            }
            if (plan == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TextureExecutionPlanRequired", "Texture execution requires a compiled TextureExecutionPlan.");
                return false;
            }
            if (!origin.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OriginKeyRequired", "Texture execution requires an origin token issued by the TextureStackMachineHost.");
                return false;
            }
            if (!host.TryInitialize(out diagnostic)) return false;
            if (options?.SourceLease != null && !options.SourceLease.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "Texture execution received an invalid retained source lease.");
                return false;
            }
            if (options?.OutputLease != null && !options.OutputLease.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "Texture execution received an invalid retained output lease.");
                return false;
            }
            handle = new TextureExecutionHandle();
            if (host.TryEnqueue(plan.DispatchPlan, plan.BindingContext, origin, handle, options, out diagnostic))
            {
                handle.SetCancellation(host.Cancel);
                return true;
            }
            handle = null;
            return false;
        }

        /// <inheritdoc />
        public override bool TryCompileDomainPlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic)
        {
            domainPlan = null;
            if (!(bindingContext is TextureBindingContext context))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TextureBindingContextRequired", "TextureExecutor requires TextureBindingContext.");
                return false;
            }
            if (!TexturePlanCompiler.TryCompile(plan, context.Document, context, out TextureDispatchPlan texturePlan, out diagnostic)) return false;
            domainPlan = texturePlan;
            return true;
        }

        /// <inheritdoc />
        protected override bool TryExecuteDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic)
        {
            if (host == null || !(domainPlan is TextureDispatchPlan texturePlan) || !(bindingContext is TextureBindingContext context) || executionHandle == null || !executionOrigin.IsValid)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TextureExecutionStateInvalid", "TextureExecutor execution state is incomplete.");
                return false;
            }
            if (!host.TryEnqueue(texturePlan, context, executionOrigin, executionHandle, executionOptions, out diagnostic)) return false;
            executionHandle.SetCancellation(host.Cancel);
            return true;
        }
    }
}
