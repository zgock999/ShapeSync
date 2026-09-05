// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Accepts real AtlasBaker page completions through the closed Spec18.3 Oracle boundary.</summary>
    public sealed class AtlasBakerOracleAcceptanceTests
    {
        private static readonly AtlasImageOracle.PixelTolerance Tolerance = AtlasOracleTolerances.Default;
        private static readonly Vector2[] Samples = { Vector2.zero, new Vector2(.5f, .5f), Vector2.one };

        [UnityTest]
        public IEnumerator Execute_RealBakerPages_AreAcceptedByClosedImageMetamorphicAndCrossOracles()
        {
            Assert.That(AtlasOracleFixture.TryCreate(FixtureDocument(), out AtlasOracleFixture fixture, out StackMachineDiagnostic fixtureDiagnostic), Is.True, fixtureDiagnostic?.message);
            Assert.That(fixture.Layout.TryGetCell(new MaterialId("", "body"), out AtlasLayoutCell cell), Is.True);
            int innerWidth = cell.Width - cell.Gutter * 2;
            int innerHeight = cell.Height - cell.Gutter * 2;
            Texture2D baseSource = Gradient(innerWidth, innerHeight, new Color(.5f, .5f, .5f, 1f));
            Texture2D normalSeed = Gradient(innerWidth, innerHeight, new Color(.5f, .5f, 1f, 1f));
            RenderTexture normalSource = SourceRenderTexture(normalSeed);
            Texture2D baseReference = null;
            Texture2D normalReference = null;
            var executions = new AtlasBakerExecutionResult[1];
            try
            {
                AtlasBakerResult logical = Logical(baseSource, normalSource);
                yield return Execute(logical, executions);
                using (AtlasBakerExecutionResult execution = executions[0])
                {
                    Assert.That(execution.Pages, Has.Count.EqualTo(2));
                    RenderTexture basePage = Page(execution, 0, AtlasTextureSemantic.BaseColor);
                    RenderTexture normalPage = Page(execution, 0, AtlasTextureSemantic.Normal);
                    baseReference = ExpectedPage(baseSource, cell, fixture.Layout.PageExtent, Color.clear);
                    normalReference = ExpectedPage(normalSeed, cell, fixture.Layout.PageExtent, new Color(.5f, .5f, 1f, 1f));
                    AssertImageAndMetamorphic(basePage, baseReference, baseSource, cell, AtlasTextureSemantic.BaseColor, true);
                    AssertImageAndMetamorphic(normalPage, normalReference, normalSource, cell, AtlasTextureSemantic.Normal, false);

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
                            RenderTexture actual = isNormal ? normalPage : basePage;
                            Texture2D reference = isNormal ? normalReference : baseReference;
                            Texture source = isNormal ? normalSource : baseSource;
                            bool linear = !isNormal;
                            bool primary = AtlasImageOracle.TryCompare(actual, reference, Probes(reference, cell), context.Semantic, linear, Tolerance, out _, out StackMachineDiagnostic primaryDiagnostic);
                            bool metamorphic = AtlasImageMetamorphicOracle.TryValidate(source, actual, cell, fixture.Layout.PageExtent, Vector2.one, Vector2.zero, Samples, AtlasUvs(cell, fixture.Layout.PageExtent), context.Semantic, linear, Tolerance, out _, out StackMachineDiagnostic metamorphicDiagnostic);
                            evidence.Add(new AtlasCrossOracle.Evidence(context, primary, primaryDiagnostic, primary ? null : context, metamorphic, metamorphicDiagnostic, metamorphic ? null : context));
                        }
                    }
                    Assert.That(AtlasCrossOracle.TryValidate(fixture, evidence, out StackMachineDiagnostic crossDiagnostic), Is.True, crossDiagnostic?.message);
                }
            }
            finally
            {
                Object.DestroyImmediate(baseReference); Object.DestroyImmediate(normalReference);
                Object.DestroyImmediate(baseSource); Object.DestroyImmediate(normalSeed); Release(normalSource);
            }
        }

        [UnityTest]
        public IEnumerator Execute_RealBakerPage_WrongDestinationIsRejectedByClosedImageOracle()
        {
            Assert.That(AtlasOracleFixture.TryCreate(FixtureDocument(), out AtlasOracleFixture fixture, out StackMachineDiagnostic fixtureDiagnostic), Is.True, fixtureDiagnostic?.message);
            Assert.That(fixture.Layout.TryGetCell(new MaterialId("", "body"), out AtlasLayoutCell actualCell), Is.True);
            Texture2D source = Gradient(actualCell.Width - actualCell.Gutter * 2, actualCell.Height - actualCell.Gutter * 2, new Color(.3f, .2f, .7f, 1f));
            Texture2D reference = null;
            Texture2D placeholder = ShaderNoneNormal();
            var executions = new AtlasBakerExecutionResult[1];
            try
            {
                AtlasBakerResult logical = Logical(source, placeholder, true);
                yield return Execute(logical, executions);
                using (AtlasBakerExecutionResult execution = executions[0])
                {
                    Assert.That(execution.Pages, Has.Count.EqualTo(1));
                    var expectedCell = new AtlasLayoutCell(actualCell.MaterialId, actualCell.PageIndex, actualCell.X + 1, actualCell.Y, actualCell.Width, actualCell.Height, actualCell.Gutter);
                    reference = ExpectedPage(source, expectedCell, 512, Color.clear);
                    Assert.That(AtlasImageOracle.TryCompare(Page(execution, 0, AtlasTextureSemantic.BaseColor), reference, Probes(reference, expectedCell), AtlasTextureSemantic.BaseColor, true, Tolerance, out _, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(diagnostic.domain, Is.EqualTo("atlas"));
                }
            }
            finally { Object.DestroyImmediate(reference); Object.DestroyImmediate(source); Object.DestroyImmediate(placeholder); }
        }

        [UnityTest]
        public IEnumerator Execute_RealBakerPage_DownscaledSource_IsAcceptedByClosedImageOracle()
        {
            Assert.That(AtlasOracleFixture.TryCreate(FixtureDocument(), out AtlasOracleFixture fixture, out StackMachineDiagnostic fixtureDiagnostic), Is.True, fixtureDiagnostic?.message);
            Assert.That(fixture.Layout.TryGetCell(new MaterialId("", "body"), out AtlasLayoutCell cell), Is.True);
            int innerWidth = cell.Width - cell.Gutter * 2;
            int innerHeight = cell.Height - cell.Gutter * 2;
            Texture2D source = ResampleGradient(innerWidth * 2, innerHeight * 2);
            Texture2D placeholder = ShaderNoneNormal();
            Texture2D reference = null;
            var executions = new AtlasBakerExecutionResult[1];
            try
            {
                yield return Execute(Logical(source, placeholder, true), executions);
                using (AtlasBakerExecutionResult execution = executions[0])
                {
                    Assert.That(execution.Pages, Has.Count.EqualTo(1));
                    reference = ExpectedResampledPage(source, cell, fixture.Layout.PageExtent, Color.clear);
                    Assert.That(AtlasImageOracle.TryCompare(Page(execution, 0, AtlasTextureSemantic.BaseColor), reference, new[] { new AtlasImageOracle.Probe(0, 0, reference.GetPixel(0, 0)) }, AtlasTextureSemantic.BaseColor, true, Tolerance, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, $"resampled Baker page: {diagnostic?.message}; max={comparison.MaxAbsoluteError}; exceeded={comparison.ExceededPixelCount}/{comparison.PixelCount}");
                }
            }
            finally { Object.DestroyImmediate(reference); Object.DestroyImmediate(source); Object.DestroyImmediate(placeholder); }
        }

        private static IEnumerator Execute(AtlasBakerResult logical, AtlasBakerExecutionResult[] resultSlot)
        {
            resultSlot[0] = null;
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            using (var operation = new AtlasBakerExecutionOperation(logical, new EditModeAtlasBakerPageExecutor(compute)))
            {
                for (int i = 0; i < 240 && operation.Status == AtlasBakerExecutionStatus.Pending; i++)
                {
                    operation.Pump();
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }
                Assert.That(operation.Status, Is.EqualTo(AtlasBakerExecutionStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                Assert.That(operation.TryTakeResult(out resultSlot[0], out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            }
        }

        private static AtlasBakerResult Logical(Texture baseColor, Texture normal, bool normalIsNeutral = false)
        {
            var id = new MaterialId("", "body");
            AtlasSchemaDocument schema = FixtureDocument();
            var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source:body") });
            using (var operation = new AtlasBakerOperation(schema, identity, new[] { new AtlasBakerMaterialInput(id, baseColor, normal, normalIsNeutral) }))
            {
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message);
                Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                return result;
            }
        }

        private static RenderTexture Page(AtlasBakerExecutionResult result, int pageIndex, AtlasTextureSemantic semantic)
        {
            foreach (AtlasBakerPageCompletion page in result.Pages)
                if (page.PageIndex == pageIndex && page.Semantic == semantic) return page.Texture;
            Assert.Fail($"Missing page {pageIndex}/{semantic}.");
            return null;
        }

        private static void AssertImageAndMetamorphic(RenderTexture actual, Texture2D reference, Texture source, AtlasLayoutCell cell, AtlasTextureSemantic semantic, bool linear)
        {
            Assert.That(AtlasImageOracle.TryCompare(actual, reference, Probes(reference, cell), semantic, linear, Tolerance, out AtlasImageOracle.Comparison image, out StackMachineDiagnostic imageDiagnostic), Is.True, $"image {semantic}: {imageDiagnostic?.message}; max={image.MaxAbsoluteError}");
            Assert.That(AtlasImageMetamorphicOracle.TryValidate(source, actual, cell, actual.width, Vector2.one, Vector2.zero, Samples, AtlasUvs(cell, actual.width), semantic, linear, Tolerance, out AtlasImageOracle.Comparison metamorphic, out StackMachineDiagnostic metamorphicDiagnostic), Is.True, $"metamorphic {semantic}: {metamorphicDiagnostic?.message}; max={metamorphic.MaxAbsoluteError}");
        }

        private static AtlasCrossOracle.Evidence MeshEvidence(AtlasOracleFixture fixture, AtlasOracleEntryMetadata context)
        {
            var before = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, uv = new[] { Vector2.zero, Vector2.right, Vector2.up } };
            before.SetTriangles(new[] { 0, 1, 2 }, 0);
            var after = Object.Instantiate(before);
            try
            {
                Assert.That(fixture.Layout.TryGetCell(context.MaterialId, out AtlasLayoutCell cell), Is.True);
                Vector2[] uv = after.uv;
                for (int i = 0; i < uv.Length; i++) uv[i] = AtlasUvTransform.Apply(before.uv[i], Vector2.one, Vector2.zero, cell, fixture.Layout.PageExtent);
                after.uv = uv;
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
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source:body") }), new[] { new AtlasSchemaEntry(id, 0, 2, 2, false, 0) });
        }

        private static Texture2D Gradient(int width, int height, Color bias)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = bias;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Texture2D ResampleGradient(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width, v = (y + .5f) / height;
                pixels[y * width + x] = new Color(.125f + .75f * u, .125f + .75f * v, .25f + .5f * (u + v) * .5f, 1f);
            }
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Texture2D ShaderNoneNormal()
        {
            var texture = Gradient(8, 8, new Color(.5f, .5f, 1f, 1f));
            texture.name = "DatabaseRenamedNormal";
            return texture;
        }

        private static Texture2D ExpectedPage(Texture2D source, AtlasLayoutCell cell, int extent, Color clear)
        {
            var page = new Texture2D(extent, extent, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[extent * extent];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++) pixels[(cell.Y + cell.Gutter + y) * extent + cell.X + cell.Gutter + x] = source.GetPixel(x, y);
            page.SetPixels(pixels); page.Apply(false, false); return page;
        }

        private static Texture2D ExpectedResampledPage(Texture2D source, AtlasLayoutCell cell, int extent, Color clear)
        {
            var page = new Texture2D(extent, extent, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[extent * extent];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            int width = cell.Width - cell.Gutter * 2, height = cell.Height - cell.Gutter * 2;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[(cell.Y + cell.Gutter + y) * extent + cell.X + cell.Gutter + x] = SampleBilinearClamp(source, new Vector2((x + .5f) / width, (y + .5f) / height));
            page.SetPixels(pixels); page.Apply(false, false); return page;
        }

        private static Color SampleBilinearClamp(Texture2D texture, Vector2 uv)
        {
            float x = Mathf.Clamp01(uv.x) * texture.width - .5f, y = Mathf.Clamp01(uv.y) * texture.height - .5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, texture.width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, texture.height - 1), x1 = Mathf.Min(x0 + 1, texture.width - 1), y1 = Mathf.Min(y0 + 1, texture.height - 1);
            return Color.Lerp(Color.Lerp(texture.GetPixel(x0, y0), texture.GetPixel(x1, y0), x - Mathf.Floor(x)), Color.Lerp(texture.GetPixel(x0, y1), texture.GetPixel(x1, y1), x - Mathf.Floor(x)), y - Mathf.Floor(y));
        }

        private static AtlasImageOracle.Probe[] Probes(Texture2D reference, AtlasLayoutCell cell) => new[]
        {
            new AtlasImageOracle.Probe(cell.X, cell.Y, reference.GetPixel(cell.X, cell.Y)),
            new AtlasImageOracle.Probe(cell.X + cell.Gutter, cell.Y + cell.Gutter, reference.GetPixel(cell.X + cell.Gutter, cell.Y + cell.Gutter)),
            new AtlasImageOracle.Probe(cell.X + cell.Gutter + 8, cell.Y + cell.Gutter + 8, reference.GetPixel(cell.X + cell.Gutter + 8, cell.Y + cell.Gutter + 8))
        };
        private static Vector2[] AtlasUvs(AtlasLayoutCell cell, int extent) { var result = new Vector2[Samples.Length]; for (int i = 0; i < result.Length; i++) result[i] = AtlasUvTransform.Apply(Samples[i], Vector2.one, Vector2.zero, cell, extent); return result; }
        private static RenderTexture SourceRenderTexture(Texture2D source) { var result = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); result.Create(); Graphics.Blit(source, result); return result; }
        private static void Release(RenderTexture texture) { if (texture == null) return; if (RenderTexture.active == texture) RenderTexture.active = null; texture.Release(); Object.DestroyImmediate(texture); }
    }
}
