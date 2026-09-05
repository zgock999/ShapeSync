// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Promotes the Mesh Core skeleton escrow into the one resolved Pure Humanoid carrier.</summary>
    /// <remarks>
    /// The caller retains ownership of <paramref name="root"/> on both success and failure.  This
    /// helper only removes candidate-owned components and surplus renderers; callers destroy the
    /// root, Mesh, and Avatar when their enclosing transaction fails or is cancelled.
    /// </remarks>
    public static class HumanoidResolvedHumanoidCarrier
    {
        /// <summary>Begins candidate promotion and returns the caller-pumped completion barrier.</summary>
        /// <remarks>At runtime, <paramref name="root"/> must remain inactive until the returned operation succeeds.</remarks>
        public static bool TryBeginPromote(
            GameObject root,
            Mesh finalMesh,
            Avatar avatar,
            Transform[] resolvedBones,
            out HumanoidResolvedHumanoidCarrierOperation operation,
            out StackMachineDiagnostic diagnostic)
        {
            operation = null;
            diagnostic = null;
            if (root == null || finalMesh == null || resolvedBones == null)
                return Reject("EditModeResolvedHumanoidMissing", "EditMode Mesh result has no final resolved Humanoid hierarchy, bone table, or Mesh.", out diagnostic);
            if (Application.isPlaying && root.activeInHierarchy)
                return Reject("RuntimeCandidateMustBeInactive", "Runtime resolved Humanoid promotion requires an inactive candidate until deferred cleanup completes.", out diagnostic);

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
                return Reject("EditModeResolvedRendererMissing", "Resolved Humanoid hierarchy has no Figure SkinnedMeshRenderer.", out diagnostic);

            SkinnedMeshRenderer renderer = renderers[0];
            Transform rootBone = renderer.rootBone;
            if (rootBone == null)
                return Reject("EditModeResolvedRootBoneMissing", "Resolved Humanoid hierarchy has no Figure renderer rootBone.", out diagnostic);

            for (int i = 1; i < renderers.Length; i++) HumanoidMeshResourceCleanup.Destroy(renderers[i]);
            renderer.sharedMesh = finalMesh;
            renderer.bones = resolvedBones;
            renderer.rootBone = rootBone;

            if (avatar != null)
            {
                Animator animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    return Reject("EditModeResolvedAnimatorMissing", "Resolved Humanoid hierarchy has no Animator for its final Avatar.", out diagnostic);

                LocalTransformPose[] resolvedPose = CaptureLocalTransformPose(root);
                animator.avatar = avatar;
                RestoreLocalTransformPose(resolvedPose);
            }

            if (!HumanoidPureHumanoidNormalizer.TryNormalize(root, out diagnostic)) return false;
            operation = new HumanoidResolvedHumanoidCarrierOperation(root);
            return true;
        }

        /// <summary>Synchronously promotes an Editor candidate after immediate cleanup.</summary>
        /// <remarks>Runtime backends must call <see cref="TryBeginPromote"/> and pump its operation across frames.</remarks>
        public static bool TryPromote(
            GameObject root,
            Mesh finalMesh,
            Avatar avatar,
            Transform[] resolvedBones,
            out StackMachineDiagnostic diagnostic)
        {
            if (!TryBeginPromote(root, finalMesh, avatar, resolvedBones, out HumanoidResolvedHumanoidCarrierOperation operation, out diagnostic)) return false;
            HumanoidResolvedHumanoidCarrierStatus status = operation.Pump(out diagnostic);
            if (status == HumanoidResolvedHumanoidCarrierStatus.Succeeded) return true;
            return Reject("ResolvedHumanoidCarrierPumpRequired", "Runtime resolved Humanoid promotion requires explicit pumping until deferred cleanup completes.", out diagnostic);
        }

        // Animator.avatar assignment can restore the clone's source-avatar pose. Mesh Core has
        // already resolved FBM, BCP, Extra Bone, and bindpose pose, so preserve it exactly.
        private readonly struct LocalTransformPose
        {
            internal readonly Transform Transform;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal readonly Vector3 Scale;

            internal LocalTransformPose(Transform transform)
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

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }

    /// <summary>Explicit completion barrier for deferred Runtime candidate cleanup.</summary>
    public sealed class HumanoidResolvedHumanoidCarrierOperation
    {
        private readonly GameObject root;
        private bool completed;

        internal HumanoidResolvedHumanoidCarrierOperation(GameObject root) { this.root = root; }

        /// <summary>Returns succeeded only after the resolved candidate has exactly one renderer and no ShapeSync behaviour.</summary>
        public HumanoidResolvedHumanoidCarrierStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (completed) return HumanoidResolvedHumanoidCarrierStatus.Succeeded;
            if (root == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "ResolvedHumanoidCandidateDestroyed", "Resolved Humanoid candidate was destroyed before promotion cleanup completed.");
                return HumanoidResolvedHumanoidCarrierStatus.Failed;
            }
            if (Application.isPlaying && root.activeInHierarchy)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "RuntimeCandidateActivatedDuringCleanup", "Runtime resolved Humanoid candidate became active before deferred cleanup completed.");
                return HumanoidResolvedHumanoidCarrierStatus.Failed;
            }

            if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 1)
                return HumanoidResolvedHumanoidCarrierStatus.Pending;

            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (HumanoidPureHumanoidNormalizer.IsShapeSyncRuntimeBehaviour(behaviours[i]))
                    return HumanoidResolvedHumanoidCarrierStatus.Pending;
            }

            completed = true;
            return HumanoidResolvedHumanoidCarrierStatus.Succeeded;
        }
    }

    /// <summary>Lifecycle state of a resolved Humanoid candidate promotion.</summary>
    public enum HumanoidResolvedHumanoidCarrierStatus
    {
        Pending,
        Succeeded,
        Failed
    }

    /// <summary>Removes ShapeSync and registered optional-package components from an unpublished candidate.</summary>
    public static class HumanoidPureHumanoidNormalizer
    {
        private const string ShapeSyncNamespace = "zgock.ShapeSync";
        private static Action<GameObject> optionalCandidateNormalizers;

        /// <summary>Registers clone-only optional-package cleanup without coupling Core to that package.</summary>
        public static void RegisterOptionalCandidateNormalizer(Action<GameObject> normalizer)
        {
            if (normalizer == null) return;
            optionalCandidateNormalizers -= normalizer;
            optionalCandidateNormalizers += normalizer;
        }

        /// <summary>Normalizes an unpublished candidate. The source Figure is never mutated.</summary>
        public static bool TryNormalize(GameObject candidate, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (candidate == null)
                return Reject("PublishCandidateRequired", "Pure Humanoid normalization requires an unpublished candidate.", out diagnostic);
            try
            {
                MonoBehaviour[] behaviours = candidate.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null || !IsShapeSyncRuntimeBehaviour(behaviour)) continue;
                    HumanoidMeshResourceCleanup.Destroy(behaviour);
                }
                optionalCandidateNormalizers?.Invoke(candidate);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PureHumanoidCandidateNormalizeFailed", "Pure Humanoid candidate normalization failed.", detail: exception.Message);
                return false;
            }
        }

        /// <summary>Returns whether a behaviour belongs to the ShapeSync runtime namespace.</summary>
        public static bool IsShapeSyncRuntimeBehaviour(MonoBehaviour behaviour)
        {
            string componentNamespace = behaviour?.GetType().Namespace;
            return !string.IsNullOrEmpty(componentNamespace)
                && (string.Equals(componentNamespace, ShapeSyncNamespace, StringComparison.Ordinal)
                    || componentNamespace.StartsWith(ShapeSyncNamespace + ".", StringComparison.Ordinal));
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }
}
