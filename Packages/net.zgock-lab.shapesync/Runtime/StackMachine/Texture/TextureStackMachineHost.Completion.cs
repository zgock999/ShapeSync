// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// The fence-completion artifact handoff for prepared requests (Spec15-2 §12.1, §12.3, §12.4, §12.6). The
    /// branch order is fixed cancelled/already-terminal then stale then success so an already-determined
    /// cancellation is never overwritten. Only newly owned Sources/Output move into new Leases; borrowed
    /// resources never create a new Lease because no two Leases may own the same physical hall. Owned fields
    /// transferred into an artifact are emptied before the cleanup runs so each hall has exactly one owner, and
    /// a Result is disposed here only when it could not be handed to the terminal state. Notifications are
    /// enqueued but never flushed by this file; the flush belongs to the caller.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Finalizes one prepared request whose fence has passed. First the submitted slot is emptied. A
        /// cancelled or already-terminal handle gets cleanup only (delivery disposed, §7.4 rollback,
        /// reservation zeroed, references cleared) with no re-notification. A stale request gets the same
        /// cleanup and is then terminalized with <c>RequestStale</c> and enqueued. Success moves only the new
        /// owned Sources (from ownedSourceHalls) and the new owned Output (from a valid ownedOutputHall) into
        /// new Leases, detaches the delivery for the Result, empties the transferred fields before
        /// <see cref="ReleasePreparedResources"/>, and hands the Result to
        /// <see cref="TextureExecutionHandle.TrySetTerminalState"/>; a false return disposes only this
        /// untransferred Result. The notification is enqueued; the flush is left to the caller.
        /// </summary>
        /// <param name="r">The submitted request whose GPU fence has passed.</param>
        private void FinishPassedRequest(QueuedRequest r)
        {
            // §12.1 step 1: the submitted slot empties first; r is the detached local.
            submitted = null;

            // §12.3/§12.6: cancelled and already-terminal handles are cleanup only; no re-notification.
            if (r.cancelled || (r.handle != null && r.handle.IsCompleted))
            {
                r.delivery?.Dispose();
                ReleasePreparedResources(r);
                r.reservedDeliveryBytes = 0;
                ClearRequestReferences(r);
                return;
            }

            // §12.4: stale terminates here with RequestStale; no retained Lease is created.
            if (r.stale)
            {
                r.delivery?.Dispose();
                ReleasePreparedResources(r);
                r.reservedDeliveryBytes = 0;
                ClearRequestReferences(r);
                if (r.handle != null && r.handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestStale", "A newer request with the same origin superseded this stale request at its fence."), null))
                    EnqueueNotification(r.handle);
                return;
            }

            // Success (§12.1 steps 2-4): only newly owned halls move into new Leases; borrowed ones stay.
            TextureSourceLease newSourceLease = null;
            if (r.retainSourceLease && r.ownedSourceHalls != null && r.ownedSourceHalls.Count > 0)
            {
                var bindings = new Dictionary<string, TextureSourceLease.Binding>(StringComparer.Ordinal);
                TextureRequestHallPlan.SourceEntry[] planSources = r.hallPlan.Sources;
                for (int i = 0; i < planSources.Length; i++)
                {
                    if (IsSourceCovered(r.requestedSourceLease, planSources[i])) continue;
                    bindings.Add(planSources[i].Name, new TextureSourceLease.Binding(planSources[i].Texture, r.sourceHalls[planSources[i].Name]));
                }
                newSourceLease = new TextureSourceLease(this, bindings);
                outstandingSourceLeases.Add(newSourceLease);
                r.ownedSourceHalls = null; // transferred: cleanup must not also release these halls
            }
            TextureOutputLease newOutputLease = null;
            if (r.retainOutputLease && r.ownedOutputHall.IsValid)
            {
                newOutputLease = new TextureOutputLease(this, r.ownedOutputHall);
                outstandingOutputLeases.Add(newOutputLease);
                r.ownedOutputHall = default; // transferred: cleanup must not also release this hall
            }
            TextureDelivery deliveryForResult = r.delivery;
            r.delivery = null;

            // Step 5: release the remaining owned halls and temporaries, release acquired uses, cut references.
            ReleasePreparedResources(r);
            ClearRequestReferences(r);

            // Step 6: hand the Result to the terminal state and enqueue the notification for the caller's flush.
            var result = new TextureExecutionResult(deliveryForResult, newSourceLease, newOutputLease);
            if (r.handle != null && r.handle.TrySetTerminalState(true, null, result))
                EnqueueNotification(r.handle);
            else
                result.Dispose();
        }
    }
}
