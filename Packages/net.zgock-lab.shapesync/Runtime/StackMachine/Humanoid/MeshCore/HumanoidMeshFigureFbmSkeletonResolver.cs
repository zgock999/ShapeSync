// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Applies the Figure FBM skeletal pose using DDB authoring data without executing DynamicBoneBlender.</summary>
    public static class HumanoidMeshFigureFbmSkeletonResolver
    {
        public static bool TryApply(HumanoidMeshFbmBakeResult bake, GameObject skeletonRoot, Animator animator, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            DynamicBoneBlender blender = bake?.LogicalPlan?.Figure.Root != null ? bake.LogicalPlan.Figure.Root.GetComponent<DynamicBoneBlender>() : null;
            if (blender == null || bake.FbmWeights.Count == 0) return true;
            Avatar baseAvatar = blender.BaseAvatar != null ? blender.BaseAvatar : animator?.avatar;
            if (baseAvatar == null || !baseAvatar.isHuman) return Fail("FigureFbmBaseAvatarRequired", "Figure FBM skeleton resolution requires a valid base Humanoid Avatar.", out diagnostic);
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform transform in skeletonRoot.GetComponentsInChildren<Transform>(true)) if (!byName.TryAdd(transform.name, transform)) return Fail("FigureFbmSkeletonNameDuplicate", "Figure FBM skeleton resolution requires unique Transform names.", out diagnostic, transform.name);
            HumanDescription baseDescription = baseAvatar.humanDescription;
            if (baseDescription.skeleton == null) return Fail("FigureFbmBaseSkeletonRequired", "Figure FBM skeleton resolution requires base Avatar skeleton data.", out diagnostic);
            // `skeletonRoot` is a detached clone, while the Blender belongs to the source
            // Figure.  Compare by the Avatar's stable skeleton name, not Transform identity:
            // source Animator Transforms can never be members of the clone hierarchy.
            var humanBoneNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) humanBoneNames.Add(bone.name);
            }
            foreach (SkeletonBone baseBone in baseDescription.skeleton)
            {
                if (!byName.TryGetValue(baseBone.name, out Transform transform)) continue;
                Vector3 position = baseBone.position; Vector3 scale = baseBone.scale; Quaternion rotation = baseBone.rotation;
                foreach (DynamicBoneBlendTarget target in blender.Targets)
                {
                    if (!TryWeight(bake.FbmWeights, target, out float weight)) continue;
                    if (!TryGetTargetBone(baseDescription, target, baseBone.name, out SkeletonBone targetBone)) continue;
                    position += (targetBone.position - baseBone.position) * weight;
                    scale += (targetBone.scale - baseBone.scale) * weight;
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, targetBone.rotation * Quaternion.Inverse(baseBone.rotation), weight) * rotation;
                }
                transform.localPosition = position; transform.localRotation = Normalize(rotation); transform.localScale = scale;
            }
            CharacterBoneRegistry registry = blender.BaseRegistry;
            if (registry == null || registry.bonePoses == null) return true;
            foreach (BonePoseData basePose in registry.bonePoses)
            {
                if (basePose == null || basePose.hasBindpose || string.IsNullOrEmpty(basePose.boneName)) continue;
                Transform transform = skeletonRoot.transform.Find(basePose.boneName);
                if (transform == null || humanBoneNames.Contains(transform.name)) continue;
                Vector3 position = basePose.localPosition; Vector3 scale = basePose.localScale; Quaternion rotation = basePose.localRotation;
                foreach (DynamicBoneBlendTarget target in blender.Targets)
                {
                    if (!TryWeight(bake.FbmWeights, target, out float weight) || !TryFindPose(target.targetRegistry, basePose.boneName, out BonePoseData targetPose)) continue;
                    position += (targetPose.localPosition - basePose.localPosition) * weight;
                    scale += (targetPose.localScale - basePose.localScale) * weight;
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, targetPose.localRotation * Quaternion.Inverse(basePose.localRotation), weight) * rotation;
                }
                transform.localPosition = position; transform.localRotation = Normalize(rotation); transform.localScale = scale;
            }
            return true;
        }

        private static bool TryWeight(IReadOnlyDictionary<string, float> weights, DynamicBoneBlendTarget target, out float value)
        { value = 0f; return target != null && target.enabled && !string.IsNullOrEmpty(target.blendName) && weights.TryGetValue(target.blendName, out value) && float.IsFinite(value); }
        private static bool TryGetTargetBone(HumanDescription baseDescription, DynamicBoneBlendTarget target, string name, out SkeletonBone value)
        {
            HumanDescription description = target.targetAvatar != null && target.targetAvatar.isHuman ? target.targetAvatar.humanDescription : baseDescription;
            if (description.skeleton != null) for (int i = 0; i < description.skeleton.Length; i++) if (description.skeleton[i].name == name) { value = description.skeleton[i]; ApplyRegistry(target.targetRegistry, ref value); return true; }
            value = default; return false;
        }
        private static void ApplyRegistry(CharacterBoneRegistry registry, ref SkeletonBone bone)
        { if (registry?.bonePoses != null) for (int i = 0; i < registry.bonePoses.Count; i++) { BonePoseData pose = registry.bonePoses[i]; if (pose != null && Leaf(pose.boneName) == bone.name) { bone.position = pose.localPosition; bone.rotation = pose.localRotation; bone.scale = pose.localScale; return; } } }
        private static bool TryFindPose(CharacterBoneRegistry registry, string path, out BonePoseData value)
        { if (registry?.bonePoses != null) for (int i = 0; i < registry.bonePoses.Count; i++) if (registry.bonePoses[i] != null && registry.bonePoses[i].boneName == path) { value = registry.bonePoses[i]; return true; } value = null; return false; }
        private static string Leaf(string path) { int index = path == null ? -1 : path.LastIndexOf('/'); return index < 0 ? path : path.Substring(index + 1); }
        private static Quaternion Normalize(Quaternion value) => value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > Mathf.Epsilon ? Quaternion.Normalize(value) : Quaternion.identity;
        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, detail: detail); return false; }
    }
}
