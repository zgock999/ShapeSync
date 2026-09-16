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
    /// Spec15-2 Phase06-20 preparation for queued borrowed Source leases (L01/L02/L07). The shared
    /// Spec152GpuFixture owns host setup, real GPU waits, retained lease creation, and teardown. New-queue
    /// integration execution is gated until 06-26; this file is compiled and statically inspected only.
    /// </summary>
    public sealed class Spec152Batch20Tests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly ConstructorInfo ExecutionPlanConstructor = typeof(TextureExecutionPlan).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(TextureBindingContext), typeof(TextureDispatchPlan) }, null);

        private Spec152GpuScope scope;

        [SetUp]
        public void SetUp() => scope = new Spec152GpuScope();

        [UnityTearDown]
        public IEnumerator CleanupFixture()
        {
            yield return Spec152GpuFixture.Cleanup(scope);
            scope = null;
        }

        // L01: a borrowed Source lease is only validated at enqueue. The pending request has no acquired use,
        // so the caller-owned lease useCount and allocator occupancy are unchanged immediately after acceptance.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L01_QueuedBorrow_DoesNotPin()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source, lease => L01QueuedBorrowCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L01QueuedBorrowCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            host.testFencePending = true;
            try
            {
                int occupiedBefore = Allocator(host).OccupiedRoomCount;
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(lease.IsValid, Is.True);
                TextureExecutionOriginKey origin = host.CreateOrigin();
                TextureExecutionOptions options = new TextureExecutionOptions(sourceLease: lease);
                Assert.That(Enqueue(host, CreateCopyPlan(source, 128), origin, options, out TextureExecutionHandle handle), Is.True);
                if (handle != null) scope.Handles.Add(handle);

                Assert.That(handle.IsCompleted, Is.False);
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Queued));
                Assert.That(SourceUseCount(lease), Is.Zero, "queued borrowed Source does not acquire a lease use");
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(occupiedBefore), "queue admission does not pin another hall");
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                Assert.That(host.HasSubmittedRequest, Is.False);

                handle.Dispose();
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(lease.IsValid, Is.True);
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(occupiedBefore));
                Assert.That(host.PendingRequestCount, Is.Zero);
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // L02: after the caller releases the borrowed lease, the queued head is not replaced with another lease.
        // The next Update revalidates the original reference and terminalizes it with SourceLeaseInvalid.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L02_QueuedBorrow_DisposeInvalidates()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source, lease => L02DisposedBorrowCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L02DisposedBorrowCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            host.testFencePending = true;
            try
            {
                int occupiedBefore = Allocator(host).OccupiedRoomCount;
                TextureExecutionOptions options = new TextureExecutionOptions(sourceLease: lease);
                Assert.That(Enqueue(host, CreateCopyPlan(source, 128), host.CreateOrigin(), options, out TextureExecutionHandle handle), Is.True);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));

                lease.Dispose();
                Assert.That(lease.IsValid, Is.False, "caller Dispose releases the retained Source lease before the head Update");
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(occupiedBefore - 1));

                scope.Tick();
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Result, Is.Null);
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("SourceLeaseInvalid"));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False, "invalid borrowed Source never reaches GPU submit");
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(occupiedBefore - 1));
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // L07: the retained Source hall is the only blocker for a 256x256 head. The tail borrows the same lease
        // but remains unpinned behind the head. Releasing the caller lease lets the head submit; only after that
        // head completes does the tail reach input revalidation and become SourceLeaseInvalid.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L07_Fifo_NoQueuedPinCycle()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source, lease => L07FifoBorrowCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L07FifoBorrowCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            host.testFencePending = true;
            try
            {
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(1));
                TextureExecutionOriginKey headOrigin = host.CreateOrigin();
                Assert.That(Enqueue(host, CreateOutputOnlyPlan(256, 256), headOrigin, null, out TextureExecutionHandle head), Is.True);
                if (head != null) scope.Handles.Add(head);
                scope.Tick();
                Assert.That(head.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
                Assert.That(head.IsCompleted, Is.False);
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(1));

                TextureExecutionOptions borrowedOptions = new TextureExecutionOptions(sourceLease: lease);
                TextureExecutionOriginKey tailOrigin = host.CreateOrigin();
                Assert.That(Enqueue(host, CreateCopyPlan(source, 128), tailOrigin, borrowedOptions, out TextureExecutionHandle tail), Is.True);
                if (tail != null) scope.Handles.Add(tail);
                Assert.That(SourceUseCount(lease), Is.Zero, "tail enqueue does not pin the borrowed lease");
                Assert.That(tail.Status, Is.EqualTo(TextureExecutionStatus.Queued));
                Assert.That(host.PendingRequestCount, Is.EqualTo(2));
                Assert.That(Allocator(host).OccupiedRoomCount, Is.EqualTo(1));

                lease.Dispose();
                Assert.That(lease.IsValid, Is.False);
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
                Assert.That(Allocator(host).OccupiedRoomCount, Is.Zero);

                scope.Tick();
                Assert.That(head.Status, Is.EqualTo(TextureExecutionStatus.Submitted), "head proceeds after the borrowed blocker is released");
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                Assert.That(tail.IsCompleted, Is.False, "tail does not overtake the submitted head");

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => head.IsCompleted);
                Assert.That(head.Succeeded, Is.True, head.Diagnostic?.message);
                Assert.That(tail.IsCompleted, Is.True, "tail reaches validation only after head completion");
                Assert.That(tail.Succeeded, Is.False);
                Assert.That(tail.Result, Is.Null);
                Assert.That(tail.Diagnostic, Is.Not.Null);
                Assert.That(tail.Diagnostic.domainCode, Is.EqualTo("SourceLeaseInvalid"));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        private bool Enqueue(TextureStackMachineHost host, TextureExecutionPlan plan, TextureExecutionOriginKey origin, TextureExecutionOptions options, out TextureExecutionHandle handle)
        {
            bool accepted = new TextureExecutor(host).TryExecute(plan, origin, options, out handle, out StackMachineDiagnostic diagnostic);
            if (!accepted) Assert.Fail(diagnostic?.message);
            return accepted;
        }

        private static TextureExecutionPlan CreateCopyPlan(Texture2D source, int edge)
        {
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            stub.Document.outputWidth = edge;
            stub.Document.outputHeight = edge;
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var record = new TextureDispatchRecord(
                TextureDispatchOperation.Copy,
                new[] { "a" },
                new[] { new TextureDispatchRectangle(0, 0, source.width, source.height) },
                "out",
                new TextureDispatchRectangle(0, 0, edge, edge),
                new TextureDispatchExtent(edge, edge),
                Array.Empty<float>());
            var dispatch = new TextureDispatchPlan(null, edge, edge, new[] { record }, new[] { "a" });
            return (TextureExecutionPlan)ExecutionPlanConstructor.Invoke(new object[] { context, dispatch });
        }

        private static TextureExecutionPlan CreateOutputOnlyPlan(int width, int height)
        {
            TextureRecipeStub stub = Spec152GpuFixture.CreateOutputOnlyStub(width, height);
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var record = new TextureDispatchRecord(
                TextureDispatchOperation.Fill,
                Array.Empty<string>(),
                Array.Empty<TextureDispatchRectangle>(),
                "out",
                new TextureDispatchRectangle(0, 0, width, height),
                new TextureDispatchExtent(width, height),
                new[] { 0f, 0f, 0f, 1f });
            var dispatch = new TextureDispatchPlan(null, width, height, new[] { record }, Array.Empty<string>());
            return (TextureExecutionPlan)ExecutionPlanConstructor.Invoke(new object[] { context, dispatch });
        }

        private static TextureHallAllocator Allocator(TextureStackMachineHost host)
            => (TextureHallAllocator)HostField(host, "allocator");

        private static int SourceUseCount(TextureSourceLease lease)
            => (int)typeof(TextureSourceLease).GetField("useCount", Refl).GetValue(lease);

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);
    }
}
