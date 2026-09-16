// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-5 CPU coverage for the prepared-path notification flush. The §12.5 single loop is verified
    /// through the shared notificationQueue/EnqueueNotification and the lifecycle flags (set via reflection because
    /// OnEnable's real event is not wired yet). Handles are finalized with TrySetTerminalState before enqueue, and
    /// only the expected observer exception is consumed with LogAssert.
    /// </summary>
    public sealed class Spec152PreparedNotificationTests
    {
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly MethodInfo FlushMethod = typeof(TextureStackMachineHost).GetMethod("FlushPreparedNotifications", Refl);

        private GameObject root;
        private TextureStackMachineHost host;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Spec152 Prepared Notifications Host");
            host = root.AddComponent<TextureStackMachineHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void NormalFlush_ProcessesOnlyTheStartCount_ChainAdvancesOnePerFlush()
        {
            TextureExecutionHandle first = NewFinalizedHandle();
            TextureExecutionHandle second = NewFinalizedHandle();
            TextureExecutionHandle third = NewFinalizedHandle();
            int fired = 0;
            first.Completed += _ => { fired++; host.EnqueueNotification(second); };
            second.Completed += _ => { fired++; host.EnqueueNotification(third); };
            third.Completed += _ => fired++;

            host.EnqueueNotification(first);
            Flush(host);
            Assert.That(fired, Is.EqualTo(1), "a normal flush processes only the notifications present at its start");

            Flush(host);
            Assert.That(fired, Is.EqualTo(2));

            Flush(host);
            Assert.That(fired, Is.EqualTo(3));
        }

        [Test]
        public void ClosingBatch_DrainsTheWholeQueue()
        {
            SetField(host, "closingNotifications", true);
            TextureExecutionHandle first = NewFinalizedHandle();
            TextureExecutionHandle second = NewFinalizedHandle();
            TextureExecutionHandle third = NewFinalizedHandle();
            int fired = 0;
            first.Completed += _ => { fired++; host.EnqueueNotification(second); };
            second.Completed += _ => { fired++; host.EnqueueNotification(third); };
            third.Completed += _ => fired++;

            host.EnqueueNotification(first);
            Flush(host);

            Assert.That(fired, Is.EqualTo(3), "the ending batch drains until the queue is empty");
            Assert.That(GetField(host, "closingNotifications"), Is.False, "the flag clears when the drained queue is empty");
            Assert.That(GetField(host, "resumeRequested"), Is.False, "the flush consumes the resume request without an OnEnable");
            Assert.That(GetField(host, "acceptingRequests"), Is.False, "without resumeRequested the host stays stopped");
        }

        [Test]
        public void FirstSubscriberThrows_SecondSubscriberStillRuns()
        {
            TextureExecutionHandle handle = NewFinalizedHandle();
            int secondCalls = 0;
            handle.Completed += _ => throw new Exception("prepared notification observer threw");
            handle.Completed += _ => secondCalls++;

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("prepared notification observer threw"));
            host.EnqueueNotification(handle);
            Flush(host);

            Assert.That(secondCalls, Is.EqualTo(1), "a throwing subscriber must not block the remaining subscribers");
        }

        [Test]
        public void ReentrantFlushWhileDraining_IsNoOp()
        {
            TextureExecutionHandle first = NewFinalizedHandle();
            TextureExecutionHandle second = NewFinalizedHandle();
            int secondCalls = 0;
            bool reentrantSawNothing = false;
            first.Completed += _ =>
            {
                Flush(host);
                reentrantSawNothing = secondCalls == 0;
            };
            second.Completed += _ => secondCalls++;

            host.EnqueueNotification(first);
            host.EnqueueNotification(second);
            Flush(host);

            Assert.That(reentrantSawNothing, Is.True, "the re-entrant flush returned without processing the pending notification");
            Assert.That(secondCalls, Is.EqualTo(1), "the outer loop still processes the pending notification");
        }

        private static TextureExecutionHandle NewFinalizedHandle()
        {
            var handle = new TextureExecutionHandle();
            Assert.That(handle.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "FixtureTerminal", "fixture terminal."), null), Is.True, "the fixture handle finalizes before its enqueue");
            return handle;
        }

        private static void Flush(TextureStackMachineHost host) => FlushMethod.Invoke(host, null);

        private static void SetField(object target, string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(target, value);

        private static object GetField(object target, string name) => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(target);
    }
}