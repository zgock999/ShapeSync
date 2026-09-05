// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class TextureEditModeStackMachineTests
    {
        [UnityTest]
        public IEnumerator StartAndPump_HandsOffOneLinearRgbaHalfCompletion()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);

            using (var machine = new TextureEditModeStackMachine(compute))
            {
                Assert.That(machine.Start(plan, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Pending));

                for (int i = 0; i < 120 && machine.Status == TextureEditModeExecutionStatus.Pending; i++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    machine.Pump(out StackMachineDiagnostic pumpDiagnostic);
                    Assert.That(pumpDiagnostic, Is.Null);
                }

                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Succeeded));
                Assert.That(machine.TryTakeCompletion(out TextureCompletion completion), Is.True);
                Assert.That(machine.TryTakeCompletion(out _), Is.False);
                machine.Dispose();
                using (completion)
                {
                    Assert.That(completion.Texture, Is.Not.Null);
                    Assert.That(completion.Texture.IsCreated(), Is.True);
                    Assert.That(completion.Texture.graphicsFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                    Assert.That(completion.Texture.sRGB, Is.False);
                    Assert.That(completion.Texture.enableRandomWrite, Is.True);
                }
            }
        }

        [UnityTest]
        public IEnumerator StartAndPump_ExecutesFrozenPlanAfterSourceDocumentMutation()
        {
            TextureRecipeStub stub = CreateFillStub(Color.red);
            Assert.That(TextureExecutionPlan.TryCreate(stub, out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            stub.Document.wordSource = "0 1 0 1 FILL .";
            stub.Document.outputLogicalName = "mutated";
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            using (var machine = new TextureEditModeStackMachine(compute))
            {
                Assert.That(machine.Start(plan, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                for (int i = 0; i < 240 && machine.Status == TextureEditModeExecutionStatus.Pending; i++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    machine.Pump(out _);
                }
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Succeeded), machine.Diagnostic?.message);
                Assert.That(machine.TryTakeCompletion(out TextureCompletion completion), Is.True);
                using (completion) AssertPixel(ReadPixel(completion.Texture), Color.red);
            }
        }

        [Test]
        public void Start_RejectsMissingComputeProgramWithStructuredDiagnostic()
        {
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            using (var machine = new TextureEditModeStackMachine(null))
            {
                Assert.That(machine.Start(plan, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Failed));
                Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.DomainFailure));
                Assert.That(diagnostic.domainCode, Is.EqualTo("ComputeProgramRequired"));
            }
        }

        [Test]
        public void Start_PropagatesCapabilityProbeRejectWithoutFallback()
        {
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            using (var machine = new TextureEditModeStackMachine(compute, null, RejectCapability))
            {
                Assert.That(machine.Start(plan, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Failed));
                Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.DomainFailure));
                Assert.That(diagnostic.domainCode, Is.EqualTo("GpuCapabilityUnavailable"));
            }
        }

        [Test]
        public void Start_RejectsComputeProgramMissingRequiredKernelWithoutFallback()
        {
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            ComputeShader normalOnlyProgram = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
            Assert.That(normalOnlyProgram, Is.Not.Null);
            using (var machine = new TextureEditModeStackMachine(normalOnlyProgram))
            {
                LogAssert.Expect(LogType.Error, new Regex("Kernel 'KFill' not found"));
                Assert.That(machine.Start(plan, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Failed));
                Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.DomainFailure));
                Assert.That(diagnostic.domainCode, Is.EqualTo("ComputeKernelMissing"));
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(TextureEditModeExecutionStatus.Failed));
                Assert.That(pumpDiagnostic, Is.SameAs(diagnostic));
            }
        }

        [Test]
        public void Cancel_StopsPendingExecutionWithoutCreatingCompletion()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            using (var machine = new TextureEditModeStackMachine(compute))
            {
                Assert.That(machine.Start(plan, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                machine.Cancel();

                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Cancelled));
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(TextureEditModeExecutionStatus.Cancelled));
                Assert.That(pumpDiagnostic, Is.Null);
                Assert.That(machine.TryTakeCompletion(out _), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator Start_QueuesSecondPlanAndCancelClearsTheBatch()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan second, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            using (var machine = new TextureEditModeStackMachine(compute))
            {
                Assert.That(machine.Start(first, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Start(second, out StackMachineDiagnostic queueDiagnostic), Is.True, queueDiagnostic?.message);
                for (int i = 0; i < 120 && machine.Status == TextureEditModeExecutionStatus.Pending; i++) { yield return null; machine.Pump(out _); }
                Assert.That(machine.TryTakeCompletion(out TextureCompletion firstCompletion), Is.True);
                firstCompletion.Dispose();
                Assert.That(machine.Pump(out StackMachineDiagnostic nextDiagnostic), Is.EqualTo(TextureEditModeExecutionStatus.Pending), nextDiagnostic?.message);
                machine.Cancel();
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Cancelled));
                Assert.That(machine.Pump(out _), Is.EqualTo(TextureEditModeExecutionStatus.Cancelled));
                Assert.That(machine.TryTakeCompletion(out _), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator Start_QueuesPlansAndHandsOffEachCompletionInOrder()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(Color.red), out TextureExecutionPlan first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(Color.green), out TextureExecutionPlan second, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            using (var machine = new TextureEditModeStackMachine(compute))
            {
                Assert.That(machine.Start(first, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Start(second, out StackMachineDiagnostic queueDiagnostic), Is.True, queueDiagnostic?.message);
                for (int i = 0; i < 120 && machine.Status == TextureEditModeExecutionStatus.Pending; i++) { yield return null; machine.Pump(out _); }
                Assert.That(machine.TryTakeCompletion(out TextureCompletion firstCompletion), Is.True);
                using (firstCompletion) AssertPixel(ReadPixel(firstCompletion.Texture), Color.red);
                Assert.That(machine.Pump(out StackMachineDiagnostic nextDiagnostic), Is.EqualTo(TextureEditModeExecutionStatus.Pending), nextDiagnostic?.message);
                for (int i = 0; i < 120 && machine.Status == TextureEditModeExecutionStatus.Pending; i++) { yield return null; machine.Pump(out _); }
                Assert.That(machine.TryTakeCompletion(out TextureCompletion secondCompletion), Is.True);
                using (secondCompletion) AssertPixel(ReadPixel(secondCompletion.Texture), Color.green);
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Idle));
            }
        }

        [Test]
        public void Start_RejectsNormalPlanWithoutDedicatedNormalComputeProgram()
        {
            var baseNormal = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var targetNormal = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$base NORMAL_BASE $base $target 0.5 NORMAL_DELTA_ADD NORMAL_FINALIZE .", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                foreach (string name in new[] { "base", "target", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "base", kind = TextureBindingKind.SourceTexture, sourceTexture = baseNormal }, new TextureBindingEntry { logicalName = "target", kind = TextureBindingKind.SourceTexture, sourceTexture = targetNormal }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureExecutionPlan.TryCreate(stub, out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                using (var machine = new TextureEditModeStackMachine(compute))
                {
                    Assert.That(machine.Start(plan, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("NormalComputeProgramRequired"));
                }
            }
            finally { Object.DestroyImmediate(baseNormal); Object.DestroyImmediate(targetNormal); }
        }

        [Test]
        public void Start_RejectsInvalidQueuedPlanWithoutFailingActiveExecution()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            var baseNormal = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var targetNormal = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan active, out StackMachineDiagnostic activeDiagnostic), Is.True, activeDiagnostic?.message);
                Assert.That(TextureExecutionPlan.TryCreate(CreateNormalStub(baseNormal, targetNormal), out TextureExecutionPlan invalidQueued, out StackMachineDiagnostic queuedDiagnostic), Is.True, queuedDiagnostic?.message);
                using (var machine = new TextureEditModeStackMachine(compute))
                {
                    Assert.That(machine.Start(active, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Start(invalidQueued, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("NormalComputeProgramRequired"));
                    Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Pending));
                }
            }
            finally { Object.DestroyImmediate(baseNormal); Object.DestroyImmediate(targetNormal); }
        }

        [Test]
        public void Dispose_ClearsActiveAndQueuedWorkWithoutTouchingTransferredCompletion()
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(TextureExecutionPlan.TryCreate(CreateFillStub(), out TextureExecutionPlan second, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            var machine = new TextureEditModeStackMachine(compute);
            try
            {
                Assert.That(machine.Start(first, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Start(second, out StackMachineDiagnostic queueDiagnostic), Is.True, queueDiagnostic?.message);
                machine.Dispose();

                Assert.That(machine.TryTakeCompletion(out _), Is.False);
                Assert.That(machine.Start(first, out StackMachineDiagnostic disposedDiagnostic), Is.False);
                Assert.That(disposedDiagnostic.domainCode, Is.EqualTo("EditModeTextureMachineDisposed"));
            }
            finally { machine.Dispose(); }
        }

        [UnityTest]
        public IEnumerator StartAndPump_ExecutesNormalVectorPlanOnDedicatedComputeProgram()
        {
            var baseNormal = Solid(new Color(0.5f, 0.5f, 1f, 1f));
            var targetNormal = Solid(new Color(1f, 0.5f, 0.5f, 1f));
            try
            {
                Assert.That(TextureExecutionPlan.TryCreate(CreateNormalStub(baseNormal, targetNormal), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
                ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
                using (var machine = new TextureEditModeStackMachine(compute, normalCompute))
                {
                    Assert.That(machine.Start(plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    for (int i = 0; i < 120 && machine.Status == TextureEditModeExecutionStatus.Pending; i++) { yield return null; machine.Pump(out _); }
                    Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Succeeded));
                    Assert.That(machine.TryTakeCompletion(out TextureCompletion completion), Is.True);
                    using (completion)
                    {
                        Assert.That(completion.Texture.graphicsFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                        Assert.That(completion.Texture.IsCreated(), Is.True);
                    }
                }
            }
            finally { Object.DestroyImmediate(baseNormal); Object.DestroyImmediate(targetNormal); }
        }

        [UnityTest]
        public IEnumerator EditModeWordGpuResults_FillCopyArithmeticAndNormalWeightedBlend()
        {
            var a = Solid(new Color(0.2f, 0.4f, 0.6f, 0.8f));
            var b = Solid(new Color(0.5f, 0.25f, 0.75f, 0.5f));
            var baseNormal = Solid(new Color(0.5f, 0.5f, 1f, 1f));
            var detailNormal = Solid(new Color(1f, 0.5f, 0.5f, 1f));
            try
            {
                yield return AssertEditModePixel("$a $out COPY DROP", new[] { Source("a", a) }, new Vector4(0.2f, 0.4f, 0.6f, 0.8f));
                yield return AssertEditModePixel("$a $b ADD $out COPY DROP", new[] { Source("a", a), Source("b", b) }, new Vector4(0.7f, 0.65f, 1.35f, 1.3f));
                yield return AssertEditModePixel("1 1 1 1 FILL $a SUB .", new[] { Source("a", a) }, new Vector4(0.8f, 0.6f, 0.4f, 0.2f));
                yield return AssertEditModePixel("$a $b MULTIPLY $out COPY DROP", new[] { Source("a", a), Source("b", b) }, new Vector4(0.1f, 0.1f, 0.45f, 0.4f));
                yield return AssertEditModePixel("$base $detail 0.25 NORMAL_WEIGHTED_BLEND $out COPY DROP", new[] { Source("base", baseNormal), Source("detail", detailNormal) }, new Vector4(0.658114f, 0.5f, 0.974342f, 1f), 0.005f);
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(baseNormal); Object.DestroyImmediate(detailNormal); }
        }

        [UnityTest]
        public IEnumerator EditModeWordGpuResults_AlphaYuvAndColorize()
        {
            var bottom = Solid(new Color(1f, 0f, 0f, 0.5f)); var top = Solid(new Color(0f, 0f, 1f, 0.25f));
            var source = Solid(new Color(0.2f, 0.4f, 0.6f, 0.9f)); var gray = Solid(new Color(0.3f, 0.6f, 0.9f, 0.1f));
            var y = Solid(new Color(0.5f, 0f, 0f)); var u = Solid(new Color(0.75f, 0f, 0f)); var v = Solid(new Color(0.25f, 0f, 0f));
            var colorize = Solid(new Color(0.5f, 0.5f, 0.5f, 0.25f));
            try
            {
                yield return AssertEditModePixel("$bottom $top ACOPY .", new[] { Source("bottom", bottom), Source("top", top) }, new Vector4(0.6f, 0f, 0.4f, 0.625f));
                yield return AssertEditModePixel("$source $gray ALPHA .", new[] { Source("source", source), Source("gray", gray) }, new Vector4(0.2f, 0.4f, 0.6f, 0.6f));
                yield return AssertEditModePixel("$y $u $v YUV $out COPY DROP", new[] { Source("y", y), Source("u", u), Source("v", v) }, new Vector4(0.1063f, 0.5702f, 0.9639f, 1f), 0.005f);
                yield return AssertEditModePixel("$colorize 0.6666667 1 0 COLORIZE .", new[] { Source("colorize", colorize) }, new Vector4(0.4278f, 0.4278f, 1f, 0.25f), 0.005f);
            }
            finally { Object.DestroyImmediate(bottom); Object.DestroyImmediate(top); Object.DestroyImmediate(source); Object.DestroyImmediate(gray); Object.DestroyImmediate(y); Object.DestroyImmediate(u); Object.DestroyImmediate(v); Object.DestroyImmediate(colorize); }
        }

        [UnityTest]
        public IEnumerator EditModeWordGpuResults_NormalVectorPipeline()
        {
            var baseNormal = Solid(new Color(0.5f, 0.5f, 1f, 1f)); var targetNormal = Solid(new Color(1f, 0.5f, 0.5f, 1f));
            try { yield return AssertEditModePixel("$base NORMAL_BASE $base $target 0.5 NORMAL_DELTA_ADD NORMAL_FINALIZE .", new[] { Source("base", baseNormal), Source("target", targetNormal) }, new Vector4(0.853553f, 0.5f, 0.853553f, 1f), 0.005f); }
            finally { Object.DestroyImmediate(baseNormal); Object.DestroyImmediate(targetNormal); }
        }

        [UnityTest]
        public IEnumerator EditModeWordGpuResults_PremultipliedResampleAndOutputDirectives()
        {
            var background = Solid(new Color(0.5f, 0f, 0f, 0.5f));
            var foreground = Solid(new Color(0f, 0f, 0.25f, 0.25f));
            var gradient = HorizontalGradient(128, 128);
            var canvas = Solid(new Color(0.125f, 0.25f, 0.5f, 1f), 128, 256);
            try
            {
                yield return AssertEditModePixel("$background $foreground PACOPY .", new[] { Source("background", background), Source("foreground", foreground) }, new Vector4(0.375f, 0f, 0.25f, 0.625f));
                yield return AssertEditModePixel("256 SIZE $gradient RESAMPLE .", new[] { Source("gradient", gradient) }, new Vector4(0.5019685f, 0f, 0f, 1f), 0.005f, 256, 256, 128, 128);
                yield return AssertEditModePixel("256 SIZE 0.125 0.25 0.5 1 FILL .", new TextureBindingEntry[0], new Vector4(0.125f, 0.25f, 0.5f, 1f), 0.002f, 256, 256);
                yield return AssertEditModePixel("128 256 RECTSIZE 0.125 0.25 0.5 1 FILL .", new TextureBindingEntry[0], new Vector4(0.125f, 0.25f, 0.5f, 1f), 0.002f, 128, 256);
                yield return AssertEditModePixel("$canvas CANVAS .", new[] { Source("canvas", canvas) }, new Vector4(0.125f, 0.25f, 0.5f, 1f), 0.002f, 128, 256);
            }
            finally { Object.DestroyImmediate(background); Object.DestroyImmediate(foreground); Object.DestroyImmediate(gradient); Object.DestroyImmediate(canvas); }
        }

        [UnityTest]
        public IEnumerator EditModeWordGpuResults_CommonStackManipulation()
        {
            var a = Solid(new Color(0.2f, 0.2f, 0.2f, 0.2f));
            var b = Solid(new Color(0.4f, 0.4f, 0.4f, 0.4f));
            var c = Solid(new Color(0.6f, 0.6f, 0.6f, 0.6f));
            try
            {
                yield return AssertEditModePixel("$a DUP ADD .", new[] { Source("a", a) }, new Vector4(0.4f, 0.4f, 0.4f, 0.4f));
                yield return AssertEditModePixel("$a $b SWAP SUB .", new[] { Source("a", a), Source("b", b) }, new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
                yield return AssertEditModePixel("$a $b OVER ADD SWAP ADD .", new[] { Source("a", a), Source("b", b) }, new Vector4(0.8f, 0.8f, 0.8f, 0.8f));
                yield return AssertEditModePixel("$a $b $c ROT SUB SUB .", new[] { Source("a", a), Source("b", b), Source("c", c) }, Vector4.zero);
                yield return AssertEditModePixel("$a $b DROP .", new[] { Source("a", a), Source("b", b) }, new Vector4(0.2f, 0.2f, 0.2f, 0.2f));
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c); }
        }

        private static TextureRecipeStub CreateFillStub()
        {
            return CreateFillStub(Color.red);
        }

        private static TextureRecipeStub CreateFillStub(Color color)
        {
            var document = new MaterialRecipeDocument { wordSource = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3} FILL $out COPY DROP", color.r, color.g, color.b, color.a), outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
        }

        private static TextureRecipeStub CreateNormalStub(Texture2D baseNormal, Texture2D targetNormal)
        {
            var document = new MaterialRecipeDocument { wordSource = "$base NORMAL_BASE $base $target 0.5 NORMAL_DELTA_ADD NORMAL_FINALIZE .", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string name in new[] { "base", "target", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "base", kind = TextureBindingKind.SourceTexture, sourceTexture = baseNormal }, new TextureBindingEntry { logicalName = "target", kind = TextureBindingKind.SourceTexture, sourceTexture = targetNormal }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
        }

        private static Texture2D Solid(Color color, int width = 128, int height = 128)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[width * height]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static Texture2D HorizontalGradient(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = new Color((float)x / (width - 1), 0f, 0f, 1f);
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static TextureBindingEntry Source(string logicalName, Texture2D texture) => new TextureBindingEntry { logicalName = logicalName, kind = TextureBindingKind.SourceTexture, sourceTexture = texture };

        private static IEnumerator AssertEditModePixel(string wordSource, TextureBindingEntry[] sources, Vector4 expected, float tolerance = 0.002f, int expectedWidth = 128, int expectedHeight = 128, int x = 0, int y = 0)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            for (int i = 0; i < sources.Length; i++) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = sources[i].logicalName, declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var bindings = new TextureBindingEntry[sources.Length + 1]; sources.CopyTo(bindings, 0); bindings[bindings.Length - 1] = new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall };
            Assert.That(TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, bindings), out TextureExecutionPlan plan, out StackMachineDiagnostic planDiagnostic), Is.True, planDiagnostic?.message);
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
            using (var machine = new TextureEditModeStackMachine(compute, normalCompute))
            {
                Assert.That(machine.Start(plan, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                for (int i = 0; i < 240 && machine.Status == TextureEditModeExecutionStatus.Pending; i++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                    machine.Pump(out _);
                }
                Assert.That(machine.Status, Is.EqualTo(TextureEditModeExecutionStatus.Succeeded), machine.Diagnostic?.message);
                Assert.That(machine.TryTakeCompletion(out TextureCompletion completion), Is.True);
                using (completion)
                {
                    Assert.That(completion.Texture.width, Is.EqualTo(expectedWidth));
                    Assert.That(completion.Texture.height, Is.EqualTo(expectedHeight));
                    AssertPixel(ReadPixel(completion.Texture, x, y), expected, tolerance);
                }
            }
        }

        private static bool RejectCapability(out TextureGpuCapability capability, out StackMachineDiagnostic diagnostic)
        {
            capability = default;
            diagnostic = StackMachineDiagnostic.CreateDomain("texture", "GpuCapabilityUnavailable", "Test-only GPU capability rejection.");
            return false;
        }

        private static Color ReadPixel(RenderTexture texture, int x = 0, int y = 0)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = texture;
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                readback.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false); readback.Apply(false, false);
                return readback.GetPixel(0, 0);
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }

        private static void AssertPixel(Color actual, Vector4 expected, float tolerance = 0.002f)
        {
            Assert.That(actual.r, Is.EqualTo(expected.x).Within(tolerance)); Assert.That(actual.g, Is.EqualTo(expected.y).Within(tolerance)); Assert.That(actual.b, Is.EqualTo(expected.z).Within(tolerance)); Assert.That(actual.a, Is.EqualTo(expected.w).Within(tolerance));
        }

    }
}
