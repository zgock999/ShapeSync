// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-2 coverage for <c>TextureStackMachineHost.TryValidateRequestInputs</c>. CPU-only synchronous
    /// whitebox tests: the private QueuedRequest fixture is built via reflection, the host allocator is injected for the
    /// unchanged-Version assertion, and nothing is submitted to the GPU. Each failure case asserts the queue count,
    /// allocator.Version, and lease use counts stay unchanged.
    /// </summary>
    public sealed class Spec152PreparedInputsTests
    {
        private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);
        private static readonly MethodInfo ValidateMethod = typeof(TextureStackMachineHost).GetMethod("TryValidateRequestInputs", InstanceAll);

        [Test]
        public void Normal128_AllInputsValid_Passes()
        {
            Texture2D source = NewSource(128);
            var root = new GameObject("Spec152-2 Normal");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            var allocator = new TextureHallAllocator(512);
            SetField(host, "allocator", allocator);
            try
            {
                TextureRequestHallPlan hallPlan = PlanWithBindingContext("1 0 0 1 FILL $a ADD $out COPY", source, out _, out TextureExecutionPlan executionPlan);
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation sourceHall), Is.True);
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation outputHall), Is.True);
                var sourceLease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(source, sourceHall) });
                var outputLease = new TextureOutputLease(host, outputHall);
                object request = NewRequest(executionPlan, hallPlan, sourceLease, outputLease);
                ulong versionBefore = allocator.Version;
                int useBefore = UseCount(sourceLease);

                Assert.That(Validate(host, request), Is.True);

                Assert.That(host.PendingRequestCount, Is.EqualTo(0), "validation never touches the queue");
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "validation never mutates the allocator");
                Assert.That(UseCount(sourceLease), Is.EqualTo(useBefore), "validation never acquires a use");
                Assert.That(host.HasSubmittedRequest, Is.False, "no GPU submit");
                sourceLease.Dispose();
                outputLease.Dispose();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void DestroyedSourceTexture_ReportsSourceTextureUnavailable()
        {
            Texture2D source = NewSource(128);
            var root = new GameObject("Spec152-2 Destroyed");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            var allocator = new TextureHallAllocator(512);
            SetField(host, "allocator", allocator);
            try
            {
                TextureRequestHallPlan hallPlan = PlanWithBindingContext("1 0 0 1 FILL $a ADD $out COPY", source, out _, out TextureExecutionPlan executionPlan);
                object request = NewRequest(executionPlan, hallPlan, null, null);
                ulong versionBefore = allocator.Version;
                UnityEngine.Object.DestroyImmediate(source);

                Assert.That(Validate(host, request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceTextureUnavailable"));
                Assert.That(host.PendingRequestCount, Is.EqualTo(0));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void SameTextureExtentChange_ReportsSourceExtentChanged()
        {
            var source = new RenderTexture(128, 128, 0);
            var root = new GameObject("Spec152-2 ExtentChanged");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            var allocator = new TextureHallAllocator(512);
            SetField(host, "allocator", allocator);
            try
            {
                TextureRequestHallPlan hallPlan = PlanWithBindingContext("1 0 0 1 FILL $a ADD $out COPY", source, out _, out TextureExecutionPlan executionPlan);
                object request = NewRequest(executionPlan, hallPlan, null, null);
                ulong versionBefore = allocator.Version;
                source.width = 256;
                source.height = 256;

                Assert.That(Validate(host, request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceExtentChanged"));
                Assert.That(host.PendingRequestCount, Is.EqualTo(0));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void ReleasedSourceLease_ReportsSourceLeaseInvalid()
        {
            Texture2D source = NewSource(128);
            var root = new GameObject("Spec152-2 ReleasedSource");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            var allocator = new TextureHallAllocator(512);
            SetField(host, "allocator", allocator);
            try
            {
                TextureRequestHallPlan hallPlan = PlanWithBindingContext("1 0 0 1 FILL $a ADD $out COPY", source, out _, out TextureExecutionPlan executionPlan);
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation sourceHall), Is.True);
                var sourceLease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(source, sourceHall) });
                object request = NewRequest(executionPlan, hallPlan, sourceLease, null);
                ulong versionBefore = allocator.Version;
                int useBefore = UseCount(sourceLease);
                sourceLease.Dispose();

                Assert.That(Validate(host, request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceLeaseInvalid"));
                Assert.That(host.PendingRequestCount, Is.EqualTo(0));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "an unregistered fixture lease must not release its hall during validation");
                Assert.That(UseCount(sourceLease), Is.EqualTo(useBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void ReleasedOutputLease_ReportsOutputLeaseInvalid()
        {
            Texture2D source = NewSource(128);
            var root = new GameObject("Spec152-2 ReleasedOutput");
            TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
            var allocator = new TextureHallAllocator(512);
            SetField(host, "allocator", allocator);
            try
            {
                TextureRequestHallPlan hallPlan = PlanWithBindingContext("1 0 0 1 FILL $a ADD $out COPY", source, out _, out TextureExecutionPlan executionPlan);
                Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation outputHall), Is.True);
                var outputLease = new TextureOutputLease(host, outputHall);
                object request = NewRequest(executionPlan, hallPlan, null, outputLease);
                ulong versionBefore = allocator.Version;
                int useBefore = UseCount(outputLease);
                outputLease.Dispose();

                Assert.That(Validate(host, request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("OutputLeaseInvalid"));
                Assert.That(host.PendingRequestCount, Is.EqualTo(0));
                Assert.That(allocator.Version, Is.EqualTo(versionBefore), "an unregistered fixture lease must not release its hall during validation");
                Assert.That(UseCount(outputLease), Is.EqualTo(useBefore));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(source); }
        }


        private static bool Validate(TextureStackMachineHost host, object request) => Validate(host, request, out _);

        private static bool Validate(TextureStackMachineHost host, object request, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { request, null };
            bool result = (bool)ValidateMethod.Invoke(host, args);
            diagnostic = (StackMachineDiagnostic)args[1];
            return result;
        }

        private static object NewRequest(TextureExecutionPlan executionPlan, TextureRequestHallPlan hallPlan, TextureSourceLease sourceLease, TextureOutputLease outputLease)
        {
            Assert.That(RequestType, Is.Not.Null, "private QueuedRequest nested type must exist");
            Assert.That(ValidateMethod, Is.Not.Null, "TryValidateRequestInputs must exist on the host");
            object request = Activator.CreateInstance(RequestType, nonPublic: true);
            RequestType.GetField("plan", InstanceAll).SetValue(request, executionPlan.DispatchPlan);
            RequestType.GetField("context", InstanceAll).SetValue(request, executionPlan.BindingContext);
            RequestType.GetField("hallPlan", InstanceAll).SetValue(request, hallPlan);
            RequestType.GetField("requestedSourceLease", InstanceAll).SetValue(request, sourceLease);
            RequestType.GetField("requestedOutputLease", InstanceAll).SetValue(request, outputLease);
            return request;
        }

        private static int UseCount(TextureSourceLease lease) => (int)typeof(TextureSourceLease).GetField("useCount", InstanceAll).GetValue(lease);

        private static int UseCount(TextureOutputLease lease) => (int)typeof(TextureOutputLease).GetField("useCount", InstanceAll).GetValue(lease);

        private static void SetField(TextureStackMachineHost host, string name, object value) => typeof(TextureStackMachineHost).GetField(name, InstanceAll).SetValue(host, value);

        private static Texture2D NewSource(int edge) => new Texture2D(edge, edge, UnityEngine.TextureFormat.RGBAHalf, false, true);

        private static TextureRequestHallPlan PlanWithBindingContext(string wordSource, Texture source, out StackMachineDiagnostic diagnostic, out TextureExecutionPlan executionPlan, int edge = 128)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = edge, outputHeight = edge };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            Assert.That(TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
            }), out executionPlan, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(TextureRequestHallPlan.TryCreate(executionPlan.DispatchPlan, executionPlan.BindingContext, out TextureRequestHallPlan hallPlan, out diagnostic), Is.True, diagnostic?.message);
            return hallPlan;
        }
    }
}

