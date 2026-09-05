// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Non-UI authoring contracts retained by Database Generate. The legacy
    /// Builder windows are debug-only; these small pure operations remain in
    /// the normal Editor assembly because Generate and contract tests use them.
    /// </summary>
    internal static class ShapeSyncLegacyBuilderContracts
    {
        internal static int NormalizePcmSlotCount(double requestedSlots)
        {
            if (double.IsNaN(requestedSlots)
                || double.IsInfinity(requestedSlots)
                || requestedSlots < 0d
                || requestedSlots > int.MaxValue
                || requestedSlots != Math.Truncate(requestedSlots))
            {
                return 0;
            }

            return (int)requestedSlots;
        }

        internal static int NormalizePcmSlotCount(string requestedSlots)
        {
            return double.TryParse(requestedSlots, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? NormalizePcmSlotCount(parsed)
                : 0;
        }

        internal static void AddReservedPcmSlots(Mesh mesh, int vertexCount, int slots, int fbmCount)
        {
            Vector3[] zeroVertices = new Vector3[vertexCount];
            Vector3[] zeroNormals = new Vector3[vertexCount];
            Vector3[] zeroTangents = new Vector3[vertexCount];
            int count = slots * (fbmCount + 1);
            for (int i = 0; i < count; i++)
                mesh.AddBlendShapeFrame(BlendShapeReservedPrefixes.MorphSlot + i, 100f, zeroVertices, zeroNormals, zeroTangents);
        }

        internal static Mesh CreateMeshWithoutBlendShapes(Mesh source, IReadOnlyList<string> removedNames)
        {
            var removed = new HashSet<string>(removedNames, StringComparer.Ordinal);
            var result = new Mesh
            {
                name = source.name + " PBM Rebuilt",
                indexFormat = source.indexFormat,
                subMeshCount = source.subMeshCount
            };

            result.vertices = source.vertices;
            if (source.normals.Length == source.vertexCount) result.normals = source.normals;
            if (source.tangents.Length == source.vertexCount) result.tangents = source.tangents;
            if (source.colors32.Length == source.vertexCount) result.colors32 = source.colors32;
            result.bindposes = source.bindposes;
            result.boneWeights = source.boneWeights;
            for (int channel = 0; channel < 8; channel++)
            {
                var values = new List<Vector4>();
                source.GetUVs(channel, values);
                if (values.Count > 0) result.SetUVs(channel, values);
            }

            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
                result.SetTriangles(source.GetTriangles(subMesh), subMesh, false);

            for (int blendShape = 0; blendShape < source.blendShapeCount; blendShape++)
            {
                string name = source.GetBlendShapeName(blendShape);
                if (removed.Contains(name)) continue;
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(blendShape); frame++)
                {
                    var vertices = new Vector3[source.vertexCount];
                    var normals = new Vector3[source.vertexCount];
                    var tangents = new Vector3[source.vertexCount];
                    source.GetBlendShapeFrameVertices(blendShape, frame, vertices, normals, tangents);
                    result.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(blendShape, frame), vertices, normals, tangents);
                }
            }

            result.bounds = source.bounds;
            return result;
        }

        internal static bool IsPositionOnlyHipsCorrection(ShapeSyncHumanoidBoneCorrection correction)
        {
            const float epsilon = 0.000001f;
            if (correction.bone != HumanBodyBones.Hips) return false;
            if (correction.localPositionDelta.sqrMagnitude <= epsilon * epsilon) return false;
            Quaternion rotation = NormalizeQuaternion(correction.localRotationDelta);
            bool hasRotation = Mathf.Abs(rotation.x) > epsilon || Mathf.Abs(rotation.y) > epsilon
                || Mathf.Abs(rotation.z) > epsilon || Mathf.Abs(rotation.w - 1f) > epsilon;
            return !hasRotation && correction.localScaleDelta.sqrMagnitude <= epsilon * epsilon;
        }

        private static Quaternion NormalizeQuaternion(Quaternion value)
        {
            float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return magnitude > 0.000001f ? new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude) : Quaternion.identity;
        }
    }
}
#endif
