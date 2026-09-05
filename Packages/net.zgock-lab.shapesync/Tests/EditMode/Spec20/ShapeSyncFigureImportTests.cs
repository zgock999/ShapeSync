// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncFigureImportTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec20FigureImportRoot;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root)) { ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_3_ShapeSyncFigureImportTests"); }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
        }

#if SHAPESYNC_RICH_TEST
        [Test]
        public void GeneratedSpec20PlayTestPrefab_ResolvesEveryRendererMaterial()
        {
            const string prefabPath = "Assets/zgock/ShapeSync/PlayTest/Spec20/Generated/BasicFemale.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, "Human Test generated Prefab must exist.");
            SkinnedMeshRenderer renderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sharedMaterials, Is.All.Not.Null, "Generated SkinnedMeshRenderer must not contain Missing Material references.");
            Assert.That(renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath), Is.All.StartsWith("Assets/zgock/ShapeSync/PlayTest/Spec20/Generated/Materials/"));
        }
#endif

        [Test]
        public void TryAdmit_ResolvesAncestorHumanoidAnimatorAndPreservesSourceHash()
        {
            const string path = Root + "/Humanoid.prefab";
            GameObject source = CreateHumanoidSource("Humanoid", includeRenderer: true, out Avatar avatar);
            try
            {
                GameObject secondBody = new GameObject("SecondBody");
                secondBody.transform.SetParent(source.transform.Find("Body"), false);
                secondBody.AddComponent<SkinnedMeshRenderer>();
                GameObject otherBody = new GameObject("OtherBody");
                otherBody.transform.SetParent(source.transform, false);
                otherBody.AddComponent<SkinnedMeshRenderer>();
                AssetDatabase.CreateAsset(avatar, Root + "/Humanoid.asset");
                source.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, path), Is.Not.Null);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("Body").gameObject;
                Hash128 before = AssetDatabase.GetAssetDependencyHash(path);

                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string diagnostic), Is.True, diagnostic);
                Assert.That(admission.Candidate, Is.SameAs(candidate));
                Assert.That(admission.HumanoidRoot, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(path)));
                Assert.That(admission.Animator, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Animator>()));
                Assert.That(admission.Avatar, Is.SameAs(AssetDatabase.LoadAssetAtPath<Avatar>(Root + "/Humanoid.asset")));
                Assert.That(admission.SourceRenderers.Count, Is.EqualTo(3));
                Assert.That(admission.SourceRenderers[0].name, Is.EqualTo("Body"));
                Assert.That(admission.SourceRenderers[1].name, Is.EqualTo("SecondBody"));
                Assert.That(admission.SourceRenderers[2].name, Is.EqualTo("OtherBody"));
                Assert.That(admission.SourceRenderers, Is.Not.InstanceOf<SkinnedMeshRenderer[]>());
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TryAdmit_RejectsNullAndNonPersistentCandidates()
        {
            Assert.That(ShapeSyncFigureImport.TryAdmit(null, out _, out string nullDiagnostic), Is.False);
            Assert.That(nullDiagnostic, Does.Contain("source GameObject"));
            GameObject sceneObject = new GameObject("SceneOnly");
            try
            {
                Assert.That(ShapeSyncFigureImport.TryAdmit(sceneObject, out _, out string transientDiagnostic), Is.False);
                Assert.That(transientDiagnostic, Does.Contain("persistent"));
            }
            finally { Object.DestroyImmediate(sceneObject); }
        }

        [Test]
        public void TryAdmit_RejectsPersistentCandidateWithoutAnimator()
        {
            const string path = Root + "/Invalid.prefab";
            GameObject source = new GameObject("Invalid");
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, path), Is.Not.Null);
                Assert.That(ShapeSyncFigureImport.TryAdmit(AssetDatabase.LoadAssetAtPath<GameObject>(path), out _, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("Animator"));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TryAdmit_RejectsMissingHumanoidAvatarAndRenderer()
        {
            const string noAvatarPath = Root + "/NoAvatar.prefab";
            GameObject noAvatar = new GameObject("NoAvatar");
            noAvatar.AddComponent<Animator>();
            GameObject noRenderer = CreateHumanoidSource("NoRenderer", includeRenderer: false, out Avatar avatar);
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(noAvatar, noAvatarPath), Is.Not.Null);
                Assert.That(ShapeSyncFigureImport.TryAdmit(AssetDatabase.LoadAssetAtPath<GameObject>(noAvatarPath), out _, out string avatarDiagnostic), Is.False);
                Assert.That(avatarDiagnostic, Does.Contain("Humanoid Avatar"));
                AssetDatabase.CreateAsset(avatar, Root + "/NoRenderer.asset");
                noRenderer.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(noRenderer, Root + "/NoRenderer.prefab"), Is.Not.Null);
                Assert.That(ShapeSyncFigureImport.TryAdmit(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/NoRenderer.prefab"), out _, out string rendererDiagnostic), Is.False);
                Assert.That(rendererDiagnostic, Does.Contain("SkinnedMeshRenderer"));
            }
            finally { Object.DestroyImmediate(noAvatar); Object.DestroyImmediate(noRenderer); }
        }

        [Test]
        public void TryAdmit_RejectsNonHumanoidAndNonPersistentAvatars()
        {
            GameObject genericSource = new GameObject("Generic");
            Animator genericAnimator = genericSource.AddComponent<Animator>();
            genericSource.AddComponent<SkinnedMeshRenderer>();
            Avatar genericAvatar = AvatarBuilder.BuildGenericAvatar(genericSource, string.Empty);
            GameObject humanoidSource = CreateHumanoidSource("Humanoid", includeRenderer: true, out Avatar persistentAvatar);
            GameObject transientAvatarRoot = CreateHumanoidSource("TransientAvatar", includeRenderer: false, out Avatar transientAvatar);
            try
            {
                AssetDatabase.CreateAsset(genericAvatar, Root + "/Generic.asset"); genericAnimator.avatar = genericAvatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(genericSource, Root + "/Generic.prefab"), Is.Not.Null);
                Assert.That(ShapeSyncFigureImport.TryAdmit(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Generic.prefab"), out _, out string genericDiagnostic), Is.False);
                Assert.That(genericDiagnostic, Does.Contain("Humanoid Avatar"));

                AssetDatabase.CreateAsset(persistentAvatar, Root + "/Persistent.asset"); humanoidSource.GetComponent<Animator>().avatar = persistentAvatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(humanoidSource, Root + "/Persistent.prefab"), Is.Not.Null);
                GameObject persistentCandidate = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Persistent.prefab");
                persistentCandidate.GetComponent<Animator>().avatar = transientAvatar;
                Assert.That(ShapeSyncFigureImport.TryAdmit(persistentCandidate, out _, out string transientDiagnostic), Is.False);
                Assert.That(transientDiagnostic, Does.Contain("persistent source Avatar"));
            }
            finally { Object.DestroyImmediate(genericSource); Object.DestroyImmediate(humanoidSource); Object.DestroyImmediate(transientAvatarRoot); Object.DestroyImmediate(transientAvatar); }
        }

        [Test]
        public void ImportRecord_PersistsConfirmedRendererOrderAfterReimport()
        {
            const string path = Root + "/Record.prefab";
            GameObject source = CreateHumanoidSource("Record", includeRenderer: true, out Avatar avatar);
            GameObject contents = null;
            try
            {
                AssetDatabase.CreateAsset(avatar, Root + "/Record.asset");
                source.GetComponent<Animator>().avatar = avatar;
                source.AddComponent<ShapeSyncDatabase>();
                GameObject intermediate = new GameObject("Intermediate"); intermediate.transform.SetParent(source.transform, false);
                source.transform.Find("Body").SetParent(intermediate.transform, false);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, path), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(path);
                SkinnedMeshRenderer renderer = contents.transform.Find("Intermediate/Body").GetComponent<SkinnedMeshRenderer>();
                ShapeSyncFigureImportRecord record = renderer.gameObject.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(record.TryConfigure(new[] { renderer }, out string diagnostic), Is.True, diagnostic);
                Assert.That(record.TryConfigure(new[] { renderer, renderer }, out string rejectDiagnostic), Is.False);
                Assert.That(rejectDiagnostic, Does.Contain("unique"));
                Assert.That(record.ConfirmedRendererOrder.Count, Is.EqualTo(1));
                Assert.That(PrefabUtility.SaveAsPrefabAsset(contents, path), Is.Not.Null);
                PrefabUtility.UnloadPrefabContents(contents); contents = null;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                ShapeSyncFigureImportRecord reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find("Intermediate/Body").GetComponent<ShapeSyncFigureImportRecord>();
                Assert.That(reloaded.ConfirmedRendererOrder.Count, Is.EqualTo(1));
                Assert.That(reloaded.ConfirmedRendererOrder[0], Is.Not.Null);
                string serializedRecord = EditorJsonUtility.ToJson(reloaded);
                Assert.That(serializedRecord, Does.Not.Contain("resolvedAvatar"));
                Assert.That(serializedRecord, Does.Not.Contain("confirmedSourceAssetPath"));
                Assert.That(serializedRecord, Does.Not.Contain("confirmedSourceRendererPaths"));
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(source); }
        }

        [Test]
        public void AxisAdmission_AcceptsPersistentMeshPrefabWithoutAnimatorOrAvatar()
        {
            const string path = Root + "/AxisMeshOnly.prefab";
            GameObject source = CreateHumanoidSource("AxisMeshOnly", includeRenderer: true, out Avatar avatar);
            try
            {
                Object.DestroyImmediate(source.GetComponent<Animator>());
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, path), Is.Not.Null);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(candidate.GetComponent<Animator>(), Is.Null);
                Assert.That(ShapeSyncFigureImport.TryAdmitAxisSource(candidate, out ShapeSyncFigureImportAdmission admission, out string diagnostic), Is.True, diagnostic);
                Assert.That(admission.Animator, Is.Null);
                Assert.That(admission.Avatar, Is.Null);
                Assert.That(admission.SourceRenderers, Has.Count.EqualTo(1));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void DatabaseMaterialCopies_PreserveNeutralNormalContentAfterRename()
        {
            Texture2D neutral = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { name = ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName };
            Color[] neutralPixels = new Color[64];
            for (int i = 0; i < neutralPixels.Length; i++) neutralPixels[i] = new Color(.5f, .5f, 1f, 1f);
            neutral.SetPixels(neutralPixels); neutral.Apply(false, false);
            Texture2D baseColor = new Texture2D(128, 128) { name = "SourceBaseColor" };
            Material source = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SourceMaterial" };
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            ShapeSyncFigureImport.DatabaseMaterialCopies copies = null;
            try
            {
                source.SetTexture("_BaseMap", baseColor);
                source.SetTexture("_BumpMap", neutral);
                source.EnableKeyword("_NORMALMAP");
                Assert.That(ShapeSyncFigureImport.DatabaseMaterialCopies.TryCreate("Figure", new[] { source }, out copies, out string diagnostic), Is.True, diagnostic);
                Texture copiedNormal = copies.Materials.Single().GetTexture("_BumpMap");
                Assert.That(copiedNormal.name, Is.EqualTo(ShapeSyncEditorTextureUtility.LegacyNeutralNormalPlaceholderName));
                copiedNormal.name = "DatabaseRenamedNormal";
                Assert.That(AtlasMeshValidator.TryValidateSemantics(baseColor, copiedNormal, copies.Materials.Single(), adapter, "_BumpMap", out StackMachineDiagnostic validation), Is.True, validation?.message);
            }
            finally
            {
                copies?.Dispose();
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(neutral);
                Object.DestroyImmediate(baseColor);
            }
        }

        [Test]
        public void AxisImport_RegistersFbmWithDatabaseLocalHumanoidAnimatorAndAvatar()
        {
            const string basePath = Root + "/AxisBase.prefab";
            GameObject source = CreateHumanoidSource("AxisBase", includeRenderer: true, out Avatar avatar);
            GameObject meshOnlySource = null;
            GameObject multiAnimatorSource = null;
            try
            {
                SkinnedMeshRenderer sourceRenderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(sourceRenderer, source.transform.Find("Hips"));
                Texture2D sourceBaseColor = new Texture2D(128, 128) { name = "SourceBaseColor" };
                Texture2D sourceNormal = new Texture2D(128, 128) { name = "SourceNormal" };
                AssetDatabase.CreateAsset(sourceBaseColor, Root + "/SourceBaseColor.asset");
                AssetDatabase.CreateAsset(sourceNormal, Root + "/SourceNormal.asset");
                sourceRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                sourceRenderer.sharedMaterial.SetTexture("_BaseMap", sourceBaseColor);
                sourceRenderer.sharedMaterial.SetTexture("_BumpMap", sourceNormal);
                sourceRenderer.sharedMaterial.SetTexture("_EmissionMap", sourceBaseColor);
                GameObject faceObject = new GameObject("Face"); faceObject.transform.SetParent(source.transform, false);
                SkinnedMeshRenderer faceRenderer = faceObject.AddComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(faceRenderer, source.transform.Find("Hips"));
                faceRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(avatar, Root + "/AxisBaseAvatar.asset");
                AssetDatabase.CreateAsset(sourceRenderer.sharedMesh, Root + "/AxisBase.mesh");
                AssetDatabase.CreateAsset(sourceRenderer.sharedMaterial, Root + "/AxisBase.mat");
                AssetDatabase.CreateAsset(faceRenderer.sharedMesh, Root + "/AxisFace.mesh");
                AssetDatabase.CreateAsset(faceRenderer.sharedMaterial, Root + "/AxisFace.mat");
                source.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, basePath), Is.Not.Null);
                GameObject persistentBase = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
                meshOnlySource = Object.Instantiate(persistentBase);
                foreach (Animator animator in meshOnlySource.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                const string meshOnlyPath = Root + "/AxisMeshOnly.prefab";
                Assert.That(PrefabUtility.SaveAsPrefabAsset(meshOnlySource, meshOnlyPath), Is.Not.Null);
                GameObject persistentMeshOnly = AssetDatabase.LoadAssetAtPath<GameObject>(meshOnlyPath);
                Assert.That(ShapeSyncDatabaseAsset.TryCreate(Root, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryAdmit(persistentBase, out ShapeSyncFigureImportAdmission baseAdmission, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(AssetDatabase.GetAssetPath(database), baseAdmission, "Master", out string importDiagnostic), Is.True, importDiagnostic);
                GameObject persistentAxis = persistentBase;
                Assert.That(ShapeSyncFigureImport.TryAdmitAxisSource(persistentAxis, out ShapeSyncFigureImportAdmission axisAdmission, out string axisDiagnostic), Is.True, axisDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase opened, out string openDiagnostic), Is.True, openDiagnostic);
                SkinnedMeshRenderer baseMaterialRenderer = opened.Registry.BaseFigures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Body", baseMaterialRenderer, 0, baseMaterialRenderer.sharedMaterial,
                    out ShapeSyncMaterialAdapterResolver.Admission materialAdmission, out string materialAdmissionDiagnostic), Is.True, materialAdmissionDiagnostic);
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(opened, "Face", baseMaterialRenderer, 1, baseMaterialRenderer.sharedMaterials[1],
                    out ShapeSyncMaterialAdapterResolver.Admission faceMaterialAdmission, out string faceMaterialAdmissionDiagnostic), Is.True, faceMaterialAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySaveWithTextureRename(AssetDatabase.GetAssetPath(database), new[] { materialAdmission, faceMaterialAdmission }, true, out string materialSaveDiagnostic), Is.True, materialSaveDiagnostic); }
                finally { materialAdmission.Dispose(); faceMaterialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out opened, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryAdmitAxisSource(persistentMeshOnly, out ShapeSyncFigureImportAdmission meshOnlyAdmission, out string meshOnlyAdmissionDiagnostic), Is.True, meshOnlyAdmissionDiagnostic);
                Assert.That(opened.Registry.TryAdmitFigureAxes(opened, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("AnimatorRequired", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] rejectedAxes, out string rejectedAxesDiagnostic), Is.True, rejectedAxesDiagnostic);
                Hash128 hashBeforeRejectedFbm = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(database));
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(rejectedAxes[0], new[] { new ShapeSyncAxisFigureSource("AnimatorRequired", meshOnlyAdmission) }) }, out string missingAnimatorDiagnostic), Is.False);
                Assert.That(missingAnimatorDiagnostic, Does.Contain("Humanoid Animator and Avatar"));
                Assert.That(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(database)), Is.EqualTo(hashBeforeRejectedFbm), "Rejected FBM admission must not modify the Database.");
                Assert.That(opened.Registry.TryAdmitFigureAxes(opened, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes, out string axesDiagnostic), Is.True, axesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(axes[0], new[] { new ShapeSyncAxisFigureSource("Tall", axisAdmission) }) }, out string axisImportDiagnostic), Is.True, axisImportDiagnostic);
                Texture databaseNormal = opened.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material.GetTexture("_BumpMap");
                Texture databaseBaseColor = opened.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material.GetTexture("_BaseMap");
                Assert.That(databaseNormal, Is.Not.Null);
                Assert.That(databaseBaseColor, Is.Not.Null);
                Assert.That(ShapeSyncNormalEntryAuthoring.TrySave(AssetDatabase.GetAssetPath(database), new[] { "Body" }, new[]
                {
                    new ShapeSyncNormalEntryAuthoring.Assignment("Body", ShapeSyncDatabaseRegistry.BaseShapeKey, databaseNormal),
                    new ShapeSyncNormalEntryAuthoring.Assignment("Body", "Tall", databaseNormal),
                }, out string normalSaveDiagnostic), Is.True, normalSaveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase reloaded, out string reloadDiagnostic), Is.True, reloadDiagnostic);
                Assert.That(reloaded.Registry.FigureAxes.Any(axis => axis.Name == "Tall" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm), Is.True);
                Texture ownerIsolationTexture = reloaded.Registry.MaterialEntries.Single(entry => entry.LogicalName == "Body").Material.GetTexture("_BaseMap");
                ShapeSyncDatabaseRegistry.TextureResourceEntry ownerIsolationResource = reloaded.Registry.TextureResources.Single(entry => entry.Texture == ownerIsolationTexture);
                ownerIsolationResource.SetOwner(ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("OwnerIsolationOutfit", "Tall"));
                Assert.That(ownerIsolationResource.Owner.Scope, Is.EqualTo(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit));
                SerializedObject pcmRegistry = new SerializedObject(reloaded.Registry);
                pcmRegistry.FindProperty("pcmSlots").intValue = -1;
                pcmRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out _, out StackMachineDiagnostic negativePcmDiagnostic), Is.False);
                Assert.That(negativePcmDiagnostic.domainCode, Is.EqualTo("FigureMorphAuthoringInvalid"));
                Assert.That(reloaded.Registry.PcmSlots, Is.EqualTo(-1));
                pcmRegistry.Update();
                pcmRegistry.FindProperty("pcmSlots").intValue = 0;
                pcmRegistry.ApplyModifiedPropertiesWithoutUndo();
                AddRawBlendShape(reloaded.transform.Find("Intermediate/Master").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, "Alpha");
                AddRawBlendShape(reloaded.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, "Alpha");
                Assert.That(reloaded.Registry.TrySetKeptRawBlendShapeNames(reloaded, new[] { "Expression", "Alpha" }, out string keepDiagnostic), Is.True, keepDiagnostic);
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out ShapeSyncFigureGenerateSnapshot snapshot, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic == null ? null : snapshotDiagnostic.message);
                Assert.That(snapshot.Axes.Single().Name, Is.EqualTo("Tall"));
                Assert.That(snapshot.Axes.Single().Figures.Single().ShapeKey, Is.EqualTo("Tall"));
                Assert.That(snapshot.KeptRawBlendShapeNames, Is.EqualTo(new[] { "Alpha", "Expression" }));
                GameObject sourceFigure = reloaded.transform.Find("Intermediate/Master").gameObject;
#if SHAPESYNC_USE_UNIVRM
                System.Type vrmInstanceType = System.AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("UniVRM10.Vrm10Instance")).FirstOrDefault(type => type != null);
                Assert.That(vrmInstanceType, Is.Not.Null);
                Component sourceVrmInstance = sourceFigure.AddComponent(vrmInstanceType);
