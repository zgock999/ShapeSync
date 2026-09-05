// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using Array = System.Array;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Connects the Spec18.4 record dispatcher to the closed Spec18.3 image acceptance gate.</summary>
    public sealed class TextureStackMachineOracleAcceptanceTests
    {
        private static readonly AtlasImageOracle.PixelTolerance Tolerance = AtlasOracleTolerances.Default;
        // KCopy preserves source texels. The closed UV transform is endpoint-based, so its exact
        // correspondence points are the two clamps and the centre; its broader interpolation law is
        // already covered by AtlasImageMetamorphicOracleTests.
        private static readonly Vector2[] Samples = { Vector2.zero, new Vector2(.5f, .5f), Vector2.one };

        [UnityTest]
        public IEnumerator RecordDispatch_ProducesLayer3EvidenceAcceptedByImageMetamorphicAndCrossOracles()
        {
            Assert.That(AtlasOracleFixture.TryCreate(FixtureDocument(), out AtlasOracleFixture fixture, out StackMachineDiagnostic fixtureDiagnostic), Is.True, fixtureDiagnostic?.message);
            Assert.That(fixture.Layout.TryGetCell(new MaterialId("", "body"), out AtlasLayoutCell cell), Is.True);
            int innerWidth = cell.Width - cell.Gutter * 2;
            int innerHeight = cell.Height - cell.Gutter * 2;
            // Exact binary-half fixture values isolate dispatch/UV correctness from format conversion;
            // the closed Oracle suite separately owns tolerance behaviour for arbitrary image values.
            Texture2D baseSource = Gradient(innerWidth, innerHeight, new Color(.5f, .5f, .5f, 1f));
            Texture2D normalSeed = Gradient(innerWidth, innerHeight, new Color(.5f, .5f, 1f, 1f));
            RenderTexture normalSource = SourceRenderTexture(normalSeed);
            Texture2D baseReference = null;
            Texture2D normalReference = null;
            var baseCompletion = new TextureCompletion[1];
            var normalCompletion = new TextureCompletion[1];
            var baseMachine = new TextureEditModeStackMachine[1];
            var normalMachine = new TextureEditModeStackMachine[1];
            try
            {
                yield return ExecutePage(baseSource, cell, fixture.Layout.PageExtent, Color.clear, baseMachine, baseCompletion);
                yield return ExecutePage(normalSource, cell, fixture.Layout.PageExtent, new Color(.5f, .5f, 1f, 1f), normalMachine, normalCompletion);
                baseReference = ExpectedPage(baseSource, cell, fixture.Layout.PageExtent, Color.clear);
                normalReference = ExpectedPage(normalSeed, cell, fixture.Layout.PageExtent, new Color(.5f, .5f, 1f, 1f));

                AssertImageAndMetamorphic(baseCompletion[0].Texture, baseReference, baseSource, cell, AtlasTextureSemantic.BaseColor, true);
                AssertImageAndMetamorphic(normalCompletion[0].Texture, normalReference, normalSource, cell, AtlasTextureSemantic.Normal, false);

                var evidence = new List<AtlasCrossOracle.Evidence>();
                foreach (AtlasOracleEntryMetadata context in fixture.Metadata)
                {
                    if (context.Layer == AtlasOracleLayer.Layout)
                    {
                        bool success = AtlasLayoutPropertyOracle.TryValidate(fixture.Document, fixture.Layout, out StackMachineDiagnostic diagnostic);
                        evidence.Add(new AtlasCrossOracle.Evidence(context, success, diagnostic, success ? null : context));
                    }
                    else if (context.Layer == AtlasOracleLayer.MeshUv)
                    {
                        evidence.Add(MeshEvidence(fixture, context));
                    }
                    else
                    {
                        bool isNormal = context.Semantic == AtlasTextureSemantic.Normal;
                        RenderTexture actual = isNormal ? normalCompletion[0].Texture : baseCompletion[0].Texture;
                        Texture2D reference = isNormal ? normalReference : baseReference;
                        Texture source = isNormal ? normalSource : baseSource;
                        bool linear = !isNormal;
                        bool primary = AtlasImageOracle.TryCompare(actual, reference, Probes(reference, cell), context.Semantic, linear, Tolerance, out _, out StackMachineDiagnostic primaryDiagnostic);
                        Vector2[] atlasUvs = AtlasUvs(cell, fixture.Layout.PageExtent);
                        bool metamorphic = AtlasImageMetamorphicOracle.TryValidate(source, actual, cell, fixture.Layout.PageExtent, Vector2.one, Vector2.zero, Samples, atlasUvs, context.Semantic, linear, Tolerance, out _, out StackMachineDiagnostic metamorphicDiagnostic);
                        evidence.Add(new AtlasCrossOracle.Evidence(context, primary, primaryDiagnostic, primary ? null : context, metamorphic, metamorphicDiagnostic, metamorphic ? null : context));
                    }
                }
                Assert.That(AtlasCrossOracle.TryValidate(fixture, evidence, out StackMachineDiagnostic crossDiagnostic), Is.True, crossDiagnostic?.message);
            }
            finally
            {
                baseCompletion[0]?.Dispose(); normalCompletion[0]?.Dispose(); baseMachine[0]?.Dispose(); normalMachine[0]?.Dispose();
                Object.DestroyImmediate(baseReference); Object.DestroyImmediate(normalReference);
                Object.DestroyImmediate(baseSource); Object.DestroyImmediate(normalSeed); Release(normalSource);
            }
        }

        [UnityTest]
        public IEnumerator RecordDispatch_WrongLogicalDestination_IsRejectedByClosedImageOracle()
        {
            var source = Gradient(56, 56, new Color(.3f, .2f, .7f, 1f));
            var cell = new AtlasLayoutCell(new MaterialId("", "body"), 0, 32, 32, 64, 64, 4);
            var completion = new TextureCompletion[1];
            var machine = new TextureEditModeStackMachine[1];
            Texture2D reference = null;
            try
            {
                var shifted = new AtlasLayoutCell(cell.MaterialId, cell.PageIndex, cell.X + 1, cell.Y, cell.Width, cell.Height, cell.Gutter);
                yield return ExecutePage(source, shifted, 128, Color.clear, machine, completion);
                reference = ExpectedPage(source, cell, 128, Color.clear);
                Assert.That(AtlasImageOracle.TryCompare(completion[0].Texture, reference, Probes(reference, cell), AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domain, Is.EqualTo("atlas"));
            }
            finally { completion[0]?.Dispose(); machine[0]?.Dispose(); Object.DestroyImmediate(reference); Object.DestroyImmediate(source); }
        }

        [UnityTest]
        public IEnumerator RecordDispatch_DownscaledPlace_UsesTheEntireSourceRectangle()
        {
            Assert.That(AtlasOracleFixture.TryCreate(FixtureDocument(), out AtlasOracleFixture fixture, out StackMachineDiagnostic fixtureDiagnostic), Is.True, fixtureDiagnostic?.message);
            Assert.That(fixture.Layout.TryGetCell(new MaterialId("", "body"), out AtlasLayoutCell cell), Is.True);
            int innerWidth = cell.Width - cell.Gutter * 2;
            int innerHeight = cell.Height - cell.Gutter * 2;
            Texture2D source = ResampleGradient(innerWidth * 2, innerHeight * 2);
            Texture2D reference = null;
            var completion = new TextureCompletion[1];
            var machine = new TextureEditModeStackMachine[1];
            try
            {
                string words = $"{fixture.Layout.PageExtent} {fixture.Layout.PageExtent} RECTSIZE $out 0 0 0 0 FILL_OUT $source 0 0 {source.width} {source.height} {cell.X + cell.Gutter} {cell.Y + cell.Gutter} {innerWidth} {innerHeight} PLACE";
                yield return ExecutePage(words, fixture.Layout.PageExtent, fixture.Layout.PageExtent, new[] { Entry("source", source) }, machine, completion);
                reference = ExpectedResampledPage(source, cell, fixture.Layout.PageExtent, Color.clear);
                Assert.That(AtlasImageOracle.TryCompare(completion[0].Texture, reference, new[] { new AtlasImageOracle.Probe(0, 0, Color.clear) }, AtlasTextureSemantic.BaseColor, true, Tolerance, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, $"resampled PLACE: {diagnostic?.message}; max={comparison.MaxAbsoluteError}; exceeded={comparison.ExceededPixelCount}/{comparison.PixelCount}");
            }
            finally { completion[0]?.Dispose(); machine[0]?.Dispose(); Object.DestroyImmediate(reference); Object.DestroyImmediate(source); }
        }

        [UnityTest]
        public IEnumerator RecordDispatch_UpscaledPlace_ClampsBothSourceTapsAtEdges()
        {
            const int extent = 128, sourceExtent = 64, destinationExtent = 128;
            Texture2D source = ResampleGradient(sourceExtent, sourceExtent); Texture2D reference = null;
            var completion = new TextureCompletion[1]; var machine = new TextureEditModeStackMachine[1];
            try
            {
                yield return ExecutePage($"{extent} {extent} RECTSIZE $out 0 0 0 0 FILL_OUT $source 0 0 {sourceExtent} {sourceExtent} 0 0 {destinationExtent} {destinationExtent} PLACE", extent, extent, new[] { Entry("source", source) }, machine, completion);
                reference = ExpectedResampledPage(source, extent, extent, 0, 0, destinationExtent, destinationExtent, Color.clear);
                Assert.That(AtlasImageOracle.TryCompare(completion[0].Texture, reference, new[] { new AtlasImageOracle.Probe(0, 0, reference.GetPixel(0, 0)), new AtlasImageOracle.Probe(destinationExtent - 1, destinationExtent - 1, reference.GetPixel(destinationExtent - 1, destinationExtent - 1)) }, AtlasTextureSemantic.BaseColor, true, Tolerance, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, $"edge-clamped PLACE: {diagnostic?.message}; max={comparison.MaxAbsoluteError}; exceeded={comparison.ExceededPixelCount}/{comparison.PixelCount}");
            }
            finally { completion[0]?.Dispose(); machine[0]?.Dispose(); Object.DestroyImmediate(reference); Object.DestroyImmediate(source); }
        }

        [UnityTest]
        public IEnumerator RecordDispatch_DisjointOffsetSrgbSources_AreAcceptedByClosedImageAndMetamorphicOracles()
        {
            const int extent = 512;
            var leftCell = new AtlasLayoutCell(new MaterialId("", "left"), 0, 32, 48, 64, 64, 4);
            var rightCell = new AtlasLayoutCell(new MaterialId("", "right"), 0, 192, 224, 64, 64, 4);
            Texture2D left = SrgbSolid(64, 64, new Color(.5f, .25f, .125f, 1f));
            Texture2D right = SrgbSolid(64, 64, new Color(.25f, .5f, .125f, 1f));
            Texture2D linearLeft = null, linearRight = null, doubleLinearLeft = null, doubleLinearRight = null, reference = null, unnormalizedReference = null, doubleNormalizedReference = null;
            var completion = new TextureCompletion[1]; var machine = new TextureEditModeStackMachine[1];
            try
            {
                string words = $"{extent} {extent} RECTSIZE $out 0 0 0 1 FILL_OUT $left 4 8 56 56 {leftCell.X + leftCell.Gutter} {leftCell.Y + leftCell.Gutter} 56 56 PLACE $right 4 8 56 56 {rightCell.X + rightCell.Gutter} {rightCell.Y + rightCell.Gutter} 56 56 PLACE";
                yield return ExecutePage(words, extent, extent, new[] { Entry("left", left), Entry("right", right) }, machine, completion);
                linearLeft = LinearizeSrgb(left); linearRight = LinearizeSrgb(right);
                reference = ExpectedPage(extent, extent, Color.black, new Placement(linearLeft, 4, 8, leftCell.X + leftCell.Gutter, leftCell.Y + leftCell.Gutter, 56, 56), new Placement(linearRight, 4, 8, rightCell.X + rightCell.Gutter, rightCell.Y + rightCell.Gutter, 56, 56));
                unnormalizedReference = ExpectedPage(extent, extent, Color.black, new Placement(left, 4, 8, leftCell.X + leftCell.Gutter, leftCell.Y + leftCell.Gutter, 56, 56), new Placement(right, 4, 8, rightCell.X + rightCell.Gutter, rightCell.Y + rightCell.Gutter, 56, 56));
                doubleLinearLeft = DoubleLinearizeSrgb(linearLeft); doubleLinearRight = DoubleLinearizeSrgb(linearRight);
                doubleNormalizedReference = ExpectedPage(extent, extent, Color.black, new Placement(doubleLinearLeft, 4, 8, leftCell.X + leftCell.Gutter, leftCell.Y + leftCell.Gutter, 56, 56), new Placement(doubleLinearRight, 4, 8, rightCell.X + rightCell.Gutter, rightCell.Y + rightCell.Gutter, 56, 56));
                Vector2 scale = new Vector2(56f / 64f, 56f / 64f); Vector2 offset = new Vector2(4f / 64f, 8f / 64f);
                AssertImageAndMetamorphic(completion[0].Texture, reference, linearLeft, leftCell, AtlasTextureSemantic.BaseColor, true, scale, offset);
                AssertImageAndMetamorphic(completion[0].Texture, reference, linearRight, rightCell, AtlasTextureSemantic.BaseColor, true, scale, offset);
                Assert.That(AtlasImageOracle.TryCompare(completion[0].Texture, unnormalizedReference, Probes(unnormalizedReference, leftCell), AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out _), Is.False, "An sRGB source reference must be normalized to the linear TSM page domain exactly once.");
                Assert.That(AtlasImageOracle.TryCompare(completion[0].Texture, doubleNormalizedReference, Probes(doubleNormalizedReference, leftCell), AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out _), Is.False, "A linear source reference must not be normalized a second time.");
                Assert.That(ReadPixel(completion[0].Texture, leftCell.X + leftCell.Gutter - 1, leftCell.Y + leftCell.Gutter), Is.EqualTo(Color.black));
            }
            finally { completion[0]?.Dispose(); machine[0]?.Dispose(); Object.DestroyImmediate(reference); Object.DestroyImmediate(unnormalizedReference); Object.DestroyImmediate(doubleNormalizedReference); Object.DestroyImmediate(left); Object.DestroyImmediate(right); Object.DestroyImmediate(linearLeft); Object.DestroyImmediate(linearRight); Object.DestroyImmediate(doubleLinearLeft); Object.DestroyImmediate(doubleLinearRight); }
        }

        [UnityTest]
        public IEnumerator RecordDispatch_NonPotRectangle_IsAcceptedByClosedImageOracle()
        {
            const int width = 256, height = 128;
            Texture2D source = Gradient(64, 48, new Color(.5f, .5f, .5f, 1f)); Texture2D reference = null;
            var completion = new TextureCompletion[1]; var machine = new TextureEditModeStackMachine[1];
            try
            {
                yield return ExecutePage($"{width} {height} RECTSIZE $out 0 0 0 1 FILL_OUT $source 4 4 56 40 160 80 56 40 PLACE", width, height, new[] { Entry("source", source) }, machine, completion);
                reference = ExpectedPage(width, height, Color.black, new Placement(source, 4, 4, 160, 80, 56, 40));
                Assert.That(completion[0].Texture.width, Is.EqualTo(width)); Assert.That(completion[0].Texture.height, Is.EqualTo(height));
                Assert.That(AtlasImageOracle.TryCompare(completion[0].Texture, reference, new[] { new AtlasImageOracle.Probe(0, 0, Color.black), new AtlasImageOracle.Probe(160, 80, reference.GetPixel(160, 80)), new AtlasImageOracle.Probe(159, 80, Color.black) }, AtlasTextureSemantic.BaseColor, true, Tolerance, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, $"code={diagnostic?.domainCode} max={comparison.MaxAbsoluteError} exceeded={comparison.ExceededPixelCount}/{comparison.PixelCount}");
            }
            finally { completion[0]?.Dispose(); machine[0]?.Dispose(); Object.DestroyImmediate(reference); Object.DestroyImmediate(source); }
        }

        private static IEnumerator ExecutePage(Texture source, AtlasLayoutCell cell, int extent, Color clearColor, TextureEditModeStackMachine[] machineSlot, TextureCompletion[] completion)
        {
            string words = $"{extent} {extent} RECTSIZE $out {clearColor.r} {clearColor.g} {clearColor.b} {clearColor.a} FILL_OUT $source 0 0 {source.width} {source.height} {cell.X + cell.Gutter} {cell.Y + cell.Gutter} {source.width} {source.height} PLACE";
            return ExecutePage(words, extent, extent, new[] { Entry("source", source) }, machineSlot, completion);
        }

        private static IEnumerator ExecutePage(string words, int width, int height, TextureBindingEntry[] sources, TextureEditModeStackMachine[] machineSlot, TextureCompletion[] completion)
        {
            completion[0] = null;
            var document = new MaterialRecipeDocument { wordSource = words, outputLogicalName = "out", outputWidth = width, outputHeight = height };
            for (int i = 0; i < sources.Length; i++) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = sources[i].logicalName, declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var bindings = new TextureBindingEntry[sources.Length + 1]; Array.Copy(sources, bindings, sources.Length); bindings[bindings.Length - 1] = new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall };
            var stub = new TextureRecipeStub(document, bindings);
            Assert.That(TextureExecutionPlan.TryCreate(stub, out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            machineSlot[0] = new TextureEditModeStackMachine(compute);
            Assert.That(machineSlot[0].Start(plan, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
            for (int i = 0; i < 240 && machineSlot[0].Status == TextureEditModeExecutionStatus.Pending; i++) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; machineSlot[0].Pump(out _); }
            Assert.That(machineSlot[0].TryTakeCompletion(out completion[0]), Is.True);
        }

        private static void AssertImageAndMetamorphic(RenderTexture actual, Texture2D reference, Texture source, AtlasLayoutCell cell, AtlasTextureSemantic semantic, bool linear, Vector2? sourceUvSetScale = null, Vector2? sourceUvSetOffset = null)
        {
            Assert.That(actual, Is.Not.Null, "TSM completion must retain the page until its completion is disposed.");
            Assert.That(reference, Is.Not.Null);
            Assert.That(actual.width, Is.EqualTo(reference.width)); Assert.That(actual.height, Is.EqualTo(reference.height));
            Assert.That(Probes(reference, cell).Length, Is.GreaterThan(0));
            bool primary = AtlasImageOracle.TryCompare(actual, reference, Probes(reference, cell), semantic, linear, Tolerance, out AtlasImageOracle.Comparison image, out StackMachineDiagnostic imageDiagnostic);
            Assert.That(primary, Is.True, $"primary semantic={semantic} code={imageDiagnostic?.domainCode} max={image.MaxAbsoluteError} exceeded={image.ExceededPixelCount}/{image.PixelCount}");
            Assert.That(image.ExceededPixelRatio, Is.Zero);
            for (int i = 0; i < Samples.Length; i++)
            {
                bool sampleAccepted = AtlasImageMetamorphicOracle.TryValidate(source, actual, cell, actual.width, sourceUvSetScale ?? Vector2.one, sourceUvSetOffset ?? Vector2.zero, new[] { Samples[i] }, new[] { AtlasUvs(cell, actual.width)[i] }, semantic, linear, Tolerance, out AtlasImageOracle.Comparison sample, out StackMachineDiagnostic sampleDiagnostic);
                Assert.That(sampleAccepted, Is.True, $"metamorphic sample={i} uv={Samples[i]} semantic={semantic} code={sampleDiagnostic?.domainCode} max={sample.MaxAbsoluteError} exceeded={sample.ExceededPixelCount}/{sample.PixelCount}");
            }
            bool metamorphicAccepted = AtlasImageMetamorphicOracle.TryValidate(source, actual, cell, actual.width, sourceUvSetScale ?? Vector2.one, sourceUvSetOffset ?? Vector2.zero, Samples, AtlasUvs(cell, actual.width), semantic, linear, Tolerance, out AtlasImageOracle.Comparison metamorphic, out StackMachineDiagnostic metamorphicDiagnostic);
            Assert.That(metamorphicAccepted, Is.True, $"metamorphic semantic={semantic} code={metamorphicDiagnostic?.domainCode} max={metamorphic.MaxAbsoluteError} exceeded={metamorphic.ExceededPixelCount}/{metamorphic.PixelCount}");
            Assert.That(metamorphic.ExceededPixelRatio, Is.Zero);
        }

        private static AtlasCrossOracle.Evidence MeshEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            var before = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, uv = new[] { Vector2.zero, Vector2.right, Vector2.up } }; before.SetTriangles(new[] { 0, 1, 2 }, 0);
            var after = Object.Instantiate(before);
            try
            {
                Assert.That(fixture.Layout.TryGetCell(context.MaterialId, out AtlasLayoutCell cell), Is.True);
                Vector2[] uv = after.uv; for (int i = 0; i < uv.Length; i++) uv[i] = AtlasUvTransform.Apply(before.uv[i], Vector2.one, Vector2.zero, cell, fixture.Layout.PageExtent); after.uv = uv;
                var contexts = new[] { new AtlasMeshStructureOracle.Context(context.MaterialId, 0, true, cell, fixture.Layout.PageExtent, Vector2.one, Vector2.zero, new AtlasMeshStructureOracle.MaterialState(new Vector4(1, 1, 0, 0), new Vector4(1, 1, 0, 0)), false, context.Semantic) };
                var state = new AtlasMeshStructureOracle.RendererState(new[] { context.MaterialId }, "root", "avatar");
                bool success = AtlasMeshStructureOracle.TryValidateForAtlasAcceptance(before, after, contexts, fixture.Layout, state, state, out StackMachineDiagnostic diagnostic);
                return new AtlasCrossOracle.Evidence(context, success, diagnostic, success ? null : context);
            }
            finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
        }

        private static AtlasSchemaDocument FixtureDocument()
        {
            var id = new MaterialId("", "body");
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source:body") }), new[] { new AtlasSchemaEntry(id, 0, 3, 3, false, 4) });
        }

        private static Texture2D Gradient(int width, int height, Color bias)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true); var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = bias;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Texture2D ResampleGradient(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true); var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width, v = (y + .5f) / height;
                pixels[y * width + x] = new Color(.125f + .75f * u, .125f + .75f * v, .25f + .5f * (u + v) * .5f, 1f);
            }
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Texture2D SrgbSolid(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false); var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Texture2D LinearizeSrgb(Texture2D source)
        {
            var target = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            target.Create();
            try
            {
                Graphics.Blit(source, target);
                RenderTexture previous = RenderTexture.active; var copy = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true);
                try { RenderTexture.active = target; copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false); copy.Apply(false, false); return copy; }
                finally { RenderTexture.active = previous; }
            }
            finally { if (RenderTexture.active == target) RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target); }
        }

        private static Texture2D DoubleLinearizeSrgb(Texture2D linearSource)
        {
            // Intentional erroneous second sRGB decode of already-linear values. This is reference-only;
            // production TSM/Baker must never execute this conversion.
            Color[] pixels = linearSource.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(Mathf.GammaToLinearSpace(pixels[i].r), Mathf.GammaToLinearSpace(pixels[i].g), Mathf.GammaToLinearSpace(pixels[i].b), pixels[i].a);
            var result = new Texture2D(linearSource.width, linearSource.height, TextureFormat.RGBAHalf, false, true);
            result.SetPixels(pixels); result.Apply(false, false); return result;
        }

        private static TextureBindingEntry Entry(string logicalName, Texture source) => new TextureBindingEntry { logicalName = logicalName, kind = TextureBindingKind.SourceTexture, sourceTexture = source };

        private readonly struct Placement
        {
            internal Placement(Texture2D source, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height) { Source = source; SourceX = sourceX; SourceY = sourceY; DestinationX = destinationX; DestinationY = destinationY; Width = width; Height = height; }
            internal Texture2D Source { get; } internal int SourceX { get; } internal int SourceY { get; } internal int DestinationX { get; } internal int DestinationY { get; } internal int Width { get; } internal int Height { get; }
        }

        private static Texture2D ExpectedPage(Texture2D source, AtlasLayoutCell cell, int extent, Color clear)
        {
            // TextureEditModeStackMachine completion is R16G16B16A16_SFloat; probes require its stored bits, not only tolerance equality.
            var page = new Texture2D(extent, extent, TextureFormat.RGBAHalf, false, true); var pixels = new Color[extent * extent]; for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++) pixels[(cell.Y + cell.Gutter + y) * extent + cell.X + cell.Gutter + x] = source.GetPixel(x, y);
            page.SetPixels(pixels); page.Apply(false, false); return page;
        }

        private static Texture2D ExpectedResampledPage(Texture2D source, AtlasLayoutCell cell, int extent, Color clear)
            => ExpectedResampledPage(source, extent, extent, cell.X + cell.Gutter, cell.Y + cell.Gutter, cell.Width - cell.Gutter * 2, cell.Height - cell.Gutter * 2, clear);

        private static Texture2D ExpectedResampledPage(Texture2D source, int pageWidth, int pageHeight, int destinationX, int destinationY, int destinationWidth, int destinationHeight, Color clear)
        {
            var page = new Texture2D(pageWidth, pageHeight, TextureFormat.RGBAHalf, false, true); var pixels = new Color[pageWidth * pageHeight];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            for (int y = 0; y < destinationHeight; y++) for (int x = 0; x < destinationWidth; x++) pixels[(destinationY + y) * pageWidth + destinationX + x] = SampleBilinearClamp(source, new Vector2((x + .5f) / destinationWidth, (y + .5f) / destinationHeight));
            page.SetPixels(pixels); page.Apply(false, false); return page;
        }

        private static Color SampleBilinearClamp(Texture2D texture, Vector2 uv)
        {
            float x = Mathf.Clamp01(uv.x) * texture.width - .5f, y = Mathf.Clamp01(uv.y) * texture.height - .5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), x1 = x0 + 1, y1 = y0 + 1;
            x0 = Mathf.Clamp(x0, 0, texture.width - 1); y0 = Mathf.Clamp(y0, 0, texture.height - 1); x1 = Mathf.Clamp(x1, 0, texture.width - 1); y1 = Mathf.Clamp(y1, 0, texture.height - 1);
            return Color.Lerp(Color.Lerp(texture.GetPixel(x0, y0), texture.GetPixel(x1, y0), x - Mathf.Floor(x)), Color.Lerp(texture.GetPixel(x0, y1), texture.GetPixel(x1, y1), x - Mathf.Floor(x)), y - Mathf.Floor(y));
        }

        private static Texture2D ExpectedPage(int width, int height, Color clear, params Placement[] placements)
        {
            var page = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true); var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            for (int p = 0; p < placements.Length; p++)
            {
                Placement placement = placements[p];
                for (int y = 0; y < placement.Height; y++) for (int x = 0; x < placement.Width; x++) pixels[(placement.DestinationY + y) * width + placement.DestinationX + x] = placement.Source.GetPixel(placement.SourceX + x, placement.SourceY + y);
            }
            page.SetPixels(pixels); page.Apply(false, false); return page;
        }

        private static AtlasImageOracle.Probe[] Probes(Texture2D reference, AtlasLayoutCell cell) => new[] { new AtlasImageOracle.Probe(cell.X, cell.Y, reference.GetPixel(cell.X, cell.Y)), new AtlasImageOracle.Probe(cell.X + cell.Gutter, cell.Y + cell.Gutter, reference.GetPixel(cell.X + cell.Gutter, cell.Y + cell.Gutter)), new AtlasImageOracle.Probe(cell.X + cell.Gutter + 8, cell.Y + cell.Gutter + 8, reference.GetPixel(cell.X + cell.Gutter + 8, cell.Y + cell.Gutter + 8)) };
        private static Vector2[] AtlasUvs(AtlasLayoutCell cell, int extent) { var result = new Vector2[Samples.Length]; for (int i = 0; i < result.Length; i++) result[i] = AtlasUvTransform.Apply(Samples[i], Vector2.one, Vector2.zero, cell, extent); return result; }
        private static RenderTexture SourceRenderTexture(Texture2D source) { var result = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); result.Create(); Graphics.Blit(source, result); return result; }
        private static Color ReadPixel(RenderTexture texture, int x, int y) { RenderTexture previous = RenderTexture.active; var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true); try { RenderTexture.active = texture; readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false); readback.Apply(false, false); return readback.GetPixel(0, 0); } finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); } }
        private static void Release(RenderTexture texture) { if (texture == null) return; if (RenderTexture.active == texture) RenderTexture.active = null; texture.Release(); Object.DestroyImmediate(texture); }
    }
}
