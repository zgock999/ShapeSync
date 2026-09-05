// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialStackMachineParserTests
    {
        [Test]
        public void ParsesUnorderedSemanticsAndImplicitMaterialClose()
        {
            const string source = "$face MATERIAL 1 1 0 0 UVSET TEXTURE 128 SIZE 1 1 1 1 FILL ENDTEXTURE 1 0.5 0.25 1 COLOR $body MATERIAL 0 0 1 1 UVSET";
            Assert.That(MaterialStackMachineParser.TryParse(source, out MaterialStackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic == null ? string.Empty : diagnostic.message);
            Assert.That(plan.Blocks.Count, Is.EqualTo(2));
            Assert.That(plan.Blocks[0].BindingName, Is.EqualTo("face")); Assert.That(plan.Blocks[0].TextureSource, Is.EqualTo("128 SIZE 1 1 1 1 FILL")); Assert.That(plan.Blocks[0].HasColor, Is.True); Assert.That(plan.Blocks[0].HasUvTransform, Is.True);
            Assert.That(plan.Blocks[1].BindingName, Is.EqualTo("body")); Assert.That(plan.Blocks[1].HasColor, Is.False); Assert.That(plan.Blocks[1].HasUvTransform, Is.True);
        }

        [Test]
        public void ParsesSrgbRgbaHexColorAsLinearColor()
        {
            Assert.That(MaterialStackMachineParser.TryParse("$body MATERIAL #FF8080FF COLOR", out MaterialStackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic == null ? string.Empty : diagnostic.message);
            Assert.That(plan.Blocks.Count, Is.EqualTo(1));
            Assert.That(plan.Blocks[0].HasColor, Is.True);
            Assert.That(plan.Blocks[0].Color.r, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(plan.Blocks[0].Color.g, Is.EqualTo(0.21586f).Within(0.0001f));
            Assert.That(plan.Blocks[0].Color.b, Is.EqualTo(0.21586f).Within(0.0001f));
            Assert.That(plan.Blocks[0].Color.a, Is.EqualTo(1f).Within(0.00001f));
        }

        [Test]
        public void ParsesFlatMaterialResetAsAnEntryTransactionItem()
        {
            Assert.That(MaterialStackMachineParser.TryParse("MATERIAL_RESET $body MATERIAL 1 1 1 1 COLOR", out MaterialStackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic == null ? string.Empty : diagnostic.message);
            Assert.That(plan.Blocks.Count, Is.EqualTo(2));
            Assert.That(plan.Blocks[0].BindingName, Is.Null);
            Assert.That(plan.Blocks[0].IsReset, Is.True);
            Assert.That(plan.Blocks[1].BindingName, Is.EqualTo("body"));
            Assert.That(plan.Blocks[1].IsReset, Is.False);
        }

        [TestCase("$body MATERIAL #FFF COLOR")]
        [TestCase("$body MATERIAL #FF80GGFF COLOR")]
        [TestCase("$body MATERIAL #FF8080FF UVSET")]
        public void RejectsInvalidOrNonColorHexUse(string source)
        {
            Assert.That(MaterialStackMachineParser.TryParse(source, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domain, Is.EqualTo("material"));
        }

        [TestCase("$face MATERIAL TEXTURE 1 1 1 1 COLOR ENDTEXTURE")]
        [TestCase("$face MATERIAL TEXTURE TEXTURE 128 SIZE 1 1 1 1 FILL ENDTEXTURE ENDTEXTURE")]
        [TestCase("$face MATERIAL NORMAL")]
        [TestCase("$face MATERIAL_RESET")]
        [TestCase("$face MATERIAL MATERIAL_RESET")]
        [TestCase("$face MATERIAL")]
        [TestCase("$face MATERIAL 1 1 1 1 COLOR $face MATERIAL 0 0 0 1 COLOR")]
        [TestCase("$face MATERIAL 1 1 1 1 COLOR 0 0 1 1 COLOR")]
        public void RejectsClosedGrammarViolations(string source)
        {
            Assert.That(MaterialStackMachineParser.TryParse(source, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domain, Is.EqualTo("material"));
        }

        [Test]
        public void ExecutionRejectsMissingMaterialAttacherWithoutTouchingTheTextureHost()
        {
            GameObject root = new GameObject("MaterialStackMachineMissingTemplateTests");
            try
            {
                TextureStackMachineHost host = root.AddComponent<TextureStackMachineHost>();
                MaterialStackMachine machine = root.AddComponent<MaterialStackMachine>();
                Assert.That(machine.TryExecute("$body MATERIAL 1 1 1 1 COLOR", out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialAttacherRequired"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExecutionUsesLogicalMaterialWordAsTheAttacherEntryName()
        {
            GameObject root = new GameObject("MaterialStackMachineDuplicateEntryTests");
            try
            {
                MaterialAttacher attacher = root.AddComponent<MaterialAttacher>();
                MaterialStackMachine machine = root.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                Assert.That(machine.TryExecute("$body MATERIAL 1 1 1 1 COLOR", out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                operation.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MaterialStackMachineDoesNotSerializeARedundantMaterialBindingTable()
        {
            Assert.That(typeof(MaterialStackMachine).GetField("bindings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null);
        }

        [Test]
        public void LatestExecutionDisposesThePriorEscrowOperation()
        {
            GameObject root = new GameObject("MaterialStackMachineLatestTests");
            try
            {
                MaterialStackMachine machine = root.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = root.AddComponent<MaterialAttacher>();
                Assert.That(machine.TryExecute("$body MATERIAL 1 0 0 1 COLOR", out MaterialStackMachineOperation previous, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                MaterialStackMachineResult previousResult = null;
                previous.Completed += result => previousResult = result;
                Assert.That(machine.TryExecuteLatest("$body MATERIAL 0 0 1 1 COLOR", out MaterialStackMachineOperation latest, out StackMachineDiagnostic latestDiagnostic), Is.True, latestDiagnostic?.message);
                Assert.That(previous.IsCompleted, Is.True);
                Assert.That(previousResult.Code, Is.EqualTo(MaterialStackMachineResultCode.Rejected));
                Assert.That(previousResult.Diagnostic.domainCode, Is.EqualTo("OperationDisposed"));
                latest.Dispose();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CurrentCanvasStub_ResolvesProxyTextureWithoutLegacyTemplate()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            GameObject root = new GameObject("MaterialStackMachineCurrentCanvasStubTests");
            Material source = new Material(shader);
            Texture2D texture = new Texture2D(1, 1);
            MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            TextureBindingTemplate reservedTemplate = null;
            try
            {
                source.SetTexture("_BaseMap", texture);
                SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = source;
                MaterialProxy proxy = root.AddComponent<MaterialProxy>();
                typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, new List<MaterialProxyEntry>
                {
                    new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter }
                });
                Assert.That(MaterialStackMachineParser.TryParse("$body MATERIAL TEXTURE $current CANVAS . ENDTEXTURE", out MaterialStackMachinePlan plan, out StackMachineDiagnostic parseDiagnostic), Is.True, parseDiagnostic?.message);

                MethodInfo method = null;
                MethodInfo[] methods = typeof(MaterialStackMachine).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    if (methods[i].Name == "TryCreateTextureStub" && parameters.Length == 5 && parameters[1].ParameterType == typeof(TextureBindingTemplate)) { method = methods[i]; break; }
                }
                Assert.That(method, Is.Not.Null);
                object[] arguments = { plan.Blocks[0], null, proxy, null, null };
                Assert.That((bool)method.Invoke(null, arguments), Is.True, ((StackMachineDiagnostic)arguments[4])?.message);
                TextureRecipeStub stub = (TextureRecipeStub)arguments[3];
                Assert.That(stub.Bindings, Has.Length.EqualTo(2));
                Assert.That(stub.Bindings[0].logicalName, Is.EqualTo("out"));
                Assert.That(stub.Bindings[1].logicalName, Is.EqualTo("current"));
                Assert.That(stub.Bindings[1].sourceTexture, Is.SameAs(texture));

                reservedTemplate = ScriptableObject.CreateInstance<TextureBindingTemplate>();
                reservedTemplate.SetBindings(new[]
                {
                    new TextureTemplateEntry { word = "out", kind = TextureBindingKind.OutputHall },
                    new TextureTemplateEntry { word = "current", kind = TextureBindingKind.SourceTexture, texture = texture }
                });
                Assert.That(MaterialStackMachineParser.TryParse("$body MATERIAL TEXTURE $skin CANVAS . ENDTEXTURE", out MaterialStackMachinePlan legacyPlan, out parseDiagnostic), Is.True, parseDiagnostic?.message);
                object[] reservedArguments = { legacyPlan.Blocks[0], reservedTemplate, proxy, null, null };
                Assert.That((bool)method.Invoke(null, reservedArguments), Is.False);
                Assert.That(((StackMachineDiagnostic)reservedArguments[4]).domainCode, Is.EqualTo("TextureTemplateInvalid"));

                object[] missingProxyArguments = { plan.Blocks[0], null, null, null, null };
                Assert.That((bool)method.Invoke(null, missingProxyArguments), Is.False);
                Assert.That(((StackMachineDiagnostic)missingProxyArguments[4]).domainCode, Is.EqualTo("CurrentTextureProxyRequired"));
            }
            finally
            {
                Object.DestroyImmediate(reservedTemplate);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(root);
            }
        }

    }
}
