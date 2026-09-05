// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    internal readonly struct HumanoidMeshTransformPose
    {
        private readonly Transform transform;
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Vector3 scale;

        public HumanoidMeshTransformPose(Transform transform)
        {
            this.transform = transform;
            position = transform == null ? default : transform.localPosition;
            rotation = transform == null ? default : transform.localRotation;
            scale = transform == null ? default : transform.localScale;
        }

        public void Restore()
        {
            if (transform == null) return;
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }
    }

    /// <summary>Compiler-owned local humanoid hierarchy used only for BCP, bindpose, and later Avatar finalization.</summary>
    public sealed class HumanoidMeshSkeletonEscrow : IDisposable
    {
        private readonly TransformPose[] initialPose;
        private readonly HumanoidMeshTransformPose[] resolvedHumanoidPose;
        private readonly HumanoidMeshAnimatorSnapshot animatorSnapshot;
        private bool disposed;

        /// <summary>Creates an escrow that owns an unpublished humanoid hierarchy and rebuilt Avatar.</summary>
        /// <param name="root">The private candidate hierarchy owned by this escrow.</param>
        /// <param name="animator">The Animator on <paramref name="root"/>.</param>
        /// <param name="avatar">The rebuilt Avatar to dispose unless it is transferred.</param>
        public HumanoidMeshSkeletonEscrow(GameObject root, Animator animator, Avatar avatar)
            : this(root, animator, avatar, default)
        {
        }

        internal HumanoidMeshSkeletonEscrow(GameObject root, Animator animator, Avatar avatar, HumanoidMeshAnimatorSnapshot animatorSnapshot)
            : this(root, animator, avatar, animatorSnapshot, null)
        {
        }

        internal HumanoidMeshSkeletonEscrow(GameObject root, Animator animator, Avatar avatar, HumanoidMeshAnimatorSnapshot animatorSnapshot, HumanoidMeshTransformPose[] resolvedHumanoidPose)
        {
            Root = root;
            Animator = animator;
            Avatar = avatar;
            this.animatorSnapshot = animatorSnapshot;
            this.resolvedHumanoidPose = resolvedHumanoidPose ?? Array.Empty<HumanoidMeshTransformPose>();
            initialPose = CapturePose(root);
        }

        /// <summary>Gets the unpublished candidate hierarchy while this escrow owns it.</summary>
        public GameObject Root { get; private set; }
        /// <summary>Gets the Animator on the candidate hierarchy.</summary>
        public Animator Animator { get; }
        /// <summary>Gets the rebuilt Avatar while this escrow owns it.</summary>
        public Avatar Avatar { get; private set; }

        /// <summary>Transfers the rebuilt Avatar once to the upper compiler carrier without disposing it with this skeleton escrow.</summary>
        internal Avatar DetachAvatar()
        {
            Avatar value = Avatar;
            Avatar = null;
            return value;
        }

        /// <summary>Transfers the fully resolved hierarchy once to the build result owner.</summary>
        /// <remarks>The receiver owns the root and must destroy it on cancellation or failure.</remarks>
        public GameObject DetachRoot()
        {
            GameObject value = Root;
            Root = null;
            return value;
        }

        /// <summary>Assigns the rebuilt Avatar while retaining the candidate's authoring pose for skinning construction.</summary>
        internal bool TryAssignRebuiltAvatar(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (Animator == null || Avatar == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "AvatarAssignmentRequired", "Final Mesh skinning requires the rebuilt Animator and Avatar.");
                return false;
            }
            try
            {
                bool wasActive = Root != null && Root.activeSelf;
                if (Root != null && !wasActive) Root.SetActive(true);
                try
                {
                    AnimatorCullingMode originalCullingMode = Animator.cullingMode;
                    Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Animator.avatar = Avatar;
                    Animator.Rebind();
                    // Bone-table and extra-bone bindposes are constructed after this
                    // assignment.  Do not let the controller's sampled pose leak into
                    // those immutable mesh inputs; it is restored only after final
                    // mesh construction has completed.
                    RestoreInitialPose();
                    Animator.cullingMode = originalCullingMode;
                    return true;
                }
                finally
                {
                    if (Root != null && !wasActive) Root.SetActive(false);
                }
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "AvatarAssignmentFailed", "Final Mesh skinning could not assign the rebuilt Avatar.", detail: exception.Message);
                return false;
            }
        }

        /// <summary>Restores the controller sample after all immutable skinning inputs have been finalized.</summary>
        internal bool TryRestoreSampledAnimatorState(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (Animator == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "AnimatorRestoreRequired", "Final candidate activation requires its rebuilt Animator.");
                return false;
            }
            try
            {
                bool wasActive = Root != null && Root.activeSelf;
                if (Root != null && !wasActive) Root.SetActive(true);
                try
                {
                    AnimatorCullingMode originalCullingMode = Animator.cullingMode;
                    Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animatorSnapshot.Restore(Animator);
                    Animator.Update(0f);
                    Animator.cullingMode = originalCullingMode;
                    return true;
                }
                finally
                {
                    if (Root != null && !wasActive) Root.SetActive(false);
                }
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "AnimatorRestoreFailed", "Final candidate activation could not restore its sampled Animator state.", detail: exception.Message);
                return false;
            }
        }

        /// <summary>Restores the detached candidate's serialized initial pose after internal skinning work.</summary>
        internal void RestoreInitialPose()
        {
            if (initialPose == null) return;
            for (int i = 0; i < initialPose.Length; i++) initialPose[i].Restore();
        }

        /// <summary>Restores the resolved FBM/BCP Humanoid rest pose without sampling an Animator.</summary>
        internal void RestoreResolvedHumanoidPose()
        {
            if (resolvedHumanoidPose == null) return;
            for (int i = 0; i < resolvedHumanoidPose.Length; i++) resolvedHumanoidPose[i].Restore();
        }

        /// <summary>Normalizes the detached Pure Humanoid root without touching its resolved child skeleton.</summary>
        internal void ResetRootTransform()
        {
            if (Root == null) return;
            Transform transform = Root.transform;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        /// <summary>Destroys every resource still owned by this escrow.</summary>
        /// <remarks>Resources transferred by <see cref="DetachRoot"/> or the internal Avatar transfer path are not destroyed.</remarks>
        public void Dispose()
        {
            if (disposed) return;
            if (Avatar != null) HumanoidMeshResourceCleanup.Destroy(Avatar);
            Avatar = null;
            if (Root != null) HumanoidMeshResourceCleanup.Destroy(Root);
            Root = null;
            disposed = true;
        }

        private static TransformPose[] CapturePose(GameObject root)
        {
            if (root == null) return Array.Empty<TransformPose>();
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var poses = new TransformPose[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) poses[i] = new TransformPose(transforms[i]);
            return poses;
        }

        private readonly struct TransformPose
        {
            private readonly Transform transform;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;
            public TransformPose(Transform transform)
            {
                this.transform = transform;
                position = transform == null ? default : transform.localPosition;
                rotation = transform == null ? default : transform.localRotation;
                scale = transform == null ? default : transform.localScale;
            }
            public void Restore()
            {
                if (transform == null) return;
                transform.localPosition = position;
                transform.localRotation = rotation;
                transform.localScale = scale;
            }
        }
    }

    /// <summary>Minimal controller snapshot used to reproduce DDB's private Avatar-rebind settle.</summary>
    internal readonly struct HumanoidMeshAnimatorSnapshot
    {
        private readonly float speed;
        private readonly HumanoidMeshAnimatorLayer[] layers;
        private readonly HumanoidMeshAnimatorParameter[] parameters;
        private readonly bool captured;

        private HumanoidMeshAnimatorSnapshot(float speed, HumanoidMeshAnimatorLayer[] layers, HumanoidMeshAnimatorParameter[] parameters)
        {
            this.speed = speed;
            this.layers = layers ?? Array.Empty<HumanoidMeshAnimatorLayer>();
            this.parameters = parameters ?? Array.Empty<HumanoidMeshAnimatorParameter>();
            captured = true;
        }

        public static HumanoidMeshAnimatorSnapshot CaptureAfterZeroUpdate(GameObject root, Animator animator)
        {
            if (animator == null) return default;
            // A valid Humanoid Avatar does not require an AnimatorController. In
            // that case there is no layer or parameter state to preserve, and
            // Unity warns when controller-only APIs such as layerCount are read.
            if (animator.runtimeAnimatorController == null)
                return new HumanoidMeshAnimatorSnapshot(animator.speed, Array.Empty<HumanoidMeshAnimatorLayer>(), Array.Empty<HumanoidMeshAnimatorParameter>());
            bool wasActive = root != null && root.activeSelf;
            if (root != null && !wasActive) root.SetActive(true);
            try
            {
                AnimatorCullingMode originalCullingMode = animator.cullingMode;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                var layers = new HumanoidMeshAnimatorLayer[animator.layerCount];
                for (int i = 0; i < layers.Length; i++)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(i);
                    layers[i] = new HumanoidMeshAnimatorLayer(i, state.fullPathHash, state.normalizedTime, animator.GetLayerWeight(i));
                }
                AnimatorControllerParameter[] declared = animator.parameters;
                var parameters = new List<HumanoidMeshAnimatorParameter>(declared == null ? 0 : declared.Length);
                if (declared != null)
                {
                    for (int i = 0; i < declared.Length; i++)
                    {
                        AnimatorControllerParameter parameter = declared[i];
                        switch (parameter.type)
                        {
                            case AnimatorControllerParameterType.Float: parameters.Add(HumanoidMeshAnimatorParameter.Float(parameter.nameHash, animator.GetFloat(parameter.nameHash))); break;
                            case AnimatorControllerParameterType.Int: parameters.Add(HumanoidMeshAnimatorParameter.Int(parameter.nameHash, animator.GetInteger(parameter.nameHash))); break;
                            case AnimatorControllerParameterType.Bool: parameters.Add(HumanoidMeshAnimatorParameter.Bool(parameter.nameHash, animator.GetBool(parameter.nameHash))); break;
                        }
                    }
                }
                HumanoidMeshAnimatorSnapshot snapshot = new HumanoidMeshAnimatorSnapshot(animator.speed, layers, parameters.ToArray());
                animator.cullingMode = originalCullingMode;
                return snapshot;
            }
            finally
            {
                if (root != null && !wasActive) root.SetActive(false);
            }
        }

        public void Restore(Animator animator)
        {
            if (!captured || animator == null) return;
            animator.speed = speed;
            if (parameters != null) for (int i = 0; i < parameters.Length; i++) parameters[i].Restore(animator);
            if (layers == null) return;
            for (int i = 0; i < layers.Length; i++)
            {
                HumanoidMeshAnimatorLayer layer = layers[i];
                if (layer.Index < 0 || layer.Index >= animator.layerCount || layer.StateHash == 0) continue;
                animator.SetLayerWeight(layer.Index, layer.Weight);
                animator.Play(layer.StateHash, layer.Index, layer.NormalizedTime);
            }
        }
    }

    internal readonly struct HumanoidMeshAnimatorLayer
    {
        public HumanoidMeshAnimatorLayer(int index, int stateHash, float normalizedTime, float weight) { Index = index; StateHash = stateHash; NormalizedTime = normalizedTime; Weight = weight; }
        public int Index { get; }
        public int StateHash { get; }
        public float NormalizedTime { get; }
        public float Weight { get; }
    }

    internal readonly struct HumanoidMeshAnimatorParameter
    {
        private readonly int hash;
        private readonly AnimatorControllerParameterType type;
        private readonly float floatValue;
        private readonly int intValue;
        private readonly bool boolValue;
        private HumanoidMeshAnimatorParameter(int hash, AnimatorControllerParameterType type, float floatValue, int intValue, bool boolValue) { this.hash = hash; this.type = type; this.floatValue = floatValue; this.intValue = intValue; this.boolValue = boolValue; }
        public static HumanoidMeshAnimatorParameter Float(int hash, float value) => new HumanoidMeshAnimatorParameter(hash, AnimatorControllerParameterType.Float, value, default, default);
        public static HumanoidMeshAnimatorParameter Int(int hash, int value) => new HumanoidMeshAnimatorParameter(hash, AnimatorControllerParameterType.Int, default, value, default);
        public static HumanoidMeshAnimatorParameter Bool(int hash, bool value) => new HumanoidMeshAnimatorParameter(hash, AnimatorControllerParameterType.Bool, default, default, value);
        public void Restore(Animator animator)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Float: animator.SetFloat(hash, floatValue); break;
                case AnimatorControllerParameterType.Int: animator.SetInteger(hash, intValue); break;
                case AnimatorControllerParameterType.Bool: animator.SetBool(hash, boolValue); break;
            }
        }
    }

    /// <summary>Builds an unpublished local humanoid hierarchy without changing the Figure source hierarchy.</summary>
    public static class HumanoidMeshSkeletonBuilder
    {
        /// <summary>Builds a private humanoid hierarchy and rebuilt Avatar for an unpublished mesh candidate.</summary>
        /// <param name="bake">The resolved Figure FBM and BCP bake result.</param>
        /// <param name="escrow">The caller-owned hierarchy escrow when creation succeeds; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic">The structured reason creation could not complete.</param>
        /// <returns><see langword="true"/> when ownership was transferred to <paramref name="escrow"/>.</returns>
        public static bool TryCreate(HumanoidMeshFbmBakeResult bake, out HumanoidMeshSkeletonEscrow escrow, out StackMachineDiagnostic diagnostic)
        {
            escrow = null;
            diagnostic = null;
            if (bake == null || bake.LogicalPlan == null || bake.LogicalPlan.Figure.Root == null)
                return Fail("FigureSkeletonRequired", "EditMode Mesh skeleton build requires a Figure source root.", out diagnostic);

            GameObject clone = UnityEngine.Object.Instantiate(bake.LogicalPlan.Figure.Root);
            clone.name = bake.LogicalPlan.Figure.Root.name;
            SetDontSave(clone.transform);
            // A working clone may exist in the active Scene while the Editor build
            // runs, but it must never render as a second Figure. Prefab commit is
            // the sole phase that reactivates the resolved Humanoid for saving.
            clone.SetActive(false);
            Animator animator = clone.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                HumanoidMeshResourceCleanup.Destroy(clone);
                return Fail("HumanoidAnimatorRequired", "EditMode Mesh skeleton build requires a valid Humanoid Animator on the Figure root.", out diagnostic);
            }

            // Runtime DDB samples the controller once before its LateUpdate Avatar
            // rebuild, then restores that exact state after Rebind.  Sample only the
            // private clone so the source Figure remains read-only.
            HumanoidMeshTransformPose[] physicalHumanoidPose = CaptureHumanoidPose(animator);
            HumanoidMeshAnimatorSnapshot animatorSnapshot = HumanoidMeshAnimatorSnapshot.CaptureAfterZeroUpdate(clone, animator);

            // The controller sample is state data only.  It must never become the
            // HumanDescription rest skeleton: doing so makes the rebuilt Avatar
            // retarget from the controller's first-frame pose while the final
            // candidate retains the Figure's authoring pose.  Restore before FBM /
            // BCP and AvatarBuilder so controller-equipped Figure prefabs produce
            // the same rest skeleton as their controller-free build baseline.
            RestoreHumanoidPose(physicalHumanoidPose);

            if (!HumanoidMeshFigureFbmSkeletonResolver.TryApply(bake, clone, animator, out diagnostic))
            {
                HumanoidMeshResourceCleanup.Destroy(clone);
                return false;
            }

            var corrections = new List<ShapeSyncHumanoidBoneCorrection>(bake.BcpDeltas.Count);
            for (int i = 0; i < bake.BcpDeltas.Count; i++)
            {
                HumanoidMeshBcpDelta delta = bake.BcpDeltas[i];
                corrections.Add(new ShapeSyncHumanoidBoneCorrection
                {
                    bone = delta.Bone,
                    localPositionDelta = delta.Position,
                    localRotationDelta = delta.Rotation,
                    localScaleDelta = delta.Scale
                });
            }
            if (!HumanoidBoneCorrectionProfileApplicator.TryApply(animator, corrections, out string error))
            {
                HumanoidMeshResourceCleanup.Destroy(clone);
                return Fail("BcpSkeletonApplyFailed", "EditMode Mesh skeleton BCP application failed.", out diagnostic, error);
            }

            // DynamicBoneBlender resolves FBM against its immutable BaseAvatar.  The
            // rebuilt Avatar must start from that same HumanDescription; the clone
            // Animator can instead still reference an authoring/dynamic Avatar.
            DynamicBoneBlender blender = bake.LogicalPlan.Figure.Root.GetComponent<DynamicBoneBlender>();
            Avatar baseAvatar = blender != null && blender.BaseAvatar != null
                ? blender.BaseAvatar
                : animator.avatar;
            if (!TryBuildAvatar(clone, baseAvatar, out Avatar avatar, out diagnostic))
            {
                HumanoidMeshResourceCleanup.Destroy(clone);
                return false;
            }

            // Keep the FBM/BCP-resolved pose available to the Editor publisher.
            // The shared Mesh machine decides whether to publish it: Runtime/DDB
            // keeps the authoring split, while the Editor output is Pure Humanoid.
            HumanoidMeshTransformPose[] resolvedHumanoidPose = CaptureHumanoidPose(animator);

            // Keep the authoring pose for the internal Avatar-Rebind and bindpose
            // construction steps. For Pure Humanoid, the resolved FBM/BCP snapshot
            // is restored by the build machine before final skinning and again
            // before publish handoff.
            RestoreHumanoidPose(physicalHumanoidPose);

            escrow = new HumanoidMeshSkeletonEscrow(clone, animator, avatar, animatorSnapshot, resolvedHumanoidPose);
            return true;
        }

        private static bool TryBuildAvatar(GameObject root, Avatar baseAvatar, out Avatar avatar, out StackMachineDiagnostic diagnostic)
        {
            avatar = null;
            diagnostic = null;
            if (baseAvatar == null || !baseAvatar.isHuman)
                return Fail("HumanoidAvatarRequired", "EditMode Mesh Avatar rebuild requires a valid base Humanoid Avatar.", out diagnostic);
            HumanDescription description = baseAvatar.humanDescription;
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!byName.TryAdd(transforms[i].name, transforms[i]))
                    return Fail("AvatarSkeletonNameDuplicate", "EditMode Mesh Avatar rebuild requires unique skeleton transform names.", out diagnostic, transforms[i].name);
            }
            SkeletonBone[] skeleton = description.skeleton == null ? null : (SkeletonBone[])description.skeleton.Clone();
            if (skeleton == null || skeleton.Length == 0)
                return Fail("AvatarSkeletonMissing", "EditMode Mesh Avatar rebuild requires a nonempty HumanDescription skeleton.", out diagnostic);
            var humanBoneNames = new HashSet<string>(StringComparer.Ordinal);
            HumanBone[] human = description.human;
            if (human != null)
            {
                for (int i = 0; i < human.Length; i++)
                {
                    if (string.IsNullOrEmpty(human[i].boneName)) continue;
                    humanBoneNames.Add(human[i].boneName);
                    if (!byName.ContainsKey(human[i].boneName))
                        return Fail("AvatarSkeletonBoneMissing", "EditMode Mesh Avatar rebuild could not resolve a HumanDescription skeleton bone.", out diagnostic, human[i].boneName);
                }
            }

            var resolvedSkeleton = new List<SkeletonBone>(skeleton.Length);
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (!byName.TryGetValue(skeleton[i].name, out Transform transform))
                {
                    // Imported avatars can retain non-Human skeleton metadata for nodes which the
                    // detached ShapeSync Figure hierarchy intentionally does not retain.  Such a
                    // node is not required to rebuild the humanoid.  A mapped Human bone remains
                    // mandatory and must never be silently discarded.
                    if (humanBoneNames.Contains(skeleton[i].name))
                        return Fail("AvatarSkeletonBoneMissing", "EditMode Mesh Avatar rebuild could not resolve a HumanDescription skeleton bone.", out diagnostic, skeleton[i].name);
                    continue;
                }
                SkeletonBone bone = skeleton[i];
                bone.position = transform.localPosition;
                bone.rotation = transform.localRotation;
                bone.scale = transform.localScale;
                resolvedSkeleton.Add(bone);
            }
            description.skeleton = resolvedSkeleton.ToArray();
            avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                if (avatar != null) HumanoidMeshResourceCleanup.Destroy(avatar);
                avatar = null;
                return Fail("AvatarBuildFailed", "EditMode Mesh Avatar rebuild did not produce a valid Humanoid Avatar.", out diagnostic);
            }
            avatar.name = root.name + " (Spec17 Avatar Escrow)";
            return true;
        }

        private static void SetDontSave(Transform transform)
        {
            transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < transform.childCount; i++) SetDontSave(transform.GetChild(i));
        }

        private static HumanoidMeshTransformPose[] CaptureHumanoidPose(Animator animator)
        {
            var poses = new List<HumanoidMeshTransformPose>();
            if (animator == null) return poses.ToArray();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform transform = animator.GetBoneTransform((HumanBodyBones)i);
                if (transform != null) poses.Add(new HumanoidMeshTransformPose(transform));
            }
            return poses.ToArray();
        }

        private static void RestoreHumanoidPose(HumanoidMeshTransformPose[] poses)
        {
            if (poses == null) return;
            for (int i = 0; i < poses.Length; i++) poses[i].Restore();
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, detail: detail);
            return false;
        }
    }
}
