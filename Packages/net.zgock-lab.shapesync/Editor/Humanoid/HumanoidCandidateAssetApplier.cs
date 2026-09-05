// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Applies persisted asset references to the 17.2-resolved Pure Humanoid before optional VRM transport.</summary>
    internal static class HumanoidCandidateAssetApplier
    {
        internal static bool TryApply(GameObject candidate, HumanoidIndividualAssetStage stage, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (candidate == null) return Reject("PublishCandidateRequired", "Candidate asset application requires an unpublished candidate.", out diagnostic);
            if (stage == null || stage.Mesh == null) return Reject("PublishStageRequired", "Candidate asset application requires staged Mesh assets.", out diagnostic);
            SkinnedMeshRenderer[] renderers = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1) return Reject("PublishCandidateRendererCountInvalid", "Pure Humanoid candidate requires exactly one Figure SkinnedMeshRenderer.", out diagnostic, detail: renderers.Length.ToString());
            if (stage.Mesh.subMeshCount != stage.Materials.Count) return Reject("PublishStageMaterialSlotMismatch", "Staged Material count must match the final Mesh submesh count.", out diagnostic);
            Material[] materials = new Material[stage.Materials.Count];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = stage.Materials[i];
                if (materials[i] == null) return Reject("PublishStageMaterialRequired", "Candidate asset application received a missing staged Material.", out diagnostic, detail: i.ToString());
            }
            Animator animator = null;
            if (stage.Avatar != null)
            {
                Animator[] animators = candidate.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1) return Reject("PublishCandidateAnimatorCountInvalid", "Staged Avatar requires exactly one candidate Animator.", out diagnostic, detail: animators.Length.ToString());
                animator = animators[0];
            }
            renderers[0].sharedMesh = stage.Mesh;
            renderers[0].sharedMaterials = materials;
            if (animator != null)
            {
                LocalTransformPose[] resolvedPose = CaptureLocalTransformPose(candidate);
                animator.avatar = stage.Avatar;
                RestoreLocalTransformPose(resolvedPose);
            }
            return true;
        }

        // Assigning an Avatar can make Unity restore the clone's source-avatar pose in EditMode.
        // The 17.2 carrier is already the resolved final hierarchy; 17.6 must preserve it while
        // only replacing the persistent Avatar reference.
        private readonly struct LocalTransformPose
        {
            public readonly Transform Transform;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public LocalTransformPose(Transform transform)
            {
                Transform = transform;
                Position = transform.localPosition;
                Rotation = transform.localRotation;
                Scale = transform.localScale;
            }
        }

        private static LocalTransformPose[] CaptureLocalTransformPose(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var poses = new LocalTransformPose[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) poses[i] = new LocalTransformPose(transforms[i]);
            return poses;
        }

        private static void RestoreLocalTransformPose(LocalTransformPose[] poses)
        {
            if (poses == null) return;
            for (int i = 0; i < poses.Length; i++)
            {
                Transform transform = poses[i].Transform;
                if (transform == null) continue;
                transform.localPosition = poses[i].Position;
                transform.localRotation = poses[i].Rotation;
                transform.localScale = poses[i].Scale;
            }
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message, detail: detail);
            return false;
        }
    }
}
