// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MeshStackMachineCorePlanTests
    {
        [TestCase("$body 2 3 + FBM_SET", 5f)]
        [TestCase("$body 2 3 - FBM_SET", -1f)]
        [TestCase("$body 2 3 * FBM_SET", 6f)]
        [TestCase("$body 6 3 / FBM_SET", 2f)]
        [TestCase("$body 2 3 MIN FBM_SET", 2f)]
        [TestCase("$body 2 3 MAX FBM_SET", 3f)]
        [TestCase("$body 2 NEGATE FBM_SET", -2f)]
        [TestCase("$body -2 ABS FBM_SET", 2f)]
        [TestCase("$body 2 1+ FBM_SET", 3f)]
        [TestCase("$body 2 1- FBM_SET", 1f)]
        [TestCase("$body 2 2* FBM_SET", 4f)]
        [TestCase("$body 2 2/ FBM_SET", 1f)]
        [TestCase("$body 2 3 DEPTH DROP + FBM_SET", 5f)]
        [TestCase("$body 2 DUP + FBM_SET", 4f)]
        [TestCase("$body 2 3 SWAP - FBM_SET", 1f)]
        [TestCase("$body 2 3 OVER + + FBM_SET", 7f)]
        [TestCase("$body 2 3 4 ROT - - FBM_SET", 1f)]
        [TestCase("TRUE FALSE AND DROP $body 1 FBM_SET", 1f)]
        [TestCase("TRUE FALSE OR DROP $body 1 FBM_SET", 1f)]
        [TestCase("TRUE FALSE XOR DROP $body 1 FBM_SET", 1f)]
        [TestCase("TRUE NOT DROP $body 1 FBM_SET", 1f)]
        [TestCase("2 2 = DROP $body 1 FBM_SET", 1f)]
        [TestCase("2 3 < DROP $body 1 FBM_SET", 1f)]
        [TestCase("3 2 > DROP $body 1 FBM_SET", 1f)]
        [TestCase("0 0= DROP $body 1 FBM_SET", 1f)]
        [TestCase("-1 0< DROP $body 1 FBM_SET", 1f)]
        public void TryCreate_EvaluatesEverySupportedCoreBuiltInBeforeFbmSet(string source, float expectedWeight)
        {
            MeshCoreBinding[] bindings = { MeshCoreBinding.Morph("body", "Body") };

            Assert.That(MeshStackMachineCorePlan.TryCreate(Document(source, "body"), bindings, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Operations, Has.Count.EqualTo(1));
            Assert.That(plan.Operations[0].Weight, Is.EqualTo(expectedWeight).Within(0.00001f));
        }

        [Test]
        public void TryCreate_LowersControlWordsAndDetachedAttachWithoutPhysicalExecution()
        {
            MeshRecipeDocument document = Document("MORPH_RESET $old DETACH DETACH_ALL $hair ATTACH", "old", "hair");
            MeshCoreBinding[] bindings = { MeshCoreBinding.Outfit("old", "outfit.old"), MeshCoreBinding.Outfit("hair", "outfit.hair", true, true) };

            Assert.That(MeshStackMachineCorePlan.TryCreate(document, bindings, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Operations, Has.Count.EqualTo(4));
            Assert.That(plan.Operations[0].Kind, Is.EqualTo(MeshCoreOperationKind.MorphReset));
            Assert.That(plan.Operations[1].Kind, Is.EqualTo(MeshCoreOperationKind.Detach));
            Assert.That(plan.Operations[2].Kind, Is.EqualTo(MeshCoreOperationKind.DetachAll));
            Assert.That(plan.Operations[3].Kind, Is.EqualTo(MeshCoreOperationKind.AttachOutfit));
            Assert.That(plan.Operations[3].LogicalName, Is.EqualTo("hair"));
            Assert.That(plan.Operations[3].RegistryId, Is.EqualTo("outfit.hair"));
            Assert.That(plan.Operations[3].RegistersPcmSource, Is.True);
            Assert.That(plan.Operations[3].RegistersBcpSource, Is.True);
        }

        [Test]
        public void TryCreate_EvaluatesFbmWeightAndRejectsDuplicateMorphBeforeExecution()
        {
            MeshRecipeDocument valid = Document("$body 0.2 0.1 + FBM_SET", "body");
            MeshCoreBinding[] bindings = { MeshCoreBinding.Morph("body", "Body") };

            Assert.That(MeshStackMachineCorePlan.TryCreate(valid, bindings, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic validDiagnostic), Is.True, validDiagnostic?.message);
            Assert.That(plan.Operations, Has.Count.EqualTo(1));
            Assert.That(plan.Operations[0].Kind, Is.EqualTo(MeshCoreOperationKind.SetMorph));
            Assert.That(plan.Operations[0].TargetName, Is.EqualTo("Body"));
            Assert.That(plan.Operations[0].Weight, Is.EqualTo(0.3f).Within(0.00001f));

            MeshRecipeDocument duplicate = Document("$body 0.3 FBM_SET $body 0.2 FBM_SET", "body");
            Assert.That(MeshStackMachineCorePlan.TryCreate(duplicate, bindings, out _, out StackMachineDiagnostic duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic.domainCode, Is.EqualTo("DuplicateMorph"));
        }

        [Test]
        public void TryCompile_UsesSharedMeshWordSetWithoutPhysicalState()
        {
            Assert.That(MeshStackMachineCorePlan.TryCompile(Document("$body 0.25 FBM_SET", "body"), out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Instructions, Has.Count.EqualTo(3));
            Assert.That(plan.Instructions[2].WordId, Is.EqualTo(MeshWordSet.FbmSet));
        }

        [Test]
        public void TryCreateRuntime_DoesNotRunCompilerOnlyLogicalLower()
        {
            MeshRecipeDocument document = Document("$outfit 0 FBM_SET", "outfit");

            Assert.That(MeshStackMachineCorePlan.TryCreateRuntime(document, out MeshStackMachineCorePlan runtimePlan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(runtimePlan.Operations, Is.Empty);
            Assert.That(runtimePlan.CommonPlan.Instructions[2].WordId, Is.EqualTo(MeshWordSet.FbmSet));
        }

        [Test]
        public void TryCreate_AcceptsLegacyEmptyRecipeDeclarationsAndUnusedDetachedBindings()
        {
            var document = new MeshRecipeDocument { wordSource = "$body 0.25 FBM_SET $hair ATTACH" };
            MeshCoreBinding[] bindings =
            {
                MeshCoreBinding.Morph("body", "Body"),
                MeshCoreBinding.Outfit("hair", "outfit.hair"),
                MeshCoreBinding.Morph("legacy-unused", "LegacyUnused")
            };

            Assert.That(document.bindings, Is.Empty);
            Assert.That(MeshStackMachineCorePlan.TryCreate(document, bindings, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Operations, Has.Count.EqualTo(2));
            Assert.That(plan.Operations[0].LogicalName, Is.EqualTo("body"));
            Assert.That(plan.Operations[1].LogicalName, Is.EqualTo("hair"));
        }

        [Test]
        public void TryCreate_ExtractsNormalTemplateAndRequiresDetachedNormalBinding()
        {
            MeshRecipeDocument document = Document("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $body 0.25 FBM_SET", "body");
            MeshCoreBinding[] bindings = { MeshCoreBinding.Normal("face"), MeshCoreBinding.Morph("body", "Body") };

            Assert.That(MeshStackMachineCorePlan.TryCreate(document, bindings, out MeshStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.CommonPlan.Instructions, Has.Count.EqualTo(3));
            Assert.That(plan.NormalTemplates, Has.Count.EqualTo(1));
            Assert.That(plan.NormalTemplates[0].EntryName, Is.EqualTo("face"));
            Assert.That(plan.NormalTemplates[0].WordSource, Is.EqualTo("$base CANVAS NORMAL_BASE NORMAL_FINALIZE"));

            MeshCoreBinding[] wrongKind = { MeshCoreBinding.Outfit("face", "outfit.face"), MeshCoreBinding.Morph("body", "Body") };
            MeshRecipeDocument declaredNormalDocument = Document("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $body 0.25 FBM_SET", "face", "body");
            Assert.That(MeshStackMachineCorePlan.TryCreate(declaredNormalDocument, wrongKind, out _, out StackMachineDiagnostic wrongKindDiagnostic), Is.False);
            Assert.That(wrongKindDiagnostic.domainCode, Is.EqualTo("NormalBindingRequired"));
        }

        [Test]
        public void RuntimeMachine_AcceptsImplicitNormalEntryWithNonemptyMeshRecipe()
        {
            var figure = new GameObject("mesh-core-implicit-normal");
            var template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                blender.ConfigureForFigure(null, null, null, null, new List<DynamicBoneBlendTarget> { new DynamicBoneBlendTarget { blendName = "Body" } });
                figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<zgock.ShapeSync.Materials.MaterialProxy>();
                zgock.ShapeSync.Materials.NormalBlender normalBlender = figure.AddComponent<zgock.ShapeSync.Materials.NormalBlender>();
                var normalSerialized = new SerializedObject(normalBlender);
                normalSerialized.FindProperty("entries").arraySize = 1;
                normalSerialized.FindProperty("entries").GetArrayElementAtIndex(0).stringValue = "face";
                normalSerialized.ApplyModifiedPropertiesWithoutUndo();
                var templateSerialized = new SerializedObject(template);
                templateSerialized.FindProperty("morphs").arraySize = 1;
                SerializedProperty morph = templateSerialized.FindProperty("morphs").GetArrayElementAtIndex(0);
                morph.FindPropertyRelative("word").stringValue = "body";
                morph.FindPropertyRelative("name").stringValue = "Body";
                templateSerialized.ApplyModifiedPropertiesWithoutUndo();
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                var machineSerialized = new SerializedObject(machine);
                machineSerialized.FindProperty("bindingTemplate").objectReferenceValue = template;
                machineSerialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(machine.TryEnsureReady(out StackMachineDiagnostic readyDiagnostic), Is.True, readyDiagnostic?.message);
                var document = new MeshRecipeDocument { wordSource = "$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $body 0 FBM_SET" };
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "unused", declaredKind = StackMachineBindingKind.Resource });
                Assert.That(machine.TryExecute(document, out StackMachineExecutionResult documentResult), Is.True, documentResult.Diagnostic?.message);
                Assert.That(machine.TryExecute("$face NORMAL $base CANVAS NORMAL_BASE NORMAL_FINALIZE ENDNORMAL $body 0 FBM_SET", out StackMachineExecutionResult result), Is.True, result.Diagnostic?.message);
                Assert.That(machine.TryGetNormalTemplate("face", out NormalRecipeTemplate normalTemplate, out StackMachineDiagnostic templateDiagnostic), Is.True, templateDiagnostic?.message);
                Assert.That(normalTemplate.WordSource, Is.EqualTo("$base CANVAS NORMAL_BASE NORMAL_FINALIZE"));
            }
            finally
            {
                Object.DestroyImmediate(template);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TryCreate_RejectsWrongBindingKindAndDuplicateAttachRegistry()
        {
            MeshCoreBinding[] morphBinding = { MeshCoreBinding.Morph("body", "Body") };
            MeshCoreBinding[] duplicateOutfitBindings = { MeshCoreBinding.Outfit("hairA", "outfit.hair"), MeshCoreBinding.Outfit("hairB", "outfit.hair") };

            Assert.That(MeshStackMachineCorePlan.TryCreate(Document("$body ATTACH", "body"), morphBinding, out _, out StackMachineDiagnostic kindDiagnostic), Is.False);
            Assert.That(kindDiagnostic.domainCode, Is.EqualTo("OutfitBindingRequired"));

            Assert.That(MeshStackMachineCorePlan.TryCreate(Document("$hairA ATTACH $hairB ATTACH", "hairA", "hairB"), duplicateOutfitBindings, out _, out StackMachineDiagnostic duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic.domainCode, Is.EqualTo("DuplicateRegistryId"));
        }

        private static MeshRecipeDocument Document(string source, params string[] names)
        {
            var document = new MeshRecipeDocument { wordSource = source };
            for (int i = 0; i < names.Length; i++) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = names[i], declaredKind = StackMachineBindingKind.Resource });
            return document;
        }
    }
}
