// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor.Atlas;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasEditorCandidateTests
    {
        private const string AssetFolder = ShapeSyncTestAssetPaths.Spec18AtlasEditorCandidateRoot;

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.DeleteAsset(AssetFolder);
        }

        [Test]
        public void Collect_ReadsFigureAndDeclaredOutfitProxiesWithoutRecipeExecution()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            GameObject outfit = PersistentOwner("Outfit", "dress", "top", true);
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            Set(binding, "outfits", new List<MeshOutfitBindingEntry> { new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = outfit } });
            ShapeSyncDocumentAsset document = PersistentDocument(binding);
            document.MeshRecipe = new MeshRecipeDocument { wordSource = "$dress ATTACH" };

            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(snapshot.Entries.Count, Is.EqualTo(2));
            Assert.That(snapshot.Entries[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
            Assert.That(snapshot.Entries[1].MaterialId, Is.EqualTo(new MaterialId("dress", "top")));
            Assert.That(snapshot.FigureIdentity, Is.Not.Empty);
            Assert.That(snapshot.DocumentIdentity, Is.Not.Empty);
            Assert.That(snapshot.Entries[0].SourceMaterialIdentity, Is.Not.Empty);
            Assert.That(snapshot.FigureIdentity, Is.EqualTo(AtlasEditorIdentityTokenProvider.Create(figure)));
            Assert.That(snapshot.DocumentIdentity, Is.EqualTo(AtlasEditorIdentityTokenProvider.Create(document)));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_IncludesInactiveChildMaterialProxy()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body", false, true);
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            ShapeSyncDocumentAsset document = PersistentDocument(binding);

            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(snapshot.Entries.Count, Is.EqualTo(2));
            Assert.That(snapshot.Entries[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
            Assert.That(snapshot.Entries[1].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "inactive")));
            Assert.That(snapshot.Entries[1].SourceMaterialIdentity, Is.Not.Empty);
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_ExcludesProxyInsideUndeclaredOutfitBoundary()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            string figurePath = AssetDatabase.GetAssetPath(figure);
            GameObject editableFigure = PrefabUtility.LoadPrefabContents(figurePath);
            AddOutfitProxy(editableFigure, "Undeclared", "orphan");
            PrefabUtility.SaveAsPrefabAsset(editableFigure, figurePath);
            PrefabUtility.UnloadPrefabContents(editableFigure);
            figure = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            ShapeSyncDocumentAsset document = PersistentDocument(binding);

            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(snapshot.Entries, Has.Count.EqualTo(1));
            Assert.That(snapshot.Entries[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_ExcludesDeclaredOutfitNotAttachedByMeshRecipe()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            GameObject dress = PersistentOwner("Dress", "dress", "top", true);
            GameObject hair = PersistentOwner("Hair", "hair", "strand", true);
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            Set(binding, "outfits", new List<MeshOutfitBindingEntry>
            {
                new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = dress },
                new MeshOutfitBindingEntry { logicalName = "hair", outfitPrefab = hair },
            });
            ShapeSyncDocumentAsset document = PersistentDocument(binding);
            document.MeshRecipe = new MeshRecipeDocument { wordSource = "$dress ATTACH" };

            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(snapshot.Entries, Has.Count.EqualTo(2));
            Assert.That(snapshot.Entries[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
            Assert.That(snapshot.Entries[1].MaterialId, Is.EqualTo(new MaterialId("dress", "top")));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_RejectsMissingInputsAndInvalidOutfitRegistry()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            Assert.That(AtlasEditorCandidateCollector.TryCollect(null, null, out _, out StackMachineDiagnostic missingFigure), Is.False);
            Assert.That(missingFigure.domainCode, Is.EqualTo("AtlasEditorFigureRequired"));
            ShapeSyncDocumentAsset document = PersistentDocument(null);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingBinding), Is.False);
            Assert.That(missingBinding.domainCode, Is.EqualTo("AtlasEditorMeshBindingRequired"));
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>(); document.MeshBinding = binding;
            ShapeSyncDocument detachedDocument = new ShapeSyncDocument { MeshBinding = binding };
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, detachedDocument, out _, out StackMachineDiagnostic missingIdentity), Is.False);
            Assert.That(missingIdentity.domainCode, Is.EqualTo("AtlasEditorIdentityRequired"));
            Set(binding, "outfits", null);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingOutfitCollection), Is.False);
            Assert.That(missingOutfitCollection.domainCode, Is.EqualTo("AtlasEditorOutfitBindingInvalid"));
            Set(binding, "outfits", new List<MeshOutfitBindingEntry>());
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot figureOnly, out StackMachineDiagnostic emptyOutfits), Is.True, emptyOutfits?.message);
            Assert.That(figureOnly.Entries.Count, Is.EqualTo(1));
            Set(binding, "morphs", null);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingMorphCollection), Is.False);
            Assert.That(missingMorphCollection.domainCode, Is.EqualTo("AtlasEditorMorphBindingInvalid"));
            Set(binding, "morphs", new List<MeshMorphBindingEntry> { new MeshMorphBindingEntry { logicalName = "morph", targetName = "Target" } });
            GameObject duplicate = PersistentOwner("Duplicate", string.Empty, "body");
            Set(binding, "outfits", new List<MeshOutfitBindingEntry> { null });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic nullOutfitBinding), Is.False);
            Assert.That(nullOutfitBinding.domainCode, Is.EqualTo("AtlasEditorOutfitRequired"));
            Set(binding, "outfits", new List<MeshOutfitBindingEntry> { new MeshOutfitBindingEntry { logicalName = "x" } });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingOutfitPrefab), Is.False);
            Assert.That(missingOutfitPrefab.domainCode, Is.EqualTo("AtlasEditorOutfitRequired"));
            Set(binding, "outfits", new List<MeshOutfitBindingEntry> { new MeshOutfitBindingEntry { logicalName = "x", outfitPrefab = duplicate } });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic duplicateId), Is.False);
            Assert.That(duplicateId.domainCode, Is.EqualTo("AtlasEditorOutfitRegistryRequired"));
            GameObject dress = PersistentOwner("Dress", "dress", "top", true);
            GameObject hat = PersistentOwner("Hat", "hat", "hat", true);
            Set(binding, "outfits", new List<MeshOutfitBindingEntry>
            {
                new MeshOutfitBindingEntry { logicalName = string.Empty, outfitPrefab = dress },
            });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic blankLogical), Is.False);
            Assert.That(blankLogical.domainCode, Is.EqualTo("AtlasEditorOutfitBindingInvalid"));
            Set(binding, "outfits", new List<MeshOutfitBindingEntry>
            {
                new MeshOutfitBindingEntry { logicalName = "same", outfitPrefab = dress },
                new MeshOutfitBindingEntry { logicalName = "same", outfitPrefab = hat },
            });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic duplicateLogical), Is.False);
            Assert.That(duplicateLogical.domainCode, Is.EqualTo("AtlasEditorOutfitBindingInvalid"));
            Set(binding, "outfits", new List<MeshOutfitBindingEntry>
            {
                new MeshOutfitBindingEntry { logicalName = "morph", outfitPrefab = dress },
            });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic morphOutfitCollision), Is.False);
            Assert.That(morphOutfitCollision.domainCode, Is.EqualTo("AtlasEditorOutfitBindingInvalid"));
            Set(binding, "morphs", new List<MeshMorphBindingEntry> { new MeshMorphBindingEntry { logicalName = "morph", targetName = "Target" } });
            GameObject duplicateRegistry = PersistentOwner("DuplicateRegistry", "dress", "shoes", true);
            Set(binding, "outfits", new List<MeshOutfitBindingEntry>
            {
                new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = dress },
                new MeshOutfitBindingEntry { logicalName = "shoes", outfitPrefab = duplicateRegistry },
            });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic duplicateRegistryDiagnostic), Is.False);
            Assert.That(duplicateRegistryDiagnostic.domainCode, Is.EqualTo("AtlasEditorOutfitRegistryDuplicate"));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_RejectsInvalidAndDuplicateMorphBindings()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            ShapeSyncDocumentAsset document = PersistentDocument(binding);
            Set(binding, "morphs", new List<MeshMorphBindingEntry> { null });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic nullMorph), Is.False);
            Assert.That(nullMorph.domainCode, Is.EqualTo("AtlasEditorMorphBindingInvalid"));
            Set(binding, "morphs", new List<MeshMorphBindingEntry> { new MeshMorphBindingEntry { logicalName = string.Empty, targetName = "Target" } });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic blankMorphLogical), Is.False);
            Assert.That(blankMorphLogical.domainCode, Is.EqualTo("AtlasEditorMorphBindingInvalid"));
            Set(binding, "morphs", new List<MeshMorphBindingEntry> { new MeshMorphBindingEntry { logicalName = "morph", targetName = string.Empty } });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic blankMorphTarget), Is.False);
            Assert.That(blankMorphTarget.domainCode, Is.EqualTo("AtlasEditorMorphBindingInvalid"));
            Set(binding, "morphs", new List<MeshMorphBindingEntry>
            {
                new MeshMorphBindingEntry { logicalName = "morph", targetName = "Target" },
                new MeshMorphBindingEntry { logicalName = "morph", targetName = "Other" },
            });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic duplicateLogical), Is.False);
            Assert.That(duplicateLogical.domainCode, Is.EqualTo("AtlasEditorMorphBindingInvalid"));
            Set(binding, "morphs", new List<MeshMorphBindingEntry>
            {
                new MeshMorphBindingEntry { logicalName = "first", targetName = "Target" },
                new MeshMorphBindingEntry { logicalName = "second", targetName = "Target" },
            });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic duplicateTarget), Is.False);
            Assert.That(duplicateTarget.domainCode, Is.EqualTo("AtlasEditorMorphDuplicate"));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_RejectsBrokenProxyAndKeepsSuccessfulSnapshotDetached()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            ShapeSyncDocumentAsset document = PersistentDocument(binding);
            MaterialProxy proxy = figure.GetComponent<MaterialProxy>();
            MaterialProxyEntry entry = proxy.Entries[0];
            SkinnedMeshRenderer renderer = entry.renderer;
            entry.entryName = string.Empty;
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic blankEntry), Is.False);
            Assert.That(blankEntry.domainCode, Is.EqualTo("AtlasEditorMaterialBindingInvalid"));
            entry.entryName = "body";
            entry.renderer = null;
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingRenderer), Is.False);
            Assert.That(missingRenderer.domainCode, Is.EqualTo("AtlasEditorMaterialBindingInvalid"));
            entry.renderer = renderer;
            entry.materialChannel = -1;
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic negativeChannel), Is.False);
            Assert.That(negativeChannel.domainCode, Is.EqualTo("AtlasEditorSourceMaterialRequired"));
            entry.materialChannel = 4;
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic channel), Is.False);
            Assert.That(channel.domainCode, Is.EqualTo("AtlasEditorSourceMaterialRequired"));
            entry.materialChannel = 0;
            entry.renderer.sharedMaterial = null;
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingMaterial), Is.False);
            Assert.That(missingMaterial.domainCode, Is.EqualTo("AtlasEditorSourceMaterialRequired"));
            Material replacement = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Original" };
            AssetDatabase.CreateAsset(replacement, AssetPath("Original", ".mat"));
            replacement.name = "Original";
            entry.renderer.sharedMaterial = replacement;
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot snapshot, out StackMachineDiagnostic success), Is.True, success?.message);
            entry.renderer.sharedMaterial.name = "Changed";
            Assert.That(snapshot.Entries[0].SourceMaterialName, Is.EqualTo("Original"));
            IList<AtlasEditorCandidate> snapshotEntries = snapshot.Entries as IList<AtlasEditorCandidate>;
            Assert.That(snapshotEntries, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => snapshotEntries[0] = null);
            Assert.That(snapshot.Entries[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
            Set(proxy, "entries", new List<MaterialProxyEntry> { null });
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic broken), Is.False);
            Assert.That(broken.domainCode, Is.EqualTo("AtlasEditorMaterialBindingInvalid"));
            Set(proxy, "entries", null);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic missingEntries), Is.False);
            Assert.That(missingEntries.domainCode, Is.EqualTo("AtlasEditorMaterialBindingInvalid"));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_RejectsDuplicateMaterialId()
        {
            GameObject figure = PersistentOwner("Figure", string.Empty, "body");
            MaterialProxyEntry original = figure.GetComponent<MaterialProxy>().Entries[0];
            Set(figure.GetComponent<MaterialProxy>(), "entries", new List<MaterialProxyEntry>
            {
                original,
                new MaterialProxyEntry { entryName = "body", renderer = original.renderer, materialChannel = 0 },
            });
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            ShapeSyncDocumentAsset document = PersistentDocument(binding);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasEditorMaterialIdDuplicate"));
            Object.DestroyImmediate(binding);
        }

        [Test]
        public void Collect_RejectsTransientFigureDocumentAndSourceMaterialIdentities()
        {
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>();
            ShapeSyncDocumentAsset persistentDocument = PersistentDocument(binding);
            GameObject transientFigure = Owner("TransientFigure", string.Empty, "body");
            Assert.That(AtlasEditorIdentityTokenProvider.Create(transientFigure), Is.Empty);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(transientFigure, persistentDocument, out _, out StackMachineDiagnostic transientFigureDiagnostic), Is.False);
            Assert.That(transientFigureDiagnostic.domainCode, Is.EqualTo("AtlasEditorIdentityRequired"));

            GameObject persistentFigure = PersistentOwner("PersistentFigure", string.Empty, "body");
            ShapeSyncDocumentAsset transientDocument = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); transientDocument.MeshBinding = binding;
            Assert.That(AtlasEditorIdentityTokenProvider.Create(transientDocument), Is.Empty);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(persistentFigure, transientDocument, out _, out StackMachineDiagnostic transientDocumentDiagnostic), Is.False);
            Assert.That(transientDocumentDiagnostic.domainCode, Is.EqualTo("AtlasEditorIdentityRequired"));

            MaterialProxyEntry entry = persistentFigure.GetComponent<MaterialProxy>().Entries[0];
            entry.renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            Assert.That(AtlasEditorIdentityTokenProvider.Create(entry.renderer.sharedMaterial), Is.Empty);
            Assert.That(AtlasEditorCandidateCollector.TryCollect(persistentFigure, persistentDocument, out _, out StackMachineDiagnostic transientMaterialDiagnostic), Is.False);
            Assert.That(transientMaterialDiagnostic.domainCode, Is.EqualTo("AtlasEditorSourceMaterialIdentityRequired"));
            Object.DestroyImmediate(transientFigure); Object.DestroyImmediate(transientDocument); Object.DestroyImmediate(binding);
        }

        private static GameObject Owner(string name, string registry, string entryName)
        {
            GameObject root = new GameObject(name); GameObject child = new GameObject("Renderer"); child.transform.SetParent(root.transform);
            SkinnedMeshRenderer renderer = child.AddComponent<SkinnedMeshRenderer>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.name = name + "Material"; renderer.sharedMaterial = material;
            MaterialProxy proxy = root.AddComponent<MaterialProxy>();
            Set(proxy, "entries", new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = entryName, renderer = renderer, materialChannel = 0 } });
            return root;
        }

        private static GameObject PersistentOwner(string name, string registry, string entryName, bool addOutfit = false, bool addInactiveProxy = false)
        {
            EnsureAssetFolder();
            GameObject root = Owner(name, registry, entryName);
            if (addOutfit)
            {
                root.AddComponent<ShapeSyncOutfit>();
                Set(root.GetComponent<ShapeSyncOutfit>(), "registryId", registry);
            }
            if (addInactiveProxy) AddInactiveProxy(root, name);
            Material material = root.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            AssetDatabase.CreateAsset(material, AssetPath(name + "Material", ".mat"));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetPath(name, ".prefab"));
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void AddInactiveProxy(GameObject root, string name)
        {
            GameObject inactive = new GameObject("InactiveProxy"); inactive.transform.SetParent(root.transform);
            GameObject rendererObject = new GameObject("Renderer"); rendererObject.transform.SetParent(inactive.transform);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name + "InactiveMaterial" };
            AssetDatabase.CreateAsset(material, AssetPath(name + "InactiveMaterial", ".mat"));
            renderer.sharedMaterial = material;
            MaterialProxy proxy = inactive.AddComponent<MaterialProxy>();
            Set(proxy, "entries", new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "inactive", renderer = renderer, materialChannel = 0 } });
            inactive.SetActive(false);
        }

        private static void AddOutfitProxy(GameObject root, string name, string entryName)
        {
            EnsureAssetFolder();
            GameObject outfit = new GameObject(name); outfit.transform.SetParent(root.transform);
            outfit.AddComponent<ShapeSyncOutfit>();
            GameObject rendererObject = new GameObject("Renderer"); rendererObject.transform.SetParent(outfit.transform);
            SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name + "Material" };
            AssetDatabase.CreateAsset(material, AssetPath(name + "Material", ".mat"));
            renderer.sharedMaterial = material;
            MaterialProxy proxy = outfit.AddComponent<MaterialProxy>();
            Set(proxy, "entries", new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = entryName, renderer = renderer, materialChannel = 0 } });
        }

        private static ShapeSyncDocumentAsset PersistentDocument(MeshBinding binding)
        {
            EnsureAssetFolder();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            document.MeshBinding = binding;
            document.MeshRecipe = new MeshRecipeDocument { wordSource = "DETACH_ALL" };
            AssetDatabase.CreateAsset(document, AssetPath("Document", ".asset"));
            return document;
        }

        private static string AssetPath(string name, string extension) => AssetFolder + "/" + name + "_" + Guid.NewGuid().ToString("N") + extension;
        private static void EnsureAssetFolder() { if (!AssetDatabase.IsValidFolder(AssetFolder)) { ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec18AtlasEditorCandidateTests"); } }
        private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
