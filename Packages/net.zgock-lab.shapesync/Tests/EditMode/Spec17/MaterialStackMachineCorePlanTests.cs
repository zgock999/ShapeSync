// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialStackMachineCorePlanTests
    {
        [Test]
        public void TryCreate_ProducesImmutableExecutionFreeCommonPlan()
        {
            var document = new MaterialRecipeDocument
            {
                wordSource = "$face MATERIAL TEXTURE $base CANVAS $out OUTPUT ENDTEXTURE #FF8040FF COLOR 1 1 0 0 UVSET $body MATERIAL 0.2 0.3 0.4 1 COLOR"
            };

            Assert.That(MaterialStackMachineCorePlan.TryCreate(document, out MaterialStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.CommonPlan, Is.Not.Null);
            Assert.That(plan.Blocks, Has.Count.EqualTo(2));
            Assert.That(plan.Blocks[0].BindingName, Is.EqualTo("face"));
            Assert.That(plan.Blocks[0].TextureSource, Is.EqualTo("$base CANVAS $out OUTPUT"));
            Assert.That(plan.Blocks[0].HasColor, Is.True);
            Assert.That(plan.Blocks[0].HasUvTransform, Is.True);
            Assert.That(plan.Blocks[1].BindingName, Is.EqualTo("body"));
            Assert.That(plan.Blocks[1].TextureSource, Is.Null);
            Assert.That(plan.Blocks[1].HasColor, Is.True);
        }

        [Test]
        public void TryCreate_PreservesResetAsCommonPlanBlock()
        {
            var document = new MaterialRecipeDocument { wordSource = "MATERIAL_RESET $face MATERIAL 1 1 1 1 COLOR" };

            Assert.That(MaterialStackMachineCorePlan.TryCreate(document, out MaterialStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Blocks, Has.Count.EqualTo(2));
            Assert.That(plan.Blocks[0].IsReset, Is.True);
            Assert.That(plan.Blocks[1].BindingName, Is.EqualTo("face"));
        }

        [Test]
        public void TryCreate_RejectsNullAndClosedGrammarFailureWithoutPhysicalAccess()
        {
            Assert.That(MaterialStackMachineCorePlan.TryCreate(null, out _, out StackMachineDiagnostic nullDiagnostic), Is.False);
            Assert.That(nullDiagnostic.domainCode, Is.EqualTo("DocumentRequired"));

            var invalid = new MaterialRecipeDocument { wordSource = "$face MATERIAL NORMAL" };
            Assert.That(MaterialStackMachineCorePlan.TryCreate(invalid, out _, out StackMachineDiagnostic grammarDiagnostic), Is.False);
            Assert.That(grammarDiagnostic.domainCode, Is.EqualTo("MaterialNormalUnsupported"));
        }
    }
}
