// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor.Atlas
{
    /// <summary>One detached Atlas Editor candidate derived without executing a Mesh or Material recipe.</summary>
    public sealed class AtlasEditorCandidate
    {
        internal AtlasEditorCandidate(string owner, MaterialId materialId, string sourceMaterialName, string sourceMaterialIdentity, MaterialProxyEntry validationBinding)
        { Owner = owner ?? string.Empty; MaterialId = materialId; SourceMaterialName = sourceMaterialName ?? string.Empty; SourceMaterialIdentity = sourceMaterialIdentity ?? string.Empty; ValidationBinding = validationBinding; }
        /// <summary>Gets the Figure owner or Outfit registry ID.</summary>
        public string Owner { get; }
        /// <summary>Gets the stable candidate key.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the read-only source Material name.</summary>
        public string SourceMaterialName { get; }
        /// <summary>Gets the canonical GlobalObjectId observed for the source Material.</summary>
        public string SourceMaterialIdentity { get; }
        /// <summary>Gets the Editor-only source declaration used by later preventive validation; it is never serialized into Schema.</summary>
        internal MaterialProxyEntry ValidationBinding { get; }
    }

    /// <summary>Detached Atlas Editor candidate snapshot.</summary>
    public sealed class AtlasEditorCandidateSnapshot
    {
        internal AtlasEditorCandidateSnapshot(string figureIdentity, string documentIdentity, IReadOnlyList<AtlasEditorCandidate> entries)
        {
            FigureIdentity = figureIdentity;
            DocumentIdentity = documentIdentity;
            Entries = new List<AtlasEditorCandidate>(entries ?? Array.Empty<AtlasEditorCandidate>()).AsReadOnly();
        }
        /// <summary>Gets the canonical Figure identity for Schema validation provenance.</summary>
        public string FigureIdentity { get; }
        /// <summary>Gets the canonical Document identity for Schema validation provenance.</summary>
        public string DocumentIdentity { get; }
        /// <summary>Gets candidates in deterministic MaterialId ordinal order.</summary>
        public IReadOnlyList<AtlasEditorCandidate> Entries { get; }
    }

    /// <summary>Collects the Editor's conservative Atlas candidate superset without recipe execution.</summary>
    public static class AtlasEditorCandidateCollector
    {
        /// <summary>Collects Figure and document Outfit MaterialProxy declarations into a detached snapshot.</summary>
        public static bool TryCollect(GameObject figure, IShapeSyncDocument document, out AtlasEditorCandidateSnapshot snapshot, out StackMachineDiagnostic diagnostic)
        {
            snapshot = null;
            if (figure == null) return Fail("AtlasEditorFigureRequired", "Atlas Editor requires a Figure.", out diagnostic);
            if (document == null || document.MeshBinding == null) return Fail("AtlasEditorMeshBindingRequired", "Atlas Editor requires a Document MeshBinding.", out diagnostic);
            string figureIdentity = AtlasEditorIdentityTokenProvider.Create(figure);
            UnityEngine.Object documentObject = document as UnityEngine.Object;
            string documentIdentity = AtlasEditorIdentityTokenProvider.Create(documentObject);
            if (string.IsNullOrEmpty(figureIdentity) || string.IsNullOrEmpty(documentIdentity)) return Fail("AtlasEditorIdentityRequired", "Atlas Editor requires persistent Figure and Document identities.", out diagnostic);
            var entries = new List<AtlasEditorCandidate>();
            if (!TryCollectOwner(figure, string.Empty, entries, out diagnostic)) return false;
            if (document.MeshRecipe == null) return Fail("AtlasEditorMeshRecipeRequired", "Atlas Editor requires a Document MeshRecipe.", out diagnostic);
            IReadOnlyList<MeshMorphBindingEntry> morphs = document.MeshBinding.Morphs;
            if (morphs == null) return Fail("AtlasEditorMorphBindingInvalid", "Atlas Editor requires a MeshBinding Morph collection.", out diagnostic);
            var logicalNames = new HashSet<string>(StringComparer.Ordinal);
            var targetNames = new HashSet<string>(StringComparer.Ordinal);
            var coreBindings = new List<MeshCoreBinding>();
            for (int i = 0; i < morphs.Count; i++)
            {
                MeshMorphBindingEntry morph = morphs[i];
                if (morph == null || string.IsNullOrWhiteSpace(morph.logicalName) || string.IsNullOrWhiteSpace(morph.targetName) || !logicalNames.Add(morph.logicalName))
                    return Fail("AtlasEditorMorphBindingInvalid", "Atlas Editor MeshBinding Morph bindings require complete, unique logical names.", out diagnostic);
                if (!targetNames.Add(morph.targetName)) return Fail("AtlasEditorMorphDuplicate", "Atlas Editor MeshBinding maps one Morph target more than once.", out diagnostic);
                coreBindings.Add(MeshCoreBinding.Morph(morph.logicalName, morph.targetName));
            }
            IReadOnlyList<MeshOutfitBindingEntry> outfits = document.MeshBinding.Outfits;
            if (outfits == null) return Fail("AtlasEditorOutfitBindingInvalid", "Atlas Editor requires a MeshBinding Outfit collection.", out diagnostic);
            var registryIds = new HashSet<string>(StringComparer.Ordinal);
            var outfitsByLogicalName = new Dictionary<string, MeshOutfitBindingEntry>(StringComparer.Ordinal);
            for (int i = 0; i < outfits.Count; i++)
            {
                MeshOutfitBindingEntry binding = outfits[i];
                GameObject prefab = binding?.outfitPrefab;
                if (prefab == null) return Fail("AtlasEditorOutfitRequired", "Atlas Editor MeshBinding contains a missing Outfit prefab.", out diagnostic);
                ShapeSyncOutfit outfit = prefab.GetComponent<ShapeSyncOutfit>();
                if (outfit == null || string.IsNullOrWhiteSpace(outfit.RegistryId)) return Fail("AtlasEditorOutfitRegistryRequired", "Atlas Editor Outfit prefab requires ShapeSyncOutfit with a registry ID.", out diagnostic);
                if (string.IsNullOrWhiteSpace(binding.logicalName) || !logicalNames.Add(binding.logicalName)) return Fail("AtlasEditorOutfitBindingInvalid", "Atlas Editor MeshBinding Outfit bindings require unique logical names.", out diagnostic);
                if (!registryIds.Add(outfit.RegistryId)) return Fail("AtlasEditorOutfitRegistryDuplicate", "Atlas Editor MeshBinding Outfit registry IDs must be unique.", out diagnostic);
                outfitsByLogicalName.Add(binding.logicalName, binding);
                bool hasPcmSource = outfit.ProfileControlledMorphEnabled || outfit.ProfileControlledMorphAsset != null;
                bool hasBcpSource = outfit.HumanoidBoneCorrectionProfile != null || (outfit.FbmHumanoidBoneCorrectionProfiles != null && outfit.FbmHumanoidBoneCorrectionProfiles.Count != 0);
                coreBindings.Add(MeshCoreBinding.Outfit(binding.logicalName, outfit.RegistryId, hasPcmSource, hasBcpSource));
            }
            if (!MeshNormalBlockParser.TryExtract(document.MeshRecipe.wordSource, out _, out IReadOnlyList<NormalRecipeTemplate> normalTemplates, out diagnostic)) return false;
            for (int i = 0; i < normalTemplates.Count; i++)
            {
                NormalRecipeTemplate template = normalTemplates[i];
                if (template == null || string.IsNullOrWhiteSpace(template.EntryName)) return Fail("AtlasEditorNormalBindingInvalid", "Atlas Editor MeshRecipe NORMAL entries require a name.", out diagnostic);
                coreBindings.Add(MeshCoreBinding.Normal(template.EntryName));
            }
            if (!MeshStackMachineCorePlan.TryCreate(document.MeshRecipe, coreBindings, out MeshStackMachineCorePlan corePlan, out diagnostic)) return false;
            for (int i = 0; i < corePlan.Operations.Count; i++)
            {
                MeshCoreOperation operation = corePlan.Operations[i];
                if (operation.Kind != MeshCoreOperationKind.AttachOutfit) continue;
                if (!outfitsByLogicalName.TryGetValue(operation.LogicalName, out MeshOutfitBindingEntry binding)) return Fail("AtlasEditorOutfitBindingMissing", "Atlas Editor MeshRecipe ATTACH refers to an unresolved MeshBinding Outfit.", out diagnostic);
                ShapeSyncOutfit outfit = binding.outfitPrefab.GetComponent<ShapeSyncOutfit>();
                if (!TryCollectOwner(binding.outfitPrefab, outfit.RegistryId, entries, out diagnostic)) return false;
            }
            entries.Sort((a, b) =>
            {
                int owner = string.CompareOrdinal(a.MaterialId.RegistryId, b.MaterialId.RegistryId);
                return owner != 0 ? owner : string.CompareOrdinal(a.MaterialId.EntryId, b.MaterialId.EntryId);
            });
            for (int i = 1; i < entries.Count; i++) if (entries[i - 1].MaterialId.Equals(entries[i].MaterialId)) return Fail("AtlasEditorMaterialIdDuplicate", "Atlas Editor candidates contain a duplicate MaterialId.", out diagnostic);
            snapshot = new AtlasEditorCandidateSnapshot(figureIdentity, documentIdentity, entries); diagnostic = null; return true;
        }

        private static bool TryCollectOwner(GameObject root, string owner, List<AtlasEditorCandidate> destination, out StackMachineDiagnostic diagnostic)
        {
            MaterialProxy[] proxies = root.GetComponentsInChildren<MaterialProxy>(true);
            for (int p = 0; p < proxies.Length; p++)
            {
                if (!IsOwnedByRoot(proxies[p], root)) continue;
                IReadOnlyList<MaterialProxyEntry> proxyEntries = proxies[p].Entries;
                if (proxyEntries == null) return Fail("AtlasEditorMaterialBindingInvalid", "Atlas Editor requires a MaterialProxy entry collection.", out diagnostic);
                for (int i = 0; i < proxyEntries.Count; i++)
                {
                    MaterialProxyEntry entry = proxyEntries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.entryName) || entry.renderer == null) return Fail("AtlasEditorMaterialBindingInvalid", "Atlas Editor requires valid MaterialProxy declarations.", out diagnostic);
                    Material[] materials = entry.renderer.sharedMaterials;
                    if (entry.materialChannel < 0 || entry.materialChannel >= materials.Length || materials[entry.materialChannel] == null) return Fail("AtlasEditorSourceMaterialRequired", "Atlas Editor MaterialProxy declaration has no source Material.", out diagnostic);
                    Material material = materials[entry.materialChannel];
                    string identity = AtlasEditorIdentityTokenProvider.Create(material);
                    if (string.IsNullOrEmpty(identity)) return Fail("AtlasEditorSourceMaterialIdentityRequired", "Atlas Editor source Material requires a persistent identity.", out diagnostic);
                    destination.Add(new AtlasEditorCandidate(owner, new MaterialId(owner, entry.entryName), material.name, identity, entry));
                }
            }
            diagnostic = null; return true;
        }

        private static bool IsOwnedByRoot(MaterialProxy proxy, GameObject root)
        {
            for (Transform current = proxy.transform; current != null && current.gameObject != root; current = current.parent)
            {
                if (current.GetComponent<ShapeSyncOutfit>() != null) return false;
            }
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message); return false; }
    }

    /// <summary>Creates the canonical Editor provenance token shared by Atlas authoring and final Editor-side build callers.</summary>
    public static class AtlasEditorIdentityTokenProvider
    {
        /// <summary>Creates a token for one persistent Unity object, or an empty string when no identity exists.</summary>
        public static string Create(UnityEngine.Object value)
        {
            if (value == null) return string.Empty;
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(value);
            string token = id.ToString();
            return id.assetGUID.Empty() || string.IsNullOrWhiteSpace(token) || token == "GlobalObjectId_V1-0-0-0-0-0" ? string.Empty : token;
        }
    }
}
