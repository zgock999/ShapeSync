// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Poll outcome for a retirement ticket (Spec15-2 §13.4).</summary>
    internal enum TextureGpuRetirementPollResult
    {
        /// <summary>The fence has not passed yet.</summary>
        Pending,
        /// <summary>The fence passed and the held raw resources may be released.</summary>
        Passed,
        /// <summary>Polling failed; the ticket stays quarantined for the session without further exceptions.</summary>
        Uncertain,
    }

    /// <summary>Pure-C# holder for the raw GPU resources a retired host's submitted work last touches (Spec15-2 §13.3/§13.4).</summary>
    /// <remarks>
    /// Holds managed references to the fence, grid, submitted delivery, and the command-referenced source textures only.
    /// It keeps no host, scene, GameObject, executor, or request-callback reference. A ticket that entered Uncertain is
    /// reported once and never re-polls into an exception; its resources stay quarantined for the session. Borrowed source
    /// textures only have their references dropped, never destroyed. Passed cleanup runs once through the disposed guard.
    /// </remarks>
    internal sealed class TextureGpuRetirementTicket
    {
        private readonly GraphicsFence fence;
        private RenderTexture grid;
        private RenderTexture delivery;
        private Texture[] sources;
        private bool uncertain;
        private bool uncertainErrorReported;
        private bool disposed;

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
        internal bool testHoldPending;
        internal bool testFailPollOnce;
        internal int testFenceReadCount;
#endif

        internal TextureGpuRetirementTicket(GraphicsFence fence, RenderTexture grid, RenderTexture delivery, Texture[] sources, bool uncertain)
        {
            this.fence = fence;
            this.grid = grid;
            this.delivery = delivery;
            this.sources = sources;
            this.uncertain = uncertain;
        }

        /// <summary>Checks the fence once. Never throws once the ticket has entered Uncertain (Spec15-2 §13.3).</summary>
        internal TextureGpuRetirementPollResult Poll()
        {
            if (uncertain)
            {
                ReportUncertainOnce();
                return TextureGpuRetirementPollResult.Uncertain;
            }
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
            if (testHoldPending) return TextureGpuRetirementPollResult.Pending;
#endif
            try
            {
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
                testFenceReadCount++;
                if (testFailPollOnce)
                {
                    testFailPollOnce = false;
                    throw new InvalidOperationException("Spec15-2 retirement poll test failure.");
                }
#endif
                return fence.passed ? TextureGpuRetirementPollResult.Passed : TextureGpuRetirementPollResult.Pending;
            }
            catch (Exception)
            {
                uncertain = true;
                ReportUncertainOnce();
                return TextureGpuRetirementPollResult.Uncertain;
            }
        }

        // Reports GpuRetirementUncertain once when the ticket is handed over with an unpollable fence (Spec15-2 §13.3/§14.1).
        internal void ReportUncertainOnce()
        {
            if (!uncertain || uncertainErrorReported) return;
            uncertainErrorReported = true;
            StackMachineDiagnostic diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuRetirementUncertain", "Retired Texture GPU work could not be polled safely; its resources stay quarantined for the session.");
            Debug.LogError(StackMachineDiagnostic.Format(diagnostic, "Retired Texture GPU work could not be polled safely."));
        }

        // Runs the passed cleanup once through the disposed guard and drops borrowed source references without destroying them.
        internal void RunPassedCleanupOnce()
        {
            if (disposed) return;
            disposed = true;
            ReleaseAndDestroy(grid);
            ReleaseAndDestroy(delivery);
            grid = null;
            delivery = null;
            sources = null;
        }

        private static void ReleaseAndDestroy(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            DestroyUtility(texture);
        }

        private static void DestroyUtility(UnityEngine.Object target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }

    /// <summary>
    /// Main-thread fallback owner for tickets whose pump was lost (CR15-2-002).
    /// PlayerLoop and editor callbacks have static targets, so scene destruction cannot remove the owner.
    /// Domain reload/process exit recovery and third-party replacement of the installed PlayerLoop are excluded.
    /// </summary>
    internal static class TextureGpuRetirementFallback
    {
        private static readonly List<TextureGpuRetirementTicket> tickets = new List<TextureGpuRetirementTicket>();

        // Also re-install after entering Play with domain reload disabled. Never clear pending tickets here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void EnsureInstalled()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] systems = loop.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            bool found = false;
            foreach (PlayerLoopSystem system in systems)
                if (system.type == typeof(TextureGpuRetirementFallback)) { found = true; break; }
            if (!found)
            {
                Array.Resize(ref systems, systems.Length + 1);
                systems[systems.Length - 1] = new PlayerLoopSystem
                {
                    type = typeof(TextureGpuRetirementFallback),
                    updateDelegate = Poll
                };
                loop.subSystemList = systems;
                PlayerLoop.SetPlayerLoop(loop);
            }
#if UNITY_EDITOR
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
#endif
        }

        // EnsureInstalled runs during initialization, never needs a GameObject during teardown.
        internal static void Retain(TextureGpuRetirementTicket value)
        {
            if (value == null || tickets.Contains(value)) return;
            tickets.Add(value);
            value.ReportUncertainOnce();
        }

        private static void Poll()
        {
            for (int i = tickets.Count - 1; i >= 0; i--)
            {
                TextureGpuRetirementTicket current = tickets[i];
                if (current.Poll() != TextureGpuRetirementPollResult.Passed) continue;
                current.RunPassedCleanupOnce();
                tickets.RemoveAt(i);
            }
        }
    }

    /// <summary>Standalone pump that releases only the raw GPU resources a destroyed host handed over (Spec15-2 §13.3).</summary>
    /// <remarks>
    /// A standby pump is disabled and holds no ticket, host, or scene reference and subscribes to nothing. Accepting a
    /// single ticket enables it; in the editor the same Poll also runs from a single EditorApplication.update subscription
    /// registered at Accept time and idempotently removed in OnDestroy. Pending does nothing; Uncertain keeps its ticket
    /// quarantined for the session; after Passed cleanup the pump destroys itself. OnDestroy transfers any remaining
    /// ticket to the static fallback before dropping its reference. It is not a scheduler and creates no
    /// new GameObject during OnDestroy.
    /// </remarks>
    [ExecuteAlways]
    internal sealed class TextureGpuRetirementPump : MonoBehaviour
    {
        private TextureGpuRetirementTicket ticket;
        private bool accepted;

        /// <summary>Accepts the single ticket, enables the pump, and registers the editor update subscription (Spec15-2 §13.4).</summary>
        internal bool Accept(TextureGpuRetirementTicket value)
        {
            if (this == null || accepted || value == null) return false;
            accepted = true;
            ticket = value;
            ticket.ReportUncertainOnce();
            enabled = true;
#if UNITY_EDITOR
            EditorApplication.update += Poll;
#endif
            return true;
        }

        private void Update() => Poll();

        private void Poll()
        {
            TextureGpuRetirementTicket current = ticket;
            if (current == null) return;
            if (current.Poll() != TextureGpuRetirementPollResult.Passed) return;
            current.RunPassedCleanupOnce();
            ticket = null;
            if (Application.isPlaying) Destroy(gameObject);
            else UnityEngine.Object.DestroyImmediate(gameObject);
        }

        private void OnDestroy()
        {
            TextureGpuRetirementFallback.Retain(ticket);
            ticket = null;
#if UNITY_EDITOR
            EditorApplication.update -= Poll;
#endif
        }
    }
}
