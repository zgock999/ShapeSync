// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Pending accounting and prepared-path cancellation (Spec15-2 §5.3, §9, §12.2, §12.3, §12.6). Queue
    /// reconstruction happens only inside these explicit operations. Cancel searches by handle reference identity,
    /// never by origin, and a requested Lease is never Disposed or given a ReleaseUse here; acquired uses must not
    /// exist yet for a pending request. The legacy entrypoints keep their old paths until 06-26.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Removes exactly the reference-matching request from <c>pending</c> by queue reconstruction, drops its
        /// <c>pendingByOrigin</c> entry, and subtracts its reserved amount from <c>pendingDeliveryBytes</c>
        /// (the amount itself stays on the request as its local reservation; no negative clamp). A request that is
        /// not in the queue returns <see langword="false"/> with zero changes.
        /// </summary>
        /// <param name="r">The request to detach from the pending queue.</param>
        /// <returns><see langword="true"/> when the request was found and detached; otherwise <see langword="false"/>.</returns>
        private bool DetachPending(QueuedRequest r)
        {
            bool found = false;
            foreach (QueuedRequest item in pending) if (ReferenceEquals(item, r)) { found = true; break; }
            if (!found) return false;

            var retained = new Queue<QueuedRequest>();
            while (pending.Count > 0)
            {
                QueuedRequest item = pending.Dequeue();
                if (!ReferenceEquals(item, r)) retained.Enqueue(item);
            }
            while (retained.Count > 0) pending.Enqueue(retained.Dequeue());
            if (r.origin.IsValid && pendingByOrigin.TryGetValue(r.origin, out QueuedRequest mapped)
                && ReferenceEquals(mapped, r))
                pendingByOrigin.Remove(r.origin);
            pendingDeliveryBytes -= r.reservedDeliveryBytes;
            return true;
        }

        /// <summary>
        /// Clears only this request's non-legacy references: plan, context, hall plan, both requested Leases, and
        /// the sourceHalls lookup. Owned and acquired entries are not released here — the caller must have released
        /// or transferred them before this call. handle, origin, stale, and cancelled stay for the terminal path.
        /// </summary>
        /// <param name="r">The request whose non-legacy references are cleared.</param>
        private void ClearRequestReferences(QueuedRequest r)
        {
            r.plan = null;
            r.context = null;
            r.hallPlan = null;
            r.requestedSourceLease = null;
            r.requestedOutputLease = null;
            r.sourceHalls.Clear();
        }

        /// <summary>
        /// Cancels by handle reference identity. A pending request is detached, its local reservation is cancelled,
        /// its non-legacy references are cleared, and the handle is terminalized with <c>RequestCancelled</c>
        /// through the deferred finalize + notification enqueue + prepared flush. A submitted request sets
        /// <c>cancelled = true</c>, terminalizes its handle with <c>RequestCancelled</c>, and flushes — the
        /// submitted request itself, its delivery, owned halls, and acquired uses live on until its fence. Any other
        /// handle returns without changes; the oldest terminal reason is never overwritten (§12.6).
        /// </summary>
        /// <param name="handle">The handle whose request is cancelled; matched by reference, never by origin.</param>
        private void CancelPrepared(TextureExecutionHandle handle)
        {
            if (handle == null) return;

            QueuedRequest pendingMatch = null;
            foreach (QueuedRequest item in pending) if (ReferenceEquals(item.handle, handle)) { pendingMatch = item; break; }
            if (pendingMatch != null)
            {
                DetachPending(pendingMatch);
                pendingMatch.reservedDeliveryBytes = 0;
                ClearRequestReferences(pendingMatch);
                if (handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "The unsubmitted Texture request was cancelled by its consumer."), null))
                    EnqueueNotification(handle);
                FlushPreparedNotifications();
                return;
            }

            if (submitted == null || !ReferenceEquals(submitted.handle, handle)) return;
            submitted.cancelled = true;
            if (handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "Submitted Texture GPU work was retained only until its fence passed after consumer cancellation."), null))
                EnqueueNotification(handle);
            FlushPreparedNotifications();
        }
    }
}