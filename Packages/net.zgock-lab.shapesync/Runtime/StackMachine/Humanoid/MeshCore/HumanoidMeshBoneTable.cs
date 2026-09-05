// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Final Figure-relative bone table and bindposes for compiler-owned Mesh remapping.</summary>
    public sealed class HumanoidMeshBoneTable
    {
        public HumanoidMeshBoneTable(Transform[] bones, Matrix4x4[] bindposes)
        {
            Bones = bones;
            Bindposes = bindposes;
        }

        public Transform[] Bones { get; }
        public Matrix4x4[] Bindposes { get; }
        /// <summary>Returns a new table with compiler-owned Extra Bones appended in deterministic attach order.</summary>
        public bool TryAppendExtraBones(IReadOnlyList<Transform> extraBones, Transform skeletonRoot, out HumanoidMeshBoneTable expanded, out StackMachineDiagnostic diagnostic)
        {
            expanded = null;
            diagnostic = null;
            if (skeletonRoot == null) return Fail("SkeletonEscrowRequired", "Extra Bone merge requires a local skeleton root.", out diagnostic);
            if (extraBones == null || extraBones.Count == 0) { expanded = this; return true; }
            var seen = new System.Collections.Generic.HashSet<Transform>(Bones);
            var appended = new System.Collections.Generic.List<Transform>(extraBones.Count);
            for (int i = 0; i < extraBones.Count; i++)
            {
                Transform bone = extraBones[i];
                if (bone == null || !seen.Add(bone)) return Fail("ExtraBoneTableConflict", "Extra Bone merge contains a null or duplicate final bone.", out diagnostic, i.ToString());
                appended.Add(bone);
            }
            var bones = new Transform[Bones.Length + appended.Count];
            var bindposes = new Matrix4x4[bones.Length];
            Array.Copy(Bones, bones, Bones.Length);
            Array.Copy(Bindposes, bindposes, Bindposes.Length);
            Matrix4x4 rootMatrix = skeletonRoot.localToWorldMatrix;
            for (int i = 0; i < appended.Count; i++)
            {
                int index = Bones.Length + i;
                bones[index] = appended[i];
                bindposes[index] = appended[i].worldToLocalMatrix * rootMatrix;
            }
            expanded = new HumanoidMeshBoneTable(bones, bindposes);
            return true;
        }

        public static bool TryCreate(HumanoidMeshSource figure, HumanoidMeshSkeletonEscrow skeleton, out HumanoidMeshBoneTable table, out StackMachineDiagnostic diagnostic)
            => TryCreate(null, figure, skeleton, out table, out diagnostic);

        /// <summary>
        /// Builds the Figure table with the same FBM-resolved Figure bindposes used by
        /// <see cref="DynamicBoneBlender"/>.  BCP changes the output skeleton pose, but
        /// does not replace DDB's independently resolved Figure bindpose contract.
        /// </summary>
        public static bool TryCreate(HumanoidMeshFbmBakeResult bake, HumanoidMeshSource figure, HumanoidMeshSkeletonEscrow skeleton, out HumanoidMeshBoneTable table, out StackMachineDiagnostic diagnostic)
        {
            table = null;
            diagnostic = null;
            if (figure.Root == null || figure.Renderer == null || figure.Renderer.sharedMesh == null)
                return Fail("FigureSkinningRequired", "EditMode Mesh bone table requires a Figure SkinnedMeshRenderer and Mesh.", out diagnostic);
            if (skeleton == null || skeleton.Root == null)
                return Fail("SkeletonEscrowRequired", "EditMode Mesh bone table requires a local skeleton escrow.", out diagnostic);
            Transform[] sourceBones = figure.Renderer.bones;
            if (sourceBones == null || sourceBones.Length == 0 || figure.Renderer.rootBone == null)
                return Fail("FigureBonesRequired", "Figure SkinnedMeshRenderer requires bones and a rootBone.", out diagnostic);

            var bones = new Transform[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                if (sourceBones[i] == null) return Fail("FigureBoneMissing", "A Figure renderer bone reference is null.", out diagnostic, i.ToString());
                string path = GetRelativePath(figure.Root.transform, sourceBones[i]);
                if (path == null) return Fail("FigureBoneOutsideRoot", "A weighted Figure bone is outside the Figure hierarchy.", out diagnostic, i.ToString());
                Transform mapped = string.IsNullOrEmpty(path) ? skeleton.Root.transform : skeleton.Root.transform.Find(path);
                if (mapped == null) return Fail("FigureBoneMissingInSkeleton", "A weighted Figure bone is missing from the local skeleton escrow.", out diagnostic, path);
                bones[i] = mapped;
            }
            if (!TryResolveFigureBindposes(bake, figure, bones, skeleton.Root.transform, out Matrix4x4[] bindposes, out diagnostic)) return false;
            table = new HumanoidMeshBoneTable(bones, bindposes);
            return true;
        }

        private static bool TryResolveFigureBindposes(HumanoidMeshFbmBakeResult bake, HumanoidMeshSource figure, Transform[] bones, Transform skeletonRoot, out Matrix4x4[] bindposes, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Mesh sourceMesh = figure.Renderer.sharedMesh;
            bindposes = sourceMesh.bindposes == null ? null : (Matrix4x4[])sourceMesh.bindposes.Clone();
            if (bindposes == null || bindposes.Length == 0)
            {
                bindposes = new Matrix4x4[bones.Length];
                for (int i = 0; i < bindposes.Length; i++)
                {
                    if (bones[i] == null) return Fail("FigureBoneMissingInSkeleton", "EditMode Mesh bone table requires every weighted Figure bone in the local skeleton.", out diagnostic, i.ToString());
                    bindposes[i] = bones[i].worldToLocalMatrix * skeletonRoot.localToWorldMatrix;
                }
                return true;
            }
            if (bindposes.Length != bones.Length)
                return Fail("FigureBindposesRequired", "EditMode Mesh bone table requires Figure bindposes for every Figure bone.", out diagnostic);

            DynamicBoneBlender blender = bake?.LogicalPlan?.Figure.Root != null ? bake.LogicalPlan.Figure.Root.GetComponent<DynamicBoneBlender>() : null;
            CharacterBoneRegistry baseRegistry = blender?.BaseRegistry;
            if (blender == null || baseRegistry?.bonePoses == null || bake.FbmWeights.Count == 0) return true;

            var baseByIndex = new BonePoseData[bindposes.Length];
            for (int i = 0; i < baseRegistry.bonePoses.Count; i++)
            {
                BonePoseData pose = baseRegistry.bonePoses[i];
                if (pose != null && pose.hasBindpose && pose.bindposeIndex >= 0 && pose.bindposeIndex < baseByIndex.Length) baseByIndex[pose.bindposeIndex] = pose;
            }
            for (int i = 0; i < baseByIndex.Length; i++) if (baseByIndex[i] == null) return true;

            for (int index = 0; index < bindposes.Length; index++)
            {
                BindposeTrs baseValue = Decompose(baseByIndex[index].bindpose);
                if (!baseValue.Valid) return true;
                Vector3 position = baseValue.Position;
                Vector3 scale = baseValue.Scale;
                Quaternion rotation = baseValue.Rotation;
                for (int targetIndex = 0; targetIndex < blender.Targets.Count; targetIndex++)
                {
                    DynamicBoneBlendTarget target = blender.Targets[targetIndex];
                    if (target == null || !target.enabled || string.IsNullOrEmpty(target.blendName)
                        || !bake.FbmWeights.TryGetValue(target.blendName, out float weight) || !float.IsFinite(weight)) continue;
                    if (!TryFindBindpose(target.targetRegistry, baseByIndex[index].boneName, out Matrix4x4 targetBindpose)) continue;
                    BindposeTrs targetValue = Decompose(targetBindpose);
                    if (!targetValue.Valid) continue;
                    position += (targetValue.Position - baseValue.Position) * weight;
                    scale += (targetValue.Scale - baseValue.Scale) * weight;
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, targetValue.Rotation * Quaternion.Inverse(baseValue.Rotation), weight) * rotation;
                }
                bindposes[index] = Matrix4x4.TRS(position, Normalize(rotation), scale);
            }
            return true;
        }

        private static bool TryFindBindpose(CharacterBoneRegistry registry, string boneName, out Matrix4x4 bindpose)
        {
            if (registry?.bonePoses != null)
            {
                for (int i = 0; i < registry.bonePoses.Count; i++)
                {
                    BonePoseData pose = registry.bonePoses[i];
                    if (pose != null && pose.hasBindpose && pose.boneName == boneName) { bindpose = pose.bindpose; return true; }
                }
            }
            bindpose = default;
            return false;
        }

        private readonly struct BindposeTrs
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;
            public readonly bool Valid;
            public BindposeTrs(Vector3 position, Quaternion rotation, Vector3 scale, bool valid) { Position = position; Rotation = rotation; Scale = scale; Valid = valid; }
        }

        private static BindposeTrs Decompose(Matrix4x4 matrix)
        {
            Vector3 right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            Vector3 scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
            if (scale.x <= Mathf.Epsilon || scale.y <= Mathf.Epsilon || scale.z <= Mathf.Epsilon) return new BindposeTrs(default, Quaternion.identity, default, false);
            right /= scale.x; up /= scale.y; forward /= scale.z;
            if (forward.sqrMagnitude <= Mathf.Epsilon || up.sqrMagnitude <= Mathf.Epsilon) return new BindposeTrs(default, Quaternion.identity, default, false);
            return new BindposeTrs(new Vector3(matrix.m03, matrix.m13, matrix.m23), Quaternion.LookRotation(forward, up), scale, true);
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float squared = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return squared > Mathf.Epsilon ? Quaternion.Normalize(value) : Quaternion.identity;
        }

        private static bool[] GetUsedBoneIndices(Mesh mesh, int count)
        {
            var used = new bool[count];
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                Mark(weight.boneIndex0, weight.weight0); Mark(weight.boneIndex1, weight.weight1); Mark(weight.boneIndex2, weight.weight2); Mark(weight.boneIndex3, weight.weight3);
            }
            return used;
            void Mark(int index, float value) { if (value > 0f && index >= 0 && index < used.Length) used[index] = true; }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var names = new System.Collections.Generic.List<string>();
            for (Transform cursor = target; cursor != null && cursor != root; cursor = cursor.parent) names.Add(cursor.name);
            if (target != root && (names.Count == 0 || target == null)) return null;
            Transform probe = target;
            while (probe != null && probe != root) probe = probe.parent;
            if (probe != root) return null;
            names.Reverse();
            return string.Join("/", names);
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, detail: detail);
            return false;
        }
    }
}
