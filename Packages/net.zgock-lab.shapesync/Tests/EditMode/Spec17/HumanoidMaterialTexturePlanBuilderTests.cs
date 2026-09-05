// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidMaterialTexturePlanBuilderTests
    {
        [Test]
        public void TryCreate_BuildsExecutionFreePlanWithoutMutatingTargetDocument()
        {
            var document = new MaterialRecipeDocument
            {
                wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE",
                outputLogicalName = "out",
                outputWidth = 128,
                outputHeight = 128
            };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "authoringOnly", declaredKind = StackMachineBindingKind.Resource });
            var binding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                Assert.That(MaterialStackMachineCorePlan.TryCreate(document, out MaterialStackMachineCorePlan core, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
                int bindingCountBefore = document.bindings.Count;

                Assert.That(HumanoidMaterialTexturePlanBuilder.TryCreate(core.Blocks[0], document, binding, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.DispatchPlan.OutputWidth, Is.EqualTo(128));
                Assert.That(plan.DispatchPlan.OutputHeight, Is.EqualTo(128));
                Assert.That(document.bindings, Has.Count.EqualTo(bindingCountBefore));
            }
            finally { Object.DestroyImmediate(binding); }
        }

        [Test]
        public void TryCreate_RejectsMissingTextureSourceAndBinding()
        {
            var colorOnly = new MaterialRecipeDocument { wordSource = "$body MATERIAL 1 1 1 1 COLOR" };
            Assert.That(MaterialStackMachineCorePlan.TryCreate(colorOnly, out MaterialStackMachineCorePlan colorCore, out StackMachineDiagnostic coreDiagnostic), Is.True, coreDiagnostic?.message);
            var binding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                Assert.That(HumanoidMaterialTexturePlanBuilder.TryCreate(colorCore.Blocks[0], colorOnly, binding, out _, out StackMachineDiagnostic sourceDiagnostic), Is.False);
                Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("MaterialTextureSourceRequired"));
            }
            finally { Object.DestroyImmediate(binding); }

            var texture = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 1 1 1 FILL $out COPY DROP ENDTEXTURE" };
            Assert.That(MaterialStackMachineCorePlan.TryCreate(texture, out MaterialStackMachineCorePlan textureCore, out coreDiagnostic), Is.True, coreDiagnostic?.message);
            Assert.That(HumanoidMaterialTexturePlanBuilder.TryCreate(textureCore.Blocks[0], texture, null, out _, out StackMachineDiagnostic bindingDiagnostic), Is.False);
            Assert.That(bindingDiagnostic.domainCode, Is.EqualTo("MaterialBindingRequired"));
        }
    }
}
