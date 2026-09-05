// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncSlimOracleTests
    {
        [Test]
        public void SlimFixture_CanGenerateFigureAndOutfitWithoutPlayTestAssets()
        {
            using (ShapeSyncSlimFixture fixture = ShapeSyncSlimFixture.Create())
            {
                const string outputRoot = ShapeSyncSlimFixture.Root + "/Generated";
                Assert.That(AssetDatabase.CreateFolder(ShapeSyncSlimFixture.Root, "Generated"), Is.Not.Empty);
                ShapeSyncDatabaseRegistry.GenerationPathSettings paths = fixture.Database.Registry.GenerationPaths;
                Assert.That(ShapeSyncFigureGenerator.TryGenerate(fixture.Database, outputRoot, paths.RegistriesPath, paths.BindingsPath,
                    paths.MaterialsPath, paths.TexturesPath, out string figureDiagnostic), Is.True, figureDiagnostic);
                Assert.That(ShapeSyncOutfitGenerator.TryGenerate(fixture.Database, outputRoot, paths.BindingsPath, paths.OutfitsPath, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                GameObject generatedFigure = AssetDatabase.LoadAssetAtPath<GameObject>(outputRoot + "/SlimFigure.prefab");
                GameObject generatedOutfit = AssetDatabase.LoadAssetAtPath<GameObject>(outputRoot + "/Outfits/SlimOutfit.prefab");
                Assert.That(generatedFigure, Is.Not.Null);
                Assert.That(generatedOutfit, Is.Not.Null);
                GameObject equivalentFigure = UnityEngine.Object.Instantiate(generatedFigure);
                GameObject equivalentOutfit = UnityEngine.Object.Instantiate(generatedOutfit);
                try
                {
                    IReadOnlyList<string> figureFirst = CompareGeneratedFigure(generatedFigure, equivalentFigure);
                    IReadOnlyList<string> figureSecond = CompareGeneratedFigure(generatedFigure, equivalentFigure);
                    CollectionAssert.AreEqual(figureFirst, figureSecond, "Figure Oracle must be deterministic for an unchanged payload.");
                    Assert.That(figureFirst, Is.Empty);
                    IReadOnlyList<string> outfitFirst = CompareGeneratedOutfit(generatedOutfit, equivalentOutfit);
                    IReadOnlyList<string> outfitSecond = CompareGeneratedOutfit(generatedOutfit, equivalentOutfit);
                    CollectionAssert.AreEqual(outfitFirst, outfitSecond, "Outfit Oracle must be deterministic for an unchanged payload.");
                    Assert.That(outfitFirst, Is.Empty);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(equivalentFigure);
                    UnityEngine.Object.DestroyImmediate(equivalentOutfit);
                }
                GameObject sourceFigure = fixture.Database.transform.Find("Intermediate/SlimFigure").gameObject;
                GameObject sourceOutfit = fixture.Database.transform.Find("Intermediate/SlimOutfit").gameObject;
                Assert.That(CompareStaticPayload(sourceFigure, generatedFigure, "Figure"), Is.Empty);
                Assert.That(CompareStaticPayload(sourceOutfit, generatedOutfit, "Outfit"), Is.Empty);
                Mesh generatedOutfitMesh = generatedOutfit.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                Assert.That(generatedOutfitMesh.GetBlendShapeIndex("Tall"), Is.GreaterThanOrEqualTo(0), "Slim Outfit Oracle must include the FBM BlendShape.");
                Assert.That(generatedOutfitMesh.GetBlendShapeIndex("PBM_Wide"), Is.GreaterThanOrEqualTo(0), "Slim Outfit Oracle must include the PBM Base difference BlendShape.");
                Assert.That(generatedOutfitMesh.GetBlendShapeIndex("PBM_Tall_Wide"), Is.GreaterThanOrEqualTo(0), "Slim Outfit Oracle must include the PBM FBM difference BlendShape.");
                ShapeSyncOutfit generatedOutfitDescriptor = generatedOutfit.GetComponent<ShapeSyncOutfit>();
                Assert.That(generatedOutfitDescriptor, Is.Not.Null);
                Assert.That(generatedOutfitDescriptor.SkinningProfile.Renderers, Has.Count.EqualTo(1));
                Assert.That(generatedOutfitDescriptor.SkinningProfile.Renderers[0].fbmBindposes, Has.Count.EqualTo(1));
                Assert.That(generatedOutfitDescriptor.SkinningProfile.Renderers[0].fbmBindposes[0].blendName, Is.EqualTo("Tall"));
                Assert.That(generatedOutfitDescriptor.ProfileControlledMorphEnabled, Is.True, "Slim Outfit Oracle must include the Full Collection PCM payload.");
                Assert.That(generatedOutfitDescriptor.ProfileControlledMorphAsset, Is.Not.Null);
                Assert.That(generatedOutfitDescriptor.HumanoidBoneCorrectionProfile, Is.Not.Null, "Slim Outfit Oracle must include the Base BC Profile.");
                Assert.That(generatedOutfitDescriptor.FbmHumanoidBoneCorrectionProfiles, Has.Count.EqualTo(1));
                Assert.That(generatedOutfitDescriptor.ProfileControlledMorphAsset.PayloadMesh, Is.Not.Null);
                Assert.That(generatedOutfitDescriptor.ProfileControlledMorphAsset.PayloadMesh.GetBlendShapeIndex("PCM_SlimOutfit"), Is.GreaterThanOrEqualTo(0));
                Assert.That(generatedOutfitDescriptor.ProfileControlledMorphAsset.PayloadMesh.GetBlendShapeIndex("PCM_Tall_SlimOutfit"), Is.GreaterThanOrEqualTo(0));
            }
        }

        [Test]
        public void SlimOracle_DifferenceInjectionFailsWithEntityAndRelation()
        {
            using (ShapeSyncSlimFixture fixture = ShapeSyncSlimFixture.Create())
            {
                const string outputRoot = ShapeSyncSlimFixture.Root + "/Generated";
                Assert.That(AssetDatabase.CreateFolder(ShapeSyncSlimFixture.Root, "Generated"), Is.Not.Empty);
                ShapeSyncDatabaseRegistry.GenerationPathSettings paths = fixture.Database.Registry.GenerationPaths;
                Assert.That(ShapeSyncFigureGenerator.TryGenerate(fixture.Database, outputRoot, paths.RegistriesPath, paths.BindingsPath,
                    paths.MaterialsPath, paths.TexturesPath, out string figureDiagnostic), Is.True, figureDiagnostic);
                Assert.That(ShapeSyncOutfitGenerator.TryGenerate(fixture.Database, outputRoot, paths.BindingsPath, paths.OutfitsPath, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                GameObject generatedFigure = AssetDatabase.LoadAssetAtPath<GameObject>(outputRoot + "/SlimFigure.prefab");
                GameObject generatedOutfit = AssetDatabase.LoadAssetAtPath<GameObject>(outputRoot + "/Outfits/SlimOutfit.prefab");

                var scenarios = new (string Name, Func<GameObject, UnityEngine.Object> Inject)[]
                {
                    ("Mesh.vertices", root => MutateFigureMesh(root, mesh => { Vector3[] values = mesh.vertices; values[0] += Vector3.right; mesh.vertices = values; })),
                    ("BlendShape.frame", root => MutateFigureMesh(root, mesh => { mesh.ClearBlendShapes(); mesh.AddBlendShapeFrame("Injected", 100f, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]); })),
                    ("Mesh.boneWeights", root => MutateFigureMesh(root, mesh => { BoneWeight[] values = mesh.boneWeights; values[0].weight0 = .5f; values[0].weight1 = .5f; values[0].boneIndex1 = 1; mesh.boneWeights = values; })),
                    ("Mesh.boneBinding", root => { SkinnedMeshRenderer renderer = root.GetComponentInChildren<SkinnedMeshRenderer>(true); Transform[] bones = renderer.bones; (bones[0], bones[1]) = (bones[1], bones[0]); renderer.bones = bones; return null; }),
                    ("Animator.avatar", root => { Animator animator = root.GetComponentInChildren<Animator>(true); animator.avatar = null; return null; }),
                    ("Runtime.MaterialProxy", root => { UnityEngine.Object.DestroyImmediate(root.GetComponent<MaterialProxy>()); return null; }),
                    ("Runtime.CharacterBoneRegistry", root => MutateFigureRegistry(root))
                };
                foreach ((string name, Func<GameObject, UnityEngine.Object> inject) in scenarios)
                {
                    GameObject mutated = UnityEngine.Object.Instantiate(generatedFigure);
                    UnityEngine.Object owned = null;
                    try
                    {
                        owned = inject(mutated);
                        IReadOnlyList<string> differences = CompareGeneratedFigure(generatedFigure, mutated);
                        Assert.That(differences, Is.Not.Empty, name + " must be detected by the Oracle.");
                        Assert.That(differences.Any(value => value.StartsWith("Figure/", StringComparison.Ordinal)), Is.True,
                            name + " must identify the Figure entity: " + string.Join(";", differences));
                    }
                    finally
                    {
                        if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
                        UnityEngine.Object.DestroyImmediate(mutated);
                    }
                }

                GameObject mutatedOutfit = UnityEngine.Object.Instantiate(generatedOutfit);
                try
                {
                    ShapeSyncOutfit outfit = mutatedOutfit.GetComponent<ShapeSyncOutfit>();
                    Assert.That(outfit, Is.Not.Null);
                    OutfitSkinningProfile profile = UnityEngine.Object.Instantiate(outfit.SkinningProfile);
                    profile.SetUsesBcpBakedBindposesForEditor(!profile.UsesBcpBakedBindposes);
                    SerializedObject serialized = new SerializedObject(outfit);
                    serialized.FindProperty("skinningProfile").objectReferenceValue = profile;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    IReadOnlyList<string> differences = CompareGeneratedOutfit(generatedOutfit, mutatedOutfit);
                    Assert.That(differences, Is.Not.Empty, "OutfitSkinningProfile difference must be detected by the Oracle.");
                    Assert.That(differences.Any(value => value.StartsWith("Outfit/", StringComparison.Ordinal)), Is.True,
                        "Outfit profile difference must identify the Outfit entity: " + string.Join(";", differences));
                    UnityEngine.Object.DestroyImmediate(profile);
                }
                finally { UnityEngine.Object.DestroyImmediate(mutatedOutfit); }

                mutatedOutfit = UnityEngine.Object.Instantiate(generatedOutfit);
                UnityEngine.Object mutatedOutfitMesh = MutateFigureMesh(mutatedOutfit, mesh =>
                {
                    mesh.ClearBlendShapes();
                    mesh.AddBlendShapeFrame("InjectedPBM", 100f, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
                });
                try
                {
                    IReadOnlyList<string> differences = CompareGeneratedOutfit(generatedOutfit, mutatedOutfit);
                    Assert.That(differences.Any(value => value == "Outfit/OutfitSkinningProfile" || value.StartsWith("Outfit/BlendShape", StringComparison.Ordinal)), Is.True,
                        "PBM/FBM Outfit payload difference must identify the Outfit relation: " + string.Join(";", differences));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mutatedOutfitMesh);
                    UnityEngine.Object.DestroyImmediate(mutatedOutfit);
                }

                mutatedOutfit = UnityEngine.Object.Instantiate(generatedOutfit);
                try
                {
                    SerializedObject serialized = new SerializedObject(mutatedOutfit.GetComponent<ShapeSyncOutfit>());
                    serialized.FindProperty("profileControlledMorphAsset").objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    IReadOnlyList<string> differences = CompareGeneratedOutfit(generatedOutfit, mutatedOutfit);
                    Assert.That(differences.Any(value => value.StartsWith("Outfit/ProfileControlledMorph", StringComparison.Ordinal)), Is.True,
                        "PCM payload difference must identify the Outfit relation: " + string.Join(";", differences));
                }
                finally { UnityEngine.Object.DestroyImmediate(mutatedOutfit); }

                mutatedOutfit = UnityEngine.Object.Instantiate(generatedOutfit);
                try
                {
                    SerializedObject serialized = new SerializedObject(mutatedOutfit.GetComponent<ShapeSyncOutfit>());
                    serialized.FindProperty("humanoidBoneCorrectionProfile").objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    IReadOnlyList<string> differences = CompareGeneratedOutfit(generatedOutfit, mutatedOutfit);
                    Assert.That(differences.Any(value => value.StartsWith("Outfit/BCProfile", StringComparison.Ordinal)), Is.True,
                        "BC Profile difference must identify the Outfit relation: " + string.Join(";", differences));
                }
                finally { UnityEngine.Object.DestroyImmediate(mutatedOutfit); }
            }
        }

        private static IReadOnlyList<string> CompareStaticPayload(GameObject expected, GameObject actual, string entity)
        {
            var differences = new List<string>();
            SkinnedMeshRenderer[] expectedRenderers = expected.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer[] actualRenderers = actual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (expectedRenderers.Length != actualRenderers.Length)
            { differences.Add(entity + "/Renderer.Count"); return differences; }
            for (int index = 0; index < expectedRenderers.Length; index++)
            {
                Mesh expectedMesh = expectedRenderers[index].sharedMesh;
                Mesh actualMesh = actualRenderers[index].sharedMesh;
                if (expectedMesh == null || actualMesh == null) { differences.Add(entity + "/Renderer[" + index + "]/Mesh"); continue; }
                if (!expectedMesh.vertices.SequenceEqual(actualMesh.vertices)) differences.Add(entity + "/Renderer[" + index + "]/Mesh.vertices");
                if (!expectedMesh.normals.SequenceEqual(actualMesh.normals)) differences.Add(entity + "/Renderer[" + index + "]/Mesh.normals");
                if (!expectedMesh.boneWeights.SequenceEqual(actualMesh.boneWeights)) differences.Add(entity + "/Renderer[" + index + "]/Mesh.boneWeights");
                string[] expectedBones = expectedRenderers[index].bones.Select(value => RelativePath(expected.transform, value)).ToArray();
                string[] actualBones = actualRenderers[index].bones.Select(value => RelativePath(actual.transform, value)).ToArray();
                if (!expectedBones.SequenceEqual(actualBones)) differences.Add(entity + "/Renderer[" + index + "]/Mesh.boneBinding");
            }
            return differences;
        }

        private static IReadOnlyList<string> CompareGeneratedFigure(GameObject expected, GameObject actual)
        {
            var differences = CompareStaticPayload(expected, actual, "Figure").ToList();
            SkinnedMeshRenderer expectedRenderer = expected.GetComponentInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer actualRenderer = actual.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (expectedRenderer != null && actualRenderer != null)
            {
                if (expectedRenderer.sharedMesh.blendShapeCount != actualRenderer.sharedMesh.blendShapeCount)
                    differences.Add("Figure/Renderer.Mesh.BlendShape.Count");
                else for (int index = 0; index < expectedRenderer.sharedMesh.blendShapeCount; index++)
                {
                    if (expectedRenderer.sharedMesh.GetBlendShapeName(index) != actualRenderer.sharedMesh.GetBlendShapeName(index)) differences.Add("Figure/Renderer.Mesh.BlendShape.Name");
                    if (expectedRenderer.sharedMesh.GetBlendShapeFrameWeight(index, 0) != actualRenderer.sharedMesh.GetBlendShapeFrameWeight(index, 0)) differences.Add("Figure/Renderer.Mesh.BlendShape.Frame");
                }
            }
            Animator expectedAnimator = expected.GetComponentInChildren<Animator>(true);
            Animator actualAnimator = actual.GetComponentInChildren<Animator>(true);
            if ((expectedAnimator?.avatar == null) != (actualAnimator?.avatar == null)) differences.Add("Figure/Animator.Avatar");
            DynamicBoneBlender expectedBlender = expected.GetComponent<DynamicBoneBlender>();
            DynamicBoneBlender actualBlender = actual.GetComponent<DynamicBoneBlender>();
            if ((expectedBlender?.BaseRegistry == null) != (actualBlender?.BaseRegistry == null)
                || expectedBlender != null && actualBlender != null && EditorJsonUtility.ToJson(expectedBlender.BaseRegistry) != EditorJsonUtility.ToJson(actualBlender.BaseRegistry)) differences.Add("Figure/Runtime.CharacterBoneRegistry");
            if (actual.GetComponent<MaterialProxy>() == null) differences.Add("Figure/Runtime.MaterialProxy");
            return differences;
        }

        private static IReadOnlyList<string> CompareGeneratedOutfit(GameObject expected, GameObject actual)
        {
            var differences = CompareStaticPayload(expected, actual, "Outfit").ToList();
            Mesh expectedMesh = expected.GetComponentInChildren<SkinnedMeshRenderer>(true)?.sharedMesh;
            Mesh actualMesh = actual.GetComponentInChildren<SkinnedMeshRenderer>(true)?.sharedMesh;
            if (expectedMesh != null && actualMesh != null)
            {
                string[] expectedShapes = Enumerable.Range(0, expectedMesh.blendShapeCount).Select(expectedMesh.GetBlendShapeName).ToArray();
                string[] actualShapes = Enumerable.Range(0, actualMesh.blendShapeCount).Select(actualMesh.GetBlendShapeName).ToArray();
                if (!expectedShapes.SequenceEqual(actualShapes)) differences.Add("Outfit/BlendShape.PBM");
            }
            ShapeSyncOutfit expectedOutfit = expected.GetComponent<ShapeSyncOutfit>();
            ShapeSyncOutfit actualOutfit = actual.GetComponent<ShapeSyncOutfit>();
            if (expectedOutfit == null || actualOutfit == null) differences.Add("Outfit/Runtime.Descriptor");
            else
            {
                if (EditorJsonUtility.ToJson(expectedOutfit.SkinningProfile) != EditorJsonUtility.ToJson(actualOutfit.SkinningProfile)) differences.Add("Outfit/OutfitSkinningProfile");
                if (expectedOutfit.ProfileControlledMorphEnabled != actualOutfit.ProfileControlledMorphEnabled
                    || (expectedOutfit.ProfileControlledMorphAsset == null) != (actualOutfit.ProfileControlledMorphAsset == null)) differences.Add("Outfit/ProfileControlledMorph");
                if ((expectedOutfit.HumanoidBoneCorrectionProfile == null) != (actualOutfit.HumanoidBoneCorrectionProfile == null)) differences.Add("Outfit/BCProfile");
                if (expectedOutfit.ProfileControlledMorphAsset != null && actualOutfit.ProfileControlledMorphAsset != null)
                {
                    Mesh expectedPayload = expectedOutfit.ProfileControlledMorphAsset.PayloadMesh;
                    Mesh actualPayload = actualOutfit.ProfileControlledMorphAsset.PayloadMesh;
                    string[] expectedPayloadShapes = expectedPayload == null ? Array.Empty<string>() : Enumerable.Range(0, expectedPayload.blendShapeCount).Select(expectedPayload.GetBlendShapeName).ToArray();
                    string[] actualPayloadShapes = actualPayload == null ? Array.Empty<string>() : Enumerable.Range(0, actualPayload.blendShapeCount).Select(actualPayload.GetBlendShapeName).ToArray();
                    if (!expectedPayloadShapes.SequenceEqual(actualPayloadShapes)) differences.Add("Outfit/ProfileControlledMorph.Payload");
                }
            }
            return differences;
        }

        private static UnityEngine.Object MutateFigureMesh(GameObject root, Action<Mesh> mutation)
        {
            SkinnedMeshRenderer renderer = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Mesh clone = UnityEngine.Object.Instantiate(renderer.sharedMesh);
            clone.name = "InjectedMesh";
            mutation(clone);
            renderer.sharedMesh = clone;
            return clone;
        }

        private static UnityEngine.Object MutateFigureRegistry(GameObject root)
        {
            DynamicBoneBlender blender = root.GetComponent<DynamicBoneBlender>();
            CharacterBoneRegistry clone = UnityEngine.Object.Instantiate(blender.BaseRegistry);
            clone.fbmBlendName = "InjectedRegistryDifference";
            SerializedObject serialized = new SerializedObject(blender);
            serialized.FindProperty("baseRegistry").objectReferenceValue = clone;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return clone;
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (value == null || value == root) return string.Empty;
            var names = new List<string>();
            for (Transform current = value; current != null && current != root; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
#endif
