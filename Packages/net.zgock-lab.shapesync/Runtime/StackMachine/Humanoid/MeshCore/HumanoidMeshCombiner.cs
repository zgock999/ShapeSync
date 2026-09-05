// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    public readonly struct HumanoidMeshCombineSource
    {
        public HumanoidMeshCombineSource(Mesh mesh, Matrix4x4 sourceToOutput) { Mesh = mesh; SourceToOutput = sourceToOutput; }
        public Mesh Mesh { get; }
        public Matrix4x4 SourceToOutput { get; }
    }

    /// <summary>Combines final-table-remapped Mesh candidates without using renderer state or runtime transactions.</summary>
    public static class HumanoidMeshCombiner
    {
        public static bool TryCombine(IReadOnlyList<Mesh> sources, HumanoidMeshBoneTable boneTable, out Mesh combined, out int[] firstSubmeshBySource, out StackMachineDiagnostic diagnostic)
        {
            if (sources == null)
            {
                combined = null;
                firstSubmeshBySource = null;
                return Fail("MeshCombineSourcesRequired", "Mesh combine requires at least the Figure candidate Mesh.", out diagnostic);
            }
            var identitySources = new HumanoidMeshCombineSource[sources.Count];
            for (int i = 0; i < sources.Count; i++) identitySources[i] = new HumanoidMeshCombineSource(sources[i], Matrix4x4.identity);
            return TryCombine(identitySources, boneTable, out combined, out firstSubmeshBySource, out diagnostic);
        }

        public static bool TryCombine(IReadOnlyList<HumanoidMeshCombineSource> sources, HumanoidMeshBoneTable boneTable, out Mesh combined, out int[] firstSubmeshBySource, out StackMachineDiagnostic diagnostic)
        {
            combined = null;
            firstSubmeshBySource = null;
            diagnostic = null;
            if (sources == null || sources.Count == 0) return Fail("MeshCombineSourcesRequired", "Mesh combine requires at least the Figure candidate Mesh.", out diagnostic);
            if (boneTable == null) return Fail("FigureBoneTableRequired", "Mesh combine requires the final bone table.", out diagnostic);

            int vertexCount = 0;
            int submeshCount = 0;
            bool hasNormals = false, hasTangents = false, hasColors = false, requiresUInt32 = false;
            var hasUv = new bool[8];
            firstSubmeshBySource = new int[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                Mesh source = sources[i].Mesh;
                if (source == null || source.vertexCount == 0) return Fail("MeshCombineSourceInvalid", "Mesh combine source is null or has no vertices.", out diagnostic, i.ToString());
                if (source.boneWeights != null && source.boneWeights.Length != 0 && source.boneWeights.Length != source.vertexCount) return Fail("MeshCombineSkinningInvalid", "Mesh combine source BoneWeight array must be empty or have one entry per vertex.", out diagnostic, i.ToString());
                firstSubmeshBySource[i] = submeshCount;
                vertexCount += source.vertexCount;
                submeshCount += source.subMeshCount;
                hasNormals |= source.normals.Length == source.vertexCount;
                hasTangents |= source.tangents.Length == source.vertexCount;
                hasColors |= source.colors32.Length == source.vertexCount;
                requiresUInt32 |= source.indexFormat == IndexFormat.UInt32;
                for (int channel = 0; channel < hasUv.Length; channel++) hasUv[channel] |= GetUv(source, channel).Count == source.vertexCount;
            }

            var vertices = new Vector3[vertexCount];
            var boneWeights = new BoneWeight[vertexCount];
            var normals = hasNormals ? new Vector3[vertexCount] : null;
            var tangents = hasTangents ? new Vector4[vertexCount] : null;
            var colors = hasColors ? new Color32[vertexCount] : null;
            var uvs = new Vector4[8][];
            for (int channel = 0; channel < uvs.Length; channel++) if (hasUv[channel]) uvs[channel] = new Vector4[vertexCount];
            int vertexOffset = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                HumanoidMeshCombineSource combineSource = sources[i];
                Mesh source = combineSource.Mesh;
                int count = source.vertexCount;
                CopyPositions(source.vertices, vertices, vertexOffset, combineSource.SourceToOutput);
                if (source.boneWeights != null && source.boneWeights.Length == count) Array.Copy(source.boneWeights, 0, boneWeights, vertexOffset, count);
                CopyNormals(source.normals, normals, vertexOffset, combineSource.SourceToOutput);
                CopyTangents(source.tangents, tangents, vertexOffset, combineSource.SourceToOutput);
                CopyIfComplete(source.colors32, colors, vertexOffset, count);
                for (int channel = 0; channel < uvs.Length; channel++) CopyIfComplete(GetUv(source, channel), uvs[channel], vertexOffset, count);
                vertexOffset += count;
            }

            var output = new Mesh { name = "ShapeSync Final Humanoid Mesh" };
            try
            {
                output.indexFormat = requiresUInt32 || vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
                output.vertices = vertices;
                output.boneWeights = boneWeights;
                output.bindposes = boneTable.Bindposes;
                if (normals != null) output.normals = normals;
                if (tangents != null) output.tangents = tangents;
                if (colors != null) output.colors32 = colors;
                for (int channel = 0; channel < uvs.Length; channel++) if (uvs[channel] != null) output.SetUVs(channel, new List<Vector4>(uvs[channel]));
                output.subMeshCount = submeshCount;
                vertexOffset = 0;
                int outputSubmesh = 0;
                for (int i = 0; i < sources.Count; i++)
                {
                    HumanoidMeshCombineSource combineSource = sources[i];
                    Mesh source = combineSource.Mesh;
                    for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                    {
                        int[] triangles = source.GetTriangles(submesh);
                        for (int triangle = 0; triangle < triangles.Length; triangle++) triangles[triangle] += vertexOffset;
                        if (combineSource.SourceToOutput.determinant < 0f) ReverseWinding(triangles);
                        output.SetTriangles(triangles, outputSubmesh++, true);
                    }
                    vertexOffset += source.vertexCount;
                }
                if (!TryCopyBlendShapes(sources, output, vertexCount, out diagnostic))
                {
                    HumanoidMeshResourceCleanup.Destroy(output);
                    return false;
                }
                output.RecalculateBounds();
                combined = output;
                return true;
            }
            catch
            {
                HumanoidMeshResourceCleanup.Destroy(output);
                throw;
            }
        }

        private static bool TryCopyBlendShapes(IReadOnlyList<HumanoidMeshCombineSource> sources, Mesh output, int outputVertexCount, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var frames = new SortedDictionary<string, SortedDictionary<float, BlendShapeFrame>>(StringComparer.Ordinal);
            int offset = 0;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                HumanoidMeshCombineSource combineSource = sources[sourceIndex];
                Mesh source = combineSource.Mesh;
                for (int shape = 0; shape < source.blendShapeCount; shape++)
                {
                    string name = source.GetBlendShapeName(shape);
                    if (!frames.TryGetValue(name, out SortedDictionary<float, BlendShapeFrame> byWeight))
                    {
                        byWeight = new SortedDictionary<float, BlendShapeFrame>();
                        frames.Add(name, byWeight);
                    }
                    for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                    {
                        float weight = source.GetBlendShapeFrameWeight(shape, frame);
                        if (!byWeight.TryGetValue(weight, out BlendShapeFrame target))
                        {
                            target = new BlendShapeFrame(outputVertexCount);
                            byWeight.Add(weight, target);
                        }
                        else if (target.Contributors.Contains(sourceIndex))
                        {
                            diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", "BlendShapeFrameWeightDuplicate", "One source Mesh contains duplicate BlendShape frame weights that cannot be represented in the final merged Mesh.", detail: name + ":" + weight);
                            return false;
                        }
                        target.Contributors.Add(sourceIndex);
                        var vertices = new Vector3[source.vertexCount];
                        var normals = new Vector3[source.vertexCount];
                        var tangents = new Vector3[source.vertexCount];
                        source.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                        CopyVectors(vertices, target.Vertices, offset, combineSource.SourceToOutput);
                        CopyNormalDeltas(normals, target.Normals, offset, combineSource.SourceToOutput);
                        CopyVectors(tangents, target.Tangents, offset, combineSource.SourceToOutput);
                    }
                }
                offset += source.vertexCount;
            }
            foreach (KeyValuePair<string, SortedDictionary<float, BlendShapeFrame>> shape in frames)
            foreach (KeyValuePair<float, BlendShapeFrame> frame in shape.Value)
                output.AddBlendShapeFrame(shape.Key, frame.Key, frame.Value.Vertices, frame.Value.Normals, frame.Value.Tangents);
            return true;
        }

        private sealed class BlendShapeFrame
        {
            public BlendShapeFrame(int vertexCount) { Vertices = new Vector3[vertexCount]; Normals = new Vector3[vertexCount]; Tangents = new Vector3[vertexCount]; }
            public Vector3[] Vertices { get; }
            public Vector3[] Normals { get; }
            public Vector3[] Tangents { get; }
            public HashSet<int> Contributors { get; } = new HashSet<int>();
        }

        private static List<Vector4> GetUv(Mesh mesh, int channel)
        {
            var result = new List<Vector4>();
            mesh.GetUVs(channel, result);
            return result;
        }

        private static void CopyPositions(IList<Vector3> source, Vector3[] destination, int offset, Matrix4x4 sourceToOutput)
        {
            if (destination == null || source == null || source.Count == 0) return;
            for (int i = 0; i < source.Count; i++) destination[offset + i] = sourceToOutput.MultiplyPoint3x4(source[i]);
        }

        private static void CopyNormals(IList<Vector3> source, Vector3[] destination, int offset, Matrix4x4 sourceToOutput)
        {
            if (destination == null || source == null || offset + source.Count > destination.Length) return;
            Matrix4x4 normalMatrix = sourceToOutput.inverse.transpose;
            for (int i = 0; i < source.Count; i++) destination[offset + i] = Normalize(normalMatrix.MultiplyVector(source[i]));
        }

        private static void CopyTangents(IList<Vector4> source, Vector4[] destination, int offset, Matrix4x4 sourceToOutput)
        {
            if (destination == null || source == null || offset + source.Count > destination.Length) return;
            float handedness = sourceToOutput.determinant < 0f ? -1f : 1f;
            for (int i = 0; i < source.Count; i++)
            {
                Vector4 value = source[i];
                Vector3 direction = Normalize(sourceToOutput.MultiplyVector(new Vector3(value.x, value.y, value.z)));
                destination[offset + i] = new Vector4(direction.x, direction.y, direction.z, value.w * handedness);
            }
        }

        private static void CopyNormalDeltas(IList<Vector3> source, Vector3[] destination, int offset, Matrix4x4 sourceToOutput)
        {
            Matrix4x4 normalMatrix = sourceToOutput.inverse.transpose;
            for (int i = 0; i < source.Count; i++) destination[offset + i] = normalMatrix.MultiplyVector(source[i]);
        }

        private static void CopyVectors(IList<Vector3> source, Vector3[] destination, int offset, Matrix4x4 sourceToOutput)
        {
            for (int i = 0; i < source.Count; i++) destination[offset + i] = sourceToOutput.MultiplyVector(source[i]);
        }

        private static Vector3 Normalize(Vector3 value) => value.sqrMagnitude > Mathf.Epsilon ? value.normalized : Vector3.zero;

        private static void ReverseWinding(int[] triangles)
        {
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int temp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temp;
            }
        }

        private static void CopyIfComplete<T>(IList<T> source, T[] destination, int offset, int count)
        {
            if (destination != null && source != null && source.Count == count) for (int i = 0; i < count; i++) destination[offset + i] = source[i];
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", code, message, detail: detail);
            return false;
        }
    }
}
