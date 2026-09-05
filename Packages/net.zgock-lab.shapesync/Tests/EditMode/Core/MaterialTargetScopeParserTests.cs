// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialTargetScopeParserTests
    {
        [Test]
        public void ParsesFigureAndOutfitScopesInSourceOrder()
        {
            const string source = "FIGURE $body MATERIAL 1 1 1 1 COLOR $hat OUTFIT $cap MATERIAL 0 0 1 1 COLOR";

            Assert.That(MaterialTargetScopeParser.TryParse(source, out var targets, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(targets.Count, Is.EqualTo(2));
            Assert.That(targets[0].OutfitRegistryId, Is.Null);
            Assert.That(targets[0].Source, Is.EqualTo("$body MATERIAL 1 1 1 1 COLOR"));
            Assert.That(targets[1].OutfitRegistryId, Is.EqualTo("hat"));
            Assert.That(targets[1].Source, Is.EqualTo("$cap MATERIAL 0 0 1 1 COLOR"));
        }

        [Test]
        public void MergesRepeatedTargetScopesWithoutChangingBlockOrder()
        {
            const string source = "$hair OUTFIT $a MATERIAL 1 1 1 1 COLOR FIGURE $body MATERIAL 1 1 1 1 COLOR $hair OUTFIT $b MATERIAL 0 0 1 1 COLOR";

            Assert.That(MaterialTargetScopeParser.TryParse(source, out var targets, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(targets.Count, Is.EqualTo(2));
            Assert.That(targets[0].OutfitRegistryId, Is.Null, "The implicit Figure target is the first target-local execution slot.");
            Assert.That(targets[0].Source, Is.EqualTo("$body MATERIAL 1 1 1 1 COLOR"));
            Assert.That(targets[1].OutfitRegistryId, Is.EqualTo("hair"));
            Assert.That(targets[1].Source, Is.EqualTo("$a MATERIAL 1 1 1 1 COLOR $b MATERIAL 0 0 1 1 COLOR"));
        }

        [Test]
        public void RejectsTargetDirectiveInsideTextureBuffer()
        {
            Assert.That(MaterialTargetScopeParser.TryParse("$body MATERIAL TEXTURE $hat OUTFIT ENDTEXTURE", out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialTargetNestingInvalid"));
        }

        [Test]
        public void RejectsTargetDirectiveWithPendingMaterialScalars()
        {
            Assert.That(MaterialTargetScopeParser.TryParse("$body MATERIAL 1 1 FIGURE 1 1 COLOR", out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialTargetNestingInvalid"));
        }

        [Test]
        public void DocumentPayload_RejectsLegacyTextureTemplateAndMaterialBindingConflict()
        {
            var host = new GameObject("Material Host");
            TextureBindingTemplate legacyTemplate = ScriptableObject.CreateInstance<TextureBindingTemplate>();
            MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                MaterialStackMachine machine = host.AddComponent<MaterialStackMachine>();
                machine.TextureBindingTemplate = legacyTemplate;
                var payload = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 1 1 1 1 COLOR" }
                };

                Assert.That(machine.TryAcceptRecipePayload(payload, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("ConfigurationConflict"));
            }
            finally
            {
                Object.DestroyImmediate(binding);
                Object.DestroyImmediate(legacyTemplate);
                Object.DestroyImmediate(host);
            }
        }
    }
}
