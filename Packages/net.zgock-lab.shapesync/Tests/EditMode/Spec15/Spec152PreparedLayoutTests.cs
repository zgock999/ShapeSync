// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-3 CPU coverage for the standalone and live layout simulations. Every case asserts that the
    /// real allocator's Version and OccupiedRoomCount are untouched; only simulations change. No GPU submit occurs.
    /// </summary>
    public sealed class Spec152PreparedLayoutTests
    {
        private const string ChainRecipe = "1 0 0 1 FILL $a ADD $out COPY";

        // The word-code lets only RESAMPLE bridge differing source/output extents (TexturePlanCompiler.TryCompileWord):
        // RESAMPLE Source128 into the temporary, then COPY the temporary into the 256 Output.
        private const string ResampleRecipe = "$a RESAMPLE $out COPY";

        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private GameObject root;
        private TextureStackMachineHost host;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Layout Host");
            host = root.AddComponent<TextureStackMachineHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void LiveProbe_Checkerboard_Output256_IsBlocked()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(512);
            FillCheckerboard(allocator);
            Texture2D source = NewSource(256);
            try
            {
                ulong versionBefore = allocator.Version;
                int occupiedBefore = allocator.OccupiedRoomCount;
                Assert.That(Probe(NewRequest(Plan(ChainRecipe, source, 256), null, null), out string stage, out int width, out int height), Is.False,
                    "fragmented free area must not satisfy a contiguous 256 rectangle");
                Assert.That(stage, Is.EqualTo("Source(a)"), "the first 256 demand in Sources order blocks first");
                Assert.That(width, Is.EqualTo(256));
                Assert.That(height, Is.EqualTo(256));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "the real allocator version is untouched by the simulation");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore), "the real allocator occupancy is untouched by the simulation");
                Assert.That(host.testLiveProbeCount, Is.EqualTo(1), "one snapshot was created for one probe");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void LiveProbe_Checkerboard_Output128_Succeeds()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(512);
            FillCheckerboard(allocator);
            Texture2D source = NewSource(128);
            try
            {
                ulong versionBefore = allocator.Version;
                int occupiedBefore = allocator.OccupiedRoomCount;
                Assert.That(Probe(NewRequest(Plan(ChainRecipe, source, 128), null, null), out string stage, out int width, out int height), Is.True);
                Assert.That(stage, Is.Null);
                Assert.That(width, Is.EqualTo(0));
                Assert.That(height, Is.EqualTo(0));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "the real allocator version is untouched by the simulation");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore), "the real allocator occupancy is untouched by the simulation");
                Assert.That(host.testLiveProbeCount, Is.EqualTo(1), "one snapshot was created for one probe");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void Standalone_Borrowed128Fixed_Output256Fails()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(256);
            Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation borrowed), Is.True, "the borrowed hall sits on the real allocator");
            Texture2D source = NewSource(128);
            TextureSourceLease lease = null;
            try
            {
                TextureRequestHallPlan plan = Plan(ResampleRecipe, source, 256);
                Assert.That(plan.Sources.Length, Is.EqualTo(1));
                Assert.That(plan.Sources[0].Width, Is.EqualTo(128));
                Assert.That(plan.Sources[0].Height, Is.EqualTo(128));
                Assert.That(plan.OutputWidth, Is.EqualTo(256));
                Assert.That(plan.OutputHeight, Is.EqualTo(256));
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(1));
                Assert.That(borrowed.Width, Is.EqualTo(source.width));
                Assert.That(borrowed.Height, Is.EqualTo(source.height));
                var bindings = new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(source, borrowed) };
                lease = new TextureSourceLease(host, bindings);
                ulong versionBefore = allocator.Version;
                int occupiedBefore = allocator.OccupiedRoomCount;
                Assert.That(Standalone(NewRequest(plan, lease, null), out StackMachineDiagnostic diagnostic), Is.False,
                    "the fixed 128 hall breaks the only contiguous 2x2 block on a 256 grid");
                Assert.That(diagnostic, Is.Not.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
                Assert.That(diagnostic.detail, Does.Contain("stage=Output").And.Contain("256x256").And.Contain("temporaryK=1").And.Contain("borrowed=1"));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "the real allocator version is untouched by the simulation");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore), "the real allocator occupancy is untouched by the simulation");
            }
            finally { try { lease?.Dispose(); } finally { UnityEngine.Object.DestroyImmediate(source); } }
        }

        [Test]
        public void Standalone_EmptyGrid_Output256Succeeds()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(512);
            Texture2D source = NewSource(128);
            try
            {
                TextureRequestHallPlan plan = Plan(ResampleRecipe, source, 256);
                Assert.That(plan.Sources.Length, Is.EqualTo(1));
                Assert.That(plan.Sources[0].Width, Is.EqualTo(128));
                Assert.That(plan.Sources[0].Height, Is.EqualTo(128));
                Assert.That(plan.OutputWidth, Is.EqualTo(256));
                Assert.That(plan.OutputHeight, Is.EqualTo(256));
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(1));
                ulong versionBefore = allocator.Version;
                int occupiedBefore = allocator.OccupiedRoomCount;
                Assert.That(Standalone(NewRequest(plan, null, null), out StackMachineDiagnostic diagnostic), Is.True,
                    "the default first-fit admits Source128, Output256, and one temporary 256 slot on an empty 512 grid");
                Assert.That(diagnostic, Is.Null);
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "the real allocator version is untouched by the simulation");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore), "the real allocator occupancy is untouched by the simulation");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private TextureHallAllocator NewInjectedAllocator(int gridEdge)
        {
            var allocator = new TextureHallAllocator(gridEdge);
            typeof(TextureStackMachineHost).GetField("allocator", Refl).SetValue(host, allocator);
            return allocator;
        }

        private object NewRequest(TextureRequestHallPlan hallPlan, TextureSourceLease sourceLease, TextureOutputLease outputLease)
        {
            Type requestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
            Assert.That(requestType, Is.Not.Null);
            object request = Activator.CreateInstance(requestType, Refl, null, null, null);
            requestType.GetField("hallPlan", Refl).SetValue(request, hallPlan);
            requestType.GetField("requestedSourceLease", Refl).SetValue(request, sourceLease);
            requestType.GetField("requestedOutputLease", Refl).SetValue(request, outputLease);
            return request;
        }

        private bool Standalone(object request, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { request, null };
            bool ok = (bool)typeof(TextureStackMachineHost).GetMethod("TryValidateStandaloneLayout", Refl).Invoke(host, args);
            diagnostic = (StackMachineDiagnostic)args[1];
            return ok;
        }

        private bool Probe(object request, out string blockedStage, out int width, out int height)
        {
            object[] args = { request, null, 0, 0 };
            bool ok = (bool)typeof(TextureStackMachineHost).GetMethod("TryProbeLiveLayout", Refl).Invoke(host, args);
            blockedStage = (string)args[1];
            width = (int)args[2];
            height = (int)args[3];
            return ok;
        }
        private static void FillCheckerboard(TextureHallAllocator allocator)
        {
            var filled = new List<TextureHallAllocation>();
            for (int i = 0; i < 16; i++)
            {
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation hall), Is.True, $"fill room {i}");
                filled.Add(hall);
            }

            for (int i = 0; i < filled.Count; i++)
            {
                int x = i % 4, y = i / 4;
                if ((x + y) % 2 == 1) Assert.That(allocator.TryRelease(filled[i]), Is.True, $"checkerboard release {i}");
            }

            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(8), "checkerboard leaves eight free rooms");
        }

        private static Texture2D NewSource(int edge) => new Texture2D(edge, edge, UnityEngine.TextureFormat.RGBAHalf, false, true);

        private static TextureRequestHallPlan Plan(string wordSource, Texture2D source, int edge)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = edge, outputHeight = edge };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            Assert.That(TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
            }), out TextureExecutionPlan executionPlan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(TextureRequestHallPlan.TryCreate(executionPlan.DispatchPlan, executionPlan.BindingContext, out TextureRequestHallPlan hallPlan, out diagnostic), Is.True, diagnostic?.message);
            return hallPlan;
        }
    }
}



