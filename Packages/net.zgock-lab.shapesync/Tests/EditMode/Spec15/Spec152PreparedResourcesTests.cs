// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-4 CPU coverage for the atomic reservation section and its rollback owner. Every case builds
    /// the private QueuedRequest, the host allocator, and any CPU leases via reflection/internal constructors per the
    /// Common fixture boundary; nothing is submitted to the GPU. Borrowed caller Leases stay fixture-owned and are
    /// disposed last, and every artificial request is resolved through ReleasePreparedResources in finally.
    /// </summary>
    public sealed class Spec152PreparedResourcesTests
    {
        // One new Source and the new Output only; reservations run Source(ordinal 0) -> Output(ordinal 1).
        private const string CopyRecipe = "$a $out COPY";

        // Proven Phase06-3 recipe: a borrowed Source, the new Output, and two temporary slots;
        // reservations run Output(ordinal 0) -> Temporary slot 0(ordinal 1).
        private const string TempRecipe = "1 0 0 1 FILL $a ADD $out COPY";

        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo AcquireMethod = typeof(TextureStackMachineHost).GetMethod("TryAcquirePreparedResources", Refl);
        private static readonly MethodInfo ReleaseMethod = typeof(TextureStackMachineHost).GetMethod("ReleasePreparedResources", Refl);

        private GameObject root;
        private TextureStackMachineHost host;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Resources Host");
            host = root.AddComponent<TextureStackMachineHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void RealReservationFailsAtOrdinal1_NewSourceRollsBackToTheAllocator()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(512);
            Texture2D source = NewSource(128);
            object request = null;
            try
            {
                TextureRequestHallPlan plan = Plan(CopyRecipe, source, 128);
                Assert.That(plan.Sources.Length, Is.EqualTo(1));
                Assert.That(plan.TemporarySlotCount, Is.Zero);
                request = NewRequest(plan, null, null);
                host.testFailReservationOrdinal = 1;

                Assert.That(Acquire(host, request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HallReservationInvariantBroken"));
                Assert.That(diagnostic.detail, Does.Contain("stage=Output"));
                Assert.That(CountOf(GetField(request, "ownedSourceHalls")), Is.Zero, "the rolled back Source hall left the owned list");
                Assert.That(AllocationOf(GetField(request, "ownedOutputHall")).IsValid, Is.False);
                Assert.That(CountOf(GetField(request, "temporaryPool")), Is.Zero);
                Assert.That(GetField(request, "acquiredSourceLease"), Is.Null);
                Assert.That(GetField(request, "acquiredOutputLease"), Is.Null);
                Assert.That(DictOf(GetField(request, "sourceHalls")).Count, Is.Zero);
                Assert.That(AllocationOf(GetField(request, "outputHall")).IsValid, Is.False);
                Assert.That(allocator.OccupiedRoomCount, Is.Zero, "the real reservation returned to the allocator");
            }
            finally { Cleanup(request); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void BorrowedSourceNewOutput_FailAtTemporaryOrdinal1_RollsBackOutputAndReturnsUse()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(512);
            Texture2D source = NewSource(128);
            TextureSourceLease lease = null;
            object request = null;
            try
            {
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation borrowed), Is.True, "the borrowed hall sits on the real allocator");
                lease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(source, borrowed) });
                TextureRequestHallPlan plan = Plan(TempRecipe, source, 128);
                Assert.That(plan.TemporarySlotCount, Is.EqualTo(2));
                request = NewRequest(plan, lease, null);
                host.testFailReservationOrdinal = 1;

                Assert.That(Acquire(host, request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HallReservationInvariantBroken"));
                Assert.That(diagnostic.detail, Does.Contain("stage=Temporary(0)"));
                Assert.That(lease.IsValid, Is.True, "the rollback never Disposes a requested Lease");
                Assert.That(UseCount(lease), Is.Zero, "the acquired use was returned to the caller lease");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1), "only the borrowed hall remains on the real allocator");
                Assert.That(CountOf(GetField(request, "ownedSourceHalls")), Is.Zero);
                Assert.That(AllocationOf(GetField(request, "ownedOutputHall")).IsValid, Is.False, "the rolled back Output hall left the owned field");
                Assert.That(CountOf(GetField(request, "temporaryPool")), Is.Zero);
                Assert.That(GetField(request, "acquiredSourceLease"), Is.Null);
                Assert.That(GetField(request, "acquiredOutputLease"), Is.Null);
                Assert.That(DictOf(GetField(request, "sourceHalls")).Count, Is.Zero);
                Assert.That(AllocationOf(GetField(request, "outputHall")).IsValid, Is.False);
            }
            finally { Cleanup(request); lease?.Dispose(); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void ReleasePreparedResources_CalledTwice_ReleasesOwnedOnce()
        {
            TextureHallAllocator allocator = NewInjectedAllocator(512);
            Texture2D source = NewSource(128);
            TextureSourceLease lease = null;
            object request = null;
            try
            {
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation borrowed), Is.True, "the borrowed hall sits on the real allocator");
                lease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(source, borrowed) });
                TextureRequestHallPlan plan = Plan(TempRecipe, source, 128);
                request = NewRequest(plan, lease, null);
                host.testFailReservationOrdinal = -1;

                Assert.That(Acquire(host, request, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(diagnostic, Is.Null);
                Assert.That(GetField(request, "acquiredSourceLease"), Is.SameAs(lease));
                Assert.That(UseCount(lease), Is.EqualTo(1));
                Assert.That(AllocationOf(GetField(request, "ownedOutputHall")).IsValid, Is.True);
                Assert.That(CountOf(GetField(request, "temporaryPool")), Is.EqualTo(2));
                Assert.That(DictOf(GetField(request, "sourceHalls")).Count, Is.EqualTo(1));
                Assert.That(AllocationOf(GetField(request, "outputHall")).IsValid, Is.True);
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(4), "the Output and two temporary slots are held on top of the borrowed hall");

                ReleasePrepared(host, request);
                ulong versionAfterFirst = allocator.Version;
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1), "the first rollback released the Output and both temporary slots");
                Assert.That(UseCount(lease), Is.Zero, "the acquired use returned to the caller lease");
                Assert.That(lease.IsValid, Is.True, "the rollback never Disposes a requested Lease");
                Assert.That(CountOf(GetField(request, "ownedSourceHalls")), Is.Zero);
                Assert.That(AllocationOf(GetField(request, "ownedOutputHall")).IsValid, Is.False);
                Assert.That(CountOf(GetField(request, "temporaryPool")), Is.Zero);
                Assert.That(DictOf(GetField(request, "sourceHalls")).Count, Is.Zero);
                Assert.That(AllocationOf(GetField(request, "outputHall")).IsValid, Is.False);

                ReleasePrepared(host, request);
                Assert.That(allocator.Version, Is.EqualTo(versionAfterFirst), "the second rollback releases nothing because the owned fields were cleared");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1));
                Assert.That(UseCount(lease), Is.Zero);
            }
            finally { Cleanup(request); lease?.Dispose(); UnityEngine.Object.DestroyImmediate(source); }
        }

        private void Cleanup(object request)
        {
            if (request != null) ReleaseMethod.Invoke(host, new[] { request });
        }

        private static bool Acquire(TextureStackMachineHost host, object request, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { request, null };
            bool ok = (bool)AcquireMethod.Invoke(host, args);
            diagnostic = (StackMachineDiagnostic)args[1];
            return ok;
        }

        private static void ReleasePrepared(TextureStackMachineHost host, object request) => ReleaseMethod.Invoke(host, new[] { request });

        private static object NewRequest(TextureRequestHallPlan hallPlan, TextureSourceLease sourceLease, TextureOutputLease outputLease)
        {
            object request = Activator.CreateInstance(RequestType, nonPublic: true);
            RequestType.GetField("hallPlan", Refl).SetValue(request, hallPlan);
            RequestType.GetField("requestedSourceLease", Refl).SetValue(request, sourceLease);
            RequestType.GetField("requestedOutputLease", Refl).SetValue(request, outputLease);
            return request;
        }

        private static object GetField(object request, string name) => RequestType.GetField(name, Refl).GetValue(request);

        private static TextureHallAllocation AllocationOf(object value) => (TextureHallAllocation)value;

        private static IDictionary DictOf(object value) => (IDictionary)value;

        private static int CountOf(object value) => value is ICollection collection ? collection.Count : 0;

        private static int UseCount(TextureSourceLease lease) => (int)typeof(TextureSourceLease).GetField("useCount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lease);

        private TextureHallAllocator NewInjectedAllocator(int gridEdge)
        {
            var allocator = new TextureHallAllocator(gridEdge);
            typeof(TextureStackMachineHost).GetField("allocator", Refl).SetValue(host, allocator);
            return allocator;
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