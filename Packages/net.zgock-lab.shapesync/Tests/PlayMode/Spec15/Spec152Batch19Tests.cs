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
    /// Spec15-2 Phase06-19 preparation for delivery-budget accounting (B01-B04). The shared
    /// Spec152GpuFixture owns host setup, real GPU waits, lease creation, and teardown. New-queue
    /// integration execution is gated until 06-26; this file is compiled and statically inspected only.
    /// </summary>
    public sealed class Spec152Batch19Tests
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

        // B01: G+C budget, no live delivery, one old pending reservation C, and a same-origin candidate C.
        // Replacement credit is exactly the old pending reservation. The candidate is accepted; the old request
        // is terminalized once as RequestCoalesced and the pending total remains C rather than 2C.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_B01_Budget_ReplacementCredit()
        {
#if UNITY_EDITOR
            const int gridEdge = 512;
            const int outputEdge = 128;
            long gridBytes = GridBytes(gridEdge);
            long deliveryBytes = DeliveryBytes(outputEdge, outputEdge);
            Spec152GpuFixture.CreateHost(scope, gridEdge);
            TextureStackMachineHost host = scope.Host;
            SetHostField(host, "capability", new TextureGpuCapability(SystemInfo.maxTextureSize, gridBytes + deliveryBytes, gridEdge));
            host.testFencePending = true;
            try
            {
                Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
                TextureExecutionOriginKey origin = host.CreateOrigin();
                TextureExecutionPlan plan = CreateCopyPlan(source, outputEdge);
                Assert.That(Enqueue(host, plan, origin, null, out TextureExecutionHandle oldHandle), Is.True);
                if (oldHandle != null) scope.Handles.Add(oldHandle);
                int notifications = 0;
                oldHandle.Completed += _ => notifications++;

                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.EqualTo(deliveryBytes));
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);

                Assert.That(Enqueue(host, plan, origin, null, out TextureExecutionHandle candidateHandle), Is.True);
                if (candidateHandle != null) scope.Handles.Add(candidateHandle);

                Assert.That(oldHandle.IsCompleted, Is.True);
                Assert.That(oldHandle.Succeeded, Is.False);
                Assert.That(oldHandle.Diagnostic, Is.Not.Null);
                Assert.That(oldHandle.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
                Assert.That(oldHandle.Result, Is.Null);
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(candidateHandle.IsCompleted, Is.False);
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.EqualTo(1));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.EqualTo(deliveryBytes));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Not.LessThan(0));

                candidateHandle.Dispose();
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Not.LessThan(0));
            }
            finally
            {
                host.testFencePending = false;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // B02: a submitted request holds one live C delivery while the fence is pending. There is no pending
        // replacement credit, so a same-origin C candidate is rejected by G+C and the submitted request is not
        // marked stale. The submitted Result/delivery is intentionally not taken in this test.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_B02_Budget_NoSubmittedCredit()
        {
#if UNITY_EDITOR
            const int gridEdge = 512;
            const int outputEdge = 128;
            long gridBytes = GridBytes(gridEdge);
            long deliveryBytes = DeliveryBytes(outputEdge, outputEdge);
            Spec152GpuFixture.CreateHost(scope, gridEdge);
            TextureStackMachineHost host = scope.Host;
            SetHostField(host, "capability", new TextureGpuCapability(SystemInfo.maxTextureSize, gridBytes + deliveryBytes, gridEdge));
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            TextureExecutionOriginKey origin = host.CreateOrigin();
            host.testFencePending = true;
            try
            {
                Assert.That(Enqueue(host, CreateCopyPlan(source, outputEdge), origin, null, out TextureExecutionHandle submittedHandle), Is.True);
                if (submittedHandle != null) scope.Handles.Add(submittedHandle);
                scope.Tick();
                Assert.That(submittedHandle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(host.LiveTransientGpuBytes, Is.EqualTo(deliveryBytes));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(SubmittedStale(host), Is.False);

                bool accepted = new TextureExecutor(host).TryExecute(
                    CreateOutputOnlyPlan(outputEdge, outputEdge), origin, null,
                    out TextureExecutionHandle rejectedHandle, out StackMachineDiagnostic rejectedDiagnostic);
                if (rejectedHandle != null) scope.Handles.Add(rejectedHandle);
                Assert.That(accepted, Is.False);
                Assert.That(rejectedHandle, Is.Null);
                Assert.That(rejectedDiagnostic, Is.Not.Null);
                Assert.That(rejectedDiagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(SubmittedStale(host), Is.False, "submitted work receives no replacement credit and is not made stale on rejection");
                Assert.That(host.LiveTransientGpuBytes, Is.EqualTo(deliveryBytes));

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => !host.HasSubmittedRequest);
                Assert.That(submittedHandle.IsCompleted, Is.True);
                Assert.That(submittedHandle.Succeeded, Is.True, submittedHandle.Diagnostic?.message);
                Assert.That(submittedHandle.Result, Is.Not.Null, "the submitted delivery remains unclaimed for cleanup");
                Assert.That(host.LiveTransientGpuBytes, Is.EqualTo(deliveryBytes));
            }
            finally
            {
                host.testFencePending = false;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // B03: a retained output lease is caller-owned. Enqueue still reserves C conservatively, submit resolves
        // the reservation to zero, and no transient Delivery is created. The caller lease use is held through the
        // real fence and then returned without a negative pending or live total.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_B03_Budget_RetainedOutputPolicy()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            yield return Spec152GpuFixture.RunFill128RetainOutputLease(scope, lease => B03RetainedOutputCase(host, lease));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator B03RetainedOutputCase(TextureStackMachineHost host, TextureOutputLease outputLease)
        {
            const int outputEdge = 128;
            long deliveryBytes = DeliveryBytes(outputEdge, outputEdge);
            host.testFencePending = true;
            try
            {
                TextureExecutionOriginKey origin = host.CreateOrigin();
                TextureExecutionOptions options = new TextureExecutionOptions(outputLease: outputLease, retainOutputLease: true);
                Assert.That(Enqueue(host, CreateOutputOnlyPlan(outputEdge, outputEdge), origin, options, out TextureExecutionHandle handle), Is.True);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.EqualTo(deliveryBytes), "retained Output still uses the conservative enqueue reservation");
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));

                scope.Tick();
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Not.LessThan(0));
                Assert.That(host.LiveTransientGpuBytes, Is.Zero, "retainOutputLease creates no transient delivery");
                Assert.That(OutputUseCount(outputLease), Is.EqualTo(1), "the caller Output lease use spans the submitted fence");

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => !host.HasSubmittedRequest);
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
                Assert.That(handle.Result, Is.Not.Null);
                Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery unexpectedDelivery), Is.False);
                if (unexpectedDelivery != null) scope.Deliveries.Add(unexpectedDelivery);
                Assert.That(unexpectedDelivery, Is.Null);
                Assert.That(OutputUseCount(outputLease), Is.Zero);
                Assert.That(outputLease.IsValid, Is.True);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // B04: keep a completed delivery unclaimed to model the material escrow boundary. Before handoff it costs
        // exactly C; TryTakeDelivery removes that transient charge and moves one item to handedOffDeliveries; the
        // final Dispose clears the handed-off set and leaves the live total at zero.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_B04_Budget_MaterialEscrow()
        {
#if UNITY_EDITOR
            const int outputEdge = 128;
            long deliveryBytes = DeliveryBytes(outputEdge, outputEdge);
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            host.testFencePending = true;
            try
            {
                TextureExecutionHandle handle;
                Assert.That(Enqueue(host, CreateOutputOnlyPlan(outputEdge, outputEdge), host.CreateOrigin(), null, out handle), Is.True);
                if (handle != null) scope.Handles.Add(handle);
                scope.Tick();
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);
                Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
                Assert.That(handle.Result, Is.Not.Null);
                Assert.That(host.LiveTransientGpuBytes, Is.EqualTo(deliveryBytes));
                Assert.That(CollectionCount(host, "outstandingDeliveries"), Is.EqualTo(1));
                Assert.That(host.HandedOffDeliveryCount, Is.Zero);

                Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                if (delivery != null) scope.Deliveries.Add(delivery);
                Assert.That(delivery, Is.Not.Null);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
                Assert.That(CollectionCount(host, "outstandingDeliveries"), Is.Zero);
                Assert.That(host.HandedOffDeliveryCount, Is.EqualTo(1));

                delivery.Dispose();
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
                Assert.That(CollectionCount(host, "outstandingDeliveries"), Is.Zero);
                Assert.That(host.HandedOffDeliveryCount, Is.Zero);
            }
            finally
            {
                host.testFencePending = false;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
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

        private static long GridBytes(int gridEdge)
            => checked((long)gridEdge * gridEdge * TextureGpuCapabilityProbe.BytesPerPixel);

        private static long DeliveryBytes(int width, int height)
            => checked((long)width * height * TextureGpuCapabilityProbe.BytesPerPixel);

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private static void SetHostField(TextureStackMachineHost host, string name, object value)
            => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private static long LongField(TextureStackMachineHost host, string name)
            => (long)HostField(host, name);

        private static int CollectionCount(TextureStackMachineHost host, string name)
            => (int)HostField(host, name).GetType().GetProperty("Count").GetValue(HostField(host, name));

        private static bool SubmittedStale(TextureStackMachineHost host)
        {
            object submitted = HostField(host, "submitted");
            return (bool)submitted.GetType().GetField("stale", Refl).GetValue(submitted);
        }

        private static int OutputUseCount(TextureOutputLease lease)
            => (int)typeof(TextureOutputLease).GetField("useCount", Refl).GetValue(lease);
    }
}
