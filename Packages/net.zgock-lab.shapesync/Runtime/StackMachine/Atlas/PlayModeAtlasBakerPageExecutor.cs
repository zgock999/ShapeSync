// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>PlayMode Atlas page executor backed by one explicit scene-local Texture StackMachine host.</summary>
    /// <remarks>Each logical page is lowered to one fully compiled Texture plan. Page result ownership transfers as a TextureDelivery-backed completion.</remarks>
    public sealed class PlayModeAtlasBakerPageExecutor : IAtlasBakerPageExecutor
    {
        private readonly TextureStackMachineHost host;
        private readonly TextureExecutor executor;
        private readonly TextureGpuCapability? partitionCapability;
        private AtlasBakerPagePlan activePage;
        private System.Collections.Generic.IReadOnlyList<AtlasBakerPageRecipePartition> partitions;
        private TextureExecutionHandle handle;
        private TextureOutputLease outputLease;
        private int partitionIndex;
        private TextureExecutionOriginKey origin;
        private bool disposed;

        /// <summary>Creates an executor bound to the supplied scene-local host.</summary>
        public PlayModeAtlasBakerPageExecutor(TextureStackMachineHost host) : this(host, new TextureExecutor(host)) { }

        internal PlayModeAtlasBakerPageExecutor(TextureStackMachineHost host, TextureGpuCapability partitionCapability) : this(host, new TextureExecutor(host)) { this.partitionCapability = partitionCapability; }

        internal PlayModeAtlasBakerPageExecutor(TextureExecutor executor) : this(null, executor) { }

        private PlayModeAtlasBakerPageExecutor(TextureStackMachineHost host, TextureExecutor executor)
        {
            this.host = host;
            this.executor = executor;
        }

        /// <inheritdoc />
        public bool Start(AtlasBakerPagePlan page, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("PlayModeAtlasPageExecutorDisposed", "PlayMode Atlas page executor has been disposed.", out diagnostic);
            if (activePage != null || handle != null) return Reject("PlayModeAtlasPageExecutorBusy", "Take, cancel, or complete the active Atlas page before starting another.", out diagnostic);
            if (executor == null) return Reject("PlayModeTextureExecutorRequired", "PlayMode Atlas execution requires a TextureExecutor.", out diagnostic);
            if (host == null) return Reject("HostRequired", "PlayMode Atlas execution requires a TextureStackMachineHost.", out diagnostic);
            if (!AtlasBakerPageRecipePartitioner.TryCreate(page, partitionCapability ?? host.Capability, out partitions, out diagnostic)) return false;
            activePage = page;
            partitionIndex = 0;
            origin = host.CreateOrigin();
            if (TryStartPartition(out diagnostic)) return true;
            Cancel();
            return false;
        }

        /// <inheritdoc />
        public AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (handle == null)
            {
                if (activePage != null && partitions != null && partitionIndex < partitions.Count)
                {
                    return TryStartPartition(out diagnostic) ? AtlasBakerExecutionStatus.Pending : AtlasBakerExecutionStatus.Failed;
                }
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "PlayModeAtlasPageHandleMissing", "PlayMode Atlas execution has no active Texture handle.");
                return AtlasBakerExecutionStatus.Failed;
            }
            if (!handle.IsCompleted) return AtlasBakerExecutionStatus.Pending;
            diagnostic = handle.Diagnostic;
            if (!handle.Succeeded) return AtlasBakerExecutionStatus.Failed;
            if (partitionIndex >= partitions.Count - 1) return AtlasBakerExecutionStatus.Succeeded;
            if (handle.Result == null || !handle.Result.TryTakeOutputLease(out TextureOutputLease nextLease))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "PlayModeAtlasOutputLeaseMissing", "An intermediate Atlas recipe completed without a retained output lease.");
                return AtlasBakerExecutionStatus.Failed;
            }
            handle.Dispose();
            handle = null;
            outputLease = nextLease;
            partitionIndex++;
            // Leave one Pump boundary between segments. This makes the retained-output state
            // observable and revalidates live hall admission immediately before the next enqueue.
            return AtlasBakerExecutionStatus.Pending;
        }

        /// <inheritdoc />
        public bool TryTakeCompletion(out AtlasBakerPageCompletion completion)
        {
            completion = null;
            if (activePage == null || handle == null || !handle.Succeeded || handle.Result == null || !handle.Result.TryTakeDelivery(out TextureDelivery delivery)) return false;
            RenderTexture texture = delivery.Texture as RenderTexture;
            if (texture == null) { delivery.Dispose(); return false; }
            AtlasBakerPagePlan page = activePage;
            outputLease?.Dispose();
            outputLease = null;
            origin = default;
            handle.Dispose();
            handle = null;
            activePage = null;
            partitions = null;
            completion = new AtlasBakerPageCompletion(page.PageIndex, page.Semantic, texture, _ => delivery.Dispose());
            return true;
        }

        /// <inheritdoc />
        public void Cancel()
        {
            handle?.Dispose();
            handle = null;
            outputLease?.Dispose();
            outputLease = null;
            origin = default;
            activePage = null;
            partitions = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            disposed = true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }

        private bool TryStartPartition(out StackMachineDiagnostic diagnostic)
        {
            AtlasBakerPageRecipePartition partition = partitions[partitionIndex];
            if (!AtlasBakerPageRecipeBuilder.TryCreate(activePage, partition.Operations, partition.InitializesOutput, out TextureExecutionPlan plan, out diagnostic)) return false;
            if (!host.TryValidateAdmission(plan, outputLease != null, out diagnostic)) return false;
            bool final = partitionIndex == partitions.Count - 1;
            var options = new TextureExecutionOptions(outputLease: outputLease, retainOutputLease: !final);
            if (!executor.TryExecute(plan, origin, options, out handle, out diagnostic)) return false;
            return true;
        }
    }
}
