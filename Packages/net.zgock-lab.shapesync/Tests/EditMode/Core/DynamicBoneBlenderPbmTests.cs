// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class DynamicBoneBlenderPbmTests
    {
        [Test]
        public void DynamicBoneBlender_AppliesPbmBaseAndFbmDifference()
        {
            GameObject root = new GameObject("PBM Figure");
            Mesh mesh = CreateMesh("BasicGirl", "PBM_BreastSize", "PBM_BasicGirl_BreastSize");
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            DynamicBoneBlender blender = root.AddComponent<DynamicBoneBlender>();
            var targets = new List<DynamicBoneBlendTarget>
            {
                new DynamicBoneBlendTarget { blendName = "BasicGirl", weight = 0.25f },
                new DynamicBoneBlendTarget { blendName = "PBM_BreastSize", weight = 0.8f }
            };

            try
            {
                blender.ConfigureForFigure(renderer, null, null, null, targets);
                Invoke(blender, "Start");
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BreastSize")), Is.EqualTo(80f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BasicGirl_BreastSize")), Is.EqualTo(20f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void OutfitBinding_AppliesDdbPbmWeightsWithoutNameLookupAfterCacheBuild()
        {
            GameObject root = new GameObject("PBM Outfit");
            Mesh mesh = CreateMesh("BasicGirl", "PBM_BreastSize", "PBM_BasicGirl_BreastSize");
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            OutfitSkinningRendererProfile profile = new OutfitSkinningRendererProfile
            {
                baseBindposes = new[] { Matrix4x4.identity }
            };

            try
            {
                Assert.That(OutfitSkinnedMeshBinding.TryCreate(renderer, profile, out OutfitSkinnedMeshBinding binding, out string error), Is.True, error);
                binding.ConfigureDdbTargets(new List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget { blendName = "BasicGirl" },
                    new DynamicBoneBlendTarget { blendName = "PBM_BreastSize" }
                });
                binding.ApplyTargetWeights(new[] { 0.25f, 0.8f });

                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BreastSize")), Is.EqualTo(80f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BasicGirl_BreastSize")), Is.EqualTo(20f).Within(0.001f));
                binding.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void OutfitBinding_AppliesRawPbmAndFbmProductWithoutClampOrRescale()
        {
            GameObject root = new GameObject("PBM Outfit Raw Weight");
            Mesh mesh = CreateMesh("BasicGirl", "PBM_BreastSize", "PBM_BasicGirl_BreastSize");
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            OutfitSkinningRendererProfile profile = new OutfitSkinningRendererProfile { baseBindposes = new[] { Matrix4x4.identity } };

            try
            {
                Assert.That(OutfitSkinnedMeshBinding.TryCreate(renderer, profile, out OutfitSkinnedMeshBinding binding, out string error), Is.True, error);
                binding.ConfigureDdbTargets(new List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget { blendName = "BasicGirl" },
                    new DynamicBoneBlendTarget { blendName = "PBM_BreastSize" }
                });
                binding.ApplyTargetWeights(new[] { -0.5f, 1.5f });

                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BreastSize")), Is.EqualTo(150f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BasicGirl_BreastSize")), Is.EqualTo(-75f).Within(0.001f));
                binding.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void OutfitBinding_RejectsNonFinitePbmWeightBeforeApplyingToRenderer()
        {
            GameObject root = new GameObject("PBM Outfit NonFinite");
            Mesh mesh = CreateMesh("BasicGirl", "PBM_BreastSize", "PBM_BasicGirl_BreastSize");
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            OutfitSkinningRendererProfile profile = new OutfitSkinningRendererProfile { baseBindposes = new[] { Matrix4x4.identity } };

            try
            {
                Assert.That(OutfitSkinnedMeshBinding.TryCreate(renderer, profile, out OutfitSkinnedMeshBinding binding, out string error), Is.True, error);
                binding.ConfigureDdbTargets(new List<DynamicBoneBlendTarget>
                {
                    new DynamicBoneBlendTarget { blendName = "BasicGirl" },
                    new DynamicBoneBlendTarget { blendName = "PBM_BreastSize" }
                });
                binding.ApplyTargetWeights(new[] { 0.5f, float.NaN });

                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BreastSize")), Is.EqualTo(0f).Within(0.001f));
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("PBM_BasicGirl_BreastSize")), Is.EqualTo(0f).Within(0.001f));
                binding.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateMesh(params string[] blendShapeNames)
        {
            Mesh mesh = new Mesh { name = "PBM Test Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            Vector3[] delta = new Vector3[3];
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, delta, null, null);
            }

            return mesh;
        }

        private static void Invoke(object instance, string methodName)
        {
            MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName + " was not found.");
            method.Invoke(instance, null);
        }
    }

}
