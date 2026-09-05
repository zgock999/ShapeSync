// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.Editor;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class BlendShapeGeneratorPcmSlotTests
    {
        [Test]
        public void NormalizePcmSlotCount_MapsInvalidValuesToZero()
        {
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(-1d), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(double.NaN), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(double.PositiveInfinity), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(double.NegativeInfinity), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount("NaN"), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount("Infinity"), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(3.5d), Is.Zero, "PCM Slots must remain an integer contract.");
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(0d), Is.Zero);
            Assert.That(ShapeSyncLegacyBuilderContracts.NormalizePcmSlotCount(10d), Is.EqualTo(10));
        }

        [Test]
        public void ReservedPcmSlots_UseExplicitZeroDeltaArrays()
        {
            const string folder = ShapeSyncTestAssetPaths.Spec10PcmSlotRoot;
            const string path = folder + "/Slots.asset";
            AssetDatabase.DeleteAsset(folder);
            ShapeSyncTestAssetPaths.EnsureConsumerTempRoot();
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/Generated"), "Spec10PcmSlotTest");
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            try
            {
                ShapeSyncLegacyBuilderContracts.AddReservedPcmSlots(mesh, mesh.vertexCount, 2, 1);
                AssetDatabase.CreateAsset(mesh, path);
                AssetDatabase.SaveAssets();
                mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Assert.That(mesh, Is.Not.Null, "The reserved-slot Mesh must survive asset serialization.");
                Assert.That(mesh.blendShapeCount, Is.EqualTo(4));
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    Assert.That(mesh.GetBlendShapeName(i), Is.EqualTo("Morph_Slot_" + i));
                    Assert.That(mesh.GetBlendShapeFrameCount(i), Is.EqualTo(1));
                    Assert.That(mesh.GetBlendShapeFrameWeight(i, 0), Is.EqualTo(100f));
                    Vector3[] v = new Vector3[mesh.vertexCount]; Vector3[] n = new Vector3[mesh.vertexCount]; Vector3[] t = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, 0, v, n, t);
                    CollectionAssert.AreEqual(new Vector3[mesh.vertexCount], v);
                    CollectionAssert.AreEqual(new Vector3[mesh.vertexCount], n);
                    CollectionAssert.AreEqual(new Vector3[mesh.vertexCount], t);
                }
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
    }

}
