// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Standalone and live layout simulations for prepared requests (Spec15-2 §7.1–§7.3). The standalone check runs
    /// on an empty simulation holding only this request's borrowed halls; the live check runs on an occupancy snapshot
    /// that already contains borrowed halls. Neither mutates the real allocator; the version-cache decision belongs to
    /// the pipeline phase, not these functions.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Standalone feasibility check (Spec15-2 §7.2): places this request's borrowed halls at their fixed positions
        /// on an empty simulation (duplicate hall ids arrive once), then first-fits the non-borrowed Sources in
        /// <see cref="TextureRequestHallPlan.Sources"/> order, the non-borrowed Output, and the temporary pool slots.
        /// Overlapping fixed inputs reject as an internal contract violation; a first-fit miss rejects as capacity.
        /// Only the simulation changes.
        /// </summary>
        /// <param name="r">The prepared request whose borrowed halls and plan demand are simulated.</param>
        /// <param name="diagnostic">Rejection diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the request runs alone under the deterministic first-fit rules; otherwise <see langword="false"/>.</returns>
        private bool TryValidateStandaloneLayout(QueuedRequest r, out StackMachineDiagnostic diagnostic)
        {
            TextureHallAllocator simulation = new TextureHallAllocator(allocator.GridEdge);
            HashSet<int> placedIds = new HashSet<int>();

            if (r.requestedSourceLease != null)
            {
                List<TextureHallAllocation> halls = new List<TextureHallAllocation>();
                r.requestedSourceLease.CopyHallsTo(halls);
                for (int i = 0; i < halls.Count; i++)
                {
                    if (!placedIds.Add(halls[i].Id)) continue;
                    if (!simulation.TryOccupyFixed(halls[i]))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HallReservationInvariantBroken", "Borrowed halls overlap or are malformed during standalone layout validation.", detail: "id=" + halls[i].Id);
                        return false;
                    }
                }
            }

            if (r.requestedOutputLease != null)
            {
                TextureHallAllocation hall = r.requestedOutputLease.Hall;
                if (placedIds.Add(hall.Id) && !simulation.TryOccupyFixed(hall))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HallReservationInvariantBroken", "Borrowed halls overlap or are malformed during standalone layout validation.", detail: "id=" + hall.Id);
                    return false;
                }
            }

            for (int i = 0; i < r.hallPlan.Sources.Length; i++)
            {
                TextureRequestHallPlan.SourceEntry entry = r.hallPlan.Sources[i];
                if (IsSourceCovered(r.requestedSourceLease, entry)) continue;
                if (!simulation.TryReserve(entry.Width, entry.Height, out _))
                {
                    diagnostic = CapacityExceeded("Source(" + entry.Name + ")", entry.Width, entry.Height, r.hallPlan.TemporarySlotCount, placedIds.Count);
                    return false;
                }
            }

            if (r.requestedOutputLease == null && !simulation.TryReserve(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, out _))
            {
                diagnostic = CapacityExceeded("Output", r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, r.hallPlan.TemporarySlotCount, placedIds.Count);
                return false;
            }

            for (int slot = 0; slot < r.hallPlan.TemporarySlotCount; slot++)
            {
                if (!simulation.TryReserve(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, out _))
                {
                    diagnostic = CapacityExceeded("Temporary(" + slot + ")", r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, r.hallPlan.TemporarySlotCount, placedIds.Count);
                    return false;
                }
            }

            diagnostic = null;
            return true;
        }

        /// <summary>
        /// Live placement check (Spec15-2 §7.3 steps 3–5): reserves the new Sources, the new Output, and the temporary
        /// pool slots on an occupancy snapshot, in that order. Borrowed halls are already inside the snapshot and are not
        /// re-added. The allocator-version cache decision is the pipeline's; this function always probes when called.
        /// Only the snapshot changes.
        /// </summary>
        /// <param name="r">The prepared request whose new demand is simulated on the snapshot.</param>
        /// <param name="blockedStage">The first stage that failed, as Source(name)/Output/Temporary(slot); otherwise <see langword="null"/>.</param>
        /// <param name="width">The failed stage's required width; otherwise 0.</param>
        /// <param name="height">The failed stage's required height; otherwise 0.</param>
        /// <returns><see langword="true"/> when the full demand fits the live snapshot; otherwise <see langword="false"/>.</returns>
        private bool TryProbeLiveLayout(QueuedRequest r, out string blockedStage, out int width, out int height)
        {
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
            testLiveProbeCount++;
#endif
            TextureHallAllocator simulation = allocator.CreateOccupancySnapshot();

            for (int i = 0; i < r.hallPlan.Sources.Length; i++)
            {
                TextureRequestHallPlan.SourceEntry entry = r.hallPlan.Sources[i];
                if (IsSourceCovered(r.requestedSourceLease, entry)) continue;
                if (!simulation.TryReserve(entry.Width, entry.Height, out _))
                {
                    blockedStage = "Source(" + entry.Name + ")";
                    width = entry.Width;
                    height = entry.Height;
                    return false;
                }
            }

            if (r.requestedOutputLease == null && !simulation.TryReserve(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, out _))
            {
                blockedStage = "Output";
                width = r.hallPlan.OutputWidth;
                height = r.hallPlan.OutputHeight;
                return false;
            }

            for (int slot = 0; slot < r.hallPlan.TemporarySlotCount; slot++)
            {
                if (!simulation.TryReserve(r.hallPlan.OutputWidth, r.hallPlan.OutputHeight, out _))
                {
                    blockedStage = "Temporary(" + slot + ")";
                    width = r.hallPlan.OutputWidth;
                    height = r.hallPlan.OutputHeight;
                    return false;
                }
            }

            blockedStage = null;
            width = 0;
            height = 0;
            return true;
        }

        // Pure helper: a plan Source is borrowed when the requested source lease still resolves its logical name to its Texture.
        private static bool IsSourceCovered(TextureSourceLease lease, TextureRequestHallPlan.SourceEntry entry)
            => lease != null && lease.TryResolve(entry.Name, entry.Texture, out _);

        // Pure helper: synchronous acceptance rejection with the missing stage, required extents, temporary K, and borrowed count (Spec15-2 §7.2 step 4).
        private static StackMachineDiagnostic CapacityExceeded(string stage, int width, int height, int temporaryK, int borrowedCount)
            => StackMachineDiagnostic.CreateDomain("texture", "RequestHallCapacityExceeded", "The request cannot run alone under the deterministic first-fit rules and current borrowed fixed positions.", detail: "stage=" + stage + ";required=" + width + "x" + height + ";temporaryK=" + temporaryK + ";borrowed=" + borrowedCount);
    }
}
