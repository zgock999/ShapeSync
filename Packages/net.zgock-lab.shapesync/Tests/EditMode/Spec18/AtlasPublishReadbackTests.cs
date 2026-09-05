// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Fixes the Spec18 atlas-only readback and importer boundary independently of normal compiler texture publishing.</summary>
    public sealed class AtlasPublishReadbackTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec18AtlasPublishReadbackRoot;
        private static string PngPath => Root + "/page.png";
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void ConfigureAtlasImporter_4096PageUsesFixedPolicyWithoutDownscale()
        {
            RenderTexture page = null;
            try
            {
                DeleteFolder(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec18"), "__AtlasPublishReadback");
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); File.WriteAllBytes(PngPath, ImageConversion.EncodeToPNG(source)); UnityEngine.Object.DestroyImmediate(source); AssetDatabase.ImportAsset(PngPath, ImportAssetOptions.ForceUpdate);
                page = new RenderTexture(new RenderTextureDescriptor(4096, 256, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0) { sRGB = false });
                Assert.That(InvokeConfigure(PngPath, CreateEntry("BaseColor", page, true), out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                var importer = (TextureImporter)AssetImporter.GetAtPath(PngPath);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default)); Assert.That(importer.sRGBTexture, Is.True); Assert.That(importer.alphaIsTransparency, Is.True);
                Assert.That(importer.mipmapEnabled, Is.False); Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp)); Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear)); Assert.That(importer.anisoLevel, Is.EqualTo(1));
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Compressed)); Assert.That(importer.compressionQuality, Is.EqualTo(50)); Assert.That(importer.crunchedCompression, Is.False); Assert.That(importer.maxTextureSize, Is.EqualTo(4096));
            }
            finally { Release(page); DeleteFolder(); }
        }

        [Test]
        public void EncodeBaseColor_RawViewMatchesLegacyPixelArrayPngBytes()
        {
            RenderTexture page = null;
            Func<RenderTexture, byte[]> previousReadback = null;
            try
            {
                page = new RenderTexture(new RenderTextureDescriptor(2, 2, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, 0) { sRGB = false }); page.Create();
                byte[] rgba = { 16, 64, 128, 32, 32, 96, 160, 64, 48, 128, 192, 96, 64, 160, 224, 128 };
                previousReadback = (Func<RenderTexture, byte[]>)Service.GetField("ReadbackRgba32", Flags).GetValue(null); Service.GetField("ReadbackRgba32", Flags).SetValue(null, new Func<RenderTexture, byte[]>(_ => rgba));
                Assert.That(InvokeEncode(CreateEntry("BaseColor", page, false), out byte[] actual, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                var legacy = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); legacy.LoadRawTextureData(rgba); legacy.Apply(false, false);
                Color32[] pixels = legacy.GetPixels32();
                for (int i = 0; i < pixels.Length; i++) { Color linear = pixels[i]; pixels[i] = new Color(Mathf.LinearToGammaSpace(linear.r), Mathf.LinearToGammaSpace(linear.g), Mathf.LinearToGammaSpace(linear.b), linear.a); }
                legacy.SetPixels32(pixels); legacy.Apply(false, false); byte[] expected = ImageConversion.EncodeToPNG(legacy); UnityEngine.Object.DestroyImmediate(legacy);
                Assert.That(actual, Is.EqualTo(expected));
            }
            finally { if (previousReadback != null) Service.GetField("ReadbackRgba32", Flags).SetValue(null, previousReadback); Release(page); }
        }

        private static object CreateEntry(string semanticName, RenderTexture page, bool isAtlasPage)
        {
            Type semantic = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidPublishTextureSemantic", true);
            Type entry = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidTextureReadbackEntry", true);
            return Activator.CreateInstance(entry, Flags, null, new object[] { default(MaterialId), Enum.Parse(semantic, semanticName), page, null, null, Array.Empty<string>(), isAtlasPage }, null);
        }
        private static bool InvokeConfigure(string path, object entry, out StackMachineDiagnostic diagnostic) { object[] args = { path, entry, null }; bool ok = (bool)Service.GetMethod("TryConfigureImporter", Flags).Invoke(null, args); diagnostic = (StackMachineDiagnostic)args[2]; return ok; }
        private static bool InvokeEncode(object entry, out byte[] png, out StackMachineDiagnostic diagnostic) { object[] args = { entry, null, null }; bool ok = (bool)Service.GetMethod("TryEncodePng", Flags).Invoke(null, args); png = (byte[])args[1]; diagnostic = (StackMachineDiagnostic)args[2]; return ok; }
        private static Type Service => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidTexturePublishReadback", true);
        private static void DeleteFolder() { AssetDatabase.DeleteAsset(Root); }
        private static void Release(RenderTexture texture) { if (texture == null) return; if (texture.IsCreated()) texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
    }
}
