// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15 P01-P08 CPU planner coverage for <see cref="TextureRequestHallPlan"/> and
    /// <see cref="TextureHallAllocator"/>. These tests are pure CPU
    /// whitebox coverage of demand lists, slot lifetime, rejection rules, and occupancy snapshots; GPU pixel
    /// verification runs in the PlayMode whitebox fixture.
    /// </summary>
    public sealed class RequestHallPlanTests
    {
        [Test]
        public void P01_ChainedTemperatures_ReserveTwoDistinctSlots()
        {
            Texture2D source = NewSource(128);
            try
            {
                TextureRequestHallPlan plan = Plan("1 0 0 1 FILL $a ADD $out COPY", source, out _);
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(2));
                Assert.That(plan.TemporarySlotByName["@texture:0"], Is.Not.EqualTo(plan.TemporarySlotByName["@texture:1"]));
                Assert.That(plan.SourceIndexByName["a"], Is.EqualTo(0));
                Assert.That(plan.Sources[0].Texture, Is.SameAs(source));

                // Spec15-2 §6.3: all three records are checked, not just TemporarySlotCount.
                Assert.That(plan.RecordDestinations.Length, Is.EqualTo(3));
                Assert.That(plan.RecordDestinations[0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)), "record 0 destination per Spec15-2 §6.3");
                Assert.That(plan.RecordDestinations[1], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 1)), "record 1 destination per Spec15-2 §6.3");
                Assert.That(plan.RecordDestinations[2], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.Output, -1)), "record 2 destination per Spec15-2 §6.3");
                Assert.That(plan.RecordSources[1].Length, Is.EqualTo(2));
                Assert.That(plan.RecordSources[1][0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)), "record 1 source 0 per Spec15-2 §6.3");
                Assert.That(plan.RecordSources[1][1], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.Source, 0)), "record 1 source 1 per Spec15-2 §6.3");
                Assert.That(plan.RecordSources[2].Length, Is.EqualTo(1));
                Assert.That(plan.RecordSources[2][0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 1)), "record 2 source 0 per Spec15-2 §6.3");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P02_LongChain_PeaksAtTwoSlots()
        {
            Texture2D source = NewSource(128);
            try
            {
                TextureRequestHallPlan plan = Plan(
                    "1 0 0 1 FILL $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $out COPY",
                    source, out _);
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(2), "peak simultaneous slots, not the total temporary name count");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P03_SameRecordDoubleRead_RegistersOneReleaseAtLastUse()
        {
            Texture2D source = NewSource(128);
            try
            {
                TextureRequestHallPlan plan = Plan("1 0 0 1 FILL DUP ADD $a ADD $out COPY", source, out _);
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(2));
                Assert.That(plan.TemporarySlotByName["@texture:0"], Is.Not.EqualTo(plan.TemporarySlotByName["@texture:1"]));
                Assert.That(plan.TemporarySlotByName["@texture:2"], Is.EqualTo(plan.TemporarySlotByName["@texture:0"]), "the single release at the last-use record frees the slot for reuse");

                // Spec15-2 §15.2 P03: scan all records' RecordDestinations/RecordSources against the §6 references.
                Assert.That(plan.RecordDestinations.Length, Is.EqualTo(4));
                Assert.That(plan.RecordDestinations[0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)));
                Assert.That(plan.RecordDestinations[1], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 1)));
                Assert.That(plan.RecordDestinations[2], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)), "the freed slot is reused by the next definition");
                Assert.That(plan.RecordDestinations[3], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.Output, -1)));
                Assert.That(plan.RecordSources[0].Length, Is.EqualTo(0));
                Assert.That(plan.RecordSources[1].Length, Is.EqualTo(2));
                Assert.That(plan.RecordSources[1][0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)));
                Assert.That(plan.RecordSources[1][1], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)), "the DUP double read resolves to the same slot");
                Assert.That(plan.RecordSources[2].Length, Is.EqualTo(2));
                Assert.That(plan.RecordSources[2][0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 1)));
                Assert.That(plan.RecordSources[2][1], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.Source, 0)));
                Assert.That(plan.RecordSources[3].Length, Is.EqualTo(1));
                Assert.That(plan.RecordSources[3][0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P04_UnreferencedTemporary_ReusesSlotAfterDefiningRecord()
        {
            Texture2D source = NewSource(128);
            try
            {
                TextureRequestHallPlan plan = Plan("1 0 0 1 FILL DROP 1 0 0 1 FILL $out COPY", source, out _);
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(1));
                Assert.That(plan.TemporarySlotByName["@texture:1"], Is.EqualTo(plan.TemporarySlotByName["@texture:0"]));

                // Spec15-2 §15.2 P03/P04: scan all records' RecordDestinations/RecordSources against the §6 references.
                Assert.That(plan.RecordDestinations.Length, Is.EqualTo(3));
                Assert.That(plan.RecordDestinations[0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)));
                Assert.That(plan.RecordDestinations[1], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)));
                Assert.That(plan.RecordDestinations[2], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.Output, -1)));
                Assert.That(plan.RecordSources[0].Length, Is.EqualTo(0));
                Assert.That(plan.RecordSources[1].Length, Is.EqualTo(0));
                Assert.That(plan.RecordSources[2].Length, Is.EqualTo(1));
                Assert.That(plan.RecordSources[2][0], Is.EqualTo(Ref(TextureRequestHallPlan.ReferenceKind.TemporarySlot, 0)));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void Spec152_P05_TemporarySameRecordReadWriteIsRejected()
        {
            Texture2D source = NewSource(128);
            try
            {
                _ = PlanWithBindingContext("1 0 0 1 FILL $out COPY DROP", source, out _, out TextureExecutionPlan executionPlan);
                TextureDispatchRecord[] records = { Rec(TextureDispatchOperation.Fill, new[] { "@texture:0" }, "@texture:0") };
                AssertRejection(new TextureDispatchPlan(null, 128, 128, records, new[] { "a" }), executionPlan.BindingContext);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P05_UndefinedTemporaryRead_IsRejected()
        {
            Texture2D source = NewSource(128);
            try
            {
                _ = PlanWithBindingContext("1 0 0 1 FILL $out COPY DROP", source, out _, out TextureExecutionPlan executionPlan);
                TextureDispatchRecord[] records = { Rec(TextureDispatchOperation.Add, new[] { "@texture:9" }, "out") };
                AssertRejection(new TextureDispatchPlan(null, 128, 128, records, new[] { "a" }), executionPlan.BindingContext);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P05_SameRecordOutputReadWrite_IsRejected()
        {
            Texture2D source = NewSource(128);
            try
            {
                _ = PlanWithBindingContext("1 0 0 1 FILL $out COPY DROP", source, out _, out TextureExecutionPlan executionPlan);
                TextureDispatchRecord[] records = { Rec(TextureDispatchOperation.Fill, new[] { "out" }, "out") };
                AssertRejection(new TextureDispatchPlan(null, 128, 128, records, Array.Empty<string>()), executionPlan.BindingContext);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P05_TemporaryRedefinedTwice_IsRejected()
        {
            Texture2D source = NewSource(128);
            try
            {
                _ = PlanWithBindingContext("1 0 0 1 FILL $out COPY DROP", source, out _, out TextureExecutionPlan executionPlan);
                TextureDispatchRecord[] records =
                {
                    Rec(TextureDispatchOperation.Add, new[] { "a", "a" }, "@texture:0"),
                    Rec(TextureDispatchOperation.Add, new[] { "a", "a" }, "@texture:0"),
                };
                AssertRejection(new TextureDispatchPlan(null, 128, 128, records, new[] { "a" }), executionPlan.BindingContext);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P06_TemporarySlotsAlwaysUseOutputExtents()
        {
            Texture2D source = NewSource(256);
            try
            {
                TextureRequestHallPlan plan = Plan("1 0 0 1 FILL $a 0 0 128 128 0 0 64 64 PLACE", source, out _, 256);
                Assert.That(plan.OutputWidth, Is.EqualTo(256));
                Assert.That(plan.OutputHeight, Is.EqualTo(256));
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(1), "temporary slots keep the output extents even when RecordExtent is smaller");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void P07_Placement_DetectsFragmentation()
        {
            var allocator = new TextureHallAllocator(512);
            var filled = new System.Collections.Generic.List<TextureHallAllocation>();
            for (int i = 0; i < 16; i++)
            {
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation hall), Is.True, $"fill room {i}");
                filled.Add(hall);
            }
            AssertReleaseCheckerboard(allocator, filled);
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(8), "checkerboard leaves eight free rooms");
            ulong versionBefore = allocator.Version;

            // Area is sufficient but no contiguous 2x2 block exists: the snapshot check must fail (live wait).
            TextureHallAllocator snapshot = allocator.CreateOccupancySnapshot();
            Assert.That(snapshot.TryReserve(256, 256, out _), Is.False, "fragmented free area must not satisfy a contiguous rectangle");
            Assert.That(snapshot.TryReserve(128, 128, out _), Is.True, "a single free room does fit on the snapshot");

            // The real allocator is unchanged by simulation, and its version only moves on real success.
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(8));
            Assert.That(allocator.Version, Is.EqualTo(versionBefore));

            // Spec15-2 §7.1: the fixed-rectangle guards run in this order - IsValid, Phase0 edge of the
            // dimensions, RoomWidth == Width / RoomEdge, RoomHeight == Height / RoomEdge, non-negative room
            // origin, bottom-right corner inside the grid, IsFree. A false guard changes neither bitmap nor count.
            TextureHallAllocator guards = allocator.CreateOccupancySnapshot();
            Assert.That(guards.OccupiedRoomCount, Is.EqualTo(8), "a fresh snapshot duplicates the grid dimensions and occupancy bitmap");
            Assert.That(guards.TryOccupyFixed(default), Is.False, "guard 1: an invalid allocation is rejected");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(0, 1, 0, 1, 1, 128, 128)), Is.False, "guard 1: id 0 is not a valid allocation");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(100, 3, 0, 1, 1, 96, 128)), Is.False, "guard 2: a dimension that is not a Phase0 edge is rejected");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(101, 2, 0, 2, 1, 128, 128)), Is.False, "guard 3: RoomWidth must equal Width / RoomEdge");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(102, 2, 0, 1, 2, 128, 128)), Is.False, "guard 4: RoomHeight must equal Height / RoomEdge");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(103, -1, 0, 1, 1, 128, 128)), Is.False, "guard 5: a negative room origin is out of range");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(104, 3, 0, 2, 1, 256, 128)), Is.False, "guard 6: the bottom-right corner must stay inside the grid");
            Assert.That(guards.OccupiedRoomCount, Is.EqualTo(8), "a rejected fixed rectangle changes neither the bitmap nor the count");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(105, 3, 0, 1, 1, 128, 128)), Is.True, "normal: a valid fixed rectangle on a free room is occupied");
            Assert.That(guards.OccupiedRoomCount, Is.EqualTo(9), "fixed occupancy updates the bitmap and count only");
            Assert.That(guards.TryOccupyFixed(new TextureHallAllocation(106, 3, 0, 1, 1, 128, 128)), Is.False, "a different hall overlapping an occupied room is an internal contract violation");
            Assert.That(guards.OccupiedRoomCount, Is.EqualTo(9), "the rejected overlapping input leaves the simulation occupancy");
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(8), "the simulation guards never change the real allocator");
            Assert.That(allocator.Version, Is.EqualTo(versionBefore), "the real version only moves on a real successful reserve or release");
        }

        [Test]
        public void P08_Placement_BorrowedFixedPosition()
        {
            // The extra rectangle fits an empty grid but not one holding only the borrowed fixed position.
            Assert.That(new TextureHallAllocator(256).TryReserve(256, 256, out _), Is.True, "empty grid fits the rectangle");
            Assert.That(new TextureHallAllocator(256).TryReserve(128, 128, out TextureHallAllocation borrowed), Is.True);

            TextureHallAllocator real = new TextureHallAllocator(256);
            TextureHallAllocator snapshot = real.CreateOccupancySnapshot();
            ulong realVersionBefore = real.Version;
            Assert.That(snapshot.TryOccupyFixed(borrowed), Is.True, "the borrowed hall lands at its fixed position");
            Assert.That(snapshot.TryOccupyFixed(borrowed), Is.False, "overlapping fixed input is an internal contract violation");
            Assert.That(snapshot.OccupiedRoomCount, Is.EqualTo(1), "fixed occupancy updates the bitmap and count only");
            Assert.That(snapshot.TryReserve(256, 256, out _), Is.False, "acceptance rejects with RequestHallCapacityExceeded, no infinite wait");

            // Spec15-2 §7.1 guard order on the same simulation: invalid, dimension/room mismatch, range, overlap.
            Assert.That(snapshot.TryOccupyFixed(default), Is.False, "guard 1: an invalid allocation is rejected");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(0, 1, 0, 1, 1, 128, 128)), Is.False, "guard 1: id 0 is not a valid allocation");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(200, 1, 0, 1, 1, 96, 128)), Is.False, "guard 2: a dimension that is not a Phase0 edge is rejected");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(201, 0, 1, 2, 1, 128, 128)), Is.False, "guard 3: RoomWidth must equal Width / RoomEdge");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(202, 0, 0, 1, 2, 128, 128)), Is.False, "guard 4: RoomHeight must equal Height / RoomEdge");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(203, -1, 0, 1, 1, 128, 128)), Is.False, "guard 5: a negative room origin is out of range");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(204, 1, 0, 2, 1, 256, 128)), Is.False, "guard 6: the bottom-right corner must stay inside the grid");
            Assert.That(snapshot.OccupiedRoomCount, Is.EqualTo(1), "a rejected fixed rectangle changes neither the bitmap nor the count");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(205, 1, 0, 1, 1, 128, 128)), Is.True, "normal: a valid fixed rectangle on a free room is occupied");
            Assert.That(snapshot.OccupiedRoomCount, Is.EqualTo(2), "fixed occupancy updates the bitmap and count only");
            Assert.That(snapshot.TryOccupyFixed(new TextureHallAllocation(206, 1, 0, 1, 1, 128, 128)), Is.False, "a different hall overlapping an occupied room is an internal contract violation");
            Assert.That(real.OccupiedRoomCount, Is.EqualTo(0), "the real allocator keeps the occupancy it had before the snapshot");
            Assert.That(real.Version, Is.EqualTo(realVersionBefore), "the real version only moves on a real successful reserve or release");

            // Phase2-1: extreme origins must be rejected without integer overflow, on separate simulations.
            TextureHallAllocator caseX = new TextureHallAllocator(256);
            ulong caseXVersion = caseX.Version;
            Assert.That(caseX.TryOccupyFixed(new TextureHallAllocation(300, int.MaxValue, 0, 1, 1, 128, 128)), Is.False, "case X: an extreme origin is rejected without overflow");
            Assert.That(caseX.OccupiedRoomCount, Is.EqualTo(0), "case X: the rejected input changes neither the bitmap nor the count");
            Assert.That(caseX.Version, Is.EqualTo(caseXVersion), "case X: the rejected input leaves the version unchanged");
            Assert.That(caseX.TryReserve(256, 256, out _), Is.True, "case X: the bitmap stayed empty");

            TextureHallAllocator caseY = new TextureHallAllocator(256);
            ulong caseYVersion = caseY.Version;
            Assert.That(caseY.TryOccupyFixed(new TextureHallAllocation(301, 0, int.MaxValue, 1, 1, 128, 128)), Is.False, "case Y: an extreme origin is rejected without overflow");
            Assert.That(caseY.OccupiedRoomCount, Is.EqualTo(0), "case Y: the rejected input changes neither the bitmap nor the count");
            Assert.That(caseY.Version, Is.EqualTo(caseYVersion), "case Y: the rejected input leaves the version unchanged");
            Assert.That(caseY.TryReserve(256, 256, out _), Is.True, "case Y: the bitmap stayed empty");
        }

        private static void AssertReleaseCheckerboard(TextureHallAllocator allocator, System.Collections.Generic.List<TextureHallAllocation> filled)
        {
            for (int i = 0; i < filled.Count; i++)
            {
                int x = i % 4, y = i / 4;
                if ((x + y) % 2 == 1) Assert.That(allocator.TryRelease(filled[i]), Is.True, $"checkerboard release {i}");
            }
        }

        private static TextureRequestHallPlan.ResolvedReference Ref(TextureRequestHallPlan.ReferenceKind kind, int index)
            => new TextureRequestHallPlan.ResolvedReference(kind, index);

        private static Texture2D NewSource(int edge) => new Texture2D(edge, edge, UnityEngine.TextureFormat.RGBAHalf, false, true);

        private static TextureRequestHallPlan Plan(string wordSource, Texture2D source, out StackMachineDiagnostic diagnostic, int edge = 128)
            => PlanWithBindingContext(wordSource, source, out diagnostic, out TextureExecutionPlan executionPlan, edge);

        private static TextureRequestHallPlan PlanWithBindingContext(string wordSource, Texture2D source, out StackMachineDiagnostic diagnostic, out TextureExecutionPlan executionPlan, int edge = 128)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = edge, outputHeight = edge };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            Assert.That(TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
            }), out executionPlan, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(TextureRequestHallPlan.TryCreate(executionPlan.DispatchPlan, executionPlan.BindingContext, out TextureRequestHallPlan hallPlan, out diagnostic), Is.True, diagnostic?.message);
            return hallPlan;
        }

        private static TextureDispatchRecord Rec(TextureDispatchOperation operation, string[] sources, string output)
            => new TextureDispatchRecord(operation, sources, Array.Empty<TextureDispatchRectangle>(), output, new TextureDispatchRectangle(0, 0, 128, 128), new TextureDispatchExtent(128, 128), Array.Empty<float>());

        private static void AssertRejection(TextureDispatchPlan plan, TextureBindingContext context)
        {
            Assert.That(TextureRequestHallPlan.TryCreate(plan, context, out TextureRequestHallPlan hallPlan, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(hallPlan, Is.Null);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("HallPlanInvalid"));
        }
    }
}