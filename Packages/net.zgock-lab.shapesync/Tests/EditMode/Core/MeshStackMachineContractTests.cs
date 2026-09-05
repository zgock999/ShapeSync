// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using InvalidOperationException = System.InvalidOperationException;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MeshStackMachineContractTests
    {
        [Test]
        public void MeshWords_CompileWithApprovedTokensAndSignatures()
        {
            var registry = new StackMachineWordRegistry();
            new MeshWordSet().RegisterInto(registry);
            var recipe = new MeshRecipeDocument
            {
                wordSource = "$hair_long DETACH $hair_short ATTACH $basicgirl 0.3 FBM_SET $basicmale 0.2 FBM_SET MORPH_RESET DETACH_ALL"
            };
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "hair_long", declaredKind = StackMachineBindingKind.Resource });
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "hair_short", declaredKind = StackMachineBindingKind.Resource });
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "basicgirl", declaredKind = StackMachineBindingKind.Resource });
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "basicmale", declaredKind = StackMachineBindingKind.Resource });

            Assert.That(StackMachineCompiler.TryCompile(recipe, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Instructions[1].WordId, Is.EqualTo(MeshWordSet.OutfitDetach));
            Assert.That(plan.Instructions[3].WordId, Is.EqualTo(MeshWordSet.OutfitAttach));
            Assert.That(plan.Instructions[6].Signature.Inputs, Is.EqualTo(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number }));
            Assert.That(plan.Instructions[10].WordId, Is.EqualTo(MeshWordSet.MorphReset));
            Assert.That(plan.Instructions[11].WordId, Is.EqualTo(MeshWordSet.DetachAll));
        }

        [TestCase("$outfit OUTFIT_ATTACH")]
        [TestCase("$outfit OUTFIT_DETACH")]
        public void MeshWords_RejectsRemovedLongOutfitTokens(string source)
        {
            var registry = new StackMachineWordRegistry();
            new MeshWordSet().RegisterInto(registry);
            var recipe = new MeshRecipeDocument { wordSource = source };
            recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "outfit", declaredKind = StackMachineBindingKind.Resource });

            Assert.That(StackMachineCompiler.TryCompile(recipe, registry, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.code, Is.EqualTo(StackMachineDiagnosticCode.UnknownToken));
        }

        [Test]
        public void MeshNormalBlockParser_ExtractsTemplateAndPreservesOuterMeshSource()
        {
            Assert.That(MeshNormalBlockParser.TryExtract("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $basic 0.5 FBM_SET", out string outer, out System.Collections.Generic.IReadOnlyList<NormalRecipeTemplate> templates, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(outer, Is.EqualTo("$basic 0.5 FBM_SET"));
            Assert.That(templates.Count, Is.EqualTo(1));
            Assert.That(templates[0].EntryName, Is.EqualTo("face"));
            Assert.That(templates[0].WordSource, Is.EqualTo("$base CANVAS NORMAL_BASE NORMAL_FINALIZE"));
        }

        [Test]
        public void MeshNormalBlockParser_RejectsNestedAndDuplicateBindings()
        {
            Assert.That(MeshNormalBlockParser.TryExtract("$face NORMAL $other NORMAL ENDNORMAL ENDNORMAL", out _, out _, out StackMachineDiagnostic nested), Is.False);
            Assert.That(nested.domainCode, Is.EqualTo("NormalBlockInvalid"));
            Assert.That(MeshNormalBlockParser.TryExtract("$face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $face NORMAL $base NORMAL_BASE NORMAL_FINALIZE ENDNORMAL", out _, out _, out StackMachineDiagnostic duplicate), Is.False);
            Assert.That(duplicate.domainCode, Is.EqualTo("NormalBlockInvalid"));
        }

        [Test]
        public void MeshRecipeAsset_DeepCopiesCommonDocumentFields()
        {
            MeshRecipeAsset asset = ScriptableObject.CreateInstance<MeshRecipeAsset>();
            try
            {
                var source = new MeshRecipeDocument { wordSource = "$morph 0.25 FBM_SET" };
                source.bindings.Add(new StackMachineBindingDeclaration { logicalName = "morph", declaredKind = StackMachineBindingKind.Resource });
                asset.SetDocument(source);
                source.bindings[0].logicalName = "changed";

                MeshRecipeDocument stored = asset.ToDocument() as MeshRecipeDocument;
                Assert.That(stored, Is.Not.Null);
                Assert.That(stored.bindings[0].logicalName, Is.EqualTo("morph"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void MeshExecutor_StagesAndCommitsFbmWeightAfterBytecodeSucceeds()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("basicgirl", null, null);
                ConfigureMorph(template, "basicgirl", "basicgirl");
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);

                StackMachinePlan commonPlan = Compile("$basicgirl 0.3 FBM_SET", "basicgirl");
                var executor = new MeshExecutor();
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0f));
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.True, result.Diagnostic?.message);
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0.3f));
                Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshExecutor_MorphResetStagesEveryDdbTargetAndRespectsRecipeOrder()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("basicgirl", null, null);
                blender.EnsureTarget("PBM_BreastSize", null, null);
                blender.Targets[0].weight = 0.8f;
                blender.Targets[1].weight = 0.6f;
                ConfigureMorph(template, "basicgirl", "basicgirl");
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);

                StackMachinePlan commonPlan = Compile("MORPH_RESET $basicgirl 0.25 FBM_SET", "basicgirl");
                var executor = new MeshExecutor();
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.True, result.Diagnostic?.message);
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0.25f));
                Assert.That(blender.Targets[1].weight, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshExecutor_DetachAllIsNoOpForAnEmptyBeginSnapshot()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
                StackMachinePlan commonPlan = Compile("DETACH_ALL DETACH_ALL");
                var executor = new MeshExecutor();
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.True, result.Diagnostic?.message);
                Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
                Assert.That(attacher.AttachedOutfits, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshExecutor_CommitFailureDoesNotReportRollbackCompleted()
        {
            CreateOutfitExecutionFixture(out GameObject figure, out GameObject outfitRoot, out MeshBindingTemplate template, out MeshBindingContext context, out StackMachinePlan commonPlan);
            try
            {
                var executor = new MeshExecutor(_ => OutfitAttacherDryRunResult.Success(), _ => false);
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.False);
                Assert.That(result.Diagnostic.domainCode, Is.EqualTo("CommitAttachFailed"));
                Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Failed));
                Assert.That(result.Lifecycle, Is.EqualTo(new[] { StackMachineTransactionStage.Started, StackMachineTransactionStage.Failed }));
            }
            finally { Object.DestroyImmediate(template); Object.DestroyImmediate(outfitRoot); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void MeshExecutor_CommitExceptionReturnsStructuredDiagnostic()
        {
            CreateOutfitExecutionFixture(out GameObject figure, out GameObject outfitRoot, out MeshBindingTemplate template, out MeshBindingContext context, out StackMachinePlan commonPlan);
            try
            {
                var executor = new MeshExecutor(_ => OutfitAttacherDryRunResult.Success(), _ => throw new InvalidOperationException("injected"));
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.False);
                Assert.That(result.Diagnostic.domainCode, Is.EqualTo("CommitUnexpectedFailure"));
                Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Failed));
            }
            finally { Object.DestroyImmediate(template); Object.DestroyImmediate(outfitRoot); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void OutfitDryRunPcmPreflightRejectsLegacyPcmWithoutFigureMesh()
        {
            var figure = new GameObject("Figure");
            var outfitRoot = new GameObject("Outfit");
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var serialized = new SerializedObject(outfit);
                serialized.FindProperty("profileControlledMorphEnabled").boolValue = true;
                serialized.FindProperty("profileControlledMorphOutfitName").stringValue = "test";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(attacher.TryValidateProfileControlledMorphAttach(outfit, out string error), Is.False);
                Assert.That(error, Does.Contain("PCM"));
            }
            finally { Object.DestroyImmediate(outfitRoot); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void OutfitDryRunVrmPreflightRejectsIntegrationWithoutReadOnlyCapability()
        {
            var figure = new GameObject("Figure");
            var outfitRoot = new GameObject("Outfit");
            var integration = new LegacyOptionalVrmIntegration();
            try
            {
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                ShapeSyncOptionalVrmIntegrationRegistry.Register(figure, integration);

                Assert.That(attacher.TryValidateOptionalVrmAttach(outfit, out string error), Is.False);
                Assert.That(error, Does.Contain("does not support read-only"));
            }
            finally
            {
                ShapeSyncOptionalVrmIntegrationRegistry.Unregister(figure, integration);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshExecutor_RejectsDuplicateMorphBeforeExecution()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("basicgirl", null, null);
                ConfigureMorph(template, "basicgirl", "basicgirl");
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);

                StackMachinePlan commonPlan = Compile("$basicgirl 0.3 FBM_SET $basicgirl 0.2 FBM_SET", "basicgirl");
                Assert.That(new MeshExecutor().TryCompileDomainPlan(commonPlan, context, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateMorph"));
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshExecutor_DryRunFailureDiscardsEarlierStagedMorph()
        {
            var figure = new GameObject("Figure");
            var outfitRoot = new GameObject("Outfit");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("basicgirl", null, null);
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                var outfitSerialized = new SerializedObject(outfit); outfitSerialized.FindProperty("registryId").stringValue = "outfit.test"; outfitSerialized.ApplyModifiedPropertiesWithoutUndo();
                ConfigureMorph(template, "basicgirl", "basicgirl");
                ConfigureOutfit(template, "test_outfit", outfitRoot);
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);

                StackMachinePlan commonPlan = Compile("$basicgirl 0.3 FBM_SET $test_outfit ATTACH", "basicgirl", "test_outfit");
                var executor = new MeshExecutor();
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.False);
                Assert.That(result.Diagnostic.domainCode, Is.EqualTo("OutfitDryRunRejected"));
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshExecutor_RuntimeFailureDiscardsStagedMorphAndCompletesRollbackLifecycle()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("basicgirl", null, null);
                ConfigureMorph(template, "basicgirl", "basicgirl");
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);

                StackMachinePlan commonPlan = Compile("$basicgirl 0.3 FBM_SET 1 0 /", "basicgirl");
                var executor = new MeshExecutor();
                Assert.That(executor.TryCompileDomainPlan(commonPlan, context, out IStackMachineDomainPlan meshPlan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(executor.TryExecute(meshPlan, context, out StackMachineExecutionResult result), Is.False);
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0f));
                Assert.That(result.Lifecycle, Is.EqualTo(new[]
                {
                    StackMachineTransactionStage.Started,
                    StackMachineTransactionStage.Failed,
                    StackMachineTransactionStage.RollbackCompleted
                }));
                Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.RollbackCompleted));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void OutfitDryRun_DetachOfMissingRegistryReturnsStructuredFailureWithoutMutation()
        {
            var figure = new GameObject("Figure");
            try
            {
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                var commands = new[] { OutfitAttacherDryRunCommand.ForDetach("missing", "hair_long") };

                Assert.That(attacher.TryDryRun(commands, out OutfitAttacherDryRunResult result), Is.False);
                Assert.That(result.Code, Is.EqualTo("RegistryNotAttached"));
                Assert.That(result.CommandIndex, Is.EqualTo(0));
                Assert.That(result.LogicalBinding, Is.EqualTo("hair_long"));
                Assert.That(attacher.AttachedOutfits, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshBindingContext_RejectsComponentsFromDifferentFigures()
        {
            var first = new GameObject("First Figure");
            var second = new GameObject("Second Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                Assert.That(MeshBindingContext.TryCreate(template, first.AddComponent<DynamicBoneBlender>(), second.AddComponent<OutfitAttacher>(), out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TemplateInvalid"));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void MeshBindingContext_ResolvesFigureLocalNormalEntryAndRejectsDuplicateEntry()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MaterialProxy>();
                figure.AddComponent<MeshStackMachine>();
                NormalBlender normalBlender = figure.AddComponent<NormalBlender>();
                SetPrivate(normalBlender, "entries", new List<string> { "face" });

                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(context.TryGetHandle("face", out int handle), Is.True);
                Assert.That(context.TryGetNormal(handle, out MeshBindingContext.NormalEntry resolved), Is.True);
                Assert.That(resolved.LogicalName, Is.EqualTo("face"));

                SetPrivate(normalBlender, "entries", new List<string> { "face", "face" });
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("NormalEntryInvalid"));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshBindingContext_UsesOnlyNormalBlenderEntryList()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<NormalBlender>();
                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(context.TryGetHandle("face", out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshBindingContext_ResolvesNormalEntryWithoutTemplateComponentReference()
        {
            var figure = new GameObject("Figure Instance");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MaterialProxy>();
                figure.AddComponent<MeshStackMachine>();
                NormalBlender normalBlender = figure.AddComponent<NormalBlender>();
                SetPrivate(normalBlender, "entries", new List<string> { "face" });

                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(context.TryGetHandle("face", out int handle), Is.True);
                Assert.That(context.TryGetNormal(handle, out MeshBindingContext.NormalEntry resolved), Is.True);
                Assert.That(resolved.LogicalName, Is.EqualTo("face"));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshBindingContext_AllowsMultipleFigureLocalNormalEntriesWithoutCompanionComponents()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MaterialProxy>();
                figure.AddComponent<MeshStackMachine>();
                NormalBlender normalBlender = figure.AddComponent<NormalBlender>();
                SetPrivate(normalBlender, "entries", new List<string> { "face", "body" });

                Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(context.TryGetHandle("face", out int faceHandle), Is.True);
                Assert.That(context.TryGetHandle("body", out int bodyHandle), Is.True);
                Assert.That(context.TryGetNormal(faceHandle, out MeshBindingContext.NormalEntry faceResolved), Is.True);
                Assert.That(context.TryGetNormal(bodyHandle, out MeshBindingContext.NormalEntry bodyResolved), Is.True);
                Assert.That(faceResolved.LogicalName, Is.EqualTo("face"));
                Assert.That(bodyResolved.LogicalName, Is.EqualTo("body"));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshStackMachine_ExpandsTargetUnitNormalTextureDictionaryForRequestedBinding()
        {
            var figure = new GameObject("Figure");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            var baseTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var targetTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MaterialProxy>();
                NormalBlender normalBlender = figure.AddComponent<NormalBlender>();
                SetPrivate(normalBlender, "entries", new List<string> { "face" });
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                SetPrivate(machine, "bindingTemplate", template);
                SerializedObject serialized = new SerializedObject(template);
                SerializedProperty targets = serialized.FindProperty("normalTargetTextures");
                targets.arraySize = 2;
                SerializedProperty baseEntry = targets.GetArrayElementAtIndex(0);
                baseEntry.FindPropertyRelative("targetName").stringValue = string.Empty;
                SerializedProperty baseTextures = baseEntry.FindPropertyRelative("textures");
                baseTextures.arraySize = 1;
                baseTextures.GetArrayElementAtIndex(0).FindPropertyRelative("entryName").stringValue = "face";
                baseTextures.GetArrayElementAtIndex(0).FindPropertyRelative("texture").objectReferenceValue = baseTexture;
                SerializedProperty target = targets.GetArrayElementAtIndex(1);
                target.FindPropertyRelative("targetName").stringValue = "BasicGirl";
                SerializedProperty textures = target.FindPropertyRelative("textures");
                textures.arraySize = 1;
                textures.GetArrayElementAtIndex(0).FindPropertyRelative("entryName").stringValue = "face";
                textures.GetArrayElementAtIndex(0).FindPropertyRelative("texture").objectReferenceValue = targetTexture;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Invoke(machine, "Awake");
                Invoke(machine, "Start");

                Assert.That(machine.TryExecute("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL", out StackMachineExecutionResult register), Is.True, register.Diagnostic?.message);
                var snapshot = new[] { new NormalTargetWeight("BasicGirl", .5f, true) };
                Assert.That(machine.TryBuildNormalRecipe("face", snapshot, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(stub.Document.wordSource, Does.Contain("NORMAL_DELTA_ADD"));
                Assert.That(stub.Bindings, Has.Some.Matches<TextureBindingEntry>(x => x.logicalName == "target0" && x.sourceTexture == targetTexture));

                baseEntry.FindPropertyRelative("targetName").stringValue = "NotBase";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(machine.TryBuildNormalRecipe("face", snapshot, out _, out StackMachineDiagnostic missingBase), Is.False);
                Assert.That(missingBase.domainCode, Is.EqualTo("NormalBaseTextureMissing"));
            }
            finally
            {
                Object.DestroyImmediate(targetTexture);
                Object.DestroyImmediate(baseTexture);
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        private static StackMachinePlan Compile(string source, params string[] bindings)
        {
            var registry = new StackMachineWordRegistry();
            new MeshWordSet().RegisterInto(registry);
            var recipe = new MeshRecipeDocument { wordSource = source };
            for (int i = 0; i < bindings.Length; i++) recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = bindings[i], declaredKind = StackMachineBindingKind.Resource });
            Assert.That(StackMachineCompiler.TryCompile(recipe, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return plan;
        }

        private static void SetPrivate(object target, string name, object value) => target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(target, null);

        private static void CreateOutfitExecutionFixture(out GameObject figure, out GameObject outfitRoot, out MeshBindingTemplate template, out MeshBindingContext context, out StackMachinePlan commonPlan)
        {
            figure = new GameObject("Figure");
            outfitRoot = new GameObject("Outfit");
            template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
            OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
            ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
            var serialized = new SerializedObject(outfit);
            serialized.FindProperty("registryId").stringValue = "outfit.test";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ConfigureOutfit(template, "test_outfit", outfitRoot);
            Assert.That(MeshBindingContext.TryCreate(template, blender, attacher, out context, out StackMachineDiagnostic contextDiagnostic), Is.True, contextDiagnostic?.message);
            commonPlan = Compile("$test_outfit ATTACH", "test_outfit");
        }

        private sealed class LegacyOptionalVrmIntegration : IShapeSyncOptionalVrmIntegration
        {
            public bool TryAttachOutfitPhysics(ShapeSyncOptionalVrmAttachRequest request, out IShapeSyncOptionalVrmAttachment attachment, out string error) { attachment = null; error = null; return true; }
            public bool TrySetExpressionWeight(string expressionName, float weight) => true;
        }

        private static void ConfigureMorph(MeshBindingTemplate template, string word, string name)
        {
            var serialized = new SerializedObject(template);
            SerializedProperty morphs = serialized.FindProperty("morphs");
            morphs.arraySize = 1;
            morphs.GetArrayElementAtIndex(0).FindPropertyRelative("word").stringValue = word;
            morphs.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = name;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureOutfit(MeshBindingTemplate template, string word, GameObject outfitRoot)
        {
            var serialized = new SerializedObject(template);
            SerializedProperty outfits = serialized.FindProperty("outfits");
            outfits.arraySize = 1;
            outfits.GetArrayElementAtIndex(0).FindPropertyRelative("word").stringValue = word;
            outfits.GetArrayElementAtIndex(0).FindPropertyRelative("obj").objectReferenceValue = outfitRoot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

    }
}
