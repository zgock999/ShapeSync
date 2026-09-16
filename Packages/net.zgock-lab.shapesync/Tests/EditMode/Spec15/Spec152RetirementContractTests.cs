// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 phase-05 CPU contract coverage for the retirement types (H04 structure, H06 CPU Uncertain
    /// part). These tests are pure CPU whitebox checks of the ticket's field-type contract, the one-shot
    /// Uncertain report with no exception re-poll and retained references, and idempotent cleanup without raw
    /// resources. The pump is not yet connected to host initialization; that lands with phase 06.
    /// </summary>
    public sealed class Spec152RetirementContractTests
    {
        [Test]
        public void Spec152_Retirement_H04_TicketInstanceFieldsHaveNoForbiddenTypes()
        {
            var ticket = new TextureGpuRetirementTicket(default, null, null, null, false);

            FieldInfo[] fields = typeof(TextureGpuRetirementTicket).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            int forbidden = 0;
            foreach (FieldInfo field in fields)
            {
                Type type = field.FieldType;
                if (type == typeof(TextureStackMachineHost) || type == typeof(UnityEngine.SceneManagement.Scene)
                    || type == typeof(GameObject) || type == typeof(TextureExecutor)
                    || typeof(Delegate).IsAssignableFrom(type))
                {
                    forbidden++;
                }
            }

            Assert.That(forbidden, Is.EqualTo(0), "the ticket must hold no host/scene/GameObject/executor/delegate reference");
            Assert.That(fields.Length, Is.GreaterThan(0), "the constructor contract requires the raw-reference fields to exist");
        }

        [Test]
        public void Spec152_Retirement_H06_UncertainTicketReportsErrorOnceAndKeepsReferences()
        {
            var grid = new RenderTexture(16, 16, 0);
            var sources = new Texture[] { new RenderTexture(16, 16, 0) };
            try
            {
                var ticket = new TextureGpuRetirementTicket(default, grid, null, sources, true);

                LogAssert.Expect(LogType.Error, new Regex("GpuRetirementUncertain"));
                ticket.ReportUncertainOnce();
                ticket.ReportUncertainOnce();

                Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain));
                Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain), "a ticket in Uncertain must not throw on re-poll");

                Assert.That(ReadField(ticket, "grid"), Is.SameAs(grid), "an Uncertain ticket keeps its resources quarantined");
                Assert.That(ReadField(ticket, "sources"), Is.SameAs(sources));
                Assert.That((bool)ReadField(ticket, "disposed"), Is.False, "Uncertain must not run the passed cleanup");
            }
            finally
            {
                if (grid != null) UnityEngine.Object.DestroyImmediate(grid);
                foreach (Texture source in sources)
                    if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Spec152_Retirement_H06_PollFailureTransitionsOnceWithoutRepoll()
        {
            var sources = new Texture[0];
            var ticket = new TextureGpuRetirementTicket(default, null, null, sources, false);
            ticket.testFailPollOnce = true;

            LogAssert.Expect(LogType.Error, new Regex("GpuRetirementUncertain"));

            Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain));
            Assert.That((int)ReadField(ticket, "testFenceReadCount"), Is.EqualTo(1));
            Assert.That((bool)ReadField(ticket, "testFailPollOnce"), Is.False);
            Assert.That((bool)ReadField(ticket, "uncertain"), Is.True);
            Assert.That((bool)ReadField(ticket, "uncertainErrorReported"), Is.True);
            Assert.That((bool)ReadField(ticket, "disposed"), Is.False);

            ticket.testFailPollOnce = true;
            ticket.testHoldPending = true;
            Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain));
            Assert.That(ticket.Poll(), Is.EqualTo(TextureGpuRetirementPollResult.Uncertain));
            Assert.That((int)ReadField(ticket, "testFenceReadCount"), Is.EqualTo(1), "an Uncertain ticket must not re-read the fence");
            Assert.That((bool)ReadField(ticket, "testFailPollOnce"), Is.True);

            ticket.ReportUncertainOnce();
            ticket.ReportUncertainOnce();
            LogAssert.NoUnexpectedReceived();

            Assert.That(ReadField(ticket, "sources"), Is.SameAs(sources));
            Assert.That(ReadField(ticket, "grid"), Is.Null);
            Assert.That(ReadField(ticket, "delivery"), Is.Null);
            Assert.That((bool)ReadField(ticket, "disposed"), Is.False);
        }

        [Test]
        public void Spec152_Retirement_H06_CleanupWithoutRawResourcesIsIdempotent()
        {
            var ticket = new TextureGpuRetirementTicket(default, null, null, null, false);

            ticket.RunPassedCleanupOnce();
            Assert.That((bool)ReadField(ticket, "disposed"), Is.True);

            Assert.That(() => ticket.RunPassedCleanupOnce(), Throws.Nothing, "the disposed guard must make the second cleanup a no-op");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CrossReview002_DestroyPump_TransfersPendingOrUncertainWithoutCleanup(bool uncertain)
        {
            TextureGpuRetirementFallback.EnsureInstalled();
            TextureGpuRetirementFallback.EnsureInstalled();
            int drivers = 0;
            foreach (var system in UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop().subSystemList)
                if (system.type == typeof(TextureGpuRetirementFallback)) drivers++;
            Assert.That(drivers, Is.EqualTo(1));
            var ticket = new TextureGpuRetirementTicket(default, null, null, Array.Empty<Texture>(), uncertain);
            ticket.testHoldPending = true; // CPU fixture: no GPU work or raw resource exists
            var root = new GameObject("CR002 retirement CPU");
            var pump = root.AddComponent<TextureGpuRetirementPump>();
            var retained = (System.Collections.IList)typeof(TextureGpuRetirementFallback)
                .GetField("tickets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            int initialCount = retained.Count;
            try
            {
                if (uncertain) LogAssert.Expect(LogType.Error, new Regex("GpuRetirementUncertain"));
                Assert.That(pump.Accept(ticket), Is.True);
                UnityEngine.Object.DestroyImmediate(root);
                Assert.That(retained.Contains(ticket), Is.True);
                TextureGpuRetirementFallback.Retain(ticket);
                Assert.That(retained.Count, Is.EqualTo(initialCount + 1), "same ticket cannot be registered twice");
                var poll = typeof(TextureGpuRetirementFallback).GetMethod("Poll", BindingFlags.Static | BindingFlags.NonPublic);
                poll.Invoke(null, null);
                poll.Invoke(null, null);
                Assert.That(ReadField(ticket, "disposed"), Is.False);
                Assert.That(ticket.testFenceReadCount, Is.Zero);
                Assert.That(retained.Contains(ticket), Is.True);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                retained.Remove(ticket); // only this resource-free artificial ticket; never clear the session owner
            }
        }

        private static object ReadField(TextureGpuRetirementTicket ticket, string name)
            => typeof(TextureGpuRetirementTicket).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(ticket);
    }
}
