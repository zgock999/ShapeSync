// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Resolves the Animator that drives a ShapeSync runtime component from its own hierarchy.
    /// </summary>
    /// <remarks>
    /// Resolution searches only the supplied Transform and its ancestors. The first enabled Animator
    /// wins; when every candidate is disabled, the nearest Animator is returned with a diagnostic so
    /// authoring workflows remain usable without silently selecting a different hierarchy branch.
    /// </remarks>
    public static class ShapeSyncAnimatorResolver
    {
        /// <summary>
        /// Resolves the first enabled Animator on <paramref name="context"/> or an ancestor.
        /// </summary>
        /// <param name="context">The runtime component Transform that defines the upward search path.</param>
        /// <param name="animator">The resolved Animator when this method returns <see langword="true"/>.</param>
        /// <param name="diagnostic">A fallback diagnostic when all candidates are disabled, or a failure diagnostic when no candidate exists.</param>
        /// <returns><see langword="true"/> when an Animator was resolved; otherwise <see langword="false"/>.</returns>
        public static bool TryResolve(Transform context, out Animator animator, out StackMachineDiagnostic diagnostic)
        {
            animator = null;
            diagnostic = null;
            if (context == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain(
                    "humanoid",
                    "AnimatorSearchRootRequired",
                    "Animator resolution requires a runtime component Transform.");
                return false;
            }

            Animator[] candidates = context.GetComponentsInParent<Animator>(true);
            if (candidates == null || candidates.Length == 0)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain(
                    "humanoid",
                    "AnimatorRequired",
                    "ShapeSync runtime components require an Animator on their GameObject or an ancestor.",
                    detail: context.name);
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                Animator candidate = candidates[i];
                if (candidate == null || !candidate.enabled) continue;
                animator = candidate;
                return true;
            }

            animator = candidates[0];
            diagnostic = StackMachineDiagnostic.CreateDomain(
                "humanoid",
                "AnimatorAllDisabledFallback",
                "No enabled Animator was found; ShapeSync is using the nearest disabled Animator.",
                detail: animator == null ? context.name : animator.gameObject.name);
            return true;
        }

    }
}