#endif
                BoxCollider sourceCollider = sourceFigure.AddComponent<BoxCollider>();
                MeshBindingTemplate sourceBindingTemplate = ScriptableObject.CreateInstance<MeshBindingTemplate>();
                MeshStackMachine sourceRootMachine = sourceFigure.AddComponent<MeshStackMachine>();
                SerializedObject sourceRootMachineSerialized = new SerializedObject(sourceRootMachine);
                sourceRootMachineSerialized.FindProperty("bindingTemplate").objectReferenceValue = sourceBindingTemplate;
                sourceRootMachineSerialized.ApplyModifiedPropertiesWithoutUndo();
                Transform sourceRuntimeChild = sourceFigure.GetComponentsInChildren<Transform>(true).First(transform => transform != sourceFigure.transform);
                MeshStackMachine sourceChildMachine = sourceRuntimeChild.gameObject.AddComponent<MeshStackMachine>();
                SerializedObject sourceChildMachineSerialized = new SerializedObject(sourceChildMachine);
                sourceChildMachineSerialized.FindProperty("bindingTemplate").objectReferenceValue = sourceBindingTemplate;
                sourceChildMachineSerialized.ApplyModifiedPropertiesWithoutUndo();
                ShapeDirector sourceDirector = sourceFigure.AddComponent<ShapeDirector>();
                sourceDirector.AutoCompile = false;
                sourceFigure.AddComponent<ShapeDocumentSerializer>();
                sourceFigure.AddComponent<ShapeDocumentDeserializer>();
                Assert.That(ShapeSyncFigureGenerateMeshBuilder.TryBuild(snapshot, out ShapeSyncFigureGenerateMeshBuilder.Result meshResult, out StackMachineDiagnostic meshBuildDiagnostic), Is.True, meshBuildDiagnostic == null ? null : meshBuildDiagnostic.message);
                using (meshResult)
                {
                    Assert.That(meshResult.Mesh.GetBlendShapeName(0), Is.EqualTo("Tall"));
                    Assert.That(meshResult.Mesh.GetBlendShapeName(1), Is.EqualTo("Alpha"));
                    Assert.That(meshResult.Mesh.GetBlendShapeName(2), Is.EqualTo("MCM_Tall_Alpha"));
                    Assert.That(meshResult.BaseRegistry.bonePoses, Is.Not.Empty);
                    Assert.That(meshResult.FbmRegistries.Single().fbmBlendName, Is.EqualTo("Tall"));
                    Assert.That(meshResult.Figure.GetComponentInChildren<Animator>().avatar, Is.SameAs(meshResult.Avatar));
                    Assert.That(meshResult.FbmAvatars.Single().isHuman && meshResult.FbmAvatars.Single().isValid, Is.True);
                    Assert.That(meshResult.FbmAvatars.Single(), Is.Not.SameAs(meshResult.Avatar));
                    Assert.That(meshResult.Figure.GetComponent<DynamicBoneBlender>(), Is.Null, "Static mesh generation must escrow runtime targets rather than attach a runtime component.");
                    Assert.That(meshResult.Figure.GetComponentsInChildren<MeshStackMachine>(true), Is.Empty, "Input runtime components must not survive the static clone.");
                    Assert.That(meshResult.Figure.GetComponent<ShapeDirector>(), Is.Null, "Input ShapeDirector state must not survive the static clone.");
                    Assert.That(meshResult.FbmTargets.Single().targetAvatar, Is.SameAs(meshResult.FbmAvatars.Single()));
                    Assert.That(meshResult.FbmTargets.Single().targetRegistry, Is.SameAs(meshResult.FbmRegistries.Single()));
#if SHAPESYNC_USE_UNIVRM
                    Assert.That(meshResult.Figure.GetComponentInChildren(vrmInstanceType), Is.Null);
#endif
                    Assert.That(meshResult.Figure.GetComponentInChildren<BoxCollider>(), Is.Not.Null);
                    int noPbmShapeCount = meshResult.Mesh.blendShapeCount;
                    Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(snapshot, meshResult, out StackMachineDiagnostic noPbmDiagnostic), Is.True, noPbmDiagnostic == null ? null : noPbmDiagnostic.message);
                    Assert.That(meshResult.Mesh.blendShapeCount, Is.EqualTo(noPbmShapeCount));
                    Assert.That(meshResult.Mesh.GetBlendShapeName(0), Is.EqualTo("Tall"));
                    Assert.That(meshResult.PbmTargets, Is.Empty);
                    ShapeSyncFigureGenerateMeshBuilder.ConfigureRuntimeGraph(meshResult);
                    DynamicMorphAdapter adapter = meshResult.Figure.GetComponent<DynamicMorphAdapter>();
                    DynamicBoneBlender blender = meshResult.Figure.GetComponent<DynamicBoneBlender>();
                    Assert.That(adapter.TargetRenderer, Is.SameAs(meshResult.Figure.GetComponentInChildren<SkinnedMeshRenderer>()));
                    Assert.That(adapter.Schema.FbmBlendNames, Is.EqualTo(new[] { "Tall" }));
                    Assert.That(adapter.Schema.FirstSlotBlendShapeIndex, Is.EqualTo(meshResult.Mesh.blendShapeCount));
                    Assert.That(blender.Targets.Single().targetAvatar, Is.SameAs(meshResult.FbmAvatars.Single()));
                    Assert.That(blender.Targets.Single().targetRegistry, Is.SameAs(meshResult.FbmRegistries.Single()));
                    Assert.That(meshResult.Figure.GetComponent<UniversalExpressionProxy>(), Is.Not.Null);
                    Assert.That(meshResult.Figure.GetComponent<FigureMorphSyncCoordinator>(), Is.Not.Null);
                    Assert.That(meshResult.Figure.GetComponent<OutfitAttacher>(), Is.Not.Null);
                    Assert.That(ShapeSyncFigureGenerateMaterialConfigurator.TryConfigure(snapshot, meshResult, out MaterialBinding generatedBinding, out MeshBinding generatedNormalBinding, out StackMachineDiagnostic materialConfigureDiagnostic), Is.True, materialConfigureDiagnostic == null ? null : materialConfigureDiagnostic.message);
                    {
                        MaterialProxy generatedProxy = meshResult.Figure.GetComponent<MaterialProxy>();
                        NormalBlender generatedNormal = meshResult.Figure.GetComponent<NormalBlender>();
                        Assert.That(generatedProxy.Entries.Select(entry => entry.entryName), Is.EqualTo(snapshot.MaterialEntries.OrderBy(entry => entry.MaterialSlot).Select(entry => entry.LogicalName)));
                        Assert.That(generatedProxy.Entries.Select(entry => entry.renderer), Is.All.SameAs(meshResult.Figure.GetComponentInChildren<SkinnedMeshRenderer>()));
                        Assert.That(generatedProxy.Entries.Select(entry => entry.materialChannel), Is.EqualTo(snapshot.MaterialEntries.OrderBy(entry => entry.MaterialSlot).Select(entry => entry.MaterialSlot)));
                        Assert.That(generatedProxy.Entries.Select(entry => entry.adapter), Is.EqualTo(snapshot.MaterialEntries.OrderBy(entry => entry.MaterialSlot).Select(entry => entry.Adapter)));
                        Assert.That(generatedNormal.Entries, Is.EqualTo(snapshot.NormalEntries.Select(entry => entry.MaterialEntryName).Distinct()));
                        Assert.That(generatedNormal.DynamicBoneBlender, Is.SameAs(blender));
                        Assert.That(generatedBinding.Textures.Select(entry => entry.logicalName), Is.EqualTo(snapshot.TextureResources.Where(entry => entry.Texture is Texture2D).Select(entry => entry.LogicalName)));
                        Assert.That(generatedBinding.Textures.Select(entry => entry.sourceTexture), Is.All.Not.Null);
                        Assert.That(meshResult.Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials[0], Is.Not.SameAs(snapshot.MaterialEntries.OrderBy(entry => entry.MaterialSlot).First().MaterialAsset));
                        Assert.That(generatedNormalBinding.NormalOwners, Has.Count.EqualTo(1));
                        Assert.That(generatedNormalBinding.NormalOwners[0].outfitRegistryId, Is.Empty);
                        Assert.That(generatedNormalBinding.NormalOwners[0].targets.Select(target => target.targetName), Is.EqualTo(new[] { string.Empty, "Tall" }));
                        Assert.That(generatedNormalBinding.NormalOwners[0].targets.SelectMany(target => target.textures).Select(entry => entry.entryName), Is.EqualTo(new[] { "Body", "Body" }));
                        Assert.That(generatedNormalBinding.NormalOwners[0].targets.SelectMany(target => target.textures).Select(entry => entry.normalTexture), Is.All.Not.Null);
                        Assert.That(generatedNormalBinding.NormalOwners[0].targets.SelectMany(target => target.textures).Select(entry => entry.normalTexture), Is.All.Not.SameAs(databaseNormal));
                        Material generatedBodyMaterial = meshResult.Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials[0];
                        Assert.That(generatedBodyMaterial.GetTexture("_BaseMap"), Is.Not.SameAs(databaseBaseColor));
                        Assert.That(generatedBinding.Textures.Select(entry => entry.sourceTexture), Does.Contain(generatedBodyMaterial.GetTexture("_BaseMap")));
                        Assert.That(generatedBodyMaterial.GetTexture("_BumpMap"), Is.Not.SameAs(databaseNormal));
                        ShapeSyncFigureGenerateSnapshot.Normal baseNormal = snapshot.NormalEntries.Single(entry => entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                        ShapeSyncFigureGenerateSnapshot.Normal tallNormal = snapshot.NormalEntries.Single(entry => entry.ShapeKey == "Tall");
                        Assert.That(generatedBodyMaterial.GetTexture("_BumpMap"), Is.SameAs(generatedNormalBinding.NormalOwners[0].targets[0].textures[0].normalTexture));
                        Assert.That(generatedBinding.Textures.Single(entry => entry.logicalName == baseNormal.TextureResourceName).sourceTexture,
                            Is.SameAs(generatedNormalBinding.NormalOwners[0].targets[0].textures[0].normalTexture));
                        Assert.That(generatedBinding.Textures.Single(entry => entry.logicalName == tallNormal.TextureResourceName).sourceTexture,
                            Is.SameAs(generatedNormalBinding.NormalOwners[0].targets[1].textures[0].normalTexture));

                        ShapeSyncFigureGenerateSnapshot noNormalSnapshot = CreateSnapshotWith(snapshot, snapshot.Axes,
                            System.Array.Empty<ShapeSyncFigureGenerateSnapshot.Normal>(), System.Array.Empty<ShapeSyncFigureGenerateSnapshot.FigureNormal>());
                        Assert.That(ShapeSyncFigureGenerateMaterialConfigurator.TryConfigure(noNormalSnapshot, meshResult, out _, out MeshBinding noNormalBinding, out StackMachineDiagnostic noNormalDiagnostic), Is.True,
                            noNormalDiagnostic == null ? null : noNormalDiagnostic.message);
                        Assert.That(meshResult.Figure.GetComponent<NormalBlender>().Entries, Is.Empty);
                        Assert.That(noNormalBinding.NormalOwners, Is.Empty, "A Figure with no declared Normal must not create a placeholder owner.");

                        ShapeSyncFigureGenerateSnapshot.Normal sentinelLogicalName = new ShapeSyncFigureGenerateSnapshot.Normal(
                            "Body", "Alpha", "LogicalNameMustNotReachNormalBlender", baseNormal.Texture);
                        ShapeSyncFigureGenerateSnapshot orderedNormalSnapshot = CreateSnapshotWith(snapshot, snapshot.Axes,
                            new[] { tallNormal, sentinelLogicalName, baseNormal });
                        Assert.That(ShapeSyncFigureGenerateMaterialConfigurator.TryConfigure(orderedNormalSnapshot, meshResult, out _, out MeshBinding orderedNormalBinding, out StackMachineDiagnostic orderedNormalDiagnostic), Is.True,
                            orderedNormalDiagnostic == null ? null : orderedNormalDiagnostic.message);
                        Assert.That(meshResult.Figure.GetComponent<NormalBlender>().Entries, Is.EqualTo(new[] { "Body" }),
                            "NormalBlender receives EntryName only, never Texture Resource logical names.");
                        Assert.That(orderedNormalBinding.NormalOwners[0].targets.Select(target => target.targetName), Is.EqualTo(new[] { string.Empty, "Alpha", "Tall" }),
                            "FBM target names are ordinal after the Base target.");

                    }
                }
                Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "Generated/Registries", "Generated/Bindings", "Generated/Materials", "Generated/Textures", out string generateDiagnostic), Is.True, generateDiagnostic);
                string generatedPrefabPath = Root + "/Master.prefab";
                GameObject generatedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(generatedPrefabPath);
                Assert.That(generatedPrefab, Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Generated/Registries/Master.prefab"), Is.Null,
                    "The generated Figure Prefab belongs directly in the selected Generate root, never in an asset subfolder.");
                Assert.That(FindDatabaseReferences(generatedPrefab, AssetDatabase.GetAssetPath(reloaded)), Is.Empty,
                    "Generated Figure must not retain a Database Prefab or Database sub-asset reference.");
                MaterialBinding generatedMaterialBinding = AssetDatabase.LoadAssetAtPath<MaterialBinding>(Root + "/Generated/Bindings/Master_MaterialBinding.asset");
                Assert.That(generatedMaterialBinding, Is.Not.Null);
                MeshBinding generatedMeshBinding = AssetDatabase.LoadAssetAtPath<MeshBinding>(Root + "/Generated/Bindings/Master_MeshBinding.asset");
                Assert.That(generatedMeshBinding, Is.Not.Null);
                AssertGeneratedMainObjectName(Root + "/Generated/Bindings/Master_MaterialBinding.asset");
                AssertGeneratedMainObjectName(Root + "/Generated/Bindings/Master_MeshBinding.asset");
                AssertGeneratedMainObjectName(Root + "/Generated/Registries/Master_Registry.asset");
                string generatedPrefabJson = EditorJsonUtility.ToJson(generatedPrefab);
                string generatedMaterialBindingJson = EditorJsonUtility.ToJson(generatedMaterialBinding);
                string generatedMeshBindingJson = EditorJsonUtility.ToJson(generatedMeshBinding);
                Assert.That(generatedPrefabJson + generatedMaterialBindingJson + generatedMeshBindingJson,
                    Does.Not.Contain("OwnerIsolationOutfit").And.Not.Contain("outfitIdentity").And.Not.Contain("sourceShapeKey"),
                    "TextureResourceOwner is authoring-only: an Outfit-owned input Texture must not serialize owner data into any generated output asset.");
                Assert.That(generatedMeshBinding.Morphs.Select(entry => (entry.logicalName, entry.targetName)),
                    Is.EqualTo(new[] { ("Tall", "Tall") }),
                    "MeshBinding must expose each Database FBM name as the logical binding and its DDB target name as the target binding.");
                ShapeDirector generatedDirector = generatedPrefab.GetComponent<ShapeDirector>();
                Assert.That(generatedDirector, Is.Not.Null);
                Assert.That(generatedDirector, Is.Not.SameAs(sourceDirector));
                Assert.That(generatedDirector.AutoCompile, Is.True, "Generated Director must not inherit the input Director serialized state.");
                SerializedObject generatedDirectorSerialized = new SerializedObject(generatedDirector);
                Assert.That(generatedDirectorSerialized.FindProperty("meshBinding").objectReferenceValue, Is.SameAs(generatedMeshBinding),
                    "The generated Figure Director must own the MeshBinding that resolves its Figure Normal targets.");
                Assert.That(generatedDirectorSerialized.FindProperty("materialBinding").objectReferenceValue, Is.SameAs(generatedMaterialBinding),
                    "The generated Figure Director must own the MaterialBinding for runtime material resolution.");
                ShapeDocumentSerializer generatedSerializer = generatedPrefab.GetComponent<ShapeDocumentSerializer>();
                ShapeDocumentDeserializer generatedDeserializer = generatedPrefab.GetComponent<ShapeDocumentDeserializer>();
                Assert.That(generatedPrefab.GetComponents<ShapeDocumentSerializer>(), Has.Length.EqualTo(1));
                Assert.That(generatedPrefab.GetComponents<ShapeDocumentDeserializer>(), Has.Length.EqualTo(1));
                Assert.That(generatedDirectorSerialized.FindProperty("serializer").objectReferenceValue, Is.SameAs(generatedSerializer));
                Assert.That(generatedDirectorSerialized.FindProperty("deserializer").objectReferenceValue, Is.SameAs(generatedDeserializer));
                MaterialProxy persistedProxy = generatedPrefab.GetComponent<MaterialProxy>();
                SkinnedMeshRenderer generatedProxyRenderer = generatedPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                int generatedShaderCount = generatedProxyRenderer.sharedMaterials.Select(material => material.shader).Distinct().Count();
                Assert.That(persistedProxy.Entries.Select(entry => entry.adapter).Distinct().Count(), Is.EqualTo(generatedShaderCount),
                    "Generated Adapter assets are owned once per output Shader, never once per Material Entry.");
                foreach (IGrouping<Shader, MaterialProxyEntry> shaderGroup in persistedProxy.Entries.GroupBy(entry => entry.renderer.sharedMaterials[entry.materialChannel].shader))
                {
                    Assert.That(shaderGroup.Select(entry => entry.adapter).Distinct().Count(), Is.EqualTo(1), "Entries that use one Shader must share one generated Adapter.");
                    MaterialShaderAdapter databaseAdapter = snapshot.MaterialEntries.Single(entry => entry.MaterialAsset.shader == shaderGroup.Key).Adapter;
                    Assert.That(shaderGroup.First().adapter.name, Is.EqualTo(databaseAdapter.name), "Generated Adapter name must preserve its Database Adapter name.");
                }
                GameObject normalRuntimeInstance = Object.Instantiate(generatedPrefab);
                try
                {
                    MeshStackMachine normalRuntimeMachine = normalRuntimeInstance.GetComponent<MeshStackMachine>();
                    Assert.That(normalRuntimeMachine.TryEnsureReady(generatedMeshBinding, out StackMachineDiagnostic bindingDiagnostic), Is.True,
                        bindingDiagnostic == null ? null : bindingDiagnostic.message);
                    Assert.That(normalRuntimeMachine.TryBuildNormalRecipe("Body", new[] { new NormalTargetWeight("Tall", 1f, true) }, out _, out StackMachineDiagnostic normalRuntimeDiagnostic), Is.True,
                        normalRuntimeDiagnostic == null ? null : normalRuntimeDiagnostic.message);
                }
                finally { Object.DestroyImmediate(normalRuntimeInstance); }
                Assert.That(AssetDatabase.FindAssets("t:Material", new[] { Root + "/Generated/Materials" }), Is.Not.Empty);
                Assert.That(AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Generated/Textures" }), Is.Not.Empty);
                Assert.That(ShapeSyncFigureGenerateMeshBuilder.TryBuild(snapshot, out ShapeSyncFigureGenerateMeshBuilder.Result oracle, out StackMachineDiagnostic oracleDiagnostic), Is.True, oracleDiagnostic == null ? null : oracleDiagnostic.message);
                using (oracle)
                {
                    Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(snapshot, oracle, out StackMachineDiagnostic oraclePbmDiagnostic), Is.True, oraclePbmDiagnostic == null ? null : oraclePbmDiagnostic.message);
                    SkinnedMeshRenderer generatedRenderer = generatedPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    Animator generatedAnimator = generatedPrefab.GetComponentInChildren<Animator>(true);
                    Assert.That(generatedRenderer.sharedMesh.bindposes, Is.EqualTo(oracle.Mesh.bindposes));
                    Assert.That(Enumerable.Range(0, generatedRenderer.sharedMesh.blendShapeCount).Select(generatedRenderer.sharedMesh.GetBlendShapeName),
                        Is.EqualTo(Enumerable.Range(0, oracle.Mesh.blendShapeCount).Select(oracle.Mesh.GetBlendShapeName)));
                    Assert.That(generatedAnimator.avatar.humanDescription.human.Select(bone => bone.humanName + ":" + bone.boneName),
                        Is.EqualTo(oracle.Avatar.humanDescription.human.Select(bone => bone.humanName + ":" + bone.boneName)));
                    Assert.That(generatedAnimator.avatar.humanDescription.skeleton.Select(bone => bone.name),
                        Is.EqualTo(oracle.Avatar.humanDescription.skeleton.Select(bone => bone.name)));
                    Assert.That(generatedPrefab.GetComponent<DynamicMorphAdapter>(), Is.Not.Null);
                    Assert.That(generatedPrefab.GetComponent<DynamicBoneBlender>(), Is.Not.Null);
                    Assert.That(generatedPrefab.GetComponent<MaterialProxy>(), Is.Not.Null);
                    Assert.That(generatedPrefab.GetComponent<NormalBlender>(), Is.Not.Null);
                    Assert.That(generatedPrefab.GetComponentsInChildren<MeshStackMachine>(true), Has.Length.EqualTo(1));
                    SerializedObject generatedMachineSerialized = new SerializedObject(generatedPrefab.GetComponent<MeshStackMachine>());
                    Assert.That(generatedMachineSerialized.FindProperty("bindingTemplate").objectReferenceValue, Is.Null,
                        "Generated MeshStackMachine must not inherit the input bindingTemplate.");
                }
                Hash128 databaseHashBeforeInvalidOutputPath = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(reloaded));
                Hash128 prefabHashBeforeInvalidOutputPath = AssetDatabase.GetAssetDependencyHash(generatedPrefabPath);
                Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "../Outside", "Generated/Bindings", "Generated/Materials", "Generated/Textures", out string invalidOutputPathDiagnostic), Is.False);
                Assert.That(invalidOutputPathDiagnostic, Does.Contain("FigureGenerateOutputPathInvalid"));
                Assert.That(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(reloaded)), Is.EqualTo(databaseHashBeforeInvalidOutputPath));
                Assert.That(AssetDatabase.GetAssetDependencyHash(generatedPrefabPath), Is.EqualTo(prefabHashBeforeInvalidOutputPath));
                string generatedPrefabGuid = AssetDatabase.AssetPathToGUID(generatedPrefabPath);
                string generatedBodyMaterialPath = Root + "/Generated/Materials/Body.asset";
                string generatedBodyMaterialGuidBeforeOverwrite = AssetDatabase.AssetPathToGUID(generatedBodyMaterialPath);
                Texture bodyMainTextureBeforeOverwritePersist = null;
                Texture bodyNormalTextureBeforeOverwritePersist = null;
                Texture bodyEmissionTextureBeforeOverwritePersist = null;
                ShapeSyncFigureGenerator.BeforePersistForTests = (asset, path) =>
                {
                    if (asset is Material material && path.EndsWith("/Generated/Materials/Body.asset", System.StringComparison.Ordinal))
                    {
                        bodyMainTextureBeforeOverwritePersist = material.GetTexture("_BaseMap");
                        bodyNormalTextureBeforeOverwritePersist = material.GetTexture("_BumpMap");
                        bodyEmissionTextureBeforeOverwritePersist = material.GetTexture("_EmissionMap");
                    }
                };
                try { Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "Generated/Registries", "Generated/Bindings", "Generated/Materials", "Generated/Textures", out generateDiagnostic), Is.True, generateDiagnostic); }
                finally { ShapeSyncFigureGenerator.BeforePersistForTests = null; }
                Assert.That(bodyMainTextureBeforeOverwritePersist, Is.Not.Null, "The transient overwrite Material must retain its Body MainTex before persistence.");
                Assert.That(bodyNormalTextureBeforeOverwritePersist, Is.Not.Null, "The transient overwrite Material must retain its Body Normal before persistence.");
                Assert.That(bodyEmissionTextureBeforeOverwritePersist, Is.Not.Null, "The transient overwrite Material must retain auxiliary shader texture aliases before persistence.");
                Assert.That(AssetDatabase.GetAssetPath(bodyMainTextureBeforeOverwritePersist), Is.EqualTo(Root + "/Generated/Textures/Master_Body.asset"));
                Assert.That(AssetDatabase.AssetPathToGUID(generatedPrefabPath), Is.EqualTo(generatedPrefabGuid));
                Assert.That(AssetDatabase.AssetPathToGUID(generatedBodyMaterialPath), Is.Not.Empty);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(generatedPrefabPath, ImportAssetOptions.ForceUpdate);
                generatedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(generatedPrefabPath);
                generatedMaterialBinding = AssetDatabase.LoadAssetAtPath<MaterialBinding>(Root + "/Generated/Bindings/Master_MaterialBinding.asset");
                SkinnedMeshRenderer overwrittenRenderer = generatedPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Material overwrittenBody = overwrittenRenderer.sharedMaterials.Single(material => material.name == "Body");
                Material overwrittenBodyAsset = AssetDatabase.LoadAssetAtPath<Material>(generatedBodyMaterialPath);
                Assert.That(overwrittenBodyAsset, Is.Not.Null);
                Assert.That(overwrittenBody, Is.SameAs(overwrittenBodyAsset), "Prefab reimport must resolve the output Material asset at its output path.");
                Assert.That(overwrittenBody.GetTexture("_BaseMap"), Is.Not.Null, "Overwrite Generate must retain the Body MainTex property.");
                Assert.That(overwrittenBody.GetTexture("_BumpMap"), Is.Not.Null, "Overwrite Generate must retain the Body Normal property.");
                Assert.That(overwrittenBody.GetTexture("_EmissionMap"), Is.Not.Null, "Overwrite Generate must retain auxiliary shader texture aliases.");
                Assert.That(overwrittenRenderer.sharedMaterials.Select(AssetDatabase.GetAssetPath), Is.All.StartsWith(Root + "/Generated/Materials/"),
                    "Overwrite Generate must keep every generated renderer Material in the selected output Materials folder.");
                Assert.That(generatedMaterialBinding.Textures.Select(entry => AssetDatabase.GetAssetPath(entry.sourceTexture)), Is.All.StartsWith(Root + "/Generated/Textures/"),
                    "Overwrite Generate must keep every MaterialBinding texture in the selected output Textures folder.");
                foreach (Material generatedMaterial in overwrittenRenderer.sharedMaterials)
                    foreach (string property in generatedMaterial.GetTexturePropertyNames())
                        if (generatedMaterial.GetTexture(property) != null)
                            Assert.That(AssetDatabase.GetAssetPath(generatedMaterial.GetTexture(property)), Does.StartWith(Root + "/Generated/Textures/"),
                                "Overwrite Generate must not leave a generated Material property pointing at a replaced or Database-owned texture: " + generatedMaterial.name + "." + property);
                ShapeSyncFigureGenerator.BeforeFinalSaveForTests = () => throw new System.InvalidOperationException("Injected Generate commit failure");
                try
                {
                    Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "Generated/Registries", "Generated/Bindings", "Generated/Materials", "Generated/Textures", out generateDiagnostic), Is.False);
                    Assert.That(generateDiagnostic, Does.Contain("Injected Generate commit failure"));
                }
                finally { ShapeSyncFigureGenerator.BeforeFinalSaveForTests = null; }
                Assert.That(AssetDatabase.AssetPathToGUID(generatedPrefabPath), Is.EqualTo(generatedPrefabGuid), "rollback must preserve the existing Prefab GUID.");
                AssetDatabase.LoadAssetAtPath<MaterialBinding>(Root + "/Generated/Bindings/Master_MaterialBinding.asset").name = string.Empty;
                AssetDatabase.LoadAssetAtPath<MeshBinding>(Root + "/Generated/Bindings/Master_MeshBinding.asset").name = "Master_Mesh PBM Rebuilt";
                AssetDatabase.LoadAssetAtPath<zgock.ShapeSync.CharacterBoneRegistry>(Root + "/Generated/Registries/Master_Registry.asset").name = string.Empty;
                GameObject sceneInstance = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(generatedPrefabPath)) as GameObject;
                try
                {
                    Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(sceneInstance), Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(generatedPrefabPath)));
                    Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "Generated/Registries", "Generated/Bindings", "Generated/Materials", "Generated/Textures", out generateDiagnostic), Is.True, generateDiagnostic);
                    Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(sceneInstance), Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(generatedPrefabPath)), "scene instance must retain its Prefab connection after regenerate.");
                    AssertGeneratedMainObjectName(Root + "/Generated/Bindings/Master_MaterialBinding.asset");
                    AssertGeneratedMainObjectName(Root + "/Generated/Bindings/Master_MeshBinding.asset");
                    AssertGeneratedMainObjectName(Root + "/Generated/Registries/Master_Registry.asset");
                }
                finally { Object.DestroyImmediate(sceneInstance); }
                string databasePathForGenerate = AssetDatabase.GetAssetPath(reloaded);
                Hash128 databaseHashBeforeGenerateFailure = AssetDatabase.GetAssetDependencyHash(databasePathForGenerate);
                ShapeSyncFigureGenerator.BeforePersistForTests = (_, __) => throw new System.InvalidOperationException("Injected pre-Prefab persist failure");
                try
                {
                    Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "PrePersistFailure/Registries", "PrePersistFailure/Bindings", "PrePersistFailure/Materials", "PrePersistFailure/Textures", out generateDiagnostic), Is.False);
                    Assert.That(generateDiagnostic, Does.Contain("Injected pre-Prefab persist failure"));
                }
                finally { ShapeSyncFigureGenerator.BeforePersistForTests = null; }
                Assert.That(AssetDatabase.IsValidFolder(Root + "/PrePersistFailure"), Is.False, "A pre-Prefab failure must remove every newly created output folder.");
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePathForGenerate), Is.EqualTo(databaseHashBeforeGenerateFailure), "Generate failure must not change its Database input.");
                ShapeSyncFigureGenerator.BeforePrefabSaveForTests = _ => throw new System.InvalidOperationException("Injected Prefab save failure");
                try
                {
                    Assert.That(ShapeSyncFigureGenerator.TryGenerate(reloaded, Root, "PrePrefabFailure/Registries", "PrePrefabFailure/Bindings", "PrePrefabFailure/Materials", "PrePrefabFailure/Textures", out generateDiagnostic), Is.False);
                    Assert.That(generateDiagnostic, Does.Contain("Injected Prefab save failure"));
                }
                finally { ShapeSyncFigureGenerator.BeforePrefabSaveForTests = null; }
                Assert.That(AssetDatabase.IsValidFolder(Root + "/PrePrefabFailure"), Is.False, "A Prefab-commit failure must roll back every newly created generated asset and folder.");
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePathForGenerate), Is.EqualTo(databaseHashBeforeGenerateFailure), "Prefab-commit failure must not change its Database input.");
                SkinnedMeshRenderer failingFbmRenderer = reloaded.transform.Find("Intermediate/Tall").GetComponentInChildren<SkinnedMeshRenderer>();
                Mesh savedFbmMesh = failingFbmRenderer.sharedMesh;
                Mesh incompatibleFbmMesh = new Mesh();
                incompatibleFbmMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
                incompatibleFbmMesh.triangles = new[] { 0, 1, 2 };
                failingFbmRenderer.sharedMesh = incompatibleFbmMesh;
                Assert.That(ShapeSyncFigureGenerateMeshBuilder.TryBuild(snapshot, out ShapeSyncFigureGenerateMeshBuilder.Result failedMeshResult, out StackMachineDiagnostic failedMeshDiagnostic), Is.False);
                Assert.That(failedMeshResult, Is.Null);
                Assert.That(failedMeshDiagnostic.domainCode, Is.EqualTo("FigureMeshBuildInvalid"));
                Assert.That(snapshot.BaseFigure.GameObject.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh, Is.Not.SameAs(incompatibleFbmMesh));
                failingFbmRenderer.sharedMesh = savedFbmMesh;
                Object.DestroyImmediate(incompatibleFbmMesh);
                SerializedObject keptNamesRegistry = new SerializedObject(reloaded.Registry);
                SerializedProperty keptNames = keptNamesRegistry.FindProperty("keptRawBlendShapeNames");
                keptNames.GetArrayElementAtIndex(0).stringValue = "Expression";
                keptNames.GetArrayElementAtIndex(1).stringValue = "Alpha";
                keptNamesRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out ShapeSyncFigureGenerateSnapshot reorderedKeepSnapshot, out StackMachineDiagnostic reorderedKeepDiagnostic), Is.True, reorderedKeepDiagnostic == null ? null : reorderedKeepDiagnostic.message);
                Assert.That(reorderedKeepSnapshot.KeptRawBlendShapeNames, Is.EqualTo(new[] { "Alpha", "Expression" }));
                keptNamesRegistry.Update();
                keptNames = keptNamesRegistry.FindProperty("keptRawBlendShapeNames");
                keptNames.arraySize = 1;
                keptNames.GetArrayElementAtIndex(0).stringValue = "Unknown";
                keptNamesRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out _, out StackMachineDiagnostic unknownKeepDiagnostic), Is.False);
                Assert.That(unknownKeepDiagnostic.domainCode, Is.EqualTo("FigureMorphAuthoringInvalid"));
                Assert.That(reloaded.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "Unknown" }));
                keptNamesRegistry.Update();
                keptNames = keptNamesRegistry.FindProperty("keptRawBlendShapeNames");
                keptNames.arraySize = 2;
                keptNames.GetArrayElementAtIndex(0).stringValue = "Alpha";
                keptNames.GetArrayElementAtIndex(1).stringValue = "Alpha";
                keptNamesRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out _, out StackMachineDiagnostic duplicateKeepDiagnostic), Is.False);
                Assert.That(duplicateKeepDiagnostic.domainCode, Is.EqualTo("FigureMorphAuthoringInvalid"));
                Assert.That(reloaded.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "Alpha", "Alpha" }));
                Assert.That(reloaded.Registry.TrySetKeptRawBlendShapeNames(reloaded, new[] { "Alpha", "Expression" }, out keepDiagnostic), Is.True, keepDiagnostic);
                ShapeSyncDatabaseRegistry.AxisFigureEntry persistedAxisFigure = reloaded.Registry.FigureAxes.Single().Figures.Single();
                FieldInfo axisFigureReference = typeof(ShapeSyncDatabaseRegistry.AxisFigureEntry).GetField("figure", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(axisFigureReference, Is.Not.Null);
                GameObject originalAxisFigure = persistedAxisFigure.Figure;
                axisFigureReference.SetValue(persistedAxisFigure, null);
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out ShapeSyncFigureGenerateSnapshot staleAxisSnapshot, out StackMachineDiagnostic staleAxisDiagnostic), Is.True, staleAxisDiagnostic == null ? null : staleAxisDiagnostic.message);
                Assert.That(staleAxisSnapshot.Axes.Single().Figures.Single().Figure, Is.Not.Null);
                Assert.That(persistedAxisFigure.Figure, Is.Null, "Generate snapshot must not repair a stale axis Figure registry reference.");
                axisFigureReference.SetValue(persistedAxisFigure, originalAxisFigure);
                SerializedObject serializedRegistry = new SerializedObject(reloaded.Registry);
                serializedRegistry.FindProperty("figureAxes").GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "MCM_Invalid";
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(snapshot.Axes.Single().Name, Is.EqualTo("Tall"));
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(reloaded, out _, out StackMachineDiagnostic invalidAxisDiagnostic), Is.False);
                Assert.That(invalidAxisDiagnostic.domainCode, Is.EqualTo("FigureAxisNameInvalid"));
                serializedRegistry.Update();
                serializedRegistry.FindProperty("figureAxes").GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = "Tall";
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(reloaded.transform.Find("Intermediate/Master").GetComponent<Animator>(), Is.Not.Null);
                Animator storedFbmAnimator = reloaded.transform.Find("Intermediate/Tall").GetComponentInChildren<Animator>(true);
                Assert.That(storedFbmAnimator, Is.Not.Null, "FBM import must retain its Humanoid Animator.");
                Assert.That(storedFbmAnimator.avatar, Is.Not.Null);
                Assert.That(storedFbmAnimator.avatar.isHuman && storedFbmAnimator.avatar.isValid, Is.True);
                Assert.That(AssetDatabase.GetAssetPath(storedFbmAnimator.avatar), Is.EqualTo(AssetDatabase.GetAssetPath(database)), "FBM import must own an Avatar sub-asset in its Database.");
                Assert.That(storedFbmAnimator.avatar, Is.Not.SameAs(reloaded.transform.Find("Intermediate/Master").GetComponent<Animator>().avatar));
                Hash128 hashBeforeRejectedFbmReplacement = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(database));
                Assert.That(ShapeSyncFigureAxisImport.TryReplaceFbm(AssetDatabase.GetAssetPath(database), "Tall", "Tall", true, meshOnlyAdmission, out string missingAnimatorReplacementDiagnostic), Is.False);
                Assert.That(missingAnimatorReplacementDiagnostic, Does.Contain("Humanoid Animator and Avatar"));
                Assert.That(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(database)), Is.EqualTo(hashBeforeRejectedFbmReplacement), "Rejected FBM replacement must not modify the Database.");
                Assert.That(reloaded.Registry.TryAdmitFigureAxes(reloaded, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Long", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] pbmAxes, out string pbmAxesDiagnostic), Is.True, pbmAxesDiagnostic);
                ShapeSyncAxisFigureSource[] pbmSources =
                {
                    new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, axisAdmission),
                    new ShapeSyncAxisFigureSource("Tall", axisAdmission)
                };
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(pbmAxes[0], pbmSources) }, out string pbmImportDiagnostic), Is.True, pbmImportDiagnostic);
                SeedOutfitCollection(AssetDatabase.GetAssetPath(database), "CollectionPbmStable", persistentBase);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase collectionAfterPbmImport, out string collectionAfterPbmImportDiagnostic), Is.True, collectionAfterPbmImportDiagnostic);
                AssertCollectionArtifactsPresent(collectionAfterPbmImport, "CollectionPbmStable");
                int avatarCountBeforePbmReplacement = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count();
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fallbackSourceDatabase, out string fallbackSourceOpenDiagnostic), Is.True, fallbackSourceOpenDiagnostic);
                GameObject oldBasePbmFigure = fallbackSourceDatabase.transform.Find("Intermediate/Master_Long").gameObject;
                GameObject oldFbmPbmFigure = fallbackSourceDatabase.transform.Find("Intermediate/Tall_Long").gameObject;
                Assert.That(ShapeSyncFigureImport.TryAdmitStoredDatabaseFigure(oldBasePbmFigure, out ShapeSyncFigureImportAdmission fallbackBaseAdmission, out string fallbackBaseDiagnostic), Is.True, fallbackBaseDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryAdmitStoredDatabaseFigure(oldFbmPbmFigure, out ShapeSyncFigureImportAdmission fallbackFbmAdmission, out string fallbackFbmDiagnostic), Is.True, fallbackFbmDiagnostic);
                ShapeSyncAxisFigureSource[] fallbackPbmSources =
                {
                    new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, fallbackBaseAdmission),
                    new ShapeSyncAxisFigureSource("Tall", fallbackFbmAdmission)
                };
                SeedOutfitPbmFollow(AssetDatabase.GetAssetPath(database), "Long", "FollowReplaceRollback");
                Func<GameObject, string, bool> originalSavePrefab = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureAxisImport.TryReplacePbm(AssetDatabase.GetAssetPath(database), "Long", "LongRollback", fallbackPbmSources, out string rollbackPbmReplaceDiagnostic), Is.False);
                    Assert.That(rollbackPbmReplaceDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefab; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase pbmReplaceRolledBack, out string pbmReplaceRollbackOpenDiagnostic), Is.True, pbmReplaceRollbackOpenDiagnostic);
                Assert.That(pbmReplaceRolledBack.Registry.FigureAxes.Any(axis => axis.Name == "Long" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True, "Failed PBM replacement must restore the original Figure axis.");
                Assert.That(pbmReplaceRolledBack.Registry.Outfits.Single(entry => entry.Identity == "FollowReplaceRollback").PbmFollows, Is.Not.Empty, "Failed PBM replacement must restore the saved Outfit follow.");
                Assert.That(pbmReplaceRolledBack.transform.Find("Intermediate/FollowReplaceRollback_Long_Master"), Is.Not.Null, "Failed PBM replacement must restore the follow Prefab.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>().Any(mesh => mesh.name == "FollowReplaceRollback_Long_Master_SkinnedMesh"), Is.True, "Failed PBM replacement must restore the follow Mesh.");
                AssertCollectionArtifactsPresent(pbmReplaceRolledBack, "CollectionPbmStable");
                GameObject restoredBasePbmFigure = pbmReplaceRolledBack.transform.Find("Intermediate/Master_Long").gameObject;
                GameObject restoredTallPbmFigure = pbmReplaceRolledBack.transform.Find("Intermediate/Tall_Long").gameObject;
                Assert.That(ShapeSyncFigureImport.TryAdmitStoredDatabaseFigure(restoredBasePbmFigure, out fallbackBaseAdmission, out string restoredBaseAdmissionDiagnostic), Is.True, restoredBaseAdmissionDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryAdmitStoredDatabaseFigure(restoredTallPbmFigure, out fallbackFbmAdmission, out string restoredTallAdmissionDiagnostic), Is.True, restoredTallAdmissionDiagnostic);
                fallbackPbmSources = new[]
                {
                    new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, fallbackBaseAdmission),
                    new ShapeSyncAxisFigureSource("Tall", fallbackFbmAdmission)
                };
                SeedOutfitPbmFollow(AssetDatabase.GetAssetPath(database), "Long", "FollowReplaceLong");
                Assert.That(ShapeSyncFigureAxisImport.TryReplacePbm(AssetDatabase.GetAssetPath(database), "Long", "LongFallback", fallbackPbmSources, out string fallbackPbmReplaceDiagnostic), Is.True, fallbackPbmReplaceDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase pbmFollowClearedByReplace, out string pbmFollowReplaceOpenDiagnostic), Is.True, pbmFollowReplaceOpenDiagnostic);
                Assert.That(pbmFollowClearedByReplace.Registry.Outfits.Single(entry => entry.Identity == "FollowReplaceLong").PbmFollows, Is.Empty, "PBM replacement must invalidate every saved Outfit follow.");
                Assert.That(pbmFollowClearedByReplace.transform.Find("Intermediate/FollowReplaceLong_Long_Master"), Is.Null, "PBM replacement must remove the Base follow Prefab.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>().Any(mesh => mesh.name == "FollowReplaceLong_Long_Master_SkinnedMesh"), Is.False, "PBM replacement must remove the Base follow Mesh.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count(), Is.EqualTo(avatarCountBeforePbmReplacement), "PBM replacement must recover only the replaced Figures' unreferenced Avatar sub-assets.");
                AssertCollectionArtifactsPresent(pbmFollowClearedByReplace, "CollectionPbmStable");
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fallbackReplacedDatabase, out string fallbackReplacedOpenDiagnostic), Is.True, fallbackReplacedOpenDiagnostic);
                Animator fallbackBaseAnimator = fallbackReplacedDatabase.transform.Find("Intermediate/Master_LongFallback").GetComponentInChildren<Animator>(true);
                Animator fallbackFbmAnimator = fallbackReplacedDatabase.transform.Find("Intermediate/Tall_LongFallback").GetComponentInChildren<Animator>(true);
                Assert.That(fallbackBaseAnimator.avatar, Is.Not.Null.And.Matches<Avatar>(avatar => avatar.isHuman && avatar.isValid));
                Assert.That(fallbackFbmAnimator.avatar, Is.Not.Null.And.Matches<Avatar>(avatar => avatar.isHuman && avatar.isValid));
                Assert.That(AssetDatabase.GetAssetPath(fallbackBaseAnimator.avatar), Is.EqualTo(AssetDatabase.GetAssetPath(database)), "PBM fallback replacement must retain a Database-local Base Avatar after removing its source Figure.");
                Assert.That(AssetDatabase.GetAssetPath(fallbackFbmAnimator.avatar), Is.EqualTo(AssetDatabase.GetAssetPath(database)), "PBM fallback replacement must retain a Database-local FBM Avatar after removing its source Figure.");
                Assert.That(ShapeSyncFigureAxisImport.TryReplacePbm(AssetDatabase.GetAssetPath(database), "LongFallback", "LongRenamed", pbmSources, out string pbmReplaceDiagnostic), Is.True, pbmReplaceDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase replaced, out string replacedDiagnostic), Is.True, replacedDiagnostic);
                Assert.That(replaced.Registry.TryAdmitFigureAxes(replaced, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] widePbmAxes, out string widePbmAxesDiagnostic), Is.True, widePbmAxesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(widePbmAxes[0], pbmSources) }, out string widePbmImportDiagnostic), Is.True, widePbmImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out replaced, out replacedDiagnostic), Is.True, replacedDiagnostic);
                Assert.That(replaced.Registry.FigureAxes.Any(axis => axis.Name == "LongRenamed" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True);
                Assert.That(replaced.Registry.FigureAxes.Any(axis => axis.Name == "Wide" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True);
                Assert.That(replaced.transform.Find("Intermediate/Master_LongRenamed"), Is.Not.Null);
                Assert.That(replaced.transform.Find("Intermediate/Tall_LongRenamed"), Is.Not.Null);
                Material[] expectedFigureMaterials = replaced.Registry.MaterialEntries.OrderBy(entry => entry.MaterialSlot).Select(entry => entry.Material).ToArray();
                Assert.That(replaced.transform.Find("Intermediate/Master_LongRenamed").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(expectedFigureMaterials));
                Assert.That(replaced.transform.Find("Intermediate/Tall_LongRenamed").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(expectedFigureMaterials));
                Animator storedBasePbmAnimator = replaced.transform.Find("Intermediate/Master_LongRenamed").GetComponentInChildren<Animator>(true);
                Animator storedFbmPbmAnimator = replaced.transform.Find("Intermediate/Tall_LongRenamed").GetComponentInChildren<Animator>(true);
                Assert.That(storedBasePbmAnimator, Is.Not.Null, "PBM import must retain a supplied Humanoid Animator.");
                Assert.That(storedFbmPbmAnimator, Is.Not.Null, "PBM import must retain a supplied Humanoid Animator.");
                Assert.That(AssetDatabase.GetAssetPath(storedBasePbmAnimator.avatar), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                Assert.That(AssetDatabase.GetAssetPath(storedFbmPbmAnimator.avatar), Is.EqualTo(AssetDatabase.GetAssetPath(database)));
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(replaced, out ShapeSyncFigureGenerateSnapshot pbmSnapshot, out StackMachineDiagnostic pbmSnapshotDiagnostic), Is.True, pbmSnapshotDiagnostic == null ? null : pbmSnapshotDiagnostic.message);
                Assert.That(ShapeSyncFigureGenerateMeshBuilder.TryBuild(pbmSnapshot, out ShapeSyncFigureGenerateMeshBuilder.Result pbmMeshResult, out StackMachineDiagnostic pbmMeshBuildDiagnostic), Is.True, pbmMeshBuildDiagnostic == null ? null : pbmMeshBuildDiagnostic.message);
                using (pbmMeshResult)
                {
                    ShapeSyncFigureGenerateSnapshot noFbmPbmSnapshot = CreateSnapshotWithoutFbmAxes(pbmSnapshot);
                    Assert.That(ShapeSyncFigureGenerateMeshBuilder.TryBuild(noFbmPbmSnapshot, out ShapeSyncFigureGenerateMeshBuilder.Result noFbmPbmResult, out StackMachineDiagnostic noFbmPbmBuildDiagnostic), Is.True, noFbmPbmBuildDiagnostic == null ? null : noFbmPbmBuildDiagnostic.message);
                    using (noFbmPbmResult)
                    {
                        Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(noFbmPbmSnapshot, noFbmPbmResult, out StackMachineDiagnostic noFbmPbmApplyDiagnostic), Is.True, noFbmPbmApplyDiagnostic == null ? null : noFbmPbmApplyDiagnostic.message);
                        Assert.That(noFbmPbmResult.Mesh.GetBlendShapeIndex("PBM_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                        Assert.That(noFbmPbmResult.Mesh.GetBlendShapeIndex("PBM_Wide"), Is.GreaterThanOrEqualTo(0));
                        Assert.That(noFbmPbmResult.Mesh.GetBlendShapeIndex("PBM_Tall_LongRenamed"), Is.EqualTo(-1));
                        Assert.That(noFbmPbmResult.PbmTargets.All(target => target.pbmDifferenceTargets.Count == 0), Is.True);
                    }
                    string[] nonPbmFrameNames = Enumerable.Range(0, pbmMeshResult.Mesh.blendShapeCount)
                        .Select(pbmMeshResult.Mesh.GetBlendShapeName).Where(name => !name.StartsWith("PBM_", System.StringComparison.Ordinal)).ToArray();
                    Material[] expectedOutputMaterials = pbmSnapshot.MaterialEntries.OrderBy(entry => entry.MaterialSlot).Select(entry => entry.MaterialAsset).ToArray();
                    Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(pbmSnapshot, pbmMeshResult, out StackMachineDiagnostic pbmApplyDiagnostic), Is.True, pbmApplyDiagnostic == null ? null : pbmApplyDiagnostic.message);
                    Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_Tall_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_Wide"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_Tall_Wide"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(Enumerable.Range(0, pbmMeshResult.Mesh.blendShapeCount).Select(pbmMeshResult.Mesh.GetBlendShapeName).Where(name => !name.StartsWith("PBM_", System.StringComparison.Ordinal)), Is.EqualTo(nonPbmFrameNames));
                    Assert.That(pbmMeshResult.Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(expectedOutputMaterials));
                    ShapeSyncFigureGenerateSnapshot.Axis pbmAxis = pbmSnapshot.Axes.Single(axis => axis.Name == "LongRenamed");
                    Mesh oracleBase = pbmSnapshot.BaseFigure.GameObject.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                    Mesh oracleBasePbm = pbmAxis.Figures.Single(binding => binding.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey).Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                    Mesh oracleFbm = pbmSnapshot.Axes.Single(axis => axis.Name == "Tall").Figures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                    Mesh oracleCombined = pbmAxis.Figures.Single(binding => binding.ShapeKey == "Tall").Figure.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                    Assert.That(BlendShapeBakeUtility.TryBuildMeshDifference(oracleBase, oracleBasePbm, out Vector3[] oracleBaseVertices, out Vector3[] oracleBaseNormals, out Vector3[] oracleBaseTangents), Is.True);
                    Assert.That(BlendShapeBakeUtility.TryBuildMeshDifference(oracleBase, oracleFbm, out Vector3[] oracleFbmVertices, out Vector3[] oracleFbmNormals, out Vector3[] oracleFbmTangents), Is.True);
                    Assert.That(BlendShapeBakeUtility.TryBuildMeshDifference(oracleBase, oracleCombined, out Vector3[] oracleCombinedVertices, out Vector3[] oracleCombinedNormals, out Vector3[] oracleCombinedTangents), Is.True);
                    AssertBlendShapeDelta(pbmMeshResult.Mesh, "PBM_LongRenamed", oracleBaseVertices, oracleBaseNormals, oracleBaseTangents);
                    AssertBlendShapeDelta(pbmMeshResult.Mesh, "PBM_Tall_LongRenamed",
                        BlendShapeBakeUtility.Subtract(BlendShapeBakeUtility.Subtract(oracleCombinedVertices, oracleBaseVertices), oracleFbmVertices),
                        BlendShapeBakeUtility.Subtract(BlendShapeBakeUtility.Subtract(oracleCombinedNormals, oracleBaseNormals), oracleFbmNormals),
                        BlendShapeBakeUtility.Subtract(BlendShapeBakeUtility.Subtract(oracleCombinedTangents, oracleBaseTangents), oracleFbmTangents));
                    DynamicBoneBlendTarget pbmTarget = pbmMeshResult.PbmTargets.Single(target => target.blendName == "PBM_LongRenamed");
                    Assert.That(pbmTarget.targetAvatar, Is.Not.Null.And.Matches<Avatar>(avatar => avatar.isHuman && avatar.isValid));
                    Assert.That(pbmTarget.targetRegistry, Is.Not.Null);
                    Assert.That(pbmTarget.pbmDifferenceTargets, Has.Count.EqualTo(1));
                    Assert.That(pbmTarget.pbmDifferenceTargets[0].fbmBlendName, Is.EqualTo("Tall"));
                    Assert.That(pbmTarget.pbmDifferenceTargets[0].targetAvatar, Is.Not.Null.And.Matches<Avatar>(avatar => avatar.isHuman && avatar.isValid));
                    Assert.That(pbmTarget.pbmDifferenceTargets[0].targetRegistry, Is.Not.Null);
                    Assert.That(ShapeSyncFigureGenerator.TryGenerate(replaced, Root, "PbmGenerated/Registries", "PbmGenerated/Bindings", "PbmGenerated/Materials", "PbmGenerated/Textures", out string pbmGenerateDiagnostic), Is.True, pbmGenerateDiagnostic);
                    GameObject pbmGeneratedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Master.prefab");
                    DynamicBoneBlender generatedPbmBlender = pbmGeneratedPrefab.GetComponent<DynamicBoneBlender>();
                    DynamicBoneBlendTarget generatedPbmTarget = generatedPbmBlender.Targets.Single(target => target.blendName == "PBM_LongRenamed");
                    MeshBinding generatedPbmMeshBinding = AssetDatabase.LoadAssetAtPath<MeshBinding>(Root + "/PbmGenerated/Bindings/Master_MeshBinding.asset");
                    Assert.That(generatedPbmMeshBinding.Morphs.Select(entry => (entry.logicalName, entry.targetName)),
                        Is.EqualTo(new[] { ("Tall", "Tall"), ("LongRenamed", "PBM_LongRenamed"), ("Wide", "PBM_Wide") }),
                        "MeshBinding must transfer every Database FBM/PBM name to its exact DDB target name.");
                    Assert.That(pbmGeneratedPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.GetBlendShapeIndex("PBM_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(pbmGeneratedPrefab.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.GetBlendShapeIndex("PBM_Tall_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                    Assert.That(generatedPbmTarget.targetAvatar, Is.Not.Null.And.Matches<Avatar>(avatar => avatar.isHuman && avatar.isValid));
                    Assert.That(generatedPbmTarget.targetRegistry, Is.Not.Null);
                    Assert.That(generatedPbmTarget.pbmDifferenceTargets.Select(target => target.fbmBlendName), Is.EqualTo(new[] { "Tall" }));
                    Assert.That(generatedPbmTarget.pbmDifferenceTargets.Single().targetAvatar, Is.Not.Null.And.Matches<Avatar>(avatar => avatar.isHuman && avatar.isValid));
                    Assert.That(generatedPbmTarget.pbmDifferenceTargets.Single().targetRegistry, Is.Not.Null);
                    int firstPbmAvatarId = pbmTarget.targetAvatar.GetInstanceID();
                    int firstPbmRegistryId = pbmTarget.targetRegistry.GetInstanceID();
                    Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(pbmSnapshot, pbmMeshResult, out StackMachineDiagnostic pbmReapplyDiagnostic), Is.True, pbmReapplyDiagnostic == null ? null : pbmReapplyDiagnostic.message);
                    Assert.That(Enumerable.Range(0, pbmMeshResult.Mesh.blendShapeCount).Count(index => pbmMeshResult.Mesh.GetBlendShapeName(index) == "PBM_LongRenamed"), Is.EqualTo(1));
                    Assert.That(Enumerable.Range(0, pbmMeshResult.Mesh.blendShapeCount).Count(index => pbmMeshResult.Mesh.GetBlendShapeName(index) == "PBM_Tall_LongRenamed"), Is.EqualTo(1));
                    pbmTarget = pbmMeshResult.PbmTargets.Single(target => target.blendName == "PBM_LongRenamed");
                    Assert.That(pbmTarget.targetAvatar.GetInstanceID(), Is.Not.EqualTo(firstPbmAvatarId));
                    Assert.That(pbmTarget.targetRegistry.GetInstanceID(), Is.Not.EqualTo(firstPbmRegistryId));
                    Assert.That(pbmMeshResult.PbmTargets.Count(target => target.blendName == "PBM_LongRenamed"), Is.EqualTo(1));
                    Assert.That(pbmMeshResult.PbmTargets.Count(target => target.blendName != null && target.blendName.StartsWith("PBM_", System.StringComparison.Ordinal)), Is.EqualTo(2));
                    int committedPbmAvatarId = pbmTarget.targetAvatar.GetInstanceID();
                    int committedPbmRegistryId = pbmTarget.targetRegistry.GetInstanceID();
                    FieldInfo commitFailureHook = typeof(ShapeSyncFigureGeneratePbmBuilder).GetField("beforeCommitForTests", BindingFlags.Static | BindingFlags.NonPublic);
                    Assert.That(commitFailureHook, Is.Not.Null);
                    commitFailureHook.SetValue(null, (System.Action)(() => throw new System.InvalidOperationException("Injected PBM commit failure.")));
                    try
                    {
                        Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(pbmSnapshot, pbmMeshResult, out StackMachineDiagnostic pbmCommitFailureDiagnostic), Is.False);
                        Assert.That(pbmCommitFailureDiagnostic.domainCode, Is.EqualTo("PbmGenerateInvalid"));
                        Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                        Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_Tall_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                        pbmTarget = pbmMeshResult.PbmTargets.Single(target => target.blendName == "PBM_LongRenamed");
                        Assert.That(pbmTarget.targetAvatar.GetInstanceID(), Is.EqualTo(committedPbmAvatarId));
                        Assert.That(pbmTarget.targetRegistry.GetInstanceID(), Is.EqualTo(committedPbmRegistryId));
                    }
                    finally { commitFailureHook.SetValue(null, null); }
                    SkinnedMeshRenderer combinedPbmRenderer = pbmSnapshot.Axes.Single(axis => axis.Name == "LongRenamed").Figures.Single(binding => binding.ShapeKey == "Tall").Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                    Mesh committedCombinedMesh = combinedPbmRenderer.sharedMesh;
                    Mesh invalidCombinedMesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward }, triangles = new[] { 0, 1, 2 } };
                    try
                    {
                        combinedPbmRenderer.sharedMesh = invalidCombinedMesh;
                        Assert.That(ShapeSyncFigureGeneratePbmBuilder.TryApply(pbmSnapshot, pbmMeshResult, out StackMachineDiagnostic pbmFailureDiagnostic), Is.False);
                        Assert.That(pbmFailureDiagnostic.domainCode, Is.EqualTo("PbmGenerateInvalid"));
                        Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                        Assert.That(pbmMeshResult.Mesh.GetBlendShapeIndex("PBM_Tall_LongRenamed"), Is.GreaterThanOrEqualTo(0));
                        pbmTarget = pbmMeshResult.PbmTargets.Single(target => target.blendName == "PBM_LongRenamed");
                        Assert.That(pbmTarget.targetAvatar.GetInstanceID(), Is.EqualTo(committedPbmAvatarId));
                        Assert.That(pbmTarget.targetRegistry.GetInstanceID(), Is.EqualTo(committedPbmRegistryId));
                    }
                    finally
                    {
                        combinedPbmRenderer.sharedMesh = committedCombinedMesh;
                        Object.DestroyImmediate(invalidCombinedMesh);
                    }
                }
                ShapeSyncDatabaseRegistry.FigureAxisEntry persistedPbmAxis = replaced.Registry.FigureAxes.Single(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && axis.Name == "LongRenamed");
                ShapeSyncDatabaseRegistry.AxisFigureEntry persistedPbmBase = persistedPbmAxis.Figures.Single(binding => binding.FbmName == ShapeSyncDatabaseRegistry.BaseShapeKey);
                ShapeSyncDatabaseRegistry.AxisFigureEntry persistedPbmFbm = persistedPbmAxis.Figures.Single(binding => binding.FbmName == "Tall");
                FieldInfo pbmFigureReference = typeof(ShapeSyncDatabaseRegistry.AxisFigureEntry).GetField("figure", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo pbmOwnerName = typeof(ShapeSyncDatabaseRegistry.AxisFigureEntry).GetField("fbmName", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pbmFigureReference, Is.Not.Null);
                Assert.That(pbmOwnerName, Is.Not.Null);
                GameObject originalPbmBaseFigure = persistedPbmBase.Figure;
                GameObject originalPbmFbmFigure = persistedPbmFbm.Figure;
                pbmFigureReference.SetValue(persistedPbmBase, null);
                pbmFigureReference.SetValue(persistedPbmFbm, null);
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(replaced, out ShapeSyncFigureGenerateSnapshot stalePbmSnapshot, out StackMachineDiagnostic stalePbmDiagnostic), Is.True, stalePbmDiagnostic == null ? null : stalePbmDiagnostic.message);
                Assert.That(stalePbmSnapshot.Axes.Single(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && axis.Name == "LongRenamed").Figures.Select(figure => figure.ShapeKey), Is.EquivalentTo(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey, "Tall" }));
                Assert.That(persistedPbmBase.Figure, Is.Null, "Generate snapshot must not repair a stale PBM Base binding.");
                Assert.That(persistedPbmFbm.Figure, Is.Null, "Generate snapshot must not repair a stale PBM FBM binding.");
                pbmFigureReference.SetValue(persistedPbmBase, originalPbmBaseFigure);
                pbmFigureReference.SetValue(persistedPbmFbm, originalPbmFbmFigure);
                pbmOwnerName.SetValue(persistedPbmFbm, "UnknownOwner");
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(replaced, out _, out StackMachineDiagnostic unresolvedPbmDiagnostic), Is.False);
                Assert.That(unresolvedPbmDiagnostic.domainCode, Is.EqualTo("FigureAxisInvalid"));
                Assert.That(persistedPbmFbm.Figure, Is.SameAs(originalPbmFbmFigure), "PBM resolution reject must not alter the Registry binding.");
                pbmOwnerName.SetValue(persistedPbmFbm, "Tall");
                Assert.That(ShapeSyncFigureAxisImport.TryRenamePbm(AssetDatabase.GetAssetPath(database), "LongRenamed", "LongStable", out string renamePbmDiagnostic), Is.True, renamePbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase collectionAfterPbmRename, out string collectionAfterPbmRenameDiagnostic), Is.True, collectionAfterPbmRenameDiagnostic);
                AssertCollectionArtifactsPresent(collectionAfterPbmRename, "CollectionPbmStable");
                replaced = collectionAfterPbmRename;
                Assert.That(replaced.Registry.TryAdmitFigureAxes(replaced, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("MeshOnly", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] meshOnlyPbmAxes, out string meshOnlyPbmAxesDiagnostic), Is.True, meshOnlyPbmAxesDiagnostic);
                ShapeSyncAxisFigureSource[] meshOnlyPbmSources =
                {
                    new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, meshOnlyAdmission),
                    new ShapeSyncAxisFigureSource("Tall", meshOnlyAdmission)
                };
                int avatarCountBeforeMeshOnlyPbm = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count();
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(meshOnlyPbmAxes[0], meshOnlyPbmSources) }, out string meshOnlyPbmImportDiagnostic), Is.True, meshOnlyPbmImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase meshOnlyPbmDatabase, out string meshOnlyPbmOpenDiagnostic), Is.True, meshOnlyPbmOpenDiagnostic);
                Assert.That(meshOnlyPbmDatabase.transform.Find("Intermediate/Master_MeshOnly").GetComponentsInChildren<Animator>(true), Is.Empty, "PBM without an Animator source must not synthesize one.");
                Assert.That(meshOnlyPbmDatabase.transform.Find("Intermediate/Tall_MeshOnly").GetComponentsInChildren<Animator>(true), Is.Empty, "PBM without an Animator source must not synthesize one.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count(), Is.EqualTo(avatarCountBeforeMeshOnlyPbm));
                AssertCollectionArtifactsPresent(meshOnlyPbmDatabase, "CollectionPbmStable");
                Assert.That(ShapeSyncFigureAxisImport.TryRemovePbm(AssetDatabase.GetAssetPath(database), "MeshOnly", out string meshOnlyPbmRemoveDiagnostic), Is.True, meshOnlyPbmRemoveDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase collectionAfterPbmRemove, out string collectionAfterPbmRemoveDiagnostic), Is.True, collectionAfterPbmRemoveDiagnostic);
                AssertCollectionArtifactsPresent(collectionAfterPbmRemove, "CollectionPbmStable");
                int avatarCountBeforeFbmRename = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count();
                Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(AssetDatabase.GetAssetPath(database), "Tall", "TallRenamed", out string renameFbmDiagnostic), Is.True, renameFbmDiagnostic);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count(), Is.EqualTo(2), "FBM rename must collect all dependent PBM Avatar sub-assets.");
                Assert.That(avatarCountBeforeFbmRename, Is.GreaterThan(2));
                Assert.That(ShapeSyncFigureAxisImport.TryRenameFbm(AssetDatabase.GetAssetPath(database), "TallRenamed", "Tall", out string restoreFbmDiagnostic), Is.True, restoreFbmDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase cleanupDatabase, out string cleanupOpenDiagnostic), Is.True, cleanupOpenDiagnostic);
                Assert.That(cleanupDatabase.Registry.TryAdmitFigureAxes(cleanupDatabase, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("CleanupPbm", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] cleanupPbmAxes, out string cleanupPbmAxesDiagnostic), Is.True, cleanupPbmAxesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(cleanupPbmAxes[0], pbmSources) }, out string cleanupPbmImportDiagnostic), Is.True, cleanupPbmImportDiagnostic);
                int avatarCountWithCleanupPbm = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count();
                Assert.That(ShapeSyncFigureAxisImport.TryRemovePbm(AssetDatabase.GetAssetPath(database), "CleanupPbm", out string cleanupPbmRemoveDiagnostic), Is.True, cleanupPbmRemoveDiagnostic);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count(), Is.EqualTo(avatarCountWithCleanupPbm - 2), "PBM removal must collect only its two unreferenced Figure Avatar sub-assets.");
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out cleanupDatabase, out cleanupOpenDiagnostic), Is.True, cleanupOpenDiagnostic);
                Assert.That(cleanupDatabase.Registry.TryAdmitFigureAxes(cleanupDatabase, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("ReplaceCleanupPbm", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] replaceCleanupPbmAxes, out string replaceCleanupPbmAxesDiagnostic), Is.True, replaceCleanupPbmAxesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(replaceCleanupPbmAxes[0], pbmSources) }, out string replaceCleanupPbmImportDiagnostic), Is.True, replaceCleanupPbmImportDiagnostic);
                SeedOutfitPbmFollow(AssetDatabase.GetAssetPath(database), "ReplaceCleanupPbm", "FollowFbmRollback");
                SeedOutfitCollection(AssetDatabase.GetAssetPath(database), "CollectionFbmReplacement", persistentBase);
                Func<GameObject, string, bool> originalFbmReplacementSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureAxisImport.TryReplaceFbm(AssetDatabase.GetAssetPath(database), "Tall", "Tall", true, axisAdmission, out string rollbackFbmReplaceDiagnostic), Is.False);
                    Assert.That(rollbackFbmReplaceDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalFbmReplacementSave; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fbmReplaceRolledBack, out string fbmReplaceRollbackOpenDiagnostic), Is.True, fbmReplaceRollbackOpenDiagnostic);
                Assert.That(fbmReplaceRolledBack.Registry.FigureAxes.Any(axis => axis.Name == "ReplaceCleanupPbm" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True, "Failed FBM redefinition must restore dependent PBM axes.");
                Assert.That(fbmReplaceRolledBack.Registry.Outfits.Single(entry => entry.Identity == "FollowFbmRollback").PbmFollows, Is.Not.Empty, "Failed FBM redefinition must restore the saved Outfit follow.");
                Assert.That(fbmReplaceRolledBack.transform.Find("Intermediate/FollowFbmRollback_ReplaceCleanupPbm_Master"), Is.Not.Null, "Failed FBM redefinition must restore the follow Prefab.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>().Any(mesh => mesh.name == "FollowFbmRollback_ReplaceCleanupPbm_Master_SkinnedMesh"), Is.True, "Failed FBM redefinition must restore the follow Mesh.");
                Assert.That(fbmReplaceRolledBack.Registry.Outfits.Single(entry => entry.Identity == "CollectionFbmReplacement").CollectionEntries, Is.Not.Empty, "Failed FBM replacement must restore the saved Collection declaration.");
                AssertCollectionArtifactsPresent(fbmReplaceRolledBack, "CollectionFbmReplacement");
                SeedOutfitPbmFollow(AssetDatabase.GetAssetPath(database), "ReplaceCleanupPbm", "FollowFbmRedefinition");
                Assert.That(ShapeSyncFigureAxisImport.TryReplaceFbm(AssetDatabase.GetAssetPath(database), "Tall", "Tall", true, axisAdmission, out string replaceFbmCleanupDiagnostic), Is.True, replaceFbmCleanupDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase pbmFollowClearedByFbmReplacement, out string fbmFollowReplaceOpenDiagnostic), Is.True, fbmFollowReplaceOpenDiagnostic);
                Assert.That(pbmFollowClearedByFbmReplacement.Registry.Outfits.Single(entry => entry.Identity == "FollowFbmRedefinition").PbmFollows, Is.Empty, "FBM redefinition must invalidate every saved Outfit follow.");
                Assert.That(pbmFollowClearedByFbmReplacement.transform.Find("Intermediate/FollowFbmRedefinition_ReplaceCleanupPbm_Master"), Is.Null, "FBM redefinition must remove the Base follow Prefab.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>().Any(mesh => mesh.name == "FollowFbmRedefinition_ReplaceCleanupPbm_Master_SkinnedMesh"), Is.False, "FBM redefinition must remove the Base follow Mesh.");
                Assert.That(pbmFollowClearedByFbmReplacement.Registry.Outfits.Single(entry => entry.Identity == "CollectionFbmReplacement").CollectionEntries, Is.Empty, "FBM replacement must invalidate the saved Collection declaration.");
                AssertCollectionArtifactsAbsent(pbmFollowClearedByFbmReplacement, "CollectionFbmReplacement");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count(), Is.EqualTo(2), "FBM replacement must collect the replaced FBM and dependent PBM Avatar sub-assets before attaching its replacement.");
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase genericFbmRedefinitionDatabase, out string genericFbmRedefinitionOpenDiagnostic), Is.True, genericFbmRedefinitionOpenDiagnostic);
                Assert.That(genericFbmRedefinitionDatabase.Registry.TryAdmitFigureAxes(genericFbmRedefinitionDatabase,
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("ImportCleanupPbm", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm) },
                    out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] genericFbmPbmAxes, out string genericFbmPbmAxesDiagnostic), Is.True, genericFbmPbmAxesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database),
                    new[] { new ShapeSyncFigureAxisImportRequest(genericFbmPbmAxes[0], pbmSources) }, out string genericFbmPbmImportDiagnostic), Is.True, genericFbmPbmImportDiagnostic);
                SeedOutfitPbmFollow(AssetDatabase.GetAssetPath(database), "ImportCleanupPbm", "FollowGenericFbmRollback");
                SeedOutfitCollection(AssetDatabase.GetAssetPath(database), "CollectionGenericFbm", persistentBase);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out genericFbmRedefinitionDatabase, out genericFbmRedefinitionOpenDiagnostic), Is.True, genericFbmRedefinitionOpenDiagnostic);
                Assert.That(genericFbmRedefinitionDatabase.Registry.TryAdmitFigureAxes(genericFbmRedefinitionDatabase,
                    new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("TallReplacement", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, true) },
                    out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] genericFbmAxes, out string genericFbmAxesDiagnostic), Is.True, genericFbmAxesDiagnostic);
                Func<GameObject, string, bool> originalGenericFbmSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database),
                        new[] { new ShapeSyncFigureAxisImportRequest(genericFbmAxes[0], new[] { new ShapeSyncAxisFigureSource("TallReplacement", axisAdmission) }) },
                        out string rollbackGenericFbmDiagnostic), Is.False);
                    Assert.That(rollbackGenericFbmDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalGenericFbmSave; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase genericFbmRolledBack, out string genericFbmRollbackOpenDiagnostic), Is.True, genericFbmRollbackOpenDiagnostic);
                Assert.That(genericFbmRolledBack.Registry.FigureAxes.Any(axis => axis.Name == "ImportCleanupPbm" && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm), Is.True, "Failed generic FBM redefinition must restore dependent PBM axes.");
                Assert.That(genericFbmRolledBack.Registry.Outfits.Single(entry => entry.Identity == "FollowGenericFbmRollback").PbmFollows, Is.Not.Empty, "Failed generic FBM redefinition must restore the saved Outfit follow.");
                Assert.That(genericFbmRolledBack.transform.Find("Intermediate/FollowGenericFbmRollback_ImportCleanupPbm_Master"), Is.Not.Null, "Failed generic FBM redefinition must restore the follow Prefab.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>().Any(mesh => mesh.name == "FollowGenericFbmRollback_ImportCleanupPbm_Master_SkinnedMesh"), Is.True, "Failed generic FBM redefinition must restore the follow Mesh.");
                Assert.That(genericFbmRolledBack.Registry.Outfits.Single(entry => entry.Identity == "CollectionGenericFbm").CollectionEntries, Is.Not.Empty, "Failed generic FBM redefinition must restore the saved Collection declaration.");
                AssertCollectionArtifactsPresent(genericFbmRolledBack, "CollectionGenericFbm");
                SeedOutfitPbmFollow(AssetDatabase.GetAssetPath(database), "ImportCleanupPbm", "FollowGenericFbmRedefinition");
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database),
                    new[] { new ShapeSyncFigureAxisImportRequest(genericFbmAxes[0], new[] { new ShapeSyncAxisFigureSource("TallReplacement", axisAdmission) }) },
                    out string genericFbmRedefinitionDiagnostic), Is.True, genericFbmRedefinitionDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase genericFbmFollowCleared, out string genericFbmFollowClearOpenDiagnostic), Is.True, genericFbmFollowClearOpenDiagnostic);
                Assert.That(genericFbmFollowCleared.Registry.Outfits.Single(entry => entry.Identity == "FollowGenericFbmRedefinition").PbmFollows, Is.Empty, "Generic FBM redefinition must invalidate every saved Outfit follow.");
                Assert.That(genericFbmFollowCleared.transform.Find("Intermediate/FollowGenericFbmRedefinition_ImportCleanupPbm_Master"), Is.Null, "Generic FBM redefinition must remove the Base follow Prefab.");
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>().Any(mesh => mesh.name == "FollowGenericFbmRedefinition_ImportCleanupPbm_Master_SkinnedMesh"), Is.False, "Generic FBM redefinition must remove the Base follow Mesh.");
                Assert.That(genericFbmFollowCleared.Registry.Outfits.Single(entry => entry.Identity == "CollectionGenericFbm").CollectionEntries, Is.Empty, "Generic FBM redefinition must invalidate the saved Collection declaration.");
                AssertCollectionArtifactsAbsent(genericFbmFollowCleared, "CollectionGenericFbm");
                SeedOutfitCollection(AssetDatabase.GetAssetPath(database), "CollectionFbmRemoval", persistentBase);
                Func<GameObject, string, bool> originalFbmRemovalSave = ShapeSyncDatabaseTransaction.SavePrefabAsset;
                try
                {
                    ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                    Assert.That(ShapeSyncFigureAxisImport.TryRemoveFbm(AssetDatabase.GetAssetPath(database), "TallReplacement", out string rollbackFbmRemovalDiagnostic), Is.False);
                    Assert.That(rollbackFbmRemovalDiagnostic, Does.Contain("could not be saved"));
                }
                finally { ShapeSyncDatabaseTransaction.SavePrefabAsset = originalFbmRemovalSave; }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fbmRemovalRolledBack, out string fbmRemovalRollbackOpenDiagnostic), Is.True, fbmRemovalRollbackOpenDiagnostic);
                Assert.That(fbmRemovalRolledBack.Registry.Outfits.Single(entry => entry.Identity == "CollectionFbmRemoval").CollectionEntries, Is.Not.Empty, "Failed FBM removal must restore the saved Collection declaration.");
                AssertCollectionArtifactsPresent(fbmRemovalRolledBack, "CollectionFbmRemoval");
                Assert.That(ShapeSyncFigureAxisImport.TryRemoveFbm(AssetDatabase.GetAssetPath(database), "TallReplacement", out string genericFbmCleanupDiagnostic), Is.True, genericFbmCleanupDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase fbmRemovalCleared, out string fbmRemovalClearOpenDiagnostic), Is.True, fbmRemovalClearOpenDiagnostic);
                Assert.That(fbmRemovalCleared.Registry.Outfits.Single(entry => entry.Identity == "CollectionFbmRemoval").CollectionEntries, Is.Empty, "FBM removal must invalidate the saved Collection declaration.");
                AssertCollectionArtifactsAbsent(fbmRemovalCleared, "CollectionFbmRemoval");
                multiAnimatorSource = Object.Instantiate(persistentBase);
                GameObject secondaryAnimatorObject = new GameObject("SecondaryAnimator");
                secondaryAnimatorObject.transform.SetParent(multiAnimatorSource.transform, false);
                secondaryAnimatorObject.AddComponent<Animator>().avatar = avatar;
                const string multiAnimatorPath = Root + "/AxisMultipleAnimators.prefab";
                Assert.That(PrefabUtility.SaveAsPrefabAsset(multiAnimatorSource, multiAnimatorPath), Is.Not.Null);
                GameObject persistentMultiAnimator = AssetDatabase.LoadAssetAtPath<GameObject>(multiAnimatorPath);
                Assert.That(ShapeSyncFigureImport.TryAdmitAxisSource(persistentMultiAnimator, out ShapeSyncFigureImportAdmission multiAnimatorAdmission, out string multiAnimatorAdmissionDiagnostic), Is.True, multiAnimatorAdmissionDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase multiAnimatorDatabase, out string multiAnimatorOpenDiagnostic), Is.True, multiAnimatorOpenDiagnostic);
                Assert.That(multiAnimatorDatabase.Registry.TryAdmitFigureAxes(multiAnimatorDatabase, new[] { new ShapeSyncDatabaseRegistry.FigureAxisDraft("AnimatorRetained", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) }, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] multiAnimatorAxes, out string multiAnimatorAxesDiagnostic), Is.True, multiAnimatorAxesDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), new[] { new ShapeSyncFigureAxisImportRequest(multiAnimatorAxes[0], new[] { new ShapeSyncAxisFigureSource("AnimatorRetained", multiAnimatorAdmission) }) }, out string multiAnimatorImportDiagnostic), Is.True, multiAnimatorImportDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(AssetDatabase.GetAssetPath(database), out ShapeSyncDatabase storedMultiAnimatorDatabase, out string storedMultiAnimatorOpenDiagnostic), Is.True, storedMultiAnimatorOpenDiagnostic);
                GameObject storedMultiAnimatorFbm = storedMultiAnimatorDatabase.transform.Find("Intermediate/AnimatorRetained").gameObject;
                Animator[] storedMultiAnimators = storedMultiAnimatorFbm.GetComponentsInChildren<Animator>(true);
                Assert.That(storedMultiAnimators, Has.Length.EqualTo(2), "Axis import must retain every source Animator rather than deleting nested Animator components.");
                Assert.That(storedMultiAnimators.Select(animator => animator.avatar).Distinct().Count(), Is.EqualTo(1), "Animators that shared one source Avatar must share one Database-local Avatar clone.");
                Assert.That(storedMultiAnimators.All(animator => animator.avatar != null && AssetDatabase.GetAssetPath(animator.avatar) == AssetDatabase.GetAssetPath(database)), Is.True);
                Assert.That(ShapeSyncDatabaseFigureExport.TryExport(storedMultiAnimatorDatabase, storedMultiAnimatorFbm, Root + "/AxisTallExport.prefab", out GameObject exportedTall, out string exportTallDiagnostic), Is.True, exportTallDiagnostic);
                Animator[] exportedTallAnimators = exportedTall.GetComponentsInChildren<Animator>(true);
                Assert.That(exportedTallAnimators, Has.Length.EqualTo(2), "FBM Export must retain every Animator stored in the Database Figure.");
                Assert.That(exportedTallAnimators.All(animator => animator.avatar != null && animator.avatar.isHuman && animator.avatar.isValid), Is.True);
                Assert.That(ShapeSyncFigureAxisImport.TryRemoveFbm(AssetDatabase.GetAssetPath(database), "Tall", out string fbmRemoveDiagnostic), Is.True, fbmRemoveDiagnostic);
                Assert.That(ShapeSyncFigureAxisImport.TryRemoveFbm(AssetDatabase.GetAssetPath(database), "AnimatorRetained", out string retainedFbmRemoveDiagnostic), Is.True, retainedFbmRemoveDiagnostic);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Avatar>().Count(), Is.EqualTo(1), "FBM removal must collect its own unreferenced Avatar after dependent PBMs have been removed.");
                string[] dependencies = AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(database), true);
                Assert.That(dependencies, Does.Not.Contain(Root + "/AxisBaseAvatar.asset"));
                Assert.That(dependencies, Does.Not.Contain(basePath));
            }
            finally { Object.DestroyImmediate(multiAnimatorSource); Object.DestroyImmediate(meshOnlySource); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ImportRecord_RejectsMissingOrDuplicateImportFacts()
        {
            GameObject databaseRoot = new GameObject("Database");
            databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject("Intermediate"); intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject host = new GameObject("RecordHost"); host.transform.SetParent(intermediate.transform, false);
            GameObject humanoid = CreateHumanoidSource("RecordAvatar", includeRenderer: false, out Avatar avatar);
            GameObject contents = null;
            try
            {
                AssetDatabase.CreateAsset(avatar, Root + "/RecordAvatar.asset");
                host.AddComponent<SkinnedMeshRenderer>();
                Assert.That(PrefabUtility.SaveAsPrefabAsset(databaseRoot, Root + "/MissingFacts.prefab"), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(Root + "/MissingFacts.prefab");
                ShapeSyncFigureImportRecord record = contents.transform.Find("Intermediate/RecordHost").gameObject.AddComponent<ShapeSyncFigureImportRecord>();
                SkinnedMeshRenderer renderer = record.GetComponent<SkinnedMeshRenderer>();
                Assert.That(record.TryConfigure(null, out string emptyDiagnostic), Is.False);
                Assert.That(emptyDiagnostic, Does.Contain("at least one"));
                Assert.That(record.TryConfigure(new[] { renderer, renderer }, out string duplicateDiagnostic), Is.False);
                Assert.That(duplicateDiagnostic, Does.Contain("unique"));
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(humanoid); }
        }

        [Test]
        public void ImportRecord_RejectsPlacementOutsideDatabase()
        {
            GameObject host = new GameObject("RecordHost");
            GameObject humanoid = CreateHumanoidSource("RecordAvatar", includeRenderer: true, out Avatar avatar);
            try
            {
                ShapeSyncFigureImportRecord record = host.AddComponent<ShapeSyncFigureImportRecord>();
                SkinnedMeshRenderer renderer = humanoid.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                Assert.That(record.TryConfigure(new[] { renderer }, out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("ShapeSync Database"));
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(humanoid); Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void ImportRecord_RejectsRendererOutsideCarrierAndPreservesConfirmedOrder()
        {
            GameObject databaseRoot = new GameObject("Database"); databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject("Intermediate"); intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject carrier = new GameObject("Carrier"); carrier.transform.SetParent(intermediate.transform, false);
            GameObject child = new GameObject("Child"); child.transform.SetParent(carrier.transform, false);
            SkinnedMeshRenderer ownedRenderer = child.AddComponent<SkinnedMeshRenderer>();
            GameObject external = new GameObject("External"); SkinnedMeshRenderer externalRenderer = external.AddComponent<SkinnedMeshRenderer>();
            GameObject humanoid = CreateHumanoidSource("RecordAvatar", includeRenderer: false, out Avatar avatar);
            GameObject contents = null;
            try
            {
                AssetDatabase.CreateAsset(avatar, Root + "/ExternalRecordAvatar.asset");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(databaseRoot, Root + "/ExternalCarrier.prefab"), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(Root + "/ExternalCarrier.prefab");
                ShapeSyncFigureImportRecord record = contents.transform.Find("Intermediate/Carrier").gameObject.AddComponent<ShapeSyncFigureImportRecord>();
                ownedRenderer = contents.transform.Find("Intermediate/Carrier/Child").GetComponent<SkinnedMeshRenderer>();
                Assert.That(record.TryConfigure(new[] { ownedRenderer }, out string configureDiagnostic), Is.True, configureDiagnostic);
                Assert.That(record.TryConfigure(new[] { externalRenderer }, out string rejectionDiagnostic), Is.False);
                Assert.That(rejectionDiagnostic, Does.Contain("below its carrier"));
                Assert.That(record.ConfirmedRendererOrder.Count, Is.EqualTo(1));
                Assert.That(record.ConfirmedRendererOrder[0], Is.SameAs(ownedRenderer));
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(external); Object.DestroyImmediate(humanoid); }
        }

        [Test]
        public void ImportRecord_RequiresIntermediateCarrier()
        {
            GameObject databaseRoot = new GameObject("Database"); databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject carrier = new GameObject("Carrier"); carrier.transform.SetParent(databaseRoot.transform, false);
            SkinnedMeshRenderer renderer = carrier.AddComponent<SkinnedMeshRenderer>();
            GameObject humanoid = CreateHumanoidSource("TransientRecordAvatar", includeRenderer: false, out Avatar avatar);
            GameObject contents = null;
            try
            {
                Assert.That(PrefabUtility.SaveAsPrefabAsset(databaseRoot, Root + "/TransientRecord.prefab"), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(Root + "/TransientRecord.prefab");
                ShapeSyncFigureImportRecord record = contents.transform.Find("Carrier").gameObject.AddComponent<ShapeSyncFigureImportRecord>();
                renderer = record.GetComponent<SkinnedMeshRenderer>();
                Assert.That(record.TryConfigure(new[] { renderer }, out string placementDiagnostic), Is.False);
                Assert.That(placementDiagnostic, Does.Contain("Intermediate"));
                GameObject intermediate = new GameObject("Intermediate"); intermediate.transform.SetParent(contents.transform, false);
                record.transform.SetParent(intermediate.transform, false);
                Assert.That(record.TryConfigure(new[] { renderer }, out string configureDiagnostic), Is.True, configureDiagnostic);
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(humanoid); }
        }

        [Test]
        public void ImportRecord_ConfirmedOrderCannotBeCastToSerializedArray()
        {
            GameObject databaseRoot = new GameObject("Database"); databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject("Intermediate"); intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject carrier = new GameObject("Carrier"); carrier.transform.SetParent(intermediate.transform, false);
            SkinnedMeshRenderer renderer = carrier.AddComponent<SkinnedMeshRenderer>();
            GameObject humanoid = CreateHumanoidSource("ReadonlyRecordAvatar", includeRenderer: false, out Avatar avatar);
            GameObject contents = null;
            try
            {
                AssetDatabase.CreateAsset(avatar, Root + "/ReadonlyRecordAvatar.asset");
                Assert.That(PrefabUtility.SaveAsPrefabAsset(databaseRoot, Root + "/ReadonlyRecord.prefab"), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(Root + "/ReadonlyRecord.prefab");
                ShapeSyncFigureImportRecord record = contents.transform.Find("Intermediate/Carrier").gameObject.AddComponent<ShapeSyncFigureImportRecord>();
                renderer = record.GetComponent<SkinnedMeshRenderer>();
                Assert.That(record.TryConfigure(new[] { renderer }, out string diagnostic), Is.True, diagnostic);
                Assert.That(record.ConfirmedRendererOrder as SkinnedMeshRenderer[], Is.Null);
                Assert.That(record.ConfirmedRendererOrder[0], Is.SameAs(renderer));
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(humanoid); }
        }

        [Test]
        public void ImportRecord_RejectsSceneDatabaseAndNullRendererElement()
        {
            GameObject databaseRoot = new GameObject("Database"); databaseRoot.AddComponent<ShapeSyncDatabase>();
            GameObject intermediate = new GameObject("Intermediate"); intermediate.transform.SetParent(databaseRoot.transform, false);
            GameObject carrier = new GameObject("Carrier"); carrier.transform.SetParent(intermediate.transform, false);
            carrier.AddComponent<SkinnedMeshRenderer>();
            GameObject humanoid = CreateHumanoidSource("RecordAvatar", includeRenderer: false, out Avatar avatar);
            GameObject contents = null;
            try
            {
                AssetDatabase.CreateAsset(avatar, Root + "/NullRendererAvatar.asset");
                ShapeSyncFigureImportRecord sceneRecord = carrier.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(sceneRecord.TryConfigure(new[] { carrier.GetComponent<SkinnedMeshRenderer>() }, out string sceneDiagnostic), Is.False);
                Assert.That(sceneDiagnostic, Does.Contain("Prefab contents"));
                Object.DestroyImmediate(sceneRecord);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(databaseRoot, Root + "/NullRenderer.prefab"), Is.Not.Null);
                contents = PrefabUtility.LoadPrefabContents(Root + "/NullRenderer.prefab");
                ShapeSyncFigureImportRecord record = contents.transform.Find("Intermediate/Carrier").gameObject.AddComponent<ShapeSyncFigureImportRecord>();
                Assert.That(record.TryConfigure(new SkinnedMeshRenderer[] { null }, out string nullRendererDiagnostic), Is.False);
                Assert.That(nullRendererDiagnostic, Does.Contain("non-null"));
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); Object.DestroyImmediate(databaseRoot); Object.DestroyImmediate(humanoid); }
        }

        [Test]
        public void MeshMerger_ReusesMeshUtilityForValidOrderAndRejectsInvalidOrderWithoutChangingSource()
        {
            GameObject source = CreateHumanoidSource("MergeSource", includeRenderer: true, out Avatar avatar);
            GameObject external = new GameObject("External");
            GameObject mergedRoot = null;
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(renderer, source.transform.Find("Hips"));
                GameObject secondObject = new GameObject("SecondRenderer"); secondObject.transform.SetParent(source.transform, false);
                SkinnedMeshRenderer secondRenderer = secondObject.AddComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(secondRenderer, source.transform.Find("Hips"));
                Mesh sourceMesh = renderer.sharedMesh;
                Material[] sourceMaterials = renderer.sharedMaterials;
                Assert.That(ShapeSyncFigureMeshMerger.TryMerge(source, new[] { renderer, secondRenderer }, out mergedRoot, out SkinnedMeshRenderer mergedRenderer, out string diagnostic), Is.True, diagnostic);
                Assert.That(mergedRoot, Is.Not.SameAs(source));
                Assert.That(mergedRenderer.sharedMesh.vertexCount, Is.EqualTo(sourceMesh.vertexCount * 2));
                Assert.That(mergedRenderer.sharedMesh.subMeshCount, Is.EqualTo(2));
                Assert.That(mergedRenderer.bones, Has.Length.EqualTo(1));
                Assert.That(mergedRenderer.sharedMesh.bindposes, Has.Length.EqualTo(1));
                Assert.That(mergedRenderer.sharedMaterials, Has.Length.EqualTo(2));
                Assert.That(mergedRenderer.sharedMaterials[0], Is.SameAs(sourceMaterials[0]));
                Assert.That(mergedRenderer.sharedMesh.blendShapeCount, Is.EqualTo(2));
                Assert.That(mergedRenderer.sharedMesh.GetBlendShapeName(0), Is.EqualTo("Expression"));
                Assert.That(mergedRenderer.sharedMesh.GetBlendShapeName(1), Is.EqualTo("SecondRenderer/Expression"));
                Assert.That(renderer.sharedMesh, Is.SameAs(sourceMesh));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(sourceMaterials));
                Assert.That(ShapeSyncFigureMeshMerger.TryMerge(source, new[] { external.AddComponent<SkinnedMeshRenderer>() }, out GameObject rejectedRoot, out _, out string rejectedDiagnostic), Is.False);
                Assert.That(rejectedRoot, Is.Null);
                Assert.That(rejectedDiagnostic, Does.Contain("below the Humanoid root"));
                SkinnedMeshRenderer[] confirmedOrder = { renderer, secondRenderer };
                Assert.That(ShapeSyncFigureMeshMerger.TryMergeOwned(source, confirmedOrder, out ShapeSyncFigureMeshMerger.Result ownedResult, out string ownedDiagnostic), Is.True, ownedDiagnostic);
                confirmedOrder[0] = secondRenderer;
                Assert.That(ownedResult.ConfirmedSourceRendererOrder, Has.Count.EqualTo(2));
                Assert.That(ownedResult.ConfirmedSourceRendererOrder[0], Is.SameAs(renderer));
                Assert.That(ownedResult.ConfirmedSourceRendererOrder[1], Is.SameAs(secondRenderer));
                Assert.That(ownedResult.ConfirmedSourceRendererOrder as SkinnedMeshRenderer[], Is.Null);
                GameObject ownedRoot = ownedResult.Root;
                ownedResult.Dispose();
                Assert.That(ownedRoot == null, Is.True);
                Assert.That(ShapeSyncFigureMeshMerger.TryMergeOwned(source, new[] { renderer }, out ShapeSyncFigureMeshMerger.Result transferredResult, out string transferredDiagnostic), Is.True, transferredDiagnostic);
                GameObject transferredRoot = transferredResult.Root;
                Mesh transferredMesh = transferredResult.DetachMesh();
                transferredResult.Dispose();
                Assert.That(transferredRoot == null, Is.True);
                Assert.That(transferredMesh == null, Is.False);
                Object.DestroyImmediate(transferredMesh);
            }
            finally
            {
                if (mergedRoot != null)
                {
                    Mesh mesh = mergedRoot.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                    Object.DestroyImmediate(mergedRoot);
                    Object.DestroyImmediate(mesh);
                }
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(external);
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void MeshMerger_GeometryOnlyAcceptsPbmSourceWithoutMaterials()
        {
            GameObject source = CreateHumanoidSource("GeometryOnlyPbmSource", includeRenderer: true, out Avatar avatar);
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(renderer, source.transform.Find("Hips"));
                Material[] sourceMaterials = renderer.sharedMaterials;
                renderer.sharedMaterials = Array.Empty<Material>();
                foreach (Material material in sourceMaterials) Object.DestroyImmediate(material);

                Assert.That(ShapeSyncFigureMeshMerger.TryMergeOwnedGeometryOnly(source, new[] { renderer },
                    out ShapeSyncFigureMeshMerger.Result result, out string diagnostic), Is.True, diagnostic);
                try
                {
                    Assert.That(result.Renderer.sharedMesh, Is.Not.Null);
                    Assert.That(result.Renderer.sharedMesh.subMeshCount, Is.EqualTo(1));
                    Assert.That(result.Renderer.sharedMaterials, Has.Length.EqualTo(1));
                    Assert.That(result.Renderer.sharedMaterials[0], Is.Null);
                }
                finally { result.Dispose(); }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void MeshMerger_ResultDisposeDoesNotDestroyPersistentMesh()
        {
            const string meshPath = Root + "/PersistentMergeResult.asset";
            Mesh persistentMesh = new Mesh { name = "PersistentMergeResult" };
            AssetDatabase.CreateAsset(persistentMesh, meshPath);
            GameObject root = new GameObject("PersistentMergeResultRoot");
            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = persistentMesh;
            ShapeSyncFigureMeshMerger.Result result = new ShapeSyncFigureMeshMerger.Result(root, renderer, new[] { renderer });

            LogAssert.NoUnexpectedReceived();
            result.Dispose();
            LogAssert.NoUnexpectedReceived();

            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(meshPath), Is.SameAs(persistentMesh),
                "A merge result must not destroy a Mesh that has already been staged as a persistent Database asset.");
        }

        [Test]
        public void TryImport_MergesAndRecordsAdmittedSourceUnderDatabaseIntermediateWithoutChangingSource()
        {
            const string sourcePath = Root + "/ImportSource.prefab";
            const string databasePath = Root + "/ImportDatabase.prefab";
            GameObject source = CreateHumanoidSource("ImportSource", includeRenderer: true, out Avatar avatar);
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(renderer, source.transform.Find("Hips"));
                AssetDatabase.CreateAsset(avatar, Root + "/ImportAvatar.asset");
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/ImportSourceMesh.asset");
                // Deliberately collide with the merger's default Mesh name. The importer must
                // reserve the Figure-owned Mesh name and suffix this Material clone instead.
                AssetDatabase.CreateAsset(renderer.sharedMaterial, Root + "/MergedSkinnedMesh.mat");
                Texture2D sourceTexture = new Texture2D(2, 2) { name = "ImportSourceTexture" };
                sourceTexture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
                sourceTexture.Apply();
                AssetDatabase.CreateAsset(sourceTexture, Root + "/ImportSourceTexture.asset");
                renderer.sharedMaterial.mainTexture = sourceTexture;
                source.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);

                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "MasterFigure", out string importDiagnostic), Is.True, importDiagnostic);
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);

                GameObject database = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath);
                Transform intermediate = database.transform.Find("Intermediate");
                Assert.That(intermediate.childCount, Is.EqualTo(1));
                Assert.That(intermediate.GetChild(0).name, Is.EqualTo("MasterFigure"));
                ShapeSyncFigureImportRecord record = intermediate.GetChild(0).GetComponent<ShapeSyncFigureImportRecord>();
                Assert.That(record, Is.Not.Null);
                Assert.That(record.ConfirmedRendererOrder, Has.Count.EqualTo(1));
                SkinnedMeshRenderer importedRenderer = record.ConfirmedRendererOrder[0];
                Assert.That(importedRenderer, Is.Not.Null);
                Assert.That(importedRenderer.sharedMesh, Is.Not.Null);
                Assert.That(AssetDatabase.Contains(importedRenderer.sharedMesh), Is.True);
                Assert.That(AssetDatabase.GetAssetPath(importedRenderer.sharedMesh), Is.EqualTo(databasePath));
                Assert.That(importedRenderer.sharedMesh.name, Is.EqualTo("MasterFigure_MergedSkinnedMesh"));
                Material importedMaterial = importedRenderer.sharedMaterial;
                Assert.That(importedMaterial, Is.Not.SameAs(renderer.sharedMaterial));
                Assert.That(AssetDatabase.GetAssetPath(importedMaterial), Is.EqualTo(databasePath));
                Assert.That(importedMaterial.name, Is.EqualTo("MasterFigure_MergedSkinnedMesh_2"));
                Texture importedTexture = importedMaterial.mainTexture;
                Assert.That(importedTexture, Is.Not.SameAs(sourceTexture));
                Assert.That(AssetDatabase.GetAssetPath(importedTexture), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Where(asset => asset is Mesh || asset is Material || asset is Texture).Select(asset => asset.name).Distinct().Count(), Is.EqualTo(3));
                Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash));
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "MasterFigure", out string duplicateNameDiagnostic), Is.False);
                Assert.That(duplicateNameDiagnostic, Does.Contain("name already exists"));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TryImport_CopiesHumanoidAnimatorAndAvatarIntoDatabaseWithoutExternalAvatarDependency()
        {
            const string sourcePath = Root + "/LocalHumanoidSource.prefab";
            const string avatarPath = Root + "/LocalHumanoidSourceAvatar.asset";
            const string databasePath = Root + "/LocalHumanoidDatabase.prefab";
            GameObject source = CreateHumanoidSource("LocalHumanoidSource", includeRenderer: true, out Avatar sourceAvatar);
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(renderer, source.transform.Find("Hips"));
                AssetDatabase.CreateAsset(sourceAvatar, avatarPath);
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/LocalHumanoidSourceMesh.asset");
                AssetDatabase.CreateAsset(renderer.sharedMaterial, Root + "/LocalHumanoidSourceMaterial.mat");
                source.GetComponent<Animator>().avatar = sourceAvatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);

                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "MasterFigure", out string importDiagnostic), Is.True, importDiagnostic);
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);

                GameObject importedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath).transform.Find("Intermediate/MasterFigure").gameObject;
                Animator importedAnimator = importedRoot.GetComponent<Animator>();
                Assert.That(importedAnimator, Is.Not.Null);
                Assert.That(importedAnimator.avatar, Is.Not.Null);
                Assert.That(importedAnimator.avatar, Is.Not.SameAs(sourceAvatar));
                Assert.That(importedAnimator.avatar.isHuman, Is.True);
                Assert.That(importedAnimator.avatar.isValid, Is.True);
                Assert.That(importedAnimator.avatar.humanDescription.human.Length, Is.EqualTo(sourceAvatar.humanDescription.human.Length));
                Assert.That(AssetDatabase.GetAssetPath(importedAnimator.avatar), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Avatar>().Single(), Is.SameAs(importedAnimator.avatar));
                AssertRendererBonesAreDatabaseOwned(importedRoot, candidate.transform);

                string[] dependencies = AssetDatabase.GetDependencies(databasePath, true);
                Assert.That(dependencies, Does.Not.Contain(sourcePath));
                Assert.That(dependencies, Does.Not.Contain(avatarPath));

                const string duplicatePath = Root + "/LocalHumanoidDatabaseDuplicate.prefab";
                Assert.That(AssetDatabase.CopyAsset(databasePath, duplicatePath), Is.True);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(duplicatePath, out ShapeSyncDatabase duplicate, out string duplicateDiagnostic), Is.True, duplicateDiagnostic);
                Animator duplicateAnimator = duplicate.transform.Find("Intermediate/MasterFigure").GetComponent<Animator>();
                Assert.That(duplicateAnimator.avatar, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(duplicateAnimator.avatar), Is.EqualTo(duplicatePath));
                AssertRendererBonesAreDatabaseOwned(duplicate.transform.Find("Intermediate/MasterFigure").gameObject, candidate.transform);
                string[] duplicateDependencies = AssetDatabase.GetDependencies(duplicatePath, true);
                Assert.That(duplicateDependencies, Does.Not.Contain(sourcePath));
                Assert.That(duplicateDependencies, Does.Not.Contain(avatarPath));

                Assert.That(ShapeSyncFigureImport.TryRenameBaseFigure(databasePath, "MasterFigure", "RenamedFigure", out string renameDiagnostic), Is.True, renameDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase renamed, out string renamedOpenDiagnostic), Is.True, renamedOpenDiagnostic);
                Animator renamedAnimator = renamed.transform.Find("Intermediate/RenamedFigure").GetComponent<Animator>();
                Assert.That(renamed.transform.Find("Intermediate/MasterFigure"), Is.Null);
                Assert.That(renamedAnimator, Is.Not.Null);
                Assert.That(renamedAnimator.avatar, Is.Not.Null);
                Assert.That(renamedAnimator.avatar.isHuman && renamedAnimator.avatar.isValid, Is.True);
                Assert.That(AssetDatabase.GetAssetPath(renamedAnimator.avatar), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<Avatar>().Single(), Is.SameAs(renamedAnimator.avatar));
                AssertRendererBonesAreDatabaseOwned(renamedAnimator.gameObject, candidate.transform);

            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TryImport_RejectsMissingAdmissionAndInvalidDatabaseBeforeChangingSource()
        {
            Assert.That(ShapeSyncFigureImport.TryImport(Root + "/Missing.prefab", null, out string admissionDiagnostic), Is.False);
            Assert.That(admissionDiagnostic, Does.Contain("successful admission"));

            GameObject source = CreateHumanoidSource("RejectedImport", includeRenderer: true, out Avatar avatar);
            try
            {
                AssetDatabase.CreateAsset(avatar, Root + "/RejectedImportAvatar.asset");
                source.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, Root + "/RejectedImport.prefab"), Is.Not.Null);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/RejectedImport.prefab");
                Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(Root + "/RejectedImport.prefab");
                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(Root + "/Missing.prefab", admission, out string databaseDiagnostic), Is.False);
                Assert.That(databaseDiagnostic, Does.Contain("Prefab root"));
                Assert.That(AssetDatabase.GetAssetDependencyHash(Root + "/RejectedImport.prefab"), Is.EqualTo(sourceHash));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void TryImport_ClonesSharedAndMultipleMaterialTexturePropertiesAndRollsBackAfterMultipleStages()
        {
            const string sourcePath = Root + "/MaterialTextureSource.prefab";
            const string databasePath = Root + "/MaterialTextureDatabase.prefab";
            GameObject source = CreateHumanoidSource("MaterialTextureSource", includeRenderer: true, out Avatar avatar);
            System.Action<Object, string> originalAdd = ShapeSyncDatabaseTransaction.AddObjectToAsset;
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                Assert.That(shader, Is.Not.Null, "URP/Lit shader is required for multi-texture property coverage.");
                SkinnedMeshRenderer body = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                GameObject faceObject = new GameObject("Face"); faceObject.transform.SetParent(source.transform, false);
                SkinnedMeshRenderer face = faceObject.AddComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(body, source.transform.Find("Hips")); ConfigureMergeRenderer(face, source.transform.Find("Hips"));
                Material bodyMaterial = new Material(shader) { name = "BodyMaterial" };
                Material faceMaterial = new Material(shader) { name = "FaceMaterial" };
                const string secondaryProperty = "_BumpMap";
                Assert.That(bodyMaterial.GetTexturePropertyNames(), Does.Contain(secondaryProperty));
                Texture2D shared = MakeTexture("Shared"); Texture2D bodySecondary = MakeTexture("BodySecondary"); Texture2D faceSecondary = MakeTexture("FaceSecondary");
                const string mainTextureProperty = "_BaseMap";
                bodyMaterial.SetTexture(mainTextureProperty, shared); faceMaterial.SetTexture(mainTextureProperty, shared);
                bodyMaterial.SetTexture(secondaryProperty, bodySecondary); faceMaterial.SetTexture(secondaryProperty, faceSecondary);
                body.sharedMaterial = bodyMaterial; face.sharedMaterial = faceMaterial;
                AssetDatabase.CreateAsset(avatar, Root + "/MaterialTextureAvatar.asset");
                AssetDatabase.CreateAsset(body.sharedMesh, Root + "/MaterialTextureBodyMesh.asset"); AssetDatabase.CreateAsset(face.sharedMesh, Root + "/MaterialTextureFaceMesh.asset");
                AssetDatabase.CreateAsset(shared, Root + "/MaterialTextureShared.asset"); AssetDatabase.CreateAsset(bodySecondary, Root + "/MaterialTextureBodySecondary.asset"); AssetDatabase.CreateAsset(faceSecondary, Root + "/MaterialTextureFaceSecondary.asset");
                AssetDatabase.CreateAsset(bodyMaterial, Root + "/MaterialTextureBody.mat"); AssetDatabase.CreateAsset(faceMaterial, Root + "/MaterialTextureFace.mat");
                source.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "MasterFigure", out string importDiagnostic), Is.True, importDiagnostic);
                // SaveAsPrefabAsset updates the on-disk Database, but a Batch-mode
                // LoadAssetAtPath may still return its pre-save cache. Reload the
                // persisted asset before asserting its authored hierarchy.
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
                SkinnedMeshRenderer imported = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath).transform.Find("Intermediate/MasterFigure").GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(imported.sharedMaterials, Has.Length.EqualTo(2));
                Assert.That(imported.sharedMaterials.All(material => AssetDatabase.GetAssetPath(material) == databasePath), Is.True);
                Assert.That(imported.sharedMaterials.All(material => material.name.StartsWith("MasterFigure_")), Is.True);
                Assert.That(imported.sharedMaterials.Select(material => material.name).Distinct().Count(), Is.EqualTo(2));
                Assert.That(imported.sharedMaterials[0].GetTexture(mainTextureProperty), Is.Not.SameAs(shared));
                Assert.That(imported.sharedMaterials[0].GetTexture(mainTextureProperty), Is.SameAs(imported.sharedMaterials[1].GetTexture(mainTextureProperty)));
                Assert.That(imported.sharedMaterials[0].GetTexture(mainTextureProperty).name, Does.StartWith("MasterFigure_"));
                Assert.That(AssetDatabase.GetAssetPath(imported.sharedMaterials[0].GetTexture(secondaryProperty)), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.GetAssetPath(imported.sharedMaterials[1].GetTexture(secondaryProperty)), Is.EqualTo(databasePath));
                Assert.That(imported.sharedMaterials[0].GetTexture(secondaryProperty).name, Does.StartWith("MasterFigure_"));
                Assert.That(imported.sharedMaterials[1].GetTexture(secondaryProperty).name, Does.StartWith("MasterFigure_"));
                ShapeSyncDatabase importedDatabase = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath).GetComponent<ShapeSyncDatabase>();
                Assert.That(importedDatabase.Registry.TextureResources, Has.Count.EqualTo(3), "One shared main Texture and two distinct secondary Textures must produce three abstract Texture entities.");
                Assert.That(importedDatabase.Registry.TextureResources.Count(resource => resource.Texture == imported.sharedMaterials[0].GetTexture(mainTextureProperty)), Is.EqualTo(1), "A Texture shared by multiple Materials must remain one Database resource entity.");
                Assert.That(importedDatabase.Registry.TextureResources.All(resource => AssetDatabase.GetAssetPath(resource.Texture) == databasePath), Is.True);
                Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash));

                Hash128 databaseHash = AssetDatabase.GetAssetDependencyHash(databasePath);
                int addCount = 0; List<Object> staged = new List<Object>();
                ShapeSyncDatabaseTransaction.AddObjectToAsset = (asset, path) => { staged.Add(asset); addCount++; if (addCount == 3) throw new System.InvalidOperationException("Injected multi-stage failure"); originalAdd(asset, path); };
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "FailedFigure", out string failureDiagnostic), Is.False);
                Assert.That(failureDiagnostic, Does.Contain("Injected multi-stage failure"));
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(databaseHash));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(databasePath).transform.Find("Intermediate").childCount, Is.EqualTo(1));
                Assert.That(staged.Take(2).All(asset => asset == null), Is.True);
            }
            finally { ShapeSyncDatabaseTransaction.AddObjectToAsset = originalAdd; Object.DestroyImmediate(source); }
        }

        [Test]
        public void TryImport_MergesSplitInput_RejectsSecondBaseWithoutMutation()
        {
            const string sourcePath = Root + "/SplitImportSource.prefab";
            const string databasePath = Root + "/SplitImportDatabase.prefab";
            GameObject source = CreateHumanoidSource("SplitImportSource", includeRenderer: true, out Avatar avatar);
            System.Func<GameObject, string, bool> originalSavePrefabAsset = ShapeSyncDatabaseTransaction.SavePrefabAsset;
            System.Action<Object, string> originalAddObjectToAsset = ShapeSyncDatabaseTransaction.AddObjectToAsset;
            System.Action<GameObject> originalUnloadPrefabContents = ShapeSyncDatabaseTransaction.UnloadPrefabContents;
            try
            {
                SkinnedMeshRenderer first = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(first, source.transform.Find("Hips"));
                GameObject secondObject = new GameObject("Face"); secondObject.transform.SetParent(source.transform, false);
                SkinnedMeshRenderer second = secondObject.AddComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(second, source.transform.Find("Hips"));
                AssetDatabase.CreateAsset(avatar, Root + "/SplitImportAvatar.asset");
                AssetDatabase.CreateAsset(first.sharedMesh, Root + "/SplitImportBodyMesh.asset");
                AssetDatabase.CreateAsset(first.sharedMaterial, Root + "/SplitImportBodyMaterial.mat");
                AssetDatabase.CreateAsset(second.sharedMesh, Root + "/SplitImportFaceMesh.asset");
                AssetDatabase.CreateAsset(second.sharedMaterial, Root + "/SplitImportFaceMaterial.mat");
                source.GetComponent<Animator>().avatar = avatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);
                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Hash128 sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "FirstFigure", out string initialImportDiagnostic), Is.True, initialImportDiagnostic);
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
                ShapeSyncFigureImportRecord firstRecord = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath).transform.Find("Intermediate").GetChild(0).GetComponent<ShapeSyncFigureImportRecord>();
                Assert.That(firstRecord.ConfirmedRendererOrder[0].sharedMesh.subMeshCount, Is.EqualTo(2));
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(databasePath).Registry.BaseFigures, Has.Count.EqualTo(1));
                Hash128 databaseHash = AssetDatabase.GetAssetDependencyHash(databasePath);

                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "SecondFigure", out string importDiagnostic), Is.False);
                Assert.That(importDiagnostic, Does.Contain("EntityCardinality"));
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);

                GameObject database = AssetDatabase.LoadAssetAtPath<GameObject>(databasePath);
                Assert.That(database.transform.Find("Intermediate").childCount, Is.EqualTo(1));
                Assert.That(database.GetComponent<ShapeSyncDatabase>().Registry.BaseFigures, Has.Count.EqualTo(1));
                int meshCount = 0;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(databasePath))
                {
                    if (asset is Mesh) meshCount++;
                }
                Assert.That(meshCount, Is.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(databaseHash));
                Assert.That(AssetDatabase.GetAssetDependencyHash(sourcePath), Is.EqualTo(sourceHash));

                const string saveFailureDatabasePath = Root + "/SaveFailureDatabase.prefab";
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(saveFailureDatabasePath, out _, out string saveFailureCreateDiagnostic), Is.True, saveFailureCreateDiagnostic);
                Hash128 saveFailureHash = AssetDatabase.GetAssetDependencyHash(saveFailureDatabasePath);
                ShapeSyncDatabaseTransaction.SavePrefabAsset = (_, _) => false;
                Assert.That(ShapeSyncFigureImport.TryImport(saveFailureDatabasePath, admission, "SaveFailureFigure", out string saveFailureDiagnostic), Is.False);
                Assert.That(saveFailureDiagnostic, Does.Contain("could not be saved"));
                ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefabAsset;
                AssetDatabase.ImportAsset(saveFailureDatabasePath, ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(saveFailureDatabasePath).transform.Find("Intermediate").childCount, Is.EqualTo(0));
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(saveFailureDatabasePath).Registry.BaseFigures, Is.Empty);
                Assert.That(AssetDatabase.GetAssetDependencyHash(saveFailureDatabasePath), Is.EqualTo(saveFailureHash));

                const string subAssetFailureDatabasePath = Root + "/SubAssetFailureDatabase.prefab";
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(subAssetFailureDatabasePath, out _, out string subAssetFailureCreateDiagnostic), Is.True, subAssetFailureCreateDiagnostic);
                Hash128 subAssetFailureHash = AssetDatabase.GetAssetDependencyHash(subAssetFailureDatabasePath);
                ShapeSyncDatabaseTransaction.AddObjectToAsset = (_, _) => throw new System.InvalidOperationException("Injected sub-asset failure");
                Assert.That(ShapeSyncFigureImport.TryImport(subAssetFailureDatabasePath, admission, "SubAssetFailureFigure", out string subAssetDiagnostic), Is.False);
                Assert.That(subAssetDiagnostic, Does.Contain("Injected sub-asset failure"));
                ShapeSyncDatabaseTransaction.AddObjectToAsset = originalAddObjectToAsset;
                AssetDatabase.ImportAsset(subAssetFailureDatabasePath, ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(subAssetFailureDatabasePath).transform.Find("Intermediate").childCount, Is.EqualTo(0));
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(subAssetFailureDatabasePath).Registry.BaseFigures, Is.Empty);
                Assert.That(AssetDatabase.GetAssetDependencyHash(subAssetFailureDatabasePath), Is.EqualTo(subAssetFailureHash));

                const string avatarFailureDatabasePath = Root + "/AvatarFailureDatabase.prefab";
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(avatarFailureDatabasePath, out _, out string avatarFailureCreateDiagnostic), Is.True, avatarFailureCreateDiagnostic);
                Hash128 avatarFailureHash = AssetDatabase.GetAssetDependencyHash(avatarFailureDatabasePath);
                ShapeSyncDatabaseTransaction.AddObjectToAsset = (asset, path) =>
                {
                    if (asset is Avatar) throw new System.InvalidOperationException("Injected Avatar sub-asset failure");
                    originalAddObjectToAsset(asset, path);
                };
                Assert.That(ShapeSyncFigureImport.TryImport(avatarFailureDatabasePath, admission, "AvatarFailureFigure", out string avatarFailureDiagnostic), Is.False);
                Assert.That(avatarFailureDiagnostic, Does.Contain("Injected Avatar sub-asset failure"));
                ShapeSyncDatabaseTransaction.AddObjectToAsset = originalAddObjectToAsset;
                AssetDatabase.ImportAsset(avatarFailureDatabasePath, ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(avatarFailureDatabasePath).transform.Find("Intermediate").childCount, Is.EqualTo(0));
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(avatarFailureDatabasePath).Registry.BaseFigures, Is.Empty);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(avatarFailureDatabasePath).OfType<Avatar>().Count(), Is.EqualTo(0));
                Assert.That(AssetDatabase.GetAssetDependencyHash(avatarFailureDatabasePath), Is.EqualTo(avatarFailureHash));

                const string cleanupFailureDatabasePath = Root + "/CleanupFailureDatabase.prefab";
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(cleanupFailureDatabasePath, out _, out string cleanupFailureCreateDiagnostic), Is.True, cleanupFailureCreateDiagnostic);
                Mesh cleanupFailureMesh = null;
                ShapeSyncDatabaseTransaction.AddObjectToAsset = (asset, path) =>
                {
                    cleanupFailureMesh = asset as Mesh;
                    originalAddObjectToAsset(asset, path);
                };
                ShapeSyncDatabaseTransaction.UnloadPrefabContents = _ => throw new System.InvalidOperationException("Injected unload failure");
                Assert.That(ShapeSyncFigureImport.TryImport(cleanupFailureDatabasePath, admission, "CleanupFailureFigure", out string cleanupDiagnostic), Is.False);
                Assert.That(cleanupDiagnostic, Does.Contain("cleanup failed"));
                Assert.That(cleanupFailureMesh == null, Is.True);

            }
            finally
            {
                ShapeSyncDatabaseTransaction.SavePrefabAsset = originalSavePrefabAsset;
                ShapeSyncDatabaseTransaction.AddObjectToAsset = originalAddObjectToAsset;
                ShapeSyncDatabaseTransaction.UnloadPrefabContents = originalUnloadPrefabContents;
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MeshMergerWindow_CleanupUnretainedMergedOutputReleasesOnlyUncommittedMeshes()
        {
            Mesh unsaved = new Mesh { name = "UnsavedMergedMesh" };
            SkinnedMeshMergerWindow.CleanupUnretainedMergedOutput(unsaved, null, false, false);
            Assert.That(unsaved == null, Is.True);

            const string retainedPath = Root + "/RetainedMergedMesh.asset";
            Mesh retained = new Mesh { name = "RetainedMergedMesh" };
            AssetDatabase.CreateAsset(retained, retainedPath);
            SkinnedMeshMergerWindow.CleanupUnretainedMergedOutput(retained, retainedPath, true, true);
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(retainedPath), Is.SameAs(retained));

            const string discardedPath = Root + "/DiscardedMergedMesh.asset";
            Mesh discarded = new Mesh { name = "DiscardedMergedMesh" };
            AssetDatabase.CreateAsset(discarded, discardedPath);
            SkinnedMeshMergerWindow.CleanupUnretainedMergedOutput(discarded, discardedPath, true, false);
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(discardedPath), Is.Null);
        }

        [Test]
        public void MeshMerger_CleansAllocatedMeshWhenBuildThrowsBeforeOutputOwnershipTransfers()
        {
            GameObject source = CreateHumanoidSource("ThrowingMergeSource", includeRenderer: true, out Avatar avatar);
            Mesh allocated = null;
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(renderer, source.transform.Find("Hips"));
                Assert.That(ShapeSyncFigureMeshMerger.TryMergeOwnedForTests(source, new[] { renderer }, mesh => { allocated = mesh; throw new System.InvalidOperationException("Injected build failure"); }, out ShapeSyncFigureMeshMerger.Result result, out string diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic, Does.Contain("Injected build failure"));
                Assert.That(allocated == null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void GenerateSnapshot_RejectsDatabaseWithoutBaseFigureWithoutMutation()
        {
            const string databasePath = Root + "/GenerateSnapshotEmpty.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out ShapeSyncDatabase database, out string createDiagnostic), Is.True, createDiagnostic);
            Hash128 before = AssetDatabase.GetAssetDependencyHash(databasePath);

            Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.domain, Is.EqualTo("figure-generate"));
            Assert.That(diagnostic.domainCode, Is.EqualTo("BaseFigureRequired"));
            Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(before));
        }

        [Test]
        public void GenerateOutputPaths_AcceptsDistinctRelativeFoldersAndRejectsEscapes()
        {
            Assert.That(ShapeSyncFigureGenerateOutputPaths.TryCreate(Root, "Registries/", "Bindings/", "Materials/", "Textures/", out ShapeSyncFigureGenerateOutputPaths paths, out StackMachineDiagnostic diagnostic), Is.True, diagnostic == null ? null : diagnostic.message);
            Assert.That(paths.RegistriesPath, Is.EqualTo(Root + "/Registries"));
            Assert.That(paths.BindingsPath, Is.EqualTo(Root + "/Bindings"));
            Assert.That(paths.MaterialsPath, Is.EqualTo(Root + "/Materials"));
            Assert.That(paths.TexturesPath, Is.EqualTo(Root + "/Textures"));

            Assert.That(ShapeSyncFigureGenerateOutputPaths.TryCreate(Root, "../Registries", "Bindings", "Materials", "Textures", out _, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GenerateOutputPathInvalid"));
            Assert.That(ShapeSyncFigureGenerateOutputPaths.TryCreate(Root, "Shared", "Shared", "Materials", "Textures", out _, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GenerateOutputPathDuplicate"));
            Assert.That(ShapeSyncFigureGenerateOutputPaths.TryCreate(Root, ".", "Bindings", "Materials", "Textures", out _, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GenerateOutputPathInvalid"));
            Assert.That(ShapeSyncFigureGenerateOutputPaths.TryCreate(Root, "Registries//Current", "Bindings", "Materials", "Textures", out _, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GenerateOutputPathInvalid"));
        }

        [Test]
        public void GenerateSnapshot_ResolvesDatabaseOwnedHumanoidBaseWithoutSourceDependency()
        {
            const string sourcePath = Root + "/GenerateSnapshotSource.prefab";
            const string avatarPath = Root + "/GenerateSnapshotSourceAvatar.asset";
            const string databasePath = Root + "/GenerateSnapshotDatabase.prefab";
            GameObject source = CreateHumanoidSource("GenerateSnapshotSource", includeRenderer: true, out Avatar sourceAvatar);
            try
            {
                SkinnedMeshRenderer renderer = source.transform.Find("Body").GetComponent<SkinnedMeshRenderer>();
                ConfigureMergeRenderer(renderer, source.transform.Find("Hips"));
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Texture2D sourceMainTexture = MakeTexture("GenerateSnapshotMain");
                renderer.sharedMaterial.SetTexture("_BaseMap", sourceMainTexture);
                AssetDatabase.CreateAsset(sourceAvatar, avatarPath);
                AssetDatabase.CreateAsset(renderer.sharedMesh, Root + "/GenerateSnapshotSourceMesh.asset");
                AssetDatabase.CreateAsset(sourceMainTexture, Root + "/GenerateSnapshotSourceMain.asset");
                AssetDatabase.CreateAsset(renderer.sharedMaterial, Root + "/GenerateSnapshotSourceMaterial.mat");
                source.GetComponent<Animator>().avatar = sourceAvatar;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(source, sourcePath), Is.Not.Null);
                Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(databasePath, out _, out string createDiagnostic), Is.True, createDiagnostic);

                GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                Assert.That(ShapeSyncFigureImport.TryAdmit(candidate, out ShapeSyncFigureImportAdmission admission, out string admitDiagnostic), Is.True, admitDiagnostic);
                Assert.That(ShapeSyncFigureImport.TryImport(databasePath, admission, "MasterFigure", out string importDiagnostic), Is.True, importDiagnostic);
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);

                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic incompleteDiagnostic), Is.False);
                Assert.That(incompleteDiagnostic.domainCode, Is.EqualTo("MaterialEntriesRequired"));
                SkinnedMeshRenderer databaseRenderer = database.Registry.BaseFigures.Single().Figure.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(ShapeSyncMaterialAdapterResolver.TryAdmit(database, "Body", databaseRenderer, 0, databaseRenderer.sharedMaterial,
                    out ShapeSyncMaterialAdapterResolver.Admission materialAdmission, out string materialAdmissionDiagnostic), Is.True, materialAdmissionDiagnostic);
                try { Assert.That(ShapeSyncMaterialEntryImport.TrySave(databasePath, new[] { materialAdmission }, out string materialSaveDiagnostic), Is.True, materialSaveDiagnostic); }
                finally { materialAdmission.Dispose(); }
                Assert.That(ShapeSyncDatabaseAsset.TryOpen(databasePath, out database, out openDiagnostic), Is.True, openDiagnostic);
                Assert.That(database.Registry.TrySetFigureNormalEntries(new[] { "Body" }, out Texture[] removedNormalTextures, out string normalDeclarationDiagnostic), Is.True, normalDeclarationDiagnostic);
                Assert.That(removedNormalTextures, Is.Empty);
                SerializedObject baseOnlyRegistry = new SerializedObject(database.Registry);
                SerializedProperty baseOnlyKeepNames = baseOnlyRegistry.FindProperty("keptRawBlendShapeNames");
                baseOnlyKeepNames.arraySize = 1;
                baseOnlyKeepNames.GetArrayElementAtIndex(0).stringValue = "Expression";
                baseOnlyRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic baseOnlyKeepDiagnostic), Is.False);
                Assert.That(baseOnlyKeepDiagnostic.domainCode, Is.EqualTo("FigureMorphAuthoringInvalid"));
                Assert.That(database.Registry.KeptRawBlendShapeNames, Is.EqualTo(new[] { "Expression" }));
                baseOnlyRegistry.Update();
                baseOnlyKeepNames = baseOnlyRegistry.FindProperty("keptRawBlendShapeNames");
                baseOnlyKeepNames.arraySize = 0;
                baseOnlyRegistry.ApplyModifiedPropertiesWithoutUndo();
                Hash128 beforeSnapshot = AssetDatabase.GetAssetDependencyHash(databasePath);
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out ShapeSyncFigureGenerateSnapshot snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic == null ? null : diagnostic.message);
                Assert.That(snapshot.DatabasePath, Is.EqualTo(databasePath));
                Assert.That(snapshot.BaseFigure.Name, Is.EqualTo("MasterFigure"));
                Assert.That(snapshot.BaseAnimator.avatar, Is.SameAs(snapshot.BaseAvatar));
                Assert.That(snapshot.BaseAvatar.isHuman && snapshot.BaseAvatar.isValid, Is.True);
                Assert.That(snapshot.FigureNormalEntries.Select(entry => entry.MaterialEntryName), Is.EqualTo(new[] { "Body" }));
                Assert.That(snapshot.NormalEntries, Is.Empty, "A declared Normal Entry without a selected Texture remains a valid Generate input.");
                Assert.That(snapshot.MaterialEntries.Single().TextureResourceNames, Is.EqualTo(snapshot.TextureResources.Select(resource => resource.LogicalName)));
                Assert.That(AssetDatabase.GetAssetPath(snapshot.BaseAvatar), Is.EqualTo(databasePath));
                Assert.That(AssetDatabase.GetDependencies(databasePath, true), Does.Not.Contain(sourcePath));
                Assert.That(AssetDatabase.GetDependencies(databasePath, true), Does.Not.Contain(avatarPath));
                Assert.That(AssetDatabase.GetAssetDependencyHash(databasePath), Is.EqualTo(beforeSnapshot));

                FieldInfo baseFigureReference = typeof(ShapeSyncDatabaseRegistry.BaseFigureEntry).GetField("figure", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo materialRendererReference = typeof(ShapeSyncDatabaseRegistry.MaterialEntry).GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(baseFigureReference, Is.Not.Null);
                Assert.That(materialRendererReference, Is.Not.Null);
                ShapeSyncDatabaseRegistry.BaseFigureEntry persistedBaseEntry = database.Registry.BaseFigures.Single();
                ShapeSyncDatabaseRegistry.MaterialEntry persistedMaterialEntry = database.Registry.MaterialEntries.Single();
                GameObject originalRegistryFigure = persistedBaseEntry.Figure;
                SkinnedMeshRenderer originalRegistryRenderer = persistedMaterialEntry.Renderer;
                baseFigureReference.SetValue(persistedBaseEntry, null);
                materialRendererReference.SetValue(persistedMaterialEntry, null);
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out ShapeSyncFigureGenerateSnapshot staleReferenceSnapshot, out StackMachineDiagnostic staleReferenceDiagnostic), Is.True, staleReferenceDiagnostic == null ? null : staleReferenceDiagnostic.message);
                Assert.That(staleReferenceSnapshot.BaseFigure.GameObject, Is.Not.Null);
                Assert.That(persistedBaseEntry.Figure, Is.Null, "Generate snapshot must not repair a stale Base Figure registry reference.");
                Assert.That(persistedMaterialEntry.Renderer, Is.Null, "Generate snapshot must not repair a stale Material Entry renderer reference.");
                baseFigureReference.SetValue(persistedBaseEntry, originalRegistryFigure);
                materialRendererReference.SetValue(persistedMaterialEntry, originalRegistryRenderer);
                FieldInfo baseFigureName = typeof(ShapeSyncDatabaseRegistry.BaseFigureEntry).GetField("name", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(baseFigureName, Is.Not.Null);
                baseFigureName.SetValue(persistedBaseEntry, "MissingFigure");
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic unresolvedReferenceDiagnostic), Is.False);
                Assert.That(unresolvedReferenceDiagnostic.domainCode, Is.EqualTo("BaseFigureInvalid"));
                Assert.That(persistedBaseEntry.Figure, Is.SameAs(originalRegistryFigure), "Reject must not repair or alter the Registry.");
                baseFigureName.SetValue(persistedBaseEntry, "MasterFigure");
                Assert.That(database.Registry.TrySetFigureNormalEntries(System.Array.Empty<string>(), out removedNormalTextures, out normalDeclarationDiagnostic), Is.True, normalDeclarationDiagnostic);
                Assert.That(snapshot.FigureNormalEntries.Select(entry => entry.MaterialEntryName), Is.EqualTo(new[] { "Body" }));

                foreach (string collectionName in new[] { "baseFigures", "materialEntries", "textureResources", "figureNormalEntries", "normalEntries", "figureAxes", "keptRawBlendShapeNames" })
                {
                    FieldInfo collectionField = typeof(ShapeSyncDatabaseRegistry).GetField(collectionName, BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(collectionField, Is.Not.Null, collectionName);
                    object savedCollection = collectionField.GetValue(database.Registry);
                    collectionField.SetValue(database.Registry, null);
                    Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic nullCollectionDiagnostic), Is.False);
                    Assert.That(nullCollectionDiagnostic.domainCode, Is.EqualTo("DatabaseRegistryCollectionsInvalid"));
                    collectionField.SetValue(database.Registry, savedCollection);
                }

                ShapeSyncDatabaseRegistry.MaterialEntry savedEntry = database.Registry.MaterialEntries.Single();
                Material savedMaterial = savedEntry.Material;
                MaterialShaderAdapter savedAdapter = savedEntry.Adapter;
                string mainResourceName = database.Registry.TextureResources.Single().LogicalName;
                Texture savedMainTexture = database.Registry.TextureResources.Single().Texture;
                Material externalMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/GenerateSnapshotSourceMaterial.mat");
                MaterialShaderAdapter externalAdapter = Object.Instantiate(savedAdapter);
                AssetDatabase.CreateAsset(externalAdapter, Root + "/GenerateSnapshotExternalAdapter.asset");
                SerializedObject serializedRegistry = new SerializedObject(database.Registry);
                SerializedProperty serializedEntry = serializedRegistry.FindProperty("materialEntries").GetArrayElementAtIndex(0);
                serializedEntry.FindPropertyRelative("logicalName").stringValue = "Mutated";
                serializedEntry.FindPropertyRelative("textureResourceNames").arraySize = 1;
                serializedEntry.FindPropertyRelative("textureResourceNames").GetArrayElementAtIndex(0).stringValue = "Unknown";
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(snapshot.MaterialEntries.Single().LogicalName, Is.EqualTo("Body"));
                Assert.That(snapshot.MaterialEntries.Single().TextureResourceNames, Is.EqualTo(new[] { mainResourceName }));
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic missingResourceDiagnostic), Is.False);
                Assert.That(missingResourceDiagnostic.domainCode, Is.EqualTo("MaterialTextureResourceMissing"));
                serializedRegistry.Update();
                serializedEntry = serializedRegistry.FindProperty("materialEntries").GetArrayElementAtIndex(0);
                serializedEntry.FindPropertyRelative("logicalName").stringValue = "Body";
                serializedEntry.FindPropertyRelative("textureResourceNames").arraySize = 0;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic emptyResourceListDiagnostic), Is.False);
                Assert.That(emptyResourceListDiagnostic.domainCode, Is.EqualTo("MaterialTextureResourceMismatch"));
                serializedRegistry.Update();
                serializedEntry = serializedRegistry.FindProperty("materialEntries").GetArrayElementAtIndex(0);
                serializedEntry.FindPropertyRelative("textureResourceNames").arraySize = 1;
                serializedEntry.FindPropertyRelative("textureResourceNames").GetArrayElementAtIndex(0).stringValue = mainResourceName;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(database.Registry.TrySetFigureNormalEntries(new[] { "Body" }, out removedNormalTextures, out normalDeclarationDiagnostic), Is.True, normalDeclarationDiagnostic);
                serializedRegistry.Update();
                SerializedProperty materialEntriesProperty = serializedRegistry.FindProperty("materialEntries");
                materialEntriesProperty.InsertArrayElementAtIndex(1);
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic duplicateEntryDiagnostic), Is.False);
                Assert.That(duplicateEntryDiagnostic.domainCode, Is.EqualTo("MaterialEntryInvalid"));
                serializedRegistry.Update();
                serializedRegistry.FindProperty("materialEntries").DeleteArrayElementAtIndex(1);
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                serializedRegistry.Update();
                serializedEntry = serializedRegistry.FindProperty("materialEntries").GetArrayElementAtIndex(0);
                serializedEntry.FindPropertyRelative("material").objectReferenceValue = externalMaterial;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic externalMaterialDiagnostic), Is.False);
                Assert.That(externalMaterialDiagnostic.domainCode, Is.EqualTo("MaterialEntryNotDatabaseOwned"));
                serializedRegistry.Update();
                serializedEntry = serializedRegistry.FindProperty("materialEntries").GetArrayElementAtIndex(0);
                serializedEntry.FindPropertyRelative("material").objectReferenceValue = savedMaterial;
                serializedEntry.FindPropertyRelative("adapter").objectReferenceValue = externalAdapter;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic externalAdapterDiagnostic), Is.False);
                Assert.That(externalAdapterDiagnostic.domainCode, Is.EqualTo("MaterialEntryNotDatabaseOwned"));
                serializedRegistry.Update();
                serializedEntry = serializedRegistry.FindProperty("materialEntries").GetArrayElementAtIndex(0);
                serializedEntry.FindPropertyRelative("adapter").objectReferenceValue = savedAdapter;
                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
                Texture2D externalPropertyTexture = MakeTexture("GenerateSnapshotExternalProperty");
                AssetDatabase.CreateAsset(externalPropertyTexture, Root + "/GenerateSnapshotExternalProperty.asset");
                savedMaterial.SetTexture("_BaseMap", externalPropertyTexture);
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic externalPropertyDiagnostic), Is.False);
                Assert.That(externalPropertyDiagnostic.domainCode, Is.EqualTo("MaterialTextureNotDatabaseOwned"));
                savedMaterial.SetTexture("_BaseMap", savedMainTexture);
                databaseRenderer.sharedMesh = null;
                Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out _, out StackMachineDiagnostic missingMeshDiagnostic), Is.False);
                Assert.That(missingMeshDiagnostic.domainCode, Is.EqualTo("FigureMeshBindingInvalid"));
            }
            finally { Object.DestroyImmediate(source); }
        }

