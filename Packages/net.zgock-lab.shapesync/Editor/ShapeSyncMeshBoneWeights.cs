// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Captures Unity's variable-influence skinning representation and remaps its
    /// per-vertex weight blocks without reducing them to the legacy four-influence
    /// <see cref="Mesh.boneWeights"/> representation.
    /// </summary>
    internal sealed class ShapeSyncMeshBoneWeights
    {
        internal readonly byte[] BonesPerVertex;
        internal readonly BoneWeight1[] Weights;

        private ShapeSyncMeshBoneWeights(byte[] bonesPerVertex, BoneWeight1[] weights)
        {
            BonesPerVertex = bonesPerVertex;
            Weights = weights;
        }

        internal static ShapeSyncMeshBoneWeights Capture(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            NativeArray<byte> sourceBonesPerVertex = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> sourceWeights = mesh.GetAllBoneWeights();
            if (sourceBonesPerVertex.Length == 0)
            {
                if (sourceWeights.Length != 0)
                    throw new InvalidOperationException("Mesh returned BoneWeight1 data without a bones-per-vertex table.");
                return null;
            }

            if (sourceBonesPerVertex.Length != mesh.vertexCount)
                throw new InvalidOperationException("Mesh bones-per-vertex data does not contain one entry per vertex.");

            int totalWeightCount = 0;
            for (int vertex = 0; vertex < sourceBonesPerVertex.Length; vertex++)
                totalWeightCount += sourceBonesPerVertex[vertex];
            if (totalWeightCount != sourceWeights.Length)
                throw new InvalidOperationException("Mesh bones-per-vertex data does not match its BoneWeight1 data.");

            var bonesPerVertex = new byte[sourceBonesPerVertex.Length];
            var weights = new BoneWeight1[sourceWeights.Length];
            for (int vertex = 0; vertex < bonesPerVertex.Length; vertex++) bonesPerVertex[vertex] = sourceBonesPerVertex[vertex];
            for (int index = 0; index < weights.Length; index++) weights[index] = sourceWeights[index];
            return new ShapeSyncMeshBoneWeights(bonesPerVertex, weights);
        }

        internal ShapeSyncMeshBoneWeights Remap(IReadOnlyList<int> outputToSource)
        {
            if (outputToSource == null) throw new ArgumentNullException(nameof(outputToSource));
            int[] sourceOffsets = BuildOffsets(BonesPerVertex);
            var bonesPerVertex = new byte[outputToSource.Count];
            int totalWeightCount = 0;
            for (int outputVertex = 0; outputVertex < outputToSource.Count; outputVertex++)
            {
                int sourceVertex = outputToSource[outputVertex];
                ValidateSourceVertex(sourceVertex);
                bonesPerVertex[outputVertex] = BonesPerVertex[sourceVertex];
                totalWeightCount += bonesPerVertex[outputVertex];
            }

            var weights = new BoneWeight1[totalWeightCount];
            int destinationIndex = 0;
            for (int outputVertex = 0; outputVertex < outputToSource.Count; outputVertex++)
            {
                int sourceVertex = outputToSource[outputVertex];
                int sourceIndex = sourceOffsets[sourceVertex];
                int count = BonesPerVertex[sourceVertex];
                for (int influence = 0; influence < count; influence++)
                    weights[destinationIndex++] = Weights[sourceIndex + influence];
            }
            return new ShapeSyncMeshBoneWeights(bonesPerVertex, weights);
        }

        internal ShapeSyncMeshBoneWeights RemapBoneIndices(IReadOnlyList<int> sourceToOutput)
        {
            if (sourceToOutput == null) throw new ArgumentNullException(nameof(sourceToOutput));
            var weights = new BoneWeight1[Weights.Length];
            for (int index = 0; index < Weights.Length; index++)
            {
                BoneWeight1 source = Weights[index];
                if (source.boneIndex < 0 || source.boneIndex >= sourceToOutput.Count)
                    throw new InvalidOperationException("BoneWeight1 remap references an invalid source bone index.");
                source.boneIndex = sourceToOutput[source.boneIndex];
                if (source.boneIndex < 0)
                    throw new InvalidOperationException("BoneWeight1 remap does not assign a destination bone index.");
                weights[index] = source;
            }
            return new ShapeSyncMeshBoneWeights((byte[])BonesPerVertex.Clone(), weights);
        }

        internal ShapeSyncMeshBoneWeights RemapSourceToCompact(int[] sourceToOutput, int outputVertexCount)
        {
            if (sourceToOutput == null) throw new ArgumentNullException(nameof(sourceToOutput));
            var outputToSource = new int[outputVertexCount];
            for (int outputVertex = 0; outputVertex < outputToSource.Length; outputVertex++) outputToSource[outputVertex] = -1;
            for (int sourceVertex = 0; sourceVertex < sourceToOutput.Length; sourceVertex++)
            {
                int outputVertex = sourceToOutput[sourceVertex];
                if (outputVertex < 0) continue;
                if (outputVertex >= outputToSource.Length || outputToSource[outputVertex] >= 0)
                    throw new InvalidOperationException("Mesh compact remap is not one-to-one for BoneWeight1 data.");
                outputToSource[outputVertex] = sourceVertex;
            }
            for (int outputVertex = 0; outputVertex < outputToSource.Length; outputVertex++)
                if (outputToSource[outputVertex] < 0)
                    throw new InvalidOperationException("Mesh compact remap does not assign every output vertex for BoneWeight1 data.");
            return Remap(outputToSource);
        }

        internal void Apply(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            var bonesPerVertex = new NativeArray<byte>(BonesPerVertex, Allocator.Temp);
            var weights = new NativeArray<BoneWeight1>(Weights, Allocator.Temp);
            try
            {
                mesh.SetBoneWeights(bonesPerVertex, weights);
            }
            finally
            {
                bonesPerVertex.Dispose();
                weights.Dispose();
            }
        }

        private void ValidateSourceVertex(int sourceVertex)
        {
            if (sourceVertex < 0 || sourceVertex >= BonesPerVertex.Length)
                throw new InvalidOperationException("BoneWeight1 remap references an invalid source vertex.");
        }

        private static int[] BuildOffsets(byte[] bonesPerVertex)
        {
            var offsets = new int[bonesPerVertex.Length];
            int offset = 0;
            for (int vertex = 0; vertex < bonesPerVertex.Length; vertex++)
            {
                offsets[vertex] = offset;
                offset += bonesPerVertex[vertex];
            }
            return offsets;
        }
    }
}
