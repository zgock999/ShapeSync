// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Provides editor-only utilities for baking and inspecting BlendShape data.</summary>
    public static class BlendShapeBakeUtility
    {
        /// <summary>Squared delta threshold below which a vertex is treated as exactly invariant.</summary>
        public const float InvariantDeltaEpsilon = 0.000001f;
        public static bool TryBuildMeshDifference(Mesh sourceMesh, Mesh targetMesh, out Vector3[] deltaVertices, out Vector3[] deltaNormals, out Vector3[] deltaTangents)
        {
            deltaVertices = null;
            deltaNormals = null;
            deltaTangents = null;

            if (sourceMesh == null || targetMesh == null || sourceMesh.vertexCount != targetMesh.vertexCount)
            {
                return false;
            }

            Vector3[] sourceVertices = sourceMesh.vertices;
            Vector3[] targetVertices = targetMesh.vertices;
            deltaVertices = new Vector3[sourceVertices.Length];

            Vector3[] sourceNormals = sourceMesh.normals;
            Vector3[] targetNormals = targetMesh.normals;
            if (sourceNormals.Length == sourceVertices.Length && targetNormals.Length == targetVertices.Length)
            {
                deltaNormals = new Vector3[sourceVertices.Length];
            }

            Vector4[] sourceTangents = sourceMesh.tangents;
            Vector4[] targetTangents = targetMesh.tangents;
            if (sourceTangents.Length == sourceVertices.Length && targetTangents.Length == targetVertices.Length)
            {
                deltaTangents = new Vector3[sourceVertices.Length];
            }

            for (int i = 0; i < sourceVertices.Length; i++)
            {
                deltaVertices[i] = targetVertices[i] - sourceVertices[i];
                if (deltaVertices[i].sqrMagnitude <= InvariantDeltaEpsilon) deltaVertices[i] = Vector3.zero;
                if (deltaNormals != null)
                {
                    deltaNormals[i] = targetNormals[i] - sourceNormals[i];
                    if (deltaNormals[i].sqrMagnitude <= InvariantDeltaEpsilon) deltaNormals[i] = Vector3.zero;
                }

                if (deltaTangents != null)
                {
                    Vector4 delta = targetTangents[i] - sourceTangents[i];
                    deltaTangents[i] = new Vector3(delta.x, delta.y, delta.z);
                    if (deltaTangents[i].sqrMagnitude <= InvariantDeltaEpsilon) deltaTangents[i] = Vector3.zero;
                }
            }

            return true;
        }

        public static bool TryGetBlendShapeDeltaAtUnityWeight(Mesh mesh, int blendShapeIndex, float unityWeight, out Vector3[] deltaVertices, out Vector3[] deltaNormals, out Vector3[] deltaTangents)
        {
            deltaVertices = null;
            deltaNormals = null;
            deltaTangents = null;

            if (mesh == null || blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount)
            {
                return false;
            }

            int frameCount = mesh.GetBlendShapeFrameCount(blendShapeIndex);
            if (frameCount <= 0)
            {
                return false;
            }

            int frameIndex = frameCount - 1;
            float frameWeight = mesh.GetBlendShapeFrameWeight(blendShapeIndex, frameIndex);
            if (Mathf.Abs(frameWeight) <= Mathf.Epsilon)
            {
                return false;
            }

            deltaVertices = new Vector3[mesh.vertexCount];
            deltaNormals = new Vector3[mesh.vertexCount];
            deltaTangents = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(blendShapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

            float scale = unityWeight / frameWeight;
            Scale(deltaVertices, scale);
            Scale(deltaNormals, scale);
            Scale(deltaTangents, scale);
            return true;
        }

        public static void AddScaled(Vector3[] destination, Vector3[] source, float scale)
        {
            if (destination == null || source == null)
            {
                return;
            }

            int count = Mathf.Min(destination.Length, source.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] += source[i] * scale;
            }
        }

        public static Vector3[] CreateZeroDelta(int vertexCount)
        {
            return new Vector3[vertexCount];
        }

        public static Vector3[] Subtract(Vector3[] from, Vector3[] subtract)
        {
            if (from == null)
            {
                return null;
            }

            Vector3[] result = new Vector3[from.Length];
            for (int i = 0; i < from.Length; i++)
            {
                Vector3 value = from[i];
                if (subtract != null && i < subtract.Length)
                {
                    value -= subtract[i];
                }

                result[i] = value;
            }

            return result;
        }

        public static void AddBlendShapeFrameOrThrow(Mesh mesh, string name, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
        {
            if (mesh.GetBlendShapeIndex(name) >= 0)
            {
                throw new System.InvalidOperationException($"BlendShape name collision: {name}");
            }

            mesh.AddBlendShapeFrame(name, 100f, deltaVertices, deltaNormals, deltaTangents);
        }

        private static void Scale(Vector3[] values, float scale)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i] *= scale;
            }
        }
    }
}
