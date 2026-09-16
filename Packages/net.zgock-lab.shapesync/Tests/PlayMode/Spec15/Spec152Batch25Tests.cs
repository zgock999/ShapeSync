// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>
    /// Spec15-2 Phase06-25 preparation for retirement transfer and fault ownership (H03-H06). The shared
    /// Spec152GpuFixture owns real Host setup, GPU waits, and teardown. New-queue integration execution is gated
    /// until 06-26; this file is compiled and statically discovered only until the public entry switch.
    /// </summary>
    public sealed class Spec152Batch25Tests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo UpdatePreparedMethod = typeof(TextureStackMachineHost).GetMethod("UpdatePrepared", Refl);
        private static readonly MethodInfo TryInitializePreparedMethod = typeof(TextureStackMachineHost).GetMethod("TryInitializePrepared", Refl);

        private Spec152GpuScope scope;
        private Spec152GpuScope controlScope;
        private TextureHallAllocation controlHall;
        private Scene retirementScene;
        private Scene originalScene;
        private AsyncOperation sceneUnload;
        private GameObject controlRoot;

        [SetUp]
        public void SetUp()
        {
            scope = new Spec152GpuScope();
            controlScope = null;
            controlHall = default;
            retirementScene = default;
            originalScene = SceneManager.GetActiveScene();
            sceneUnload = null;
            controlRoot = null;
        }

        private TextureGpuRetirementTicket crossReviewTicket;
        private RenderTexture crossReviewGrid;

        [UnityTearDown]
        public IEnumerator CleanupFixture()
        {
            if (crossReviewTicket == null && crossReviewGrid != null)
                crossReviewTicket = FindFallbackTicket(crossReviewGrid);
            if (crossReviewTicket != null)
            {
#if UNITY_EDITOR
                crossReviewTicket.testHoldPending = false;
#endif
                yield return WaitForTicketDisposed(crossReviewTicket);
            }
            crossReviewTicket = null;
            crossReviewGrid = null;
            yield return Spec152GpuFixture.Cleanup(scope);
            if (retirementScene.IsValid() && retirementScene.isLoaded)
            {
                if (sceneUnload == null) sceneUnload = SceneManager.UnloadSceneAsync(retirementScene);
                if (sceneUnload != null) yield return Spec152GpuFixture.WaitForRealGpu(() => sceneUnload.isDone);
            }
            if (controlScope != null)
            {
                if (controlHall.IsValid && controlScope.Host != null) controlScope.Host.TryReleaseHall(controlHall);
                yield return Spec152GpuFixture.Cleanup(controlScope);
            }
            Scene activeScene = SceneManager.GetActiveScene();
            if (originalScene.IsValid() && originalScene.isLoaded && activeScene != originalScene)
                SceneManager.SetActiveScene(originalScene);
            controlRoot = null;
            controlScope = null;
            controlHall = default;
            retirementScene = default;
            originalScene = default;
            sceneUnload = null;
            scope = null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator CrossReview002_MissingStandby_RetainsUntilRealFence()
            => CrossReview002_LostPump(0);

        [UnityTest, Timeout(300000)]
        public IEnumerator CrossReview002_DestroyedStandby_RetainsUntilRealFence()
            => CrossReview002_LostPump(1);

        [UnityTest, Timeout(300000)]
        public IEnumerator CrossReview002_DestroyedAcceptedPump_RetainsUntilRealFence()
            => CrossReview002_LostPump(2);

        // Controlled pump-loss injection around a real submission, not a claimed natural Play-exit sequence.
        private IEnumerator CrossReview002_LostPump(int loss)
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureGpuRetirementPump pump = (TextureGpuRetirementPump)scope.Pump;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            bool accepted = new TextureExecutor(host).TryExecute(Spec152GpuFixture.CreateCopy128Stub(source), host.CreateOrigin(),
                out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(accepted, Is.True, diagnostic?.message);
            int completions = 0;
            int destroying = 0;
            handle.Completed += _ => completions++;
            host.Destroying += () => destroying++;
            host.testFencePending = true;
            scope.Tick();
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
            crossReviewGrid = host.Grid;
            if (loss == 0) SetField(host, "retirementPump", null);
            if (loss == 1)
            {
                UnityEngine.Object.Destroy(pump.gameObject);
                yield return WaitForPumpDestroyed(pump);
            }
            UnityEngine.Object.Destroy(host);
            yield return Spec152GpuFixture.WaitForRealGpu(() => host == null);
            if (loss == 2)
            {
                crossReviewTicket = (TextureGpuRetirementTicket)TicketFromPump(pump);
                Assert.That(crossReviewTicket, Is.Not.Null);
                UnityEngine.Object.Destroy(pump.gameObject);
                yield return WaitForPumpDestroyed(pump);
            }
            else crossReviewTicket = FindFallbackTicket(crossReviewGrid);
            Assert.That(crossReviewTicket, Is.Not.Null);
            Assert.That(FindFallbackTicket(crossReviewGrid), Is.SameAs(crossReviewTicket));
            Assert.That(crossReviewGrid.IsCreated(), Is.True);
            Assert.That(GetField(crossReviewTicket, "disposed"), Is.False);
            Assert.That(GetField(crossReviewTicket, "uncertain"), Is.False);
            Assert.That((Texture[])GetField(crossReviewTicket, "sources"), Does.Contain(source));
            var delivery = (RenderTexture)GetField(crossReviewTicket, "delivery");
            Assert.That(delivery != null && delivery.IsCreated(), Is.True);
            Assert.That(handle.IsCompleted, Is.True);
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(destroying, Is.EqualTo(1));
            AssertTicketHasNoScheduler(crossReviewTicket);

            // Suppress only the fallback editor callback: this verifies its actual PlayerLoop driver.
#if UNITY_EDITOR
            var fallbackPoll = (UnityEditor.EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                typeof(UnityEditor.EditorApplication.CallbackFunction),
                typeof(TextureGpuRetirementFallback).GetMethod("Poll", BindingFlags.Static | BindingFlags.NonPublic));
            UnityEditor.EditorApplication.update -= fallbackPoll;
#endif
            try
            {
                crossReviewTicket.testHoldPending = false;
                yield return WaitForTicketDisposed(crossReviewTicket);
                Assert.That(crossReviewGrid == null || !crossReviewGrid.IsCreated(), Is.True);
                Assert.That(delivery == null || !delivery.IsCreated(), Is.True);
                Assert.That(source != null, Is.True, "borrowed sources must not be destroyed");
                Assert.That(FindFallbackTicket(crossReviewGrid), Is.Null);
            }
            finally { TextureGpuRetirementFallback.EnsureInstalled(); }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        private static TextureGpuRetirementTicket FindFallbackTicket(RenderTexture grid)
        {
            var tickets = (System.Collections.IEnumerable)typeof(TextureGpuRetirementFallback)
                .GetField("tickets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (TextureGpuRetirementTicket ticket in tickets)
                if (ReferenceEquals(GetField(ticket, "grid"), grid)) return ticket;
            return null;
        }

        // H03: the Host lives in a dedicated additive scene and is disabled by scene unload while a real submitted
        // fence is held. A separate initialized control Host survives the unload. The retirement ticket owns the raw
        // grid until the real fence passes; only then does the grid release and the pump destroy itself.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H03_Destroy_DefersGpuResources()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            controlScope = new Spec152GpuScope();
            Spec152GpuFixture.CreateHost(controlScope, 256);
            TextureStackMachineHost controlHost = controlScope.Host;
            Assert.That(controlHost.TryReserveHall(128, 128, out controlHall, out StackMachineDiagnostic controlDiagnostic), Is.True, controlDiagnostic?.message);
            RenderTexture controlGrid = controlHost.Grid;
            ulong controlAllocatorVersion = Allocator(controlHost).Version;
            int controlPending = controlHost.PendingRequestCount;
            int controlOccupied = Allocator(controlHost).OccupiedRoomCount;
            Assert.That(controlGrid, Is.Not.Null);
            Assert.That(controlGrid.IsCreated(), Is.True);
            Assert.That(controlHall.IsValid, Is.True);
            Assert.That(controlOccupied, Is.EqualTo(1));

            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            TextureExecutor executor = new TextureExecutor(host);
            bool accepted = executor.TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(accepted, Is.True, diagnostic?.message);
            Assert.That(handle, Is.Not.Null);
            int completedNotifications = 0;
            Action<TextureExecutionHandle> completedObserver = _ => completedNotifications++;
            handle.Completed += completedObserver;
            int destroyingNotifications = 0;
            Action destroyingObserver = () => destroyingNotifications++;
            host.Destroying += destroyingObserver;

            retirementScene = SceneManager.CreateScene("Spec152Batch25RetirementScene");
            SceneManager.MoveGameObjectToScene(scope.Root, retirementScene);
            RenderTexture rawGrid = null;
            host.testFencePending = true;
            try
            {
                scope.Tick();
                Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                rawGrid = host.Grid;
                Assert.That(rawGrid, Is.Not.Null);
                Assert.That(rawGrid.IsCreated(), Is.True);

                sceneUnload = SceneManager.UnloadSceneAsync(retirementScene);
                Assert.That(sceneUnload, Is.Not.Null);
                yield return Spec152GpuFixture.WaitForRealGpu(() => sceneUnload.isDone);

                TextureGpuRetirementPump pump = scope.Pump as TextureGpuRetirementPump;
                Assert.That(pump, Is.Not.Null, "the standby control fixture survives the Host scene unload");
                object ticket = TicketFromPump(pump);
                Assert.That(ticket, Is.Not.Null, "destroy transfers the unpassed submission to one retirement ticket");
                Assert.That(rawGrid.IsCreated(), Is.True, "the ticket prevents an early grid release");
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("HostDisabled"));
                Assert.That(completedNotifications, Is.EqualTo(1));
                Assert.That(destroyingNotifications, Is.EqualTo(1));
                AssertTicketHasNoScheduler(ticket);
                Assert.That((bool)GetField(ticket, "testHoldPending"), Is.True);
                Assert.That(controlHost.Grid, Is.SameAs(controlGrid));
                Assert.That(controlGrid.IsCreated(), Is.True);
                Assert.That(controlHost.PendingRequestCount, Is.EqualTo(controlPending));
                Assert.That(Allocator(controlHost).OccupiedRoomCount, Is.EqualTo(controlOccupied));
                Assert.That(Allocator(controlHost).Version, Is.EqualTo(controlAllocatorVersion));

                SetField(ticket, "testHoldPending", false);
                yield return WaitForTicketDisposed(ticket);

                yield return WaitForPumpDestroyed(pump);
                Assert.That(rawGrid == null || !rawGrid.IsCreated(), Is.True, "the real fence passed before ticket cleanup released the grid");
                Assert.That(controlHost.Grid, Is.SameAs(controlGrid));
                Assert.That(controlGrid.IsCreated(), Is.True);
                Assert.That(controlHost.PendingRequestCount, Is.EqualTo(controlPending));
                Assert.That(Allocator(controlHost).OccupiedRoomCount, Is.EqualTo(controlOccupied));
                Assert.That(Allocator(controlHost).Version, Is.EqualTo(controlAllocatorVersion));
            }
            finally
            {
                if (host != null) host.testFencePending = false;
                handle.Completed -= completedObserver;
                if (host != null) host.Destroying -= destroyingObserver;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // H04: the retirement ticket is a resource-only holder, never a scheduler. Every instance field type is
        // checked directly for forbidden Host/Scene/GameObject/Executor/request/delegate references.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H04_Retirement_HasNoScheduler()
        {
#if UNITY_EDITOR
            var ticket = new TextureGpuRetirementTicket(default, null, null, Array.Empty<Texture>(), false);
            AssertTicketHasNoScheduler(ticket);
            Assert.That(ticket, Is.Not.Null);
#else
            Assert.Ignore("Retirement ticket contract is Editor-only.");
#endif
            yield break;
        }

        // H05 covers both pre-submit failure contacts. Neither contact has attempted GPU submission, so Update's
        // terminal cleanup must return every owned hall/use/delivery and leave no pending accounting or submitted slot.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H05_PreparationFault_NoLeak_BeforeDelivery()
            => Spec152_H05_PreparationFault_NoLeak("BeforeDelivery");

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H05_PreparationFault_NoLeak_BeforeExecute()
            => Spec152_H05_PreparationFault_NoLeak("BeforeExecute");

        private IEnumerator Spec152_H05_PreparationFault_NoLeak(string failurePoint)
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            int ingestBefore = IngestDispatchCount(host);
            host.testFailurePoint = failurePoint == "BeforeDelivery"
                ? TextureStackMachineHost.TestFailurePoint.BeforeDelivery
                : TextureStackMachineHost.TestFailurePoint.BeforeExecute;
            try
            {
                bool accepted = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
                if (handle != null) scope.Handles.Add(handle);
                Assert.That(accepted, Is.True, diagnostic?.message);
                Assert.That(handle, Is.Not.Null);

                scope.Tick();

                Assert.That(host.testFailurePoint, Is.EqualTo(TextureStackMachineHost.TestFailurePoint.None));
                Assert.That(IngestDispatchCount(host), Is.EqualTo(ingestBefore));
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Succeeded, Is.False);
                Assert.That(handle.Diagnostic, Is.Not.Null);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("GpuPreparationFailed"));
                Assert.That(handle.Result, Is.Null);
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(Allocator(host).OccupiedRoomCount, Is.Zero);
                Assert.That(host.LiveTransientGpuBytes, Is.Zero);
                Assert.That(CollectionCount(host, "outstandingDeliveries"), Is.Zero);
            }
            finally
            {
                host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.None;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // H06 parameterizes the two submitted-side failure contacts. AfterExecute uses the real GPU path and must
        // quarantine the in-flight resources. FencePoll uses the same prepared fault transition with an artificial
        // request carrying no GPU resources, so the Uncertain ticket policy is observed without leaking GPU work.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H06_SubmittedFault_IsQuarantined_AfterExecute()
            => Spec152_H06_SubmittedFault_IsQuarantined("AfterExecute");

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H06_SubmittedFault_IsQuarantined_FencePoll()
            => Spec152_H06_SubmittedFault_IsQuarantined("FencePoll");

        private IEnumerator Spec152_H06_SubmittedFault_IsQuarantined(string failurePoint)
        {
#if UNITY_EDITOR
            if (failurePoint == "FencePoll")
            {
                RunFencePollFaultWithoutGpuResources();
                yield break;
            }

            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
            TextureExecutor executor = new TextureExecutor(host);
            TextureExecutionOriginKey origin = host.CreateOrigin();
            bool faultAccepted = executor.TryExecute(stub, origin, out TextureExecutionHandle faultHandle, out StackMachineDiagnostic diagnostic);
            if (faultHandle != null) scope.Handles.Add(faultHandle);
            Assert.That(faultAccepted, Is.True, diagnostic?.message);
            TextureExecutionOriginKey pendingOrigin = host.CreateOrigin();
            bool pendingAccepted = executor.TryExecute(stub, pendingOrigin, out TextureExecutionHandle pendingHandle, out diagnostic);
            if (pendingHandle != null) scope.Handles.Add(pendingHandle);
            Assert.That(pendingAccepted, Is.True, diagnostic?.message);
            host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.AfterExecute;
            host.testFencePending = true;
            try
            {
                scope.Tick();

                Assert.That(faultHandle.IsCompleted, Is.True);
                Assert.That(faultHandle.Succeeded, Is.False);
                Assert.That(faultHandle.Diagnostic, Is.Not.Null);
                Assert.That(faultHandle.Diagnostic.domainCode, Is.EqualTo("GpuSubmissionFailed"));
                Assert.That(pendingHandle.IsCompleted, Is.True);
                Assert.That(pendingHandle.Succeeded, Is.False);
                Assert.That(pendingHandle.Diagnostic.domainCode, Is.EqualTo("GpuSubmissionFailed"));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(GetField(host, "faulted"), Is.True);

                bool rejected = executor.TryExecute(stub, origin, out TextureExecutionHandle rejectedHandle, out StackMachineDiagnostic rejectedDiagnostic);
                if (rejectedHandle != null) scope.Handles.Add(rejectedHandle);
                Assert.That(rejected, Is.False);
                Assert.That(rejectedHandle, Is.Null);
                Assert.That(rejectedDiagnostic, Is.Not.Null);
                Assert.That(rejectedDiagnostic.domainCode, Is.EqualTo("HostFaulted"));

                object ticket = TicketFromPump(scope.Pump as TextureGpuRetirementPump);
                Assert.That(ticket, Is.Not.Null);
                TextureGpuRetirementPump pump = scope.Pump as TextureGpuRetirementPump;
                RenderTexture rawGrid = (RenderTexture)GetField(ticket, "grid");
                Assert.That(rawGrid, Is.Not.Null);
                Assert.That(rawGrid.IsCreated(), Is.True, "post-Execute failure keeps the unpassed grid in the ticket");
                AssertTicketHasNoScheduler(ticket);
                Assert.That((bool)GetField(ticket, "testHoldPending"), Is.True);

                object[] initArgs = { null };
                Assert.That((bool)TryInitializePreparedMethod.Invoke(host, initArgs), Is.False);
                Assert.That(((StackMachineDiagnostic)initArgs[0]).domainCode, Is.EqualTo("HostFaulted"));

                SetField(ticket, "testHoldPending", false);
                host.testFencePending = false;
                yield return WaitForTicketDisposed(ticket);
                yield return WaitForPumpDestroyed(pump);
                Assert.That(rawGrid == null || !rawGrid.IsCreated(), Is.True);
            }
            finally
            {
                host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.None;
                host.testFencePending = false;
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // H06's poll-failure companion is a CPU-only ticket contract. It checks Uncertain's one-shot report, no
        // second fence read, and resource reference retention without intentionally quarantining a real GPU object.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_H06_UncertainTicket_DoesNotRepoll()
        {
#if UNITY_EDITOR
            var sources = new Texture[] { null };
            var ticket = new TextureGpuRetirementTicket(default, null, null, sources, false);
            SetField(ticket, "testFailPollOnce", true);
            LogAssert.Expect(LogType.Error, new Regex("GpuRetirementUncertain"));
            Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain));
            Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain));
            Assert.That((int)GetField(ticket, "testFenceReadCount"), Is.EqualTo(1));
            Assert.That((bool)GetField(ticket, "testFailPollOnce"), Is.False);
            Assert.That((bool)GetField(ticket, "uncertain"), Is.True);
            Assert.That((bool)GetField(ticket, "disposed"), Is.False);
            Assert.That(GetField(ticket, "sources"), Is.SameAs(sources));
#else
            Assert.Ignore("Retirement ticket contract is Editor-only.");
#endif
            yield break;
        }

        private void RunFencePollFaultWithoutGpuResources()
        {
#if UNITY_EDITOR
            controlRoot = new GameObject("Spec152Batch25 FencePoll ControlFixture");
            TextureStackMachineHost host = controlRoot.AddComponent<TextureStackMachineHost>();
            TextureGpuRetirementPump pump = controlRoot.AddComponent<TextureGpuRetirementPump>();
            pump.enabled = false;
            SetField(host, "retirementPump", pump);
            SetField(host, "initialized", true);
            SetField(host, "acceptingRequests", true);

            TextureExecutionHandle submittedHandle = new TextureExecutionHandle();
            TextureExecutionHandle pendingHandle = new TextureExecutionHandle();
            var artificialHandles = new List<TextureExecutionHandle> { submittedHandle, pendingHandle };
            object submitted = NewRequest(submittedHandle, default(TextureExecutionOriginKey), 0L);
            object pending = NewRequest(pendingHandle, default(TextureExecutionOriginKey), 0L);
            EnqueuePending(host, pending);
            SetField(host, "submitted", submitted);
            host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.FencePoll;
            LogAssert.Expect(LogType.Error, new Regex("GpuRetirementUncertain"));
            try
            {
                UpdatePreparedMethod.Invoke(host, null);

                Assert.That(submittedHandle.IsCompleted, Is.True);
                Assert.That(submittedHandle.Succeeded, Is.False);
                Assert.That(submittedHandle.Diagnostic.domainCode, Is.EqualTo("GpuFencePollFailed"));
                Assert.That(pendingHandle.IsCompleted, Is.True);
                Assert.That(pendingHandle.Diagnostic.domainCode, Is.EqualTo("GpuFencePollFailed"));
                Assert.That((bool)GetField(host, "faulted"), Is.True);
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(CollectionCount(host, "pendingByOrigin"), Is.Zero);
                Assert.That(LongField(host, "pendingDeliveryBytes"), Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
                object ticket = TicketFromPump(pump);
                Assert.That(ticket, Is.Not.Null, "the fault owns one Uncertain ticket even when no raw GPU resource was attached");
                Assert.That((bool)GetField(ticket, "uncertain"), Is.True);
                AssertTicketHasNoScheduler(ticket);

                object[] initArgs = { null };
                Assert.That((bool)TryInitializePreparedMethod.Invoke(host, initArgs), Is.False);
                Assert.That(((StackMachineDiagnostic)initArgs[0]).domainCode, Is.EqualTo("HostFaulted"));
            }
            finally
            {
                host.testFailurePoint = TextureStackMachineHost.TestFailurePoint.None;
                ClearArtificialQueueState(host);
                foreach (TextureExecutionHandle handle in artificialHandles) DisposeArtificialHandle(handle);
                object artificialTicket = TicketFromPump(pump);
                UnityEngine.Object.DestroyImmediate(controlRoot);
                controlRoot = null;
                // This fixture never submits or attaches raw GPU resources. Remove only its artificial ticket
                // after OnDestroy transfers it; real Pending/Uncertain retirement tickets are never cleared here.
                if (artificialTicket != null)
                {
                    Assert.That(GetField(artificialTicket, "grid"), Is.Null);
                    Assert.That(GetField(artificialTicket, "delivery"), Is.Null);
                    Assert.That(((Texture[])GetField(artificialTicket, "sources")).Length, Is.Zero);
                    var retained = (System.Collections.IList)typeof(TextureGpuRetirementFallback)
                        .GetField("tickets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    retained.Remove(artificialTicket);
                }
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
        }

        private static void ClearArtificialQueueState(TextureStackMachineHost host)
        {
            GetField(host, "pending").GetType().GetMethod("Clear").Invoke(GetField(host, "pending"), null);
            GetField(host, "pendingByOrigin").GetType().GetMethod("Clear").Invoke(GetField(host, "pendingByOrigin"), null);
            SetField(host, "pendingDeliveryBytes", 0L);
            SetField(host, "submitted", null);
        }

        private static void DisposeArtificialHandle(TextureExecutionHandle handle)
        {
            if (handle == null) return;
            if (!handle.IsCompleted)
                handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "FixtureCleanup", "Artificial FencePoll test cleanup finalized the handle."), null);
            handle.Dispose();
        }

        private static object NewRequest(TextureExecutionHandle handle, TextureExecutionOriginKey origin, long reserved)
        {
            object request = Activator.CreateInstance(RequestType, nonPublic: true);
            RequestType.GetField("handle", Refl).SetValue(request, handle);
            RequestType.GetField("origin", Refl).SetValue(request, origin);
            RequestType.GetField("reservedDeliveryBytes", Refl).SetValue(request, reserved);
            return request;
        }

        private static void EnqueuePending(TextureStackMachineHost host, object request)
        {
            object queue = GetField(host, "pending");
            queue.GetType().GetMethod("Enqueue").Invoke(queue, new[] { request });
        }

        private static IEnumerator WaitForTicketDisposed(object ticket)
            => Spec152GpuFixture.WaitForRealGpu(() => (bool)GetField(ticket, "disposed"));

        private static IEnumerator WaitForPumpDestroyed(TextureGpuRetirementPump pump)
            => Spec152GpuFixture.WaitForRealGpu(() => pump == null);

        private static object TicketFromPump(TextureGpuRetirementPump pump)
            => pump == null ? null : typeof(TextureGpuRetirementPump).GetField("ticket", Refl).GetValue(pump);

        private static void AssertTicketHasNoScheduler(object ticket)
        {
            Assert.That(ticket, Is.Not.Null);
            int forbidden = 0;
            foreach (FieldInfo field in typeof(TextureGpuRetirementTicket).GetFields(Refl))
            {
                Type type = field.FieldType;
                bool isForbidden = typeof(TextureStackMachineHost).IsAssignableFrom(type)
                    || type == typeof(Scene)
                    || typeof(GameObject).IsAssignableFrom(type)
                    || typeof(TextureExecutor).IsAssignableFrom(type)
                    || typeof(Delegate).IsAssignableFrom(type)
                    || (RequestType != null && RequestType.IsAssignableFrom(type));
                if (isForbidden) forbidden++;
            }
            Assert.That(forbidden, Is.Zero, "the retirement ticket must hold no Host, Scene, GameObject, Executor, request, or delegate reference");
        }

        private static object GetField(object target, string name)
            => target.GetType().GetField(name, Refl).GetValue(target);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, Refl).SetValue(target, value);

        private static int CollectionCount(TextureStackMachineHost host, string name)
        {
            object collection = GetField(host, name);
            return (int)collection.GetType().GetProperty("Count").GetValue(collection);
        }

        private static long LongField(TextureStackMachineHost host, string name)
            => (long)GetField(host, name);

        private static int IngestDispatchCount(TextureStackMachineHost host)
            => host.IngestDispatchCount;

        private static TextureHallAllocator Allocator(TextureStackMachineHost host)
            => (TextureHallAllocator)GetField(host, "allocator");
    }
}
