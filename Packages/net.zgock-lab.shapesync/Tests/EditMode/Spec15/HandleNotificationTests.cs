// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15 step-4 CPU coverage for the handle terminal/safe-notification split, the host notification
    /// flush boundary, and the read-only lease acquire checks. These tests are pure CPU whitebox coverage;
    /// queue/waiting behaviour (Q-series) lands with the host scheduler step.
    /// </summary>
    public sealed class HandleNotificationTests
    {
        [Test]
        public void TerminalStateFinalizesOnceAndSafeFiringRunsEverySubscriber()
        {
            var handle = new TextureExecutionHandle();
            StackMachineDiagnostic first = StackMachineDiagnostic.CreateDomain("texture", "FirstTerminal", "first terminal.");
            StackMachineDiagnostic second = StackMachineDiagnostic.CreateDomain("texture", "SecondTerminal", "second terminal.");
            int firstCalls = 0;
            int secondCalls = 0;
            handle.Completed += _ => { firstCalls++; throw new Exception("notification observer threw"); };
            handle.Completed += _ => secondCalls++;

            LogAssert.Expect(LogType.Exception, new Regex("notification observer threw"));
            handle.CompleteFailure(first);

            Assert.That(handle.IsCompleted, Is.True);
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Completed));
            Assert.That(handle.Succeeded, Is.False);
            Assert.That(handle.Diagnostic, Is.SameAs(first));
            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1), "a throwing subscriber must not block the remaining subscribers");

            Assert.That(handle.TrySetTerminalState(false, second, null), Is.False, "the first terminal state is final");
            Assert.That(handle.Diagnostic, Is.SameAs(first), "a duplicate terminal must not overwrite state");
            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void StatusAndWaitingDiagnosticFollowTransitionRules()
        {
            var handle = new TextureExecutionHandle();
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Queued));
            Assert.That(handle.WaitingDiagnostic, Is.Null);

            StackMachineDiagnostic waiting = StackMachineDiagnostic.CreateDomain("texture", "WaitingForHalls", "waiting.");
            handle.SetWaiting(waiting);
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
            Assert.That(handle.WaitingDiagnostic, Is.SameAs(waiting));

            handle.MarkSubmitted();
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
            Assert.That(handle.WaitingDiagnostic, Is.Null);

            StackMachineDiagnostic terminal = StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "cancelled.");
            Assert.That(handle.TrySetTerminalState(false, terminal, null), Is.True);
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Completed));
            Assert.That(handle.WaitingDiagnostic, Is.Null);
            Assert.That(handle.Diagnostic, Is.SameAs(terminal));

            handle.SetWaiting(waiting);
            handle.MarkSubmitted();
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Completed), "SetWaiting and MarkSubmitted are no-ops after the terminal state");
            Assert.That(handle.WaitingDiagnostic, Is.Null);
        }

        [Test]
        public void FlushProcessesOnlyNotificationsQueuedAtStart()
        {
            var root = new GameObject("spec15-flush-boundary");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            try
            {
                var first = new TextureExecutionHandle();
                var second = new TextureExecutionHandle();
                var deferred = new TextureExecutionHandle();
                int firstCalls = 0;
                int secondCalls = 0;
                int deferredCalls = 0;
                first.Completed += _ =>
                {
                    firstCalls++;
                    Assert.That(deferred.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "deferred terminal."), null), Is.True, "the deferred handle terminalizes before its enqueue");
                    host.EnqueueNotification(deferred);
                    host.FlushNotifications();
                    Assert.That(deferredCalls, Is.EqualTo(0), "a notification enqueued during draining must defer to the next safe boundary");
                };
                second.Completed += _ => secondCalls++;
                deferred.Completed += _ => deferredCalls++;

                Assert.That(first.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "first terminal."), null), Is.True, "only terminal-confirmed handles are enqueued");
                host.EnqueueNotification(first);
                Assert.That(second.TrySetTerminalState(false, StackMachineDiagnostic.CreateDomain("texture", "RequestCancelled", "second terminal."), null), Is.True);
                host.EnqueueNotification(second);
                host.EnqueueNotification(second);
                host.FlushNotifications();

                Assert.That(firstCalls, Is.EqualTo(1));
                Assert.That(secondCalls, Is.EqualTo(1), "a handle enqueued twice must fire its notification only once");
                Assert.That(deferredCalls, Is.EqualTo(0));

                host.FlushNotifications();
                Assert.That(deferredCalls, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void CanAcquireIsReadOnlyAndReleasedLeasesReject()
        {
            var root = new GameObject("spec15-lease-canacquire");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            try
            {
                var bindings = new Dictionary<string, TextureSourceLease.Binding>();
                var sourceLease = new TextureSourceLease(host, bindings);
                Assert.That(sourceLease.CanAcquire, Is.True);
                Assert.That(sourceLease.CanAcquire, Is.True, "CanAcquire must not consume a use");
                Assert.That(sourceLease.TryAcquire(), Is.True);
                Assert.That(sourceLease.TryDispose(out _), Is.True);
                Assert.That(sourceLease.CanAcquire, Is.False, "a released lease cannot be acquired");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}