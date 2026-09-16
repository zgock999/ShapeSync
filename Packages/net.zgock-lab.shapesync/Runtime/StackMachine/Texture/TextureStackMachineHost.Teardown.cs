// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Prepared-path Destroy/fault teardown with retirement handoff (Spec15-2 §13.2 as made concrete by
    /// Bootups/Phase06-9, §13.3, §13.4). The 13 steps run in the fixed order: lifecycle flags, pending detach,
    /// inFlight selection, terminal finalization, one guarded fence poll feeding the single ticket when work is
    /// unpassed or uncertain, ownership-set detaches, grid/allocator handoff, standby Accept, inFlight reference
    /// clears, delivery disposals, the once-per-cycle Destroying notification followed by the unreturned-Lease
    /// warning count and logical lease invalidation, and the final prepared flush. The old OnDestroy's direct
    /// physical releases are not copied here, the ticket receives raw resources and values only (no Host delegate),
    /// and the existing TryInitialize/OnEnable/OnDisable/OnDestroy entrypoints stay on the old path until 06-26.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Runs the §13.4 teardown for Destroy or a post-submission fault. asFault sets faulted, otherwise
        /// destroying; acceptance stops and closingNotifications is set. Pending moves to a local batch and the
        /// queue/map/budget clear before finalization. inFlight is the argument candidate or the submitted slot,
        /// marked cancelled and finalized with the given reason (already-finalized handles keep their reason).
        /// knownUncertain never re-reads the fence; otherwise the fence is polled exactly once (exception means
        /// Uncertain). Unpassed or Uncertain inFlight work is handed to the standby pump through one ticket (raw
        /// delivery detached, grid and hallPlan-order source Texture references, no Host callbacks); when no
        /// ticket is needed the grid is released here and the standby destroyed. Caller-owned sets detach to
        /// locals, the grid/allocator leave the Host, Destroying fires once, unreturned Leases warn once, every
        /// Lease is then logically invalidated, pending references clear, and the prepared flush drains.
        /// </summary>
        /// <param name="inFlight">The candidate already past reservation when it is not yet in submitted; otherwise <see langword="null"/>.</param>
        /// <param name="reason">Termination reason applied to every not-yet-finalized handle.</param>
        /// <param name="asFault">When <see langword="true"/>, the Host enters the faulted state instead of destroying.</param>
        /// <param name="knownUncertain">When <see langword="true"/>, the fence is never re-read and the ticket is Uncertain.</param>
        private void TeardownPrepared(QueuedRequest inFlight, StackMachineDiagnostic reason, bool asFault, bool knownUncertain)
        {
            // 1. Lifecycle flags first.
            if (asFault) faulted = true;
            else destroying = true;
            acceptingRequests = false;
            closingNotifications = true;

            // 2. Detach every pending request into the local batch, then clear queue, map, and budget.
            var pendingLocal = new List<QueuedRequest>();
            while (pending.Count > 0) pendingLocal.Add(pending.Dequeue());
            pendingByOrigin.Clear();
            pendingDeliveryBytes = 0;

            // 3. inFlight is the argument candidate or the submitted slot; the Host slot empties.
            if (inFlight == null) inFlight = submitted;
            submitted = null;

            // 4. Finalize incomplete handles with the teardown reason; already-finalized handles keep their reason.
            foreach (QueuedRequest r in pendingLocal)
            {
                if (r.handle != null && r.handle.TrySetTerminalState(false, reason, null))
                    EnqueueNotification(r.handle);
            }
            if (inFlight != null)
            {
                inFlight.cancelled = true;
                if (inFlight.handle != null && inFlight.handle.TrySetTerminalState(false, reason, null))
                    EnqueueNotification(inFlight.handle);
            }

            // 5. knownUncertain never reads the fence; otherwise poll it exactly once (exception -> Uncertain).
            //    An unpassed or Uncertain inFlight hands its raw resources to one ticket.
            TextureGpuRetirementTicket ticket = null;
            RenderTexture rawGrid = null;
            if (inFlight != null)
            {
                bool holdPending = false;
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                holdPending = testFencePending;
#endif
                bool uncertain = knownUncertain;
                bool passed = false;
                if (!uncertain && !holdPending)
                {
                    try { passed = inFlight.fence.passed; }
                    catch (Exception) { uncertain = true; }
                }
                if (!passed)
                {
                    RenderTexture rawDelivery = inFlight.delivery?.DetachForRetirement();
                    rawGrid = grid;
                    Texture[] sources = Array.Empty<Texture>();
                    if (inFlight.hallPlan != null)
                    {
                        TextureRequestHallPlan.SourceEntry[] planSources = inFlight.hallPlan.Sources;
                        sources = new Texture[planSources.Length];
                        for (int i = 0; i < planSources.Length; i++) sources[i] = planSources[i].Texture;
                    }
                    ticket = new TextureGpuRetirementTicket(inFlight.fence, rawGrid, rawDelivery, sources, uncertain);
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                    ticket.testHoldPending = holdPending;
#endif
                }
            }

            // 6. Move the ownership sets into locals and clear the Host sets before any logical invalidation.
            var deliveriesLocal = new List<TextureDelivery>(outstandingDeliveries);
            deliveriesLocal.AddRange(handedOffDeliveries);
            outstandingDeliveries.Clear();
            handedOffDeliveries.Clear();
            var sourceLeasesLocal = new List<TextureSourceLease>(outstandingSourceLeases);
            var outputLeasesLocal = new List<TextureOutputLease>(outstandingOutputLeases);
            outstandingSourceLeases.Clear();
            outstandingOutputLeases.Clear();

            // 7. The grid and allocator leave the Host; initialized drops.
            RenderTexture gridLocal = grid;
            TextureHallAllocator allocatorLocal = allocator;
            grid = null;
            allocator = null;
            initialized = false;

            // 8. The Lease locals stay until step 12; a Dispose in the window only raises releaseRequested
            //    because the Host sets are already clear.

            // 9. A ticket goes to the standby pump, and the Host reference is cut. Without a ticket the
            //    grid is released here and the standby itself is destroyed.
            if (ticket != null)
            {
                // Unity lifetime check, then a strong fallback owner before dropping Host ownership.
                if (retirementPump == null || !retirementPump.Accept(ticket))
                    TextureGpuRetirementFallback.Retain(ticket);
                retirementPump = null;
            }
            else
            {
                if (gridLocal != null)
                {
                    gridLocal.Release();
                    DestroyCreated(gridLocal);
                }
                if (retirementPump != null)
                {
                    DestroyCreated(retirementPump.gameObject);
                    retirementPump = null;
                }
            }

            // 10. Clear inFlight references; ownership moved to the ticket/grid disposal, so no ReleasePreparedResources.
            if (inFlight != null)
            {
                inFlight.ownedSourceHalls = null;
                inFlight.ownedOutputHall = default;
                inFlight.acquiredSourceLease = null;
                inFlight.acquiredOutputLease = null;
                inFlight.requestedSourceLease = null;
                inFlight.requestedOutputLease = null;
                inFlight.sourceHalls.Clear();
                inFlight.temporaryPool = Array.Empty<TextureHallAllocation>();
                inFlight.delivery = null;
                inFlight.context = null;
                inFlight.hallPlan = null;
            }

            // 11. Dispose the local delivery array; wrappers already detached to the ticket are no-ops.
            foreach (TextureDelivery delivery in deliveriesLocal) delivery.Dispose();

            // 12. Destroying first (once per cycle), then the unreturned-Lease warning count, then logical
            //     invalidation without a physical hall release callback.
            RaiseDestroyingOnce();
            int unreturnedSources = 0;
            foreach (TextureSourceLease lease in sourceLeasesLocal) if (!lease.IsReleaseRequested) unreturnedSources++;
            int unreturnedOutputs = 0;
            foreach (TextureOutputLease lease in outputLeasesLocal) if (!lease.IsReleaseRequested) unreturnedOutputs++;
            if (unreturnedSources + unreturnedOutputs > 0)
            {
                StackMachineDiagnostic leak = StackMachineDiagnostic.CreateDomain("texture", "LeaseForcedReleaseOnHostDestroy", "TextureStackMachineHost destroyed caller-owned retained halls.", detail: "sourceLeases=" + unreturnedSources + "; outputLeases=" + unreturnedOutputs);
                Debug.LogWarning(leak.domainCode + ": " + leak.message + " detail=" + leak.detail, this);
            }
            foreach (TextureSourceLease lease in sourceLeasesLocal) lease.InvalidateFromHost();
            foreach (TextureOutputLease lease in outputLeasesLocal) lease.InvalidateFromHost();

            // 13. Clear the pending batch references and drain the closing notification batch.
            foreach (QueuedRequest r in pendingLocal) ClearRequestReferences(r);
            FlushPreparedNotifications();
        }

        /// <summary>
        /// Destroy wrapper: runs the §13.4 teardown with no inFlight candidate and the <c>HostDestroyed</c>
        /// reason. Not wired to OnDestroy yet (the switch happens at 06-26).
        /// </summary>
        private void DestroyPrepared()
        {
            TextureStaticMachineFactory.Invalidate(gameObject.scene);
            TeardownPrepared(null, StackMachineDiagnostic.CreateDomain("texture", "HostDestroyed", "TextureStackMachineHost was destroyed before its Texture requests could finish."), false, false);
        }
    }
}

