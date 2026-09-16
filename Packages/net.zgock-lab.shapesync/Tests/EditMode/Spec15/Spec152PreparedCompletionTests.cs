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
    /// Spec15-2 Phase06-11 CPU coverage for the prepared fence-completion handoff. The fixture sets the
    /// initialized flag, the allocator, artificial requests, and the submitted slot explicitly via reflection;
    /// no GPU is started and no artificial fence is passed off as Passed — FinishPassedRequest is invoked
    /// directly with resource-less or CPU-borrowed requests. The three cases check that Cancelled wins over
    /// Stale, that an already-terminal reason and Result are not overwritten, and that a borrowed retain
    /// creates no new Lease. Fixture subscriptions are removed in finally and every case leaves the queue,
    /// map, and submitted slot empty.
    /// </summary>
    public sealed class Spec152PreparedCompletionTests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo FinishMethod = typeof(TextureStackMachineHost).GetMethod("FinishPassedRequest", Refl);
        private static readonly MethodInfo CancelMethod = typeof(TextureStackMachineHost).GetMethod("CancelPrepared", Refl);

        private GameObject root;
        private TextureStackMachineHost host;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Completion Host");
            host = root.AddComponent<TextureStackMachineHost>();
            SetHostField("initialized", true);
            SetHostField("submitted", null);
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

        [Test]
        public void FinishPassedRequest_CancelledWinsOverStale_CleanupOnlyKeepsCancelledReason()
        {
            var handle = new TextureExecutionHandle();
            object r = NewRequest(handle, Origin(1), 1000L);
            SetRequestField(r, "stale", true);
            SetHostField("submitted", r);

            int fired = 0;
            Action<TextureExecutionHandle> observer = _ => fired++;
            handle.Completed += observer;
            try
            {
                CancelMethod.Invoke(host, new object[] { handle });
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(fired, Is.EqualTo(1), "the cancel flush delivered the single notification");

                FinishMethod.Invoke(host, new[] { r });

                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"), "cancelled wins over stale at the fence");
                Assert.That(fired, Is.EqualTo(1), "the cleanup-only branch does not re-notify");
                Assert.That(HostField("submitted"), Is.Null);
                Assert.That(RequestField(r, "reservedDeliveryBytes"), Is.EqualTo(0L));
            }
            finally
            {
                handle.Completed -= observer;
            }
        }

        [Test]
        public void FinishPassedRequest_AlreadyTerminalHandle_KeepsExistingReasonWithoutSecondNotification()
        {
            Texture2D marker = null;
            TextureExecutionHandle handle = null;
            TextureExecutionResult existing = null;
            int released = 0;
            int fired = 0;
            Action<TextureExecutionHandle> observer = _ => fired++;
            try
            {
                marker = new Texture2D(1, 1);
                var delivery = new TextureDelivery(marker, _ => released++);
                existing = new TextureExecutionResult(delivery);
                handle = new TextureExecutionHandle();
                Assert.That(handle.TrySetTerminalState(true, null, existing), Is.True);
                handle.Completed += observer;
                object r = NewRequest(handle, Origin(1), 0L);
                SetHostField("submitted", r);

                FinishMethod.Invoke(host, new[] { r });

                Assert.That(handle.Succeeded, Is.True);
                Assert.That(handle.Diagnostic, Is.Null);
                Assert.That(handle.Result, Is.SameAs(existing));
                Assert.That(delivery.Texture, Is.SameAs(marker));
                Assert.That(released, Is.Zero, "completion must not dispose the existing Result");
                Assert.That(fired, Is.Zero);
                Assert.That(NotificationQueue().Count, Is.Zero);
                Assert.That(HostField("submitted"), Is.Null);
            }
            finally
            {
                try
                {
                    if (handle != null) handle.Completed -= observer;
                    SetHostField("submitted", null);
                    NotificationQueue().Clear();
                }
                finally
                {
                    try { existing?.Dispose(); }
                    finally { if (marker != null) UnityEngine.Object.DestroyImmediate(marker); }
                }
            }
        }

        [Test]
        public void FinishPassedRequest_BorrowedRetain_CreatesNoNewLease()
        {
            var allocator = new TextureHallAllocator(512);
            TextureHallAllocation sourceHall = default;
            TextureHallAllocation outputHall = default;
            Texture2D source = null;
            TextureSourceLease sourceLease = null;
            TextureOutputLease outputLease = null;
            TextureExecutionHandle handle = null;
            int fired = 0;
            Action<TextureExecutionHandle> observer = _ => fired++;
            var sourceSet = (HashSet<TextureSourceLease>)HostField("outstandingSourceLeases");
            var outputSet = (HashSet<TextureOutputLease>)HostField("outstandingOutputLeases");
            try
            {
                SetHostField("allocator", allocator);
                source = new Texture2D(128, 128);
                Assert.That(allocator.TryReserve(128, 128, out sourceHall), Is.True);
                Assert.That(allocator.TryReserve(128, 128, out outputHall), Is.True);
                Assert.That(sourceHall, Is.Not.EqualTo(outputHall));
                sourceLease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding>
                    { ["a"] = new TextureSourceLease.Binding(source, sourceHall) });
                sourceSet.Add(sourceLease);
                outputLease = new TextureOutputLease(host, outputHall);
                outputSet.Add(outputLease);
                Assert.That(sourceLease.TryAcquire(), Is.True);
                Assert.That(outputLease.TryAcquire(128, 128), Is.True);

                handle = new TextureExecutionHandle();
                handle.Completed += observer;
                object r = NewRequest(handle, Origin(1), 0L);
                SetRequestField(r, "retainSourceLease", true);
                SetRequestField(r, "retainOutputLease", true);
                SetRequestField(r, "requestedSourceLease", sourceLease);
                SetRequestField(r, "acquiredSourceLease", sourceLease);
                SetRequestField(r, "requestedOutputLease", outputLease);
                SetRequestField(r, "acquiredOutputLease", outputLease);
                ((Dictionary<string, TextureHallAllocation>)RequestField(r, "sourceHalls")).Add("a", sourceHall);
                SetRequestField(r, "outputHall", outputHall);
                SetHostField("submitted", r);
                int occupied = allocator.OccupiedRoomCount;
                ulong version = allocator.Version;
                Assert.That(typeof(TextureSourceLease).GetField("useCount", Refl).GetValue(sourceLease), Is.EqualTo(1));
                Assert.That(typeof(TextureOutputLease).GetField("useCount", Refl).GetValue(outputLease), Is.EqualTo(1));

                FinishMethod.Invoke(host, new[] { r });

                Assert.That(handle.Succeeded, Is.True);
                Assert.That(handle.Result, Is.Not.Null);
                Assert.That(handle.Result.TryTakeSourceLease(out TextureSourceLease takenSource), Is.False);
                Assert.That(takenSource, Is.Null);
                Assert.That(handle.Result.TryTakeOutputLease(out TextureOutputLease takenOutput), Is.False);
                Assert.That(takenOutput, Is.Null);
                Assert.That(sourceLease.IsValid, Is.True);
                Assert.That(outputLease.IsValid, Is.True);
                Assert.That(sourceLease.IsReleaseRequested, Is.False);
                Assert.That(outputLease.IsReleaseRequested, Is.False);
                Assert.That(typeof(TextureSourceLease).GetField("useCount", Refl).GetValue(sourceLease), Is.EqualTo(0));
                Assert.That(typeof(TextureOutputLease).GetField("useCount", Refl).GetValue(outputLease), Is.EqualTo(0));
                Assert.That(sourceSet.Count, Is.EqualTo(1));
                Assert.That(outputSet.Count, Is.EqualTo(1));
                Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupied));
                Assert.That(allocator.Version, Is.EqualTo(version));
                Assert.That(RequestField(r, "requestedSourceLease"), Is.Null);
                Assert.That(RequestField(r, "requestedOutputLease"), Is.Null);
                Assert.That(RequestField(r, "acquiredSourceLease"), Is.Null);
                Assert.That(RequestField(r, "acquiredOutputLease"), Is.Null);
                Assert.That(RequestField(r, "reservedDeliveryBytes"), Is.EqualTo(0L));
                Assert.That(HostField("submitted"), Is.Null);
                Assert.That(NotificationQueue().Count, Is.EqualTo(1));
                Assert.That(fired, Is.Zero);
            }
            finally
            {
                try
                {
                    if (handle != null) handle.Completed -= observer;
                    SetHostField("submitted", null);
                    NotificationQueue().Clear();
                }
                finally
                {
                    try { handle?.Result?.Dispose(); }
                    finally
                    {
                        try { sourceLease?.ReleaseUse(); sourceLease?.Dispose(); }
                        finally
                        {
                            try { outputLease?.ReleaseUse(); outputLease?.Dispose(); }
                            finally
                            {
                                try
                                {
                                    if (sourceLease == null && sourceHall.IsValid) allocator.TryRelease(sourceHall);
                                    if (outputLease == null && outputHall.IsValid) allocator.TryRelease(outputHall);
                                }
                                finally { if (source != null) UnityEngine.Object.DestroyImmediate(source); }
                            }
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

        private object HostField(string name) => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);

        private void SetHostField(string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private object RequestField(object request, string name) => RequestType.GetField(name, Refl).GetValue(request);

        private void SetRequestField(object request, string name, object value) => RequestType.GetField(name, Refl).SetValue(request, value);

        private Queue<TextureExecutionHandle> NotificationQueue() => (Queue<TextureExecutionHandle>)HostField("notificationQueue");

        private object PendingQueue() => HostField("pending");

        private void ClearPendingQueue() => PendingQueue().GetType().GetMethod("Clear").Invoke(PendingQueue(), null);

        private IDictionary PendingMap() => (IDictionary)HostField("pendingByOrigin");
    }
}
