// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Tests
{
    public sealed class TextureStackMachineContractTests
    {
        [Test]
        public void ColorLiteralNormalizer_ExpandsRgbaAndMapsEachExpandedToken()
        {
            MaterialRecipeDocument document = CreateDocument("#FFFFFFFF FILL $out COPY DROP");

            Assert.That(TextureColorLiteralNormalizer.TryNormalizeForCompile(document, out MaterialRecipeDocument normalized, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(normalized.wordSource, Is.EqualTo("1 1 1 1 FILL $out COPY DROP"));
            Assert.That(normalized.diagnosticSourceMap.Count, Is.EqualTo(8));
            Assert.That(normalized.diagnosticSourceMap[0].sourceOffset, Is.EqualTo(0));
            Assert.That(normalized.diagnosticSourceMap[3].sourceOffset, Is.EqualTo(0));
            Assert.That(normalized.diagnosticSourceMap[4].sourceOffset, Is.EqualTo(10));
        }

        [Test]
        public void ColorLiteralNormalizer_RejectsNonRgbaHex()
        {
            MaterialRecipeDocument document = CreateDocument("#FFF FILL $out COPY DROP");

            Assert.That(TextureColorLiteralNormalizer.TryNormalizeForCompile(document, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MalformedColorLiteral"));
            Assert.That(diagnostic.tokenIndex, Is.EqualTo(0));
        }

        [Test]
        public void TextureBindingContext_RequiresOneMatchingOutputHall()
        {
            MaterialRecipeDocument document = CreateDocument("$out DROP");
            var stub = new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "other", kind = TextureBindingKind.OutputHall }
            });

            Assert.That(TextureBindingContext.TryCreate(stub, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("OutputBindingInvalid"));
        }

        [Test]
        public void TextureBindingTemplate_CreatesValidatedRuntimeStubFromSerializedLogicalWords()
        {
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var template = UnityEngine.ScriptableObject.CreateInstance<TextureBindingTemplate>();
            try
            {
                template.SetBindings(new[]
                {
                    new TextureTemplateEntry { word = "source", texture = source, kind = TextureBindingKind.SourceTexture },
                    new TextureTemplateEntry { word = "out", kind = TextureBindingKind.OutputHall }
                });
                var document = new MaterialRecipeDocument { wordSource = "$source $out COPY", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "source", declaredKind = StackMachineBindingKind.Resource });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });

                Assert.That(template.TryCreateStub(document, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(context.TryGetBinding("source", out TextureBinding binding), Is.True);
                Assert.That(binding.SourceTexture, Is.SameAs(source));
            }
            finally { UnityEngine.Object.DestroyImmediate(template); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TextureBindingTemplate_NewInstanceStartsWithReservedOutOutputHall()
        {
            var template = UnityEngine.ScriptableObject.CreateInstance<TextureBindingTemplate>();
            try
            {
                Assert.That(template.Bindings.Count, Is.EqualTo(1));
                Assert.That(template.Bindings[0].word, Is.EqualTo("out"));
                Assert.That(template.Bindings[0].kind, Is.EqualTo(TextureBindingKind.OutputHall));
                Assert.That(template.Bindings[0].texture, Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(template); }
        }

        [Test]
        public void MaterialRecipeDocument_RejectsUnsupportedOutputEdge()
        {
            MaterialRecipeDocument document = CreateDocument("$out DROP");
            document.outputWidth = 1920;

            Assert.That(StackMachineRecipeSerialization.TryValidateDocument(document, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("InvalidOutputExtent"));
        }

        [Test]
        public void TextureWordSet_CompilesFillSignatureAfterNormalization()
        {
            MaterialRecipeDocument document = CreateDocument("#00000000 FILL $out COPY DROP");
            Assert.That(TextureColorLiteralNormalizer.TryNormalizeForCompile(document, out MaterialRecipeDocument normalized, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);

            Assert.That(StackMachineCompiler.TryCompile(normalized, registry, out StackMachinePlan plan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
            Assert.That(plan.Instructions[4].WordId, Is.EqualTo(TextureWordSet.Fill));
        }

        [Test]
        public void TextureWordSet_CompilesStraightAndPremultipliedAlphaWords()
        {
            var document = new MaterialRecipeDocument { wordSource = "$fg $bg ACOPY DROP $fg $bg PACOPY DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "fg", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "bg", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);

            Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Instructions[2].WordId, Is.EqualTo(TextureWordSet.AlphaOver));
            Assert.That(plan.Instructions[6].WordId, Is.EqualTo(TextureWordSet.PremultipliedAlphaOver));
        }

        [Test]
        public void TexturePlanCompiler_CompilesNormalVectorPipelineWithRawDeltaWeight()
        {
            var document = new MaterialRecipeDocument { wordSource = "128 128 RECTSIZE $base NORMAL_BASE $base $target 1.5 NORMAL_DELTA_ADD NORMAL_FINALIZE .", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "base", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "target", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var baseTexture = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var targetTexture = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "base", kind = TextureBindingKind.SourceTexture, sourceTexture = baseTexture }, new TextureBindingEntry { logicalName = "target", kind = TextureBindingKind.SourceTexture, sourceTexture = targetTexture }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
                var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);
                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.NormalBase));
                Assert.That(plan.Records[1].Operation, Is.EqualTo(TextureDispatchOperation.NormalDeltaAdd));
                Assert.That(plan.Records[1].Scalars[0], Is.EqualTo(1.5f));
                Assert.That(plan.Records[2].Operation, Is.EqualTo(TextureDispatchOperation.NormalFinalize));
                Assert.That(plan.Records[3].Output, Is.EqualTo("out"));
            }
            finally { UnityEngine.Object.DestroyImmediate(baseTexture); UnityEngine.Object.DestroyImmediate(targetTexture); }
        }

        [Test]
        public void TexturePlanCompiler_ColorizeRejectsOutOfRangeParameters()
        {
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$source 1.1 0.5 0 COLORIZE .", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                foreach (string name in new[] { "source", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);
                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("InvalidColorizeParameters"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TexturePlanCompiler_ColorizeCompilesOrderedParameters()
        {
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$source 0.62 0.7 -0.2 COLORIZE .", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                foreach (string name in new[] { "source", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);
                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.Colorize));
                Assert.That(plan.Records[0].Scalars, Is.EqualTo(new[] { 0.62f, 0.7f, -0.2f }));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void NormalRecipeExpander_InsertsOnlyActiveNonPbmDeltasBeforeFinalize()
        {
            var baseTexture = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var targetTexture = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var template = new NormalRecipeTemplate("face_normal", "$base CANVAS NORMAL_BASE NORMAL_FINALIZE");
                var targets = new[] { new NormalTargetSource { targetName = "body", texture = targetTexture }, new NormalTargetSource { targetName = "unused", texture = null } };
                var snapshot = new[] { new NormalTargetWeight("body", 0.75f, true), new NormalTargetWeight("unused", 0f, true), new NormalTargetWeight("PBM_smile", 0.5f, true) };

                Assert.That(NormalRecipeExpander.TryCreateStub(template, baseTexture, targets, snapshot, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(stub.Document.wordSource, Does.Contain("0.75 NORMAL_DELTA_ADD"));
                Assert.That(stub.Document.wordSource, Does.Not.Contain("0.5 NORMAL_DELTA_ADD"));
                Assert.That(stub.Document.wordSource, Does.EndWith("NORMAL_FINALIZE ."));
                Assert.That(stub.Bindings.Length, Is.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(baseTexture); UnityEngine.Object.DestroyImmediate(targetTexture); }
        }

        [Test]
        public void NormalRecipeExpander_RejectsPbmSourceName()
        {
            var baseTexture = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var targetTexture = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var template = new NormalRecipeTemplate("face_normal", "$base CANVAS NORMAL_BASE NORMAL_FINALIZE");
                var targets = new[] { new NormalTargetSource { targetName = "PBM_smile", texture = targetTexture } };
                var snapshot = new[] { new NormalTargetWeight("PBM_smile", 0.75f, true) };

                Assert.That(NormalRecipeExpander.TryCreateStub(template, baseTexture, targets, snapshot, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("NormalSourceMapInvalid"));
            }
            finally { UnityEngine.Object.DestroyImmediate(baseTexture); UnityEngine.Object.DestroyImmediate(targetTexture); }
        }

        [Test]
        public void TexturePlanCompiler_CompilesSourceLessFillAtOutputEdge()
        {
            MaterialRecipeDocument document = CreateDocument("#10203040 FILL $out COPY DROP");
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
            Assert.That(TextureColorLiteralNormalizer.TryNormalizeForCompile(document, out MaterialRecipeDocument normalized, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(normalized, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);

            Assert.That(TexturePlanCompiler.TryCompile(commonPlan, normalized, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.OutputWidth, Is.EqualTo(128));
            Assert.That(plan.OutputHeight, Is.EqualTo(128));
            Assert.That(plan.Records.Count, Is.EqualTo(2));
            Assert.That(plan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.Fill));
            Assert.That(plan.Records[1].Operation, Is.EqualTo(TextureDispatchOperation.Copy));
        }

        [Test]
        public void TexturePlanCompiler_RectSize_SelectsNativeRectangularExtent()
        {
            MaterialRecipeDocument document = CreateDocument("256 128 RECTSIZE #10203040 FILL $out COPY DROP");
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
            Assert.That(TextureColorLiteralNormalizer.TryNormalizeForCompile(document, out MaterialRecipeDocument normalized, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(normalized, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);

            Assert.That(TexturePlanCompiler.TryCompile(commonPlan, normalized, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.OutputWidth, Is.EqualTo(256));
            Assert.That(plan.OutputHeight, Is.EqualTo(128));
        }

        [Test]
        public void TexturePlanCompiler_RejectsCanvasForSourceLessPlan()
        {
            MaterialRecipeDocument document = CreateDocument("$source CANVAS #FFFFFFFF FILL .");
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "source", declaredKind = StackMachineBindingKind.Resource });
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
            Assert.That(TextureColorLiteralNormalizer.TryNormalizeForCompile(document, out MaterialRecipeDocument normalized, out StackMachineDiagnostic normalizeDiagnostic), Is.True, normalizeDiagnostic?.message);
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(normalized, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic commonDiagnostic), Is.True, commonDiagnostic?.message);

            try
            {
                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, normalized, context, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TextureCanvasSourceNotRead"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TextureGpuCapabilityProbe_SelectsLargestGridAndDeliveryEdge()
        {
            long budgetFor1024 = 2L * 1024L * 1024L * TextureGpuCapabilityProbe.BytesPerPixel;
            long budgetFor8192 = 2L * 8192L * 8192L * TextureGpuCapabilityProbe.BytesPerPixel;

            Assert.That(TextureGpuCapabilityProbe.SelectFixedGridEdge(8192, budgetFor1024), Is.EqualTo(1024));
            Assert.That(TextureGpuCapabilityProbe.SelectFixedGridEdge(8192, budgetFor8192), Is.EqualTo(8192));
            Assert.That(TextureGpuCapabilityProbe.SelectFixedGridEdge(8192, budgetFor8192 - 1L), Is.EqualTo(4096));
            Assert.That(TextureGpuCapabilityProbe.SelectFixedGridEdge(127, budgetFor1024), Is.EqualTo(0));
            Assert.That(TextureGpuCapabilityProbe.SelectFixedGridEdge(128, 2L * 128L * 128L * TextureGpuCapabilityProbe.BytesPerPixel - 1L), Is.EqualTo(0));
        }

        [Test]
        public void TextureStackMachineHost_RejectsExtentOutsideFixedGrid()
        {
            Assert.That(TextureStackMachineHost.TryValidateExtentWithinGrid(256, 128, 256, out StackMachineDiagnostic validDiagnostic), Is.True, validDiagnostic?.message);
            Assert.That(TextureStackMachineHost.TryValidateExtentWithinGrid(512, 128, 256, out StackMachineDiagnostic oversizedDiagnostic), Is.False);
            Assert.That(oversizedDiagnostic.domainCode, Is.EqualTo("OutputExtentExceedsGrid"));
            Assert.That(TextureStackMachineHost.TryValidateExtentWithinGrid(128, 192, 256, out StackMachineDiagnostic nonPowerOfTwoDiagnostic), Is.False);
            Assert.That(nonPowerOfTwoDiagnostic.domainCode, Is.EqualTo("OutputExtentExceedsGrid"));
        }

        [Test]
        public void TextureStackMachineHost_RejectsDeliveryReservationBeyondGpuBudget()
        {
            const long gridBytes = 1024;
            const long budgetBytes = 4096;

            Assert.That(TextureStackMachineHost.TryValidateDeliveryReservation(gridBytes, 1024, 1024, 1024, budgetBytes, out StackMachineDiagnostic validDiagnostic), Is.True, validDiagnostic?.message);
            Assert.That(TextureStackMachineHost.TryValidateDeliveryReservation(gridBytes, 1024, 1024, 1025, budgetBytes, out StackMachineDiagnostic candidateDiagnostic), Is.False);
            Assert.That(candidateDiagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
            Assert.That(TextureStackMachineHost.TryValidateDeliveryReservation(gridBytes, 0, 0, 4097, budgetBytes, out StackMachineDiagnostic oversizedDiagnostic), Is.False);
            Assert.That(oversizedDiagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
        }

        [Test]
        public void TextureHallAllocator_UsesRectangularFirstFitAndRejectsDoubleRelease()
        {
            var allocator = new TextureHallAllocator(512);

            Assert.That(allocator.TryReserve(256, 128, out TextureHallAllocation first), Is.True);
            Assert.That(first.RoomX, Is.EqualTo(0));
            Assert.That(first.RoomY, Is.EqualTo(0));
            Assert.That(allocator.TryReserve(256, 128, out TextureHallAllocation second), Is.True);
            Assert.That(second.RoomX, Is.EqualTo(2));
            Assert.That(second.RoomY, Is.EqualTo(0));
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(4));
            Assert.That(allocator.TryRelease(first), Is.True);
            Assert.That(allocator.TryRelease(first), Is.False);
            Assert.That(allocator.TryReserve(256, 128, out TextureHallAllocation reused), Is.True);
            Assert.That(reused.RoomX, Is.EqualTo(0));
            Assert.That(reused.RoomY, Is.EqualTo(0));
        }

        [Test]
        public void TextureExecutionOriginKey_IsIssuedByHostAndIncludesHostIdentity()
        {
            var firstRoot = new GameObject("Spec14_FirstOriginHost");
            var secondRoot = new GameObject("Spec14_SecondOriginHost");
            try
            {
                TextureStackMachineHost firstHost = firstRoot.AddComponent<TextureStackMachineHost>();
                TextureStackMachineHost secondHost = secondRoot.AddComponent<TextureStackMachineHost>();
                TextureExecutionOriginKey first = firstHost.CreateOrigin();
                TextureExecutionOriginKey second = firstHost.CreateOrigin();
                TextureExecutionOriginKey foreign = secondHost.CreateOrigin();

                Assert.That(default(TextureExecutionOriginKey).IsValid, Is.False);
                Assert.That(first.IsValid, Is.True);
                Assert.That(first.Value, Is.Not.EqualTo(second.Value));
                Assert.That(first, Is.Not.EqualTo(foreign), "Origin equality must include the issuing host identity.");
                Assert.That(first, Is.EqualTo(first));
            }
            finally
            {
                Object.DestroyImmediate(firstRoot);
                Object.DestroyImmediate(secondRoot);
            }
        }

        [Test]
        public void TextureExecutor_UsesFullyCompiledExecutionMode()
        {
            var executor = new TextureExecutor(null);

            Assert.That(executor.ExecutionMode, Is.EqualTo(StackMachineDomainExecutionMode.FullyCompiled));
        }

        [Test]
        public void TextureExecutionHandle_DisposeIsSafeBeforeCompletion()
        {
            var handle = new TextureExecutionHandle();

            Assert.DoesNotThrow(handle.Dispose);
            Assert.That(handle.Result, Is.Null);
        }

        [Test]
        public void TexturePlanCompiler_AllowsOutputReadAfterAnEarlierRecordForScratchExecution()
        {
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
            var document = new MaterialRecipeDocument { wordSource = "$src $out COPY DROP $out $out ADD DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "src", declaredKind = StackMachineBindingKind.Resource });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[]
                {
                    new TextureBindingEntry { logicalName = "src", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
                    new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
                });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry();
                new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records.Count, Is.EqualTo(2));
                Assert.That(plan.Records[1].Sources[0], Is.EqualTo("out"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TexturePlanCompiler_RejectsCopyWhenSourceAndOutputEdgesDiffer()
        {
            var source = new UnityEngine.Texture2D(256, 256, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$src $out COPY", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "src", declaredKind = StackMachineBindingKind.Resource });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[]
                {
                    new TextureBindingEntry { logicalName = "src", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
                    new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
                });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry();
                new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TextureExtentMismatch"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TextureBindingTemplate_RequiresReservedOutOutputHall()
        {
            var template = UnityEngine.ScriptableObject.CreateInstance<TextureBindingTemplate>();
            try
            {
                template.SetBindings(new[] { new TextureTemplateEntry { word = "result", kind = TextureBindingKind.OutputHall } });
                var document = new MaterialRecipeDocument { wordSource = "1 0 0 1 $result FILL" };

                Assert.That(template.TryCreateStub(document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("OutputBindingInvalid"));
            }
            finally { UnityEngine.Object.DestroyImmediate(template); }
        }

        [Test]
        public void TexturePlanCompiler_SizeFirstWord_SelectsOutputEdge()
        {
            var document = new MaterialRecipeDocument { wordSource = "256 SIZE 1 0 0 1 FILL $out COPY DROP" };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
            Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

            Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.OutputWidth, Is.EqualTo(256));
            Assert.That(plan.OutputHeight, Is.EqualTo(256));
        }

        [Test]
        public void TexturePlanCompiler_CanvasFirstWord_SelectsNonSquareRestoreSource()
        {
            var iris = new UnityEngine.Texture2D(256, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$iris CANVAS $out COPY" };
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "iris", declaredKind = StackMachineBindingKind.Resource });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[]
                {
                    new TextureBindingEntry { logicalName = "iris", kind = TextureBindingKind.SourceTexture, sourceTexture = iris },
                    new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
                });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry();
                new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.OutputWidth, Is.EqualTo(256));
                Assert.That(plan.OutputHeight, Is.EqualTo(128));
            }
            finally { UnityEngine.Object.DestroyImmediate(iris); }
        }

        [Test]
        public void TexturePlanCompiler_CanvasFirstWord_AcceptsSquareSourceWithoutRestore()
        {
            var iris = new UnityEngine.Texture2D(1024, 1024, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$iris CANVAS ." };
                foreach (string name in new[] { "iris", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "iris", kind = TextureBindingKind.SourceTexture, sourceTexture = iris }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.OutputWidth, Is.EqualTo(1024));
                Assert.That(plan.OutputHeight, Is.EqualTo(1024));
                Assert.That(plan.Records[0].Sources, Is.EqualTo(new[] { "iris" }), "CANVAS must leave its source at stack top for the following publish.");
            }
            finally { UnityEngine.Object.DestroyImmediate(iris); }
        }

        [Test]
        public void TexturePlanCompiler_ChainsImageResultsAndPublishesReservedOut()
        {
            var iris = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var light = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var overlay = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$iris $light ACOPY $overlay ACOPY $out COPY DROP" };
                foreach (string name in new[] { "iris", "light", "overlay", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[]
                {
                    new TextureBindingEntry { logicalName = "iris", kind = TextureBindingKind.SourceTexture, sourceTexture = iris },
                    new TextureBindingEntry { logicalName = "light", kind = TextureBindingKind.SourceTexture, sourceTexture = light },
                    new TextureBindingEntry { logicalName = "overlay", kind = TextureBindingKind.SourceTexture, sourceTexture = overlay },
                    new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
                });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry();
                new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records.Count, Is.EqualTo(3));
                Assert.That(plan.Records[1].Sources[0], Is.EqualTo("overlay"));
                Assert.That(TexturePlanCompiler.IsTemporary(plan.Records[1].Sources[1]), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(iris); UnityEngine.Object.DestroyImmediate(light); UnityEngine.Object.DestroyImmediate(overlay); }
        }

        [Test]
        public void TexturePlanCompiler_PublishWordIsEquivalentToReservedOutCopyDrop()
        {
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$source ." };
                foreach (string name in new[] { "source", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[]
                {
                    new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
                    new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall }
                });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry();
                new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records.Count, Is.EqualTo(1));
                Assert.That(plan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.Copy));
                Assert.That(plan.Records[0].Sources[0], Is.EqualTo("source"));
                Assert.That(plan.Records[0].Output, Is.EqualTo("out"));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TexturePlanCompiler_AlphaUsesSourceThenGrayMask()
        {
            var source = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var gray = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$source $gray ALPHA ." };
                foreach (string name in new[] { "source", "gray", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "gray", kind = TextureBindingKind.SourceTexture, sourceTexture = gray }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);

                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records.Count, Is.EqualTo(2));
                Assert.That(plan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.Alpha));
                Assert.That(plan.Records[0].Sources, Is.EqualTo(new[] { "source", "gray" }));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(gray); }
        }

        [Test]
        public void TexturePlanCompiler_UsesDistinctTemporaryHallsForDuplicatedIntermediate()
        {
            var iris = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var a = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var b = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            var c = new UnityEngine.Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            try
            {
                var document = new MaterialRecipeDocument { wordSource = "$iris $a ACOPY DUP $b ACOPY SWAP $c ACOPY ADD ." };
                foreach (string name in new[] { "iris", "a", "b", "c", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "iris", kind = TextureBindingKind.SourceTexture, sourceTexture = iris }, new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = a }, new TextureBindingEntry { logicalName = "b", kind = TextureBindingKind.SourceTexture, sourceTexture = b }, new TextureBindingEntry { logicalName = "c", kind = TextureBindingKind.SourceTexture, sourceTexture = c }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(TextureBindingContext.TryCreate(stub, out TextureBindingContext context, out StackMachineDiagnostic bindingDiagnostic), Is.True, bindingDiagnostic?.message);
                var registry = new StackMachineWordRegistry(); new TextureWordSet().RegisterInto(registry);
                Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Records.Count, Is.EqualTo(5));
                Assert.That(plan.Records[1].Sources[1], Is.EqualTo(plan.Records[2].Sources[1]));
                Assert.That(plan.Records[3].Sources[0], Is.EqualTo(plan.Records[1].Output));
                Assert.That(plan.Records[3].Sources[1], Is.EqualTo(plan.Records[2].Output));
            }
            finally { UnityEngine.Object.DestroyImmediate(iris); UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(c); }
        }

        [Test]
        public void TextureExecutionPlan_TryCreate_LowersWithoutExecutionOwnership()
        {
            var document = CreateDocument("1 0 0 1 FILL $out COPY DROP");
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });

            Assert.That(TextureExecutionPlan.TryCreate(stub, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.BindingContext, Is.Not.Null);
            Assert.That(plan.DispatchPlan, Is.Not.Null);
            Assert.That(plan.DispatchPlan.Records.Count, Is.EqualTo(2));
            Assert.That(plan.DispatchPlan.Records[0].Operation, Is.EqualTo(TextureDispatchOperation.Fill));
            document.outputLogicalName = "mutated";
            Assert.That(plan.BindingContext.OutputLogicalName, Is.EqualTo("out"));
        }

        private static MaterialRecipeDocument CreateDocument(string source)
        {
            var document = new MaterialRecipeDocument { wordSource = source, outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            return document;
        }
    }
}
