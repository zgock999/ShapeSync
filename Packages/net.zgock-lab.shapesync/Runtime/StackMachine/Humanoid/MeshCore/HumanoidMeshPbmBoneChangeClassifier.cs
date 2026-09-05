// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>
    /// Classifies whether one PBM changes data that a Pure Humanoid BlendShape cannot represent.
    /// This mirrors the read-only target and difference-registry inputs consumed by
    /// <see cref="DynamicBoneBlender"/>; it neither executes nor mutates that runtime component.
    /// </summary>
    public static class HumanoidMeshPbmBoneChangeClassifier
    {
        private const float Tolerance = 0.00001f;

        /// <summary>
        /// Gets whether the named PBM has any bindpose, Avatar skeleton, local-pose, or attached Outfit extra-bone change.
        /// Missing target data means that DynamicBoneBlender has no skeletal contribution for that input.
        /// </summary>
        public static bool HasBoneChange(HumanoidMeshLogicalPlan plan, string pbmName)
        {
            if (plan == null || string.IsNullOrWhiteSpace(pbmName)) return false;
            string targetName = BlendShapeReservedPrefixes.Pbm + pbmName;
            DynamicBoneBlender blender = plan.Figure.Root == null ? null : plan.Figure.Root.GetComponent<DynamicBoneBlender>();
            if (blender != null && HasFigureBoneChange(blender, targetName)) return true;
            return HasAttachedOutfitExtraBoneChange(plan.AttachedOutfits, blender, targetName, pbmName);
        }

        private static bool HasFigureBoneChange(DynamicBoneBlender blender, string pbmTargetName)
        {
            DynamicBoneBlendTarget pbm = FindTarget(blender.Targets, pbmTargetName);
            if (pbm == null) return false;
            if (RegistryChanges(blender.BaseRegistry, pbm.targetRegistry)
                || AvatarSkeletonChanges(blender.BaseAvatar, pbm.targetAvatar, pbm.targetRegistry)) return true;

            IReadOnlyList<DynamicBonePbmDifferenceTarget> differences = pbm.pbmDifferenceTargets;
            for (int i = 0; differences != null && i < differences.Count; i++)
            {
                DynamicBonePbmDifferenceTarget difference = differences[i];
                if (difference == null || string.IsNullOrWhiteSpace(difference.fbmBlendName)) continue;
                DynamicBoneBlendTarget fbm = FindTarget(blender.Targets, difference.fbmBlendName);
                if (fbm == null) continue;
                if (RegistryChanges(fbm.targetRegistry, difference.targetRegistry)
                    || AvatarSkeletonChanges(fbm.targetAvatar, difference.targetAvatar, difference.targetRegistry)) return true;
            }
            return false;
        }

        private static bool HasAttachedOutfitExtraBoneChange(IReadOnlyList<HumanoidMeshSource> outfits, DynamicBoneBlender blender, string pbmTargetName, string pbmName)
        {
            for (int i = 0; outfits != null && i < outfits.Count; i++)
            {
                ShapeSyncOutfit outfit = outfits[i].Outfit;
                if (outfit == null) continue;
                if (outfit.TryGetFbmExtraBoneRegistry(pbmTargetName, out CharacterBoneRegistry pbmRegistry)
                    && RegistryChanges(outfit.BaseExtraBoneRegistry, pbmRegistry)) return true;

                if (blender == null) continue;
                DynamicBoneBlendTarget pbm = FindTarget(blender.Targets, pbmTargetName);
                IReadOnlyList<DynamicBonePbmDifferenceTarget> differences = pbm == null ? null : pbm.pbmDifferenceTargets;
                for (int differenceIndex = 0; differences != null && differenceIndex < differences.Count; differenceIndex++)
                {
                    DynamicBonePbmDifferenceTarget difference = differences[differenceIndex];
                    if (difference == null || string.IsNullOrWhiteSpace(difference.fbmBlendName)) continue;
                    string differenceName = BlendShapeReservedPrefixes.Pbm + difference.fbmBlendName + "_" + pbmName;
                    if (!outfit.TryGetFbmExtraBoneRegistry(differenceName, out CharacterBoneRegistry differenceRegistry)) continue;
                    outfit.TryGetFbmExtraBoneRegistry(difference.fbmBlendName, out CharacterBoneRegistry fbmRegistry);
                    if (RegistryChanges(fbmRegistry ?? outfit.BaseExtraBoneRegistry, differenceRegistry)) return true;
                }
            }
            return false;
        }

        private static DynamicBoneBlendTarget FindTarget(IReadOnlyList<DynamicBoneBlendTarget> targets, string name)
        {
            for (int i = 0; targets != null && i < targets.Count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                if (target != null && string.Equals(target.blendName, name, StringComparison.Ordinal)) return target;
            }
            return null;
        }

        private static bool RegistryChanges(CharacterBoneRegistry baseline, CharacterBoneRegistry target)
        {
            if (baseline == null || target == null || baseline.bonePoses == null || target.bonePoses == null) return false;
            var targetByPath = new Dictionary<string, BonePoseData>(StringComparer.Ordinal);
            for (int i = 0; i < target.bonePoses.Count; i++)
            {
                BonePoseData pose = target.bonePoses[i];
                if (pose != null && !string.IsNullOrEmpty(pose.boneName) && !targetByPath.ContainsKey(pose.boneName)) targetByPath.Add(pose.boneName, pose);
            }
            for (int i = 0; i < baseline.bonePoses.Count; i++)
            {
                BonePoseData basePose = baseline.bonePoses[i];
                if (basePose == null || string.IsNullOrEmpty(basePose.boneName) || !targetByPath.TryGetValue(basePose.boneName, out BonePoseData targetPose)) continue;
                if (!Approximately(basePose.localPosition, targetPose.localPosition)
                    || !Approximately(basePose.localRotation, targetPose.localRotation)
                    || !Approximately(basePose.localScale, targetPose.localScale)) return true;
                if (basePose.hasBindpose && targetPose.hasBindpose && !Approximately(basePose.bindpose, targetPose.bindpose)) return true;
            }
            return false;
        }

        private static bool AvatarSkeletonChanges(Avatar baselineAvatar, Avatar targetAvatar, CharacterBoneRegistry targetRegistry)
        {
            if (baselineAvatar == null || !baselineAvatar.isHuman) return false;
            HumanDescription baseline = baselineAvatar.humanDescription;
            HumanDescription target = targetAvatar != null && targetAvatar.isHuman ? targetAvatar.humanDescription : baseline;
            if (baseline.skeleton == null || target.skeleton == null) return false;
            for (int i = 0; i < baseline.skeleton.Length; i++)
            {
                SkeletonBone baseBone = baseline.skeleton[i];
                if (!TryGetSkeletonBone(target.skeleton, baseBone.name, out SkeletonBone targetBone)) continue;
                ApplyRegistry(targetRegistry, ref targetBone);
                if (!Approximately(baseBone.position, targetBone.position)
                    || !Approximately(baseBone.rotation, targetBone.rotation)
                    || !Approximately(baseBone.scale, targetBone.scale)) return true;
            }
            return false;
        }

        private static bool TryGetSkeletonBone(SkeletonBone[] skeleton, string name, out SkeletonBone bone)
        {
            for (int i = 0; skeleton != null && i < skeleton.Length; i++)
            {
                if (skeleton[i].name == name) { bone = skeleton[i]; return true; }
            }
            bone = default;
            return false;
        }

        private static void ApplyRegistry(CharacterBoneRegistry registry, ref SkeletonBone bone)
        {
            for (int i = 0; registry?.bonePoses != null && i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose == null || Leaf(pose.boneName) != bone.name) continue;
                bone.position = pose.localPosition;
                bone.rotation = pose.localRotation;
                bone.scale = pose.localScale;
                return;
            }
        }

        private static string Leaf(string path)
        {
            int index = path == null ? -1 : path.LastIndexOf('/');
            return index < 0 ? path : path.Substring(index + 1);
        }

        private static bool Approximately(Vector3 left, Vector3 right) => (left - right).sqrMagnitude <= Tolerance * Tolerance;
        private static bool Approximately(Quaternion left, Quaternion right) => Mathf.Abs(Quaternion.Dot(left, right)) >= 1f - Tolerance;
        private static bool Approximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) if (Mathf.Abs(left[row, column] - right[row, column]) > Tolerance) return false;
            return true;
        }
    }
}
