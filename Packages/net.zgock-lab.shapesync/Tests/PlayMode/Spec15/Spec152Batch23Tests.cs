// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>
    /// Spec15-2 Phase06-23 preparation for termination during notification delivery (Q15/Q17). Lifecycle methods
    /// are invoked through the host lifecycle and notification APIs. New-queue integration execution is gated until
    /// Phase06-26.
    /// </summary>
    public sealed class Spec152Batch23Tests
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

        // Q15: DestroyImmediate is deliberately inside the first Completed subscriber. The outer prepared flush
        // must finish the already captured notification batch without collection mutation or a second delivery.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q15_Reentrant_DestroyHost()
        {
#if UNITY_EDITOR
            TextureStackMachineHost host = CreateNotificationHost();
            TextureExecutionHandle first = FinalizedHandle();
            TextureExecutionHandle second = FinalizedHandle();
            int firstCalls = 0;
            int secondCalls = 0;
            first.Completed += _ =>
            {
                firstCalls++;
                UnityEngine.Object.DestroyImmediate(host.gameObject);
            };
            second.Completed += _ => secondCalls++;

            host.EnqueueNotification(first);
            host.EnqueueNotification(second);
            host.FlushNotifications();

            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1), "the captured ending batch finishes after the Host is destroyed");
            Assert.That(NotificationQueueCount(host), Is.Zero);
#else
            Assert.Ignore("Unity lifecycle tests are Editor-only.");
#endif
            yield break;
        }

        // Q17 is split into Disable and Destroy cases. The first observer ends the Host; the second observer tries
        // Enable and a real executor enqueue while the ending batch is still closing.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q17_Lifecycle_DrainsDuringReentry_Disable() => Spec152_Q17_Lifecycle_DrainsDuringReentry(false);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Q17_Lifecycle_DrainsDuringReentry_Destroy() => Spec152_Q17_Lifecycle_DrainsDuringReentry(true);

        private IEnumerator Spec152_Q17_Lifecycle_DrainsDuringReentry(bool destroyCase)
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Texture2D source = Spec152GpuFixture.CreateCopy128Source(scope);
            yield return Q17LifecycleCase(host, source, destroyCase);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        private IEnumerator Q17LifecycleCase(TextureStackMachineHost host, Texture2D source, bool destroyCase)
        {
            TextureRecipeStub enqueueStub = Spec152GpuFixture.CreateCopy128Stub(source);
            TextureExecutor executor = new TextureExecutor(host);
            TextureExecutionOriginKey origin = host.CreateOrigin();
            TextureExecutionHandle first = FinalizedHandle();
            TextureExecutionHandle second = FinalizedHandle();
            int firstCalls = 0;
            int secondCalls = 0;
            StackMachineDiagnostic enqueueDiagnostic = null;
            bool enqueueAccepted = false;

            first.Completed += _ =>
            {
                firstCalls++;
                if (destroyCase) UnityEngine.Object.DestroyImmediate(host.gameObject);
                else host.enabled = false;
            };
            second.Completed += _ =>
            {
                secondCalls++;
                if (!destroyCase) host.enabled = true;
                enqueueAccepted = executor.TryExecute(enqueueStub, origin, null,
                    out TextureExecutionHandle rejectedHandle, out enqueueDiagnostic);
                if (rejectedHandle != null) scope.Handles.Add(rejectedHandle);
            };

            host.EnqueueNotification(first);
            host.EnqueueNotification(second);
            host.FlushNotifications();

            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
            Assert.That(enqueueAccepted, Is.False);
            Assert.That(enqueueDiagnostic, Is.Not.Null);
            Assert.That(enqueueDiagnostic.domainCode, Is.EqualTo(destroyCase ? "HostDestroyed" : "HostDisabled"));
            Assert.That(NotificationQueueCount(host), Is.Zero);

            if (destroyCase)
            {
                Assert.That(GetField(host, "destroying"), Is.True);
                Assert.That(GetField(host, "acceptingRequests"), Is.False);
            }
            else
            {
                Assert.That(GetField(host, "closingNotifications"), Is.False, "ending Disable batch is drained");
                Assert.That(GetField(host, "resumeRequested"), Is.False);
                Assert.That(GetField(host, "acceptingRequests"), Is.True, "Enable resumes only after the ending batch");
            }
            yield break;
        }

        private TextureStackMachineHost CreateNotificationHost()
        {
            var root = new GameObject("Spec152Batch23 Notification Host");
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

        private static object GetField(object target, string name)
            => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(target);

        private static int NotificationQueueCount(TextureStackMachineHost host)
        {
            object queue = GetField(host, "notificationQueue");
            return (int)queue.GetType().GetProperty("Count").GetValue(queue);
        }
    }
}
