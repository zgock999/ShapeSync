// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class ShapeSyncDocumentTests
    {
        [Test]
        public void Snapshot_DeepCopiesRecipeMetadataAndPreservesBindingReferences()
        {
            MeshBinding meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
            MaterialBinding materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                var source = new ShapeSyncDocument
                {
                    MeshBinding = meshBinding,
                    MaterialBinding = materialBinding,
                    MeshRecipe = new MeshRecipeDocument { wordSource = "$body 0.5 FBM_SET" },
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL" }
                };
                source.MeshRecipe.bindings.Add(new StackMachineBindingDeclaration { logicalName = "body", declaredKind = StackMachineBindingKind.Resource });
                source.MaterialRecipe.capabilities.Add("texture-v2");

                Assert.That(ShapeSyncDocument.TryCreateSnapshot(source, out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                source.MeshRecipe.wordSource = "changed";
                source.MeshRecipe.bindings[0].logicalName = "changed";
                source.MaterialRecipe.capabilities[0] = "changed";

                Assert.That(snapshot.MeshRecipe.wordSource, Is.EqualTo("$body 0.5 FBM_SET"));
                Assert.That(snapshot.MeshRecipe.bindings[0].logicalName, Is.EqualTo("body"));
                Assert.That(snapshot.MaterialRecipe.capabilities[0], Is.EqualTo("texture-v2"));
                Assert.That(snapshot.MaterialRecipe.textureDomainVersion, Is.EqualTo(2));
                Assert.That(snapshot.MeshBinding, Is.SameAs(meshBinding));
                Assert.That(snapshot.MaterialBinding, Is.SameAs(materialBinding));
            }
            finally
            {
                Object.DestroyImmediate(meshBinding);
                Object.DestroyImmediate(materialBinding);
            }
        }

        [Test]
        public void Carrier_SnapshotsWithoutMutatingItsStoredDocument()
        {
            ShapeSyncDocumentAsset carrier = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            try
            {
                var source = new ShapeSyncDocument { MeshRecipe = new MeshRecipeDocument { wordSource = "$body 1 FBM_SET" } };
                Assert.That(carrier.TrySetDocument(source, out StackMachineDiagnostic setDiagnostic), Is.True, setDiagnostic?.message);
                source.MeshRecipe.wordSource = "changed";

                Assert.That(carrier.TryGetSnapshot(out ShapeSyncDocument first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                first.MeshRecipe.wordSource = "changed again";
                Assert.That(carrier.TryGetSnapshot(out ShapeSyncDocument second, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);

                Assert.That(first.MeshRecipe.wordSource, Is.EqualTo("changed again"));
                Assert.That(second.MeshRecipe.wordSource, Is.EqualTo("$body 1 FBM_SET"));
            }
            finally
            {
                Object.DestroyImmediate(carrier);
            }
        }

        [Test]
        public void Snapshot_RejectsNullDocument()
        {
            Assert.That(ShapeSyncDocument.TryCreateSnapshot(null, out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(snapshot, Is.Null);
            Assert.That(diagnostic.domainCode, Is.EqualTo("DocumentRequired"));
        }

        [Test]
        public void Snapshot_DeepCopiesMaterialDomainFields()
        {
            var source = new ShapeSyncDocument
            {
                MaterialRecipe = new MaterialRecipeDocument
                {
                    wordSource = "$body MATERIAL",
                    textureDomainVersion = 2,
                    outputLogicalName = "out",
                    outputWidth = 1024,
                    outputHeight = 512
                }
            };

            Assert.That(ShapeSyncDocument.TryCreateSnapshot(source, out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            source.MaterialRecipe.outputWidth = 128;
            source.MaterialRecipe.outputHeight = 128;

            Assert.That(snapshot.MaterialRecipe.outputWidth, Is.EqualTo(1024));
            Assert.That(snapshot.MaterialRecipe.outputHeight, Is.EqualTo(512));
        }
    }
}
