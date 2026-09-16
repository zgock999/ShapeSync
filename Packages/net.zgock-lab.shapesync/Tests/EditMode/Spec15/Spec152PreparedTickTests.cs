// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-12 CPU coverage for the new Update processing order. Three synchronous cases: a head
    /// that cannot fit a fully externally-occupied grid waits in place with the §14.1 detail, a runnable
    /// successor behind a blocked head does not overtake (one head per call), and a cached allocator-version
    /// match skips the occupancy snapshot (no re-probe). Only placements that never reach the GPU are used;
    /// the measurement Update delegate is built once per test and the measured call contains no reflection or
    /// asserts. Fixture-owned textures are destroyed and the queue/map/budget/flags are left empty in finally.
    /// </summary>
    public sealed class Spec152PreparedTickTests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);

        // The word-code lets only RESAMPLE bridge differing source/output extents (TexturePlanCompiler.TryCompileWord):
        // RESAMPLE Source128 into the temporary, then COPY the temporary into the Output.
        private const string ResampleRecipe = "$a RESAMPLE $out COPY";

        private GameObject root;
        private TextureStackMachineHost host;
        private TextureHallAllocator allocator;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Tick Host");
            host = root.AddComponent<TextureStackMachineHost>();
            allocator = new TextureHallAllocator(512);
            SetHostField("allocator", allocator);
            SetHostField("acceptingRequests", true);
        }

        [TearDown]
        public void TearDown()
        {
            PendingQueue().GetType().GetMethod("Clear").Invoke(PendingQueue(), null);
            PendingMap().Clear();
            SetHostField("pendingDeliveryBytes", 0L);
            SetHostField("submitted", null);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void UpdatePrepared_FullGridExternallyOccupied_HeadWaitsInPlace()
        {
            // Occupy the whole 512 grid with four real 256 halls so nothing of the head's demand fits.
            for (int i = 0; i < 4; i++)
                Assert.That(allocator.TryReserve(256, 256, out _), Is.True, $"external hall {i}");

            Texture2D source = NewSource(128);
            TextureExecutionHandle handle = null;
            try
            {
                int probeBefore = host.testLiveProbeCount;
                handle = new TextureExecutionHandle();
                object request = NewRequest(Plan(ResampleRecipe, source, 256), handle);
                EnqueuePending(request);

                Action update = BuildUpdateDelegate();
                update();

                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls), "the blocked head gets the non-terminal waiting state");
                Assert.That(handle.WaitingDiagnostic, Is.Not.Null);
                Assert.That(handle.WaitingDiagnostic.domainCode, Is.EqualTo("WaitingForHalls"));
                Assert.That(PendingCount(), Is.EqualTo(1), "the waiting head keeps its queue position");
                Assert.That(host.testLiveProbeCount, Is.EqualTo(probeBefore + 1), "the changed grid was probed exactly once for the head");
                Assert.That(HostField("submitted"), Is.Null, "the waiting head never reaches the GPU");

                string detail = handle.WaitingDiagnostic.detail;
                Assert.That(detail, Is.Not.Null, "a new waiting decision builds the §14.1 detail");
                string[] fields = { ";output=256x256", ";grid=512", ";occupiedRooms=16", ";pendingCount=1",
                    ";hasSubmitted=false", ";retainedSourceLeases=0", ";retainedOutputLeases=0", ";temporarySlots=1",
                    ";blockedStage=Source(a)", ";allocatorVersion=" };
                Assert.That(detail, Does.StartWith("origin=0"), "the detail starts with origin");
                int previous = detail.IndexOf("origin=", StringComparison.Ordinal);
                foreach (string field in fields)
                {
                    int at = detail.IndexOf(field, previous + 1, StringComparison.Ordinal);
                    Assert.That(at, Is.GreaterThan(previous), $"the detail contains {field} in the §14.1 order");
                    previous = at;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void UpdatePrepared_BlockedHeadWaiting_RunnableSuccessorDoesNotOvertake()
        {
            // Checkerboard: eight of sixteen rooms stay free, so a 128 demand fits but a contiguous 256 does not.
            var filled = new System.Collections.Generic.List<TextureHallAllocation>();
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

            Texture2D headSource = NewSource(256);
            Texture2D successorSource = NewSource(128);
            TextureExecutionHandle headHandle = null;
            TextureExecutionHandle successorHandle = null;
            try
            {
                int probeBefore = host.testLiveProbeCount;
                headHandle = new TextureExecutionHandle();
                successorHandle = new TextureExecutionHandle();
                EnqueuePending(NewRequest(Plan(ResampleRecipe, headSource, 256), headHandle));
                EnqueuePending(NewRequest(Plan(ResampleRecipe, successorSource, 128), successorHandle));

                Action update = BuildUpdateDelegate();
                update();

                Assert.That(headHandle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls), "the head cannot fit the fragmented grid and waits");
                Assert.That(successorHandle.Status, Is.EqualTo(TextureExecutionStatus.Queued), "the runnable successor does not overtake the waiting head");
                Assert.That(PendingCount(), Is.EqualTo(2), "both requests keep their queue positions");
                Assert.That(host.testLiveProbeCount, Is.EqualTo(probeBefore + 1), "only the head was probed in this call");
                Assert.That(HostField("submitted"), Is.Null, "one call performs at most one submit and none happened");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(headSource);
                UnityEngine.Object.DestroyImmediate(successorSource);
            }
        }

        [Test]
        public void UpdatePrepared_VersionCacheMatches_NoSnapshotReprobe()
        {
            // Nothing is occupied; the version cache matches, so the snapshot is skipped and the detail is not rebuilt.
            Texture2D source = NewSource(128);
            TextureExecutionHandle handle = null;
            try
            {
                handle = new TextureExecutionHandle();
                object request = NewRequest(Plan(ResampleRecipe, source, 128), handle);
                EnqueuePending(request);
                SetRequestField(request, "waitingAllocatorVersion", allocator.Version);
                SetRequestField(request, "hasWaitingAllocatorVersion", true);
                StackMachineDiagnostic existing = StackMachineDiagnostic.CreateDomain("texture", "WaitingForHalls", "waiting decision from an earlier tick.");
                handle.SetWaiting(existing);

                int probeBefore = host.testLiveProbeCount;
                Action update = BuildUpdateDelegate();
                update();

                Assert.That(host.testLiveProbeCount, Is.EqualTo(probeBefore), "a matching cached version must not create a new snapshot");
                Assert.That(PendingCount(), Is.EqualTo(1), "the request stays at the queue head");
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls), "the waiting state is untouched");
                Assert.That(handle.WaitingDiagnostic, Is.SameAs(existing), "the detail is not rebuilt for the same version");
                Assert.That(HostField("submitted"), Is.Null, "the version-cache skip never reaches the GPU");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void TryPollPreparedFence_InjectedFailure_IsDiagnosticAndConsumedOnce()
        {
            object request = Activator.CreateInstance(RequestType, nonPublic: true);
            MethodInfo poll = typeof(TextureStackMachineHost).GetMethod("TryPollPreparedFence", Refl);
            try
            {
                host.testFencePending = false;
                host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.FencePoll;
                object[] args = { request, false, null };
                Assert.That((bool)poll.Invoke(host, args), Is.True);
                Assert.That((bool)args[1], Is.False);
                Assert.That(((StackMachineDiagnostic)args[2]).domainCode, Is.EqualTo("GpuFencePollFailed"));
                Assert.That(host.testFailurePoint, Is.EqualTo(TextureStackMachineHost.TestFailurePoint.None));

                host.testFencePending = true;
                object[] pendingArgs = { request, false, null };
                Assert.That((bool)poll.Invoke(host, pendingArgs), Is.False);
                Assert.That((bool)pendingArgs[1], Is.False);
                Assert.That(pendingArgs[2], Is.Null);
            }
            finally
            {
                host.testFencePending = false;
                host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.None;
            }
        }

        private Action BuildUpdateDelegate()
            => (Action)typeof(TextureStackMachineHost).GetMethod("UpdatePrepared", Refl).CreateDelegate(typeof(Action), host);

        private object NewRequest(TextureRequestHallPlan hallPlan, TextureExecutionHandle handle)
        {
            object request = Activator.CreateInstance(RequestType, nonPublic: true);
            RequestType.GetField("handle", Refl).SetValue(request, handle);
            RequestType.GetField("hallPlan", Refl).SetValue(request, hallPlan);
            return request;
        }

        private void SetRequestField(object request, string name, object value) => RequestType.GetField(name, Refl).SetValue(request, value);

        private object HostField(string name) => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private void SetHostField(string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private object PendingQueue() => HostField("pending");

        private int PendingCount() => ((ICollection)PendingQueue()).Count;

        private void EnqueuePending(object request) => PendingQueue().GetType().GetMethod("Enqueue").Invoke(PendingQueue(), new[] { request });

        private IDictionary PendingMap() => (IDictionary)HostField("pendingByOrigin");

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