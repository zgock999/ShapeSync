// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Tests.Spec18;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>Focused acceptance for the Spec19.5 one-page PlayMode Atlas executor.</summary>
    public sealed class PlayModeAtlasBakerPageExecutorTests
    {
        [Test]
        public void Executor_RejectsMissingSceneLocalHost_WithTextureHostDiagnostic()
        {
            Texture2D source = Solid(Color.red);
            Texture2D normal = Solid(new Color(.5f, .5f, 1f, 1f));
            try
            {
                AtlasSchemaDocument schema = CreateSchema();
                using (var core = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), new[] { new AtlasBakerMaterialInput(new MaterialId("figure", "body"), source, normal) }))
                {
                    Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                    Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic logicalDiagnostic), Is.True, logicalDiagnostic?.message);
                    using (var executor = new PlayModeAtlasBakerPageExecutor((TextureStackMachineHost)null))
                    {
                        Assert.That(executor.Start(logical.Pages[0], out StackMachineDiagnostic diagnostic), Is.False);
                        Assert.That(diagnostic, Is.Not.Null);
                        Assert.That(diagnostic.domainCode, Is.EqualTo("HostRequired"));
                    }
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(normal); }
        }

        [UnityTest]
        public IEnumerator Executor_RejectsConcurrentStart_AndCancelsQueuedHandleWithoutHostResidue()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D source = Solid(Color.red);
            Texture2D normal = Solid(new Color(.5f, .5f, 1f, 1f));
            try
            {
                AtlasSchemaDocument schema = CreateSchema();
                using (var core = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), new[] { new AtlasBakerMaterialInput(new MaterialId("figure", "body"), source, normal) }))
                {
                    Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                    Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic logicalDiagnostic), Is.True, logicalDiagnostic?.message);
                    using (var executor = new PlayModeAtlasBakerPageExecutor(host))
                    {
                        Assert.That(executor.Start(logical.Pages[0], out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                        Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                        Assert.That(executor.Start(logical.Pages[1], out StackMachineDiagnostic busyDiagnostic), Is.False);
                        Assert.That(busyDiagnostic.domainCode, Is.EqualTo("PlayModeAtlasPageExecutorBusy"));

                        executor.Cancel();
                        Assert.That(host.PendingRequestCount, Is.Zero);
                        Assert.That(host.HasSubmittedRequest, Is.False);
                    }
                }
            }
            finally { Object.Destroy(root); Object.Destroy(source); Object.Destroy(normal); }
            yield return null;
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Executor_CleansRejectedFirstSegment_AndCanStartAgain()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D source = Solid(Color.red); Texture2D normal = Solid(new Color(.5f, .5f, 1f, 1f));
            TextureHallAllocation blocker = default;
            try
            {
                AtlasSchemaDocument schema = CreateSchema();
                using (var core = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), new[] { new AtlasBakerMaterialInput(new MaterialId("figure", "body"), source, normal) }))
                {
                    Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                    Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic logicalDiagnostic), Is.True, logicalDiagnostic?.message);
                    Assert.That(host.TryReserveHall(host.Capability.FixedGridEdge, host.Capability.FixedGridEdge, out blocker, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);
                    using (var executor = new PlayModeAtlasBakerPageExecutor(host))
                    {
                        Assert.That(executor.Start(logical.Pages[0], out StackMachineDiagnostic rejected), Is.False);
                        Assert.That(rejected.domainCode, Is.EqualTo("AtlasLiveAdmissionRejected"));
                        Assert.That(host.TryReleaseHall(blocker), Is.True); blocker = default;
                        Assert.That(executor.Start(logical.Pages[0], out StackMachineDiagnostic accepted), Is.True, accepted?.message);
                        executor.Cancel();
                    }
                }
            }
            finally { if (blocker.IsValid) host.TryReleaseHall(blocker); Object.Destroy(root); Object.Destroy(source); Object.Destroy(normal); }
            yield return null;
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Executor_CumulativePartitions_MatchSingleRecipePixels()
        {
#if UNITY_EDITOR
            // A preceding PlayMode test can release a GPU-owned RenderTexture at frame end.
            // Begin this readback-sensitive comparison on a clean frame.
            yield return null;
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D[] baseColors = { Solid(Color.red, 512), Solid(Color.green, 512), Solid(Color.blue, 512), Solid(Color.white, 512) };
            Texture2D[] normals = { Solid(new Color(.5f, .5f, 1f, 1f), 512), Solid(new Color(.6f, .5f, 1f, 1f), 512), Solid(new Color(.5f, .6f, 1f, 1f), 512), Solid(new Color(.4f, .5f, 1f, 1f), 512) };
            try
            {
                AtlasSchemaDocument schema = CreateFourSourceSchema();
                var inputs = new AtlasBakerMaterialInput[4];
                for (int i = 0; i < inputs.Length; i++) inputs[i] = new AtlasBakerMaterialInput(new MaterialId("figure", "body" + i), baseColors[i], normals[i]);
                using (var core = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), inputs))
                {
                    Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                    Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic logicalDiagnostic), Is.True, logicalDiagnostic?.message);
                    var splitCapability = new TextureGpuCapability(1024, 128L * 1024L * 1024L, 1024);
                    var feasibilitySources = new AtlasFeasibilitySource[8];
                    for (int i = 0; i < 4; i++)
                    {
                        var id = new MaterialId("figure", "body" + i);
                        feasibilitySources[i * 2] = new AtlasFeasibilitySource(id, AtlasTextureSemantic.BaseColor, 512, 512);
                        feasibilitySources[i * 2 + 1] = new AtlasFeasibilitySource(id, AtlasTextureSemantic.Normal, 512, 512);
                    }
                    Assert.That(AtlasFeasibility.TryEvaluate(logical.Layout, feasibilitySources, splitCapability, out AtlasFeasibilityResult feasibility, out StackMachineDiagnostic feasibilityDiagnostic), Is.True, feasibilityDiagnostic?.message);
                    Assert.That(feasibility.RequiredRecipeCount, Is.EqualTo(2));
                    Assert.That(AtlasBakerPageRecipePartitioner.TryCreate(logical.Pages[0], splitCapability, out var partitions, out StackMachineDiagnostic partitionDiagnostic), Is.True, partitionDiagnostic?.message);
                    Assert.That(partitions.Count, Is.EqualTo(2));
                    using (var single = new AtlasBakerExecutionOperation(logical, new PlayModeAtlasBakerPageExecutor(host)))
                    using (var split = new AtlasBakerExecutionOperation(logical, new PlayModeAtlasBakerPageExecutor(host, splitCapability)))
                    {
                        yield return PumpToSuccess(single);
                        Assert.That(single.TryTakeResult(out AtlasBakerExecutionResult singleResult, out StackMachineDiagnostic singleDiagnostic), Is.True, singleDiagnostic?.message);
                        using (singleResult)
                        {
                            yield return PumpToSuccess(split);
                            Assert.That(split.TryTakeResult(out AtlasBakerExecutionResult splitResult, out StackMachineDiagnostic splitDiagnostic), Is.True, splitDiagnostic?.message);
                            using (splitResult)
                            {
                                Assert.That(splitResult.Pages, Has.Count.EqualTo(singleResult.Pages.Count));
                                for (int i = 0; i < singleResult.Pages.Count; i++) yield return AssertSamePixels(singleResult.Pages[i].Texture, splitResult.Pages[i].Texture);
                            }
                        }
                    }
                }
            }
            finally { Object.Destroy(root); for (int i = 0; i < baseColors.Length; i++) { Object.Destroy(baseColors[i]); Object.Destroy(normals[i]); } }
            yield return null;
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
#endif
        }

        [UnityTest]
        public IEnumerator Executor_CancelsRetainedOutput_WhenNextSegmentAdmissionRejects()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D[] baseColors = { Solid(Color.red, 512), Solid(Color.green, 512), Solid(Color.blue, 512), Solid(Color.white, 512) };
            Texture2D[] normals = { Solid(new Color(.5f, .5f, 1f, 1f), 512), Solid(new Color(.6f, .5f, 1f, 1f), 512), Solid(new Color(.5f, .6f, 1f, 1f), 512), Solid(new Color(.4f, .5f, 1f, 1f), 512) };
            var blockers = new List<TextureHallAllocation>();
            try
            {
                AtlasSchemaDocument schema = CreateFourSourceSchema();
                var inputs = new AtlasBakerMaterialInput[4];
                for (int i = 0; i < inputs.Length; i++) inputs[i] = new AtlasBakerMaterialInput(new MaterialId("figure", "body" + i), baseColors[i], normals[i]);
                using (var core = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), inputs))
                {
                    Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                        Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic logicalDiagnostic), Is.True, logicalDiagnostic?.message);
                    using (var executor = new PlayModeAtlasBakerPageExecutor(host, new TextureGpuCapability(1024, 128L * 1024L * 1024L, 1024)))
                    {
                        Assert.That(executor.Start(logical.Pages[0], out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                        bool firstSegmentHandedOff = false;
                        for (int frame = 0; frame < 180; frame++)
                        {
                            Assert.That(executor.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(AtlasBakerExecutionStatus.Pending), pumpDiagnostic?.message);
                            if (host.OutstandingOutputLeaseCount == 1 && !host.HasSubmittedRequest && host.PendingRequestCount == 0)
                            {
                                firstSegmentHandedOff = true;
                                break;
                            }
                            yield return null;
                        }
                        Assert.That(firstSegmentHandedOff, Is.True, "The executor must consume the first completed handle before another segment is admitted.");
                        Assert.That(host.OutstandingOutputLeaseCount, Is.EqualTo(1), "The first partition must retain its output before the second is admitted.");
                        Assert.That(host.HasSubmittedRequest, Is.False);
                        FillRemainingHalls(host, blockers);
                        Assert.That(AtlasBakerPageRecipePartitioner.TryCreate(logical.Pages[0], new TextureGpuCapability(1024, 128L * 1024L * 1024L, 1024), out var partitions, out StackMachineDiagnostic partitionDiagnostic), Is.True, partitionDiagnostic?.message);
                        Assert.That(AtlasBakerPageRecipeBuilder.TryCreate(logical.Pages[0], partitions[1].Operations, partitions[1].InitializesOutput, out TextureExecutionPlan nextPlan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                        Assert.That(host.TryValidateAdmission(nextPlan, true, out StackMachineDiagnostic blocked), Is.False, blocked?.message);

                        Assert.That(executor.Pump(out StackMachineDiagnostic rejected), Is.EqualTo(AtlasBakerExecutionStatus.Failed));
                        Assert.That(rejected.domainCode, Is.EqualTo("AtlasLiveAdmissionRejected"));
                        executor.Cancel();
                        Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
                    }
                }
            }
            finally
            {
                for (int i = blockers.Count - 1; i >= 0; i--) host.TryReleaseHall(blockers[i]);
                Object.Destroy(root);
                for (int i = 0; i < baseColors.Length; i++) { Object.Destroy(baseColors[i]); Object.Destroy(normals[i]); }
            }
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Executor_HandoffDelivery_IsExcludedFromTransientBudget_AndHostDestroyReleasesIt()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D source = Solid(Color.red); Texture2D normal = Solid(new Color(.5f, .5f, 1f, 1f));
            try
            {
                AtlasBakerResult logical = CreateLogical(source, normal);
                using (var operation = new AtlasBakerExecutionOperation(logical, new PlayModeAtlasBakerPageExecutor(host)))
                {
                    yield return PumpToSuccess(operation);
                    Assert.That(operation.TryTakeResult(out AtlasBakerExecutionResult result, out StackMachineDiagnostic resultDiagnostic), Is.True, resultDiagnostic?.message);
                    using (result)
                    {
                        Assert.That(host.HandedOffDeliveryCount, Is.EqualTo(2));
                        Assert.That(host.LiveTransientGpuBytes, Is.Zero, "Caller-handed-off Atlas pages must not consume future transient admission budget.");
                        RenderTexture handedOffTexture = result.Pages[0].Texture;
                        Object.DestroyImmediate(root); root = null;
                        yield return null;
                        Assert.That(handedOffTexture == null, Is.True, "Host destruction must release an un-disposed handed-off delivery.");
                    }
                }
            }
            finally { if (root != null) Object.Destroy(root); Object.Destroy(source); Object.Destroy(normal); }
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ExecutionOperation_CancelAndFailure_DisposeUntakenPageDeliveries()
        {
#if UNITY_EDITOR
            Texture2D source = Solid(Color.red); Texture2D normal = Solid(new Color(.5f, .5f, 1f, 1f));
            GameObject cancelRoot = null; GameObject failureRoot = null;
            try
            {
                cancelRoot = CreateHost(out TextureStackMachineHost cancelHost);
                AtlasBakerResult cancelLogical = CreateLogical(source, normal);
                using (var operation = new AtlasBakerExecutionOperation(cancelLogical, new PlayModeAtlasBakerPageExecutor(cancelHost)))
                {
                    yield return PumpUntilFirstDelivery(operation, cancelHost);
                    Assert.That(cancelHost.HandedOffDeliveryCount, Is.EqualTo(1));
                    operation.Cancel();
                    Assert.That(operation.Status, Is.EqualTo(AtlasBakerExecutionStatus.Cancelled));
                    Assert.That(cancelHost.HandedOffDeliveryCount, Is.Zero);
                }

                failureRoot = CreateHost(out TextureStackMachineHost failureHost);
                AtlasBakerResult failureLogical = CreateLogical(source, normal);
                TextureHallAllocation blocker = default;
                try
                {
                    using (var operation = new AtlasBakerExecutionOperation(failureLogical, new PlayModeAtlasBakerPageExecutor(failureHost)))
                    {
                        yield return PumpUntilFirstDelivery(operation, failureHost);
                        Assert.That(failureHost.HandedOffDeliveryCount, Is.EqualTo(1));
                        Assert.That(failureHost.TryReserveHall(failureHost.Capability.FixedGridEdge, failureHost.Capability.FixedGridEdge, out blocker, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);
                        Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Failed));
                        Assert.That(operation.Diagnostic.domainCode, Is.EqualTo("AtlasLiveAdmissionRejected"));
                        Assert.That(failureHost.HandedOffDeliveryCount, Is.Zero);
                    }
                }
                finally { if (blocker.IsValid) failureHost.TryReleaseHall(blocker); }
            }
            finally { if (cancelRoot != null) Object.Destroy(cancelRoot); if (failureRoot != null) Object.Destroy(failureRoot); Object.Destroy(source); Object.Destroy(normal); }
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Executor_ExecutesAllLogicalPages_AndMatchesSharedLayoutAndImageOracles()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D source = Solid(Color.red);
            Texture2D normal = Solid(new Color(.5f, .5f, 1f, 1f));
            AtlasSchemaDocument schema = CreateSchema();
            AtlasValidationIdentity identity = schema.ValidationIdentity.Clone();
            try
            {
                using (var core = new AtlasBakerOperation(schema, identity, new[] { new AtlasBakerMaterialInput(new MaterialId("figure", "body"), source, normal) }))
                {
                    Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                    Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic logicalDiagnostic), Is.True, logicalDiagnostic?.message);
                    Assert.That(AtlasLayoutPropertyOracle.TryValidate(schema, logical.Layout, out StackMachineDiagnostic layoutDiagnostic), Is.True, layoutDiagnostic?.message);
                    Assert.That(logical.Pages, Has.Count.EqualTo(2));

                    using (var operation = new AtlasBakerExecutionOperation(logical, new PlayModeAtlasBakerPageExecutor(host)))
                    {
                        for (int frame = 0; operation.Status == AtlasBakerExecutionStatus.Pending && frame < 180; frame++)
                        {
                            operation.Pump();
                            yield return null;
                        }
                        Assert.That(operation.Status, Is.EqualTo(AtlasBakerExecutionStatus.Succeeded), operation.Diagnostic?.message);
                        Assert.That(operation.TryTakeResult(out AtlasBakerExecutionResult result, out StackMachineDiagnostic resultDiagnostic), Is.True, resultDiagnostic?.message);
                        using (result)
                        {
                            Assert.That(result.Pages, Has.Count.EqualTo(2));
                            for (int i = 0; i < result.Pages.Count; i++)
                            {
                                AtlasBakerPageCompletion completion = result.Pages[i];
                                AtlasBakerPagePlan page = logical.Pages[i];
                                Assert.That(completion.PageIndex, Is.EqualTo(page.PageIndex));
                                Assert.That(completion.Semantic, Is.EqualTo(page.Semantic));
                                Assert.That(completion.Texture, Is.Not.Null);
                                yield return AssertImageMatches(page, completion.Texture);
                            }
                        }
                    }
                }
            }
            finally { Object.Destroy(root); Object.Destroy(source); Object.Destroy(normal); }
