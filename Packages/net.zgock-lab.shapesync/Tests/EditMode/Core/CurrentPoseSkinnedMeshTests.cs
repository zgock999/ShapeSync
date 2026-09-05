// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.Editor;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class CurrentPoseSkinnedMeshTests
    {
        [Test]
        public void CP01_CapturedPoseSkinning_PreservesBoneWeightsAndUsesIdentitySkinMatrixAtCapture()
        {
            GameObject root = new GameObject("CurrentPoseRoot");
            GameObject rendererObject = new GameObject("Renderer");
            GameObject boneObject = new GameObject("Bone");
            Mesh source = new Mesh();
            Mesh baked = new Mesh();
            try
            {
                rendererObject.transform.SetParent(root.transform, false);
                boneObject.transform.SetParent(root.transform, false);
                root.transform.position = new Vector3(2f, 3f, 4f);
                boneObject.transform.localPosition = new Vector3(0.25f, 0.5f, -0.75f);

                source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                source.triangles = new[] { 0, 1, 2 };
                source.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                };
                baked.vertices = source.vertices;

                SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                renderer.bones = new[] { boneObject.transform };
                renderer.rootBone = boneObject.transform;

                Assert.That(ExternalTransferWindow.TryConfigureCapturedPoseSkinning(renderer, source, baked, out string error), Is.True, error);
                Assert.That(baked.boneWeights, Has.Length.EqualTo(source.vertexCount));
                Assert.That(baked.bindposes, Has.Length.EqualTo(1));

                Matrix4x4 skinMatrix = boneObject.transform.localToWorldMatrix * baked.bindposes[0] * rendererObject.transform.worldToLocalMatrix;
                AssertIdentity(skinMatrix);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertIdentity(Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float expected = row == column ? 1f : 0f;
                    Assert.That(matrix[row, column], Is.EqualTo(expected).Within(0.00001f));
                }
            }
        }
    }

}
