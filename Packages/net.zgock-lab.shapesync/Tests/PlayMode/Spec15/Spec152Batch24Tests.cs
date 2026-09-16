// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>
    /// Spec15-2 Phase06-24 preparation for the Disable/Enable integration boundary (H01/H02). The shared
    /// Spec152GpuFixture owns host setup, GPU waits, and teardown. New-queue integration execution is gated
    /// until 06-26; this file is compiled and statically discovered only until the public entry switch.
    /// </summary>
    public sealed class Spec152Batch24Tests
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

        // H01: one real submitted request plus two pending requests is disabled as one ending transition. Pending
        // requests are detached first; all handles terminate once, queue/map/budget become empty, the submitted
        // request remains physically fence-owned, and a new admission is rejected while acceptance is disabled.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H01_Disable_TerminatesPending()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            TextureExecutor executor = new TextureExecutor(host);
            TextureExecutionHandle submittedHandle;
            TextureExecutionHandle pendingHandleA;
            TextureExecutionHandle pendingHandleB;
            Assert.That(Enqueue(scope, executor, host.CreateOrigin(), stub, out submittedHandle), Is.True);
            Assert.That(Enqueue(scope, executor, host.CreateOrigin(), stub, out pendingHandleA), Is.True);
            Assert.That(Enqueue(scope, executor, host.CreateOrigin(), stub, out pendingHandleB), Is.True);

            int submittedNotifications = 0;
            int pendingNotifications = 0;
            int pendingBNotifications = 0;
            Action<TextureExecutionHandle> submittedObserver = _ => submittedNotifications++;
            Action<TextureExecutionHandle> pendingObserver = _ => pendingNotifications++;
            Action<TextureExecutionHandle> pendingBObserver = _ => pendingBNotifications++;
            submittedHandle.Completed += submittedObserver;
            pendingHandleA.Completed += pendingObserver;
            pendingHandleB.Completed += pendingBObserver;
            int destroyingNotifications = 0;
            Action destroying = () => destroyingNotifications++;
            host.Destroying += destroying;
            host.testFencePending = true;
            try
            {
                scope.Tick();

                Assert.That(submittedHandle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(host.PendingRequestCount, Is.EqualTo(2));
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.EqualTo(2));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.EqualTo(2L * DeliveryBytes(128, 128)));

                RenderTexture rawGrid = host.Grid;
                host.enabled = false;

                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.True, "Disable keeps submitted GPU resources until its fence passes");
                Assert.That(host.Grid, Is.Not.Null, "Disable does not release the physical grid");
                Assert.That(rawGrid, Is.Not.Null);
                Assert.That(rawGrid.IsCreated(), Is.True, "Disable keeps the submitted raw grid created");
                Assert.That(((TextureHallAllocator)GetField(host, "allocator")).OccupiedRoomCount, Is.EqualTo(2));
                Assert.That(GetField(host, "acceptingRequests"), Is.False);
                Assert.That(GetField(host, "closingNotifications"), Is.False, "Disable drains its ending notification batch");
                Assert.That(destroyingNotifications, Is.EqualTo(1));

                AssertHostDisabled(submittedHandle);
                AssertHostDisabled(pendingHandleA);
                AssertHostDisabled(pendingHandleB);
                Assert.That(submittedNotifications, Is.EqualTo(1));
                Assert.That(pendingNotifications, Is.EqualTo(1));
                Assert.That(pendingBNotifications, Is.EqualTo(1));

                bool accepted = executor.TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle rejectedHandle, out StackMachineDiagnostic rejectedDiagnostic);
                if (rejectedHandle != null) scope.Handles.Add(rejectedHandle);
                Assert.That(accepted, Is.False);
                Assert.That(rejectedHandle, Is.Null);
                Assert.That(rejectedDiagnostic, Is.Not.Null);
                Assert.That(rejectedDiagnostic.domainCode, Is.EqualTo("HostDisabled"));
            }
            finally
            {
                host.testFencePending = false;
                host.Destroying -= destroying;
                submittedHandle.Completed -= submittedObserver;
                pendingHandleA.Completed -= pendingObserver;
                pendingHandleB.Completed -= pendingBObserver;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // H02: after H01, Enable reopens the same Host while the cancelled submitted request still owns its real
        // fence. A new request is queued on that Host; releasing the fence lets one prepared update clean the old
        // request first and submit the new request, without refiring the old terminal notification.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H02_Enable_CleansOldFenceFirst()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            TextureExecutor executor = new TextureExecutor(host);
            TextureExecutionHandle oldSubmitted;
            TextureExecutionHandle oldPendingA;
            TextureExecutionHandle oldPendingB;
            Assert.That(Enqueue(scope, executor, host.CreateOrigin(), stub, out oldSubmitted), Is.True);
            Assert.That(Enqueue(scope, executor, host.CreateOrigin(), stub, out oldPendingA), Is.True);
            Assert.That(Enqueue(scope, executor, host.CreateOrigin(), stub, out oldPendingB), Is.True);
            int oldSubmittedNotifications = 0;
            Action<TextureExecutionHandle> oldSubmittedObserver = _ => oldSubmittedNotifications++;
            oldSubmitted.Completed += oldSubmittedObserver;

            host.testFencePending = true;
            try
            {
                scope.Tick();
                Assert.That(oldSubmitted.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(host.PendingRequestCount, Is.EqualTo(2));

                host.enabled = false;
                host.enabled = true;
                Assert.That(GetField(host, "acceptingRequests"), Is.True);
                Assert.That(host.HasSubmittedRequest, Is.True, "Enable does not discard the old submitted fence");

                bool accepted = executor.TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle next, out StackMachineDiagnostic diagnostic);
                if (next != null) scope.Handles.Add(next);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(next, Is.Not.Null);
                Assert.That(next.Status, Is.EqualTo(TextureExecutionStatus.Queued));
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));

                object oldSubmittedSlot = GetField(host, "submitted");
                Assert.That(oldSubmittedSlot, Is.Not.Null);
                Assert.That(oldSubmittedSlot.GetType().GetField("handle", Refl).GetValue(oldSubmittedSlot), Is.SameAs(oldSubmitted));
                Assert.That(host.testFencePending, Is.True);
                scope.Tick();
                Assert.That(next.Status, Is.EqualTo(TextureExecutionStatus.Queued));
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(GetField(host, "submitted"), Is.SameAs(oldSubmittedSlot));
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                Assert.That(oldSubmittedNotifications, Is.EqualTo(1));

                host.testFencePending = false;
                yield return WaitUntilSubmitted(next);
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(next.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(oldSubmittedNotifications, Is.EqualTo(1), "old HostDisabled notification is not refired by fence cleanup");

                yield return Spec152GpuFixture.WaitForRealGpu(() => next.IsCompleted);
                Assert.That(next.IsCompleted, Is.True);
                Assert.That(next.Succeeded, Is.True, next.Diagnostic?.message);
                Assert.That(next.Result, Is.Not.Null);
                Assert.That(next.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                if (delivery != null) scope.Deliveries.Add(delivery);
                Assert.That(delivery, Is.Not.Null);
                delivery.Dispose();
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
            }
            finally
            {
                host.testFencePending = false;
                oldSubmitted.Completed -= oldSubmittedObserver;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private static bool Enqueue(Spec152GpuScope scope, TextureExecutor executor, TextureExecutionOriginKey origin, TextureRecipeStub stub, out TextureExecutionHandle handle)
        {
            bool accepted = executor.TryExecute(stub, origin, out handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(accepted, Is.True, diagnostic?.message);
            return accepted;
        }

        private static IEnumerator WaitUntilSubmitted(TextureExecutionHandle handle)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + Spec152GpuFixture.RealGpuWaitLimitSeconds;
            while (handle.Status != TextureExecutionStatus.Submitted)
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) Assert.Fail("The real GPU wait exceeded the 30-second test limit.");
                yield return null;
            }
        }

        private static void AssertHostDisabled(TextureExecutionHandle handle)
        {
            Assert.That(handle.IsCompleted, Is.True);
            Assert.That(handle.Succeeded, Is.False);
            Assert.That(handle.Diagnostic, Is.Not.Null);
            Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("HostDisabled"));
            Assert.That(handle.Result, Is.Null);
        }

        private static long DeliveryBytes(int width, int height)
            => checked((long)width * height * TextureGpuCapabilityProbe.BytesPerPixel);

        private static object GetField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private static long LongField(TextureStackMachineHost host, string name)
            => (long)GetField(host, name);

        private static int CollectionCount(TextureStackMachineHost host, string name)
        {
            object collection = GetField(host, name);
            return (int)collection.GetType().GetProperty("Count").GetValue(collection);
        }

    }
}
