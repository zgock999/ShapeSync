// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class StackMachineFoundationTests
    {
        [Test]
        public void DomainDiagnostic_ToStringPreservesAllStructuredFields()
        {
            StackMachineDiagnostic diagnostic = StackMachineDiagnostic.CreateDomain(
                "outfit-topology", "OutfitTopologyBoneMapNotClosed", "Bone mapping is not closed.",
                tokenIndex: 7, instructionPointer: 11, wordId: "NormalizeOutfit",
                bindingName: "Hair1/SampleI", detail: "renderer=Hair/Renderer; submesh=all");

            string formatted = diagnostic.ToString();
            StringAssert.Contains("OutfitTopologyBoneMapNotClosed", formatted);
            StringAssert.Contains("Bone mapping is not closed.", formatted);
            StringAssert.Contains("code=DomainFailure", formatted);
            StringAssert.Contains("domain=outfit-topology", formatted);
            StringAssert.Contains("domainCode=OutfitTopologyBoneMapNotClosed", formatted);
            StringAssert.Contains("tokenIndex=7", formatted);
            StringAssert.Contains("instructionPointer=11", formatted);
            StringAssert.Contains("wordId=NormalizeOutfit", formatted);
            StringAssert.Contains("bindingName=Hair1/SampleI", formatted);
            StringAssert.Contains("detail=renderer=Hair/Renderer; submesh=all", formatted);
        }

        [Test]
        public void DomainDiagnostic_FormatIsNullSafe()
        {
            Assert.That(StackMachineDiagnostic.Format(null, "fallback"), Is.EqualTo("fallback"));
            Assert.That(StackMachineDiagnostic.Format(null, null), Is.EqualTo("Stack machine failed without a diagnostic."));
        }

        [TestCase(StackMachineDomainExecutionMode.Sequential)]
        [TestCase(StackMachineDomainExecutionMode.BytecodeTransaction)]
        [TestCase(StackMachineDomainExecutionMode.FullyCompiled)]
        public void DomainExecutor_CanChooseEveryExecutionMode(StackMachineDomainExecutionMode mode)
        {
            StackMachinePlan plan = Compile("1 2 +");
            var executor = new FakeExecutor(mode);
            var context = new FakeBindingContext();
            Assert.That(executor.TryCompileDomainPlan(plan, context, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(executor.TryExecute(domainPlan, context, out StackMachineExecutionResult result), Is.True);
            Assert.That(executor.ExecutionMode, Is.EqualTo(mode));
            Assert.That(executor.ReceivedPlan, Is.SameAs(plan));
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
        }

        [Test]
        public void StubWordSet_ExplicitlyRegistersAndEvaluatesNoDomainWords()
        {
            var registry = new StackMachineWordRegistry();
            var words = new StackMachineStubWordSet();
            words.RegisterInto(registry);
            StackMachineRecipeDocument recipe = new StackMachineRecipeDocument { wordSource = "DOMAIN_TEST" };
            Assert.That(StackMachineCompiler.TryCompile(recipe, registry, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.UnknownToken));
        }

        [Test]
        public void DomainWordSet_RegistersAndReceivesEvaluation()
        {
            var registry = new StackMachineWordRegistry();
            var words = new TestWordSet();
            words.RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(new StackMachineRecipeDocument { wordSource = "TEST-WORD" }, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            var machine = new StackMachineStubMachine(words);
            StackMachineExecutionResult result = machine.Execute(plan);
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            Assert.That(words.Evaluated, Is.True);
        }

        [Test]
        public void RejectedDomainWord_ProducesFailureLifecycleAndDiagnostic()
        {
            var registry = new StackMachineWordRegistry();
            var words = new RejectingWordSet(); words.RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(new StackMachineRecipeDocument { wordSource = "REJECT" }, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            StackMachineExecutionResult result = new StackMachineStubMachine(words).Execute(plan);
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Failed }));
            Assert.That(result.Diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.TypeMismatch));
        }

        [Test]
        public void DomainWordSignature_IsEmbeddedInThePlanAndValidated()
        {
            var registry = new StackMachineWordRegistry();
            new NumberToBooleanWordSet().RegisterInto(registry);
            Assert.That(StackMachineCompiler.TryCompile(new StackMachineRecipeDocument { wordSource = "1 NUMBER-TO-BOOLEAN" }, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Instructions[1].Signature.Inputs[0], Is.EqualTo(StackMachineValueTag.Number));
            Assert.That(StackMachineCompiler.TryCompile(new StackMachineRecipeDocument { wordSource = "TRUE NUMBER-TO-BOOLEAN" }, registry, out _, out diagnostic), Is.False);
            Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.TypeMismatch));
        }

        [Test]
        public void InternalCallAndExit_UseReturnStackWithoutSourceSyntax()
        {
            var plan = new StackMachinePlan(new[]
            {
                new StackMachineInstruction(StackMachineOp.Call, target: 3),
                new StackMachineInstruction(StackMachineOp.PushNumber, number: 1),
                new StackMachineInstruction(StackMachineOp.Exit),
                new StackMachineInstruction(StackMachineOp.PushNumber, number: 2),
                new StackMachineInstruction(StackMachineOp.Exit)
            });
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(plan);
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            Assert.That(result.DataStack.Count, Is.EqualTo(2));
            Assert.That(result.DataStack[0].Number, Is.EqualTo(2));
            Assert.That(result.DataStack[1].Number, Is.EqualTo(1));
            Assert.That(result.ReturnDepth, Is.EqualTo(0));
        }

        [Test]
        public void InternalCallAndExit_SupportNestedFrames()
        {
            var plan = new StackMachinePlan(new[]
            {
                new StackMachineInstruction(StackMachineOp.Call, target: 5),
                new StackMachineInstruction(StackMachineOp.PushNumber, number: 1),
                new StackMachineInstruction(StackMachineOp.PushNumber, number: 99),
                new StackMachineInstruction(StackMachineOp.Exit),
                new StackMachineInstruction(StackMachineOp.PushNumber, number: -1),
                new StackMachineInstruction(StackMachineOp.Call, target: 7),
                new StackMachineInstruction(StackMachineOp.Exit),
                new StackMachineInstruction(StackMachineOp.PushNumber, number: 2),
                new StackMachineInstruction(StackMachineOp.Exit)
            });

            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(plan);

            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            Assert.That(result.DataStack, Has.Count.EqualTo(3));
            Assert.That(result.DataStack[0].Number, Is.EqualTo(2));
            Assert.That(result.DataStack[1].Number, Is.EqualTo(1));
            Assert.That(result.DataStack[2].Number, Is.EqualTo(99));
            Assert.That(result.ReturnDepth, Is.EqualTo(0));
        }

        [Test]
        public void InternalCall_InvalidTargetFailsWithoutLeavingFrame()
        {
            var plan = new StackMachinePlan(new[] { new StackMachineInstruction(StackMachineOp.Call, target: 1) });
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(plan);

            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Failed));
            Assert.That(result.Diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.InvalidControlFlow));
            Assert.That(result.ReturnDepth, Is.EqualTo(0));
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Failed }));
        }

        [Test]
        public void RootExit_SucceedsWithoutRequiringSourceControlSyntax()
        {
            var plan = new StackMachinePlan(new[] { new StackMachineInstruction(StackMachineOp.Exit) });
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(plan);

            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Succeeded }));
            Assert.That(result.ReturnDepth, Is.EqualTo(0));
        }

        [Test]
        public void ExecutionResult_DataStackIsSnapshotAcrossSubsequentRuns()
        {
            var machine = new StackMachineStubMachine();
            StackMachineExecutionResult first = machine.Execute(Compile("1 2 +"));
            machine.Execute(Compile("9"));

            Assert.That(first.DataStack, Has.Count.EqualTo(1));
            Assert.That(first.DataStack[0].Number, Is.EqualTo(3));
        }

        [Test]
        public void DomainValue_UsesOpaqueDomainHandleWithoutCoreSemantics()
        {
            StackMachineValue value = StackMachineValue.FromDomainValue(42);
            Assert.That(value.Tag, Is.EqualTo(StackMachineValueTag.DomainValue));
            Assert.That(value.Handle, Is.EqualTo(42));
            Assert.That(value.ToString(), Is.EqualTo("#42"));
        }

        [Test]
        public void PlanBuilder_PatchesTargetsAndPlanDefensivelyCopiesBytecode()
        {
            var builder = new StackMachinePlanBuilder();
            int call = builder.Emit(new StackMachineInstruction(StackMachineOp.Call));
            builder.Emit(new StackMachineInstruction(StackMachineOp.Exit));
            Assert.That(builder.TryPatchTarget(call, 1, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            StackMachinePlan plan = builder.Build();
            Assert.That(plan.Instructions[0].Target, Is.EqualTo(1));

            var source = new[] { new StackMachineInstruction(StackMachineOp.PushNumber, number: 1) };
            var copied = new StackMachinePlan(source);
            source[0] = new StackMachineInstruction(StackMachineOp.PushNumber, number: 99);
            Assert.That(copied.Instructions[0].Number, Is.EqualTo(1));
            Assert.That(copied.Instructions as StackMachineInstruction[], Is.Null);
            Assert.That(builder.TryPatchTarget(call, 2, out diagnostic), Is.False);
            Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.InvalidControlFlow));
        }

        [TestCase("2 2 =", true)]
        [TestCase("2 3 =", false)]
        public void Equality_UsesTaggedScalarValues(string source, bool expected)
        {
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(Compile(source));
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            Assert.That(result.DataStack[0].Boolean, Is.EqualTo(expected));
        }

        [Test]
        public void BindingLiteral_IsValidatedButNotResolvedByPureStub()
        {
            var recipe = new StackMachineRecipeDocument { wordSource = "$figure" };
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "figure" });
            Assert.That(StackMachineCompiler.TryCompile(recipe, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(plan);
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Failed));
            Assert.That(result.Diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.InvalidBinding));
        }

        [Test]
        public void BindingLiteral_ContributesItsDeclaredResourceHandleStackEffect()
        {
            var recipe = new StackMachineRecipeDocument { wordSource = "$figure DUP DROP" };
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "figure", declaredKind = StackMachineBindingKind.Resource });
            Assert.That(StackMachineCompiler.TryCompile(recipe, out _, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [TestCase("+", StackMachineDiagnosticCode.StackUnderflow)]
        [TestCase("TRUE 1 +", StackMachineDiagnosticCode.TypeMismatch)]
        public void BuiltInSignatures_FailAtCompileTime(string source, StackMachineDiagnosticCode code)
        {
            Assert.That(StackMachineCompiler.TryCompile(new StackMachineRecipeDocument { wordSource = source }, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.code, Is.EqualTo(code));
        }

        [TestCase("2 3 +", "5")]
        [TestCase("8 3 -", "5")]
        [TestCase("2 3 *", "6")]
        [TestCase("8 2 /", "4")]
        [TestCase("-2 ABS", "2")]
        [TestCase("2 NEGATE", "-2")]
        [TestCase("3 5 MIN", "3")]
        [TestCase("3 5 MAX", "5")]
        [TestCase("1 1+ 2*", "4")]
        [TestCase("8 2/ 1-", "3")]
        [TestCase("2 3 <", "True")]
        [TestCase("2 3 >", "False")]
        [TestCase("0 0=", "True")]
        [TestCase("-1 0<", "True")]
        [TestCase("TRUE FALSE OR", "True")]
        [TestCase("TRUE FALSE AND", "False")]
        [TestCase("TRUE FALSE XOR", "True")]
        [TestCase("FALSE NOT", "True")]
        public void PureBuiltIns_ExecuteExpectedTopValue(string source, string expected)
        {
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(Compile(source));
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            Assert.That(result.DataStack[result.DataStack.Count - 1].ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void TransactionLifecycle_RecordsStartAndSuccess()
        {
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(Compile("1 2 +"));
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Succeeded }));
        }

        [Test]
        public void TransactionLifecycle_RecordsStartAndFailure()
        {
            var plan = new StackMachinePlan(new[] { new StackMachineInstruction(StackMachineOp.PushNumber, number: 1), new StackMachineInstruction(StackMachineOp.Divide) });
            StackMachineExecutionResult result = new StackMachineStubMachine().Execute(plan);
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Failed }));
            Assert.That(result.Diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.StackUnderflow));
        }

        [Test]
        public void TransactionResult_CanRecordDomainRollbackCompletion()
        {
            var result = new StackMachineExecutionResult { Stage = StackMachineTransactionStage.Failed };
            result.Lifecycle.Add(StackMachineTransactionStage.Started); result.Lifecycle.Add(StackMachineTransactionStage.Failed);
            result.MarkRollbackCompleted();
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Failed, StackMachineTransactionStage.RollbackCompleted }));
            Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.RollbackCompleted));
        }

        [Test]
        public void RecipeSerialization_PreservesMetadata()
        {
            var recipe = new StackMachineRecipeDocument { wordSource = "1", provenance = new StackMachineProvenance { author = "test", note = "metadata" } };
            recipe.capabilities.Add("core-1"); recipe.diagnosticSourceMap.Add(new StackMachineSourceMapEntry { tokenIndex = 0, sourceOffset = 0 });
            Assert.That(StackMachineRecipeSerialization.TrySerializeJson(recipe, out string json, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(StackMachineRecipeSerialization.TryDeserializeJson(json, out StackMachineRecipeDocument parsed, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(parsed.provenance.author, Is.EqualTo("test")); Assert.That(parsed.capabilities[0], Is.EqualTo("core-1")); Assert.That(parsed.diagnosticSourceMap[0].tokenIndex, Is.EqualTo(0));
        }

        [Test]
        public void RecipeSerialization_WritesAndReadsCarrierWithoutMutatingOnInvalidDocument()
        {
            var carrier = UnityEngine.ScriptableObject.CreateInstance<StackMachineRecipeAsset>();
            try
            {
                var valid = new StackMachineRecipeDocument { wordSource = "1" };
                Assert.That(StackMachineRecipeSerialization.TryWriteAsset(valid, carrier, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(StackMachineRecipeSerialization.TryDeserializeAsset(carrier, out StackMachineRecipeDocument loaded, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(loaded.wordSource, Is.EqualTo("1"));
                var invalid = new StackMachineRecipeDocument { recipeFormatVersion = 99, wordSource = "2" };
                Assert.That(StackMachineRecipeSerialization.TryWriteAsset(invalid, carrier, out diagnostic), Is.False);
                Assert.That(carrier.ToDocument().wordSource, Is.EqualTo("1"));
            }
            finally { UnityEngine.Object.DestroyImmediate(carrier); }
        }

        [Test]
        public void RecipeSerialization_RoundTripsJsonAndCarrier()
        {
            var recipe = new StackMachineRecipeDocument { wordSource = "1 2 +" };
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "figure", declaredKind = StackMachineBindingKind.Resource });
            Assert.That(StackMachineRecipeSerialization.TrySerializeJson(recipe, out string json, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(StackMachineRecipeSerialization.TryDeserializeJson(json, out StackMachineRecipeDocument parsed, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(parsed.wordSource, Is.EqualTo(recipe.wordSource));
            var carrier = UnityEngine.ScriptableObject.CreateInstance<StackMachineRecipeAsset>();
            try { carrier.SetDocument(parsed); Assert.That(carrier.ToDocument().bindings[0].logicalName, Is.EqualTo("figure")); }
            finally { UnityEngine.Object.DestroyImmediate(carrier); }
        }

        [Test]
        public void RecipeSerialization_UsesDeepCopiesAndBuildsRuntimeBindingLookup()
        {
            var source = new StackMachineRecipeDocument { wordSource = "$figure", provenance = new StackMachineProvenance { author = "author" } };
            source.bindings.Add(new StackMachineBindingDeclaration { logicalName = "figure", declaredKind = StackMachineBindingKind.Resource });
            source.diagnosticSourceMap.Add(new StackMachineSourceMapEntry { tokenIndex = 0, sourceOffset = 2 });
            var carrier = UnityEngine.ScriptableObject.CreateInstance<StackMachineRecipeAsset>();
            try
            {
                carrier.SetDocument(source);
                source.bindings[0].logicalName = "mutated";
                source.provenance.author = "mutated";
                StackMachineRecipeDocument stored = carrier.ToDocument();
                Assert.That(stored.bindings[0].logicalName, Is.EqualTo("figure"));
                Assert.That(stored.provenance.author, Is.EqualTo("author"));
                Assert.That(StackMachineBindingLookup.TryCreate(stored, out StackMachineBindingLookup lookup, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(lookup.TryGet("figure", out StackMachineBindingDeclaration declaration), Is.True);
                Assert.That(declaration.declaredKind, Is.EqualTo(StackMachineBindingKind.Resource));
            }
            finally { UnityEngine.Object.DestroyImmediate(carrier); }
        }

        [Test]
        public void InvalidRecipe_DoesNotMutateCarrier()
        {
            var carrier = UnityEngine.ScriptableObject.CreateInstance<StackMachineRecipeAsset>();
            try
            {
                carrier.SetDocument(new StackMachineRecipeDocument { wordSource = "1 2 +" });
                string before = carrier.ToDocument().wordSource;
                Assert.That(StackMachineRecipeSerialization.TryDeserializeJson("{", out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.MalformedDocument));
                Assert.That(carrier.ToDocument().wordSource, Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(carrier); }
        }

        [Test]
        public void DomainExecutorBase_ExposesCompileReuseLifecycleAndRollbackHooks()
        {
            var executor = new HookedExecutor();
            var context = new FakeBindingContext();
            StackMachinePlan commonPlan = Compile("1");
            Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(executor.CanReusePlan(commonPlan, context, domainPlan), Is.True);
            Assert.That(executor.RequiresReevaluation(commonPlan, context, domainPlan), Is.False);
            Assert.That(executor.TryExecute(domainPlan, context, out StackMachineExecutionResult result), Is.False);
            Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Failed, StackMachineTransactionStage.RollbackCompleted }));
            Assert.That(executor.Started && executor.Failed && executor.RolledBack, Is.True);
        }

        [Test]
        public void DomainDiagnostic_PreservesStructuredDomainContext()
        {
            StackMachineDiagnostic diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "TopologyMismatch", "Topology differs.", tokenIndex: 4, instructionPointer: 7, wordId: "mesh.attach", bindingName: "outfitMesh", detail: "expected=120; actual=98");
            Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.DomainFailure));
            Assert.That(diagnostic.domainCode, Is.EqualTo("TopologyMismatch"));
            Assert.That(diagnostic.bindingName, Is.EqualTo("outfitMesh"));
        }

        [Test]
        public void DerivedRecipeDocument_UsesTheCommonJsonAndValidationEntryPoint()
        {
            var source = new DomainValidatedRecipeDocument { wordSource = "1", domainRequiredValue = "mesh" };
            Assert.That(StackMachineRecipeSerialization.TrySerializeJson(source, out string json, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(StackMachineRecipeSerialization.TryDeserializeJson<DomainValidatedRecipeDocument>(json, out DomainValidatedRecipeDocument loaded, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(loaded.domainRequiredValue, Is.EqualTo("mesh"));
            source.domainRequiredValue = null;
            Assert.That(StackMachineRecipeSerialization.TrySerializeJson(source, out _, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MissingDomainValue"));
        }

        private static StackMachinePlan Compile(string source)
        {
            Assert.That(StackMachineCompiler.TryCompile(new StackMachineRecipeDocument { wordSource = source }, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return plan;
        }

        private sealed class FakePlan : IStackMachineDomainPlan
        {
            public StackMachinePlan CommonPlan { get; }
            public FakePlan(StackMachinePlan commonPlan) { CommonPlan = commonPlan; }
        }

        private sealed class FakeExecutor : StackMachineDomainExecutorBase
        {
            public override StackMachineDomainExecutionMode ExecutionMode { get; }
            public StackMachinePlan ReceivedPlan { get; private set; }
            public FakeExecutor(StackMachineDomainExecutionMode mode) { ExecutionMode = mode; }
            public override bool TryCompileDomainPlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic) { ReceivedPlan = plan; domainPlan = new FakePlan(plan); diagnostic = null; return true; }
            protected override bool TryExecuteDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
        }
        private sealed class FakeBindingContext : IStackMachineBindingContext { }

        private sealed class HookedExecutor : StackMachineDomainExecutorBase
        {
            public bool Started { get; private set; } public bool Failed { get; private set; } public bool RolledBack { get; private set; }
            public override StackMachineDomainExecutionMode ExecutionMode => StackMachineDomainExecutionMode.FullyCompiled;
            public override bool CanReusePlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, IStackMachineDomainPlan cachedPlan) => cachedPlan != null && ReferenceEquals(plan, cachedPlan.CommonPlan);
            public override bool TryCompileDomainPlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic) { domainPlan = new FakePlan(plan); diagnostic = null; return true; }
            protected override bool TryExecuteDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("test", "ExpectedFailure", "Expected."); return false; }
            protected override void OnStarted(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { Started = true; }
            protected override void OnFailed(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { Failed = true; }
            protected override void OnRollbackCompleted(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result) { RolledBack = true; }
        }

        private sealed class TestWordSet : StackMachineWordSetBase
        {
            public bool Evaluated { get; private set; }
            protected override void RegisterWords(StackMachineWordRegistry registry) { Assert.That(registry.TryRegisterDomainWord("TEST-WORD", "test.word", new StackMachineWordSignature(null, null)), Is.True); }
            public override bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext context, out StackMachineDiagnostic diagnostic) { diagnostic = null; Evaluated = instruction.Op == StackMachineOp.DomainWord && instruction.WordId == "test.word"; return Evaluated; }
        }
        private sealed class RejectingWordSet : StackMachineWordSetBase
        {
            protected override void RegisterWords(StackMachineWordRegistry registry) { registry.TryRegisterDomainWord("REJECT", "test.reject", new StackMachineWordSignature(null, null)); }
            public override bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext context, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch, "Rejected by domain evaluator."); return false; }
        }
        private sealed class NumberToBooleanWordSet : StackMachineWordSetBase
        {
            protected override void RegisterWords(StackMachineWordRegistry registry) { registry.TryRegisterDomainWord("NUMBER-TO-BOOLEAN", "test.number-to-boolean", new StackMachineWordSignature(new[] { StackMachineValueTag.Number }, new[] { StackMachineValueTag.Boolean })); }
            public override bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext context, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
        }
        [System.Serializable]
        private sealed class DomainValidatedRecipeDocument : StackMachineRecipeDocument
        {
            public string domainRequiredValue;
            public override bool TryValidateDomain(out StackMachineDiagnostic diagnostic)
            {
                diagnostic = string.IsNullOrWhiteSpace(domainRequiredValue) ? StackMachineDiagnostic.CreateDomain("test", "MissingDomainValue", "Domain field is required.") : null;
                return diagnostic == null;
            }
        }
    }
}
