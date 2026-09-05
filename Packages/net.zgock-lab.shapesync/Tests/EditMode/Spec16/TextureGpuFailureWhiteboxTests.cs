// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Reflection;
using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>Virtual GPU failure coverage for the Spec16 current-texture Material route.</summary>
    /// <remarks>
    /// These tests deliberately stop at the Texture execution boundary. They do not claim to reproduce a
    /// driver or hardware fault; they force the same terminal failure seam that the host uses after a failed
    /// submit, stale fence, or unsupported dispatch, and verify the no-delivery/no-commit contract in whitebox.
    /// </remarks>
    public sealed class TextureGpuFailureWhiteboxTests
    {
        [Test]
        public void VirtualGpuDispatchFailure_IsTerminalAndCannotPublishDelivery()
        {
            var handle = new TextureExecutionHandle();
            int completionCount = 0;
            handle.Completed += _ => completionCount++;
            StackMachineDiagnostic failure = StackMachineDiagnostic.CreateDomain("texture", "DispatchOperationUnsupported", "Virtual GPU dispatch failure.");

            InvokeFailure(handle, failure);

            Assert.That(handle.IsCompleted, Is.True);
            Assert.That(handle.Succeeded, Is.False);
            Assert.That(handle.Diagnostic, Is.SameAs(failure));
            Assert.That(handle.Result, Is.Null);
            Assert.That(completionCount, Is.EqualTo(1));

            // A late fence callback must not turn a failed dispatch into a successful delivery.
            InvokeGpuFence(handle);
            Assert.That(handle.Succeeded, Is.False);
            Assert.That(handle.Result, Is.Null);
            Assert.That(handle.Diagnostic, Is.SameAs(failure));
            Assert.That(completionCount, Is.EqualTo(1));
            handle.Dispose();
        }

        [Test]
        public void VirtualGpuAdmissionFailure_RejectsBeforeReservation()
        {
            const long gridBytes = 1024L;
            const long budgetBytes = 4096L;

            Assert.That(TextureStackMachineHost.TryValidateDeliveryReservation(
                gridBytes, 1024, 1024, 1025, budgetBytes, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
        }

        private static void InvokeFailure(TextureExecutionHandle handle, StackMachineDiagnostic diagnostic)
        {
            MethodInfo method = typeof(TextureExecutionHandle).GetMethod(
                "CompleteFailure", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(StackMachineDiagnostic) }, null);
            Assert.That(method, Is.Not.Null);
            method.Invoke(handle, new object[] { diagnostic });
        }

        private static void InvokeGpuFence(TextureExecutionHandle handle)
        {
            MethodInfo method = typeof(TextureExecutionHandle).GetMethod(
                "CompleteGpuFence", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(TextureDelivery), typeof(TextureSourceLease), typeof(TextureOutputLease) }, null);
            Assert.That(method, Is.Not.Null);
            method.Invoke(handle, new object[] { null, null, null });
        }
    }
}
