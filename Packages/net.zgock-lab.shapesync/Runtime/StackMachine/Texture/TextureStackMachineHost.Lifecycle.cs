// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// Prepared-path initialization and the Disable/Enable lifecycle (Spec15-2 §8.4, §12.5, §13.1, §13.3). The
    /// entry checks run in the fixed §8.4 order; the uninitialized path copies the existing kernel/probe/grid
    /// generation and adds the §13.3 standby pump, setting initialized/acceptingRequests only at the very end.
    /// Disable detaches every pending request into a local batch first, keeps the real GPU resources, notifies
    /// Destroying once per cycle, and ends through the prepared flush. Enable defers to the flush's resume rule
    /// while the ending batch is closing. The existing TryInitialize/OnDisable entrypoints stay on the old path
    /// until 06-26.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Prepared initialization. Entry checks run in §8.4 order: <c>HostFaulted</c>, <c>HostDestroyed</c>,
        /// <c>HostDisabled</c> (closing or inactive), then an already-initialized host re-opens acceptance and
        /// never duplicates the standby. The uninitialized path copies the existing kernel/probe/grid generation,
        /// creates the single §13.3 standby pump (hidden GameObject, runtime-only DontDestroyOnLoad, component
        /// disabled), and only then sets initialized/acceptingRequests. A generation exception discards only what
        /// this call created and returns <c>HostInitializationFailed</c>. No re-entrant callback runs on this path.
        /// </summary>
        /// <param name="diagnostic">Rejection diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the fixed grid and its standby are ready.</returns>
        private bool TryInitializePrepared(out StackMachineDiagnostic diagnostic)
        {
            if (faulted) { diagnostic = Reject("HostFaulted"); return false; }
            if (destroying) { diagnostic = Reject("HostDestroyed"); return false; }
            if (closingNotifications || !isActiveAndEnabled) { diagnostic = Reject("HostDisabled"); return false; }
            if (initialized) { acceptingRequests = true; diagnostic = null; return true; }

            if (computeProgram == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "ComputeProgramRequired", "TextureStackMachineHost requires an explicitly assigned ComputeShader.");
                return false;
            }
            if (!TryCacheKernels(out diagnostic)) return false;

            GameObject standbyRoot = null;
            try
            {
                if (!TextureGpuCapabilityProbe.TryProbe(out capability, out diagnostic)) return false;
                var descriptor = new RenderTextureDescriptor(capability.FixedGridEdge, capability.FixedGridEdge, GraphicsFormat.R16G16B16A16_SFloat, 0)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                grid = new RenderTexture(descriptor) { name = "ShapeSync.TextureStackMachine.Grid" };
                if (!grid.Create())
                {
                    Destroy(grid);
                    grid = null;
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GridCreateFailed", "Failed to create the fixed Texture StackMachine grid.");
                    return false;
                }
                allocator = new TextureHallAllocator(capability.FixedGridEdge);
                TextureGpuRetirementFallback.EnsureInstalled();
                // §13.3: exactly one standby pump is created with a successful prepared initialization.
                // An already generated standby is never duplicated.
                standbyRoot = new GameObject("ShapeSync.TextureGpuRetirementPump") { hideFlags = HideFlags.HideInHierarchy };
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(standbyRoot);
                TextureGpuRetirementPump standby = standbyRoot.AddComponent<TextureGpuRetirementPump>();
                standby.enabled = false;
                retirementPump = standby;
            }
            catch (Exception exception)
            {
                // A generation exception frees only what this call created and reports HostInitializationFailed.
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "HostInitializationFailed", "Texture StackMachine prepared initialization failed while generating the grid or standby.", detail: exception.GetType().Name);
                if (standbyRoot != null) DestroyCreated(standbyRoot);
                retirementPump = null;
                if (grid != null) DestroyCreated(grid);
                grid = null;
                allocator = null;
                return false;
            }

            initialized = true;
            acceptingRequests = true;
            diagnostic = null;
            return true;
        }

        /// <summary>
        /// Prepared Disable (§13.1). Acceptance stops first and the closing flag is set, then every pending
        /// request is detached into a local batch and the queue, map, and pending budget are cleared before the
        /// batch is terminated request by request with <c>HostDisabled</c> (enqueue only on a successful terminal
        /// finalization; CancelPrepared is not used because it flushes mid-batch). The submitted request is marked
        /// cancelled and terminated with <c>HostDisabled</c> while its real GPU resources are kept for its fence.
        /// Destroying is notified exactly once per cycle with per-subscriber exception isolation, and the closing
        /// batch ends through the prepared flush.
        /// </summary>
        private void DisablePrepared()
        {
            acceptingRequests = false;
            closingNotifications = true;

            var detached = new List<QueuedRequest>();
            while (pending.Count > 0) detached.Add(pending.Dequeue());
            pendingByOrigin.Clear();
            pendingDeliveryBytes = 0;
            foreach (QueuedRequest r in detached)
            {
                r.reservedDeliveryBytes = 0;
                ClearRequestReferences(r);
                if (r.handle != null && r.handle.TrySetTerminalState(false, Reject("HostDisabled"), null))
                    EnqueueNotification(r.handle);
            }

            if (submitted != null)
            {
                submitted.cancelled = true;
                if (submitted.handle != null && submitted.handle.TrySetTerminalState(false, Reject("HostDisabled"), null))
                    EnqueueNotification(submitted.handle);
            }

            RaiseDestroyingOnce();
            FlushPreparedNotifications();
        }

        /// <summary>
        /// Prepared Enable (§12.5/§13.1). While the ending batch is closing this only raises
        /// <c>resumeRequested</c> — no acceptance resume or fence cleanup happens here; the flush's finally
        /// decides the resume once the batch is drained. Otherwise a destroying or faulted host stays stopped and
        /// any other host resets the notification flag for the next disable cycle and reopens acceptance to
        /// <c>initialized</c>.
        /// </summary>
        private void EnablePrepared()
        {
            if (closingNotifications) { resumeRequested = true; return; }
            if (destroying || faulted) return;
            lifecycleEndingNotified = false;
            acceptingRequests = initialized;
        }

        // Notifies Destroying at most once per disable cycle; the flag is set before the event so a re-entrant
        // lifecycle callback cannot fire it twice, and each subscriber runs under its own exception guard (§13.1).
        private void RaiseDestroyingOnce()
        {
            if (lifecycleEndingNotified) return;
            lifecycleEndingNotified = true;
            Action handler = Destroying;
            if (handler == null) return;
            foreach (Action subscriber in handler.GetInvocationList())
            {
                try { subscriber(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        // Destroys an object created by this call: Destroy while playing, DestroyImmediate in the editor (§13.3).
        private static void DestroyCreated(UnityEngine.Object target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
