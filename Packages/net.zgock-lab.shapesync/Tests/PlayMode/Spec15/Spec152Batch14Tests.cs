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
    /// Spec15-2 Phase06-14 integration-test preparation for the temporary pool and fragmentation (P01/P02/P06/P07/P08).
    /// The shared Spec152GpuFixture supplies the host/grid swap, the once-built Update delegate, the real 30-second GPU
    /// waits, the readback conversion, the lease flows, and the Scope-based UnityTearDown cleanup; nothing here duplicates
    /// those helpers. New-semantics integration tests do not run until the 06-26 entry switch, so this batch is verified by
    /// compilation and a static method/attribute check only; execution status stays 未実施（06-26）. No random numbers.
    /// The measured region calls the stored delegate directly and keeps reflection, asserts, and string building outside it.
    /// </summary>
    public sealed class Spec152Batch14Tests
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

        // P01 (T0→T1→out): two simultaneous temporary slots backed by two distinct physical halls. The submitted
        // request is held by the §13.5 fence contact, the pool is inspected, then the contact is released and the real
        // fence finishes the request.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_P01_TemporaryPool_AllocateBeforeFree()
        {
#if UNITY_EDITOR
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = BuildStub("1 0 0 1 FILL $a ADD $out COPY", 128, source);
            SetHostField(host, "testFencePending", true);
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            scope.Tick(); // one measured tick: probe, acquire both temporary halls, and submit
            object r = HostField(host, "submitted");
            Assert.That(r, Is.Not.Null, "the frozen request stays submitted so the pool can be inspected");
            TextureHallAllocation[] pool = (TextureHallAllocation[])FieldOn(r, "temporaryPool");
            Assert.That(pool.Length, Is.EqualTo(2), "the T0 then T1 chain needs two simultaneous temporary slots");
            Assert.That(pool[0].Id, Is.Not.EqualTo(pool[1].Id), "T0 and T1 are backed by distinct physical halls");
            SetHostField(host, "testFencePending", false);
            yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // P02 (10-stage chain, current P02 word verbatim): the physical temporary pool holds the peak of two slots, not
        // one hall per temporary name, and the GPU result matches (a is all (0,0,0,0), the result is red (1,0,0,1)).
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_P02_TemporaryPool_ReusesDeadSlots()
        {
#if UNITY_EDITOR
            Texture2D source = NewSource(scope, 128); // all pixels (0,0,0,0)
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = BuildStub(
                "1 0 0 1 FILL $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $a ADD $out COPY", 128, source);
            SetHostField(host, "testFencePending", true);
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            scope.Tick();
            object r = HostField(host, "submitted");
            Assert.That(r, Is.Not.Null, "the frozen request stays submitted so the pool can be inspected");
            TextureHallAllocation[] pool = (TextureHallAllocation[])FieldOn(r, "temporaryPool");
            Assert.That(pool.Length, Is.EqualTo(2), "the peak of two slots is reserved, not one hall per temporary name");
            Assert.That(pool[0].Id, Is.Not.EqualTo(pool[1].Id), "the two peak slots are backed by distinct physical halls");
            SetHostField(host, "testFencePending", false);
            yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            handle.Result.TryTakeDelivery(out TextureDelivery delivery);
            if (delivery != null) scope.Deliveries.Add(delivery);
            Assert.That(delivery, Is.Not.Null);
            yield return Spec152GpuFixture.AssertAllPixels(scope, delivery.Texture, 128, 128, new Vector4(1f, 0f, 0f, 1f));
            delivery.Dispose();
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // P06 (small RecordExtent, Output=256): a temporary demand keeps the output extents (256x256 = four rooms, not
        // one room); on the live 512 fixture the run occupies Source(4) + Output(4) + temporary(4) = 12 rooms.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_P06_HallPlan_UsesOutputExtent()
        {
#if UNITY_EDITOR
            Texture2D source = NewSource(scope, 256);
            Spec152GpuFixture.CreateHost(scope, 512);
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = BuildStub("1 0 0 1 FILL $a 0 0 128 128 0 0 64 64 PLACE", 256, source);
            SetHostField(host, "testFencePending", true);
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            scope.Tick();
            object r = HostField(host, "submitted");
            Assert.That(r, Is.Not.Null, "the frozen request stays submitted so the halls can be inspected");
            TextureHallAllocation[] pool = (TextureHallAllocation[])FieldOn(r, "temporaryPool");
            Assert.That(pool.Length, Is.EqualTo(1), "the recipe reserves one temporary slot");
            Assert.That(pool[0].RoomWidth * pool[0].RoomHeight, Is.EqualTo(4), "the temporary demand stays 256x256 and is not shrunk to one room for the small record");
            Assert.That(((TextureHallAllocator)HostField(host, "allocator")).OccupiedRoomCount, Is.EqualTo(12), "live 512 fixture: Source(4) + Output(4) + one temporary(4) = 12 rooms");
            SetHostField(host, "testFencePending", false);
            yield return Spec152GpuFixture.WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // P07: a 512 grid has eight free 128x128 holes after the checkerboard release, but no free 256x256
        // rectangle. The Output-only head must remain queued as WaitingForHalls after one measured tick; neither
        // the real allocator nor its version may change and no request may reach submitted.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_P07_Placement_DetectsFragmentation()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 512);
            TextureStackMachineHost host = scope.Host;
            TextureHallAllocator allocator = (TextureHallAllocator)HostField(host, "allocator");
            var reserved = new System.Collections.Generic.List<TextureHallAllocation>();
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation hall), Is.True, "checkerboard fixture reservation");
                    reserved.Add(hall);
                }
            }
            for (int i = reserved.Count - 1; i >= 0; i--)
            {
                TextureHallAllocation hall = reserved[i];
                if ((hall.RoomX + hall.RoomY) % 2 == 1)
                    Assert.That(allocator.TryRelease(hall), Is.True, "checkerboard fixture release");
            }

            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;
            TextureRecipeStub stub = Spec152GpuFixture.CreateOutputOnlyStub(256, 256);
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            Assert.That(host.PendingRequestCount, Is.EqualTo(1), "the head stays in the pending queue");

            scope.Tick();

            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls), "fragmentation blocks the 256x256 head without terminal failure");
            Assert.That(handle.WaitingDiagnostic, Is.Not.Null);
            Assert.That(handle.WaitingDiagnostic.domainCode, Is.EqualTo("WaitingForHalls"));
            Assert.That(host.PendingRequestCount, Is.EqualTo(1), "waiting preserves the queue head");
            Assert.That(host.HasSubmittedRequest, Is.False, "a fragmented probe must not submit GPU work");
            Assert.That(allocator.Version, Is.EqualTo(versionBefore), "live probing does not mutate the allocator version");
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore), "live probing does not mutate allocator occupancy");
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // P08: first prove that an Output-only 256 request fits an empty 256 grid, then retain a real Source128
        // lease at the allocator's top-left position. Reusing that fixed lease for Source128->RESAMPLE->Output256
        // must be rejected synchronously by standalone admission with RequestHallCapacityExceeded; it must not
        // enter a waiting loop. The source lease remains scope-owned for the common fixture cleanup.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_P08_Placement_BorrowedFixedPosition()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;

            SetHostField(host, "testFencePending", true);
            TextureRecipeStub controlStub = Spec152GpuFixture.CreateOutputOnlyStub(256, 256);
            bool controlAccepted = new TextureExecutor(host).TryExecute(controlStub, host.CreateOrigin(), out TextureExecutionHandle controlHandle, out StackMachineDiagnostic controlDiagnostic);
            if (controlHandle != null) scope.Handles.Add(controlHandle);
            Assert.That(controlAccepted, Is.True, controlDiagnostic?.message);
            scope.Tick();
            Assert.That(host.HasSubmittedRequest, Is.True, "Output-only 256 fits the empty 256 grid");
            SetHostField(host, "testFencePending", false);
            yield return Spec152GpuFixture.WaitForRealGpu(() => controlHandle.IsCompleted);
            Assert.That(controlHandle.Succeeded, Is.True, controlHandle.Diagnostic?.message);
            Assert.That(controlHandle.Result.TryTakeDelivery(out TextureDelivery controlDelivery), Is.True);
            if (controlDelivery != null) scope.Deliveries.Add(controlDelivery);
            Assert.That(controlDelivery, Is.Not.Null);
            controlDelivery.Dispose();

            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Spec152GpuFixture.RunCopy128RetainSourceLease(scope, source, lease =>
            {
                TextureRecipeStub candidateStub = BuildStub("$a RESAMPLE $out COPY", 256, source);
                TextureExecutionOptions options = new TextureExecutionOptions(sourceLease: lease);
                bool accepted = new TextureExecutor(host).TryExecute(candidateStub, host.CreateOrigin(), options, out TextureExecutionHandle candidateHandle, out StackMachineDiagnostic candidateDiagnostic);
                if (candidateHandle != null) scope.Handles.Add(candidateHandle);
                Assert.That(accepted, Is.False, candidateDiagnostic?.message);
                Assert.That(candidateDiagnostic, Is.Not.Null);
                Assert.That(candidateDiagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
                Assert.That(host.PendingRequestCount, Is.Zero, "standalone rejection does not enter the queue");
                Assert.That(host.HasSubmittedRequest, Is.False, "standalone rejection submits no GPU work");
                return null;
            });
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private static Texture2D NewSource(Spec152GpuScope targetScope, int edge)
        {
            var source = new Texture2D(edge, edge, TextureFormat.RGBAHalf, false, true);
            targetScope.Textures.Add(source);
            source.SetPixels(new Color[edge * edge]);
            source.Apply(false, false);
            return source;
        }

        private static TextureRecipeStub BuildStub(string wordSource, int outputEdge, Texture2D source)
        {
            var document = new MaterialRecipeDocument
            {
                wordSource = wordSource,
                outputLogicalName = "out",
                outputWidth = outputEdge,
                outputHeight = outputEdge,
            };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var bindings = new[]
            {
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
            };
            return new TextureRecipeStub(document, bindings);
        }

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private static void SetHostField(TextureStackMachineHost host, string name, object value)
            => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private static object FieldOn(object value, string name)
            => value.GetType().GetField(name, Refl).GetValue(value);
    }
}
