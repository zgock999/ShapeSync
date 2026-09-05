// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasBakerExecutionOperationTests
    {
        [Test]
        public void EditModeRecipeBuilder_LowersOneCorePageToOneFillOutAndPlaceRecipe()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                AtlasBakerResult logical = Logical(baseColor, normal);
                AtlasBakerPagePlan page = logical.Pages[0];
                Assert.That(AtlasEditModeRecipeBuilder.TryCreate(page, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.DispatchPlan.OutputWidth, Is.EqualTo(512));
                Assert.That(plan.DispatchPlan.OutputHeight, Is.EqualTo(512));
                Assert.That(plan.DispatchPlan.Records, Has.Count.EqualTo(2));
                Assert.That(plan.DispatchPlan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.Fill));
                Assert.That(plan.DispatchPlan.Records[1].Operation, Is.EqualTo(TextureDispatchOperation.Place));
                Assert.That(plan.DispatchPlan.Records[1].Sources[0], Is.EqualTo("source0"));
                Assert.That(plan.DispatchPlan.Records[1].DestinationRectangle.X, Is.EqualTo(page.Operations[1].DestinationRectangle.X));
                Assert.That(plan.DispatchPlan.Records[1].DestinationRectangle.Y, Is.EqualTo(page.Operations[1].DestinationRectangle.Y));
                Assert.That(plan.BindingContext.TryGetBinding("source0", out TextureBinding binding), Is.True);
                Assert.That(binding.SourceTexture, Is.SameAs(baseColor));
            }
            finally { UnityEngine.Object.DestroyImmediate(baseColor); UnityEngine.Object.DestroyImmediate(normal); }
        }

        [Test]
        public void EditModeRecipeBuilder_RejectsMissingDuplicateOrLateFillOut()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                AtlasBakerPagePlan canonical = Logical(baseColor, normal).Pages[0];
                AtlasBakerPageOperation fill = canonical.Operations[0];
                AtlasBakerPageOperation place = canonical.Operations[1];
                AssertReject(new AtlasBakerPagePlan(canonical.PageIndex, canonical.Semantic, canonical.Extent, new[] { place }), "AtlasBakerFillRequired");
                AssertReject(new AtlasBakerPagePlan(canonical.PageIndex, canonical.Semantic, canonical.Extent, new[] { fill, fill, place }), "AtlasBakerFillOrderInvalid");
                AssertReject(new AtlasBakerPagePlan(canonical.PageIndex, canonical.Semantic, canonical.Extent, new[] { place, fill }), "AtlasBakerFillOrderInvalid");
            }
            finally { UnityEngine.Object.DestroyImmediate(baseColor); UnityEngine.Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_TransitionsPendingCompletionAndSingleTakeWithoutMutatingLogicalResult()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            var executor = new FakeExecutor(AtlasBakerExecutionStatus.Pending, AtlasBakerExecutionStatus.Succeeded, AtlasBakerExecutionStatus.Pending, AtlasBakerExecutionStatus.Succeeded);
            try
            {
                AtlasBakerResult logical = Logical(baseColor, normal);
                using (var operation = new AtlasBakerExecutionOperation(logical, executor))
                {
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(executor.StartCount, Is.EqualTo(1));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Succeeded));
                    Assert.That(operation.TryTakeResult(out AtlasBakerExecutionResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    using (result)
                    {
                        Assert.That(result.Pages, Has.Count.EqualTo(2));
                        Assert.That(result.Pages[0].PageIndex, Is.EqualTo(logical.Pages[0].PageIndex));
                        Assert.That(result.Pages[0].Semantic, Is.EqualTo(logical.Pages[0].Semantic));
                        Assert.That(result.Pages[0].Texture, Is.Not.Null);
                    }
                    Assert.That(executor.ReleaseCount, Is.EqualTo(2));
                    Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic duplicate), Is.False);
                    Assert.That(duplicate.domainCode, Is.EqualTo("AtlasBakerExecutionResultAlreadyTaken"));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baseColor); UnityEngine.Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_EmptyLogicalResult_SucceedsWithoutAConcreteExecutor()
        {
            AtlasBakerResult logical = EmptyLogical();
            using (var operation = new AtlasBakerExecutionOperation(logical, null))
            {
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Succeeded));
                Assert.That(operation.TryTakeResult(out AtlasBakerExecutionResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (result) Assert.That(result.Pages, Is.Empty);
            }
        }

        [Test]
        public void Pump_TerminalRejectAndCancelReleaseOnlyUntakenExecutorResources()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                AtlasBakerResult logical = Logical(baseColor, normal);
                var rejected = new FakeExecutor(AtlasBakerExecutionStatus.Failed) { RejectStart = true };
                using (var operation = new AtlasBakerExecutionOperation(logical, rejected))
                {
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Failed));
                    Assert.That(operation.Diagnostic.domainCode, Is.EqualTo("FakeStartRejected"));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Failed));
                    Assert.That(operation.TryTakeResult(out _, out StackMachineDiagnostic unavailable), Is.False);
                    Assert.That(unavailable.domainCode, Is.EqualTo("AtlasBakerExecutionResultUnavailable"));
                }

                var pending = new FakeExecutor(AtlasBakerExecutionStatus.Pending);
                using (var operation = new AtlasBakerExecutionOperation(logical, pending))
                {
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    operation.Cancel();
                    Assert.That(operation.Status, Is.EqualTo(AtlasBakerExecutionStatus.Cancelled));
                    Assert.That(pending.CancelCount, Is.EqualTo(1));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Cancelled));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baseColor); UnityEngine.Object.DestroyImmediate(normal); }
        }

        [Test]
        public void Pump_CancelOrFailureAfterEarlierPageCompletion_ReleasesUntakenPages()
        {
            Texture2D baseColor = Texture(128, 128);
            Texture2D normal = Texture(128, 128);
            try
            {
                AtlasBakerResult logical = Logical(baseColor, normal);
                var cancelled = new FakeExecutor(AtlasBakerExecutionStatus.Succeeded, AtlasBakerExecutionStatus.Cancelled);
                using (var operation = new AtlasBakerExecutionOperation(logical, cancelled))
                {
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Cancelled));
                    Assert.That(cancelled.ReleaseCount, Is.EqualTo(1));
                }

                var failed = new FakeExecutor(AtlasBakerExecutionStatus.Succeeded, AtlasBakerExecutionStatus.Failed);
                using (var operation = new AtlasBakerExecutionOperation(logical, failed))
                {
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending));
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Failed));
                    Assert.That(failed.ReleaseCount, Is.EqualTo(1));
                    Assert.That(failed.CancelCount, Is.EqualTo(1));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baseColor); UnityEngine.Object.DestroyImmediate(normal); }
        }

        [UnityTest]
        public IEnumerator EditModeExecutor_SubmitsCorePageAndTransfersLinearRenderTextureOnce()
        {
            RenderTexture baseColor = SolidRenderTexture(new Color(0.8f, 0.1f, 0.2f, 1f));
            Texture2D normal = Solid(new Color(0.2f, 0.4f, 0.6f, 1f));
            try
            {
                ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                Assert.That(compute, Is.Not.Null);
                AtlasBakerResult logical = Logical(baseColor, normal);
                using (var operation = new AtlasBakerExecutionOperation(logical, new EditModeAtlasBakerPageExecutor(compute)))
                {
                    for (int i = 0; i < 240 && operation.Status == AtlasBakerExecutionStatus.Pending; i++)
                    {
                        operation.Pump();
                        EditorApplication.QueuePlayerLoopUpdate();
                        yield return null;
                    }

                    Assert.That(operation.Status, Is.EqualTo(AtlasBakerExecutionStatus.Succeeded), operation.Diagnostic?.message + " :: " + operation.Diagnostic?.detail);
                    Assert.That(operation.TryTakeResult(out AtlasBakerExecutionResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    using (result)
                    {
                        Assert.That(result.Pages, Has.Count.EqualTo(2));
                        Assert.That(result.Pages[0].Texture.width, Is.EqualTo(512));
                        Assert.That(result.Pages[0].Texture.height, Is.EqualTo(512));
                        Assert.That(result.Pages[0].Texture.graphicsFormat, Is.EqualTo(UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat));
                        TextureDispatchRectangle destination = logical.Pages[0].Operations[1].DestinationRectangle;
                        AssertColor(PagePixel(result.Pages[0].Texture, destination.X + destination.Width / 2, destination.Y + destination.Height / 2), new Color(0.8f, 0.1f, 0.2f, 1f));
                        AssertColor(PagePixel(result.Pages[0].Texture, 511, 511), Color.clear);
                        Assert.That(result.Pages[1].Texture, Is.Not.Null);
                        AssertColor(PagePixel(result.Pages[1].Texture, destination.X + destination.Width / 2, destination.Y + destination.Height / 2), new Color(0.2f, 0.4f, 0.6f, 1f));
                        AssertColor(PagePixel(result.Pages[1].Texture, 511, 511), new Color(0.5f, 0.5f, 1f, 1f));
                    }
                }
            }
            finally { baseColor.Release(); UnityEngine.Object.DestroyImmediate(baseColor); UnityEngine.Object.DestroyImmediate(normal); }
        }

        private static AtlasBakerResult Logical(Texture baseColor, Texture normal)
        {
            var id = new MaterialId("outfit", "body");
            var entry = new AtlasSchemaEntry(id, 0, 2, 2, false, 0);
            var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source-body") });
            var schema = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { entry });
            using (var core = new AtlasBakerOperation(schema, identity, new[] { new AtlasBakerMaterialInput(id, baseColor, normal) }))
            {
                Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                Assert.That(core.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                return result;
            }
        }
        private static AtlasBakerResult EmptyLogical()
        {
            var id = new MaterialId("outfit", "excluded");
            var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source-excluded") });
            var schema = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { new AtlasSchemaEntry(id, 0, 2, 2, true, 0) });
            using (var core = new AtlasBakerOperation(schema, identity, Array.Empty<AtlasBakerMaterialInput>()))
            {
                Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                Assert.That(core.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Pages, Is.Empty);
                return result;
            }
        }

        private static Texture2D Texture(int width, int height) => new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
        private static Texture2D Solid(Color color)
        {
            var texture = Texture(128, 128);
            var pixels = new Color[128 * 128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }
        private static RenderTexture SolidRenderTexture(Color color)
        {
            Texture2D source = Solid(color);
            var target = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
            try { Assert.That(target.Create(), Is.True); Graphics.Blit(source, target); return target; }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        private static void AssertReject(AtlasBakerPagePlan page, string code)
        {
            Assert.That(AtlasEditModeRecipeBuilder.TryCreate(page, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo(code));
        }
        private static Color PagePixel(RenderTexture source, int x, int y)
        {
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, false, true);
            try
            {
                RenderTexture.active = source;
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixel(x, y);
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
        }
        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.003f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.003f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.003f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.003f));
        }

        private sealed class FakeExecutor : IAtlasBakerPageExecutor
        {
            private readonly Queue<AtlasBakerExecutionStatus> statuses;
            private AtlasBakerPagePlan page;
            internal FakeExecutor(params AtlasBakerExecutionStatus[] statuses) { this.statuses = new Queue<AtlasBakerExecutionStatus>(statuses); }
            internal bool RejectStart { get; set; }
            internal int StartCount { get; private set; }
            internal int CancelCount { get; private set; }
            internal int ReleaseCount { get; private set; }

            public bool Start(AtlasBakerPagePlan page, out StackMachineDiagnostic diagnostic)
            {
                StartCount++;
                this.page = page;
                if (!RejectStart) { diagnostic = null; return true; }
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "FakeStartRejected", "Test executor rejected the page.");
                return false;
            }

            public AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic)
            {
                AtlasBakerExecutionStatus status = statuses.Count == 0 ? AtlasBakerExecutionStatus.Succeeded : statuses.Dequeue();
                diagnostic = status == AtlasBakerExecutionStatus.Failed ? StackMachineDiagnostic.CreateDomain("atlas", "FakePumpFailed", "Test executor failed.") : null;
                return status;
            }

            public bool TryTakeCompletion(out AtlasBakerPageCompletion completion)
            {
                completion = null;
                if (page == null) return false;
                var texture = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
                if (!texture.Create()) return false;
                completion = new AtlasBakerPageCompletion(page.PageIndex, page.Semantic, texture, Release);
                page = null;
                return true;
            }

            public void Cancel() { CancelCount++; page = null; }
            public void Dispose() { }

            private void Release(RenderTexture texture)
            {
                ReleaseCount++;
                if (texture == null) return;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
