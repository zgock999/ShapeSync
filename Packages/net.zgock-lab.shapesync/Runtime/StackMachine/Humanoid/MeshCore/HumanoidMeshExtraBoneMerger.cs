// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Detached Extra Bone merge mapping retained for later Outfit Mesh bone remapping.</summary>
    public sealed class HumanoidMeshExtraBoneMergeResult
    {
        public HumanoidMeshExtraBoneMergeResult(HumanoidMeshBoneTable boneTable, Dictionary<Transform, Transform> finalByOutfitTransform, string[] ownedRootPaths)
        {
            BoneTable = boneTable;
            FinalByOutfitTransform = finalByOutfitTransform;
            OwnedRootPaths = Array.AsReadOnly(ownedRootPaths ?? Array.Empty<string>());
        }

        public HumanoidMeshBoneTable BoneTable { get; }
        public IReadOnlyDictionary<Transform, Transform> FinalByOutfitTransform { get; }
        public IReadOnlyList<string> OwnedRootPaths { get; }
    }

    /// <summary>
    /// Recreates OutfitAttacher's retained Extra Bone hierarchy rule inside the compiler-owned skeleton only.
    /// It never attaches an Outfit, mutates a source, or reuses the runtime transaction route.
    /// </summary>
    public static class HumanoidMeshExtraBoneMerger
    {
        public static bool TryMerge(HumanoidMeshSource outfit, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable baseTable, ISet<string> claimedRootPaths, out HumanoidMeshExtraBoneMergeResult result, out StackMachineDiagnostic diagnostic)
        {
            return TryMerge(outfit, skeleton, baseTable, claimedRootPaths, null, out result, out diagnostic);
        }

        public static bool TryMerge(HumanoidMeshSource outfit, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable baseTable, ISet<string> claimedRootPaths, IReadOnlyDictionary<string, float> fbmWeights, out HumanoidMeshExtraBoneMergeResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (outfit.Root == null || outfit.Outfit == null)
                return Fail("OutfitExtraBoneSourceRequired", "Extra Bone merge requires an attached Outfit source.", out diagnostic);
            if (skeleton == null || skeleton.Root == null)
                return Fail("SkeletonEscrowRequired", "Extra Bone merge requires a local skeleton escrow.", out diagnostic);
            if (baseTable == null)
                return Fail("FigureBoneTableRequired", "Extra Bone merge requires the Figure base bone table.", out diagnostic);

            CharacterBoneRegistry registry = outfit.Outfit.BaseExtraBoneRegistry;
            if (registry == null || registry.bonePoses == null || registry.bonePoses.Count == 0)
            {
                result = new HumanoidMeshExtraBoneMergeResult(baseTable, new Dictionary<Transform, Transform>(), Array.Empty<string>());
                return true;
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var humanoidBones = BuildHumanoidBoneSet(skeleton.Animator);
            var finalBySource = new Dictionary<Transform, Transform>();
            var appended = new List<Transform>();
            var plannedRoots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose == null || string.IsNullOrEmpty(pose.boneName))
                    return Fail("ExtraBonePathInvalid", "Extra Bone Registry contains a null or empty bone path.", out diagnostic, i.ToString());
                if (!paths.Add(pose.boneName))
                    return Fail("ExtraBonePathDuplicate", "Extra Bone Registry contains a duplicate bone path.", out diagnostic, pose.boneName);
                if (pose.hasBindpose || pose.bindposeIndex >= 0)
                    return Fail("ExtraBoneBindposeForbidden", "Extra Bone Registry cannot contain bindpose reference bones.", out diagnostic, pose.boneName);
                if (IsOwnedByClaimedRoot(pose.boneName, claimedRootPaths))
                    return Fail("ExtraBoneRootOwned", "Extra Bone path is already owned by an earlier attached Outfit.", out diagnostic, pose.boneName);

                if (!TryGetRootPlan(skeleton.Root.transform, pose.boneName, out Transform anchor, out string rootPath, out diagnostic)) return false;
                Transform sourceBone = outfit.Root.transform.Find(pose.boneName);
                if (sourceBone == null)
                    return Fail("ExtraBoneSourceMissing", "Extra Bone Registry path is absent from the Outfit source.", out diagnostic, pose.boneName);
                Transform existing = skeleton.Root.transform.Find(pose.boneName);
                if (existing != null && humanoidBones.Contains(existing))
                    return Fail("ExtraBoneHumanoidConflict", "Extra Bone Registry path resolves to a Humanoid bone.", out diagnostic, pose.boneName);

                if (rootPath == null)
                {
                    finalBySource[sourceBone] = existing;
                    continue;
                }
                if (!plannedRoots.Add(rootPath)) continue;
                Transform sourceRoot = outfit.Root.transform.Find(rootPath);
                if (sourceRoot == null)
                    return Fail("ExtraBoneSourceMissing", "Extra Bone root is absent from the Outfit source.", out diagnostic, rootPath);
                if (!TryCloneSubtree(sourceRoot, anchor, finalBySource, appended, out diagnostic)) return false;
            }

            // DynamicBoneBlender applies the base registry followed by each active FBM registry
            // to retained Extra Bones.  The compiler has no retained runtime blender, so bake
            // that same local pose into the detached clone before deriving final bindposes.
            if (!TryApplyResolvedFbmPoses(outfit.Outfit, registry, finalBySource, fbmWeights, out diagnostic)) return false;
            if (!baseTable.TryAppendExtraBones(appended, skeleton.Root.transform, out HumanoidMeshBoneTable expanded, out diagnostic)) return false;
            result = new HumanoidMeshExtraBoneMergeResult(expanded, finalBySource, new List<string>(plannedRoots).ToArray());
            return true;
        }

        private static bool TryApplyResolvedFbmPoses(ShapeSyncOutfit outfit, CharacterBoneRegistry baseRegistry, IReadOnlyDictionary<Transform, Transform> finalBySource, IReadOnlyDictionary<string, float> fbmWeights, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (outfit == null || baseRegistry == null || baseRegistry.bonePoses == null) return Fail("ExtraBoneRegistryRequired", "Extra Bone pose bake requires a base registry.", out diagnostic);
            var baseByPath = new Dictionary<string, BonePoseData>(StringComparer.Ordinal);
            for (int i = 0; i < baseRegistry.bonePoses.Count; i++)
            {
                BonePoseData pose = baseRegistry.bonePoses[i];
                if (pose == null || string.IsNullOrEmpty(pose.boneName) || !baseByPath.TryAdd(pose.boneName, pose))
                    return Fail("ExtraBonePathDuplicate", "Extra Bone Registry contains an invalid or duplicate bone path.", out diagnostic, i.ToString());
            }

            var targetByBlend = new Dictionary<string, Dictionary<string, BonePoseData>>(StringComparer.Ordinal);
            IReadOnlyList<ShapeSyncOutfitFbmExtraBoneRegistry> registries = outfit.FbmExtraBoneRegistries;
            if (registries == null) return Fail("FbmExtraBoneRegistryRequired", "FBM Extra Bone Registry list is null.", out diagnostic, outfit.RegistryId);
            for (int i = 0; i < registries.Count; i++)
            {
                ShapeSyncOutfitFbmExtraBoneRegistry entry = registries[i];
                if (entry == null || string.IsNullOrEmpty(entry.blendName) || entry.extraBoneRegistry == null || entry.extraBoneRegistry.bonePoses == null)
                    return Fail("FbmExtraBoneRegistryInvalid", "FBM Extra Bone Registry entry is incomplete.", out diagnostic, i.ToString());
                var poses = new Dictionary<string, BonePoseData>(StringComparer.Ordinal);
                for (int poseIndex = 0; poseIndex < entry.extraBoneRegistry.bonePoses.Count; poseIndex++)
                {
                    BonePoseData pose = entry.extraBoneRegistry.bonePoses[poseIndex];
                    if (pose == null || string.IsNullOrEmpty(pose.boneName) || pose.hasBindpose || pose.bindposeIndex >= 0 || !poses.TryAdd(pose.boneName, pose))
                        return Fail("FbmExtraBoneRegistryInvalid", "FBM Extra Bone Registry contains an invalid, bindpose, or duplicate bone path.", out diagnostic, entry.blendName);
                }
                if (!targetByBlend.TryAdd(entry.blendName, poses)) return Fail("FbmExtraBoneRegistryDuplicate", "FBM Extra Bone Registry contains a duplicate blend name.", out diagnostic, entry.blendName);
            }

            foreach (KeyValuePair<string, BonePoseData> pair in baseByPath)
            {
                Transform source = outfit.transform.Find(pair.Key);
                if (source == null || !finalBySource.TryGetValue(source, out Transform final)) return Fail("ExtraBoneSourceMissing", "Extra Bone pose path is absent from the cloned Outfit hierarchy.", out diagnostic, pair.Key);
                BonePoseData basePose = pair.Value;
                Vector3 position = basePose.localPosition;
                Vector3 scale = basePose.localScale;
                Quaternion rotation = basePose.localRotation;
                if (fbmWeights != null)
                {
                    foreach (KeyValuePair<string, float> weight in fbmWeights)
                    {
                        if (!targetByBlend.TryGetValue(weight.Key, out Dictionary<string, BonePoseData> targets) || !targets.TryGetValue(pair.Key, out BonePoseData target)) continue;
                        position += (target.localPosition - basePose.localPosition) * weight.Value;
                        scale += (target.localScale - basePose.localScale) * weight.Value;
                        rotation = Quaternion.SlerpUnclamped(Quaternion.identity, target.localRotation * Quaternion.Inverse(basePose.localRotation), weight.Value) * rotation;
                    }
                }
                final.localPosition = position;
                final.localRotation = rotation;
                final.localScale = scale;
                if (!IsFiniteNonZero(final.lossyScale))
                    return Fail("ExtraBonePoseScaleInvalid", "Extra Bone pose resolution produced a zero or non-finite world scale.", out diagnostic, pair.Key + "; source=" + source.localScale + "; resolved=" + scale + "; final=" + final.localScale);
            }
            return true;
        }

        private static bool IsFiniteNonZero(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z)
                && Mathf.Abs(value.x) > Mathf.Epsilon && Mathf.Abs(value.y) > Mathf.Epsilon && Mathf.Abs(value.z) > Mathf.Epsilon;
        }

        private static bool TryGetRootPlan(Transform skeletonRoot, string path, out Transform anchor, out string rootPath, out StackMachineDiagnostic diagnostic)
        {
            anchor = null;
            rootPath = null;
            diagnostic = null;
            Transform current = skeletonRoot;
            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i])) return Fail("ExtraBonePathInvalid", "Extra Bone path contains an empty segment.", out diagnostic, path);
                Transform child = current.Find(segments[i]);
                if (child == null)
                {
                    anchor = current;
                    rootPath = JoinPath(segments, i + 1);
                    return true;
                }
                current = child;
            }
            anchor = current;
            return true;
        }

        private static bool TryCloneSubtree(Transform sourceRoot, Transform anchor, Dictionary<Transform, Transform> finalBySource, List<Transform> appended, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (sourceRoot == null || anchor == null) return Fail("ExtraBoneCloneInputInvalid", "Extra Bone clone requires a source root and skeleton anchor.", out diagnostic);
            GameObject clone = UnityEngine.Object.Instantiate(sourceRoot.gameObject, anchor, false);
            clone.name = sourceRoot.name;
            if (!TryMapHierarchy(sourceRoot, clone.transform, finalBySource, appended))
            {
                HumanoidMeshResourceCleanup.Destroy(clone);
                return Fail("ExtraBoneCloneHierarchyInvalid", "Extra Bone clone did not preserve its source hierarchy.", out diagnostic, sourceRoot.name);
            }
            return true;
        }

        private static bool TryMapHierarchy(Transform source, Transform final, Dictionary<Transform, Transform> finalBySource, List<Transform> appended)
        {
            if (source == null || final == null || source.childCount != final.childCount) return false;
            finalBySource[source] = final;
            appended.Add(final);
            for (int i = 0; i < source.childCount; i++)
            {
                if (!TryMapHierarchy(source.GetChild(i), final.GetChild(i), finalBySource, appended)) return false;
            }
            return true;
        }

        private static HashSet<Transform> BuildHumanoidBoneSet(Animator animator)
        {
            var result = new HashSet<Transform>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return result;
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                Transform transform = animator.GetBoneTransform(bone);
                if (transform != null) result.Add(transform);
            }
            return result;
        }

        private static string JoinPath(string[] segments, int count) => string.Join("/", segments, 0, count);

        private static bool IsOwnedByClaimedRoot(string path, ISet<string> claimedRootPaths)
        {
            if (claimedRootPaths == null) return false;
            foreach (string root in claimedRootPaths) if (path == root || path.StartsWith(root + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", code, message, detail: detail);
            return false;
        }
    }
}
