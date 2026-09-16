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
    /// Spec15-2 Phase06-18 preparation for coalescing, waiting cancellation, and cancellation priority
    /// (Q08/Q09/Q10/Q12). The shared Spec152GpuFixture owns host setup, waits, and cleanup. New-queue
    /// integration execution is gated until 06-26; this file is compiled and statically inspected only.
    /// </summary>
    public sealed class Spec152Batch18Tests
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

        // Q08 uses three independent invalid-candidate cases. In each case A1 and B are already queued, and the
        // candidate shares A1's origin. The rejection must leave the original queue order and A1 terminal state
        // untouched. The budget case uses a 512 grid so Output-only512 fits the layout but exceeds the fixed budget.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q08_Coalesce_InvalidCandidateKeepsOld_InvalidSource() => Spec152_Q08_Coalesce_InvalidCandidateKeepsOld("invalid_source");

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q08_Coalesce_InvalidCandidateKeepsOld_Capacity() => Spec152_Q08_Coalesce_InvalidCandidateKeepsOld("capacity");

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q08_Coalesce_InvalidCandidateKeepsOld_Budget() => Spec152_Q08_Coalesce_InvalidCandidateKeepsOld("budget");

        private IEnumerator Spec152_Q08_Coalesce_InvalidCandidateKeepsOld(string candidateKind)
        {
#if UNITY_EDITOR
            bool budgetCase = candidateKind == "budget";
            Spec152GpuFixture.CreateHost(scope, budgetCase ? 512 : 256);
            TextureStackMachineHost host = scope.Host;
            host.testFencePending = true;
            try
            {
                Texture2D sourceA = Spec152GpuFixture.CreateCopy128Source(scope);
                Texture2D sourceB = Spec152GpuFixture.CreateCopy128Source(scope);
                TextureExecutionOriginKey originA = host.CreateOrigin();
                TextureExecutionOriginKey originB = host.CreateOrigin();
                TextureExecutionPlan planA = CreateCopyPlan(sourceA, 128);
                TextureExecutionPlan planB = CreateCopyPlan(sourceB, 128);

                Assert.That(Enqueue(host, planA, originA, out TextureExecutionHandle a1), Is.True);
                if (a1 != null) scope.Handles.Add(a1);
                Assert.That(Enqueue(host, planB, originB, out TextureExecutionHandle b), Is.True);
                if (b != null) scope.Handles.Add(b);
                Assert.That(PendingHandles(host), Is.EqualTo(new[] { a1, b }));
                Assert.That(a1.IsCompleted, Is.False);
                int pendingBefore = host.PendingRequestCount;
                int originMapBefore = CollectionCount(host, "pendingByOrigin");
                long budgetBefore = LongField(host, "pendingDeliveryBytes");

                TextureExecutionHandle rejectedHandle;
                StackMachineDiagnostic rejectedDiagnostic;
                bool accepted;
                if (candidateKind == "invalid_source")
                {
                    accepted = new TextureExecutor(host).TryExecute(InvalidSourceStub(), originA, out rejectedHandle, out rejectedDiagnostic);
                    Assert.That(rejectedDiagnostic, Is.Not.Null);
                    Assert.That(rejectedDiagnostic.domainCode, Is.EqualTo("SourceTextureRequired"));
                }
                else if (candidateKind == "capacity")
                {
                    Texture2D oversizedSource = NewSource(scope, 256);
                    accepted = TryExecute(host, CreateCopyPlan(oversizedSource, 256), originA, out rejectedHandle, out rejectedDiagnostic);
                    Assert.That(rejectedDiagnostic, Is.Not.Null);
                    Assert.That(rejectedDiagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
                }
                else
                {
                    long gridBytes = 512L * 512L * TextureGpuCapabilityProbe.BytesPerPixel;
                    long copyBytes = 128L * 128L * TextureGpuCapabilityProbe.BytesPerPixel;
                    SetHostField(host, "capability", new TextureGpuCapability(SystemInfo.maxTextureSize, gridBytes + copyBytes * 2L, 512));
                    accepted = TryExecute(host, CreateOutputOnlyPlan(512, 512), originA, out rejectedHandle, out rejectedDiagnostic);
                    Assert.That(rejectedDiagnostic, Is.Not.Null);
                    Assert.That(rejectedDiagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
                }

                if (rejectedHandle != null) scope.Handles.Add(rejectedHandle);
                Assert.That(accepted, Is.False);
                Assert.That(rejectedHandle, Is.Null);
                Assert.That(host.PendingRequestCount, Is.EqualTo(pendingBefore));
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.EqualTo(originMapBefore));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.EqualTo(budgetBefore));
                Assert.That(PendingHandles(host), Is.EqualTo(new[] { a1, b }), "the rejected candidate never changes FIFO order");
                Assert.That(a1.IsCompleted, Is.False);
                Assert.That(a1.Diagnostic, Is.Null);
                Assert.That(host.HasSubmittedRequest, Is.False);
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

        // A submitted A0 is also covered: an invalid same-origin candidate fails before coalesce commit and must
        // not mark the submitted request stale. The real fence is released only after this observation.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q08_InvalidCandidate_DoesNotMarkSubmittedStale()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            host.testFencePending = true;
            try
            {
                TextureExecutionOriginKey origin = host.CreateOrigin();
                Assert.That(Enqueue(host, CreateCopyPlan(source, 128), origin, out TextureExecutionHandle a0), Is.True);
                if (a0 != null) scope.Handles.Add(a0);
                scope.Tick();
                Assert.That(host.HasSubmittedRequest, Is.True);
                Assert.That(a0.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                Assert.That(SubmittedStale(host), Is.False);

                bool accepted = new TextureExecutor(host).TryExecute(InvalidSourceStub(), origin, out TextureExecutionHandle rejected, out StackMachineDiagnostic diagnostic);
                if (rejected != null) scope.Handles.Add(rejected);
                Assert.That(accepted, Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceTextureRequired"));
                Assert.That(SubmittedStale(host), Is.False, "invalid candidate did not stale submitted A0");
                Assert.That(a0.Status, Is.EqualTo(TextureExecutionStatus.Submitted));

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => !host.HasSubmittedRequest);
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

        // Q09: enqueue [A1,B], then a valid A2 with A1's origin. Coalescing terminalizes A1 once and appends A2,
        // producing [B,A2] rather than overwriting A1's FIFO position.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q09_Coalesce_ValidCandidateAppends()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            host.testFencePending = true;
            try
            {
                Texture2D sourceA1 = Spec152GpuFixture.CreateCopy128Source(scope);
                Texture2D sourceB = Spec152GpuFixture.CreateCopy128Source(scope);
                Texture2D sourceA2 = Spec152GpuFixture.CreateCopy128Source(scope);
                TextureExecutionOriginKey originA = host.CreateOrigin();
                TextureExecutionOriginKey originB = host.CreateOrigin();
                Assert.That(Enqueue(host, CreateCopyPlan(sourceA1, 128), originA, out TextureExecutionHandle a1), Is.True);
                if (a1 != null) scope.Handles.Add(a1);
                Assert.That(Enqueue(host, CreateCopyPlan(sourceB, 128), originB, out TextureExecutionHandle b), Is.True);
                if (b != null) scope.Handles.Add(b);
                int a1Notifications = 0;
                a1.Completed += _ => a1Notifications++;

                Assert.That(Enqueue(host, CreateCopyPlan(sourceA2, 128), originA, out TextureExecutionHandle a2), Is.True);
                if (a2 != null) scope.Handles.Add(a2);

                Assert.That(a1.IsCompleted, Is.True);
                Assert.That(a1.Succeeded, Is.False);
                Assert.That(a1.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
                Assert.That(a1Notifications, Is.EqualTo(1));
                Assert.That(a2.IsCompleted, Is.False);
                Assert.That(b.IsCompleted, Is.False);
                Assert.That(PendingHandles(host), Is.EqualTo(new[] { b, a2 }), "valid A2 appends after B");
                Assert.That(host.PendingRequestCount, Is.EqualTo(2));
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.EqualTo(2));
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

        // Q10 starts in the fixed Q03 waiting state. Disposing the waiting handle twice terminalizes/notifies once,
        // removes both queue and origin map entries, and leaves the external four-room hall untouched.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q10_CancelWaiting_ReleasesBudget()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureHallAllocation externalHall = default;
            host.testFencePending = true;
            try
            {
                Assert.That(host.TryReserveHall(256, 256, out externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);
                TextureExecutionOriginKey origin = host.CreateOrigin();
                Assert.That(Enqueue(host, CreateOutputOnlyPlan(256, 256), origin, out TextureExecutionHandle handle), Is.True);
                if (handle != null) scope.Handles.Add(handle);
                int notifications = 0;
                handle.Completed += _ => notifications++;

                scope.Tick();
                TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
                Assert.That(handle.IsCompleted, Is.False);
                Assert.That(PendingHandles(host), Is.EqualTo(new[] { handle }));
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(4));
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.GreaterThan(0));

                handle.Dispose();
                handle.Dispose();

                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(4), "external occupancy is caller-owned and unchanged");
            }
            finally
            {
                host.testFencePending = false;
                if (externalHall.IsValid) host.TryReleaseHall(externalHall);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // Q12 fixes the order: submitted A1, accepted same-origin A2, A1.Dispose, A2.Dispose. A1's stale mark
        // cannot resurrect success after cancellation; both handles notify once and have no Result after the real
        // submitted fence cleanup.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q12_Stale_CancelPriority()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            host.testFencePending = true;
            try
            {
                TextureExecutionOriginKey origin = host.CreateOrigin();
                Assert.That(Enqueue(host, CreateCopyPlan(source, 128), origin, out TextureExecutionHandle a1), Is.True);
                if (a1 != null) scope.Handles.Add(a1);
                int a1Notifications = 0;
                a1.Completed += _ => a1Notifications++;
                scope.Tick();
                Assert.That(a1.Status, Is.EqualTo(TextureExecutionStatus.Submitted));

                Assert.That(Enqueue(host, CreateCopyPlan(source, 128), origin, out TextureExecutionHandle a2), Is.True);
                if (a2 != null) scope.Handles.Add(a2);
                int a2Notifications = 0;
                a2.Completed += _ => a2Notifications++;
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));

                a1.Dispose();
                a2.Dispose();

                Assert.That(a1.IsCompleted, Is.True);
                Assert.That(a1.Succeeded, Is.False);
                Assert.That(a1.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(a1.Result, Is.Null);
                Assert.That(a1Notifications, Is.EqualTo(1));
                Assert.That(a2.IsCompleted, Is.True);
                Assert.That(a2.Succeeded, Is.False);
                Assert.That(a2.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(a2.Result, Is.Null);
                Assert.That(a2Notifications, Is.EqualTo(1));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => !host.HasSubmittedRequest);
                Assert.That(a1.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"), "stale never replaces cancellation");
                Assert.That(a1.Result, Is.Null);
                Assert.That(a2.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(a2.Result, Is.Null);
                Assert.That(a1Notifications, Is.EqualTo(1));
                Assert.That(a2Notifications, Is.EqualTo(1));
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
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

        private bool Enqueue(TextureStackMachineHost host, TextureExecutionPlan plan, TextureExecutionOriginKey origin, out TextureExecutionHandle handle)
        {
            bool accepted = new TextureExecutor(host).TryExecute(plan, origin, null, out handle, out StackMachineDiagnostic diagnostic);
            if (!accepted) Assert.Fail(diagnostic?.message);
            return accepted;
        }

        private static bool TryExecute(TextureStackMachineHost host, TextureExecutionPlan plan, TextureExecutionOriginKey origin, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
            => new TextureExecutor(host).TryExecute(plan, origin, null, out handle, out diagnostic);

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

        private static TextureRecipeStub InvalidSourceStub()
        {
            var document = new MaterialRecipeDocument { wordSource = "$a $out COPY DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = null },
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
            });
        }

        private static Texture2D NewSource(Spec152GpuScope targetScope, int edge)
        {
            var source = new Texture2D(edge, edge, TextureFormat.RGBAHalf, false, true);
            targetScope.Textures.Add(source);
            return source;
        }

        private static List<TextureExecutionHandle> PendingHandles(TextureStackMachineHost host)
        {
            var result = new List<TextureExecutionHandle>();
            foreach (object request in (IEnumerable)HostField(host, "pending"))
                result.Add((TextureExecutionHandle)request.GetType().GetField("handle", Refl).GetValue(request));
            return result;
        }

        private static int CollectionCount(TextureStackMachineHost host, string name)
            => (int)HostField(host, name).GetType().GetProperty("Count").GetValue(HostField(host, name));

        private static long LongField(TextureStackMachineHost host, string name)
            => (long)HostField(host, name);

        private static bool SubmittedStale(TextureStackMachineHost host)
        {
            object submitted = HostField(host, "submitted");
            return (bool)submitted.GetType().GetField("stale", Refl).GetValue(submitted);
        }

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private static void SetHostField(TextureStackMachineHost host, string name, object value)
            => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);
    }
}
