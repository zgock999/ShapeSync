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
    /// Spec15-2 Phase06-8 CPU coverage for the prepared lifecycle. Fixtures set the initialized flag and artificial
    /// requests via reflection and call DisablePrepared/EnablePrepared directly; no GPU work is started. The H01
    /// logical part checks the detach-first order, HostDisabled termination of every pending and the submitted
    /// request (whose slot is kept), and the single Destroying notification ending in the prepared flush. The
    /// closing-deferral case checks that Enable only raises resumeRequested until the drain finishes. Fixture
    /// subscriptions are removed in finally and every case leaves the queue, map, and submitted slot empty.
    /// </summary>
    public sealed class Spec152PreparedLifecycleTests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo DisableMethod = typeof(TextureStackMachineHost).GetMethod("DisablePrepared", Refl);
        private static readonly MethodInfo EnableMethod = typeof(TextureStackMachineHost).GetMethod("EnablePrepared", Refl);
        private static readonly MethodInfo FlushMethod = typeof(TextureStackMachineHost).GetMethod("FlushPreparedNotifications", Refl);

        private GameObject root;
        private TextureStackMachineHost host;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Lifecycle Host");
            host = root.AddComponent<TextureStackMachineHost>();
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
        public void DisablePrepared_PendingAndSubmitted_AllTerminateWithHostDisabled()
        {
            SetHostField("initialized", true);
            object a = NewRequest(NewHandle(), Origin(1), 1000L);
            object b = NewRequest(NewHandle(), Origin(2), 2000L);
            object sub = NewRequest(NewHandle(), Origin(3), 3000L);
            EnqueuePending(a);
            EnqueuePending(b);
            PendingMap().Add(Origin(1), a);
            PendingMap().Add(Origin(2), b);
            SetHostField("pendingDeliveryBytes", 3000L);
            SetHostField("submitted", sub);

            var completed = new List<TextureExecutionHandle>();
            Action<TextureExecutionHandle> observer = handle => completed.Add(handle);
            foreach (object request in new[] { a, b, sub })
                ((TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(request)).Completed += observer;
            int destroyingFired = 0;
            Action destroyer = () => destroyingFired++;
            host.Destroying += destroyer;
            try
            {
                DisableMethod.Invoke(host, null);

                Assert.That(PendingCount(), Is.EqualTo(0), "every pending request left the queue");
                Assert.That(PendingMap().Count, Is.EqualTo(0), "the origin map is empty");
                Assert.That(HostField("pendingDeliveryBytes"), Is.EqualTo(0L), "the pending budget was cleared");
                Assert.That(completed.Count, Is.EqualTo(3), "the ending batch flushed every finalized handle");
                foreach (object request in new[] { a, b, sub })
                {
                    TextureExecutionHandle handle = (TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(request);
                    Assert.That(handle.IsCompleted, Is.True);
                    Assert.That(handle.Succeeded, Is.False);
                    Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("HostDisabled"));
                }
                Assert.That(HostField("submitted"), Is.SameAs(sub), "the submitted request keeps its slot and real resources");
                Assert.That(RequestType.GetField("cancelled", Refl).GetValue(sub), Is.True, "the submitted request is marked cancelled");
                Assert.That(destroyingFired, Is.EqualTo(1), "Destroying is notified once for the disable cycle");
                Assert.That(HostField("lifecycleEndingNotified"), Is.True);
                Assert.That(HostField("closingNotifications"), Is.False, "the drained batch cleared the closing flag");
                Assert.That(HostField("acceptingRequests"), Is.False, "no resume was requested during the disable");
            }
            finally
            {
                foreach (object request in new[] { a, b, sub })
                    ((TextureExecutionHandle)RequestType.GetField("handle", Refl).GetValue(request)).Completed -= observer;
                host.Destroying -= destroyer;
            }
        }

        [Test]
        public void EnablePrepared_DuringClosing_OnlyRaisesResumeRequested()
        {
            SetHostField("initialized", true);
            SetHostField("closingNotifications", true);
            TextureExecutionHandle handle = NewFinalizedHandle();
            int fired = 0;
            Action<TextureExecutionHandle> observer = _ => fired++;
            handle.Completed += observer;
            host.EnqueueNotification(handle);
            try
            {
                EnableMethod.Invoke(host, null);

                Assert.That(GetHostField("resumeRequested"), Is.True, "Enable during closing only raises the resume request");
                Assert.That(GetHostField("acceptingRequests"), Is.False, "acceptance does not resume before the ending drain");
                Assert.That(GetHostField("lifecycleEndingNotified"), Is.False, "Enable leaves the notification flag to the flush");
                Assert.That(fired, Is.EqualTo(0), "Enable does not run the flush itself");

                FlushMethod.Invoke(host, null);

                Assert.That(fired, Is.EqualTo(1), "the ending batch drained the pending notification");
                Assert.That(GetHostField("closingNotifications"), Is.False, "the drained batch cleared the closing flag");
                Assert.That(GetHostField("resumeRequested"), Is.False, "the flush consumed the resume request");
                Assert.That(GetHostField("acceptingRequests"), Is.True, "the resume reopens acceptance for the active, enabled fixture host");
                Assert.That(GetHostField("lifecycleEndingNotified"), Is.False, "the reset flag serves the next disable cycle");
            }
            finally
            {
                handle.Completed -= observer;
            }
        }

        [Test]
        public void DisablePrepared_SameCycle_NotifiesDestroyingOnce()
        {
            int fired = 0;
            Action destroyer = () => fired++;
            host.Destroying += destroyer;
            try
            {
                DisableMethod.Invoke(host, null);
                Assert.That(fired, Is.EqualTo(1), "the first disable notifies Destroying");

                DisableMethod.Invoke(host, null);
                Assert.That(fired, Is.EqualTo(1), "the same disable cycle notifies Destroying exactly once");
                Assert.That(GetHostField("lifecycleEndingNotified"), Is.True);
                Assert.That(PendingCount(), Is.EqualTo(0));
                Assert.That(PendingMap().Count, Is.EqualTo(0));
            }
            finally
            {
                host.Destroying -= destroyer;
            }
        }

        private static TextureExecutionHandle NewFinalizedHandle()
        {
            var handle = new TextureExecutionHandle();
            Assert.That(handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "FixtureTerminal", "fixture terminal."), null), Is.True, "the fixture handle finalizes before its enqueue");
            return handle;
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

        private object GetHostField(string name) => HostField(name);

        private void SetHostField(string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private object PendingQueue() => HostField("pending");

        private int PendingCount() => ((ICollection)PendingQueue()).Count;

        private void ClearPendingQueue() => PendingQueue().GetType().GetMethod("Clear").Invoke(PendingQueue(), null);

        private void EnqueuePending(object request) => PendingQueue().GetType().GetMethod("Enqueue").Invoke(PendingQueue(), new[] { request });

        private IDictionary PendingMap() => (IDictionary)HostField("pendingByOrigin");
    }
}
