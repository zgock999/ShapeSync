// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Read-only physical source resolved for the Figure or one ATTACH Outfit candidate.</summary>
    public readonly struct HumanoidMeshSource
    {
        public HumanoidMeshSource(string logicalName, string registryId, GameObject root, ShapeSyncOutfit outfit, SkinnedMeshRenderer renderer, MaterialProxy materialProxy, IReadOnlyDictionary<Transform, string> weightedBonePaths = null)
        {
            LogicalName = logicalName;
            RegistryId = registryId;
            Root = root;
            Outfit = outfit;
            Renderer = renderer;
            MaterialProxy = materialProxy;
            WeightedBonePaths = weightedBonePaths;
        }

        public string LogicalName { get; }
        public string RegistryId { get; }
        public GameObject Root { get; }
        public ShapeSyncOutfit Outfit { get; }
        public SkinnedMeshRenderer Renderer { get; }
        public MaterialProxy MaterialProxy { get; }
        /// <summary>OutfitAttacher-equivalent source-root-relative paths for used weighted bones; null for the Figure source.</summary>
        public IReadOnlyDictionary<Transform, string> WeightedBonePaths { get; }
    }

    /// <summary>One immutable non-Base Normal texture source for an EditMode Mesh owner entry.</summary>
    public readonly struct HumanoidMeshNormalTargetSource
    {
        public HumanoidMeshNormalTargetSource(string targetName, Texture2D texture)
        {
            TargetName = targetName;
            Texture = texture;
        }

        public string TargetName { get; }
        public Texture2D Texture { get; }
    }

    /// <summary>One detached Mesh-owned Normal input selected for a Figure or ATTACH Outfit Proxy entry.</summary>
    public sealed class HumanoidMeshNormalSource
    {
        public HumanoidMeshNormalSource(HumanoidMeshSource owner, string entryName, Texture2D baseTexture, HumanoidMeshNormalTargetSource[] targets)
        {
            Owner = owner;
            EntryName = entryName;
            BaseTexture = baseTexture;
            Targets = Array.AsReadOnly(targets);
        }

        public HumanoidMeshSource Owner { get; }
        public string EntryName { get; }
        public Texture2D BaseTexture { get; }
        public IReadOnlyList<HumanoidMeshNormalTargetSource> Targets { get; }
    }

    /// <summary>One recipe-resolved source Normal texture keyed by the final MaterialId.</summary>
    public readonly struct HumanoidMeshNormalTextureRegistration
    {
        public HumanoidMeshNormalTextureRegistration(MaterialId materialId, Texture2D normalTexture)
        {
            MaterialId = materialId;
            NormalTexture = normalTexture;
        }

        /// <summary>Gets the Figure or Outfit material identity that receives this source Normal texture.</summary>
        public MaterialId MaterialId { get; }

        /// <summary>Gets the read-only source Normal texture selected by the NORMAL clause.</summary>
        public Texture2D NormalTexture { get; }
    }

    /// <summary>Detached logical result consumed by later EditMode Mesh finalization steps.</summary>
    public sealed class HumanoidMeshLogicalPlan
    {
        public HumanoidMeshLogicalPlan(MeshStackMachineCorePlan corePlan, HumanoidMeshSource figure, HumanoidMeshSource[] attachedOutfits, HumanoidMeshSource[] pcmSources, HumanoidMeshSource[] bcpSources, HumanoidMeshNormalSource[] normalSources, HumanoidMeshNormalTextureRegistration[] normalTextureRegistrations = null)
        {
            CorePlan = corePlan;
            Figure = figure;
            AttachedOutfits = Array.AsReadOnly(attachedOutfits);
            PcmSources = Array.AsReadOnly(pcmSources);
            BcpSources = Array.AsReadOnly(bcpSources);
            NormalSources = Array.AsReadOnly(normalSources);
            NormalTextureRegistrations = Array.AsReadOnly(normalTextureRegistrations ?? Array.Empty<HumanoidMeshNormalTextureRegistration>());
        }

        public MeshStackMachineCorePlan CorePlan { get; }
        public HumanoidMeshSource Figure { get; }
        public IReadOnlyList<HumanoidMeshSource> AttachedOutfits { get; }
        public IReadOnlyList<HumanoidMeshSource> PcmSources { get; }
        public IReadOnlyList<HumanoidMeshSource> BcpSources { get; }
        public IReadOnlyList<HumanoidMeshNormalSource> NormalSources { get; }
        /// <summary>Gets the source Normal texture registry emitted by resolved NORMAL clauses.</summary>
        public IReadOnlyList<HumanoidMeshNormalTextureRegistration> NormalTextureRegistrations { get; }
    }

    /// <summary>
    /// UnityEditor-independent ATTACH logical-name carrier emitted by completed Mesh lower.
    /// It owns only copied logical names; 17.6 resolves those names through the same detached Mesh binding.
    /// </summary>
    public sealed class HumanoidMeshVrmTransportProvenance : IDisposable
    {
        private string[] attachedOutfitLogicalNames;
        private IReadOnlyList<string> attachedOutfitLogicalNameView;

        private HumanoidMeshVrmTransportProvenance(IReadOnlyList<HumanoidMeshSource> attachedOutfits)
        {
            attachedOutfitLogicalNames = new string[attachedOutfits == null ? 0 : attachedOutfits.Count];
            for (int i = 0; i < attachedOutfitLogicalNames.Length; i++) attachedOutfitLogicalNames[i] = attachedOutfits[i].LogicalName;
            attachedOutfitLogicalNameView = Array.AsReadOnly(attachedOutfitLogicalNames);
        }

        /// <summary>Creates one independent logical-name snapshot from a completed Mesh lower plan.</summary>
        public static bool TryCreate(HumanoidMeshLogicalPlan plan, out HumanoidMeshVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic)
        {
            provenance = null;
            diagnostic = null;
            if (plan == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "VrmTransportLogicalPlanRequired", "VRM transport provenance requires a completed Mesh logical plan.");
                return false;
            }

            for (int i = 0; i < plan.AttachedOutfits.Count; i++)
            {
                if (!string.IsNullOrEmpty(plan.AttachedOutfits[i].LogicalName)) continue;
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "VrmTransportLogicalNameRequired", "VRM transport provenance requires a logical name for every ATTACH Outfit.");
                return false;
            }

            provenance = new HumanoidMeshVrmTransportProvenance(plan.AttachedOutfits);
            return true;
        }

        /// <summary>Gets ATTACH Outfit logical names in Mesh lower order, or an empty view after disposal.</summary>
        public IReadOnlyList<string> AttachedOutfitLogicalNames => attachedOutfitLogicalNameView ?? Array.Empty<string>();

        /// <summary>Clears only this carrier's copied logical-name list.</summary>
        public void Dispose()
        {
            attachedOutfitLogicalNames = Array.Empty<string>();
            attachedOutfitLogicalNameView = Array.Empty<string>();
        }
    }

    /// <summary>
    /// Resolves the Editor-only, read-only source records required by the EditMode Mesh backend.
    /// It does not instantiate Outfits, modify the Figure, create Meshes, dispatch Textures, or start a transaction.
    /// </summary>
    public static class HumanoidMeshLogicalCollector
    {
        public static bool TryCreate(GameObject figureRoot, ShapeSyncDocument document, out HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            diagnostic = null;
            if (figureRoot == null) return Fail("FigureRequired", "EditMode Mesh collection requires a Figure root.", out diagnostic);
            if (document == null) return Fail("DocumentRequired", "EditMode Mesh collection requires a detached ShapeSyncDocument.", out diagnostic);
            if (document.MeshRecipe == null)
            {
                // A Director with no committed Mesh shapes still requests a valid base-Figure
                // bake. Its resolved MeshBinding is retained as Figure context, but there are
                // no logical mesh clauses to lower. Do not fabricate a recovery recipe.
                if (!TryResolveFigure(figureRoot, out HumanoidMeshSource baseFigure, out diagnostic)) return false;
                if (!MeshStackMachineCorePlan.TryCreate(new MeshRecipeDocument { wordSource = "MORPH_RESET" }, new List<MeshCoreBinding>(), out MeshStackMachineCorePlan emptyPlan, out diagnostic)) return false;
                plan = new HumanoidMeshLogicalPlan(emptyPlan, baseFigure, Array.Empty<HumanoidMeshSource>(), Array.Empty<HumanoidMeshSource>(), Array.Empty<HumanoidMeshSource>(), Array.Empty<HumanoidMeshNormalSource>());
                return true;
            }
            if (document.MeshRecipe == null || document.MeshBinding == null)
                return Fail("MeshDocumentBindingRequired", "EditMode Mesh collection requires both MeshRecipe and MeshBinding.", out diagnostic);

            if (!TryResolveFigure(figureRoot, out HumanoidMeshSource figure, out diagnostic)) return false;
            if (!TryCreateCoreBindings(document.MeshBinding, document.MeshRecipe, out List<MeshCoreBinding> coreBindings, out Dictionary<string, OutfitDeclaration> outfitsByLogicalName, out diagnostic)) return false;
            if (!MeshStackMachineCorePlan.TryCreate(document.MeshRecipe, coreBindings, out MeshStackMachineCorePlan corePlan, out diagnostic)) return false;

            var attachedOutfits = new List<HumanoidMeshSource>();
            var pcmSources = new List<HumanoidMeshSource>();
            var bcpSources = new List<HumanoidMeshSource>();
            for (int i = 0; i < corePlan.Operations.Count; i++)
            {
                MeshCoreOperation operation = corePlan.Operations[i];
                if (operation.Kind != MeshCoreOperationKind.AttachOutfit) continue;
                if (!outfitsByLogicalName.TryGetValue(operation.LogicalName, out OutfitDeclaration declaration))
                    return Fail("OutfitBindingMissing", "ATTACH refers to an unresolved MeshBinding Outfit.", out diagnostic, operation.LogicalName);
                if (!TryResolveOutfit(figure.Root, declaration, out HumanoidMeshSource outfit, out diagnostic)) return false;
                attachedOutfits.Add(outfit);
                if (operation.RegistersPcmSource) pcmSources.Add(outfit);
                if (operation.RegistersBcpSource) bcpSources.Add(outfit);
            }

            if (!TryCreateNormalSources(document.MeshBinding.NormalOwners, corePlan.NormalTemplates, figure, attachedOutfits, out HumanoidMeshNormalSource[] normalSources, out HumanoidMeshNormalTextureRegistration[] normalTextureRegistrations, out diagnostic)) return false;
            plan = new HumanoidMeshLogicalPlan(corePlan, figure, attachedOutfits.ToArray(), pcmSources.ToArray(), bcpSources.ToArray(), normalSources, normalTextureRegistrations);
            return true;
        }

        private static bool TryCreateCoreBindings(MeshBinding binding, MeshRecipeDocument recipe, out List<MeshCoreBinding> coreBindings, out Dictionary<string, OutfitDeclaration> outfitsByLogicalName, out StackMachineDiagnostic diagnostic)
        {
            coreBindings = new List<MeshCoreBinding>();
            outfitsByLogicalName = new Dictionary<string, OutfitDeclaration>(StringComparer.Ordinal);
            diagnostic = null;
            IReadOnlyList<MeshMorphBindingEntry> morphs = binding.Morphs;
            for (int i = 0; i < morphs.Count; i++)
            {
                MeshMorphBindingEntry morph = morphs[i];
                if (morph == null || string.IsNullOrWhiteSpace(morph.logicalName) || string.IsNullOrWhiteSpace(morph.targetName))
                    return Fail("MorphBindingInvalid", "MeshBinding morph entries require logical and target names.", out diagnostic);
                coreBindings.Add(MeshCoreBinding.Morph(morph.logicalName, morph.targetName));
            }

            IReadOnlyList<MeshOutfitBindingEntry> outfits = binding.Outfits;
            var registryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < outfits.Count; i++)
            {
                MeshOutfitBindingEntry entry = outfits[i];
                ShapeSyncOutfit outfit = entry == null || entry.outfitPrefab == null ? null : entry.outfitPrefab.GetComponent<ShapeSyncOutfit>();
                if (entry == null || string.IsNullOrWhiteSpace(entry.logicalName) || outfit == null || string.IsNullOrWhiteSpace(outfit.RegistryId))
                    return Fail("OutfitBindingInvalid", "MeshBinding Outfit entries require a logical name, ShapeSyncOutfit root, and RegistryId.", out diagnostic, entry == null ? null : entry.logicalName);
                if (!outfitsByLogicalName.TryAdd(entry.logicalName, new OutfitDeclaration(entry.logicalName, entry.outfitPrefab, outfit)))
                    return Fail("DuplicateLogicalBinding", "MeshBinding Outfit logical names must be unique.", out diagnostic, entry.logicalName);
                if (!registryIds.Add(outfit.RegistryId))
                    return Fail("DuplicateRegistryId", "MeshBinding Outfit entries must have unique RegistryIds even when a recipe does not ATTACH every entry.", out diagnostic, entry.logicalName, outfit.RegistryId);
                bool hasPcmSource = outfit.ProfileControlledMorphEnabled || outfit.ProfileControlledMorphAsset != null;
                bool hasBcpSource = outfit.HumanoidBoneCorrectionProfile != null || (outfit.FbmHumanoidBoneCorrectionProfiles != null && outfit.FbmHumanoidBoneCorrectionProfiles.Count != 0);
                coreBindings.Add(MeshCoreBinding.Outfit(entry.logicalName, outfit.RegistryId, hasPcmSource, hasBcpSource));
            }

            if (!MeshNormalBlockParser.TryExtract(recipe.wordSource, out _, out IReadOnlyList<NormalRecipeTemplate> normalTemplates, out diagnostic)) return false;
            for (int i = 0; i < normalTemplates.Count; i++)
            {
                NormalRecipeTemplate template = normalTemplates[i];
                if (template == null || string.IsNullOrWhiteSpace(template.EntryName)) return Fail("NormalEntryInvalid", "NORMAL requires a non-empty entry name.", out diagnostic);
                coreBindings.Add(MeshCoreBinding.Normal(template.EntryName));
            }
            return true;
        }

        private static bool TryResolveFigure(GameObject figureRoot, out HumanoidMeshSource source, out StackMachineDiagnostic diagnostic)
        {
            source = default;
            if (!TryGetSingleRenderer(figureRoot, "Figure", out SkinnedMeshRenderer renderer, out diagnostic)) return false;
            MaterialProxy proxy = figureRoot.GetComponent<MaterialProxy>();
            if (!TryValidateMaterialProxy(proxy, renderer, "Figure", out diagnostic)) return false;
            source = new HumanoidMeshSource(null, string.Empty, figureRoot, null, renderer, proxy);
            return true;
        }

        private static bool TryResolveOutfit(GameObject figureRoot, OutfitDeclaration declaration, out HumanoidMeshSource source, out StackMachineDiagnostic diagnostic)
        {
            source = default;
            if (!TryGetSingleRenderer(declaration.Root, "Outfit", out SkinnedMeshRenderer renderer, out diagnostic, declaration.LogicalName)) return false;
            if (!declaration.Outfit.TryValidateProfileControlledMorphConfiguration(out string pcmError))
                return Fail("OutfitPcmConfigurationInvalid", "Outfit PCM configuration is not accepted by OutfitAttacher.", out diagnostic, declaration.LogicalName, pcmError);
            if (!TryCreateOutfitWeightedBonePaths(figureRoot, declaration, renderer, out IReadOnlyDictionary<Transform, string> weightedBonePaths, out diagnostic)) return false;
            MaterialProxy proxy = declaration.Root.GetComponent<MaterialProxy>();
            if (!TryValidateMaterialProxy(proxy, renderer, "Outfit", out diagnostic, declaration.LogicalName)) return false;
            source = new HumanoidMeshSource(declaration.LogicalName, declaration.Outfit.RegistryId, declaration.Root, declaration.Outfit, renderer, proxy, weightedBonePaths);
            return true;
        }

        /// <summary>Read-only equivalent of OutfitAttacher's renderer-plan preflight for Spec17's one-renderer topology.</summary>
        private static bool TryCreateOutfitWeightedBonePaths(GameObject figureRoot, OutfitDeclaration declaration, SkinnedMeshRenderer renderer, out IReadOnlyDictionary<Transform, string> weightedBonePaths, out StackMachineDiagnostic diagnostic)
        {
            weightedBonePaths = null;
            diagnostic = null;
            OutfitSkinningProfile skinning = declaration.Outfit.SkinningProfile;
            if (skinning == null) return Fail("OutfitSkinningProfileRequired", "Outfit attach requires an OutfitSkinningProfile.", out diagnostic, declaration.LogicalName);
            string rendererPath = GetRelativePath(declaration.Root.transform, renderer.transform);
            if (string.IsNullOrEmpty(rendererPath) || !skinning.TryGetRenderer(rendererPath, out OutfitSkinningRendererProfile profile) || profile == null)
                return Fail("OutfitSkinningRendererProfileMissing", "OutfitSkinningProfile has no entry for the source renderer path.", out diagnostic, declaration.LogicalName, rendererPath);
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            if (mesh == null || bones == null || bones.Length == 0 || renderer.rootBone == null)
                return Fail("OutfitSkinningRendererInvalid", "Outfit source renderer requires a Mesh, bones, and rootBone.", out diagnostic, declaration.LogicalName);
            if (mesh.bindposes == null || profile.baseBindposes == null || profile.baseBindposes.Length != mesh.bindposes.Length)
                return Fail("OutfitSkinningBindposeMismatch", "OutfitSkinningProfile base bindpose count must match the source Mesh.", out diagnostic, declaration.LogicalName);
            if (GetRelativePath(declaration.Root.transform, renderer.rootBone) == null)
                return Fail("OutfitSkinningRootBoneOutsideRoot", "Outfit renderer rootBone must be inside the Outfit hierarchy.", out diagnostic, declaration.LogicalName);

            bool[] used = GetUsedBoneIndices(mesh, bones.Length);
            var paths = new Dictionary<Transform, string>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (!used[i]) continue;
                Transform bone = bones[i];
                string path = bone == null ? null : GetRelativePath(declaration.Root.transform, bone);
                if (path == null)
                {
                    if (bone == null || figureRoot == null || GetRelativePath(figureRoot.transform, bone) == null)
                        return Fail("OutfitSkinningBonePathInvalid", "A weighted Outfit bone must be inside the Outfit hierarchy or the Figure hierarchy.", out diagnostic, declaration.LogicalName, i.ToString());
                    paths[bone] = null;
                    continue;
                }
                paths[bone] = path;
            }
            weightedBonePaths = paths;
            return true;
        }

        private static bool[] GetUsedBoneIndices(Mesh mesh, int boneCount)
        {
            var used = new bool[boneCount];
            if (mesh == null || boneCount == 0) return used;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                Mark(weights[i].boneIndex0, weights[i].weight0); Mark(weights[i].boneIndex1, weights[i].weight1);
                Mark(weights[i].boneIndex2, weights[i].weight2); Mark(weights[i].boneIndex3, weights[i].weight3);
            }
            return used;
            void Mark(int index, float weight) { if (weight > 0f && index >= 0 && index < used.Length) used[index] = true; }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (root == target) return string.Empty;
            var segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root) { segments.Push(current.name); current = current.parent; }
            return current == root ? string.Join("/", segments) : null;
        }

        private static bool TryGetSingleRenderer(GameObject root, string owner, out SkinnedMeshRenderer renderer, out StackMachineDiagnostic diagnostic, string binding = null)
        {
            renderer = null;
            if (root == null) return Fail(owner + "RootMissing", owner + " source root is missing.", out diagnostic, binding);
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
                return Fail(owner + "RendererCountInvalid", owner + " source requires exactly one SkinnedMeshRenderer.", out diagnostic, binding, renderers.Length.ToString());
            renderer = renderers[0];
            diagnostic = null;
            return true;
        }

        private static bool TryValidateMaterialProxy(MaterialProxy proxy, SkinnedMeshRenderer renderer, string owner, out StackMachineDiagnostic diagnostic, string binding = null)
        {
            diagnostic = null;
            if (proxy == null) return Fail("MaterialProxyMissing", owner + " source requires a root-local MaterialProxy.", out diagnostic, binding);
            IReadOnlyList<MaterialProxyEntry> entries = proxy.Entries;
            if (entries == null || entries.Count == 0) return Fail("MaterialProxyEntryMissing", owner + " MaterialProxy requires at least one entry.", out diagnostic, binding);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var channels = new HashSet<int>();
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialProxyEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.entryName)) return Fail("MaterialProxyEntryInvalid", owner + " MaterialProxy entries require unique names.", out diagnostic, binding);
                if (!names.Add(entry.entryName)) return Fail("MaterialProxyEntryDuplicate", owner + " MaterialProxy entry names must be unique.", out diagnostic, entry.entryName);
                if (entry.renderer != renderer) return Fail("MaterialProxyRendererMismatch", owner + " MaterialProxy entries must target the source SkinnedMeshRenderer.", out diagnostic, entry.entryName);
                if (!channels.Add(entry.materialChannel)) return Fail("MaterialProxyChannelDuplicate", owner + " MaterialProxy entries must own unique material channels.", out diagnostic, entry.entryName);
                if (entry.materialChannel < 0 || entry.materialChannel >= materials.Length || materials[entry.materialChannel] == null)
                    return Fail("MaterialProxySourceMaterialMissing", owner + " MaterialProxy entry has no source Material channel.", out diagnostic, entry.entryName);
                if (entry.adapter == null) return Fail("MaterialProxyAdapterMissing", owner + " MaterialProxy entry requires a MaterialShaderAdapter.", out diagnostic, entry.entryName);
            }
            return true;
        }

        private static bool TryCreateNormalSources(IReadOnlyList<MeshNormalOwnerBindingEntry> owners, IReadOnlyList<NormalRecipeTemplate> templates, HumanoidMeshSource figure, IReadOnlyList<HumanoidMeshSource> attachedOutfits, out HumanoidMeshNormalSource[] sources, out HumanoidMeshNormalTextureRegistration[] normalTextureRegistrations, out StackMachineDiagnostic diagnostic)
        {
            sources = null;
            normalTextureRegistrations = null;
            diagnostic = null;
            if (!TryIndexNormalOwners(owners, out Dictionary<string, MeshNormalOwnerBindingEntry> ownersByRegistryId, out diagnostic)) return false;
            var result = new List<HumanoidMeshNormalSource>();
            var registrations = new HumanoidMeshNormalTextureRegistration[templates.Count];
            var materialIds = new HashSet<MaterialId>();
            for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
            {
                string entryName = templates[templateIndex].EntryName;
                HumanoidMeshSource owner = default;
                int matches = 0;
                if (ProxyContainsEntry(figure.MaterialProxy, entryName)) { owner = figure; matches++; }
                for (int outfitIndex = 0; outfitIndex < attachedOutfits.Count; outfitIndex++)
                {
                    if (!ProxyContainsEntry(attachedOutfits[outfitIndex].MaterialProxy, entryName)) continue;
                    owner = attachedOutfits[outfitIndex];
                    matches++;
                }
                if (matches != 1) return Fail("NormalEntryOwnerInvalid", "NORMAL entry must resolve to exactly one Figure or ATTACH Outfit MaterialProxy entry.", out diagnostic, entryName, matches.ToString());
                string registryId = owner.RegistryId ?? string.Empty;
                if (!ownersByRegistryId.TryGetValue(registryId, out MeshNormalOwnerBindingEntry bindingOwner))
                    return Fail("NormalBaseTextureMissing", "NORMAL entry requires a matching Figure or ATTACH Outfit Normal owner with a Base texture.", out diagnostic, entryName, registryId);
                if (!TryCreateNormalSource(owner, bindingOwner, entryName, out HumanoidMeshNormalSource source, out diagnostic)) return false;
                var materialId = new MaterialId(owner.RegistryId, entryName);
                if (!materialIds.Add(materialId))
                    return Fail("NormalTextureRegistrationDuplicate", "NORMAL source textures must resolve to unique MaterialIds.", out diagnostic, entryName, materialId.ToString());
                registrations[templateIndex] = new HumanoidMeshNormalTextureRegistration(materialId, source.BaseTexture);
                if (TryGetNormalBlenderEntry(owner, entryName, out bool calculate, out diagnostic) == false) return false;
                if (calculate && source.Targets.Count != 0) result.Add(source);
            }
            sources = result.ToArray();
            normalTextureRegistrations = registrations;
            return true;
        }

        private static bool TryGetNormalBlenderEntry(HumanoidMeshSource owner, string entryName, out bool calculate, out StackMachineDiagnostic diagnostic)
        {
            calculate = false;
            diagnostic = null;
            NormalBlender blender = owner.Root == null ? null : owner.Root.GetComponent<NormalBlender>();
            if (blender == null) return true;

            var entries = blender.Entries;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                string candidate = entries[i];
                if (string.IsNullOrWhiteSpace(candidate) || !unique.Add(candidate))
                    return Fail("NormalBlenderEntryInvalid", "NormalBlender entries must be non-empty and unique.", out diagnostic, candidate, owner.RegistryId);
                if (candidate == entryName) calculate = true;
            }
            return true;
        }

        private static bool TryIndexNormalOwners(IReadOnlyList<MeshNormalOwnerBindingEntry> owners, out Dictionary<string, MeshNormalOwnerBindingEntry> ownersByRegistryId, out StackMachineDiagnostic diagnostic)
        {
            ownersByRegistryId = new Dictionary<string, MeshNormalOwnerBindingEntry>(StringComparer.Ordinal);
            diagnostic = null;
            var entryOwnerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int ownerIndex = 0; owners != null && ownerIndex < owners.Count; ownerIndex++)
            {
                MeshNormalOwnerBindingEntry owner = owners[ownerIndex];
                string registryId = owner == null ? null : owner.outfitRegistryId ?? string.Empty;
                if (owner == null || owner.targets == null || !ownersByRegistryId.TryAdd(registryId, owner))
                    return Fail("NormalTextureDictionaryInvalid", "Normal owners must be complete and have unique Figure or Outfit RegistryIds.", out diagnostic);
                var targetNames = new HashSet<string>(StringComparer.Ordinal);
                bool hasBase = false;
                for (int targetIndex = 0; targetIndex < owner.targets.Count; targetIndex++)
                {
                    MeshNormalTargetBindingEntry target = owner.targets[targetIndex];
                    if (target == null || target.textures == null)
                        return Fail("NormalTextureDictionaryInvalid", "Normal targets must include their texture lists.", out diagnostic);
                    if (string.IsNullOrEmpty(target.targetName))
                    {
                        if (hasBase) return Fail("NormalTextureDictionaryInvalid", "Each Normal owner has exactly one Base target.", out diagnostic);
                        hasBase = true;
                    }
                    else if (target.targetName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal) || !targetNames.Add(target.targetName))
                    {
                        return Fail("NormalTextureDictionaryInvalid", "Normal targets must have unique non-PBM target names.", out diagnostic);
                    }
                    var entryNames = new HashSet<string>(StringComparer.Ordinal);
                    for (int textureIndex = 0; textureIndex < target.textures.Count; textureIndex++)
                    {
                        MeshNormalTextureBindingEntry texture = target.textures[textureIndex];
                        if (texture == null || string.IsNullOrWhiteSpace(texture.entryName) || texture.normalTexture == null || !entryNames.Add(texture.entryName))
                            return Fail("NormalTextureDictionaryInvalid", "Normal texture entries require a texture and a unique entry name within each target.", out diagnostic);
                        if (entryOwnerIds.TryGetValue(texture.entryName, out string existingOwnerId) && existingOwnerId != registryId)
                            return Fail("NormalTextureDictionaryInvalid", "A Normal entry name must belong to exactly one Figure or Outfit owner.", out diagnostic, texture.entryName);
                        entryOwnerIds[texture.entryName] = registryId;
                    }
                }
                if (!hasBase) return Fail("NormalBaseTextureMissing", "Each Normal owner requires one Base target.", out diagnostic);
            }
            return true;
        }

        private static bool TryCreateNormalSource(HumanoidMeshSource owner, MeshNormalOwnerBindingEntry bindingOwner, string entryName, out HumanoidMeshNormalSource source, out StackMachineDiagnostic diagnostic)
        {
            source = null;
            diagnostic = null;
            Texture2D baseTexture = null;
            var targets = new List<HumanoidMeshNormalTargetSource>();
            for (int targetIndex = 0; targetIndex < bindingOwner.targets.Count; targetIndex++)
            {
                MeshNormalTargetBindingEntry target = bindingOwner.targets[targetIndex];
                MeshNormalTextureBindingEntry selected = null;
                for (int textureIndex = 0; textureIndex < target.textures.Count; textureIndex++)
                {
                    MeshNormalTextureBindingEntry candidate = target.textures[textureIndex];
                    if (candidate.entryName == entryName) { selected = candidate; break; }
                }
                if (string.IsNullOrEmpty(target.targetName))
                {
                    if (selected == null || selected.normalTexture == null)
                        return Fail("NormalBaseTextureMissing", "The Base Normal target requires a texture for every NORMAL entry.", out diagnostic, entryName, owner.RegistryId);
                    baseTexture = selected.normalTexture;
                }
                else if (selected != null)
                {
                    targets.Add(new HumanoidMeshNormalTargetSource(target.targetName, selected.normalTexture));
                }
            }
            if (baseTexture == null) return Fail("NormalBaseTextureMissing", "NORMAL entry requires a Base Normal texture.", out diagnostic, entryName, owner.RegistryId);
            source = new HumanoidMeshNormalSource(owner, entryName, baseTexture, targets.ToArray());
            return true;
        }

        private static bool ProxyContainsEntry(MaterialProxy proxy, string entryName)
        {
            IReadOnlyList<MaterialProxyEntry> entries = proxy.Entries;
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && entries[i].entryName == entryName) return true;
            return false;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message, bindingName: binding, detail: detail);
            return false;
        }

        private readonly struct OutfitDeclaration
        {
            public OutfitDeclaration(string logicalName, GameObject root, ShapeSyncOutfit outfit) { LogicalName = logicalName; Root = root; Outfit = outfit; }
            public string LogicalName { get; }
            public GameObject Root { get; }
            public ShapeSyncOutfit Outfit { get; }
        }
    }
}
