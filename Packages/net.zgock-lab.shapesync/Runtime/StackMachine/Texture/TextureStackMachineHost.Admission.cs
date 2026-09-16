// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Globalization;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Admission validation and coalesce commit for the prepared path (Spec15-2 §8.1, §8.2, §8.4, §9 as made
    /// concrete by Bootups/Phase06-7-1). Validation changes nothing but the normal-kernel cache; the commit stage
    /// runs only after every validation step passed and never flushes notifications. The public entrypoints stay on
    /// the legacy path until 06-26.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Validates one prepared admission in the fixed V01-V07 order (Host state, kernel contract, hall plan,
        /// requested Leases, standalone layout, delivery budget with replacement credit) and commits in the fixed
        /// C01-C08 order. A rejected admission leaves queue, map, accounting, prior requests, submitted state,
        /// caller Leases, and the allocator unchanged. No callbacks run inside this method.
        /// </summary>
        /// <param name="plan">The compiled dispatch plan.</param>
        /// <param name="context">The binding context for the plan.</param>
        /// <param name="origin">The host-issued origin token (valid, or <see langword="default"/> when absent).</param>
        /// <param name="handle">The fresh, incomplete handle the Executor created for this request.</param>
        /// <param name="options">Execution options, or <see langword="null"/>.</param>
        /// <param name="diagnostic">Rejection diagnostic; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the candidate was committed to the pending queue.</returns>
        private bool TryEnqueuePrepared(TextureDispatchPlan plan, TextureBindingContext context, TextureExecutionOriginKey origin, TextureExecutionHandle handle, TextureExecutionOptions options, out StackMachineDiagnostic diagnostic)
        {
            // V01: Host state, inputs, and origin in §8.4 order.
            if (faulted) { diagnostic = Reject("HostFaulted"); return false; }
            if (destroying) { diagnostic = Reject("HostDestroyed"); return false; }
            if (closingNotifications || !isActiveAndEnabled) { diagnostic = Reject("HostDisabled"); return false; }
            if (!initialized) { diagnostic = Reject("HostNotInitialized"); return false; }
            if (!acceptingRequests) { diagnostic = Reject("HostDisabled"); return false; }
            if (plan == null || context == null || handle == null) { diagnostic = Reject("QueueInputRequired"); return false; }
            if (!origin.IsValid) { diagnostic = Reject("OriginKeyRequired"); return false; }
            if (!origin.BelongsTo(this)) { diagnostic = Reject("OriginHostMismatch"); return false; }

            // V02: kernel contract, the ingest/Delivery-copy pair, then one record-order scan.
            if (computeProgram == null) { diagnostic = Reject("ComputeProgramRequired"); return false; }
            if (kernels == null || kernels.Length != 14) { diagnostic = Reject("ComputeKernelMissing"); return false; }
            if (kernels[11] < 0 || kernels[12] < 0) { diagnostic = Reject("ComputeKernelMissing"); return false; }
            if (UsesNormalOperations(plan) && !TryEnsureNormalKernels(out diagnostic)) return false;
            for (int i = 0; i < plan.Records.Count; i++)
            {
                if (!TryRecordKernel(plan.Records[i].Operation, out ComputeShader kernelProgram, out int kernelIndex) || kernelProgram == null || kernelIndex < 0)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "DispatchOperationUnsupported", "DispatchOperationUnsupported", instructionPointer: i);
                    return false;
                }
            }

            // V03: immutable hall plan; this API's own diagnostics are adopted as-is.
            if (!TextureRequestHallPlan.TryCreate(plan, context, out TextureRequestHallPlan hallPlan, out diagnostic)) return false;

            // V04: requested source lease; a binding mismatch switches to the new-ingest candidate without disposing.
            TextureSourceLease requestedSource = options?.SourceLease;
            TextureSourceLease leaseToInvalidateOnAccept = null;
            if (requestedSource != null)
            {
                if (!requestedSource.BelongsTo(this)) { diagnostic = Reject("SourceLeaseHostMismatch"); return false; }
                if (!requestedSource.CanAcquire) { diagnostic = Reject("SourceLeaseInvalid"); return false; }
                if (!requestedSource.Matches(plan, context))
                {
                    leaseToInvalidateOnAccept = requestedSource;
                    requestedSource = null;
                }
            }

            // V05: requested output lease; no fallback like the Source stage has.
            TextureOutputLease requestedOutput = options?.OutputLease;
            if (requestedOutput != null)
            {
                if (!requestedOutput.BelongsTo(this)) { diagnostic = Reject("OutputLeaseHostMismatch"); return false; }
                if (!requestedOutput.IsValid || requestedOutput.IsReleaseRequested) { diagnostic = Reject("OutputLeaseInvalid"); return false; }
                if (!requestedOutput.MatchesExtent(plan.OutputWidth, plan.OutputHeight)) { diagnostic = Reject("OutputLeaseExtentMismatch"); return false; }
                if (!requestedOutput.CanAcquire(plan.OutputWidth, plan.OutputHeight)) { diagnostic = Reject("OutputLeaseInvalid"); return false; }
            }

            // V06: the candidate; a validation failure holds no resources, so no release is ever called on it.
            var candidate = new QueuedRequest
            {
                plan = plan,
                context = context,
                origin = origin,
                handle = handle,
                hallPlan = hallPlan,
                requestedSourceLease = requestedSource,
                requestedOutputLease = requestedOutput,
                retainSourceLease = options?.RetainSourceLease == true,
                retainOutputLease = options?.RetainOutputLease == true,
                reservedDeliveryBytes = checked((long)hallPlan.OutputWidth * hallPlan.OutputHeight * 8L),
            };
            if (!TryValidateStandaloneLayout(candidate, out diagnostic)) return false;

            // V07: delivery budget with the replacement credit; the live count is read exactly once.
            pendingByOrigin.TryGetValue(origin, out QueuedRequest replaced);
            long credit = replaced == null ? 0L : replaced.reservedDeliveryBytes;
            long originalPending = pendingDeliveryBytes;
            long effectivePending = originalPending - credit;
            long gridBytes = checked((long)capability.FixedGridEdge * capability.FixedGridEdge * 8L);
            long liveBytes = GetLiveTransientGpuBytes();
            long budget = capability.GpuBudgetBytes;
            if (!TryValidateDeliveryReservation(gridBytes, liveBytes, effectivePending, candidate.reservedDeliveryBytes, budget, out _))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuTransientBudgetExceeded", "GpuTransientBudgetExceeded", detail: "grid=" + gridBytes.ToString(CultureInfo.InvariantCulture) + ";live=" + liveBytes.ToString(CultureInfo.InvariantCulture) + ";pending=" + originalPending.ToString(CultureInfo.InvariantCulture) + ";replacementCredit=" + credit.ToString(CultureInfo.InvariantCulture) + ";candidate=" + candidate.reservedDeliveryBytes.ToString(CultureInfo.InvariantCulture) + ";budget=" + budget.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            // Commit (C01-C08). No callbacks, no flush, no legacy release path.
            handle.SetCancellation(CancelPrepared);
            if (replaced != null)
            {
                DetachPending(replaced);
                replaced.reservedDeliveryBytes = 0;
                ClearRequestReferences(replaced);
                if (replaced.handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestCoalesced", "RequestCoalesced"), null))
                    EnqueueNotification(replaced.handle);
            }
            if (submitted != null && submitted.origin == origin) submitted.stale = true;
            pending.Enqueue(candidate);
            pendingByOrigin.Add(origin, candidate);
            pendingDeliveryBytes += candidate.reservedDeliveryBytes;
            leaseToInvalidateOnAccept?.Dispose();
            diagnostic = null;
            return true;
        }

        // Pure helper: rejection diagnostic whose message is fixed to its code (Bootups/Phase06-7-1 §3).
        private static StackMachineDiagnostic Reject(string code)
            => StackMachineDiagnostic.CreateDomain("texture", code, code);
    }
}
