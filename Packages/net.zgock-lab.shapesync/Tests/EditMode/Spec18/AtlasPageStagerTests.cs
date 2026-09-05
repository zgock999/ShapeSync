// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Focuses page-keyed Atlas staging while retaining live non-Atlas texture dependencies.</summary>
    public sealed class AtlasPageStagerTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec18AtlasPageStagerRoot;
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void Stage_AtlasPagesAndLiveNonAtlasTexturesAreSavedOnceAndSharedByPersistentMaterials()
        {
            HumanoidBuildResult result = null; Material sourceA = null; Material sourceB = null; Material targetA = null; Material targetB = null; Texture2D sampler = null; RenderTexture basePage = null; RenderTexture normalPage = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                DeleteFolder(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec18"), "__AtlasPageStager");
                sampler = new Texture2D(4, 4); sampler.Apply(false, false);
                sourceA = new Material(Shader.Find("Universal Render Pipeline/Lit")); sourceB = new Material(sourceA); sourceA.SetTexture("_BaseMap", sampler); sourceB.SetTexture("_BaseMap", sampler); sourceA.SetTexture("_BumpMap", sampler); sourceB.SetTexture("_BumpMap", sampler); sourceA.SetTexture("_EmissionMap", sampler); sourceB.SetTexture("_EmissionMap", sampler);
                targetA = new Material(sourceA); targetB = new Material(sourceB); basePage = CreatePage(); normalPage = CreatePage(); targetA.SetTexture("_BaseMap", basePage); targetB.SetTexture("_BaseMap", basePage); targetA.SetTexture("_BumpMap", normalPage); targetB.SetTexture("_BumpMap", normalPage);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                var mesh = new Mesh { subMeshCount = 2 };
                var candidate = new InMemoryHumanoidMesh(mesh);
                Invoke(candidate, "TrySetMaterials", new object[] { new[] { targetA, targetB }, null });
                Invoke(candidate, "TrySetMaterialSlots", new object[] { new[] { new HumanoidBuildMaterialSlot(new MaterialId("a", "body"), 0, sourceA, adapter), new HumanoidBuildMaterialSlot(new MaterialId("b", "body"), 1, sourceB, adapter) }, null });
                var completion = new AtlasBakerPageCompletion(2, AtlasTextureSemantic.BaseColor, basePage, Release);
                var normalCompletion = new AtlasBakerPageCompletion(2, AtlasTextureSemantic.Normal, normalPage, Release);
                var carrier = (AtlasBakerCandidatePages)Activator.CreateInstance(typeof(AtlasBakerCandidatePages), Flags, null, new object[] { new[] { completion, normalCompletion } }, null);
                Invoke(candidate, "SetAtlasPages", new object[] { carrier });
                result = new HumanoidBuildResult(candidate);

                Assert.That(InvokeStage(Root, "Look", result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/__AtlasPageStager_atlas2_basecolor.png");
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/__AtlasPageStager_atlas2_normal.png");
                Assert.That(atlas, Is.Not.Null);
                Assert.That(normal, Is.Not.Null);
                Assert.That(Directory.GetFiles(Path.GetFullPath(Root), "*.png"), Has.Length.EqualTo(3), "Atlas pages and the live non-Atlas Emission dependency are all required by the published Materials.");
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(Root + "/__AtlasPageStager_a_body.mat").GetTexture("_BaseMap"), Is.EqualTo(atlas));
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(Root + "/__AtlasPageStager_b_body.mat").GetTexture("_BaseMap"), Is.EqualTo(atlas));
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(Root + "/__AtlasPageStager_a_body.mat").GetTexture("_BumpMap"), Is.EqualTo(normal));
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(Root + "/__AtlasPageStager_b_body.mat").GetTexture("_BumpMap"), Is.EqualTo(normal));
                Texture2D emission = AssetDatabase.LoadAssetAtPath<Material>(Root + "/__AtlasPageStager_a_body.mat").GetTexture("_EmissionMap") as Texture2D;
                Assert.That(emission, Is.Not.Null);
                Assert.That(emission, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(Root + "/__AtlasPageStager_b_body.mat").GetTexture("_EmissionMap")));
                Assert.That(AssetDatabase.GetAssetPath(emission), Does.EndWith("__AtlasPageStager_a_body_0.png"));
                AssertAtlasImporter(Root + "/__AtlasPageStager_atlas2_basecolor.png", true);
                AssertAtlasImporter(Root + "/__AtlasPageStager_atlas2_normal.png", false);
            }
            finally { result?.Dispose(); Destroy(sourceA); Destroy(sourceB); Destroy(targetA); Destroy(targetB); Destroy(sampler); Destroy(adapter); DeleteFolder(); }
        }

        [Test]
        public void Stage_DuplicatePageIdentity_RejectsBeforeWritingArtifacts()
        {
            HumanoidBuildResult result = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture first = null; RenderTexture duplicate = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                DeleteFolder(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec18"), "__AtlasPageStager");
                sampler = new Texture2D(4, 4); source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sampler); target = new Material(source); first = CreatePage(); duplicate = CreatePage(); target.SetTexture("_BaseMap", first); adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                var candidate = new InMemoryHumanoidMesh(new Mesh { subMeshCount = 1 }); Invoke(candidate, "TrySetMaterials", new object[] { new[] { target }, null }); Invoke(candidate, "TrySetMaterialSlots", new object[] { new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null });
                var carrier = (AtlasBakerCandidatePages)Activator.CreateInstance(typeof(AtlasBakerCandidatePages), Flags, null, new object[] { new[] { new AtlasBakerPageCompletion(0, AtlasTextureSemantic.BaseColor, first, Release), new AtlasBakerPageCompletion(0, AtlasTextureSemantic.BaseColor, duplicate, Release) } }, null); Invoke(candidate, "SetAtlasPages", new object[] { carrier }); result = new HumanoidBuildResult(candidate);
                Assert.That(InvokeStage(Root, "Look", result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishAtlasPageDuplicate"));
                Assert.That(Directory.GetFiles(Path.GetFullPath(Root)), Is.Empty);
            }
            finally { result?.Dispose(); Destroy(source); Destroy(target); Destroy(sampler); Destroy(adapter); DeleteFolder(); }
        }

        [Test]
        public void Stage_AtlasSecondPageWriteFailure_ReturnsFirstPageAsResidual()
        {
            HumanoidBuildResult result = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture basePage = null; RenderTexture normalPage = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                DeleteFolder(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec18"), "__AtlasPageStager");
                sampler = new Texture2D(4, 4); source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sampler); source.SetTexture("_BumpMap", sampler); target = new Material(source); basePage = CreatePage(); normalPage = CreatePage(); target.SetTexture("_BaseMap", basePage); target.SetTexture("_BumpMap", normalPage); adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                var candidate = new InMemoryHumanoidMesh(new Mesh { subMeshCount = 1 }); Invoke(candidate, "TrySetMaterials", new object[] { new[] { target }, null }); Invoke(candidate, "TrySetMaterialSlots", new object[] { new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null });
                var carrier = (AtlasBakerCandidatePages)Activator.CreateInstance(typeof(AtlasBakerCandidatePages), Flags, null, new object[] { new[] { new AtlasBakerPageCompletion(0, AtlasTextureSemantic.BaseColor, basePage, Release), new AtlasBakerPageCompletion(0, AtlasTextureSemantic.Normal, normalPage, Release) } }, null); Invoke(candidate, "SetAtlasPages", new object[] { carrier }); result = new HumanoidBuildResult(candidate);
                int writes = 0; SetWriter((path, bytes) => { if (writes++ == 1) throw new IOException("injected"); File.WriteAllBytes(path, bytes); });
                Assert.That(InvokeStageWithResidual(Root, "Look", result, out string[] residuals, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishAssetStagingFailed"));
                Assert.That(residuals, Is.EqualTo(new[] { Root + "/__AtlasPageStager_atlas0_basecolor.png" }));
            }
            finally { SetWriter(File.WriteAllBytes); result?.Dispose(); Destroy(source); Destroy(target); Destroy(sampler); Destroy(adapter); DeleteFolder(); }
        }

        private static bool InvokeStage(string folder, string name, HumanoidBuildResult result, out StackMachineDiagnostic diagnostic)
        { object[] args = { folder, name, result, null, null, null }; bool ok = (bool)Stager.GetMethod("TryStage", Flags).Invoke(null, args); diagnostic = (StackMachineDiagnostic)args[5]; return ok; }
        private static bool InvokeStageWithResidual(string folder, string name, HumanoidBuildResult result, out string[] residuals, out StackMachineDiagnostic diagnostic)
        { object[] args = { folder, name, result, null, null, null }; bool ok = (bool)Stager.GetMethod("TryStage", Flags).Invoke(null, args); residuals = (string[])args[4]; diagnostic = (StackMachineDiagnostic)args[5]; return ok; }
        private static Type Stager => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidIndividualAssetStager", true);
        private static void SetWriter(Action<string, byte[]> writer) => Stager.GetField("WriteAllBytes", Flags).SetValue(null, writer);
        private static object Invoke(object instance, string method, object[] args) => instance.GetType().GetMethod(method, Flags).Invoke(instance, args);
        private static RenderTexture CreatePage() { var value = new RenderTexture(new RenderTextureDescriptor(4, 4, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0) { sRGB = false }); value.Create(); return value; }
        private static void Release(RenderTexture texture) { if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); } }
        private static void AssertAtlasImporter(string path, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default)); Assert.That(importer.sRGBTexture, Is.EqualTo(srgb)); Assert.That(importer.alphaIsTransparency, Is.EqualTo(srgb));
            Assert.That(importer.mipmapEnabled, Is.False); Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp)); Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear)); Assert.That(importer.anisoLevel, Is.EqualTo(1)); Assert.That(importer.maxTextureSize, Is.GreaterThanOrEqualTo(4)); Assert.That(importer.crunchedCompression, Is.False);
        }
        private static void DeleteFolder() { AssetDatabase.DeleteAsset(Root); }
        private static void Destroy(UnityEngine.Object value) { if (value != null) UnityEngine.Object.DestroyImmediate(value); }
    }
}
