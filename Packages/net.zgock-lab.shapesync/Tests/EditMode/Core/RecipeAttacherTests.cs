// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>Focused ingress and phase-gate coverage for the Spec15.3 Figure RecipeAttacher.</summary>
    public sealed class RecipeAttacherTests
    {
        [Test]
        public void CarrierRoute_RejectsWhenNoCarrierIsAssigned()
        {
            var figure = new GameObject("RecipeAttacherCarrierRequired");
            try
            {
                RecipeAttacher attacher = figure.AddComponent<RecipeAttacher>();
                InvokeAwake(attacher);

                Assert.That(attacher.TryApply(out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domain, Is.EqualTo("document"));
                Assert.That(diagnostic.domainCode, Is.EqualTo("CarrierRequired"));
            }
            finally { Object.DestroyImmediate(figure); }
        }

        [Test]
        public void InMemoryRoute_UsesBothEmptyDomainNoOpPaths()
        {
            var figure = new GameObject("RecipeAttacherEmptyDocument");
            try
            {
                figure.AddComponent<MeshStackMachine>();
                MaterialAttacher materialAttacher = figure.AddComponent<MaterialAttacher>();
                MaterialStackMachine materialMachine = figure.AddComponent<MaterialStackMachine>();
                materialMachine.MaterialAttacher = materialAttacher;
                RecipeAttacher attacher = figure.AddComponent<RecipeAttacher>();
                InvokeAwake(attacher);

                Assert.That(attacher.TryApply(new ShapeSyncDocument(), out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            }
            finally { Object.DestroyImmediate(figure); }
        }

        [Test]
        public void ShapeDocumentCarrierRoute_UsesTheSameMeshThenMaterialIngress()
        {
            var figure = new GameObject("RecipeAttacherShapeDocument");
            ShapeDocument document = null;
            try
            {
                figure.AddComponent<MeshStackMachine>();
                MaterialAttacher materialAttacher = figure.AddComponent<MaterialAttacher>();
                MaterialStackMachine materialMachine = figure.AddComponent<MaterialStackMachine>();
                materialMachine.MaterialAttacher = materialAttacher;
                RecipeAttacher attacher = figure.AddComponent<RecipeAttacher>();
                InvokeAwake(attacher);
                document = ScriptableObject.CreateInstance<ShapeDocument>();
                document.MeshRecipe = new MeshRecipeDocument { wordSource = "" };
                attacher.DocumentAsset = document;

                Assert.That(attacher.TryApply(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            }
            finally
            {
                if (document != null) Object.DestroyImmediate(document);
                Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void ShapeDocumentCarrierRoute_SnapshotsItsOwnSerializedRecipeFields()
        {
            ShapeDocument document = ScriptableObject.CreateInstance<ShapeDocument>();
            try
            {
                document.MeshRecipe = new MeshRecipeDocument { wordSource = "$basicgirl 0.5 FBM_SET" };

                Assert.That(document.TryGetSnapshot(out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(snapshot.MeshRecipe.wordSource, Is.EqualTo("$basicgirl 0.5 FBM_SET"));
            }
            finally
            {
                Object.DestroyImmediate(document);
            }
        }

        [Test]
        public void InMemoryRoute_MeshRejectPreventsMaterialPhase()
        {
            var figure = new GameObject("RecipeAttacherMeshPhaseGate");
            try
            {
                figure.AddComponent<MeshStackMachine>();
                MaterialAttacher materialAttacher = figure.AddComponent<MaterialAttacher>();
                MaterialStackMachine materialMachine = figure.AddComponent<MaterialStackMachine>();
                materialMachine.MaterialAttacher = materialAttacher;
                RecipeAttacher attacher = figure.AddComponent<RecipeAttacher>();
                InvokeAwake(attacher);
                var document = new ShapeSyncDocument
                {
                    MeshRecipe = new MeshRecipeDocument { wordSource = "$missing 1 FBM_SET" },
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 1 0 0 1 COLOR" },
                    MaterialBinding = ScriptableObject.CreateInstance<MaterialBinding>()
                };
                try
                {
                    Assert.That(attacher.TryApply(document, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(diagnostic.domain, Is.EqualTo("mesh"));
                    Assert.That(materialMachine.IsBusy, Is.False, "Material phase must not begin after a Mesh reject.");
                }
                finally { Object.DestroyImmediate(document.MaterialBinding); }
            }
            finally
            {
                Object.DestroyImmediate(figure);
            }
        }

        private static void InvokeAwake(RecipeAttacher attacher)
        {
            typeof(RecipeAttacher).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attacher, null);
        }
    }
}
