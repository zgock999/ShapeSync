// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using UniVRM10;
using UnityEngine;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// UniVRM companion helpers for resolving VRM10 instances without exposing UniVRM types to Core.
    /// </summary>
    public static class ShapeSyncVrmInstanceUtility
    {
        public static bool TryGetOrCreateFigureInstance(
            GameObject figureRoot,
            Animator animator,
            out Vrm10Instance instance,
            out string error)
        {
            instance = null;
            error = null;
            if (figureRoot == null || animator == null)
            {
                error = "Figure root and Animator are required.";
                return false;
            }

            Vrm10Instance[] instances = figureRoot.GetComponentsInChildren<Vrm10Instance>(true);
            Vrm10Instance valid = null;
            int validCount = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                Vrm10Instance candidate = instances[i];
                // A Spec19 Hybrid warm artifact is a generated Figure clone retained below
                // the live Figure. It is not a second VRM source role, even while inactive.
                if (IsHybridArtifactDescendant(figureRoot, candidate == null ? null : candidate.transform))
                {
                    continue;
                }
                if (candidate == null || candidate.Vrm == null || candidate.Humanoid == null)
                {
                    continue;
                }

                valid = candidate;
                validCount++;
            }

            if (validCount > 1)
            {
                error = "Figure contains multiple valid Vrm10Instance components.";
                return false;
            }

            if (valid != null)
            {
                instance = valid;
                return true;
            }

            if (!animator.isHuman || animator.GetBoneTransform(HumanBodyBones.Head) == null)
            {
                error = "Figure Animator must be a valid Humanoid with a Head bone to create a Vrm10Instance.";
                return false;
            }

            UniHumanoid.Humanoid humanoid = figureRoot.GetComponent<UniHumanoid.Humanoid>();
            if (humanoid == null)
            {
                humanoid = figureRoot.AddComponent<UniHumanoid.Humanoid>();
            }

            if (!humanoid.AssignBonesFromAnimator())
            {
                error = "Figure Humanoid mapping could not be assigned from Animator.";
                return false;
            }

            instance = figureRoot.GetComponent<Vrm10Instance>();
            if (instance == null)
            {
                instance = figureRoot.AddComponent<Vrm10Instance>();
            }

            instance.Vrm = CreatePersistentFigureVrmObject(figureRoot);
            return true;
        }

        private static bool IsHybridArtifactDescendant(GameObject figureRoot, Transform candidate)
        {
            if (figureRoot == null || candidate == null) return false;
            StackMachine.Humanoid.HybridHotBakeFigure[] hybrids = figureRoot.GetComponentsInChildren<StackMachine.Humanoid.HybridHotBakeFigure>(true);
            for (int i = 0; i < hybrids.Length; i++)
            {
                GameObject bakedRoot = hybrids[i] == null ? null : hybrids[i].BakedRoot;
                if (bakedRoot != null && candidate.IsChildOf(bakedRoot.transform)) return true;
            }
            return false;
        }

        private static VRM10Object CreatePersistentFigureVrmObject(GameObject figureRoot)
        {
            // The destination may be a scene instance of an existing Prefab.  Never
            // attach the generated VRM object to its source asset: the transporter
            // writes a separate output Prefab and persists this object there.
            return ScriptableObject.CreateInstance<VRM10Object>();
        }
    }
}
#endif
