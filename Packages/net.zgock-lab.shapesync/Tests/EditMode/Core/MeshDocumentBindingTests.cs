// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MeshDocumentBindingTests
    {
        [Test]
        public void MeshBinding_BuildsFigureLocalMorphContextWithoutLegacyTemplate()
        {
            var figure = new GameObject("Figure");
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("BasicGirl", null, null);
                AddMorph(binding, "girl", "BasicGirl");

                Assert.That(MeshBindingContext.TryCreate(binding, blender, attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(context.TryGetHandle("girl", out int handle), Is.True);
                Assert.That(context.TryGetMorph(handle, out MeshBindingContext.MorphBinding morph), Is.True);
                Assert.That(morph.TargetName, Is.EqualTo("BasicGirl"));
            }
            finally
            {
                Object.DestroyImmediate(binding);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshPayload_RejectsLegacyTemplateAndDocumentBindingConfigurationConflict()
        {
            var figure = new GameObject("Figure");
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            MeshBindingTemplate legacyTemplate = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                blender.EnsureTarget("BasicGirl", null, null);
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                var serializedMachine = new SerializedObject(machine);
                serializedMachine.FindProperty("bindingTemplate").objectReferenceValue = legacyTemplate;
                serializedMachine.ApplyModifiedPropertiesWithoutUndo();

                AddMorph(binding, "girl", "BasicGirl");
                var recipe = new MeshRecipeDocument { wordSource = "$girl 0.5 FBM_SET" };
                recipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "girl", declaredKind = StackMachineBindingKind.Resource });
                var payload = new ShapeSyncDocument { MeshBinding = binding, MeshRecipe = recipe };

                Assert.That(machine.TryAcceptRecipePayload(payload, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("ConfigurationConflict"));
            }
            finally
            {
                Object.DestroyImmediate(legacyTemplate);
                Object.DestroyImmediate(binding);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshPayload_DerivesCompilerBindingsFromTheSharedMeshBinding()
        {
            var figure = new GameObject("Figure");
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                blender.EnsureTarget("BasicGirl", null, null);
                AddMorph(binding, "girl", "BasicGirl");
                var payload = new ShapeSyncDocument
                {
                    MeshBinding = binding,
                    MeshRecipe = new MeshRecipeDocument { wordSource = "$girl 0.7 FBM_SET" }
                };

                Assert.That(machine.TryAcceptRecipePayload(payload, out StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Stage, Is.EqualTo(StackMachineTransactionStage.Succeeded));
                Assert.That(blender.Targets[0].weight, Is.EqualTo(0.7f).Within(0.00001f));
            }
            finally
            {
                Object.DestroyImmediate(binding);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void MeshBinding_BuildsDynamicNormalRecipeWithoutDocumentNormalBlocks()
        {
            var figure = new GameObject("Figure");
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            var baseTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var targetTexture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            try
            {
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MaterialProxy>();
                NormalBlender normalBlender = figure.AddComponent<NormalBlender>();
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                blender.EnsureTarget("BasicGirl", null, null);
                SetNormalEntries(normalBlender, "face");
                AddNormalSources(binding, baseTexture, targetTexture);

                Assert.That(machine.TryEnsureReady(binding, out StackMachineDiagnostic ready), Is.True, ready?.message);
                var snapshot = new[] { new NormalTargetWeight("BasicGirl", 0.7f, true) };
                Assert.That(machine.TryBuildNormalRecipe("face", snapshot, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(stub.Document.wordSource, Does.Contain("NORMAL_DELTA_ADD"));
                Assert.That(stub.Bindings, Has.Some.Matches<TextureBindingEntry>(x => x.logicalName == "target0" && x.sourceTexture == targetTexture));
            }
            finally
            {
                Object.DestroyImmediate(targetTexture);
                Object.DestroyImmediate(baseTexture);
                Object.DestroyImmediate(binding);
                Object.DestroyImmediate(figure);
            }
        }

        private static void AddMorph(MeshBinding binding, string logicalName, string targetName)
        {
            var serialized = new SerializedObject(binding);
            SerializedProperty morphs = serialized.FindProperty("morphs");
            morphs.InsertArrayElementAtIndex(0);
            SerializedProperty entry = morphs.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("logicalName").stringValue = logicalName;
            entry.FindPropertyRelative("targetName").stringValue = targetName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetNormalEntries(NormalBlender blender, params string[] entries)
        {
            var serialized = new SerializedObject(blender);
            SerializedProperty property = serialized.FindProperty("entries");
            property.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++) property.GetArrayElementAtIndex(i).stringValue = entries[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddNormalSources(MeshBinding binding, Texture2D baseTexture, Texture2D targetTexture)
        {
            var serialized = new SerializedObject(binding);
            SerializedProperty owners = serialized.FindProperty("normalOwners");
            owners.arraySize = 1;
            SerializedProperty targets = owners.GetArrayElementAtIndex(0).FindPropertyRelative("targets");
            targets.arraySize = 2;
            SetNormalTarget(targets.GetArrayElementAtIndex(0), string.Empty, baseTexture);
            SetNormalTarget(targets.GetArrayElementAtIndex(1), "BasicGirl", targetTexture);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetNormalTarget(SerializedProperty target, string targetName, Texture2D texture)
        {
            target.FindPropertyRelative("targetName").stringValue = targetName;
            SerializedProperty textures = target.FindPropertyRelative("textures");
            textures.arraySize = 1;
            SerializedProperty item = textures.GetArrayElementAtIndex(0);
            item.FindPropertyRelative("entryName").stringValue = "face";
            item.FindPropertyRelative("normalTexture").objectReferenceValue = texture;
        }
    }
}
