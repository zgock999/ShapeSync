// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Produces a compiler-owned Mesh whose skinning indices address the final Figure bone table.</summary>
    public static class HumanoidMeshSkinningRemapper
    {
        public static bool TryRemap(HumanoidMeshFbmBakedSource source, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable table, IReadOnlyDictionary<Transform, Transform> extraBoneTransforms, out Mesh remapped, out StackMachineDiagnostic diagnostic)
            => TryRemap(source, skeleton, table, extraBoneTransforms, null, out remapped, out diagnostic);

        public static bool TryRemap(HumanoidMeshFbmBakedSource source, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable table, IReadOnlyDictionary<Transform, Transform> extraBoneTransforms, IReadOnlyDictionary<Transform, Transform> figureBoneTransforms, out Mesh remapped, out StackMachineDiagnostic diagnostic)
            => TryRemap(source, skeleton, table, extraBoneTransforms, figureBoneTransforms, null, out remapped, out diagnostic);

        /// <summary>
        /// Remaps one baked source into the shared Figure table.  OutfitAttacher gives each
        /// attached renderer a source-specific bindpose space (aligned to the Figure for normal
        /// outfits; profile-controlled for BCP-baked outfits).  That space is baked into its
        /// temporary vertices before the common table is assigned.
        /// </summary>
        public static bool TryRemap(HumanoidMeshFbmBakedSource source, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable table, IReadOnlyDictionary<Transform, Transform> extraBoneTransforms, IReadOnlyDictionary<Transform, Transform> figureBoneTransforms, IReadOnlyDictionary<string, float> fbmWeights, out Mesh remapped, out StackMachineDiagnostic diagnostic)
        {
            remapped = null;
            diagnostic = null;
            if (source.Mesh == null || source.Source.Renderer == null || source.Source.Root == null)
                return Fail("MeshSkinningSourceRequired", "Mesh skinning remap requires a candidate Mesh, renderer, and source root.", out diagnostic);
            if (skeleton == null || skeleton.Root == null || table == null)
                return Fail("MeshSkinningTargetRequired", "Mesh skinning remap requires a final skeleton and bone table.", out diagnostic);
            Transform[] sourceBones = source.Source.Renderer.bones;
            if (!HasWeightedBone(source.Mesh))
            {
                remapped = ShapeSyncMeshCloneUtility.Clone(source.Mesh);
                remapped.name = source.Mesh.name + " (ShapeSync Final Skinning)";
                remapped.bindposes = table.Bindposes;
                return true;
            }
            if (sourceBones == null || sourceBones.Length == 0)
                return Fail("MeshBonesRequired", "Mesh skinning remap requires renderer bones.", out diagnostic, source.Source.LogicalName);

            if (HasInvalidWeightedIndex(source.Mesh, sourceBones.Length))
                return Fail("MeshBoneIndexInvalid", "A weighted Mesh bone index is outside renderer.bones.", out diagnostic, source.Source.LogicalName);

            bool[] used = GetUsedBoneIndices(source.Mesh, sourceBones.Length);
            int[] finalIndexBySourceIndex = new int[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                if (!used[i]) { finalIndexBySourceIndex[i] = 0; continue; }
                if (!TryResolveFinalBone(source.Source, sourceBones[i], skeleton.Root.transform, extraBoneTransforms, figureBoneTransforms, out Transform finalBone))
                    return Fail("MeshBoneResolveFailed", "A weighted source Mesh bone could not be resolved in the final skeleton.", out diagnostic, source.Source.LogicalName + ":" + i);
                int finalIndex = IndexOf(table.Bones, finalBone);
                if (finalIndex < 0)
                    return Fail("MeshBoneTableMissing", "A resolved source Mesh bone is absent from the final bone table.", out diagnostic, source.Source.LogicalName + ":" + finalBone.name);
                finalIndexBySourceIndex[i] = finalIndex;
            }

            Mesh clone = ShapeSyncMeshCloneUtility.Clone(source.Mesh);
            clone.name = source.Mesh.name + " (ShapeSync Final Skinning)";
            try
            {
                BoneWeight[] weights = clone.boneWeights;
                if (!TryBakeOutfitBindposeSpace(source, clone, weights, finalIndexBySourceIndex, skeleton, table, extraBoneTransforms, fbmWeights, out diagnostic))
                {
                    HumanoidMeshResourceCleanup.Destroy(clone);
                    return false;
                }
                for (int i = 0; i < weights.Length; i++) weights[i] = Remap(weights[i], finalIndexBySourceIndex);
                clone.boneWeights = weights;
                clone.bindposes = table.Bindposes;
                remapped = clone;
                return true;
            }
            catch
            {
                HumanoidMeshResourceCleanup.Destroy(clone);
                throw;
            }
        }

        private static bool TryBakeOutfitBindposeSpace(HumanoidMeshFbmBakedSource source, Mesh mesh, BoneWeight[] weights, int[] finalIndexBySourceIndex, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable table, IReadOnlyDictionary<Transform, Transform> extraBoneTransforms, IReadOnlyDictionary<string, float> fbmWeights, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            OutfitSkinningProfile skinning = source.Source.Outfit == null ? null : source.Source.Outfit.SkinningProfile;
            if (skinning == null) return true;
            string rendererPath = GetRelativePath(source.Source.Root == null ? null : source.Source.Root.transform, source.Source.Renderer == null ? null : source.Source.Renderer.transform);
            if (string.IsNullOrEmpty(rendererPath) || !skinning.TryGetRenderer(rendererPath, out OutfitSkinningRendererProfile profile) || profile == null)
                return Fail("OutfitBindposeProfileMissing", "An Outfit requires its source renderer bindpose profile during final skinning remap.", out diagnostic, source.Source.LogicalName);
            if (!TryResolveOutfitBindposes(source, profile, finalIndexBySourceIndex, skeleton, table, extraBoneTransforms, fbmWeights, skinning.UsesBcpBakedBindposes, out Matrix4x4[] sourceBindposes))
                return Fail("OutfitBindposeProfileInvalid", "An Outfit bindpose profile could not be resolved for the current FBM state.", out diagnostic, source.Source.LogicalName);

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            bool hasNormals = normals.Length == vertices.Length;
            bool hasTangents = tangents.Length == vertices.Length;
            var correction = new Matrix4x4[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                if (!TryCreateCorrection(weights[vertex], finalIndexBySourceIndex, table, sourceBindposes, out Matrix4x4 value, out string correctionError))
                    return Fail("OutfitBindposeCorrectionInvalid", "An Outfit vertex could not be normalized into the shared Figure bindpose table.", out diagnostic, source.Source.LogicalName + ":" + vertex + "; " + correctionError);
                correction[vertex] = value;
                vertices[vertex] = value.MultiplyPoint3x4(vertices[vertex]);
                if (hasNormals) normals[vertex] = TransformNormal(value, normals[vertex]);
                if (hasTangents)
                {
                    Vector4 tangent = tangents[vertex];
                    Vector3 xyz = value.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z));
                    if (xyz.sqrMagnitude > Mathf.Epsilon) xyz.Normalize();
                    tangents[vertex] = new Vector4(xyz.x, xyz.y, xyz.z, tangent.w);
                }
            }
            mesh.vertices = vertices;
            if (hasNormals) mesh.normals = normals;
            if (hasTangents) mesh.tangents = tangents;
            if (!TryTransformBlendShapes(mesh, correction, out diagnostic)) return false;
            mesh.RecalculateBounds();
            return true;
        }

        private static bool TryResolveOutfitBindposes(HumanoidMeshFbmBakedSource source, OutfitSkinningRendererProfile profile, int[] finalIndexBySourceIndex, HumanoidMeshSkeletonEscrow skeleton, HumanoidMeshBoneTable table, IReadOnlyDictionary<Transform, Transform> extraBoneTransforms, IReadOnlyDictionary<string, float> weights, bool applyFbmBindposes, out Matrix4x4[] resolved)
        {
            resolved = null;
            int bindposeCount = finalIndexBySourceIndex.Length;
            if (profile.baseBindposes == null || profile.baseBindposes.Length != bindposeCount) return false;
            resolved = new Matrix4x4[bindposeCount];
            for (int index = 0; index < bindposeCount; index++)
            {
                if (!TryDecompose(profile.baseBindposes[index], out BindposeTrs baseValue)) return false;
                Vector3 position = baseValue.Position;
                Vector3 scale = baseValue.Scale;
                Quaternion rotation = baseValue.Rotation;
                Transform sourceBone = source.Source.Renderer == null || source.Source.Renderer.bones == null || index >= source.Source.Renderer.bones.Length ? null : source.Source.Renderer.bones[index];
                bool useFbmProfile = applyFbmBindposes || (sourceBone != null && extraBoneTransforms != null && extraBoneTransforms.ContainsKey(sourceBone));
                if (useFbmProfile && profile.fbmBindposes != null)
                {
                    for (int targetIndex = 0; targetIndex < profile.fbmBindposes.Count; targetIndex++)
                    {
                        OutfitSkinningFbmBindposes target = profile.fbmBindposes[targetIndex];
                        if (target == null || string.IsNullOrEmpty(target.blendName) || target.bindposes == null || target.bindposes.Length != bindposeCount
                            || weights == null || !weights.TryGetValue(target.blendName, out float weight) || !float.IsFinite(weight)) continue;
                        if (!TryDecompose(target.bindposes[index], out BindposeTrs targetValue)) return false;
                        position += (targetValue.Position - baseValue.Position) * weight;
                        scale += (targetValue.Scale - baseValue.Scale) * weight;
                        rotation = Quaternion.SlerpUnclamped(Quaternion.identity, targetValue.Rotation * Quaternion.Inverse(baseValue.Rotation), weight) * rotation;
                    }
                }
                int finalIndex = finalIndexBySourceIndex[index];
                if (finalIndex < 0 || finalIndex >= table.Bones.Length) return false;
                resolved[index] = Matrix4x4.TRS(position, Normalize(rotation), scale);
                // OutfitAttacher aligns Figure-owned bones to the current Figure skinning
                // table. Retained Extra Bones keep their profile bindpose and are converted
                // to the final table space by the vertex correction below.
                if (!applyFbmBindposes &&
                    (sourceBone == null || extraBoneTransforms == null || !extraBoneTransforms.ContainsKey(sourceBone)))
                    resolved[index] = table.Bindposes[finalIndex];
            }
            return true;
        }

        private static bool TryCreateCorrection(BoneWeight weight, int[] finalIndexBySourceIndex, HumanoidMeshBoneTable table, Matrix4x4[] sourceBindposes, out Matrix4x4 correction, out string error)
        {
            Matrix4x4 source = Matrix4x4.zero;
            Matrix4x4 destination = Matrix4x4.zero;
            string reason = null;
            if (!Add(weight.boneIndex0, weight.weight0) || !Add(weight.boneIndex1, weight.weight1) || !Add(weight.boneIndex2, weight.weight2) || !Add(weight.boneIndex3, weight.weight3)) { correction = default; error = reason; return false; }
            if (Mathf.Abs(destination.determinant) <= 0.0000001f) { correction = default; error = "destination matrix is singular: " + destination; return false; }
            correction = destination.inverse * source;
            error = null;
            return true;

            bool Add(int sourceIndex, float value)
            {
                if (value <= 0f) return true;
                if (sourceIndex < 0 || sourceIndex >= finalIndexBySourceIndex.Length || sourceIndex >= sourceBindposes.Length) { reason = "source bindpose index is outside the weighted renderer"; return false; }
                int finalIndex = finalIndexBySourceIndex[sourceIndex];
                if (finalIndex < 0 || finalIndex >= table.Bones.Length || finalIndex >= table.Bindposes.Length || table.Bones[finalIndex] == null) { reason = "resolved final bone is outside the shared table"; return false; }
                source = AddWeighted(source, table.Bones[finalIndex].localToWorldMatrix * sourceBindposes[sourceIndex], value);
                destination = AddWeighted(destination, table.Bones[finalIndex].localToWorldMatrix * table.Bindposes[finalIndex], value);
                return true;
            }
        }

        private static Matrix4x4 AddWeighted(Matrix4x4 destination, Matrix4x4 value, float weight)
        {
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) destination[row, column] += value[row, column] * weight;
            return destination;
        }

        private static Vector3 TransformNormal(Matrix4x4 correction, Vector3 normal)
        {
            Matrix4x4 normalMatrix = correction.inverse.transpose;
            Vector3 value = normalMatrix.MultiplyVector(normal);
            return value.sqrMagnitude > Mathf.Epsilon ? value.normalized : value;
        }

        private static bool TryTransformBlendShapes(Mesh mesh, Matrix4x4[] correction, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            int vertexCount = mesh.vertexCount;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector3[vertexCount];
            var frames = new List<(string Name, float Weight, Vector3[] Vertices, Vector3[] Normals, Vector3[] Tangents)>();
            try
            {
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    string name = mesh.GetBlendShapeName(shape);
                    for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                    {
                        mesh.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                        var transformedVertices = new Vector3[vertexCount];
                        var transformedNormals = new Vector3[vertexCount];
                        var transformedTangents = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            transformedVertices[i] = correction[i].MultiplyVector(vertices[i]);
                            transformedNormals[i] = TransformNormal(correction[i], normals[i]);
                            transformedTangents[i] = correction[i].MultiplyVector(tangents[i]);
                        }
                        frames.Add((name, mesh.GetBlendShapeFrameWeight(shape, frame), transformedVertices, transformedNormals, transformedTangents));
                    }
                }
                if (frames.Count == 0) return true;
                mesh.ClearBlendShapes();
                for (int i = 0; i < frames.Count; i++) mesh.AddBlendShapeFrame(frames[i].Name, frames[i].Weight, frames[i].Vertices, frames[i].Normals, frames[i].Tangents);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", "BcpBlendShapeTransformFailed", "A BCP-baked Outfit BlendShape could not be normalized into the shared Figure bindpose table.", detail: exception.Message);
                return false;
            }
        }

        private readonly struct BindposeTrs
        {
            public BindposeTrs(Vector3 position, Quaternion rotation, Vector3 scale) { Position = position; Rotation = rotation; Scale = scale; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
        }

        private static bool TryDecompose(Matrix4x4 matrix, out BindposeTrs value)
        {
            Vector3 right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
            Vector3 scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
            if (scale.x <= Mathf.Epsilon || scale.y <= Mathf.Epsilon || scale.z <= Mathf.Epsilon || forward.sqrMagnitude <= Mathf.Epsilon || up.sqrMagnitude <= Mathf.Epsilon) { value = default; return false; }
            right /= scale.x; up /= scale.y; forward /= scale.z;
            value = new BindposeTrs(new Vector3(matrix.m03, matrix.m13, matrix.m23), Quaternion.LookRotation(forward, up), scale);
            return true;
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float squared = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return squared > Mathf.Epsilon ? Quaternion.Normalize(value) : Quaternion.identity;
        }

        private static bool TryResolveFinalBone(HumanoidMeshSource source, Transform sourceBone, Transform skeletonRoot, IReadOnlyDictionary<Transform, Transform> extraBoneTransforms, IReadOnlyDictionary<Transform, Transform> figureBoneTransforms, out Transform finalBone)
        {
            finalBone = null;
            if (sourceBone == null) return false;
            if (extraBoneTransforms != null && extraBoneTransforms.TryGetValue(sourceBone, out finalBone) && finalBone != null) return true;
            if (figureBoneTransforms != null && figureBoneTransforms.TryGetValue(sourceBone, out finalBone) && finalBone != null) return true;
            string path = null;
            if (source.WeightedBonePaths != null && !source.WeightedBonePaths.TryGetValue(sourceBone, out path)) return false;
            if (source.WeightedBonePaths == null) path = GetRelativePath(source.Root == null ? null : source.Root.transform, sourceBone);
            if (path != null)
            {
                finalBone = string.IsNullOrEmpty(path) ? skeletonRoot : skeletonRoot.Find(path);
                if (finalBone != null) return true;
            }
            return false;
        }

        private static BoneWeight Remap(BoneWeight value, int[] map)
        {
            value.boneIndex0 = RemapIndex(value.boneIndex0, value.weight0, map);
            value.boneIndex1 = RemapIndex(value.boneIndex1, value.weight1, map);
            value.boneIndex2 = RemapIndex(value.boneIndex2, value.weight2, map);
            value.boneIndex3 = RemapIndex(value.boneIndex3, value.weight3, map);
            return value;
        }

        private static int RemapIndex(int index, float weight, int[] map)
        {
            if (weight <= 0f) return 0;
            return map[index];
        }

        private static bool[] GetUsedBoneIndices(Mesh mesh, int count)
        {
            var used = new bool[count];
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                Mark(weights[i].boneIndex0, weights[i].weight0); Mark(weights[i].boneIndex1, weights[i].weight1);
                Mark(weights[i].boneIndex2, weights[i].weight2); Mark(weights[i].boneIndex3, weights[i].weight3);
            }
            return used;
            void Mark(int index, float weight) { if (weight > 0f && index >= 0 && index < used.Length) used[index] = true; }
        }

        private static bool HasWeightedBone(Mesh mesh)
        {
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i].weight0 > 0f || weights[i].weight1 > 0f || weights[i].weight2 > 0f || weights[i].weight3 > 0f) return true;
            }
            return false;
        }

        private static bool HasInvalidWeightedIndex(Mesh mesh, int boneCount)
        {
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                if (Invalid(weights[i].boneIndex0, weights[i].weight0) || Invalid(weights[i].boneIndex1, weights[i].weight1)
                    || Invalid(weights[i].boneIndex2, weights[i].weight2) || Invalid(weights[i].boneIndex3, weights[i].weight3)) return true;
            }
            return false;
            bool Invalid(int index, float weight) => weight > 0f && (index < 0 || index >= boneCount);
        }

        private static int IndexOf(Transform[] bones, Transform value)
        {
            for (int i = 0; i < bones.Length; i++) if (bones[i] == value) return i;
            return -1;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var names = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent) names.Add(current.name);
            if (target.parent == null || (names.Count == 0)) return null;
            Transform cursor = target;
            while (cursor != null && cursor != root) cursor = cursor.parent;
            if (cursor != root) return null;
            names.Reverse();
            return string.Join("/", names);
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", code, message, detail: detail);
            return false;
        }
    }
}
