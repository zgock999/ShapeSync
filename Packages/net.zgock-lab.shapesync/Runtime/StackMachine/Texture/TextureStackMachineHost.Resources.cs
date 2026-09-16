// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// The atomic real-reservation section for prepared requests and its rollback owner (Spec15-2 §7.4). Uses are
    /// acquired in the fixed order, every successful new hall reservation is registered into the owned fields
    /// immediately, and the lookup fields resolve only after all reservations completed. The rollback releases each
    /// owned hall exactly once, returns only acquired uses, and clears this request's owned/acquired/lookup state;
    /// it never Disposes a requested Lease and never touches pending budgets or queue positions.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Runs the atomic reservation section for one prepared request in the fixed §7.4 order: acquire the
        /// requested Source Lease use, acquire the requested Output Lease use, then reserve from the real allocator
        /// in the order new Sources (plan order), new Output, temporary slots, registering each success into the
        /// owned fields immediately, and finally resolve the sourceHalls/outputHall lookups. A failed use reports
        /// <c>SourceLeaseInvalid</c>/<c>OutputLeaseInvalid</c> and a failed real reservation reports
        /// <c>HallReservationInvariantBroken</c> (the live probe passed for the same demand moments earlier); both
        /// roll back everything acquired in this section and end as a terminal failure. Same thread, no callbacks.
        /// </summary>
        /// <param name="r">The prepared request whose uses, owned halls, and lookups are acquired.</param>
        /// <param name="diagnostic">Failure diagnostic on terminal failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the full demand was acquired; otherwise <see langword="false"/> after the full rollback.</returns>
        private bool TryAcquirePreparedResources(QueuedRequest r, out StackMachineDiagnostic diagnostic)
        {
            // 1. Requested Source Lease use; successes land in acquiredSourceLease.
            if (r.requestedSourceLease != null)
            {
                if (!r.requestedSourceLease.TryAcquire())
                {
                    ReleasePreparedResources(r);
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "SourceLeaseInvalid", "The atomic reservation section could not acquire a use from the requested source lease.");
                    return false;
                }
                r.acquiredSourceLease = r.requestedSourceLease;
            }

            // 2. Requested Output Lease use; successes land in acquiredOutputLease.
            if (r.requestedOutputLease != null)
            {
                if (!r.requestedOutputLease.TryAcquire(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight))
                {
                    ReleasePreparedResources(r);
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputLeaseInvalid", "The atomic reservation section could not acquire a use from the requested output lease.");
                    return false;
                }
                r.acquiredOutputLease = r.requestedOutputLease;
            }

            // 3. New halls from the real allocator: new Sources, then the new Output, then the temporary pool.
            int reservationOrdinal = 0;
            for (int i = 0; i < r.hallPlan.Sources.Length; i++)
            {
                TextureRequestHallPlan.SourceEntry entry = r.hallPlan.Sources[i];
                if (IsSourceCovered(r.requestedSourceLease, entry)) continue;
                if (!TryReserveNewHall(entry.Width, entry.Height, reservationOrdinal, out TextureHallAllocation allocation))
                {
                    ReleasePreparedResources(r);
                    diagnostic = HallReservationInvariantBroken("Source(" + entry.Name + ")");
                    return false;
                }
                reservationOrdinal++;
                if (r.ownedSourceHalls == null) r.ownedSourceHalls = new List<TextureHallAllocation>();
                r.ownedSourceHalls.Add(allocation);
            }

            if (r.requestedOutputLease == null)
            {
                if (!TryReserveNewHall(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, reservationOrdinal, out TextureHallAllocation allocation))
                {
                    ReleasePreparedResources(r);
                    diagnostic = HallReservationInvariantBroken("Output");
                    return false;
                }
                reservationOrdinal++;
                r.ownedOutputHall = allocation;
            }

            if (r.hallPlan.TemporarySlotCount > 0)
            {
                r.temporaryPool = new TextureHallAllocation[r.hallPlan.TemporarySlotCount];
                for (int slot = 0; slot < r.hallPlan.TemporarySlotCount; slot++)
                {
                    if (!TryReserveNewHall(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, reservationOrdinal, out TextureHallAllocation allocation))
                    {
                        ReleasePreparedResources(r);
                        diagnostic = HallReservationInvariantBroken("Temporary(" + slot + ")");
                        return false;
                    }
                    reservationOrdinal++;
                    r.temporaryPool[slot] = allocation;
                }
            }

            // 4. Lookups resolve only after every reservation completed: Sources order, then the Output.
            int ownedSourceIndex = 0;
            for (int i = 0; i < r.hallPlan.Sources.Length; i++)
            {
                TextureRequestHallPlan.SourceEntry entry = r.hallPlan.Sources[i];
                if (IsSourceCovered(r.requestedSourceLease, entry) && r.requestedSourceLease.TryResolve(entry.Name, entry.Texture, out TextureHallAllocation borrowed))
                {
                    r.sourceHalls[entry.Name] = borrowed;
                }
                else
                {
                    r.sourceHalls[entry.Name] = r.ownedSourceHalls[ownedSourceIndex++];
                }
            }
            r.outputHall = r.requestedOutputLease != null ? r.requestedOutputLease.Hall : r.ownedOutputHall;

            diagnostic = null;
            return true;
        }

        /// <summary>
        /// Rolls back one prepared request in the fixed §7.4 order: ownedSourceHalls, ownedOutputHall, temporaryPool,
        /// acquiredSourceLease.ReleaseUse, acquiredOutputLease.ReleaseUse, then every owned/acquired/lookup field is
        /// cleared. Null, empty, and default entries are skipped, each owned hall is released exactly once, a
        /// requested Lease is never Disposed, and pending budgets and queue positions are untouched. Fields already
        /// transferred to the new Leases are cleared before the call; once Destroy handed the whole grid to a ticket
        /// this rollback is not called and the §13.4 detach is used instead.
        /// </summary>
        /// <param name="r">The prepared request whose owned halls, acquired uses, and lookups are released and cleared.</param>
        private void ReleasePreparedResources(QueuedRequest r)
        {
            if (r.ownedSourceHalls != null)
            {
                for (int i = 0; i < r.ownedSourceHalls.Count; i++)
                {
                    if (r.ownedSourceHalls[i].IsValid) allocator.TryRelease(r.ownedSourceHalls[i]);
                }
            }

            if (r.ownedOutputHall.IsValid) allocator.TryRelease(r.ownedOutputHall);

            if (r.temporaryPool != null)
            {
                for (int i = 0; i < r.temporaryPool.Length; i++)
                {
                    if (r.temporaryPool[i].IsValid) allocator.TryRelease(r.temporaryPool[i]);
                }
            }

            if (r.acquiredSourceLease != null) r.acquiredSourceLease.ReleaseUse();
            if (r.acquiredOutputLease != null) r.acquiredOutputLease.ReleaseUse();

            r.ownedSourceHalls = null;
            r.ownedOutputHall = default;
            r.temporaryPool = Array.Empty<TextureHallAllocation>();
            r.acquiredSourceLease = null;
            r.acquiredOutputLease = null;
            r.sourceHalls.Clear();
            r.outputHall = default;
        }

        // Pure helper: one new reservation through the real allocator with the §13.5 test contact applied first
        // (a matching 0-based ordinal skips the real TryReserve and reports the invariant break).
        private bool TryReserveNewHall(int width, int height, int ordinal, out TextureHallAllocation allocation)
        {
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
            if (testFailReservationOrdinal >= 0 && ordinal == testFailReservationOrdinal)
            {
                allocation = default;
                return false;
            }
#endif
            return allocator.TryReserve(width, height, out allocation);
        }

        // Pure helper: terminal diagnostic for a real reservation that failed although the live probe passed the same demand earlier in the section (Spec15-2 §7.4).
        private static StackMachineDiagnostic HallReservationInvariantBroken(string stage)
            => StackMachineDiagnostic.CreateDomain("texture", "HallReservationInvariantBroken", "A real hall reservation failed after the live probe had passed for the same demand.", detail: "stage=" + stage);
    }
}
