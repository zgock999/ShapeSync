// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Computes the authoring-time geometry signature used to verify that a PCM payload matches its Figure mesh.
    /// </summary>
    public static class MeshGeometrySignature
    {
        // Authoring-time signature for the immutable base geometry carried by a PCM payload.
        // Runtime only compares the serialized value, so attach does not request mesh arrays.
        public static ulong Calculate(Mesh mesh)
        {
            if (mesh == null) return 0UL;
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Mix(ref hash, mesh.vertexCount);
                Mix(ref hash, mesh.subMeshCount);
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                for (int i = 0; i < vertices.Length; i++) Mix(ref hash, vertices[i]);
                for (int i = 0; i < normals.Length; i++) Mix(ref hash, normals[i]);
                for (int i = 0; i < tangents.Length; i++) Mix(ref hash, tangents[i]);
                return hash;
            }
        }

        private static void Mix(ref ulong hash, int value) => Mix(ref hash, unchecked((uint)value));
        private static void Mix(ref ulong hash, float value) => Mix(ref hash, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        private static void Mix(ref ulong hash, Vector3 value) { Mix(ref hash, value.x); Mix(ref hash, value.y); Mix(ref hash, value.z); }
        private static void Mix(ref ulong hash, Vector4 value) { Mix(ref hash, value.x); Mix(ref hash, value.y); Mix(ref hash, value.z); Mix(ref hash, value.w); }
        private static void Mix(ref ulong hash, uint value) { hash ^= value; hash *= 1099511628211UL; }
    }
}
