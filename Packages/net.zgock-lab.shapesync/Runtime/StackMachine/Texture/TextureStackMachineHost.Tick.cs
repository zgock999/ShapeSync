// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Globalization;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// The new Update processing order for the prepared path (Spec15-2 §10 as made concrete by
    /// Bootups/Phase06-12). Everything runs on the main thread; one Update call starts at most one GPU
    /// request and processes exactly one pending head with no retry loop. The fence poll runs through the
    /// §13.5 wrapper: pending keeps the request in place, a fence fault diagnostic ends through the §13.4
    /// teardown as a fault with the ticket Uncertain, and a passed fence ends through the §12 handoff. Input
    /// revalidation runs every tick before the allocator-version cache check; only a new waiting decision
    /// builds the §14.1 waiting detail. The entrypoint Update stays on the old path and does not call this
    /// method yet (the switch happens at 06-26).
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Runs the §10 order: destroying returns; a submitted request polls its fence (unpassed flushes and
        /// returns); the pending head revalidates its inputs every tick, skips the occupancy snapshot only when
        /// the allocator-version cache matches, waits in place with the §14.1 diagnostic when the live check
        /// fails, and otherwise runs the §7.4 reservation, moves the queue/map/budget entries to local, and
        /// performs the single §11 submit. A Prepare false is already rolled back, so only detach, budget
        /// clear, reference clear, and terminalization run. An unsubmitted submit false disposes this request's
        /// delivery, releases its resources, zeroes the reservation, clears references, and terminalizes; a
        /// false that entered the fault teardown owns r already and gets no cleanup here. A successful submit
        /// fills the submitted slot and marks the handle Submitted. Notifications flush once at the end.
        /// </summary>
        private void UpdatePrepared()
        {
            if (destroying) return;

            if (submitted != null)
            {
                if (!TryPollPreparedFence(submitted, out bool fencePassed, out StackMachineDiagnostic fenceDiagnostic))
                {
                    FlushPreparedNotifications();
                    return;
                }
                if (fenceDiagnostic != null)
                {
                    TeardownPrepared(submitted, fenceDiagnostic, true, true);
                    return;
                }
                if (fencePassed) FinishPassedRequest(submitted);
            }

            if (acceptingRequests && pending.Count > 0)
            {
                QueuedRequest r = pending.Peek();

                // Input revalidation runs every tick, before the allocator-version cache decision.
                if (!TryValidateRequestInputs(r, out StackMachineDiagnostic inputDiagnostic))
                {
                    DetachPending(r);
                    r.reservedDeliveryBytes = 0;
                    ClearRequestReferences(r);
                    if (r.handle != null && r.handle.TrySetTerminalState(false, inputDiagnostic, null))
                        EnqueueNotification(r.handle);
                    FlushPreparedNotifications();
                    return;
                }

                if (r.hasWaitingAllocatorVersion && r.waitingAllocatorVersion == allocator.Version)
                {
                    // The occupancy state is unchanged since this request started waiting; it stays at the
                    // queue head and the waiting detail is not rebuilt for the same version.
                    FlushPreparedNotifications();
                    return;
                }

                if (!TryProbeLiveLayout(r, out string blockedStage, out _, out _))
                {
                    // A new waiting decision: cache the version and build the §14.1 detail in this order.
                    r.waitingAllocatorVersion = allocator.Version;
                    r.hasWaitingAllocatorVersion = true;
                    if (r.handle != null) r.handle.SetWaiting(BuildWaitingDiagnostic(r, blockedStage));
                    FlushPreparedNotifications();
                    return;
                }

                if (!TryAcquirePreparedResources(r, out StackMachineDiagnostic prepareDiagnostic))
                {
                    // §7.4 rolled everything back already: detach, budget clear, reference clear, terminal only.
                    DetachPending(r);
                    r.reservedDeliveryBytes = 0;
                    ClearRequestReferences(r);
                    if (r.handle != null && r.handle.TrySetTerminalState(false, prepareDiagnostic, null))
                        EnqueueNotification(r.handle);
                    FlushPreparedNotifications();
                    return;
                }

                // Prepare succeeded: dequeue, drop the map entry, and move the pending budget to local.
                pending.Dequeue();
                if (r.origin.IsValid) pendingByOrigin.Remove(r.origin);
                pendingDeliveryBytes -= r.reservedDeliveryBytes;

                if (!TrySubmitPrepared(r, out StackMachineDiagnostic submitDiagnostic))
                {
                    if (faulted)
                    {
                        // Ownership already moved into the §13.4 teardown inside the submit; r gets no cleanup.
                        FlushPreparedNotifications();
                        return;
                    }
                    r.delivery?.Dispose();
                    r.delivery = null;
                    ReleasePreparedResources(r);
                    r.reservedDeliveryBytes = 0;
                    ClearRequestReferences(r);
                    if (r.handle != null && r.handle.TrySetTerminalState(false, submitDiagnostic, null))
                        EnqueueNotification(r.handle);
                    FlushPreparedNotifications();
                    return;
                }

                submitted = r;
                if (r.handle != null) r.handle.MarkSubmitted();
            }

            FlushPreparedNotifications();
        }

        /// <summary>
        /// Fence poll wrapper for the submitted prepared request. The §13.5 test contact reports pending first
        /// and never fakes a Passed. Both injected seam exceptions and real fence-poll exceptions run inside the
        /// same try and are caught by the common catch, which returns the <c>GpuFencePollFailed</c> diagnostic
        /// with the decision flag set; the Update side then runs the §13.4 teardown as a fault with the ticket
        /// Uncertain. A normal read returns the real fence state with no diagnostic. Return values are decisions
        /// only: pending returns false, a passed fence returns true, and a fault returns true with a diagnostic.
        /// </summary>
        /// <param name="r">The submitted request whose fence is polled.</param>
        /// <param name="passed">The real fence state when the poll reached the fence; otherwise <see langword="false"/>.</param>
        /// <param name="diagnostic">Terminal fence-fault diagnostic when the seam or the real poll threw; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the poll reached a decision (passed or fault); <see langword="false"/> when pending.</returns>
        private bool TryPollPreparedFence(QueuedRequest r, out bool passed, out StackMachineDiagnostic diagnostic)
        {
            passed = false;
            diagnostic = null;
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
            if (testFencePending) return false;
#endif
            try
            {
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                if (testFailurePoint == TestFailurePoint.FencePoll)
                {
                    testFailurePoint = TestFailurePoint.None;
                    throw new InvalidOperationException("Spec15-2 §13.5 test seam: FencePoll.");
                }
#endif
                passed = r.fence.passed;
                return passed;
            }
            catch (Exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuFencePollFailed", "The fence poll threw on submitted Texture work; the host entered the fault state and hands GPU-referenced resources to retirement.");
                return true;
            }
        }

        // Builds the waiting diagnostic for a new live-placement decision; the detail lists origin, output,
        // grid, occupiedRooms, pendingCount, hasSubmitted, retainedSourceLeases, retainedOutputLeases,
        // temporarySlots, blockedStage, and allocatorVersion in this order (§14.1). Waiting itself never logs.
        private StackMachineDiagnostic BuildWaitingDiagnostic(QueuedRequest r, string blockedStage)
        {
            string detail = "origin=" + r.origin.Value.ToString(CultureInfo.InvariantCulture)
                + ";output=" + r.hallPlan.OutputWidth.ToString(CultureInfo.InvariantCulture) + "x" + r.hallPlan.OutputHeight.ToString(CultureInfo.InvariantCulture)
                + ";grid=" + allocator.GridEdge.ToString(CultureInfo.InvariantCulture)
                + ";occupiedRooms=" + allocator.OccupiedRoomCount.ToString(CultureInfo.InvariantCulture)
                + ";pendingCount=" + pending.Count.ToString(CultureInfo.InvariantCulture)
                + ";hasSubmitted=" + (submitted != null ? "true" : "false")
                + ";retainedSourceLeases=" + outstandingSourceLeases.Count.ToString(CultureInfo.InvariantCulture)
                + ";retainedOutputLeases=" + outstandingOutputLeases.Count.ToString(CultureInfo.InvariantCulture)
                + ";temporarySlots=" + r.hallPlan.TemporarySlotCount.ToString(CultureInfo.InvariantCulture)
                + ";blockedStage=" + blockedStage
                + ";allocatorVersion=" + allocator.Version.ToString(CultureInfo.InvariantCulture);
            return StackMachineDiagnostic.CreateDomain("texture", "WaitingForHalls", "The prepared request cannot fit the live grid right now and keeps its queue position.", detail: detail);
        }
    }
}
