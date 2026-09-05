// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>Black-box phase-boundary tests for Spec15.2 Material StackMachine escrow orchestration.</summary>
    public sealed class MaterialStackMachineRuntimeTests
    {
        [UnityTest]
        public IEnumerator TextureEscrowCompletesBeforeTheAttacherCommits()
        {
#if UNITY_EDITOR
            GameObject target = new GameObject("MaterialStackMachineEscrowTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            TextureBindingTemplate textureTemplate = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                textureTemplate = CreateOutputTemplate();
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                machine.TextureBindingTemplate = textureTemplate;

                const string recipe = "$body MATERIAL TEXTURE 256 128 RECTSIZE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE 0 1 0 1 COLOR";
                Assert.That(machine.TryExecute(recipe, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(renderer.sharedMaterial, Is.SameAs(source), "The Material Attacher must not be touched while the Texture delivery is still escrowed.");

                while (!operation.IsCompleted) yield return null;
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(source), "Commit begins only after the escrow phase finishes.");
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.green));
                Texture deliveryTexture = renderer.sharedMaterial.GetTexture("_BaseMap");
                Assert.That(deliveryTexture, Is.Not.Null);
                Assert.That(deliveryTexture.width, Is.EqualTo(256));
                Assert.That(deliveryTexture.height, Is.EqualTo(128));
            }
            finally
            {
                if (textureTemplate != null) Object.Destroy(textureTemplate);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator TextureCompileFailurePreventsEveryMaterialCommit()
        {
#if UNITY_EDITOR
            GameObject bodyTarget = new GameObject("MaterialStackMachineBodyFailureTests");
            Material bodySource = null;
            MaterialShaderAdapter bodyAdapter = null;
            TextureBindingTemplate textureTemplate = null;
            try
            {
                MaterialAttacher bodyAttacher = ConfigureUnlitTarget(bodyTarget, out SkinnedMeshRenderer bodyRenderer, out bodySource, out bodyAdapter);
                textureTemplate = CreateOutputTemplate();
                MaterialStackMachine machine = bodyTarget.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = bodyAttacher;
                machine.TextureBindingTemplate = textureTemplate;

                const string recipe = "$body MATERIAL 0 1 0 1 COLOR $face MATERIAL TEXTURE NOT_A_TEXTURE_WORD ENDTEXTURE";
                Assert.That(machine.TryExecute(recipe, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic, Is.Not.Null, "The nested Texture compiler should reject before Material phase begins.");
                yield return null;
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource), "A valid preceding MATERIAL block must remain untouched when a later Texture block rejects.");
            }
            finally
            {
                if (textureTemplate != null) Object.Destroy(textureTemplate);
                if (bodyAdapter != null) Object.Destroy(bodyAdapter);
                if (bodySource != null) Object.Destroy(bodySource);
                Object.Destroy(bodyTarget);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator ColorOnlyTransactionDoesNotRequireATextureHost()
        {
#if UNITY_EDITOR
            GameObject target = new GameObject("MaterialStackMachineColorOnlyTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;

                Assert.That(machine.TryExecute("$body MATERIAL 2 3 0.25 0.5 UVSET 0 0 1 1 COLOR", out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!operation.IsCompleted) yield return null;
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.blue));
                Assert.That(renderer.sharedMaterial.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2f, 3f)));
                Assert.That(renderer.sharedMaterial.GetTextureOffset("_BaseMap"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
            }
            finally
            {
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DocumentMaterialBinding_ExecutesTextureSourceAndTransfersRenderTextureWithoutLegacyTemplate()
        {
#if UNITY_EDITOR
            GameObject target = new GameObject("MaterialStackMachineDocumentBindingTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding binding = null;
            Texture2D texture = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                texture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                texture.SetPixel(0, 0, Color.magenta);
                texture.Apply(false, false);
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(MaterialBinding).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MaterialTextureBindingEntry>
                {
                    new MaterialTextureBindingEntry { logicalName = "source", sourceTexture = texture }
                });
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                var payload = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE $source CANVAS . ENDTEXTURE" }
                };

                Assert.That(machine.TryAcceptRecipePayload(payload, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!operation.IsCompleted) yield return null;
                Texture delivery = renderer.sharedMaterial.GetTexture("_BaseMap");
                Assert.That(delivery, Is.TypeOf<RenderTexture>());
                Assert.That(delivery.width, Is.EqualTo(128));
                Assert.That(delivery.height, Is.EqualTo(128));
            }
            finally
            {
                if (texture != null) Object.Destroy(texture);
                if (binding != null) Object.Destroy(binding);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DocumentMaterialBinding_FigureTargetFailureDoesNotAbortAttachedOutfitTarget()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("MaterialStackMachineFigureLocalAbortTests");
            GameObject outfitRoot = new GameObject("MaterialStackMachineOutfitLocalAbortTests");
            Material outfitSource = null;
            MaterialShaderAdapter outfitAdapter = null;
            MaterialBinding binding = null;
            Texture2D texture = null;
            try
            {
                OutfitAttacher outfitAttacher = figure.AddComponent<OutfitAttacher>();
                MaterialAttacher figureMaterialAttacher = figure.AddComponent<MaterialAttacher>();
                MaterialStackMachine figureMachine = figure.AddComponent<MaterialStackMachine>();
                figureMachine.MaterialAttacher = figureMaterialAttacher;

                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                typeof(ShapeSyncOutfit).GetField("registryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "hat");
                typeof(ShapeSyncOutfit).GetField("registryName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "Hat");
                MaterialAttacher outfitMaterialAttacher = ConfigureUnlitTarget(outfitRoot, out SkinnedMeshRenderer outfitRenderer, out outfitSource, out outfitAdapter);
                MaterialStackMachine outfitMachine = outfitRoot.AddComponent<MaterialStackMachine>();
                outfitMachine.MaterialAttacher = outfitMaterialAttacher;
                RegisterAttachedOutfit(outfitAttacher, outfit, outfitMachine);

                texture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                texture.SetPixel(0, 0, Color.magenta);
                texture.Apply(false, false);
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(MaterialBinding).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MaterialTextureBindingEntry>
                {
                    new MaterialTextureBindingEntry { logicalName = "source", sourceTexture = texture }
                });
                var payload = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument
                    {
                        wordSource = "$missing MATERIAL TEXTURE $absent CANVAS . ENDTEXTURE $hat OUTFIT $body MATERIAL 0 0 1 1 COLOR"
                    }
                };

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Material StackMachine skipped Figure target"));
                Assert.That(figureMachine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                int completionCount = 0;
                operation.Completed += _ => completionCount++;
                while (!operation.IsCompleted) yield return null;

                Assert.That(outfitRenderer.sharedMaterial, Is.Not.SameAs(outfitSource), "A Figure-local reject must not prevent an attached Outfit transaction from committing.");
                Assert.That(outfitRenderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.blue));
                Assert.That(operation.TargetCompletions.Count, Is.EqualTo(2));
                Assert.That(operation.TargetCompletions, Is.Not.TypeOf<MaterialTargetCompletion[]>());
                Assert.That(operation.TargetCompletions[0].RegistryId, Is.Empty);
                Assert.That(operation.TargetCompletions[0].Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Rejected));
                Assert.That(operation.TargetCompletions[1].RegistryId, Is.EqualTo("hat"));
                Assert.That(operation.TargetCompletions[1].Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                Assert.That(completionCount, Is.EqualTo(1));
            }
            finally
            {
                if (texture != null) Object.Destroy(texture);
                if (binding != null) Object.Destroy(binding);
                if (outfitAdapter != null) Object.Destroy(outfitAdapter);
                if (outfitSource != null) Object.Destroy(outfitSource);
                Object.Destroy(outfitRoot);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Document material routing tests require UnityEditor-only test setup.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator UnsupportedBaseColorTextureReportsPartialAppliedAndCommitsColor()
        {
#if UNITY_EDITOR
            GameObject target = new GameObject("MaterialStackMachinePartialAppliedTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            TextureBindingTemplate textureTemplate = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                typeof(MaterialShaderAdapter).GetField("assignmentTemplates", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter, new List<MaterialPropertyBindingTemplate>
                {
                    new MaterialPropertyBindingTemplate { propertyName = "_BaseColor", writeKind = MaterialPropertyWriteKind.Color, valueSource = MaterialPropertyValueSource.Color, required = true }
                });
                textureTemplate = CreateOutputTemplate();
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                machine.TextureBindingTemplate = textureTemplate;

                Assert.That(machine.TryExecute("$body MATERIAL TEXTURE 128 128 RECTSIZE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE 0 1 0 1 COLOR", out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                MaterialStackMachineResult result = null;
                operation.Completed += value => result = value;
                while (!operation.IsCompleted) yield return null;
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Code, Is.EqualTo(MaterialStackMachineResultCode.PartialApplied));
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.green));
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Null);
            }
            finally
            {
                if (textureTemplate != null) Object.Destroy(textureTemplate);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CompletionRoute_DocumentBindingReportsPartialApplied()
        {
#if UNITY_EDITOR
            GameObject target = new GameObject("MaterialCompletionPartialAppliedTests");
            Material source = null; MaterialShaderAdapter adapter = null; MaterialBinding binding = null; Texture2D texture = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                typeof(MaterialShaderAdapter).GetField("assignmentTemplates", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter, new List<MaterialPropertyBindingTemplate>
                {
                    new MaterialPropertyBindingTemplate { propertyName = "_BaseColor", writeKind = MaterialPropertyWriteKind.Color, valueSource = MaterialPropertyValueSource.Color, required = true }
                });
                texture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true); texture.SetPixel(0, 0, Color.white); texture.Apply(false, false);
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(MaterialBinding).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MaterialTextureBindingEntry> { new MaterialTextureBindingEntry { logicalName = "source", sourceTexture = texture } });
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>(); machine.MaterialAttacher = attacher;
                var payload = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE $source CANVAS . ENDTEXTURE 0 1 0 1 COLOR" } };
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!operation.IsCompleted) yield return null;
                Assert.That(operation.TargetCompletions[0].Result.Code, Is.EqualTo(MaterialStackMachineResultCode.PartialApplied));
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.green));
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Null);
            }
            finally { if (texture != null) Object.Destroy(texture); if (binding != null) Object.Destroy(binding); if (adapter != null) Object.Destroy(adapter); if (source != null) Object.Destroy(source); Object.Destroy(target); }
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DocumentRecoveryResetThenColorize_MatchesTheEquivalentNonResetColorizeOutput()
        {
#if UNITY_EDITOR
            GameObject target = new GameObject("MaterialStackMachineRecoveryColorizeTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding binding = null;
            Texture2D texture = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                texture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
                texture.SetPixel(0, 0, new Color(.75f, .15f, .05f, 1f));
                texture.Apply(false, false);
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(MaterialBinding).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MaterialTextureBindingEntry>
                {
                    new MaterialTextureBindingEntry { logicalName = "source", sourceTexture = texture }
                });
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;

                const string colorize = "$body MATERIAL TEXTURE $source CANVAS 0.61 0.8 0 COLORIZE . ENDTEXTURE";
                Assert.That(machine.TryAcceptRecipePayload(new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "FIGURE\n" + colorize }
                }, out MaterialStackMachineOperation first, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!first.IsCompleted) yield return null;
                Vector4 expected = ReadPixel(renderer.sharedMaterial.GetTexture("_BaseMap") as RenderTexture);

                Assert.That(machine.TryAcceptRecipePayload(new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "FIGURE\nMATERIAL_RESET\nFIGURE\n" + colorize }
                }, out MaterialStackMachineOperation restored, out diagnostic), Is.True, diagnostic?.message);
                while (!restored.IsCompleted) yield return null;
                Vector4 actual = ReadPixel(renderer.sharedMaterial.GetTexture("_BaseMap") as RenderTexture);

                Assert.That(restored.Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                Assert.That(Vector4.Distance(actual, expected), Is.LessThanOrEqualTo(.005f), "A saved recovery recipe must produce the same COLORIZE result after MATERIAL_RESET as the equivalent runtime update.");
            }
            finally
            {
                if (texture != null) Object.Destroy(texture);
                if (binding != null) Object.Destroy(binding);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DryRunFailurePreventsEveryMaterialCommit()
        {
#if UNITY_EDITOR
            GameObject bodyTarget = new GameObject("MaterialStackMachineBodyDryRunTests");
            Material bodySource = null;
            MaterialShaderAdapter bodyAdapter = null;
            try
            {
                MaterialAttacher bodyAttacher = ConfigureUnlitTarget(bodyTarget, out SkinnedMeshRenderer bodyRenderer, out bodySource, out bodyAdapter);
                MaterialStackMachine machine = bodyTarget.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = bodyAttacher;

                Assert.That(machine.TryExecute("$body MATERIAL 0 1 0 1 COLOR $invalid MATERIAL 1 0 0 1 COLOR", out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                MaterialStackMachineResult result = null;
                operation.Completed += value => result = value;
                while (!operation.IsCompleted) yield return null;
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Code, Is.EqualTo(MaterialStackMachineResultCode.Rejected));
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource), "A successful earlier DryRun must not commit before all entries pass DryRun.");
            }
            finally
            {
                if (bodyAdapter != null) Object.Destroy(bodyAdapter);
                if (bodySource != null) Object.Destroy(bodySource);
                Object.Destroy(bodyTarget);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static Vector4 ReadPixel(RenderTexture source)
        {
            Assert.That(source, Is.Not.Null);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source;
                readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(readback);
            }
        }

        private static TextureBindingTemplate CreateOutputTemplate()
        {
            TextureBindingTemplate template = ScriptableObject.CreateInstance<TextureBindingTemplate>();
            template.SetBindings(new[] { new TextureTemplateEntry { word = "out", kind = TextureBindingKind.OutputHall } });
            return template;
        }

        private static MaterialAttacher ConfigureUnlitTarget(GameObject target, out SkinnedMeshRenderer renderer, out Material source, out MaterialShaderAdapter adapter)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            renderer = target.AddComponent<SkinnedMeshRenderer>();
            source = new Material(shader);
            renderer.sharedMaterial = source;
            MaterialProxy proxy = target.AddComponent<MaterialProxy>();
            MaterialAttacher attacher = target.AddComponent<MaterialAttacher>();
            adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter }
            });
            attacher.Proxy = proxy;
            return attacher;
        }

        private static void RegisterAttachedOutfit(OutfitAttacher figureOutfitAttacher, ShapeSyncOutfit outfit, MaterialStackMachine materialMachine = null)
        {
            var attached = new AttachedOutfitRegistrySet(
                outfit,
                outfit.gameObject,
                new List<Transform>(),
                new List<string>(),
                new List<OutfitSkinnedMeshBinding>(),
                new List<Transform>(),
                null,
                null,
                null);
            List<AttachedOutfitRegistrySet> registry = (List<AttachedOutfitRegistrySet>)typeof(OutfitAttacher)
                .GetField("attachedOutfits", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(figureOutfitAttacher);
            registry.Add(attached);
        }

#endif
    }
}
