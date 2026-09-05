// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Normalizes the unpublished output clone into a Pure Humanoid before publish work begins.</summary>
    public static class HumanoidPureHumanoidComponentStripper
    {
        /// <summary>Registers the optional-package cleanup performed as part of candidate normalization.</summary>
        /// <remarks>Used by the UniVRM Editor assembly so this assembly remains UniVRM-reference free.</remarks>
        /// <param name="normalizer">The clone-only cleanup callback; null is ignored.</param>
        public static void RegisterOptionalCandidateNormalizer(Action<GameObject> normalizer)
        {
            HumanoidPureHumanoidNormalizer.RegisterOptionalCandidateNormalizer(normalizer);
        }

        /// <summary>Removes ShapeSync and optional-package source-clone components from a candidate.</summary>
        /// <param name="candidate">The unpublished working clone to normalize. The source Figure is never mutated.</param>
        /// <param name="diagnostic">A structured diagnostic when normalization fails.</param>
        /// <returns>True when the candidate contains no runtime ShapeSync or registered optional-package components.</returns>
        public static bool TryNormalize(GameObject candidate, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (candidate == null) return Reject("PublishCandidateRequired", "Pure Humanoid normalization requires an unpublished candidate.", out diagnostic);
            try
            {
                RemoveMissingScripts(candidate);
                return HumanoidPureHumanoidNormalizer.TryNormalize(candidate, out diagnostic);
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PureHumanoidCandidateNormalizeFailed", "Pure Humanoid candidate normalization failed.", detail: exception.Message);
                return false;
            }
        }

        // Optional packages can be removed after a Figure was authored. Their serialized
        // components then survive on the working clone as missing-script entries, which
        // PrefabUtility refuses to save. This is Editor-only candidate normalization and
        // deliberately does not touch the source Figure.
        private static void RemoveMissingScripts(GameObject candidate)
        {
            Transform[] transforms = candidate.GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[transformIndex].gameObject);
            }
        }

        // Compatibility surface for existing focused tests. New production paths use TryNormalize.
        internal static bool TryStrip(GameObject candidate, out StackMachineDiagnostic diagnostic) => TryNormalize(candidate, out diagnostic);

        internal static bool IsShapeSyncRuntimeBehaviour(MonoBehaviour behaviour)
        {
            return HumanoidPureHumanoidNormalizer.IsShapeSyncRuntimeBehaviour(behaviour);
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }
}
