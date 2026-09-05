// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Read-only source selected for one compiler MaterialId.</summary>
    public readonly struct HumanoidMaterialEntrySource
    {
        public HumanoidMaterialEntrySource(MaterialId materialId, Material sourceMaterial, MaterialShaderAdapter adapter)
        {
            MaterialId = materialId;
            SourceMaterial = sourceMaterial;
            Adapter = adapter;
        }

        public MaterialId MaterialId { get; }
        public Material SourceMaterial { get; }
        public MaterialShaderAdapter Adapter { get; }
    }

    /// <summary>One Figure or Outfit target-local Material plan with immutable entry sources.</summary>
    public sealed class HumanoidMaterialTargetPlan
    {
        private readonly HumanoidMaterialEntrySource[] entries;
        private readonly Dictionary<string, HumanoidMaterialEntrySource> entriesByName;

        internal HumanoidMaterialTargetPlan(string registryId, MaterialRecipeDocument textureDocument, MaterialStackMachineCorePlan corePlan, HumanoidMaterialEntrySource[] entries)
        {
            RegistryId = registryId ?? string.Empty;
            TextureDocument = textureDocument;
            CorePlan = corePlan;
            this.entries = entries;
            entriesByName = new Dictionary<string, HumanoidMaterialEntrySource>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++) entriesByName.Add(entries[i].MaterialId.EntryId, entries[i]);
        }

        /// <summary>Gets empty string for Figure or the physical Outfit RegistryId.</summary>
        public string RegistryId { get; }
        /// <summary>Gets the target-local Texture metadata preserved for later BaseColor plan compilation.</summary>
        public MaterialRecipeDocument TextureDocument { get; }
        public MaterialStackMachineCorePlan CorePlan { get; }
        public IReadOnlyList<HumanoidMaterialEntrySource> Entries => Array.AsReadOnly(entries);
        public bool TryGetEntry(string entryName, out HumanoidMaterialEntrySource source) => entriesByName.TryGetValue(entryName ?? string.Empty, out source);
    }

    /// <summary>Detached compiler input for Material recipe execution; it owns no Material or Texture result.</summary>
    public sealed class HumanoidMaterialLogicalPlan
    {
        private readonly HumanoidMaterialTargetPlan[] targets;
        internal HumanoidMaterialLogicalPlan(MaterialBinding textureBinding, HumanoidMaterialTargetPlan[] targets)
        {
            TextureBinding = textureBinding;
            this.targets = targets;
        }

        /// <summary>Gets the shared read-only binding used later to build BaseColor Texture requests.</summary>
        public MaterialBinding TextureBinding { get; }
        public IReadOnlyList<HumanoidMaterialTargetPlan> Targets => Array.AsReadOnly(targets);
    }

    /// <summary>Collects Figure and Material-scope Outfit sources without proxy commits, cloning, or scene mutation.</summary>
    public static class HumanoidMaterialLogicalCollector
    {
        public static bool TryCreate(GameObject figureRoot, ShapeSyncDocument document, out HumanoidMaterialLogicalPlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            diagnostic = null;
            if (figureRoot == null) return Fail("FigureRequired", "Humanoid Material collection requires a Figure root.", out diagnostic);
            if (document == null) return Fail("MaterialDocumentRequired", "Humanoid Material collection requires a detached ShapeSyncDocument.", out diagnostic);
            if (document.MaterialRecipe == null)
            {
                // A Director with no committed Material shape preserves the source materials.
                // No target-local material execution or shared binding is required.
                plan = new HumanoidMaterialLogicalPlan(null, Array.Empty<HumanoidMaterialTargetPlan>());
                return true;
            }
            if (!MaterialTargetScopeParser.TryParse(document.MaterialRecipe.wordSource, out IReadOnlyList<MaterialTargetSource> targets, out diagnostic)) return false;

            Dictionary<string, GameObject> outfitsByRegistry = null;
            var plans = new List<HumanoidMaterialTargetPlan>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                MaterialTargetSource target = targets[i];
                string registryId = target.OutfitRegistryId ?? string.Empty;
                GameObject root;
                if (registryId.Length == 0) root = figureRoot;
                else
                {
                    if (outfitsByRegistry == null && !TryCreateOutfitRegistryIndex(document.MeshBinding, out outfitsByRegistry, out diagnostic)) return false;
                    if (!outfitsByRegistry.TryGetValue(registryId, out root)) return Fail("MaterialOutfitRegistryMissing", "Material OUTFIT target does not resolve to a MeshBinding Outfit RegistryId.", out diagnostic, registryId);
                }

                MaterialRecipeDocument targetDocument = CreateTargetDocument(document.MaterialRecipe, target.Source);
                if (!MaterialStackMachineCorePlan.TryCreate(targetDocument, out MaterialStackMachineCorePlan corePlan, out diagnostic)) return false;
                if (RequiresTextureBinding(corePlan) && document.MaterialBinding == null) return Fail("MaterialBindingRequired", "Material TEXTURE requires the shared MaterialBinding.", out diagnostic, registryId);
                if (!TryCreateEntrySources(root, registryId, out HumanoidMaterialEntrySource[] entries, out diagnostic)) return false;
                if (!ValidatePlanBindings(corePlan, entries, out diagnostic)) return false;
                plans.Add(new HumanoidMaterialTargetPlan(registryId, targetDocument, corePlan, entries));
            }

            plan = new HumanoidMaterialLogicalPlan(document.MaterialBinding, plans.ToArray());
            return true;
        }

        private static MaterialRecipeDocument CreateTargetDocument(MaterialRecipeDocument source, string wordSource)
        {
            return new MaterialRecipeDocument
            {
                recipeFormatVersion = source.recipeFormatVersion,
                wordSource = wordSource,
                bindings = source.bindings,
                capabilities = source.capabilities,
                provenance = source.provenance,
                diagnosticSourceMap = source.diagnosticSourceMap,
                textureDomainVersion = source.textureDomainVersion,
                outputLogicalName = source.outputLogicalName,
                outputWidth = source.outputWidth,
                outputHeight = source.outputHeight
            };
        }

        private static bool TryCreateOutfitRegistryIndex(MeshBinding binding, out Dictionary<string, GameObject> outfitsByRegistry, out StackMachineDiagnostic diagnostic)
        {
            outfitsByRegistry = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            if (binding == null) return Fail("MeshBindingRequired", "Material OUTFIT target requires a shared MeshBinding.", out diagnostic);
            IReadOnlyList<MeshOutfitBindingEntry> outfits = binding.Outfits;
            for (int i = 0; i < outfits.Count; i++)
            {
                MeshOutfitBindingEntry entry = outfits[i];
                ShapeSyncOutfit outfit = entry == null || entry.outfitPrefab == null ? null : entry.outfitPrefab.GetComponent<ShapeSyncOutfit>();
                if (entry == null || string.IsNullOrWhiteSpace(entry.logicalName) || outfit == null || string.IsNullOrWhiteSpace(outfit.RegistryId))
                    return Fail("OutfitBindingInvalid", "MeshBinding Outfit entries require a logical name, ShapeSyncOutfit root, and RegistryId.", out diagnostic, entry == null ? null : entry.logicalName);
                if (!outfitsByRegistry.TryAdd(outfit.RegistryId, entry.outfitPrefab))
                    return Fail("DuplicateRegistryId", "MeshBinding Outfit RegistryIds must be unique.", out diagnostic, entry.logicalName, outfit.RegistryId);
            }
            diagnostic = null;
            return true;
        }

        private static bool TryCreateEntrySources(GameObject root, string registryId, out HumanoidMaterialEntrySource[] sources, out StackMachineDiagnostic diagnostic)
        {
            sources = null;
            if (root == null) return Fail("MaterialSourceRootMissing", "Material source root is missing.", out diagnostic, registryId);
            MaterialProxy proxy = root.GetComponent<MaterialProxy>();
            if (proxy == null) return Fail("MaterialProxyMissing", "Material source requires a root-local MaterialProxy.", out diagnostic, registryId);
            IReadOnlyList<MaterialProxyEntry> entries = proxy.Entries;
            if (entries == null || entries.Count == 0) return Fail("MaterialProxyEntryMissing", "MaterialProxy requires at least one entry.", out diagnostic, registryId);

            var names = new HashSet<string>(StringComparer.Ordinal);
            var channels = new HashSet<string>(StringComparer.Ordinal);
            var result = new HumanoidMaterialEntrySource[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialProxyEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.entryName) || !names.Add(entry.entryName))
                    return Fail("MaterialProxyEntryInvalid", "MaterialProxy entries require unique non-empty names.", out diagnostic, registryId);
                if (entry.renderer == null || !entry.renderer.transform.IsChildOf(root.transform))
                    return Fail("MaterialProxyRendererMismatch", "MaterialProxy entry renderer must belong to its source root.", out diagnostic, entry.entryName, registryId);
                string channelKey = entry.renderer.GetInstanceID().ToString() + "/" + entry.materialChannel.ToString();
                if (!channels.Add(channelKey)) return Fail("MaterialProxyChannelDuplicate", "MaterialProxy entries must own unique renderer material channels.", out diagnostic, entry.entryName, registryId);
                Material[] materials = entry.renderer.sharedMaterials;
                if (entry.materialChannel < 0 || entry.materialChannel >= materials.Length || materials[entry.materialChannel] == null)
                    return Fail("MaterialProxySourceMaterialMissing", "MaterialProxy entry has no source Material channel.", out diagnostic, entry.entryName, registryId);
                if (entry.adapter == null) return Fail("MaterialProxyAdapterMissing", "MaterialProxy entry requires a MaterialShaderAdapter.", out diagnostic, entry.entryName, registryId);
                result[i] = new HumanoidMaterialEntrySource(new MaterialId(registryId, entry.entryName), materials[entry.materialChannel], entry.adapter);
            }
            sources = result;
            diagnostic = null;
            return true;
        }

        private static bool ValidatePlanBindings(MaterialStackMachineCorePlan corePlan, IReadOnlyList<HumanoidMaterialEntrySource> entries, out StackMachineDiagnostic diagnostic)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++) names.Add(entries[i].MaterialId.EntryId);
            for (int i = 0; i < corePlan.Blocks.Count; i++)
            {
                MaterialStackMachineBlock block = corePlan.Blocks[i];
                if (block.IsReset) continue;
                if (string.IsNullOrWhiteSpace(block.BindingName) || !names.Contains(block.BindingName))
                    return Fail("MaterialBindingMissing", "Material recipe block does not resolve to a source MaterialProxy entry.", out diagnostic, block.BindingName);
            }
            diagnostic = null;
            return true;
        }

        private static bool RequiresTextureBinding(MaterialStackMachineCorePlan corePlan)
        {
            for (int i = 0; i < corePlan.Blocks.Count; i++) if (corePlan.Blocks[i].TextureSource != null) return true;
            return false;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message, bindingName: binding, detail: detail);
            return false;
        }
    }
}
