// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-6 CPU coverage for pending accounting and prepared-path cancellation. Fixtures build the
    /// private QueuedRequest via reflection and wire pending/pendingByOrigin/pendingDeliveryBytes/submitted with
    /// reflection; nothing is submitted to the GPU. Cancel searches by handle reference. Fixture-owned leases are
    /// disposed last and every artificial request leaves the queue, map, and submitted slot empty in finally.
    /// </summary>
    public sealed class Spec152PreparedQueueTests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo DetachMethod = typeof(TextureStackMachineHost).GetMethod("DetachPending", Refl);
        private static readonly MethodInfo CancelMethod = typeof(TextureStackMachineHost).GetMethod("CancelPrepared", Refl);

        private GameObject root;
        private TextureStackMachineHost host;
        private TextureHallAllocator allocator;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Queue Host");
            host = root.AddComponent<TextureStackMachineHost>();
            allocator = new TextureHallAllocator(512);
            SetHostField("allocator", allocator);
        }

        [TearDown]
        public void TearDown()
        {
            ClearPendingQueue();
            PendingMap().Clear();
            SetHostField("pendingDeliveryBytes", 0L);
            SetHostField("submitted", null);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        // CR004: internal fault injection, not a claim of publicly reachable duplicate cancellation.
        [TestCase(false)]
        [TestCase(true)]
        public void CrossReview004_AlreadyTerminalCancel_DoesNotEnqueue(bool inSubmitted)
        {
            var handle = NewHandle();
            var firstReason = StackMachineDiagnostic.CreateDomain("texture", "HostDisabled", "first reason");
            Assert.That(handle.TrySetTerminalState(false, firstReason, null), Is.True);
            object request = NewRequest(handle, Origin(1), inSubmitted ? 0L : 1000L);
            if (inSubmitted) SetHostField("submitted", request);
            else
            {
                EnqueuePending(request);
                PendingMap().Add(Origin(1), request);
                SetHostField("pendingDeliveryBytes", 1000L);
            }
            SetHostField("drainingNotifications", true); // preserve queue contents for direct observation
            try
            {
                CancelMethod.Invoke(host, new object[] { handle });
                Assert.That(((ICollection)HostField("notificationQueue")).Count, Is.Zero);
                Assert.That(handle.Diagnostic, Is.SameAs(firstReason));
                Assert.That(PendingCount(), Is.Zero);
                Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L));
                if (inSubmitted)
                {
                    Assert.That(HostField("submitted"), Is.SameAs(request));
                    Assert.That(RequestField(request, "cancelled"), Is.True);
                }
                handle.Dispose(); // public completion is a no-op for cancellation
                Assert.That(((ICollection)HostField("notificationQueue")).Count, Is.Zero);
            }
            finally { SetHostField("drainingNotifications", false); }
        }

        [Test]
        public void DetachPending_FromPair_RemovesOnlyTheMatch()
        {
            object a = NewRequest(NewHandle(), Origin(1), 1000L);
            object b = NewRequest(NewHandle(), Origin(2), 2000L);
            EnqueuePending(a);
            EnqueuePending(b);
            PendingMap().Add(Origin(1), a);
            PendingMap().Add(Origin(2), b);
            SetHostField("pendingDeliveryBytes", 3000L);

            Assert.That((bool)DetachMethod.Invoke(host, new[] { a }), Is.True);

            Assert.That(PendingCount(), Is.EqualTo(1));
            Assert.That(PeekPending(), Is.SameAs(b), "B keeps its queue position");
            Assert.That(PendingMap().Count, Is.EqualTo(1));
            Assert.That(PendingMap().Contains(Origin(2)), Is.True, "only the detached origin left the map");
            Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(2000L), "the detached reservation left the pending accounting");
            Assert.That(RequestField(a, "reservedDeliveryBytes"), Is.EqualTo(1000L), "the amount stays on the request as its local reservation");
        }

        [Test]
        public void DetachPending_MissingRequest_ReturnsFalseWithZeroChanges()
        {
            object a = NewRequest(NewHandle(), Origin(1), 1000L);
            object b = NewRequest(NewHandle(), Origin(2), 2000L);
            EnqueuePending(b);
            PendingMap().Add(Origin(2), b);
            SetHostField("pendingDeliveryBytes", 2000L);

            Assert.That((bool)DetachMethod.Invoke(host, new[] { a }), Is.False);

            Assert.That(PendingCount(), Is.EqualTo(1));
            Assert.That(PeekPending(), Is.SameAs(b));
            Assert.That(PendingMap().Count, Is.EqualTo(1));
            Assert.That(PendingMap().Contains(Origin(2)), Is.True);
            Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(2000L), "a miss changes nothing");
        }

        [Test]
        public void DetachPending_DifferentMappedRequest_DoesNotRemoveItsEntry()
        {
            object oldRequest = NewRequest(NewHandle(), Origin(1), 1000L);
            object differentRequest = NewRequest(NewHandle(), Origin(1), 2000L);
            EnqueuePending(oldRequest);
            PendingMap().Add(Origin(1), differentRequest);
            SetHostField("pendingDeliveryBytes", 1000L);

            Assert.That((bool)DetachMethod.Invoke(host, new[] { oldRequest }), Is.True);
            Assert.That(PendingCount(), Is.Zero);
            Assert.That(PendingMap().Count, Is.EqualTo(1));
            Assert.That(PendingMap()[Origin(1)], Is.SameAs(differentRequest));
            Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L));
            Assert.That(RequestField(oldRequest, "reservedDeliveryBytes"), Is.EqualTo(1000L));
            Assert.That(RequestField(differentRequest, "reservedDeliveryBytes"), Is.EqualTo(2000L));
        }

        [Test]
        public void PendingCancelTwice_NotifiesOnceAndLeavesUsesUnchanged()
        {
            Texture2D source = NewSource(128);
            Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation hall), Is.True);
            var lease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(source, hall) });
            Assert.That(lease.TryAcquire(), Is.True, "the fixture acquires the use it owns");
            int notifications = 0;
            try
            {
                TextureExecutionHandle handle = NewHandle();
                handle.Completed += _ => notifications++;
                object request = NewRequest(handle, Origin(1), 1000L);
                RequestType.GetField("requestedSourceLease", Refl).SetValue(request, lease);
                EnqueuePending(request);
                PendingMap().Add(Origin(1), request);
                SetHostField("pendingDeliveryBytes", 1000L);

                CancelMethod.Invoke(host, new[] { handle });

                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));

                CancelMethod.Invoke(host, new[] { handle });

                Assert.That(notifications, Is.EqualTo(1), "the second cancel finds nothing and changes nothing");
                Assert.That(lease.IsValid, Is.True, "cancel never Disposes a requested Lease");
                Assert.That(UseCount(lease), Is.EqualTo(1), "cancel never returns an acquired use");
                Assert.That(PendingCount(), Is.Zero);
                Assert.That(PendingMap().Count, Is.Zero, "the cancelled request left the map");
                Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L), "the cancelled local reservation was resolved once");
            }
            finally
            {
                lease.ReleaseUse();
                lease.Dispose();
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void SubmittedCancel_TerminatesHandleAndKeepsResourcesUntilFence()
        {
            Texture2D source = null;
            TextureSourceLease lease = null;
            bool acquired = false;
            TextureHallAllocation borrowedSource = default;
            TextureHallAllocation ownedSource = default;
            TextureHallAllocation ownedOutput = default;
            TextureHallAllocation pool = default;
            TextureExecutionHandle handle = null;
            int notifications = 0;
            Action<TextureExecutionHandle> callback = _ => notifications++;
            try
            {
                source = NewSource(128);
                Assert.That(allocator.TryReserve(128, 128, out borrowedSource), Is.True);
                Assert.That(allocator.TryReserve(128, 128, out ownedSource), Is.True);
                Assert.That(allocator.TryReserve(256, 256, out ownedOutput), Is.True);
                Assert.That(allocator.TryReserve(256, 256, out pool), Is.True);
                Assert.That(borrowedSource, Is.Not.EqualTo(ownedSource));
                lease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding>
                    { ["a"] = new TextureSourceLease.Binding(source, borrowedSource) });
                acquired = lease.TryAcquire();
                Assert.That(acquired, Is.True);
                handle = NewHandle();
                handle.Completed += callback;
                object request = NewRequest(handle, Origin(3), 0L);
                var owned = new List<TextureHallAllocation> { ownedSource };
                var temporaries = new[] { pool };
                RequestType.GetField("ownedSourceHalls", Refl).SetValue(request, owned);
                RequestType.GetField("ownedOutputHall", Refl).SetValue(request, ownedOutput);
                RequestType.GetField("temporaryPool", Refl).SetValue(request, temporaries);
                RequestType.GetField("acquiredSourceLease", Refl).SetValue(request, lease);
                SetHostField("submitted", request);
                int occupiedBefore = allocator.OccupiedRoomCount;
                ulong versionBefore = allocator.Version;

                CancelMethod.Invoke(host, new[] { handle });

                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(RequestField(request, "cancelled"), Is.EqualTo(true));
                Assert.That(HostField("submitted"), Is.SameAs(request));
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore));
                Assert.That(RequestField(request, "ownedSourceHalls"), Is.SameAs(owned));
                Assert.That(owned.Count, Is.EqualTo(1));
                Assert.That(owned[0], Is.EqualTo(ownedSource));
                Assert.That(RequestField(request, "ownedOutputHall"), Is.EqualTo(ownedOutput));
                Assert.That(RequestField(request, "temporaryPool"), Is.SameAs(temporaries));
                Assert.That(temporaries[0], Is.EqualTo(pool));
                Assert.That(RequestField(request, "acquiredSourceLease"), Is.SameAs(lease));
                Assert.That(UseCount(lease), Is.EqualTo(1));
                Assert.That(lease.IsReleaseRequested, Is.False);
            }
            finally
            {
                try
                {
                    if (handle != null) handle.Completed -= callback;
                    SetHostField("submitted", null);
                }
                finally
                {
                    try { if (acquired) lease.ReleaseUse(); }
                    finally
                    {
                        try { lease?.Dispose(); }
                        finally
                        {
                            try
                            {
                                if (borrowedSource.IsValid) allocator.TryRelease(borrowedSource);
                                if (ownedSource.IsValid) allocator.TryRelease(ownedSource);
                                if (ownedOutput.IsValid) allocator.TryRelease(ownedOutput);
                                if (pool.IsValid) allocator.TryRelease(pool);
                            }
                            finally { if (source != null) UnityEngine.Object.DestroyImmediate(source); }
                        }
                    }
                }
            }
        }

        private static TextureExecutionHandle NewHandle() => new TextureExecutionHandle();

        private object Origin(ulong value) => new TextureExecutionOriginKey(host, value);

        private object NewRequest(TextureExecutionHandle handle, object origin, long reserved)
        {
            object request = Activator.CreateInstance(RequestType, nonPublic: true);
            RequestType.GetField("handle", Refl).SetValue(request, handle);
            RequestType.GetField("origin", Refl).SetValue(request, origin);
            RequestType.GetField("reservedDeliveryBytes", Refl).SetValue(request, reserved);
            return request;
        }

        private object RequestField(object request, string name) => RequestType.GetField(name, Refl).GetValue(request);

        private object HostField(string name) => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private void SetHostField(string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private object PendingQueue() => HostField("pending");

        private int PendingCount() => ((ICollection)PendingQueue()).Count;

        private void ClearPendingQueue() => PendingQueue().GetType().GetMethod("Clear").Invoke(PendingQueue(), null);

        private void EnqueuePending(object request) => PendingQueue().GetType().GetMethod("Enqueue").Invoke(PendingQueue(), new[] { request });

        private object PeekPending() => PendingQueue().GetType().GetMethod("Peek").Invoke(PendingQueue(), null);

        private IDictionary PendingMap() => (IDictionary)HostField("pendingByOrigin");

        private static int UseCount(TextureSourceLease lease) => (int)typeof(TextureSourceLease).GetField("useCount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lease);

        private static Texture2D NewSource(int edge) => new Texture2D(edge, edge, UnityEngine.TextureFormat.RGBAHalf, false, true);
    }
}