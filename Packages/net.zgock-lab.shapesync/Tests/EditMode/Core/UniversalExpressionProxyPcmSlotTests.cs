// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class UniversalExpressionProxyPcmSlotTests
    {
        [Test]
        public void RebuildExpressionList_ExcludesReservedPcmSlots()
        {
            GameObject root = new GameObject("UniversalExpressionProxyPcmSlotTest");
            Mesh mesh = new Mesh { name = "UniversalExpressionProxyPcmSlotTestMesh" };
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                Vector3[] zeroes = new Vector3[mesh.vertexCount];
                mesh.AddBlendShapeFrame("Smile", 100f, zeroes, zeroes, zeroes);
                mesh.AddBlendShapeFrame(zgock.ShapeSync.BlendShapeReservedPrefixes.MorphSlot + "0", 100f, zeroes, zeroes, zeroes);
                mesh.AddBlendShapeFrame(zgock.ShapeSync.BlendShapeReservedPrefixes.MorphSlot + "1", 100f, zeroes, zeroes, zeroes);
                mesh.AddBlendShapeFrame(zgock.ShapeSync.BlendShapeReservedPrefixes.Pcm + "Shoes3", 100f, zeroes, zeroes, zeroes);
                mesh.AddBlendShapeFrame(zgock.ShapeSync.BlendShapeReservedPrefixes.Pbm + "BreastSize", 100f, zeroes, zeroes, zeroes);

                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                UniversalExpressionProxy proxy = root.AddComponent<UniversalExpressionProxy>();
                proxy.ConfigureForFigure(renderer, null);
                proxy.RebuildExpressionList();

                Assert.That(proxy.Expressions.Count, Is.EqualTo(1));
                Assert.That(proxy.Expressions[0].blendShapeName, Is.EqualTo("Smile"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }
    }

}
