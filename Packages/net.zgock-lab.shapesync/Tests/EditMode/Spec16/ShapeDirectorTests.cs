// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeDirectorTests
    {
        [Test]
        public void DispatchPayload_RaisesTransactionStartingBeforeMeshExecutionCanMutateTopology()
        {
            var gameObject = new GameObject("director");
            try
            {
                var director = gameObject.AddComponent<ShapeDirector>();
                int starts = 0;
                director.TransactionStarting += () => starts++;
                MethodInfo dispatch = typeof(ShapeDirector).GetMethod("TryDispatchPayload", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(dispatch, Is.Not.Null);
                object[] arguments = { new List<ShapeSyncShape>(), null, true, false, null };

                Assert.That((bool)dispatch.Invoke(director, arguments), Is.False, "The fixture has no MeshStackMachine and must reject after the pre-mutation notification.");
                Assert.That(starts, Is.EqualTo(1), "Derived-runtime owners must release their hierarchies before Mesh execution begins.");
                Assert.That(((StackMachineDiagnostic)arguments[4]).domainCode, Is.EqualTo("MeshMachineRequired"));
            }
            finally { Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void Compile_RejectsNonEmptyMeshRecipeWithoutSharedBinding()
        {
            var gameObject = new GameObject("director");
            var template = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            try
            {
                template.ShapeId = "shape";
                template.Morphs.Add(new MorphValue { Target = "girl", Value = .2f });
                var director = gameObject.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryCompile(out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MeshBindingRequired"));
            }
            finally { Object.DestroyImmediate(template); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void Crud_AddReplaceRemove_DoesNotMutateTemplate()
        {
            var gameObject = new GameObject("director"); var template = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            try
            {
                template.ShapeId = "shape"; template.Morphs.Add(new MorphValue { Target = "girl", Value = .2f });
                var director = gameObject.AddComponent<ShapeDirector>(); director.AutoCompile = false;
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryReplaceRuntimeShape(new MorphShape("shape", 0, null, new[] { new MorphValue { Target = "girl", Value = .7f } }), out diagnostic), Is.True, diagnostic?.message);
                Assert.That(template.Morphs[0].Value, Is.EqualTo(.2f));
                Assert.That(director.TryRemoveRuntimeShape("shape", out diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.RuntimeShapes, Is.Empty);
                Assert.That(director.TemplateList, Has.Count.EqualTo(1), "Runtime D must not alter the inspector TemplateList input.");
            }
            finally { Object.DestroyImmediate(template); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void Crud_RejectsDuplicateShapeId()
        {
            var gameObject = new GameObject("director"); var first = ScriptableObject.CreateInstance<MorphShapeTemplate>(); var second = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            try
            {
                first.ShapeId = second.ShapeId = "same"; second.Priority = 1; var director = gameObject.AddComponent<ShapeDirector>(); director.AutoCompile = false;
                Assert.That(director.TryAddTemplate(first, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryAddTemplate(second, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateShapeId"));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void Crud_AddAtTheSameTypeAndPriority_ReplacesOnlyThatLogicalWearingSlot()
        {
            var gameObject = new GameObject("director");
            var first = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            var second = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            var retained = ScriptableObject.CreateInstance<HairShapeTemplate>();
            try
            {
                first.ShapeId = "first"; first.Priority = 10;
                second.ShapeId = "second"; second.Priority = 10;
                retained.ShapeId = "retained"; retained.Priority = 10;
                var director = gameObject.AddComponent<ShapeDirector>();
                director.AutoCompile = false;

                Assert.That(director.TryAddTemplate(first, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryAddTemplate(retained, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryAddTemplate(second, out diagnostic), Is.True, diagnostic?.message);

                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(2));
                Assert.That(director.TryGetRuntimeShape("first", out _, out _), Is.False);
                Assert.That(director.TryGetRuntimeShape("second", out _, out _), Is.True);
                Assert.That(director.TryGetRuntimeShape("retained", out _, out _), Is.True);
                Assert.That(director.TemplateList, Is.EqualTo(new ShapeSyncShapeTemplate[] { first, retained, second }), "Priority exchange changes logical Runtime Shapes only; TemplateList remains an inspector input history.");
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(retained);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SynchronizeTemplateList_RebuildsRuntimeShapesOnlyWhenExplicitlyRequested()
        {
            var gameObject = new GameObject("director"); var first = ScriptableObject.CreateInstance<MorphShapeTemplate>(); var second = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            try
            {
                first.ShapeId = "first"; first.Morphs.Add(new MorphValue { Target = "girl", Value = .2f });
                second.ShapeId = "second";
                var director = gameObject.AddComponent<ShapeDirector>(); director.AutoCompile = false;
                director.TemplateList.Add(first);
                Assert.That(director.TrySynchronizeTemplateList(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryReplaceRuntimeShape(new MorphShape("first", 0, null, new[] { new MorphValue { Target = "girl", Value = .8f } }), out diagnostic), Is.True, diagnostic?.message);
                director.TemplateList.Add(second);
                Assert.That(((MorphShape)director.RuntimeShapes[0]).Morphs[0].Value, Is.EqualTo(.8f), "Editing TemplateList alone must not synchronize Runtime Shapes.");
                Assert.That(director.TrySynchronizeTemplateList(out diagnostic), Is.True, diagnostic?.message);
                Assert.That(((MorphShape)director.RuntimeShapes[0]).Morphs[0].Value, Is.EqualTo(.2f), "Explicit synchronization rebuilds Runtime Shapes from TemplateList.");
                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(2));
                director.TemplateList.RemoveAt(0);
                Assert.That(director.TrySynchronizeTemplateList(out diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(1));
                Assert.That(director.RuntimeShapes[0].ShapeId, Is.EqualTo("second"));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void SynchronizeTemplateList_WarnsAndSkipsInvalidEntries()
        {
            var gameObject = new GameObject("director");
            var valid = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            try
            {
                valid.ShapeId = "valid";
                ShapeDirector director = gameObject.AddComponent<ShapeDirector>(); director.AutoCompile = false;
                director.TemplateList.Add(null);
                director.TemplateList.Add(valid);
                director.TemplateList.Add(valid);
                LogAssert.Expect(LogType.Warning, "Shape Director TemplateList sync skipped entry 0. TemplateRequired: TemplateList entry is null.");
                LogAssert.Expect(LogType.Warning, "Shape Director TemplateList sync skipped entry 2. DuplicateShapeId: TemplateList produced a duplicate ShapeId.");
                Assert.That(director.TrySynchronizeTemplateList(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(1));
                Assert.That(director.RuntimeShapes[0].ShapeId, Is.EqualTo("valid"));
            }
            finally { Object.DestroyImmediate(valid); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void FilterDetachedOutfitMaterialEntries_UsesDesiredMaterialRegistryIdsNotMeshLogicalNames()
        {
            var current = new System.Collections.Generic.List<ShapeSyncMergedEntry>
            {
                MergedTexture(string.Empty),
                MergedTexture("retired-outfit"),
                MergedTexture("active-outfit")
            };
            var desired = new System.Collections.Generic.List<ShapeSyncMergedEntry> { MergedTexture("active-outfit") };

            MethodInfo filter = typeof(ShapeDirector).GetMethod("FilterDetachedOutfitMaterialEntries", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(filter, Is.Not.Null);
            var filtered = (System.Collections.Generic.List<ShapeSyncMergedEntry>)filter.Invoke(null, new object[] { current, desired });

            Assert.That(filtered, Has.Count.EqualTo(2));
            Assert.That(((MaterialEntry)filtered[0].Entry).RegistryId, Is.EqualTo(string.Empty));
            Assert.That(((MaterialEntry)filtered[1].Entry).RegistryId, Is.EqualTo("active-outfit"));
            Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(filtered, desired, out string source, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(source, Does.Not.Contain("retired-outfit"));
        }

        [Test]
        public void RecoveryClassification_AcceptsOnlyMeshCommitExecutionFailures()
        {
            MethodInfo requiresRecovery = typeof(ShapeDirector).GetMethod("RequiresRecovery", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(requiresRecovery, Is.Not.Null);
            Assert.That((bool)requiresRecovery.Invoke(null, new object[] { StackMachineDiagnostic.CreateDomain("mesh", "CommitAttachFailed", "injected") }), Is.True);
            Assert.That((bool)requiresRecovery.Invoke(null, new object[] { StackMachineDiagnostic.CreateDomain("mesh", "CommitDetachFailed", "injected") }), Is.True);
            Assert.That((bool)requiresRecovery.Invoke(null, new object[] { StackMachineDiagnostic.CreateDomain("mesh", "CommitUnexpectedFailure", "injected") }), Is.True);
            Assert.That((bool)requiresRecovery.Invoke(null, new object[] { StackMachineDiagnostic.CreateDomain("mesh", "OutfitDryRunRejected", "injected") }), Is.False);
        }

        [Test]
        public void TryLoadDocument_RejectsInvalidShapesReturnedByCustomInMemoryDeserializer()
        {
            var gameObject = new GameObject("director");
            ShapeDocument document = ScriptableObject.CreateInstance<ShapeDocument>();
            MorphShapeTemplate retainedTemplate = ScriptableObject.CreateInstance<MorphShapeTemplate>();
            try
            {
                ShapeDirector director = gameObject.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                InvalidInMemoryShapeDocumentDeserializer deserializer = gameObject.AddComponent<InvalidInMemoryShapeDocumentDeserializer>();
                retainedTemplate.ShapeId = "retained";
                retainedTemplate.Morphs.Add(new MorphValue { Target = "retained-morph", Value = 0.5f });
                Assert.That(director.TryAddTemplate(retainedTemplate, out StackMachineDiagnostic retainedDiagnostic), Is.True, retainedDiagnostic?.message);
                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(1));

                deserializer.RuntimeShapes = new List<ShapeSyncShape> { null };
                Assert.That(director.TryLoadDocument(document, out StackMachineDiagnostic nullDiagnostic), Is.False);
                Assert.That(nullDiagnostic.domainCode, Is.EqualTo("RuntimeShapeRequired"));
                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(1));
                Assert.That(director.RuntimeShapes[0].ShapeId, Is.EqualTo("retained"));

                deserializer.RuntimeShapes = new List<ShapeSyncShape>
                {
                    new MorphShape("duplicate", 0, null, null),
                    new MorphShape("duplicate", 1, null, null)
                };
                Assert.That(director.TryLoadDocument(document, out StackMachineDiagnostic duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic.domainCode, Is.EqualTo("DuplicateShapeId"));
                Assert.That(director.RuntimeShapes, Has.Count.EqualTo(1));
                Assert.That(director.RuntimeShapes[0].ShapeId, Is.EqualTo("retained"));
            }
            finally { Object.DestroyImmediate(retainedTemplate); Object.DestroyImmediate(document); Object.DestroyImmediate(gameObject); }
        }

        private static ShapeSyncMergedEntry MergedTexture(string registryId)
        {
            return new ShapeSyncMergedEntry(
                new TextureEntry { RegistryId = registryId, ProxyEntry = "body", LogicalName = "texture" },
                priority: 0,
                shapeId: "shape",
                listPosition: 0);
        }

        [Test]
        public void Destroy_ClearsOnlyDirectorTransactionBookkeeping()
        {
            var gameObject = new GameObject("director");
            try
            {
                ShapeDirector director = gameObject.AddComponent<ShapeDirector>();
                typeof(ShapeDirector).GetField("transactionInFlight", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, true);
                typeof(ShapeDirector).GetField("recoveryRequestedOnEnable", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, true);
                typeof(ShapeDirector).GetField("pendingDesiredPhysical", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, new System.Collections.Generic.List<ShapeSyncShape> { new MorphShape("pending", 0, null, null) });
                typeof(ShapeDirector).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, null);

                Assert.That((bool)typeof(ShapeDirector).GetField("transactionInFlight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.False);
                Assert.That((bool)typeof(ShapeDirector).GetField("recoveryRequestedOnEnable", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.False);
                Assert.That(typeof(ShapeDirector).GetField("pendingDesiredPhysical", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.Null);
            }
            finally { Object.DestroyImmediate(gameObject); }
        }
    }

    public sealed class InvalidInMemoryShapeDocumentDeserializer : ShapeDeserializer, IShapeDocumentSourceDeserializer
    {
        public List<ShapeSyncShape> RuntimeShapes = new List<ShapeSyncShape>();

        public override bool TryDeserialize(string fileName, out List<ShapeSyncShape> runtimeShapes)
        {
            runtimeShapes = RuntimeShapes;
            return true;
        }

        public bool TryDeserialize(ShapeDocument source, out List<ShapeSyncShape> runtimeShapes, out ShapeSyncDocument payload, out StackMachineDiagnostic diagnostic)
        {
            runtimeShapes = RuntimeShapes;
            payload = null;
            diagnostic = null;
            return true;
        }
    }
}
