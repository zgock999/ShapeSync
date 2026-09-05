// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>UnityEngine-only BlendShape operations shared by EditMode compilation and future runtime bake backends.</summary>
    public static class HumanoidMeshBlendShapeUtility
    {
        /// <summary>Squared delta threshold below which a vertex is emitted as invariant.</summary>
        public const float InvariantDeltaEpsilon = 0.000001f;

        public static bool TryGetDeltaAtUnityWeight(Mesh mesh, int blendShapeIndex, float unityWeight, out Vector3[] vertices, out Vector3[] normals, out Vector3[] tangents)
        {
            vertices = null;
            normals = null;
            tangents = null;
            if (mesh == null || blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount) return false;
            int frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameCount <= 0) return false;
            int frameIndex = frameCount - 1;
            float frameWeight = mesh.GetBlendShapeFrameWeight(blendShapeIndex, frameIndex);
            if (Mathf.Abs(frameWeight) <= Mathf.Epsilon) return false;
            vertices = new Vector3[mesh.vertexCount];
            normals = new Vector3[mesh.vertexCount];
            tangents = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(blendShapeIndex, frameIndex, vertices, normals, tangents);
            float scale = unityWeight / frameWeight;
            Scale(vertices, scale);
            Scale(normals, scale);
            Scale(tangents, scale);
            return true;
        }

        public static bool TryBuildDifference(Mesh source, Mesh target, out Vector3[] vertices, out Vector3[] normals, out Vector3[] tangents)
        {
            vertices = null;
            normals = null;
            tangents = null;
            if (source == null || target == null || source.vertexCount != target.vertexCount) return false;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] targetVertices = target.vertices;
            vertices = new Vector3[sourceVertices.Length];
            Vector3[] sourceNormals = source.normals;
            Vector3[] targetNormals = target.normals;
            if (sourceNormals.Length == sourceVertices.Length && targetNormals.Length == targetVertices.Length) normals = new Vector3[sourceVertices.Length];
            Vector4[] sourceTangents = source.tangents;
            Vector4[] targetTangents = target.tangents;
            if (sourceTangents.Length == sourceVertices.Length && targetTangents.Length == targetVertices.Length) tangents = new Vector3[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                vertices[i] = targetVertices[i] - sourceVertices[i];
                if (vertices[i].sqrMagnitude <= InvariantDeltaEpsilon) vertices[i] = Vector3.zero;
                if (normals != null)
                {
                    normals[i] = targetNormals[i] - sourceNormals[i];
                    if (normals[i].sqrMagnitude <= InvariantDeltaEpsilon) normals[i] = Vector3.zero;
                }
                if (tangents != null)
                {
                    Vector4 delta = targetTangents[i] - sourceTangents[i];
                    tangents[i] = new Vector3(delta.x, delta.y, delta.z);
                    if (tangents[i].sqrMagnitude <= InvariantDeltaEpsilon) tangents[i] = Vector3.zero;
                }
            }
            return true;
        }

        public static void AddFrameOrThrow(Mesh mesh, string name, Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (mesh.GetBlendShapeIndex(name) >= 0) throw new InvalidOperationException("BlendShape name collision: " + name);
            mesh.AddBlendShapeFrame(name, 100f, vertices, normals, tangents);
        }

        private static void Scale(Vector3[] values, float scale)
        {
            for (int i = 0; i < values.Length; i++) values[i] *= scale;
        }
    }
}
