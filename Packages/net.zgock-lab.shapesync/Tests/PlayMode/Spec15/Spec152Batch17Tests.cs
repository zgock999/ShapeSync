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
    /// Spec15-2 Phase06-17 preparation for atomic rollback and submitted cancellation (Q06/Q11).
    /// The shared Spec152GpuFixture owns host setup, real GPU lease creation, waits, and teardown. New-queue
    /// integration execution is gated until 06-26; this file is compiled and statically inspected only.
    /// </summary>
    public sealed class Spec152Batch17Tests
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

        // Q06 has two fixed cases. The direct compiled dispatch artifact contains exactly two Copy records:
        // Copy(a -> @texture:0), then Copy(@texture:0 -> out), with one 128x128 temporary slot. Borrowed Source
        // reserves the requested use first, then Output ordinal 0; temporary ordinal 1 is fault-injected. The new
        // Source case reserves Source ordinal 0, then Output ordinal 1; the same injected ordinal fails Output.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q06_Rollback_ReleasesOnlyOwned_BorrowedSource() => Spec152_Q06_Rollback_ReleasesOnlyOwned(true);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q06_Rollback_ReleasesOnlyOwned_NewSource() => Spec152_Q06_Rollback_ReleasesOnlyOwned(false);

        private IEnumerator Spec152_Q06_Rollback_ReleasesOnlyOwned(bool borrowedSource)
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);

            if (borrowedSource)
            {
                // The shared helper creates a real retained Source lease through COPY128 and leaves it scope-owned.
                yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source, lease =>
                    Q06BorrowedSourceCase(host, source, lease));
            }
            else
            {
                Q06NewSourceCase(host, source);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator Q06BorrowedSourceCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            TextureExecutionPlan plan = CreateCopyChainPlan(source);
            host.testFailReservationOrdinal = 1; // Output ordinal 0 succeeds; Temporary(0) ordinal 1 fails.
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(
                    plan, host.CreateOrigin(), new TextureExecutionOptions(sourceLease: lease),
                    out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(handle, Is.Not.Null);

                scope.Tick();

                TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("HallReservationInvariantBroken"));
                Assert.That(handle.Diagnostic.detail, Does.Contain("stage=Temporary(0)"));
                Assert.That(handle.Result, Is.Null);
                Assert.That(lease.IsValid, Is.True, "rollback never disposes the caller-owned Source lease");
                Assert.That(SourceUseCount(lease), Is.Zero, "rollback returns the acquired borrowed use exactly once");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1), "only the caller-owned borrowed Source hall remains");
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
                Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
                ulong versionAfterFailure = allocator.Version;
                handle.Dispose();
                Assert.That(allocator.Version, Is.EqualTo(versionAfterFailure), "a terminal handle cannot release the rolled-back request twice");
            }
            finally
            {
                host.testFailReservationOrdinal = -1;
            }
            yield break;
        }

        private void Q06NewSourceCase(TextureStackMachineHost host, Texture2D source)
        {
            TextureExecutionPlan plan = CreateCopyChainPlan(source);
            host.testFailReservationOrdinal = 1; // Source ordinal 0 succeeds; Output ordinal 1 fails.
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(
                    plan, host.CreateOrigin(), null,
                    out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(handle, Is.Not.Null);

                scope.Tick();

                TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("HallReservationInvariantBroken"));
                Assert.That(handle.Diagnostic.detail, Does.Contain("stage=Output"));
                Assert.That(handle.Result, Is.Null);
                Assert.That(allocator.OccupiedRoomCount, Is.Zero, "the newly reserved Source hall was rolled back");
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
                Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
                ulong versionAfterFailure = allocator.Version;
                handle.Dispose();
                Assert.That(allocator.Version, Is.EqualTo(versionAfterFailure), "a terminal handle cannot release the rolled-back request twice");
            }
            finally
            {
                host.testFailReservationOrdinal = -1;
            }
        }

        // Q11: submit one borrowed-Source request while the fence contact reports pending, then cancel the handle.
        // Cancellation terminalizes and notifies immediately, while the submitted fence retains the borrowed use
        // and newly reserved Output. After the contact is released and the real fence passes, only the caller's
        // retained Source hall remains; disposing that caller lease returns the final room.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q11_CancelSubmitted_DefersRelease()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);

            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source, lease =>
                Q11SubmittedCancellationCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator Q11SubmittedCancellationCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            TextureExecutionPlan plan = CreateCopy128Plan(source);
            int notificationCount = 0;
            host.testFencePending = true;
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(
                    plan, host.CreateOrigin(), new TextureExecutionOptions(sourceLease: lease),
                    out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(handle, Is.Not.Null);
                handle.Completed += _ => notificationCount++;

                scope.Tick();

                TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(2), "borrowed Source plus new Output are held by submitted work");
                Assert.That(SourceUseCount(lease), Is.EqualTo(1));

                handle.Dispose();

                Assert.That(handle.IsCompleted, Is.True, "submitted cancellation terminalizes immediately");
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(handle.Result, Is.Null);
                Assert.That(notificationCount, Is.EqualTo(1), "cancellation notifies exactly once");
                Assert.That(host.HasSubmittedRequest, Is.True, "fence-owned resources remain until the real fence passes");
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(2));
                Assert.That(SourceUseCount(lease), Is.EqualTo(1));
                Assert.That(lease.IsValid, Is.True);

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => !host.HasSubmittedRequest);

                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1), "fence cleanup releases the new Output and keeps caller Source");
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(lease.IsValid, Is.True);
                Assert.That(notificationCount, Is.EqualTo(1), "fence cleanup does not renotify a cancelled handle");

                lease.Dispose();
                Assert.That(lease.IsValid, Is.False);
                Assert.That(allocator.OccupiedRoomCount, Is.Zero, "the final caller lease disposal releases the borrowed hall");
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        private static TextureExecutionPlan CreateCopy128Plan(Texture2D source)
        {
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var record = new TextureDispatchRecord(
                TextureDispatchOperation.Copy,
                new[] { "a" },
                new[] { new TextureDispatchRectangle(0, 0, 128, 128) },
                "out",
                new TextureDispatchRectangle(0, 0, 128, 128),
                new TextureDispatchExtent(128, 128),
                Array.Empty<float>());
            var dispatch = new TextureDispatchPlan(null, 128, 128, new[] { record }, new[] { "a" });
            return (TextureExecutionPlan)ExecutionPlanConstructor.Invoke(new object[] { context, dispatch });
        }

        private static TextureExecutionPlan CreateCopyChainPlan(Texture2D source)
        {
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var records = new[]
            {
                new TextureDispatchRecord(
                    TextureDispatchOperation.Copy,
                    new[] { "a" },
                    new[] { new TextureDispatchRectangle(0, 0, 128, 128) },
                    "@texture:0",
                    new TextureDispatchRectangle(0, 0, 128, 128),
                    new TextureDispatchExtent(128, 128),
                    Array.Empty<float>()),
                new TextureDispatchRecord(
                    TextureDispatchOperation.Copy,
                    new[] { "@texture:0" },
                    new[] { new TextureDispatchRectangle(0, 0, 128, 128) },
                    "out",
                    new TextureDispatchRectangle(0, 0, 128, 128),
                    new TextureDispatchExtent(128, 128),
                    Array.Empty<float>()),
            };
            var dispatch = new TextureDispatchPlan(null, 128, 128, records, new[] { "a" });
            return (TextureExecutionPlan)ExecutionPlanConstructor.Invoke(new object[] { context, dispatch });
        }

        private static int SourceUseCount(TextureSourceLease lease)
            => (int)typeof(TextureSourceLease).GetField("useCount", Refl).GetValue(lease);

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);
    }
}
