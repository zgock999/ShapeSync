// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>
    /// Spec15-2 Phase06-15 preparation for burst ordering and standalone capacity rejection (Q01/Q02/Q05).
    /// New-queue integration execution is gated until 06-26; this file is compiled and statically inspected only.
    /// The shared Spec152GpuFixture owns host setup, real GPU waits, readback, and cleanup.
    /// </summary>
    public sealed class Spec152Batch15Tests
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

        // Q01: enqueue three independent COPY128 requests synchronously while the fence contact prevents the
        // PlayerLoop from submitting early. Admission sees pending=3 and occupied=0; one direct Update submits the
        // head with Source+Output=2 rooms. Real fence completion and full-pixel readback are checked for every item.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q01_CopyBurst_WaitsAndCompletes()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            var handles = new TextureExecutionHandle[3];
            SetHostField(host, "testFencePending", true);
            for (int i = 0; i < handles.Length; i++)
            {
                Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
                bool enqueued = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateCopy128Stub(source), host.CreateOrigin(), out handles[i], out StackMachineDiagnostic diagnostic);
                if (handles[i] != null) scope.Handles.Add(handles[i]);
                Assert.That(enqueued, Is.True, diagnostic?.message);
            }
            Assert.That(host.PendingRequestCount, Is.EqualTo(3), "all three origins are queued before the direct tick");
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.Zero, "admission does not reserve halls");

            scope.Tick();

            Assert.That(host.HasSubmittedRequest, Is.True, "one queue head is submitted after the direct tick");
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.EqualTo(2), "COPY128 reserves Source+Output");
            SetHostField(host, "testFencePending", false);
            for (int i = 0; i < handles.Length; i++)
            {
                yield return Spec152GpuFixture.WaitForRealGpu(() => handles[i].IsCompleted);
                Assert.That(handles[i].Succeeded, Is.True, handles[i].Diagnostic?.message);
                Assert.That(handles[i].Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                if (delivery != null) scope.Deliveries.Add(delivery);
                Assert.That(delivery, Is.Not.Null);
                yield return Spec152GpuFixture.AssertAllPixels(scope, delivery.Texture, 128, 128, new Vector4(0.25f, 0.5f, 0.75f, 1f));
                delivery.Dispose();
            }
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.Zero);
            Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
            Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
            Assert.That(host.LiveTransientGpuBytes, Is.Zero);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // Q02: enqueue four independent FILL128 requests synchronously. The first submitted request owns Output plus
        // one temporary slot (2 rooms); real fence completion drains the burst and each result is red.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q02_FillBurst_ReservesTemporaryAtSubmit()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            var handles = new TextureExecutionHandle[4];
            SetHostField(host, "testFencePending", true);
            for (int i = 0; i < handles.Length; i++)
            {
                bool enqueued = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateFill128Stub(), host.CreateOrigin(), out handles[i], out StackMachineDiagnostic diagnostic);
                if (handles[i] != null) scope.Handles.Add(handles[i]);
                Assert.That(enqueued, Is.True, diagnostic?.message);
            }
            Assert.That(host.PendingRequestCount, Is.EqualTo(4), "the complete burst is queued before the direct tick");
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.Zero, "queued requests hold no halls");

            scope.Tick();

            Assert.That(host.HasSubmittedRequest, Is.True);
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.EqualTo(2), "the submitted FILL owns Output+temporary");
            SetHostField(host, "testFencePending", false);
            for (int i = 0; i < handles.Length; i++)
            {
                yield return Spec152GpuFixture.WaitForRealGpu(() => handles[i].IsCompleted);
                Assert.That(handles[i].Succeeded, Is.True, handles[i].Diagnostic?.message);
                Assert.That(handles[i].Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                if (delivery != null) scope.Deliveries.Add(delivery);
                Assert.That(delivery, Is.Not.Null);
                yield return Spec152GpuFixture.AssertAllPixels(scope, delivery.Texture, 128, 128, new Vector4(1f, 0f, 0f, 1f));
                delivery.Dispose();
            }
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.Zero);
            Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
            Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
            Assert.That(host.LiveTransientGpuBytes, Is.Zero);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // Q05: a Source256 + Output256 COPY cannot fit alone on the empty 256 grid. Admission must reject it with
        // the exact capacity diagnostic and must leave handle, pending queue, submitted state, and real occupancy clear.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q05_Oversized_IsRejected()
        {
#if UNITY_EDITOR
            Texture2D source = NewSource(scope, 256);
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = BuildSourceOutputStub(source, 256);
            bool accepted = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(accepted, Is.False, diagnostic?.message);
            Assert.That(handle, Is.Null);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
            Assert.That(diagnostic.detail, Does.Contain("stage=Output").And.Contain("256x256"));
            Assert.That(host.PendingRequestCount, Is.Zero);
            Assert.That(host.HasSubmittedRequest, Is.False);
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.Zero);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private static Texture2D NewSource(Spec152GpuScope targetScope, int edge)
        {
            var source = new Texture2D(edge, edge, TextureFormat.RGBAHalf, false, true);
            targetScope.Textures.Add(source);
            return source;
        }

        private static TextureRecipeStub BuildSourceOutputStub(Texture2D source, int outputEdge)
        {
            var document = new MaterialRecipeDocument
            {
                wordSource = "$a $out COPY DROP",
                outputLogicalName = "out",
                outputWidth = outputEdge,
                outputHeight = outputEdge,
            };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
            });
        }

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private static void SetHostField(TextureStackMachineHost host, string name, object value)
            => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);
    }
}