#if SHAPESYNC_RICH_TEST
        [Test]
        public void GenerateSnapshot_Spec20PlayTestDatabaseAcceptsFbmImportedTextureResourceSuffixes()
        {
            const string playTestDatabasePath = "Assets/zgock/ShapeSync/PlayTest/Spec20/ShapeSyncDatabase.prefab";
            ShapeSyncDatabase database = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(playTestDatabasePath);

            Assert.That(database, Is.Not.Null, "The Step 6 Human Test Database must be available to the Generate admission regression test.");
            Assert.That(ShapeSyncFigureGenerateSnapshot.TryCreate(database, out ShapeSyncFigureGenerateSnapshot snapshot, out StackMachineDiagnostic diagnostic), Is.True,
                diagnostic == null ? null : diagnostic.ToString());
            Assert.That(snapshot.MaterialEntries.Any(entry => entry.TextureResourceNames.Count > ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(entry.MaterialAsset).Count()), Is.True,
                "The Human Test Database must retain FBM Import All resource suffixes beyond Base Material property resources.");
        }
#endif

        private static void SeedOutfitPbmFollow(string databaseAssetPath, string pbmName, string outfitIdentity)
        {
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
            {
                if (!database.Registry.Outfits.Any(entry => entry != null && entry.Identity == outfitIdentity))
                    Assert.That(database.Registry.TryAddOutfit(outfitIdentity, outfitIdentity, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                GameObject baseFollow = CreatePbmFollowArtifact(intermediate, outfitIdentity + "_" + pbmName + "_Master", transaction);
                GameObject tallFollow = CreatePbmFollowArtifact(intermediate, outfitIdentity + "_" + pbmName + "_Tall", transaction);
                GameObject baseSource = CreatePbmFollowArtifact(intermediate, outfitIdentity + "_Master_Source", transaction);
                GameObject tallSource = CreatePbmFollowArtifact(intermediate, outfitIdentity + "_Tall_Source", transaction);
                Assert.That(database.Registry.TrySetOutfitPbmFollows(database, outfitIdentity, new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry(pbmName, new[]
                    {
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, baseSource, baseFollow),
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry("Tall", tallSource, tallFollow)
                    })
                }, out string followDiagnostic), Is.True, followDiagnostic);
            }, out string diagnostic), Is.True, diagnostic);
        }

        [Test]
        public void GenerateTextureClone_HandlesNonReadableImportedTextureWithoutChangingImporter()
        {
            const string pngPath = Root + "/GenerateNonReadable.png";
            Texture2D authored = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            authored.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            authored.Apply(false, false);
            try
            {
                File.WriteAllBytes(pngPath, authored.EncodeToPNG());
                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceSynchronousImport);
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                Assert.That(source, Is.Not.Null);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
                importer.isReadable = false;
                importer.SaveAndReimport();
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                Assert.That(source.isReadable, Is.False);

                Texture2D clone = (Texture2D)ShapeSyncEditorTextureUtility.Clone(source);
                try
                {
                    Assert.That(clone, Is.Not.Null);
                    Assert.That(clone, Is.Not.SameAs(source));
                    Assert.That(clone.isReadable, Is.True);
                    Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(pngPath));
                    Assert.That(((TextureImporter)AssetImporter.GetAtPath(pngPath)).isReadable, Is.False,
                        "Generate-side readback must not modify the source importer.");
                }
                finally { Object.DestroyImmediate(clone); }
            }
            finally
            {
                Object.DestroyImmediate(authored);
                AssetDatabase.DeleteAsset(pngPath);
            }
        }

        private static void SeedOutfitCollection(string databaseAssetPath, string outfitIdentity, GameObject source)
        {
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructure(databaseAssetPath, (database, _) =>
            {
                if (!database.Registry.Outfits.Any(entry => entry != null && entry.Identity == outfitIdentity))
                    Assert.That(database.Registry.TryAddOutfit(outfitIdentity, outfitIdentity, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
            }, out string setupDiagnostic), Is.True, setupDiagnostic);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out ShapeSyncDatabase database, out string openDiagnostic), Is.True, openDiagnostic);
            var sources = new List<ShapeSyncMeshOutfitCollectionAuthoring.Source>
            {
                new ShapeSyncMeshOutfitCollectionAuthoring.Source(ShapeSyncDatabaseRegistry.BaseShapeKey, source)
            };
            sources.AddRange(database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                .Select(axis => new ShapeSyncMeshOutfitCollectionAuthoring.Source(axis.Name, source)));
            Assert.That(ShapeSyncMeshOutfitCollectionAuthoring.TrySave(databaseAssetPath, outfitIdentity,
                ShapeSyncDatabaseRegistry.OutfitCollectionKind.Bone, false, sources, out string collectionDiagnostic), Is.True, collectionDiagnostic);
        }

        private static void AssertCollectionArtifactsPresent(ShapeSyncDatabase database, string outfitIdentity)
        {
            string databaseAssetPath = AssetDatabase.GetAssetPath(database);
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.Single(entry => entry.Identity == outfitIdentity);
            Assert.That(outfit.CollectionEntries, Is.Not.Empty);
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry entry in outfit.CollectionEntries)
            {
                Assert.That(entry.SourcePrefab, Is.Not.Null, "Collection rollback must restore every source Prefab.");
                Assert.That(entry.CollectionPrefab, Is.Not.Null, "Collection rollback must restore every output Prefab.");
                Assert.That(entry.SourcePrefab.transform.parent, Is.SameAs(database.transform.Find("Intermediate")));
                Assert.That(entry.CollectionPrefab.transform.parent, Is.SameAs(database.transform.Find("Intermediate")));
                foreach (GameObject prefab in new[] { entry.SourcePrefab, entry.CollectionPrefab })
                {
                    SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.That(renderers, Is.Not.Empty, "Collection rollback must restore every artifact renderer.");
                    foreach (SkinnedMeshRenderer renderer in renderers)
                    {
                        Assert.That(renderer.sharedMesh, Is.Not.Null, "Collection rollback must restore every artifact Mesh.");
                        Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh), Is.EqualTo(databaseAssetPath), "Collection rollback must restore Database-owned Meshes only.");
                    }
                }
            }
        }

        private static void AssertCollectionArtifactsAbsent(ShapeSyncDatabase database, string outfitIdentity)
        {
            Transform intermediate = database.transform.Find("Intermediate");
            Assert.That(intermediate.Cast<Transform>().Where(child => child.name.StartsWith(outfitIdentity + "_", StringComparison.Ordinal)
                && child.name.Contains("_Collection")).ToArray(), Is.Empty, "Collection cleanup must remove every source and output direct child.");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(database)).OfType<Mesh>()
                .Where(mesh => mesh.name.StartsWith(outfitIdentity + "_", StringComparison.Ordinal) && mesh.name.Contains("_Collection")).ToArray(), Is.Empty,
                "Collection cleanup must remove every Database-owned Collection Mesh.");
        }

        private static GameObject CreatePbmFollowArtifact(Transform intermediate, string name, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            GameObject artifact = new GameObject(name);
            artifact.transform.SetParent(intermediate, false);
            SkinnedMeshRenderer renderer = artifact.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = name + "_SkinnedMesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            transaction.AddSubAsset(mesh);
            renderer.sharedMesh = mesh;
            return artifact;
        }

        private static void ConfigureMergeRenderer(SkinnedMeshRenderer renderer, Transform bone)
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f } };
            mesh.bindposes = new[] { bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix };
            mesh.RecalculateNormals();
            mesh.AddBlendShapeFrame("Expression", 100f, new[] { Vector3.right, Vector3.right, Vector3.right }, new Vector3[3], new Vector3[3]);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.rootBone = bone;
            renderer.sharedMaterials = new[] { new Material(Shader.Find("Sprites/Default")) };
        }

        private static void AddRawBlendShape(Mesh mesh, string name)
        {
            Assert.That(mesh, Is.Not.Null);
            int vertexCount = mesh.vertexCount;
            mesh.AddBlendShapeFrame(name, 100f, new Vector3[vertexCount], new Vector3[vertexCount], new Vector3[vertexCount]);
        }

        private static void AssertBlendShapeDelta(Mesh mesh, string name, Vector3[] expectedVertices, Vector3[] expectedNormals, Vector3[] expectedTangents)
        {
            int index = mesh.GetBlendShapeIndex(name);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
            Assert.That(BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(mesh, index, 100f, out Vector3[] vertices, out Vector3[] normals, out Vector3[] tangents), Is.True, name);
            CollectionAssert.AreEqual(expectedVertices, vertices, name + " vertices");
            CollectionAssert.AreEqual(expectedNormals, normals, name + " normals");
            CollectionAssert.AreEqual(expectedTangents, tangents, name + " tangents");
        }

        private static void AssertGeneratedMainObjectName(string assetPath)
        {
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            Assert.That(mainAsset, Is.Not.Null, assetPath);
            Assert.That(mainAsset.name, Is.EqualTo(System.IO.Path.GetFileNameWithoutExtension(assetPath)),
                "Generated asset Main Object name must match its filename: " + assetPath);
        }

        private static ShapeSyncFigureGenerateSnapshot CreateSnapshotWithoutFbmAxes(ShapeSyncFigureGenerateSnapshot source)
        {
            return CreateSnapshotWith(source, source.Axes.Where(axis => axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).ToArray(), source.NormalEntries);
        }

        private static string[] FindDatabaseReferences(GameObject root, string databasePath)
        {
            var references = new List<string>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null
                        && AssetDatabase.GetAssetPath(property.objectReferenceValue) == databasePath)
                        references.Add(component.GetType().Name + "." + property.propertyPath);
            }
            return references.ToArray();
        }

        private static ShapeSyncFigureGenerateSnapshot CreateSnapshotWith(ShapeSyncFigureGenerateSnapshot source,
            IReadOnlyList<ShapeSyncFigureGenerateSnapshot.Axis> axes, IReadOnlyList<ShapeSyncFigureGenerateSnapshot.Normal> normalEntries,
            IReadOnlyList<ShapeSyncFigureGenerateSnapshot.FigureNormal> figureNormalEntries = null)
        {
            ConstructorInfo constructor = typeof(ShapeSyncFigureGenerateSnapshot).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(ShapeSyncFigureGenerateSnapshot.Figure), typeof(Animator), typeof(Avatar), typeof(IReadOnlyList<ShapeSyncFigureGenerateSnapshot.Axis>), typeof(IReadOnlyList<ShapeSyncFigureGenerateSnapshot.Material>), typeof(IReadOnlyList<ShapeSyncFigureGenerateSnapshot.TextureResource>), typeof(IReadOnlyList<ShapeSyncFigureGenerateSnapshot.FigureNormal>), typeof(IReadOnlyList<ShapeSyncFigureGenerateSnapshot.Normal>), typeof(int), typeof(IReadOnlyList<string>) }, null);
            Assert.That(constructor, Is.Not.Null);
            return (ShapeSyncFigureGenerateSnapshot)constructor.Invoke(new object[]
            {
                source.DatabasePath, source.BaseFigure, source.BaseAnimator, source.BaseAvatar,
                axes, source.MaterialEntries, source.TextureResources,
                figureNormalEntries ?? source.FigureNormalEntries, normalEntries, source.PcmSlots, source.KeptRawBlendShapeNames
            });
        }

        private static Texture2D MakeTexture(string name) { Texture2D texture = new Texture2D(2, 2) { name = name }; texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); texture.Apply(); return texture; }

        private static GameObject CreateHumanoidSource(string name, bool includeRenderer, out Avatar avatar)
        {
            GameObject root = new GameObject(name);
            Animator animator = root.AddComponent<Animator>();
            var bones = new List<Transform>();
            Transform hips = AddBone(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
            Transform spine = AddBone(hips, "Spine", Vector3.up * .15f, bones); Transform chest = AddBone(spine, "Chest", Vector3.up * .15f, bones); Transform neck = AddBone(chest, "Neck", Vector3.up * .15f, bones); AddBone(neck, "Head", Vector3.up * .12f, bones);
            Transform leftUpperArm = AddBone(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones); Transform leftLowerArm = AddBone(leftUpperArm, "LeftLowerArm", Vector3.left * .2f, bones); AddBone(leftLowerArm, "LeftHand", Vector3.left * .18f, bones);
            Transform rightUpperArm = AddBone(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones); Transform rightLowerArm = AddBone(rightUpperArm, "RightLowerArm", Vector3.right * .2f, bones); AddBone(rightLowerArm, "RightHand", Vector3.right * .18f, bones);
            Transform leftUpperLeg = AddBone(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones); Transform leftLowerLeg = AddBone(leftUpperLeg, "LeftLowerLeg", Vector3.down * .35f, bones); AddBone(leftLowerLeg, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
            Transform rightUpperLeg = AddBone(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones); Transform rightLowerLeg = AddBone(rightUpperLeg, "RightLowerLeg", Vector3.down * .35f, bones); AddBone(rightLowerLeg, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
            string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            var human = new HumanBone[names.Length]; for (int index = 0; index < names.Length; index++) human[index] = new HumanBone { boneName = names[index], humanName = names[index], limit = new HumanLimit { useDefaultValues = true } };
            var skeleton = new List<SkeletonBone> { ToSkeletonBone(root.transform) }; for (int index = 0; index < bones.Count; index++) skeleton.Add(ToSkeletonBone(bones[index]));
            avatar = AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
            animator.avatar = avatar;
            if (includeRenderer)
            {
                GameObject body = new GameObject("Body"); body.transform.SetParent(root.transform, false); body.AddComponent<SkinnedMeshRenderer>();
            }
            return root;
        }

        private static Transform AddBone(Transform parent, string name, Vector3 position, List<Transform> bones) { Transform bone = new GameObject(name).transform; bone.SetParent(parent, false); bone.localPosition = position; bones.Add(bone); return bone; }

        private static void AssertRendererBonesAreDatabaseOwned(GameObject figureRoot, Transform sourceRoot)
        {
            SkinnedMeshRenderer renderer = figureRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.rootBone, Is.Not.Null);
            Assert.That(IsInHierarchy(figureRoot.transform, renderer.rootBone), Is.True, "The saved rootBone must be owned by the Database Figure hierarchy.");
            Assert.That(IsInHierarchy(sourceRoot, renderer.rootBone), Is.False, "The saved rootBone must not retain the source hierarchy.");
            Assert.That(renderer.bones, Is.Not.Empty);
            foreach (Transform bone in renderer.bones)
            {
                Assert.That(bone, Is.Not.Null);
                Assert.That(IsInHierarchy(figureRoot.transform, bone), Is.True, "Every saved bone must be owned by the Database Figure hierarchy.");
                Assert.That(IsInHierarchy(sourceRoot, bone), Is.False, "No saved bone may retain the source hierarchy.");
            }
        }

        private static bool IsInHierarchy(Transform root, Transform target) => root == target || target.IsChildOf(root);
        private static SkeletonBone ToSkeletonBone(Transform transform) => new SkeletonBone { name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale };
    }
}
#endif
