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
    /// Spec15-2 Phase06-9 CPU coverage for the prepared teardown. The fixture sets the initialized flag, the
    /// allocator, artificial requests, and submitted=null explicitly via reflection; no GPU is started and no
    /// artificial fence is passed off as Passed (no ticket path is exercised). The three cases check the pending
    /// destroy with the detach completing before Destroying, the fault path rejecting the next initialization with
    /// HostFaulted, and an existing Cancelled terminal reason surviving the teardown without a second notification.
    /// Fixture subscriptions are removed in finally and every case leaves the queue, map, and submitted slot empty.
    /// </summary>
    public sealed class Spec152PreparedTeardownTests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo DestroyMethod = typeof(TextureStackMachineHost).GetMethod("DestroyPrepared", Refl);
        private static readonly MethodInfo TeardownMethod = typeof(TextureStackMachineHost).GetMethod("TeardownPrepared", Refl);
        private static readonly MethodInfo CancelMethod = typeof(TextureStackMachineHost).GetMethod("CancelPrepared", Refl);
        private static readonly MethodInfo TryInitializeMethod = typeof(TextureStackMachineHost).GetMethod("TryInitializePrepared", Refl);

        private GameObject root;
        private TextureStackMachineHost host;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Teardown Host");
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
        public void DestroyPrepared_PendingOnly_DetachesBeforeDestroyingAndTerminatesAll()
        {
            object a = NewRequest(NewHandle(), Origin(1), 1000L);
            object b = NewRequest(NewHandle(), Origin(2), 2000L);
            EnqueuePending(a);
            EnqueuePending(b);
            PendingMap().Add(Origin(1), a);
            PendingMap().Add(Origin(2), b);
            SetHostField("pendingDeliveryBytes", 3000L);

            var completed = new List<TextureExecutionHandle>();
            Action<TextureExecutionHandle> observer = handle => completed.Add(handle);
            ((TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(a)).Completed += observer;
            ((TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(b)).Completed += observer;
            int destroyingFired = 0;
            int queueCountAtDestroying = -1;
            int mapCountAtDestroying = -1;
            Action destroyer = () =>
            {
                destroyingFired++;
                queueCountAtDestroying = PendingCount();
                mapCountAtDestroying = PendingMap().Count;
            };
            host.Destroying += destroyer;
            try
            {
                DestroyMethod.Invoke(host, null);

                Assert.That(queueCountAtDestroying, Is.EqualTo(0), "the pending batch was detached before Destroying");
                Assert.That(mapCountAtDestroying, Is.EqualTo(0), "the map was cleared before Destroying");
                Assert.That(PendingCount(), Is.EqualTo(0));
                Assert.That(PendingMap().Count, Is.EqualTo(0));
                Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L));
                Assert.That(HostField("submitted"), Is.Null);
                Assert.That(completed.Count, Is.EqualTo(2), "the closing batch flushed both pending notifications");
                foreach (object request in new[] { a, b })
                {
                    TextureExecutionHandle handle = (TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(request);
                    Assert.That(handle.IsCompleted, Is.True);
                    Assert.That(handle.Succeeded, Is.False);
                    Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("HostDestroyed"));
                }
                Assert.That(destroyingFired, Is.EqualTo(1));
                Assert.That(HostField("destroying"), Is.True);
                Assert.That(HostField("faulted"), Is.False);
                Assert.That(HostField("closingNotifications"), Is.False, "the drained batch cleared the closing flag");
                Assert.That(HostField("acceptingRequests"), Is.False);
                Assert.That(HostField("initialized"), Is.False);
            }
            finally
            {
                ((TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(a)).Completed -= observer;
                ((TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(b)).Completed -= observer;
                host.Destroying -= destroyer;
            }
        }

        [Test]
        public void TeardownPrepared_Fault_TerminatesPendingAndNextInitializeReportsHostFaulted()
        {
            object a = NewRequest(NewHandle(), Origin(1), 1000L);
            EnqueuePending(a);
            PendingMap().Add(Origin(1), a);
            SetHostField("pendingDeliveryBytes", 1000L);

            TextureExecutionHandle handle = (TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(a);
            int fired = 0;
            Action<TextureExecutionHandle> observer = _ => fired++;
            handle.Completed += observer;
            int destroyingFired = 0;
            Action destroyer = () => destroyingFired++;
            host.Destroying += destroyer;
            try
            {
                StackMachineDiagnostic faultReason = StackMachineDiagnostic.CreateDomain("texture", "GpuSubmissionFailed", "fixture fault reason.");
                TeardownMethod.Invoke(host, new object[] { null, faultReason, true, false });

                Assert.That(HostField("faulted"), Is.True);
                Assert.That(HostField("destroying"), Is.False);
                Assert.That(PendingCount(), Is.EqualTo(0));
                Assert.That(PendingMap().Count, Is.EqualTo(0));
                Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L));
                Assert.That(handle.IsCompleted, Is.True);
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("GpuSubmissionFailed"), "pending terminates with the teardown reason");
                Assert.That(fired, Is.EqualTo(1), "the closing batch flushed the pending notification");
                Assert.That(destroyingFired, Is.EqualTo(1));

                var args = new object[] { null };
                bool accepted = (bool)TryInitializeMethod.Invoke(host, args);
                Assert.That(accepted, Is.False);
                Assert.That(((StackMachineDiagnostic)args[0]).domainCode, Is.EqualTo("HostFaulted"), "the next initialization reports HostFaulted in the same session");
                Assert.That(HostField("acceptingRequests"), Is.False);
            }
            finally
            {
                handle.Completed -= observer;
                host.Destroying -= destroyer;
            }
        }

        [Test]
        public void TeardownPrepared_KeepsExistingCancelledTerminalReason()
        {
            object a = NewRequest(NewHandle(), Origin(1), 1000L);
            EnqueuePending(a);
            PendingMap().Add(Origin(1), a);
            SetHostField("pendingDeliveryBytes", 1000L);

            TextureExecutionHandle handle = (TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(a);
            int fired = 0;
            Action<TextureExecutionHandle> observer = _ => fired++;
            handle.Completed += observer;
            try
            {
                CancelMethod.Invoke(host, new object[] { handle });
                Assert.That(fired, Is.EqualTo(1), "the cancel flush delivered the single notification");
                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));

                DestroyMethod.Invoke(host, null);

                Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"), "an existing terminal reason is not overwritten");
                Assert.That(fired, Is.EqualTo(1), "the already-notified handle does not fire again");
                Assert.That(PendingCount(), Is.EqualTo(0));
                Assert.That(PendingMap().Count, Is.EqualTo(0));
                Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L));
                Assert.That(HostField("destroying"), Is.True);
            }
            finally
            {
                handle.Completed -= observer;
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

        private object PendingQueue() => HostField("pending");

        private int PendingCount() => ((ICollection)PendingQueue()).Count;

        private void ClearPendingQueue() => PendingQueue().GetType().GetMethod("Clear").Invoke(PendingQueue(), null);

        private void EnqueuePending(object request) => PendingQueue().GetType().GetMethod("Enqueue").Invoke(PendingQueue(), new[] { request });

        private IDictionary PendingMap() => (IDictionary)HostField("pendingByOrigin");
    }
}

