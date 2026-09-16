// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>
    /// Spec15-2 Phase06-22 preparation for ordinary re-entrant notification delivery (Q13/Q14/Q16). The shared
    /// fixture owns initialized GPU hosts and teardown where Q13 needs the executor; Q14/Q16 use an uninitialized
    /// host only as the notification queue owner. New-queue integration execution is gated until Phase06-26.
    /// </summary>
    public sealed class Spec152Batch22Tests
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

        // Q13 is fixed as two cases: the same executor receives a stub, or the already compiled plan, for A1/A2/A3.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q13_Reentrant_EnqueueSameExecutor_Stub() => Spec152_Q13_Reentrant_EnqueueSameExecutor(false);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q13_Reentrant_EnqueueSameExecutor_Compiled() => Spec152_Q13_Reentrant_EnqueueSameExecutor(true);

        private IEnumerator Spec152_Q13_Reentrant_EnqueueSameExecutor(bool compiledPlan)
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Q13ReentrantCase(host, source, compiledPlan);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator Q13ReentrantCase(TextureStackMachineHost host, Texture2D source, bool compiledPlan)
        {
            host.testFencePending = true;
            try
            {
                TextureRecipeStub stub = Spec152GpuFixture.CreateCopy128Stub(source);
                TextureExecutionPlan plan = null;
                if (compiledPlan)
                    Assert.That(TextureExecutionPlan.TryCreate(stub, out plan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                TextureExecutor executor = new TextureExecutor(host);
                TextureExecutionOriginKey origin = host.CreateOrigin();
                TextureExecutionHandle a3 = null;
                int a1ObserverCalls = 0;

                bool acceptedA1 = Execute(executor, compiledPlan, stub, plan, origin, out TextureExecutionHandle a1, out StackMachineDiagnostic a1Diagnostic);
                if (a1 != null) scope.Handles.Add(a1);
                Assert.That(acceptedA1, Is.True, a1Diagnostic?.message);
                Assert.That(a1, Is.Not.Null);
                a1.Completed += _ =>
                {
                    a1ObserverCalls++;
                    bool acceptedA3 = Execute(executor, compiledPlan, stub, plan, origin, out a3, out StackMachineDiagnostic a3Diagnostic);
                    if (a3 != null) scope.Handles.Add(a3);
                    Assert.That(acceptedA3, Is.True, a3Diagnostic?.message);
                    Assert.That(a3, Is.Not.Null);
                };

                bool acceptedA2 = Execute(executor, compiledPlan, stub, plan, origin, out TextureExecutionHandle a2, out StackMachineDiagnostic a2Diagnostic);
                if (a2 != null) scope.Handles.Add(a2);
                Assert.That(acceptedA2, Is.True, a2Diagnostic?.message);
                Assert.That(a1ObserverCalls, Is.EqualTo(1));
                Assert.That(a1.IsCompleted, Is.True);
                Assert.That(a1.Diagnostic, Is.Not.Null);
                Assert.That(a1.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
                Assert.That(a2, Is.Not.Null);
                Assert.That(a2.IsCompleted, Is.True, "A3 accepted from A1's observer coalesces the returned A2 handle");
                Assert.That(a2.Diagnostic, Is.Not.Null);
                Assert.That(a2.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
                Assert.That(a3, Is.Not.Null);
                Assert.That(a3.IsCompleted, Is.False);
                Assert.That(host.PendingRequestCount, Is.EqualTo(1), "the final A3 remains as the sole pending request");
                Assert.That(host.HasSubmittedRequest, Is.False);

                host.FlushNotifications();
                Assert.That(host.PendingRequestCount, Is.EqualTo(1));
                a3.Dispose();
                Assert.That(a3.IsCompleted, Is.True);
                Assert.That(a3.Diagnostic, Is.Not.Null);
                Assert.That(a3.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(host.PendingRequestCount, Is.Zero);
            }
            finally
            {
                host.testFencePending = false;
            }
            yield break;
        }

        // Q14: the first subscriber throws exactly once; the second subscriber still runs and enqueues a deferred
        // finalized handle. The exception is observed with LogAssert.Expect, never hidden by a catch in the test.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q14_Reentrant_ObserverThrows()
        {
#if UNITY_EDITOR
            TextureStackMachineHost host = CreateNotificationHost();
            TextureExecutionHandle first = FinalizedHandle();
            TextureExecutionHandle deferred = FinalizedHandle();
            int secondCalls = 0;
            int deferredCalls = 0;
            first.Completed += _ => throw new Exception("phase06-22 observer threw");
            first.Completed += _ =>
            {
                secondCalls++;
                host.EnqueueNotification(deferred);
            };
            deferred.Completed += _ => deferredCalls++;

            LogAssert.Expect(LogType.Exception, new Regex("phase06-22 observer threw"));
            host.EnqueueNotification(first);
            host.FlushNotifications();

            Assert.That(secondCalls, Is.EqualTo(1));
            Assert.That(deferredCalls, Is.Zero, "the second subscriber's enqueue is deferred to the next normal flush");
            Assert.That(NotificationQueueCount(host), Is.EqualTo(1));

            host.FlushNotifications();
            Assert.That(deferredCalls, Is.EqualTo(1));
            Assert.That(NotificationQueueCount(host), Is.Zero);
#else
            Assert.Ignore("Unity notification host tests are Editor-only.");
#endif
            yield break;
        }

        // Q16: callback-added notifications are bounded by the queue count observed at each normal flush start.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q16_Notification_DrainIsBounded()
        {
#if UNITY_EDITOR
            TextureStackMachineHost host = CreateNotificationHost();
            TextureExecutionHandle first = FinalizedHandle();
            TextureExecutionHandle second = FinalizedHandle();
            TextureExecutionHandle third = FinalizedHandle();
            int fired = 0;
            first.Completed += _ => { fired++; host.EnqueueNotification(second); };
            second.Completed += _ => { fired++; host.EnqueueNotification(third); };
            third.Completed += _ => fired++;

            host.EnqueueNotification(first);
            host.FlushNotifications();
            Assert.That(fired, Is.EqualTo(1));
            Assert.That(NotificationQueueCount(host), Is.EqualTo(1));

            host.FlushNotifications();
            Assert.That(fired, Is.EqualTo(2));
            Assert.That(NotificationQueueCount(host), Is.EqualTo(1));

            host.FlushNotifications();
            Assert.That(fired, Is.EqualTo(3));
            Assert.That(NotificationQueueCount(host), Is.Zero);
#else
            Assert.Ignore("Unity notification host tests are Editor-only.");
#endif
            yield break;
        }

        private TextureStackMachineHost CreateNotificationHost()
        {
            var root = new GameObject("Spec152Batch22 Notification Host");
            scope.Root = root;
            scope.Host = root.AddComponent<TextureStackMachineHost>();
            return scope.Host;
        }

        private static TextureExecutionHandle FinalizedHandle()
        {
            var handle = new TextureExecutionHandle();
            Assert.That(handle.TrySetTerminalState(false,
                StackMachineDiagnostic.CreateDomain("texture", "FixtureTerminal", "fixture terminal."), null), Is.True);
            return handle;
        }

        private static bool Execute(TextureExecutor executor, bool compiledPlan, TextureRecipeStub stub, TextureExecutionPlan plan,
            TextureExecutionOriginKey origin, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
        {
            if (compiledPlan) return executor.TryExecute(plan, origin, null, out handle, out diagnostic);
            return executor.TryExecute(stub, origin, null, out handle, out diagnostic);
        }

        private static int NotificationQueueCount(TextureStackMachineHost host)
            => (int)HostField(host, "notificationQueue").GetType().GetProperty("Count").GetValue(HostField(host, "notificationQueue"));

        private static object HostField(TextureStackMachineHost host, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);
    }
}
