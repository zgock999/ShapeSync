// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class ShapeSyncEditorComponentTests
    {
        [Test]
        public void BuilderRuntimeComponentSetup_AddsOneSpec14AndSpec15_1ComponentGraphAndPreservesDirectReferences()
        {
            GameObject root = new GameObject("Builder runtime component graph");
            try
            {
                DynamicBoneBlender dynamicBoneBlender = root.AddComponent<DynamicBoneBlender>();

                BuilderRuntimeComponentSetup.Ensure(root, dynamicBoneBlender);
                BuilderRuntimeComponentSetup.Ensure(root, dynamicBoneBlender);

                Assert.That(root.GetComponents<MaterialProxy>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponents<MaterialAttacher>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponents<MaterialStackMachine>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponents<MeshStackMachine>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponents<NormalBlender>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponent<MaterialAttacher>().Proxy, Is.SameAs(root.GetComponent<MaterialProxy>()));
                Assert.That(root.GetComponent<MaterialStackMachine>().MaterialAttacher, Is.SameAs(root.GetComponent<MaterialAttacher>()));
                Assert.That(root.GetComponent<NormalBlender>().DynamicBoneBlender, Is.SameAs(dynamicBoneBlender));
                Assert.That(root.GetComponent<RecipeAttacher>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BuilderRuntimeComponentSetup_OutfitGraphLeavesNormalDdbForOutfitAttacherInjection()
        {
            GameObject root = new GameObject("Builder Outfit runtime component graph");
            try
            {
                BuilderRuntimeComponentSetup.Ensure(root);

                Assert.That(root.GetComponent<MaterialStackMachine>(), Is.Not.Null);
                Assert.That(root.GetComponent<MeshStackMachine>(), Is.Not.Null);
                Assert.That(root.GetComponent<NormalBlender>(), Is.Not.Null);
                Assert.That(root.GetComponent<NormalBlender>().DynamicBoneBlender, Is.Null);
                Assert.That(root.GetComponent<RecipeAttacher>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FbmBodyPrefab_UsesNormalFbmTargetsAndAppliesRegistryPose()
        {
            GameObject root = new GameObject("FBM Body Root");
            GameObject hips = new GameObject("Hips");
            hips.transform.SetParent(root.transform, false);
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            try
            {
                DynamicBoneBlender blender = root.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(
                    null,
                    null,
                    null,
                    null,
                    new List<DynamicBoneBlendTarget>
                    {
                        new DynamicBoneBlendTarget { blendName = "BasicGirl" },
                        new DynamicBoneBlendTarget { blendName = BlendShapeReservedPrefixes.Pbm + "BreastSize" }
                    });
                registry.bonePoses.Add(new BonePoseData
                {
                    boneName = "Hips",
                    localPosition = new Vector3(0f, 0.1f, 0f),
                    localRotation = Quaternion.Euler(0f, 10f, 0f),
                    localScale = new Vector3(1.1f, 1f, 1f)
                });

                Assert.That(FbmBodyPrefabWindow.GetNormalFbmNames(root), Is.EqualTo(new[] { "BasicGirl" }));
                Assert.That(FbmBodyPrefabWindow.TryApplyRegistryPose(root.transform, registry, out string error), Is.True, error);
                Assert.That(hips.transform.localPosition.y, Is.EqualTo(0.1f).Within(0.00001f));
                Assert.That(hips.transform.localScale.x, Is.EqualTo(1.1f).Within(0.00001f));
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FbmBodyPrefab_AppliesRegistryBindposesByIndex()
        {
            Mesh mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right } };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            try
            {
                Matrix4x4 first = Matrix4x4.Translate(new Vector3(1f, 0f, 0f));
                Matrix4x4 second = Matrix4x4.Translate(new Vector3(0f, 2f, 0f));
                registry.bonePoses.Add(new BonePoseData { bindposeIndex = 0, hasBindpose = true, bindpose = first });
                registry.bonePoses.Add(new BonePoseData { bindposeIndex = 1, hasBindpose = true, bindpose = second });

                Assert.That(FbmBodyPrefabWindow.TryApplyRegistryBindposes(mesh, registry, out string error), Is.True, error);
                Assert.That(mesh.bindposes[0], Is.EqualTo(first));
                Assert.That(mesh.bindposes[1], Is.EqualTo(second));
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(mesh);
            }
        }

    }

}
