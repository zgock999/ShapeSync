// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.Utilities
{
    /// <summary>
    /// Creates a mutable, non-persistent Mesh without using Object.Instantiate on the source.
    /// The MeshData path preserves the source vertex-buffer layout, including UV dimensions,
    /// color formats, variable skin weights, and submesh baseVertex values.
    /// </summary>
    internal static class ShapeSyncMeshCloneUtility
    {
        internal static Mesh Clone(Mesh source, bool copyBlendShapes = true)
        {
            if (source == null) return null;
            if (!source.isReadable) throw new InvalidOperationException("Mesh clone requires a readable source Mesh (Read/Write Enabled): " + source.name + ".");

            Mesh clone = new Mesh { name = source.name };
            Mesh.MeshDataArray readOnly = default;
            Mesh.MeshDataArray writable = default;
            bool writableNeedsDispose = false;
            try
            {
                readOnly = Mesh.AcquireReadOnlyMeshData(source);
                Mesh.MeshData sourceData = readOnly[0];
                writable = Mesh.AllocateWritableMeshData(1);
                writableNeedsDispose = true;
                Mesh.MeshData destination = writable[0];

                destination.SetVertexBufferParams(sourceData.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < sourceData.vertexBufferCount; stream++)
                {
                    var sourceBytes = sourceData.GetVertexData<byte>(stream);
                    var destinationBytes = destination.GetVertexData<byte>(stream);
                    if (sourceBytes.Length != destinationBytes.Length)
                        throw new InvalidOperationException("Mesh vertex buffer size changed while cloning stream " + stream + ".");
                    sourceBytes.CopyTo(destinationBytes);
                }

                var sourceIndices = sourceData.GetIndexData<byte>();
                int bytesPerIndex = sourceData.indexFormat == IndexFormat.UInt16 ? 2 : 4;
                if (sourceData.indexFormat != IndexFormat.UInt16 && sourceData.indexFormat != IndexFormat.UInt32)
                    throw new InvalidOperationException("Unsupported Mesh index format: " + sourceData.indexFormat + ".");
                destination.SetIndexBufferParams(sourceIndices.Length / bytesPerIndex, sourceData.indexFormat);
                var destinationIndices = destination.GetIndexData<byte>();
                if (sourceIndices.Length != destinationIndices.Length)
                    throw new InvalidOperationException("Mesh index buffer size changed while cloning.");
                sourceIndices.CopyTo(destinationIndices);

                destination.subMeshCount = sourceData.subMeshCount;
                for (int submesh = 0; submesh < sourceData.subMeshCount; submesh++)
                    destination.SetSubMesh(submesh, sourceData.GetSubMesh(submesh), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

                readOnly.Dispose();
                readOnly = default;
                Mesh.ApplyAndDisposeWritableMeshData(writable, clone, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                writable = default;
                writableNeedsDispose = false;
                clone.bounds = source.bounds;
                clone.bindposes = source.bindposes;
                if (copyBlendShapes) CopyBlendShapes(source, clone);
                return clone;
            }
            catch
            {
                if (writableNeedsDispose) writable.Dispose();
                readOnly.Dispose();
                DestroyClone(clone);
                throw;
            }
        }

        private static void CopyBlendShapes(Mesh source, Mesh destination)
        {
            var vertices = new Vector3[source.vertexCount];
            var normals = new Vector3[source.vertexCount];
            var tangents = new Vector3[source.vertexCount];
            for (int blendShape = 0; blendShape < source.blendShapeCount; blendShape++)
            {
                string blendShapeName = source.GetBlendShapeName(blendShape);
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(blendShape); frame++)
                {
                    source.GetBlendShapeFrameVertices(blendShape, frame, vertices, normals, tangents);
                    destination.AddBlendShapeFrame(blendShapeName, source.GetBlendShapeFrameWeight(blendShape, frame), vertices, normals, tangents);
                }
            }
        }

        private static void DestroyClone(Mesh clone)
        {
            if (clone == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(clone);
            else UnityEngine.Object.DestroyImmediate(clone);
        }
    }
}