#else
            Assert.Ignore("PlayMode Atlas executor GPU fixture is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static GameObject CreateHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            var root = new GameObject("Spec19_5_AtlasHost");
            host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assign), Is.True, assign?.message);
            Assert.That(host.TryInitialize(out StackMachineDiagnostic initialize), Is.True, initialize?.message);
            return root;
        }

        private static AtlasSchemaDocument CreateSchema()
        {
            var id = new MaterialId("figure", "body");
            var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source:body") });
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { new AtlasSchemaEntry(id, 0, 2, 2, false, 0) });
        }

        private static AtlasBakerResult CreateLogical(Texture2D source, Texture2D normal)
        {
            AtlasSchemaDocument schema = CreateSchema();
            using (var core = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), new[] { new AtlasBakerMaterialInput(new MaterialId("figure", "body"), source, normal) }))
            {
                Assert.That(core.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), core.Diagnostic?.message);
                Assert.That(core.TryTakeResult(out AtlasBakerResult logical, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                return logical;
            }
        }

        private static AtlasSchemaDocument CreateFourSourceSchema()
        {
            var identities = new AtlasSourceMaterialIdentity[4]; var entries = new AtlasSchemaEntry[4];
            for (int i = 0; i < 4; i++) { var id = new MaterialId("figure", "body" + i); identities[i] = new AtlasSourceMaterialIdentity(id, "source:body" + i); entries[i] = new AtlasSchemaEntry(id, 0, 2, 2, false, 0); }
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", identities), entries);
        }

        private static Texture2D Solid(Color color, int edge = 128)
        {
            var texture = new Texture2D(edge, edge, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[edge * edge];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(false, false);
            return texture;
        }

        private static IEnumerator AssertImageMatches(AtlasBakerPagePlan page, RenderTexture actualTexture)
        {
            bool done = false;
            AsyncGPUReadbackRequest request = default;
            AsyncGPUReadback.Request(actualTexture, 0, TextureFormat.RGBAFloat, value => { request = value; done = true; });
            while (!done) yield return null;
            Assert.That(request.hasError, Is.False);

            var expected = new Color[page.Extent * page.Extent];
            Color fill = page.Operations[0].FillColor;
            for (int i = 0; i < expected.Length; i++) expected[i] = fill;
            AtlasBakerPageOperation place = page.Operations[1];
            Color sourceColor = page.Semantic == AtlasTextureSemantic.Normal ? new Color(.5f, .5f, 1f, 1f) : Color.red;
            for (int y = 0; y < place.DestinationRectangle.Height; y++)
                for (int x = 0; x < place.DestinationRectangle.Width; x++)
                    expected[(place.DestinationRectangle.Y + y) * page.Extent + place.DestinationRectangle.X + x] = sourceColor;

            Color[] actual = request.GetData<Color>().ToArray();
            var probes = new[]
            {
                new AtlasImageOracle.Probe(page.Extent - 1, page.Extent - 1, fill),
                new AtlasImageOracle.Probe(place.DestinationRectangle.X + place.DestinationRectangle.Width / 2, place.DestinationRectangle.Y + place.DestinationRectangle.Height / 2, sourceColor)
            };
            Assert.That(AtlasImageOracle.TryComparePixels(page.Extent, page.Extent, actual, expected, probes, true, AtlasOracleTolerances.Default, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(comparison.ExceededPixelCount, Is.Zero);
        }

        private static IEnumerator PumpToSuccess(AtlasBakerExecutionOperation operation)
        {
            for (int frame = 0; operation.Status == AtlasBakerExecutionStatus.Pending && frame < 360; frame++) { operation.Pump(); yield return null; }
            Assert.That(operation.Status, Is.EqualTo(AtlasBakerExecutionStatus.Succeeded), operation.Diagnostic?.message);
        }

        private static IEnumerator PumpUntilFirstDelivery(AtlasBakerExecutionOperation operation, TextureStackMachineHost host)
        {
            for (int frame = 0; frame < 180; frame++)
            {
                Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerExecutionStatus.Pending), operation.Diagnostic?.message);
                if (host.HandedOffDeliveryCount == 1) yield break;
                yield return null;
            }
            Assert.Fail("The first Atlas page delivery was not handed off within the frame budget.");
        }

        private static void FillRemainingHalls(TextureStackMachineHost host, List<TextureHallAllocation> allocations)
        {
            // The partition fixture reads 512x512 source textures. Fill at that exact
            // reservation granularity first so the next admission cannot find a source hall.
            for (int edge = Mathf.Min(512, host.Capability.FixedGridEdge); edge >= 128; edge /= 2)
            {
                while (host.TryReserveHall(edge, edge, out TextureHallAllocation allocation, out _)) allocations.Add(allocation);
            }
        }

        private static IEnumerator AssertSamePixels(RenderTexture expected, RenderTexture actual)
        {
            Color[] expectedPixels = null;
            Color[] actualPixels = null;
            for (int attempt = 0; attempt < 4 && expectedPixels == null; attempt++)
            {
                AsyncGPUReadbackRequest expectedRequest = AsyncGPUReadback.Request(expected, 0, TextureFormat.RGBAFloat);
                AsyncGPUReadbackRequest actualRequest = AsyncGPUReadback.Request(actual, 0, TextureFormat.RGBAFloat);
                while (!expectedRequest.done || !actualRequest.done) yield return null;
                if (!expectedRequest.hasError && !actualRequest.hasError)
                {
                    expectedPixels = expectedRequest.GetData<Color>().ToArray();
                    actualPixels = actualRequest.GetData<Color>().ToArray();
                }
                else yield return null;
            }
            Assert.That(expectedPixels, Is.Not.Null, "Atlas pixel readback failed after clean-frame retries.");
            Assert.That(AtlasImageOracle.TryComparePixels(expected.width, expected.height, actualPixels, expectedPixels, new[] { new AtlasImageOracle.Probe(0, 0, expectedPixels[0]) }, true, AtlasOracleTolerances.Default, out AtlasImageOracle.Comparison comparison, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(comparison.ExceededPixelCount, Is.Zero);
        }

#endif
    }
}
