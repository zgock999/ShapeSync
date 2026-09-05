// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class TextureRecordDispatchEditModeTests
    {
        [UnityTest]
        public IEnumerator DirectRecords_UseRectangleExtentAndTexture2DOrRenderTextureSources()
        {
            var texture2D = Solid(new Color(0.9f, 0.1f, 0.2f, 1f));
            var renderSource = Solid(new Color(0.1f, 0.8f, 0.3f, 1f));
            var renderTexture = new RenderTexture(256, 128, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
            try
            {
                Assert.That(renderTexture.Create(), Is.True);
                Graphics.Blit(renderSource, renderTexture);
                var document = Document("256 128 RECTSIZE $out 0.05 0.1 0.15 1 FILL_OUT $texture2D 8 16 32 24 32 48 32 24 PLACE $renderTexture 64 40 16 20 192 80 16 20 PLACE", "texture2D", "renderTexture", "out");
                var stub = new TextureRecipeStub(document, new[]
                {
                    new TextureBindingEntry { logicalName = "texture2D", kind = TextureBindingKind.SourceTexture, sourceTexture = texture2D },
                    new TextureBindingEntry { logicalName = "renderTexture", kind = TextureBindingKind.SourceTexture, sourceTexture = renderTexture },
                    new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
                });
                Assert.That(TextureExecutionPlan.TryCreate(stub, out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                using (var machine = new TextureEditModeStackMachine(compute))
                {
                    Assert.That(machine.Start(plan, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    for (int i = 0; i < 240 && machine.Status == TextureEditModeExecutionStatus.Pending; i++) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; machine.Pump(out _); }
                    Assert.That(machine.TryTakeCompletion(out TextureCompletion completion), Is.True);
                    using (completion)
                    {
                        Assert.That(completion.Texture.width, Is.EqualTo(256));
                        Assert.That(completion.Texture.height, Is.EqualTo(128));
                        AssertPixel(ReadPixel(completion.Texture, 0, 0), new Color(0.05f, 0.1f, 0.15f, 1f));
                        AssertPixel(ReadPixel(completion.Texture, 32, 48), new Color(0.9f, 0.1f, 0.2f, 1f));
                        AssertPixel(ReadPixel(completion.Texture, 192, 80), new Color(0.1f, 0.8f, 0.3f, 1f));
                        AssertPixel(ReadPixel(completion.Texture, 31, 48), new Color(0.05f, 0.1f, 0.15f, 1f));
                    }
                }
            }
            finally { Object.DestroyImmediate(texture2D); Object.DestroyImmediate(renderSource); renderTexture.Release(); Object.DestroyImmediate(renderTexture); }
        }

        private static MaterialRecipeDocument Document(string wordSource, params string[] names)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string name in names) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            return document;
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(256, 128, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[256 * 128]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Color ReadPixel(RenderTexture texture, int x, int y)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = texture;
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try { readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false); readback.Apply(false, false); return readback.GetPixel(0, 0); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }

        private static void AssertPixel(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.003f)); Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.003f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.003f)); Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.003f));
        }
    }
}
