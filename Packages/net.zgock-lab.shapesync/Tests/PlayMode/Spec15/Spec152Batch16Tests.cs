// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>
    /// Spec15-2 Phase06-16 preparation for waiting stability, wake-up, and strict FIFO (Q03/Q04/Q07).
    /// The shared Spec152GpuFixture owns host setup, real GPU waits, readback, and teardown. New-queue
    /// integration execution is gated until 06-26; this batch is compiled and statically inspected only.
    /// </summary>
    public sealed class Spec152Batch16Tests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private Spec152GpuScope scope;

        [SetUp]
        public void SetUp() => scope = new Spec152GpuScope();

        [UnityTearDown]
        public IEnumerator CleanupFixture()
        {
            yield return Spec152GpuFixture.Cleanup(scope);
            scope = null;
        }

        // Q03: a 256x256 external hall blocks every room on the 256 grid. The COPY128 head enters WaitingForHalls
        // once; the cached version makes the following warm-up plus 100 direct Updates allocation-free and probe-free.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q03_Waiting_DoesNotSpin()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Assert.That(host.TryReserveHall(256, 256, out TextureHallAllocation externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);

            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            SetHostField(host, "testFencePending", true);
            bool enqueued = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateCopy128Stub(source), host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            Assert.That(host.PendingRequestCount, Is.EqualTo(1));

            int probeBefore = host.testLiveProbeCount;
            scope.Tick();
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
            Assert.That(handle.IsCompleted, Is.False);
            Assert.That(handle.WaitingDiagnostic, Is.Not.Null);
            Assert.That(handle.Diagnostic, Is.Null, "waiting is non-terminal");
            Assert.That(host.PendingRequestCount, Is.EqualTo(1));
            Assert.That(host.HasSubmittedRequest, Is.False);
            int probeAfterFirstWait = host.testLiveProbeCount;
            Assert.That(probeAfterFirstWait, Is.EqualTo(probeBefore + 1));
            Assert.That(probeAfterFirstWait, Is.EqualTo(1), "the blocked head is probed exactly once cumulatively");

            // Warm the already-created delegate once outside the measured region.
            scope.Tick();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) scope.Tick();
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            int probeAfterMeasuredUpdates = host.testLiveProbeCount;

            Assert.That(allocatedAfter - allocatedBefore, Is.EqualTo(0), "cached waiting Updates allocate no managed bytes");
            Assert.That(probeAfterMeasuredUpdates, Is.EqualTo(probeAfterFirstWait), "cached waiting Updates do not re-probe the unchanged allocator version");
            Assert.That(handle.IsCompleted, Is.False);
            Assert.That(handle.Diagnostic, Is.Null);
            Assert.That(host.PendingRequestCount, Is.EqualTo(1));
            Assert.That(host.HasSubmittedRequest, Is.False);

            SetHostField(host, "testFencePending", false);
            Assert.That(host.TryReleaseHall(externalHall), Is.True);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // Q04: after Q03's blocked state, releasing the external 256 hall changes the allocator version. The next
        // single Update performs exactly one new probe, submits COPY128, and the real GPU result is fully checked.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q04_Waiting_ReleaseWakes()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
            Assert.That(host.TryReserveHall(256, 256, out TextureHallAllocation externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);

            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            SetHostField(host, "testFencePending", true);
            bool enqueued = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateCopy128Stub(source), host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);

            scope.Tick();
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
            int probeBeforeRelease = host.testLiveProbeCount;
            ulong versionBeforeRelease = allocator.Version;
            Assert.That(host.TryReleaseHall(externalHall), Is.True);
            Assert.That(allocator.Version, Is.GreaterThan(versionBeforeRelease), "hall release wakes the version-gated head");

            scope.Tick();
            Assert.That(host.testLiveProbeCount, Is.EqualTo(probeBeforeRelease + 1), "the changed version causes exactly one new probe");
            Assert.That(host.HasSubmittedRequest, Is.True);
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(2), "COPY128 owns Source and Output while submitted");

            SetHostField(host, "testFencePending", false);
            yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            if (delivery != null) scope.Deliveries.Add(delivery);
            Assert.That(delivery, Is.Not.Null);
            yield return Spec152GpuFixture.AssertAllPixels(scope, delivery.Texture, 128, 128, new Vector4(0.25f, 0.5f, 0.75f, 1f));
            delivery.Dispose();
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(host.HasSubmittedRequest, Is.False);
            Assert.That(allocator.OccupiedRoomCount, Is.Zero);
            Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
            Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
            Assert.That(host.LiveTransientGpuBytes, Is.Zero);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // Q07: the blocked Output-only256 head remains first even though the FILL128 successor is runnable with the
        // external 128 hall held. Disposing only the head removes it; exactly the next Update submits the tail.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q07_Waiting_IsStrictFifo()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
            Assert.That(host.TryReserveHall(128, 128, out TextureHallAllocation externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);

            SetHostField(host, "testFencePending", true);
            bool headEnqueued = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateOutputOnlyStub(256, 256), host.CreateOrigin(), out TextureExecutionHandle head, out StackMachineDiagnostic headDiagnostic);
            if (head != null) scope.Handles.Add(head);
            Assert.That(headEnqueued, Is.True, headDiagnostic?.message);
            bool tailEnqueued = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateFill128Stub(), host.CreateOrigin(), out TextureExecutionHandle tail, out StackMachineDiagnostic tailDiagnostic);
            if (tail != null) scope.Handles.Add(tail);
            Assert.That(tailEnqueued, Is.True, tailDiagnostic?.message);
            Assert.That(host.PendingRequestCount, Is.EqualTo(2));

            scope.Tick();
            Assert.That(head.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
            Assert.That(tail.Status, Is.EqualTo(TextureExecutionStatus.Queued), "a runnable successor cannot overtake the waiting head");
            Assert.That(host.PendingRequestCount, Is.EqualTo(2));
            Assert.That(host.HasSubmittedRequest, Is.False);
            Assert.That(host.testLiveProbeCount, Is.EqualTo(1), "only the FIFO head is probed");

            head.Dispose();
            Assert.That(head.IsCompleted, Is.True);
            Assert.That(head.Diagnostic, Is.Not.Null);
            Assert.That(head.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
            Assert.That(host.PendingRequestCount, Is.EqualTo(1));
            Assert.That(tail.Status, Is.EqualTo(TextureExecutionStatus.Queued));

            scope.Tick();
            Assert.That(host.HasSubmittedRequest, Is.True);
            Assert.That(tail.Status, Is.EqualTo(TextureExecutionStatus.Submitted), "the next Update submits the tail after head cancellation");
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(3), "external hall plus FILL output and temporary halls are live");

            SetHostField(host, "testFencePending", false);
            yield return Spec152GpuFixture.WaitForRealGpu(() => tail.IsCompleted);
            Assert.That(tail.Succeeded, Is.True, tail.Diagnostic?.message);
            Assert.That(tail.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            if (delivery != null) scope.Deliveries.Add(delivery);
            Assert.That(delivery, Is.Not.Null);
            yield return Spec152GpuFixture.AssertAllPixels(scope, delivery.Texture, 128, 128, new Vector4(1f, 0f, 0f, 1f));
            delivery.Dispose();
            Assert.That(host.HasSubmittedRequest, Is.False);
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(host.TryReleaseHall(externalHall), Is.True);
            Assert.That(allocator.OccupiedRoomCount, Is.Zero);
            Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
            Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
            Assert.That(host.LiveTransientGpuBytes, Is.Zero);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private static void SetHostField(TextureStackMachineHost host, string name, object value)
            => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);
    }
}
