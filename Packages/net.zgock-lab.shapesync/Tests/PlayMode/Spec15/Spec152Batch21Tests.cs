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
    /// Spec15-2 Phase06-21 preparation for borrowed Source lifetime, reuse, mismatch acceptance, and fixed-position
    /// layout rejection (L03/L04/L05/L06). The shared fixture owns host setup, real retained leases, waits, and
    /// teardown. New-queue integration execution is gated until Phase06-26; this file is compiled and discovered
    /// only during this phase.
    /// </summary>
    public sealed class Spec152Batch21Tests
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

        // L03: a submitted borrowed Source use remains live after caller Dispose and is released only by fence cleanup.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L03_SubmittedBorrow_DisposeDefers()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source,
                lease => L03SubmittedBorrowCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L03SubmittedBorrowCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            TextureHallAllocator allocator = Allocator(host);
            int occupiedBeforeSubmit = allocator.OccupiedRoomCount;
            host.testFencePending = true;
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(
                    CreateCopyPlan(source, 128), host.CreateOrigin(), new TextureExecutionOptions(sourceLease: lease),
                    out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(handle, Is.Not.Null);

                scope.Tick();

                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(handle.IsCompleted, Is.False);
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(SourceUseCount(lease), Is.EqualTo(1));
                Assert.That(lease.IsValid, Is.True);
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBeforeSubmit + 1),
                    "submitted borrowed Source plus one newly owned Output hall are live");

                lease.Dispose();

                Assert.That(SourceReleaseRequested(lease), Is.True);
                Assert.That(lease.IsValid, Is.True, "caller Dispose is deferred while the submitted use is live");
                Assert.That(SourceUseCount(lease), Is.EqualTo(1));
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBeforeSubmit + 1));
                Assert.That(host.HasSubmittedRequest, Is.True);

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);

                Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(lease.IsValid, Is.False, "fence cleanup releases the caller lease after its final use");
                Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
                Assert.That(allocator.OccupiedRoomCount, Is.Zero);

                Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                if (delivery != null) scope.Deliveries.Add(delivery);
                Assert.That(delivery, Is.Not.Null);
                delivery.Dispose();
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // L04: reusing the same Source object and retained lease skips ingest and does not create a second lease.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L04_ReusedSource_NoDuplicateLease()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source,
                lease => L04ReusedSourceCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L04ReusedSourceCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            TextureHallAllocator allocator = Allocator(host);
            int occupiedBefore = allocator.OccupiedRoomCount;
            int ingestBefore = IngestDispatchCount(host);
            int sourceLeaseCountBefore = host.OutstandingSourceLeaseCount;
            host.testFencePending = true;
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(
                    CreateCopyPlan(source, 128), host.CreateOrigin(),
                    new TextureExecutionOptions(sourceLease: lease, retainSourceLease: true),
                    out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(handle, Is.Not.Null);

                scope.Tick();
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(SourceUseCount(lease), Is.EqualTo(1));

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);

                Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
                Assert.That(IngestDispatchCount(host), Is.EqualTo(ingestBefore), "borrowed Source reuse performs no new ingest");
                Assert.That(handle.Result.TryTakeSourceLease(out TextureSourceLease returnedLease), Is.False);
                Assert.That(returnedLease, Is.Null, "borrowed Source reuse never returns a duplicate lease");
                Assert.That(lease.IsValid, Is.True);
                Assert.That(SourceUseCount(lease), Is.Zero);
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(sourceLeaseCountBefore));

                Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                if (delivery != null) scope.Deliveries.Add(delivery);
                Assert.That(delivery, Is.Not.Null);
                delivery.Dispose();
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore), "only the original retained Source hall remains");
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // L05: valid and invalid Source-mismatch candidates are separate fixed cases. Only acceptance disposes old lease.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L05_SourceMismatch_ValidCandidateDisposesOldLease() => Spec152_L05_SourceMismatch_OnlyAcceptDisposes(true);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L05_SourceMismatch_InvalidCandidateKeepsOldLease() => Spec152_L05_SourceMismatch_OnlyAcceptDisposes(false);

        private IEnumerator Spec152_L05_SourceMismatch_OnlyAcceptDisposes(bool validCandidate)
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D sourceA = Spec152GpuFixture.CreateCopy128Source(scope);
            Texture2D sourceB = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, sourceA,
                lease => L05SourceMismatchCase(host, sourceB, lease, validCandidate));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L05SourceMismatchCase(TextureStackMachineHost host, Texture2D candidateSource, TextureSourceLease oldLease, bool validCandidate)
        {
            TextureHallAllocator allocator = Allocator(host);
            int occupiedBefore = allocator.OccupiedRoomCount;
            int ingestBefore = IngestDispatchCount(host);
            TextureExecutionPlan candidate = CreateCopyPlan(candidateSource, validCandidate ? 128 : 256);
            host.testFencePending = true;
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(
                    candidate, host.CreateOrigin(), new TextureExecutionOptions(sourceLease: oldLease),
                    out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);

                if (validCandidate)
                {
                    Assert.That(accepted, Is.True, diagnostic?.message);
                    Assert.That(handle, Is.Not.Null);
                    Assert.That(SourceReleaseRequested(oldLease), Is.True, "old mismatched lease is disposed only after candidate acceptance");
                    Assert.That(oldLease.IsValid, Is.False);
                    Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
                    Assert.That(host.PendingRequestCount, Is.EqualTo(1));

                    scope.Tick();

                    Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                    Assert.That(IngestDispatchCount(host), Is.EqualTo(ingestBefore + 1), "accepted candidate ingests its new Source");

                    host.testFencePending = false;
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
                }
                else
                {
                    Assert.That(accepted, Is.False);
                    Assert.That(handle, Is.Null);
                    Assert.That(diagnostic, Is.Not.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
                    Assert.That(SourceReleaseRequested(oldLease), Is.False, "failed candidate does not dispose the old lease");
                    Assert.That(oldLease.IsValid, Is.True);
                    Assert.That(SourceUseCount(oldLease), Is.Zero);
                    Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
                    Assert.That(host.PendingRequestCount, Is.Zero);
                    Assert.That(host.HasSubmittedRequest, Is.False);
                    Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore));
                }
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // L06: a borrowed Source128 fixed hall leaves only three free rooms; Output256 cannot be packed there.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_L06_BorrowedLayout_IsNotEmptyPacking()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source,
                lease => L06BorrowedLayoutCase(host, source, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator L06BorrowedLayoutCase(TextureStackMachineHost host, Texture2D source, TextureSourceLease lease)
        {
            TextureHallAllocator allocator = Allocator(host);
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1));
            bool accepted = new TextureExecutor(host).TryExecute(
                CreateResamplePlan(source, 256), host.CreateOrigin(), new TextureExecutionOptions(sourceLease: lease),
                out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);

            Assert.That(accepted, Is.False);
            Assert.That(handle, Is.Null);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
            Assert.That(lease.IsValid, Is.True);
            Assert.That(SourceUseCount(lease), Is.Zero);
            Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(host.HasSubmittedRequest, Is.False);
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(1));
            yield break;
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

        private static TextureExecutionPlan CreateResamplePlan(Texture2D source, int edge)
        {
            var document = new MaterialRecipeDocument
            {
                wordSource = "$a RESAMPLE $out COPY",
                outputLogicalName = "out",
                outputWidth = edge,
                outputHeight = edge
            };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var stub = new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
            });
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var record = new TextureDispatchRecord(
                TextureDispatchOperation.Resample,
                new[] { "a" },
                new[] { new TextureDispatchRectangle(0, 0, source.width, source.height) },
                "out",
                new TextureDispatchRectangle(0, 0, edge, edge),
                new TextureDispatchExtent(edge, edge),
                Array.Empty<float>());
            var dispatch = new TextureDispatchPlan(null, edge, edge, new[] { record }, new[] { "a" });
            return (TextureExecutionPlan)ExecutionPlanConstructor.Invoke(new object[] { context, dispatch });
        }

        private static TextureHallAllocator Allocator(TextureStackMachineHost host)
            => (TextureHallAllocator)HostField(host, "allocator");

        private static int SourceUseCount(TextureSourceLease lease)
            => (int)typeof(TextureSourceLease).GetField("useCount", Refl).GetValue(lease);

        private static bool SourceReleaseRequested(TextureSourceLease lease)
            => (bool)typeof(TextureSourceLease).GetProperty("IsReleaseRequested", Refl).GetValue(lease);

        private static int IngestDispatchCount(TextureStackMachineHost host)
            => (int)typeof(TextureStackMachineHost).GetProperty("IngestDispatchCount", Refl).GetValue(host);

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);
    }
}
