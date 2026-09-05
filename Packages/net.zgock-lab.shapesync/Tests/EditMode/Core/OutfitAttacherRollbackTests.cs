// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class OutfitAttacherRollbackTests
    {
        private GameObject figure;
        private GameObject testOutfit;
        private CharacterBoneRegistry registry;
        private OutfitSkinningProfile skinningProfile;
        private Mesh outfitMesh;
        private Mesh figureMesh;

        [UnityTest]
        public IEnumerator A10_FigureBoneResolveFailureAfterExtraRootAttach_RollsBackEverything()
        {
            const string extraBonePath = "A10_ExtraRoot";
            const string missingBonePath = "A10_MissingFigureBone";
            figure = new GameObject("A10 Figure");
            DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
            Animator animator = figure.AddComponent<Animator>();
            OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
            attacher.ConfigureForFigure(blender, animator);

            testOutfit = new GameObject("A10 Outfit");
            new GameObject(extraBonePath).transform.SetParent(testOutfit.transform, false);
            GameObject missingFigureBone = new GameObject(missingBonePath);
            missingFigureBone.transform.SetParent(testOutfit.transform, false);
            GameObject rendererObject = new GameObject("Hair");
            rendererObject.transform.SetParent(testOutfit.transform, false);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            outfitMesh = CreateMesh(hasPositiveBoneWeight: true);
            renderer.sharedMesh = outfitMesh;
            renderer.bones = new[] { missingFigureBone.transform };
            renderer.rootBone = missingFigureBone.transform;

            registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            registry.bonePoses.Add(new BonePoseData { boneName = extraBonePath });
            skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
            skinningProfile.SetRendererProfiles(new List<OutfitSkinningRendererProfile>
            {
                new OutfitSkinningRendererProfile
                {
                    rendererPath = "Hair",
                    baseBindposes = new[] { Matrix4x4.identity }
                }
            });

            ShapeSyncOutfit outfit = testOutfit.AddComponent<ShapeSyncOutfit>();
            SetPrivateField(outfit, "registryId", "a10-in-memory");
            SetPrivateField(outfit, "baseExtraBoneRegistry", registry);
            SetPrivateField(outfit, "skinningProfile", skinningProfile);
            SetFbmRegistries(outfit, new List<ShapeSyncOutfitFbmExtraBoneRegistry>());

            LogAssert.Expect(LogType.Warning, new Regex("OutfitAttacher rejected outfit attach: Renderer 'Hair' bone path 'A10_MissingFigureBone' was not found on the Figure after Extra Bone transplant\\."));
            Assert.That(attacher.TryAttach(outfit), Is.False);

            yield return null;

            Assert.That(attacher.AttachedOutfits, Is.Empty);
            Assert.That(figure.transform.Find(extraBonePath), Is.Null, "The Extra Bone root attached before the failure must be rolled back.");
            Assert.That(figure.transform.Find("A10 Outfit (ShapeSync Runtime)"), Is.Null, "A failed attach must not leave a retained Outfit Root.");
        }

        [Test]
        public void UnweightedRendererBone_DoesNotRequireFigureBonePath()
        {
            figure = new GameObject("Unweighted Figure");
            OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();

            testOutfit = new GameObject("Unweighted Outfit");
            GameObject missingFigureBone = new GameObject("Unweighted Missing Bone");
            missingFigureBone.transform.SetParent(testOutfit.transform, false);
            GameObject rendererObject = new GameObject("Hair");
            rendererObject.transform.SetParent(testOutfit.transform, false);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            outfitMesh = CreateMesh(hasPositiveBoneWeight: false);
            renderer.sharedMesh = outfitMesh;
            renderer.bones = new[] { missingFigureBone.transform };
            renderer.rootBone = testOutfit.transform;

            skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
            skinningProfile.SetRendererProfiles(new List<OutfitSkinningRendererProfile>
            {
                new OutfitSkinningRendererProfile
                {
                    rendererPath = "Hair",
                    baseBindposes = new[] { Matrix4x4.identity }
                }
            });

            ShapeSyncOutfit outfit = testOutfit.AddComponent<ShapeSyncOutfit>();
            SetPrivateField(outfit, "skinningProfile", skinningProfile);

            MethodInfo method = typeof(OutfitAttacher).GetMethod("TryBuildRendererPlans", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { outfit, null, null };
            Assert.That((bool)method.Invoke(attacher, arguments), Is.True, arguments[2] as string);

            IList plans = arguments[1] as IList;
            Assert.That(plans, Has.Count.EqualTo(1));
            FieldInfo bonePathsField = plans[0].GetType().GetField("bonePaths", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(bonePathsField, Is.Not.Null);
            string[] bonePaths = (string[])bonePathsField.GetValue(plans[0]);
            Assert.That(bonePaths, Is.EqualTo(new string[] { null }));
        }

        [Test]
        public void ExtraBoneAttach_PreservesGeneratedBindposeAndNullRootBoneContract()
        {
            figure = new GameObject("A11 Figure");
            Transform figureAnchor = new GameObject("FigureAnchor").transform;
            figureAnchor.SetParent(figure.transform, false);
            figureAnchor.localPosition = new Vector3(10f, 0f, 0f);
            SkinnedMeshRenderer figureRenderer = figure.AddComponent<SkinnedMeshRenderer>();
            figureMesh = CreateMesh(hasPositiveBoneWeight: true);
            figureRenderer.sharedMesh = figureMesh;
            figureRenderer.bones = new[] { figure.transform };
            figureRenderer.rootBone = figure.transform;

            DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
            Animator animator = figure.AddComponent<Animator>();
            blender.ConfigureForFigure(figureRenderer, animator, null, null, new List<DynamicBoneBlendTarget>());
            OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
            attacher.ConfigureForFigure(blender, animator);

            testOutfit = new GameObject("A11 Outfit");
            Transform sourceAnchor = new GameObject("FigureAnchor").transform;
            sourceAnchor.SetParent(testOutfit.transform, false);
            sourceAnchor.localPosition = new Vector3(1f, 0f, 0f);
            Transform sourceExtraBone = new GameObject("ExtraBone").transform;
            sourceExtraBone.SetParent(sourceAnchor, false);
            sourceExtraBone.localPosition = new Vector3(2f, 0f, 0f);
            GameObject rendererObject = new GameObject("Hair");
            rendererObject.transform.SetParent(testOutfit.transform, false);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            outfitMesh = CreateMesh(hasPositiveBoneWeight: true);
            renderer.sharedMesh = outfitMesh;
            renderer.bones = new[] { sourceExtraBone };
            renderer.rootBone = testOutfit.transform;
            outfitMesh.bindposes = new[] { sourceExtraBone.worldToLocalMatrix * renderer.transform.localToWorldMatrix };
            Matrix4x4 generatedBindpose = outfitMesh.bindposes[0];

            registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            registry.bonePoses.Add(new BonePoseData
            {
                boneName = "FigureAnchor/ExtraBone",
                localPosition = sourceExtraBone.localPosition,
                localRotation = sourceExtraBone.localRotation,
                localScale = sourceExtraBone.localScale
            });
            skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
            skinningProfile.SetRendererProfiles(new List<OutfitSkinningRendererProfile>
            {
                new OutfitSkinningRendererProfile
                {
                    rendererPath = "Hair",
                    baseBindposes = (Matrix4x4[])outfitMesh.bindposes.Clone()
                }
            });

            ShapeSyncOutfit outfit = testOutfit.AddComponent<ShapeSyncOutfit>();
            SetPrivateField(outfit, "registryId", "a11-extra-bone-rebase");
            SetPrivateField(outfit, "baseExtraBoneRegistry", registry);
            SetPrivateField(outfit, "skinningProfile", skinningProfile);
            SetFbmRegistries(outfit, new List<ShapeSyncOutfitFbmExtraBoneRegistry>());

            Assert.That(attacher.TryAttach(outfit), Is.True);
            Assert.That(attacher.AttachedOutfits, Has.Count.EqualTo(1));
            ShapeSyncOutfit attached = attacher.AttachedOutfits[0].RuntimeOutfitInstance.GetComponent<ShapeSyncOutfit>();
            SkinnedMeshRenderer attachedRenderer = attached.transform.Find("Hair").GetComponent<SkinnedMeshRenderer>();
            Transform attachedBone = figure.transform.Find("FigureAnchor/ExtraBone");
            Assert.That(attachedBone, Is.Not.Null);
            Assert.That(attachedRenderer.rootBone, Is.Null);

            AssertMatrixApproximatelyEqual(attachedRenderer.sharedMesh.bindposes[0], generatedBindpose);
        }

        [TearDown]
        public void TearDown()
        {
            if (testOutfit != null)
            {
                Object.DestroyImmediate(testOutfit);
            }

            if (figure != null)
            {
                Object.DestroyImmediate(figure);
            }

            if (outfitMesh != null)
            {
                Object.DestroyImmediate(outfitMesh);
            }

            if (figureMesh != null)
            {
                Object.DestroyImmediate(figureMesh);
            }

            if (skinningProfile != null)
            {
                Object.DestroyImmediate(skinningProfile);
            }

            if (registry != null)
            {
                Object.DestroyImmediate(registry);
            }
        }

        private static Mesh CreateMesh(bool hasPositiveBoneWeight)
        {
            Mesh mesh = new Mesh { name = "A10 Outfit Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { Matrix4x4.identity };
            if (hasPositiveBoneWeight)
            {
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                };
            }
            return mesh;
        }

        private static void SetFbmRegistries(ShapeSyncOutfit outfit, List<ShapeSyncOutfitFbmExtraBoneRegistry> entries)
        {
            SetPrivateField(outfit, "fbmExtraBoneRegistries", entries);
        }

        private static void AssertMatrixApproximatelyEqual(Matrix4x4 actual, Matrix4x4 expected)
        {
            for (int index = 0; index < 16; index++)
            {
                Assert.That(actual[index], Is.EqualTo(expected[index]).Within(0.0001f), "Matrix element " + index);
            }
        }

        private static void SetPrivateField(ShapeSyncOutfit outfit, string fieldName, object value)
        {
            FieldInfo field = typeof(ShapeSyncOutfit).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(outfit, value);
        }
    }

}
