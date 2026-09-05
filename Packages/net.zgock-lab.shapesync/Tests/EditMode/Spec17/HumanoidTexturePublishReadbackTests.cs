// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidTexturePublishReadbackTests
    {
        private const string TestPngPath = ShapeSyncTestAssetPaths.Spec17TexturePublishRoot + ".png";

        [Test]
        public void CollectReadbackAndConfigureImporter_PreservesExtentAlphaAndSamplerForBaseAndNormal()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture baseTexture = null; RenderTexture normalTexture = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                AssetDatabase.DeleteAsset(TestPngPath);
                sampler = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Mirror, filterMode = FilterMode.Point, anisoLevel = 4 };
                sampler.SetPixel(0, 0, new Color(.25f, .5f, .75f, .4f)); sampler.Apply(true, false);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sampler); source.SetTexture("_BumpMap", sampler);
                target = new Material(source); baseTexture = CreateTexture(sampler); normalTexture = CreateTexture(sampler); target.SetTexture("_BaseMap", baseTexture); target.SetTexture("_BumpMap", normalTexture);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                mesh = CreateMesh(source, target, adapter);

                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                Assert.That(entries.Length, Is.EqualTo(2));
                object baseEntry = FindEntry(entries, "_BaseMap"); object normalEntry = FindEntry(entries, "_BumpMap");
                Assert.That(InvokeEncode(baseEntry, out byte[] basePng, out StackMachineDiagnostic baseDiagnostic), Is.True, baseDiagnostic?.message);
                Assert.That(InvokeEncode(normalEntry, out byte[] normalPng, out StackMachineDiagnostic normalDiagnostic), Is.True, normalDiagnostic?.message);
                Assert.That(basePng.Length, Is.GreaterThan(0)); Assert.That(normalPng.Length, Is.GreaterThan(0));
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false); ImageConversion.LoadImage(decoded, basePng);
                Assert.That(decoded.width, Is.EqualTo(2)); Assert.That(decoded.height, Is.EqualTo(2)); Assert.That(decoded.GetPixel(0, 0).a, Is.EqualTo(.4f).Within(.02f)); Assert.That(decoded.GetPixel(0, 0).r, Is.GreaterThan(.25f)); UnityEngine.Object.DestroyImmediate(decoded);

                File.WriteAllBytes(TestPngPath, basePng); AssetDatabase.ImportAsset(TestPngPath, ImportAssetOptions.ForceUpdate);
                Assert.That(InvokeConfigure(TestPngPath, baseEntry, out StackMachineDiagnostic importerDiagnostic), Is.True, importerDiagnostic?.message);
                var importer = (TextureImporter)AssetImporter.GetAtPath(TestPngPath);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default)); Assert.That(importer.sRGBTexture, Is.True); Assert.That(importer.alphaIsTransparency, Is.True); Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Mirror)); Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point)); Assert.That(importer.anisoLevel, Is.EqualTo(4)); Assert.That(importer.mipmapEnabled, Is.True);
                Assert.That(InvokeConfigure(TestPngPath, normalEntry, out importerDiagnostic), Is.True, importerDiagnostic?.message);
                var normalImporter = (TextureImporter)AssetImporter.GetAtPath(TestPngPath);
                Assert.That(normalImporter.sRGBTexture, Is.False); Assert.That(normalImporter.alphaIsTransparency, Is.False);
            }
            finally { AssetDatabase.DeleteAsset(TestPngPath); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(baseTexture); Release(normalTexture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Collect_IncludesUnprocessedTexture2DAndEncodesReadableTransientSource()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; UrpUnlitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                sampler.SetPixel(0, 0, Color.red); sampler.Apply(false, false);
                source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler);
                target = new Material(source); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                mesh = CreateMesh(source, target, adapter);

                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                Assert.That(entries.Length, Is.EqualTo(1));
                object entry = entries.GetValue(0);
                Assert.That(entry.GetType().GetProperty("Texture", Flags).GetValue(entry), Is.SameAs(sampler));
                SetPngEncoder(_ => new byte[] { 1, 2, 3 });
                Assert.That(InvokeEncode(entry, out byte[] png, out StackMachineDiagnostic encode), Is.True, encode?.message);
                Assert.That(png, Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            finally { SetPngEncoder(ImageConversion.EncodeToPNG); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Collect_IncludesPreservedShaderTextureProperties()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture baseTexture = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                sampler.SetPixel(0, 0, Color.white); sampler.Apply(false, false);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                source.SetTexture("_BaseMap", sampler); source.SetTexture("_EmissionMap", sampler);
                target = new Material(source); baseTexture = CreateTexture(sampler); target.SetTexture("_BaseMap", baseTexture);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);

                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(entries.Length, Is.EqualTo(2));
                object emissionEntry = FindEntry(entries, "_EmissionMap");
                Assert.That(PropertyNames(emissionEntry), Does.Contain("_EmissionMap"));
                Assert.That(emissionEntry.GetType().GetProperty("Semantic", Flags).GetValue(emissionEntry).ToString(), Is.EqualTo("Preserved"));
            }
            finally { mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(baseTexture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void ConfigureImporter_PreservedTextureInheritsSourceColorSemantics()
        {
            const string sourcePath = ShapeSyncTestAssetPaths.Spec17TexturePublishRoot + "_preserved_source.png";
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture preservedTexture = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                AssetDatabase.DeleteAsset(TestPngPath); AssetDatabase.DeleteAsset(sourcePath);
                var authored = new Texture2D(2, 2, TextureFormat.RGBA32, false, false); authored.SetPixel(0, 0, Color.white); authored.Apply(false, false);
                byte[] png = authored.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(authored);
                File.WriteAllBytes(sourcePath, png); AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
                sampler = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(sourcePath); sourceImporter.sRGBTexture = true; sourceImporter.alphaIsTransparency = true; sourceImporter.SaveAndReimport();
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_BaseMap", sampler); source.SetTexture("_EmissionMap", sampler);
                target = new Material(source); preservedTexture = CreateTexture(sampler); target.SetTexture("_EmissionMap", preservedTexture);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);

                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                object preservedEntry = FindEntry(entries, "_EmissionMap");
                File.WriteAllBytes(TestPngPath, png); AssetDatabase.ImportAsset(TestPngPath, ImportAssetOptions.ForceUpdate);
                Assert.That(InvokeConfigure(TestPngPath, preservedEntry, out StackMachineDiagnostic configure), Is.True, configure?.message);
                var outputImporter = (TextureImporter)AssetImporter.GetAtPath(TestPngPath);
                Assert.That(outputImporter.sRGBTexture, Is.True); Assert.That(outputImporter.alphaIsTransparency, Is.True);
            }
            finally { AssetDatabase.DeleteAsset(TestPngPath); AssetDatabase.DeleteAsset(sourcePath); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(preservedTexture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void ConfigureImporter_PreservedEmbeddedTextureUsesRuntimeColorSpaceWhenSourceHasNoTextureImporter()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture preservedTexture = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                AssetDatabase.DeleteAsset(TestPngPath);
                sampler = new Texture2D(2, 2, TextureFormat.RGBA32, false, false); sampler.SetPixel(0, 0, Color.white); sampler.Apply(false, false);
                source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetTexture("_EmissionMap", sampler);
                target = new Material(source); preservedTexture = CreateTexture(sampler); target.SetTexture("_EmissionMap", preservedTexture);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);
                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic diagnostic), Is.True, diagnostic == null ? "collect failed without diagnostic" : StackMachineDiagnostic.Format(diagnostic, "collect failed"));
                object entry = FindEntry(entries, "_EmissionMap");
                File.WriteAllBytes(TestPngPath, sampler.EncodeToPNG()); AssetDatabase.ImportAsset(TestPngPath, ImportAssetOptions.ForceUpdate);
                Assert.That(InvokeConfigure(TestPngPath, entry, out diagnostic), Is.True, diagnostic == null ? "configure failed without diagnostic" : StackMachineDiagnostic.Format(diagnostic, "configure failed"));
                var importer = (TextureImporter)AssetImporter.GetAtPath(TestPngPath);
                Assert.That(importer.sRGBTexture, Is.True);
                Assert.That(importer.alphaIsTransparency, Is.False);
            }
            finally { AssetDatabase.DeleteAsset(TestPngPath); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(preservedTexture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Encode_RejectsUnpublishedUnreadableTexture2D()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; UrpUnlitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                sampler.Apply(false, true);
                source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler);
                target = new Material(source); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);
                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishTextureSourceNotReadable"));
            }
            finally { mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Encode_AllowsUnreadablePersistentTextureSubAssetViaGpuReadback()
        {
            const string sourcePath = ShapeSyncTestAssetPaths.Spec17TexturePublishRoot + "_embedded.asset";
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; UrpUnlitMaterialShaderAdapter adapter = null; ScriptableObject container = null;
            try
            {
                AssetDatabase.DeleteAsset(sourcePath);
                container = ScriptableObject.CreateInstance<ScriptableObject>();
                AssetDatabase.CreateAsset(container, sourcePath);
                sampler = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                sampler.SetPixel(0, 0, Color.white); sampler.Apply(false, true);
                AssetDatabase.AddObjectToAsset(sampler, sourcePath); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
                sampler = FindTextureSubAsset(sourcePath);
                Assert.That(sampler, Is.Not.Null);
                Assert.That(sampler.isReadable, Is.False);

                source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler);
                target = new Material(source); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);
                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                SetCapability(() => true); SetReadback(_ => new byte[16]); SetPngEncoder(_ => new byte[] { 1, 2, 3 });
                Assert.That(InvokeEncode(entries.GetValue(0), out byte[] png, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(png, Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
            finally
            {
                SetCapability(() => SystemInfo.supportsAsyncGPUReadback); SetReadback(null); SetPngEncoder(ImageConversion.EncodeToPNG);
                mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(adapter); AssetDatabase.DeleteAsset(sourcePath); if (container != null) UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void Collect_DeduplicatesSharedSubmeshTexture_AndRejectsInvalidPublishInput()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture texture = null; UrpUnlitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2); source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler);
                target = new Material(source); texture = CreateTexture(sampler); target.SetTexture("_BaseMap", texture); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                mesh = CreateMesh(source, target, adapter, 2);
                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(entries.Length, Is.EqualTo(1));
                Assert.That(PropertyNames(entries.GetValue(0)).Length, Is.EqualTo(1));
                Assert.That(InvokeConfigure(string.Empty, entries.GetValue(0), out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishTextureAssetPathRequired"));
                Assert.That(InvokeConfigure(ShapeSyncTestAssetPaths.ConsumerAssetPath("zgock/ShapeSync/Tests/EditMode/Spec17/__missing.png"), entries.GetValue(0), out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PublishTextureImporterMissing"));
            }
            finally { mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(texture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Collect_DeduplicatesPreservedTextureForSharedTargetAcrossMaterialIds()
        {
            InMemoryHumanoidMesh mesh = null; Material sourceA = null; Material sourceB = null; Material target = null; Texture2D sampler = null; RenderTexture texture = null; UrpLitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2); sampler.SetPixel(0, 0, Color.white); sampler.Apply(false, false);
                sourceA = new Material(Shader.Find("Universal Render Pipeline/Lit")); sourceA.SetTexture("_EmissionMap", sampler);
                sourceB = new Material(sourceA); target = new Material(sourceA); texture = CreateTexture(sampler); target.SetTexture("_EmissionMap", texture);
                adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
                mesh = CreateMesh(new[] { sourceA, sourceB }, new[] { target, target }, adapter, new[] { new MaterialId("slot-a", "body"), new MaterialId("slot-b", "body") });

                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(entries.Length, Is.EqualTo(1));
                object entry = entries.GetValue(0);
                Assert.That(entry.GetType().GetProperty("Semantic", Flags).GetValue(entry).ToString(), Is.EqualTo("Preserved"));
                Assert.That(PropertyNames(entry), Is.EqualTo(new[] { "_EmissionMap" }));
            }
            finally { mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(sourceA); UnityEngine.Object.DestroyImmediate(sourceB); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(texture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void CollectAndEncode_RejectsSlotMismatchAndNonRgbaHalfTexture()
        {
            Material source = null; Material target = null; Texture2D sampler = null; RenderTexture invalid = null; UrpUnlitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh mesh = null;
            try
            {
                sampler = new Texture2D(2, 2); source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler); target = new Material(source);
                invalid = new RenderTexture(2, 2, 0, RenderTextureFormat.ARGB32); invalid.Create(); target.SetTexture("_BaseMap", invalid); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);
                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic format), Is.False); Assert.That(format.domainCode, Is.EqualTo("PublishTextureFormatInvalid"));
                Assert.That(format.detail, Does.Contain("property=_BaseMap"));
            }
            finally { mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(invalid); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Encode_RejectsUncreatedTextureAndGpuCapability()
        {
            Material source = null; Material target = null; Texture2D sampler = null; RenderTexture texture = null; UrpUnlitMaterialShaderAdapter adapter = null; InMemoryHumanoidMesh mesh = null;
            try
            {
                sampler = new Texture2D(2, 2); source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler); target = new Material(source); texture = new RenderTexture(new RenderTextureDescriptor(2, 2, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0)); target.SetTexture("_BaseMap", texture); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);
                Assert.That(InvokeCollect(mesh, out Array entries, out StackMachineDiagnostic collect), Is.True, collect?.message);
                Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic uncreated), Is.False); Assert.That(uncreated.domainCode, Is.EqualTo("PublishRenderTextureRequired"));
                texture.Create(); SetCapability(() => false);
                Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic capability), Is.False); Assert.That(capability.domainCode, Is.EqualTo("PublishGpuReadbackUnsupported"));
            }
            finally { SetCapability(() => SystemInfo.supportsAsyncGPUReadback); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(texture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Encode_RejectsEmptyPngOutput()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture texture = null; UrpUnlitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2); source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler); target = new Material(source); texture = CreateTexture(sampler); target.SetTexture("_BaseMap", texture); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter);
                Assert.That(InvokeCollect(mesh, out Array entries, out _), Is.True); SetPngEncoder(_ => null);
                Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic diagnostic), Is.False); Assert.That(diagnostic.domainCode, Is.EqualTo("PublishPngEncodeFailed"));
            }
            finally { SetPngEncoder(ImageConversion.EncodeToPNG); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(texture); UnityEngine.Object.DestroyImmediate(adapter); }
        }

        [Test]
        public void Encode_RejectsReadbackErrorAndInvalidByteLength()
        {
            InMemoryHumanoidMesh mesh = null; Material source = null; Material target = null; Texture2D sampler = null; RenderTexture texture = null; UrpUnlitMaterialShaderAdapter adapter = null;
            try
            {
                sampler = new Texture2D(2, 2); source = new Material(Shader.Find("Universal Render Pipeline/Unlit")); source.SetTexture("_BaseMap", sampler); target = new Material(source); texture = CreateTexture(sampler); target.SetTexture("_BaseMap", texture); adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); mesh = CreateMesh(source, target, adapter); Assert.That(InvokeCollect(mesh, out Array entries, out _), Is.True);
                SetReadback(_ => null); Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic error), Is.False); Assert.That(error.domainCode, Is.EqualTo("PublishGpuReadbackFailed"));
                SetReadback(_ => new byte[1]); Assert.That(InvokeEncode(entries.GetValue(0), out _, out StackMachineDiagnostic length), Is.False); Assert.That(length.domainCode, Is.EqualTo("PublishGpuReadbackLengthInvalid"));
            }
            finally { SetReadback(null); mesh?.Dispose(); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(sampler); Release(texture); UnityEngine.Object.DestroyImmediate(adapter); }
        }


        private static RenderTexture CreateTexture(Texture source) { var rt = new RenderTexture(new RenderTextureDescriptor(2, 2, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0) { sRGB = false }); rt.Create(); Graphics.Blit(source, rt); return rt; }
        private static Texture2D FindTextureSubAsset(string path) { foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path)) if (asset is Texture2D texture) return texture; return null; }
        private static void Release(RenderTexture texture) { if (texture == null) return; if (RenderTexture.active == texture) RenderTexture.active = null; texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
        private static InMemoryHumanoidMesh CreateMesh(Material source, Material target, MaterialShaderAdapter adapter, int submeshes = 1)
        {
            var unityMesh = new Mesh { subMeshCount = submeshes }; var result = new InMemoryHumanoidMesh(unityMesh);
            var materials = new Material[submeshes]; var slots = new HumanoidBuildMaterialSlot[submeshes];
            for (int i = 0; i < submeshes; i++) { materials[i] = target; slots[i] = new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), i, source, adapter); }
            Invoke(result, "TrySetMaterials", new object[] { materials, null });
            Invoke(result, "TrySetMaterialSlots", new object[] { slots, null });
            return result;
        }
        private static InMemoryHumanoidMesh CreateMesh(Material[] sources, Material[] targets, MaterialShaderAdapter adapter, MaterialId[] ids)
        {
            var unityMesh = new Mesh { subMeshCount = targets.Length }; var result = new InMemoryHumanoidMesh(unityMesh);
            var slots = new HumanoidBuildMaterialSlot[targets.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = new HumanoidBuildMaterialSlot(ids[i], i, sources[i], adapter);
            Invoke(result, "TrySetMaterials", new object[] { targets, null });
            Invoke(result, "TrySetMaterialSlots", new object[] { slots, null });
            return result;
        }
        private static bool InvokeCollect(InMemoryHumanoidMesh mesh, out Array entries, out StackMachineDiagnostic diagnostic) { object[] args = { mesh, null, null }; bool ok = (bool)Invoke(Service, "TryCollect", args); entries = ((IEnumerable)args[1] as IEnumerable).CastToArray(); diagnostic = (StackMachineDiagnostic)args[2]; return ok; }
        private static bool InvokeEncode(object entry, out byte[] png, out StackMachineDiagnostic diagnostic) { object[] args = { entry, null, null }; bool ok = (bool)Invoke(Service, "TryEncodePng", args); png = (byte[])args[1]; diagnostic = (StackMachineDiagnostic)args[2]; return ok; }
        private static bool InvokeConfigure(string path, object entry, out StackMachineDiagnostic diagnostic) { object[] args = { path, entry, null }; bool ok = (bool)Invoke(Service, "TryConfigureImporter", args); diagnostic = (StackMachineDiagnostic)args[2]; return ok; }
        private static object FindEntry(Array entries, string propertyName) { foreach (object entry in entries) foreach (string name in PropertyNames(entry)) if (name == propertyName) return entry; throw new AssertionException("Missing entry: " + propertyName); }
        private static string[] PropertyNames(object entry) { var values = (IEnumerable)entry.GetType().GetProperty("PropertyNames", Flags).GetValue(entry); return values.CastToArray().CastStrings(); }
        private static readonly BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Type Service => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidTexturePublishReadback", true);
        private static void SetCapability(Func<bool> value) => Service.GetField("AsyncGpuReadbackSupported", Flags).SetValue(null, value);
        private static void SetPngEncoder(Func<Texture2D, byte[]> value) => Service.GetField("PngEncoder", Flags).SetValue(null, value);
        private static void SetReadback(Func<RenderTexture, byte[]> value) { var field = Service.GetField("ReadbackRgba32", Flags); field.SetValue(null, value ?? (Func<RenderTexture, byte[]>)Service.GetMethod("DefaultReadbackRgba32", Flags).CreateDelegate(typeof(Func<RenderTexture, byte[]>))); }
        private static object Invoke(object instance, string name, object[] args) => instance.GetType().GetMethod(name, Flags).Invoke(instance, args);
        private static object Invoke(Type type, string name, object[] args) => type.GetMethod(name, Flags).Invoke(null, args);
    }
    internal static class EnumerableTestExtensions { internal static Array CastToArray(this IEnumerable values) { var list = new System.Collections.Generic.List<object>(); foreach (object value in values) list.Add(value); return list.ToArray(); } internal static string[] CastStrings(this Array values) { var result = new string[values.Length]; for (int i = 0; i < values.Length; i++) result[i] = (string)values.GetValue(i); return result; } }
}
