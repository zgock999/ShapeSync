// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.PlayMode
{

    public sealed class Spec10PlugablePcmRuntimeTests
    {
        [UnityTest]
        public IEnumerator PlugablePcm_CommitAppliesWeightsAndDetachKeepsRuntimeMesh()
        {
            GameObject figure = new GameObject("Spec10 Runtime Figure");
            Mesh source = CreateFigureMesh();
            Mesh payload = null;
            ProfileControlledMorphAsset asset = null;
            try
            {
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = source;
                DynamicMorphAdapter adapter = figure.AddComponent<DynamicMorphAdapter>();
                adapter.ConfigureForFigure(renderer, 2, 1, new[] { "FBM_A" });
                payload = Object.Instantiate(source);
                payload.ClearBlendShapes();
                payload.AddBlendShapeFrame("PCM_Coat", 100f, Deltas(source.vertexCount, 1f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                payload.AddBlendShapeFrame("PCM_FBM_A_Coat", 100f, Deltas(source.vertexCount, 2f), Zeroes(source.vertexCount), Zeroes(source.vertexCount));
                asset = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
                asset.ConfigureForBuild(payload, "Coat", new List<string> { "FBM_A" }, false);

                yield return null;
                Assert.That(ProfileControlledMorphBinding.TryCreate(adapter, asset, 81, out ProfileControlledMorphBinding binding, out string error), Is.True, error);
                Assert.That(binding.Commit(out error), Is.True, error);
                Mesh runtimeMesh = renderer.sharedMesh;
                binding.ApplyBase();
                binding.ApplyFbmWeight(new FbmWeightChange("FBM_A", 0.75f, true));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(100f));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(75f));

                binding.Dispose();
                Assert.That(renderer.sharedMesh, Is.SameAs(runtimeMesh));
                Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f));
                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(0f));
            }
            finally
            {
                Object.Destroy(figure);
                Object.Destroy(payload);
                Object.Destroy(asset);
                Object.Destroy(source);
            }
        }

        private static Mesh CreateFigureMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("Body", 100f, Zeroes(mesh.vertexCount), Zeroes(mesh.vertexCount), Zeroes(mesh.vertexCount));
            for (int i = 0; i < 4; i++) mesh.AddBlendShapeFrame("Morph_Slot_" + i, 100f, Zeroes(mesh.vertexCount), Zeroes(mesh.vertexCount), Zeroes(mesh.vertexCount));
            return mesh;
        }

        private static Vector3[] Zeroes(int count) => new Vector3[count];

        private static Vector3[] Deltas(int count, float x)
        {
            Vector3[] result = new Vector3[count];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector3(x, 0f, 0f);
            return result;
        }
    }

}
