// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class ShapeDirectorRuntimeTests
    {
 #if SHAPESYNC_RICH_TEST
        [UnityTest]
        public IEnumerator ShapeDocument_BFixture_RecoveryMatchesTheRecompiledTextureOutput()
        {
#if UNITY_EDITOR
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Spec16/ShapeDocument_B.asset";
            GameObject instance = null;
            GameObject deserializerHost = null;
            Material source = null;
            MaterialShaderAdapter adapter = null;
            try
            {
                ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
                Assert.That(document, Is.Not.Null);
                instance = new GameObject("ShapeDocument B recovery test");
                MaterialAttacher attacher = ConfigureUnlitTarget(instance, "iris", out _, out source, out adapter);
                MaterialStackMachine machine = instance.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                MaterialProxy proxy = attacher.Proxy;
                MaterialProxyEntry iris = FindEntry(proxy, "iris");
                Assert.That(iris, Is.Not.Null);

                Assert.That(ShapeSyncDocument.TryCreateSnapshot(document, out ShapeSyncDocument recovery, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                recovery.MaterialRecipe.wordSource = "FIGURE\nMATERIAL_RESET\nFIGURE\n" + FindMaterialBlock(document.MaterialRecipe.wordSource, "iris");
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(recovery, out MaterialStackMachineDispatchOperation recoveryOperation, out diagnostic), Is.True, diagnostic?.message);
                while (!recoveryOperation.IsCompleted) yield return null;
                Assert.That(recoveryOperation.TargetCompletions[0].Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                Material recoveryMaterial = IrisMaterial(iris);
                Texture recoveryTexture = recoveryMaterial.GetTexture("_BaseMap");
                Assert.That(recoveryTexture, Is.TypeOf<RenderTexture>(), "Iris recovery retained the original BaseColor texture instead of the Texture StackMachine delivery.");
                Vector4 recoveryPixel = ReadPixel(recoveryTexture as RenderTexture);

                deserializerHost = new GameObject("ShapeDocument B deserializer");
                ShapeDocumentDeserializer deserializer = deserializerHost.AddComponent<ShapeDocumentDeserializer>();
                Assert.That(deserializer.TryDeserialize(documentPath, out List<ShapeSyncShape> shapes), Is.True);
                Assert.That(ShapeSyncShapeResolver.TryResolve(shapes, out List<ShapeSyncShape> physical, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(ShapeSyncEntryMerge.TryMerge(physical, out _, out List<ShapeSyncMergedEntry> material, out diagnostic), Is.True, diagnostic?.message);
                material.RemoveAll(entry => !(entry.Entry is TextureEntry textureEntry)
                    || !string.IsNullOrEmpty(textureEntry.RegistryId)
                    || textureEntry.ProxyEntry != "iris");
                Assert.That(ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), material, out string recompiled, out diagnostic), Is.True, diagnostic?.message);
                var runtimePayload = new ShapeSyncDocument { MaterialBinding = document.MaterialBinding, MaterialRecipe = new MaterialRecipeDocument { wordSource = recompiled } };
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(runtimePayload, out MaterialStackMachineDispatchOperation runtimeOperation, out diagnostic), Is.True, diagnostic?.message);
                while (!runtimeOperation.IsCompleted) yield return null;
                Assert.That(runtimeOperation.TargetCompletions[0].Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                Texture runtimeTexture = IrisMaterial(iris).GetTexture("_BaseMap");
                Assert.That(runtimeTexture, Is.TypeOf<RenderTexture>());
                Vector4 runtimePixel = ReadPixel(runtimeTexture as RenderTexture);

                Assert.That(Vector4.Distance(recoveryPixel, runtimePixel), Is.LessThanOrEqualTo(.005f), "Saved recovery and the equivalent runtime compilation must produce the same iris Texture pixel.");
            }
            finally
            {
                if (deserializerHost != null) Object.Destroy(deserializerHost);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                if (instance != null) Object.Destroy(instance);
            }
#else
            Assert.Ignore("ShapeDocument fixture inspection is Editor-only.");
            yield break;
#endif
        }
#endif

        [Test]
        public void OutfitMaterialRoute_DoesNotCreateRendererLocalRuntimeRoute()
        {
            GameObject outfitRoot = new GameObject("shape-director-outfit-material-source");
            GameObject rendererRoot = new GameObject("shape-director-outfit-renderer-clone");
            MaterialShaderAdapter adapter = null;
            try
            {
                rendererRoot.transform.SetParent(outfitRoot.transform, false);
                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                SkinnedMeshRenderer renderer = rendererRoot.AddComponent<SkinnedMeshRenderer>();
                MaterialProxy sourceProxy = outfitRoot.AddComponent<MaterialProxy>();
                MaterialAttacher sourceAttacher = outfitRoot.AddComponent<MaterialAttacher>();
                sourceAttacher.Proxy = sourceProxy;
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sourceProxy, new List<MaterialProxyEntry>
                {
                    new MaterialProxyEntry { entryName = "hair", renderer = renderer, materialChannel = 0, adapter = adapter, configuredValues = new MaterialProxySemanticValues { applyColor = true, color = Color.magenta } }
                });
                MaterialStackMachine sourceMachine = outfitRoot.AddComponent<MaterialStackMachine>();
                sourceMachine.MaterialAttacher = sourceAttacher;

                Assert.That(typeof(OutfitAttacher).GetMethod("TryCreateRuntimeMaterialRoute", BindingFlags.Static | BindingFlags.NonPublic), Is.Null,
                    "Outfit attach must retain the cloned Root route rather than synthesizing a renderer-local Material route.");
            }
            finally
            {
                if (adapter != null) Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(outfitRoot);
            }
        }

        [UnityTest]
        public IEnumerator OutfitMaterialRoute_ActualAttachDispatchAndDetachRetainsOutfitRootRoute()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director-outfit-route-figure");
            GameObject outfitRoot = new GameObject("shape-director-outfit-route-source");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            Mesh mesh = null;
            Mesh figureMesh = null;
            OutfitSkinningProfile skinningProfile = null;
            CharacterBoneRegistry extraBoneRegistry = null;
            MaterialBinding binding = null;
            try
            {
                Animator animator = figure.AddComponent<Animator>();
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();
                OutfitAttacher outfitAttacher = figure.AddComponent<OutfitAttacher>();
                MaterialStackMachine figureMachine = figure.AddComponent<MaterialStackMachine>();
                Transform figureRoot = new GameObject("Root").transform;
                figureRoot.SetParent(figure.transform, false);
                Transform figureHead = new GameObject("Head").transform;
                figureHead.SetParent(figureRoot, false);
                GameObject figureRendererRoot = new GameObject("Figure Renderer");
                figureRendererRoot.transform.SetParent(figure.transform, false);
                SkinnedMeshRenderer figureRenderer = figureRendererRoot.AddComponent<SkinnedMeshRenderer>();
                figureMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                figureRenderer.sharedMesh = figureMesh;
                typeof(DynamicBoneBlender).GetField("targets", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(blender, new List<DynamicBoneBlendTarget>());
                typeof(DynamicBoneBlender).GetField("targetSkinnedMeshRenderer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(blender, figureRenderer);
                outfitAttacher.ConfigureForFigure(blender, animator);

                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                typeof(ShapeSyncOutfit).GetField("registryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "route-hair");
                typeof(ShapeSyncOutfit).GetField("registryName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "Route Hair");
                Transform outfitExtraBone = new GameObject("Root").transform;
                outfitExtraBone.SetParent(outfitRoot.transform, false);
                outfitExtraBone = new GameObject("Head").transform;
                outfitExtraBone.SetParent(outfitRoot.transform.Find("Root"), false);
                Transform hairBone = new GameObject("J_Sec_Hair").transform;
                hairBone.SetParent(outfitExtraBone, false);
                GameObject rendererRoot = new GameObject("Hair");
                rendererRoot.transform.SetParent(outfitRoot.transform, false);
                SkinnedMeshRenderer sourceRenderer = rendererRoot.AddComponent<SkinnedMeshRenderer>();
                mesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 },
                    bindposes = new[] { Matrix4x4.identity },
                    boneWeights = new[]
                    {
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                    }
                };
                sourceRenderer.sharedMesh = mesh;
                sourceRenderer.bones = new[] { hairBone };
                sourceRenderer.rootBone = hairBone;
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                source = new Material(shader);
                sourceRenderer.sharedMaterial = source;
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                MaterialProxy sourceProxy = outfitRoot.AddComponent<MaterialProxy>();
                typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sourceProxy, new List<MaterialProxyEntry>
                {
                    new MaterialProxyEntry { entryName = "hair", renderer = sourceRenderer, materialChannel = 0, adapter = adapter }
                });
                MaterialAttacher sourceAttacher = outfitRoot.AddComponent<MaterialAttacher>();
                sourceAttacher.Proxy = sourceProxy;
                MaterialStackMachine sourceMachine = outfitRoot.AddComponent<MaterialStackMachine>();
                sourceMachine.MaterialAttacher = sourceAttacher;
                skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
                skinningProfile.SetRendererProfiles(new List<OutfitSkinningRendererProfile>
                {
                    new OutfitSkinningRendererProfile { rendererPath = "Hair", baseBindposes = new[] { Matrix4x4.identity } }
                });
                skinningProfile.SetUsesBcpBakedBindposesForEditor(true);
                extraBoneRegistry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
                extraBoneRegistry.bonePoses.Add(new BonePoseData { boneName = "Root/Head/J_Sec_Hair" });
                typeof(ShapeSyncOutfit).GetField("skinningProfile", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, skinningProfile);
                typeof(ShapeSyncOutfit).GetField("baseExtraBoneRegistry", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, extraBoneRegistry);
                typeof(ShapeSyncOutfit).GetField("fbmExtraBoneRegistries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, new List<ShapeSyncOutfitFbmExtraBoneRegistry>());

                string attachRejection = null;
                void CaptureAttachLog(string condition, string _, LogType type)
                {
                    if (type == LogType.Warning && condition != null && condition.StartsWith("OutfitAttacher rejected outfit attach")) attachRejection = condition;
                }
                Application.logMessageReceived += CaptureAttachLog;
                bool attached = outfitAttacher.TryAttach(outfit);
                Application.logMessageReceived -= CaptureAttachLog;
                Assert.That(attached, Is.True, attachRejection);
                yield return null;
                Assert.That(outfitAttacher.TryGetAttachedMaterialStackMachine("route-hair", out MaterialStackMachine runtimeMachine, out StackMachineDiagnostic lookupDiagnostic), Is.True, lookupDiagnostic?.message);
                Assert.That(runtimeMachine, Is.Not.SameAs(sourceMachine));
                GameObject runtimeOutfitRoot = outfitAttacher.AttachedOutfits[0].RuntimeOutfitInstance;
                Assert.That(runtimeOutfitRoot, Is.Not.Null, "The cloned Outfit Root is the retained runtime ownership boundary.");
                Assert.That(runtimeOutfitRoot.transform.parent, Is.SameAs(figure.transform));
                Assert.That(runtimeMachine.gameObject, Is.SameAs(runtimeOutfitRoot));
                Transform transplantedHairBone = figure.transform.Find("Root/Head/J_Sec_Hair");
                Assert.That(transplantedHairBone, Is.Not.Null, "Registered Extra Bone must be transplanted into the Figure hierarchy.");
                Assert.That(runtimeOutfitRoot.transform.Find("Root"), Is.Null,
                    "The cloned Outfit source skeleton must be released after its bones are rebound to the Figure.");

                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                var payload = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "$route-hair OUTFIT $hair MATERIAL 0 1 0 1 COLOR" }
                };
                Assert.That(figureMachine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation dispatch, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!dispatch.IsCompleted) yield return null;
                Assert.That(dispatch.TargetCompletions[0].Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                MaterialProxy runtimeProxy = runtimeMachine.MaterialAttacher.Proxy;
                SkinnedMeshRenderer runtimeRenderer = runtimeProxy.Entries[0].renderer;
                Assert.That(runtimeRenderer.transform.IsChildOf(runtimeOutfitRoot.transform), Is.True, "The renderer remains below its retained Outfit Root.");
                Assert.That(runtimeRenderer.bones[0], Is.SameAs(transplantedHairBone));
                Assert.That(runtimeRenderer.rootBone, Is.Null);
                Assert.That(runtimeRenderer.sharedMaterial, Is.Not.SameAs(source));
                Assert.That(runtimeRenderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.green));

                Assert.That(outfitAttacher.Detach("route-hair"), Is.True);
                yield return null;
                Assert.That(runtimeMachine == null, Is.True);
                Assert.That(outfitAttacher.TryGetAttachedMaterialStackMachine("route-hair", out _, out StackMachineDiagnostic detachedDiagnostic), Is.False);
                Assert.That(detachedDiagnostic.domainCode, Is.EqualTo("OutfitRegistryMissing"));
            }
            finally
            {
                if (binding != null) Object.Destroy(binding);
                if (extraBoneRegistry != null) Object.Destroy(extraBoneRegistry);
                if (skinningProfile != null) Object.Destroy(skinningProfile);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                if (mesh != null) Object.Destroy(mesh);
                if (figureMesh != null) Object.Destroy(figureMesh);
                Object.Destroy(figure);
                Object.Destroy(outfitRoot);
            }
#else
            Assert.Ignore("Runtime Outfit route coverage is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compile_DispatchesMaterialDirectlyAndCommitsSnapshotAfterTerminalSuccess()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding binding = null;
            SkinShapeTemplate template = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(figure, out SkinnedMeshRenderer renderer, out source, out adapter);
                MaterialStackMachine machine = figure.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, binding);
                template = ScriptableObject.CreateInstance<SkinShapeTemplate>();
                template.ShapeId = "skin";
                template.Parts.Add(new ColorEntry { RegistryId = string.Empty, ProxyEntry = "body", Color = Color.green });

                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                yield return null;

                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.green));
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                Assert.That((string)typeof(ShapeDirector).GetField("lastMaterialSource", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.EqualTo("FIGURE"));
            }
            finally
            {
                if (template != null) Object.Destroy(template);
                if (binding != null) Object.Destroy(binding);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Director runtime routing setup is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compile_FigureMaterialRejectKeepsPreviousPhysicalSnapshot()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director-reject");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding binding = null;
            MeshBinding meshBinding = null;
            SkinShapeTemplate template = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(figure, out _, out source, out adapter);
                MaterialStackMachine machine = figure.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MeshStackMachine>();
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, binding);
                typeof(ShapeDirector).GetField("meshBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, meshBinding);
                MaterialProxy proxy = figure.GetComponent<MaterialProxy>();
                List<MaterialProxyEntry> entries = (List<MaterialProxyEntry>)typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(proxy);
                entries.Add(new MaterialProxyEntry { entryName = "broken", renderer = null, materialChannel = 0, adapter = adapter });
                template = ScriptableObject.CreateInstance<SkinShapeTemplate>();
                template.ShapeId = "invalid-skin";
                template.Parts.Add(new ColorEntry { RegistryId = string.Empty, ProxyEntry = "missing", Color = Color.red });
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);

                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                yield return null;
                Assert.That((string)typeof(ShapeDirector).GetField("lastMaterialSource", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Does.Contain("$missing MATERIAL"));
                LogAssert.Expect(LogType.Warning, new Regex("Shape Director recovery Material phase failed\\. ResetDryRunRejected:"));
                yield return null;
                Assert.That(director.LastTransactionDiagnostic, Is.Not.Null);
                Assert.That(director.LastTransactionDiagnostic.domainCode, Is.EqualTo("ResetDryRunRejected"));
            }
            finally
            {
                if (template != null) Object.Destroy(template);
                if (meshBinding != null) Object.Destroy(meshBinding);
                if (binding != null) Object.Destroy(binding);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Director runtime routing setup is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compile_OutfitMaterialFailurePolicyFalseCommitsOnlySuccessfulTargets()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director-partial");
            GameObject outfitRoot = new GameObject("shape-director-partial-outfit");
            Material figureSource = null;
            Material outfitSource = null;
            MaterialShaderAdapter figureAdapter = null;
            MaterialShaderAdapter outfitAdapter = null;
            MaterialBinding binding = null;
            SkinShapeTemplate template = null;
            try
            {
                OutfitAttacher outfitAttacher = figure.AddComponent<OutfitAttacher>();
                MaterialAttacher figureAttacher = ConfigureUnlitTarget(figure, out SkinnedMeshRenderer figureRenderer, out figureSource, out figureAdapter);
                MaterialStackMachine figureMachine = figure.AddComponent<MaterialStackMachine>();
                figureMachine.MaterialAttacher = figureAttacher;
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, binding);

                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                typeof(ShapeSyncOutfit).GetField("registryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "hat");
                typeof(ShapeSyncOutfit).GetField("registryName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "Hat");
                MaterialAttacher outfitAttacherComponent = ConfigureUnlitTarget(outfitRoot, out _, out outfitSource, out outfitAdapter);
                MaterialStackMachine outfitMachine = outfitRoot.AddComponent<MaterialStackMachine>();
                outfitMachine.MaterialAttacher = outfitAttacherComponent;
                RegisterAttachedOutfit(outfitAttacher, outfit, outfitMachine);

                template = ScriptableObject.CreateInstance<SkinShapeTemplate>();
                template.ShapeId = "partial";
                template.Parts.Add(new ColorEntry { RegistryId = string.Empty, ProxyEntry = "body", Color = Color.green });
                template.Parts.Add(new ColorEntry { RegistryId = "hat", ProxyEntry = "missing", Color = Color.red });
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                yield return null;

                Assert.That(figureRenderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(Color.green));
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                string retrySource = (string)typeof(ShapeDirector).GetField("lastMaterialSource", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director);
                Assert.That(retrySource, Does.Contain("$hat OUTFIT"));
                Assert.That(retrySource, Does.Not.Contain("$body MATERIAL"));
            }
            finally
            {
                if (template != null) Object.Destroy(template);
                if (binding != null) Object.Destroy(binding);
                if (figureAdapter != null) Object.Destroy(figureAdapter);
                if (outfitAdapter != null) Object.Destroy(outfitAdapter);
                if (figureSource != null) Object.Destroy(figureSource);
                if (outfitSource != null) Object.Destroy(outfitSource);
                Object.Destroy(outfitRoot);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Director runtime routing setup is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Compile_OutfitMaterialFailurePolicyTrueRecoversAllTargetMaterials()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director-abort-outfit");
            GameObject outfitRoot = new GameObject("shape-director-abort-outfit-target");
            Material figureSource = null;
            Material outfitSource = null;
            MaterialShaderAdapter figureAdapter = null;
            MaterialShaderAdapter outfitAdapter = null;
            MaterialBinding materialBinding = null;
            MeshBinding meshBinding = null;
            SkinShapeTemplate template = null;
            try
            {
                OutfitAttacher outfitAttacher = figure.AddComponent<OutfitAttacher>();
                MaterialAttacher figureAttacher = ConfigureUnlitTarget(figure, out SkinnedMeshRenderer figureRenderer, out figureSource, out figureAdapter);
                MaterialStackMachine figureMachine = figure.AddComponent<MaterialStackMachine>();
                figureMachine.MaterialAttacher = figureAttacher;
                figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<MeshStackMachine>();
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                director.AbortOnOutfitMaterialFailure = true;
                materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
                meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, materialBinding);
                typeof(ShapeDirector).GetField("meshBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, meshBinding);

                ShapeSyncOutfit outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                typeof(ShapeSyncOutfit).GetField("registryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "hat");
                typeof(ShapeSyncOutfit).GetField("registryName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfit, "Hat");
                MaterialAttacher outfitMaterialAttacher = ConfigureUnlitTarget(outfitRoot, out _, out outfitSource, out outfitAdapter);
                MaterialStackMachine outfitMachine = outfitRoot.AddComponent<MaterialStackMachine>();
                outfitMachine.MaterialAttacher = outfitMaterialAttacher;
                RegisterAttachedOutfit(outfitAttacher, outfit, outfitMachine);
                Assert.That(outfitAttacher.TryGetAttachedOutfit("hat", out _, out StackMachineDiagnostic outfitDiagnostic), Is.True, outfitDiagnostic?.message);
                Assert.That(outfitAttacher.TryGetAttachedMaterialStackMachine("hat", out MaterialStackMachine resolvedOutfitMachine, out outfitDiagnostic), Is.True, outfitDiagnostic?.message);
                Assert.That(resolvedOutfitMachine, Is.SameAs(outfitMachine));

                template = ScriptableObject.CreateInstance<SkinShapeTemplate>();
                template.ShapeId = "abort-outfit";
                template.Parts.Add(new ColorEntry { RegistryId = string.Empty, ProxyEntry = "body", Color = Color.green });
                template.Parts.Add(new ColorEntry { RegistryId = "hat", ProxyEntry = "missing", Color = Color.red });
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                yield return null;
                yield return null;

                Assert.That(figureRenderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(figureSource.GetColor("_BaseColor")), "Policy=true must reset Figure material after any Outfit Material failure.");
                Assert.That((bool)typeof(ShapeDirector).GetField("transactionInFlight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.False);
                Assert.That(director.LastTransactionDiagnostic, Is.Null);
            }
            finally
            {
                if (template != null) Object.Destroy(template);
                if (meshBinding != null) Object.Destroy(meshBinding);
                if (materialBinding != null) Object.Destroy(materialBinding);
                if (figureAdapter != null) Object.Destroy(figureAdapter);
                if (outfitAdapter != null) Object.Destroy(outfitAdapter);
                if (figureSource != null) Object.Destroy(figureSource);
                if (outfitSource != null) Object.Destroy(outfitSource);
                Object.Destroy(outfitRoot);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Director runtime routing setup is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator DisableDuringMaterialWait_DefersRecoveryUntilDirectorIsEnabledAgain()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director-disable-recovery");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding materialBinding = null;
            MeshBinding meshBinding = null;
            SkinShapeTemplate template = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(figure, out _, out source, out adapter);
                MaterialStackMachine materialMachine = figure.AddComponent<MaterialStackMachine>();
                materialMachine.MaterialAttacher = attacher;
                figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MeshStackMachine>();
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
                meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, materialBinding);
                typeof(ShapeDirector).GetField("meshBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, meshBinding);
                template = ScriptableObject.CreateInstance<SkinShapeTemplate>();
                template.ShapeId = "deferred";
                template.Parts.Add(new ColorEntry { ProxyEntry = "missing", Color = Color.red });
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);

                director.enabled = false;
                yield return null;
                Assert.That((bool)typeof(ShapeDirector).GetField("recoveryRequestedOnEnable", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.True);
                director.enabled = true;
                yield return null;
                yield return null;

                Assert.That((bool)typeof(ShapeDirector).GetField("transactionInFlight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.False);
                Assert.That(director.LastTransactionDiagnostic, Is.Null, "Deferred recovery resets the valid target-wide Proxy and restores the empty snapshot.");
            }
            finally
            {
                if (template != null) Object.Destroy(template);
                if (meshBinding != null) Object.Destroy(meshBinding);
                if (materialBinding != null) Object.Destroy(materialBinding);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Director runtime routing setup is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator InactiveFigureDisposesMaterialMachineOperationAndDirectorRecoversOnReenable()
        {
#if UNITY_EDITOR
            GameObject figure = new GameObject("shape-director-machine-disable-recovery");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding materialBinding = null;
            MeshBinding meshBinding = null;
            SkinShapeTemplate template = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(figure, out _, out source, out adapter);
                MaterialStackMachine materialMachine = figure.AddComponent<MaterialStackMachine>();
                materialMachine.MaterialAttacher = attacher;
                figure.AddComponent<DynamicBoneBlender>();
                figure.AddComponent<OutfitAttacher>();
                figure.AddComponent<MeshStackMachine>();
                ShapeDirector director = figure.AddComponent<ShapeDirector>();
                director.AutoCompile = false;
                materialBinding = ScriptableObject.CreateInstance<MaterialBinding>();
                meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                typeof(ShapeDirector).GetField("materialBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, materialBinding);
                typeof(ShapeDirector).GetField("meshBinding", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, meshBinding);
                template = ScriptableObject.CreateInstance<SkinShapeTemplate>();
                template.ShapeId = "machine-deferred";
                template.Parts.Add(new ColorEntry { ProxyEntry = "missing", Color = Color.red });
                Assert.That(director.TryAddTemplate(template, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(director.TryCompile(out diagnostic), Is.True, diagnostic?.message);
                Assert.That(materialMachine.IsBusy, Is.True, "The Material Machine owns the accepted operation before the Figure becomes inactive.");

                figure.SetActive(false);
                yield return null;
                Assert.That(materialMachine.IsBusy, Is.False, "MaterialStackMachine.OnDisable must dispose rather than resume its in-flight operation.");
                Assert.That((bool)typeof(ShapeDirector).GetField("recoveryRequestedOnEnable", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.True);

                figure.SetActive(true);
                yield return null;
                yield return null;

                Assert.That((bool)typeof(ShapeDirector).GetField("transactionInFlight", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director), Is.False);
                Assert.That(materialMachine.IsBusy, Is.False);
                Assert.That(director.LastTransactionDiagnostic, Is.Null, "Director owns the post-enable recovery; the Machine neither resumes nor retains the aborted operation.");
            }
            finally
            {
                if (template != null) Object.Destroy(template);
                if (meshBinding != null) Object.Destroy(meshBinding);
                if (materialBinding != null) Object.Destroy(materialBinding);
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(figure);
            }
#else
            Assert.Ignore("Director runtime routing setup is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static MaterialProxyEntry FindEntry(MaterialProxy proxy, string entryName)
        {
            Assert.That(proxy, Is.Not.Null);
            for (int i = 0; i < proxy.Entries.Count; i++)
                if (proxy.Entries[i].entryName == entryName) return proxy.Entries[i];
            return null;
        }

        private static string FindMaterialBlock(string source, string entryName)
        {
            Assert.That(source, Is.Not.Null);
            Match match = Regex.Match(source, @"\$" + Regex.Escape(entryName) + @"\s+MATERIAL\s+.*?ENDTEXTURE", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Assert.That(match.Success, Is.True, "ShapeDocument fixture has no MATERIAL block for '" + entryName + "'.");
            return match.Value;
        }

        private static Material IrisMaterial(MaterialProxyEntry entry)
        {
            Assert.That(entry.renderer, Is.Not.Null);
            return entry.renderer.sharedMaterials[entry.materialChannel];
        }

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

        private static MaterialAttacher ConfigureUnlitTarget(GameObject target, out SkinnedMeshRenderer renderer, out Material source, out MaterialShaderAdapter adapter)
        {
            return ConfigureUnlitTarget(target, "body", out renderer, out source, out adapter);
        }

        private static MaterialAttacher ConfigureUnlitTarget(GameObject target, string entryName, out SkinnedMeshRenderer renderer, out Material source, out MaterialShaderAdapter adapter)
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
                new MaterialProxyEntry { entryName = entryName, renderer = renderer, materialChannel = 0, adapter = adapter }
            });
            attacher.Proxy = proxy;
            return attacher;
        }

        private static void RegisterAttachedOutfit(OutfitAttacher figureOutfitAttacher, ShapeSyncOutfit outfit, MaterialStackMachine materialMachine = null)
        {
            var attached = new AttachedOutfitRegistrySet(outfit, outfit.gameObject, new List<Transform>(), new List<string>(), new List<OutfitSkinnedMeshBinding>(), new List<Transform>(), null, null, null);
            List<AttachedOutfitRegistrySet> registry = (List<AttachedOutfitRegistrySet>)typeof(OutfitAttacher).GetField("attachedOutfits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(figureOutfitAttacher);
            registry.Add(attached);
        }
#endif
    }
}
