// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Base Mesh-domain recipe document. Applications may derive it to validate application metadata.</summary>
    [Serializable]
    public class MeshRecipeDocument : StackMachineRecipeDocument { }

    /// <summary>Non-serialized Figure-local resolution of Mesh recipe logical names.</summary>
    public sealed class MeshBindingContext : IStackMachineBindingContext
    {
        /// <summary>Resolved logical morph binding exposed by a Figure-local Mesh context.</summary>
        public readonly struct MorphBinding
        {
            /// <summary>Gets the recipe logical binding name.</summary>
            public readonly string LogicalName;
            /// <summary>Gets the DDB target name resolved from the binding template.</summary>
            public readonly string TargetName;
            /// <summary>Gets the resolved Figure-local DDB target.</summary>
            public readonly DynamicBoneBlendTarget Target;

            /// <summary>Creates one resolved Figure-local morph binding.</summary>
            /// <param name="logicalName">The recipe logical binding name.</param>
            /// <param name="targetName">The resolved DDB target name.</param>
            /// <param name="target">The resolved Figure-local DDB target.</param>
            public MorphBinding(string logicalName, string targetName, DynamicBoneBlendTarget target) { LogicalName = logicalName; TargetName = targetName; Target = target; }
        }

        /// <summary>Resolved logical Outfit binding exposed by a Figure-local Mesh context.</summary>
        public readonly struct OutfitBinding
        {
            /// <summary>Gets the recipe logical binding name.</summary>
            public readonly string LogicalName;
            /// <summary>Gets the resolved Outfit prefab root.</summary>
            public readonly ShapeSyncOutfit Outfit;
            /// <summary>Gets the Outfit registry identity resolved at context creation.</summary>
            public readonly string RegistryId;

            /// <summary>Creates one resolved Figure-local Outfit binding.</summary>
            /// <param name="logicalName">The recipe logical binding name.</param>
            /// <param name="outfit">The resolved Outfit prefab root.</param>
            public OutfitBinding(string logicalName, ShapeSyncOutfit outfit) { LogicalName = logicalName; Outfit = outfit; RegistryId = outfit == null ? null : outfit.RegistryId; }
        }

        /// <summary>Resolved logical Normal entry exposed by a Figure-local Mesh context.</summary>
        public readonly struct NormalEntry
        {
            /// <summary>Gets the Proxy entry name that owns the logical Normal binding.</summary>
            public readonly string LogicalName;

            /// <summary>Creates one resolved Figure-local Normal entry.</summary>
            /// <param name="logicalName">The Proxy entry name that owns the Normal binding.</param>
            public NormalEntry(string logicalName) { LogicalName = logicalName; }
        }
        private readonly Dictionary<string, int> handles;
        private readonly Dictionary<int, MorphBinding> morphs;
        private readonly Dictionary<int, OutfitBinding> outfits;
        private readonly Dictionary<int, NormalEntry> normals;
        private readonly IReadOnlyList<MorphBinding> morphBindings;
        /// <summary>Gets the Figure-local Dynamic Bone Blender resolved for this context.</summary>
        public DynamicBoneBlender DynamicBoneBlender { get; }
        /// <summary>Gets the Figure-local Outfit Attacher resolved for this context.</summary>
        public OutfitAttacher OutfitAttacher { get; }

        private MeshBindingContext(Dictionary<string, int> handles, Dictionary<int, MorphBinding> morphs, Dictionary<int, OutfitBinding> outfits, Dictionary<int, NormalEntry> normals, DynamicBoneBlender blender, OutfitAttacher attacher)
        { this.handles = handles; this.morphs = morphs; this.outfits = outfits; this.normals = normals; morphBindings = Array.AsReadOnly(new List<MorphBinding>(morphs.Values).ToArray()); DynamicBoneBlender = blender; OutfitAttacher = attacher; }
        /// <summary>Resolves a logical recipe name to its opaque Mesh binding handle.</summary>
        /// <param name="logicalName">The name without the Forth <c>$</c> prefix.</param><param name="handle">The resolved handle.</param><returns><see langword="true"/> when the name is bound.</returns>
        public bool TryGetHandle(string logicalName, out int handle) => handles.TryGetValue(logicalName, out handle);
        /// <summary>Resolves a handle as a morph binding.</summary>
        /// <param name="handle">The opaque Mesh binding handle.</param><param name="binding">The resolved morph binding.</param><returns><see langword="true"/> when the handle denotes a morph.</returns>
        public bool TryGetMorph(int handle, out MorphBinding binding) => morphs.TryGetValue(handle, out binding);
        /// <summary>Resolves a handle as an Outfit binding.</summary>
        /// <param name="handle">The opaque Mesh binding handle.</param><param name="binding">The resolved Outfit binding.</param><returns><see langword="true"/> when the handle denotes an Outfit.</returns>
        public bool TryGetOutfit(int handle, out OutfitBinding binding) => outfits.TryGetValue(handle, out binding);
        /// <summary>Resolves a handle as a Normal entry.</summary>
        /// <param name="handle">The opaque Mesh binding handle.</param>
        /// <param name="entry">The resolved Normal entry.</param>
        /// <returns><see langword="true"/> when the handle denotes a Normal entry.</returns>
        public bool TryGetNormal(int handle, out NormalEntry entry) => normals.TryGetValue(handle, out entry);
        internal IReadOnlyList<MorphBinding> MorphBindings => morphBindings;

        /// <summary>Builds and validates the non-serialized Figure-local Mesh binding lookup.</summary>
        /// <param name="template">The binding template assigned to the Figure.</param><param name="blender">The Figure's DDB component.</param><param name="attacher">The Figure's Outfit Attacher component.</param><param name="context">The constructed context on success.</param><param name="diagnostic">The structured validation failure on rejection.</param><returns><see langword="true"/> when all bindings are valid and unique.</returns>
        public static bool TryCreate(MeshBindingTemplate template, DynamicBoneBlender blender, OutfitAttacher attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic)
        {
            context = null; diagnostic = null;
            if (template == null || blender == null || attacher == null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "TemplateInvalid", "MeshBindingTemplate, DynamicBoneBlender, and OutfitAttacher are required."); return false; }
            if (blender.gameObject != attacher.gameObject) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "TemplateInvalid", "DynamicBoneBlender and OutfitAttacher must belong to the same Figure root."); return false; }
            var targetByName = new Dictionary<string, DynamicBoneBlendTarget>(StringComparer.Ordinal);
            IReadOnlyList<DynamicBoneBlendTarget> targets = blender.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                if (target == null || string.IsNullOrEmpty(target.blendName)) continue;
                if (targetByName.ContainsKey(target.blendName)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MorphAmbiguous", "DynamicBoneBlender has duplicate morph name.", bindingName: target.blendName); return false; }
                targetByName.Add(target.blendName, target);
            }
            var handles = new Dictionary<string, int>(StringComparer.Ordinal); var morphs = new Dictionary<int, MorphBinding>(); var outfits = new Dictionary<int, OutfitBinding>(); var normals = new Dictionary<int, NormalEntry>(); var mappedMorphNames = new HashSet<string>(StringComparer.Ordinal); var mappedRegistryIds = new HashSet<string>(StringComparer.Ordinal); var mappedNormals = new HashSet<string>(StringComparer.Ordinal); int next = 1;
            foreach (MorphEntry entry in template.Morphs)
            {
                if (entry == null || (string.IsNullOrWhiteSpace(entry.word) && string.IsNullOrWhiteSpace(entry.name))) continue;
                if (string.IsNullOrWhiteSpace(entry.word) || string.IsNullOrWhiteSpace(entry.name) || handles.ContainsKey(entry.word)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "DuplicateLogicalBinding", "Morph logical bindings must be complete and unique."); return false; }
                handles.Add(entry.word, next);
                if (!targetByName.TryGetValue(entry.name, out DynamicBoneBlendTarget target)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MorphNotFound", "Configured morph does not exist in DynamicBoneBlender.", bindingName: entry.word, detail: entry.name); return false; }
                if (!mappedMorphNames.Add(entry.name)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "DuplicateMorph", "MeshBindingTemplate maps one morph target more than once.", bindingName: entry.word, detail: entry.name); return false; }
                morphs.Add(next, new MorphBinding(entry.word, entry.name, target)); next++;
            }
            foreach (OutfitEntry entry in template.Outfits)
            {
                if (entry == null || (string.IsNullOrWhiteSpace(entry.word) && entry.obj == null)) continue;
                ShapeSyncOutfit outfit = entry == null || entry.obj == null ? null : entry.obj.GetComponent<ShapeSyncOutfit>();
                if (string.IsNullOrWhiteSpace(entry.word) || outfit == null || handles.ContainsKey(entry.word)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "TemplateInvalid", "Outfit bindings require a unique word and ShapeSyncOutfit prefab."); return false; }
                handles.Add(entry.word, next);
                if (string.IsNullOrEmpty(outfit.RegistryId) || !mappedRegistryIds.Add(outfit.RegistryId)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "DuplicateRegistryId", "MeshBindingTemplate Outfit registry IDs must be unique.", bindingName: entry.word, detail: outfit.RegistryId); return false; }
                outfits.Add(next, new OutfitBinding(entry.word, outfit)); next++;
            }
            Materials.NormalBlender normalBlender = blender.GetComponent<Materials.NormalBlender>();
            IReadOnlyList<string> configuredNormals = normalBlender != null ? normalBlender.Entries : Array.Empty<string>();
            if (configuredNormals.Count != 0 && (blender.GetComponent<Materials.MaterialProxy>() == null || blender.GetComponent<MeshStackMachine>() == null))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalEntryHostInvalid", "Normal entries require a co-located MaterialProxy and MeshStackMachine."); return false;
            }
            for (int i = 0; i < configuredNormals.Count; i++)
            {
                string entryName = configuredNormals[i];
                if (string.IsNullOrWhiteSpace(entryName) || handles.ContainsKey(entryName) || !mappedNormals.Add(entryName)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalEntryInvalid", "Each Figure-local Normal entry requires one unique Proxy entry name.", bindingName: entryName); return false; }
                handles.Add(entryName, next); normals.Add(next, new NormalEntry(entryName)); next++;
            }
            context = new MeshBindingContext(handles, morphs, outfits, normals, blender, attacher); return true;
        }

        /// <summary>Builds and validates a non-serialized Figure-local Mesh binding lookup from a Spec15.3 document binding.</summary>
        /// <param name="binding">The shared document Mesh binding.</param>
        /// <param name="blender">The Figure's DDB component.</param>
        /// <param name="attacher">The Figure's Outfit Attacher component.</param>
        /// <param name="context">The constructed context on success.</param>
        /// <param name="diagnostic">The structured validation failure on rejection.</param>
        /// <returns><see langword="true"/> when all bindings are valid and unique.</returns>
        public static bool TryCreate(MeshBinding binding, DynamicBoneBlender blender, OutfitAttacher attacher, out MeshBindingContext context, out StackMachineDiagnostic diagnostic)
        {
            context = null;
            diagnostic = null;
            if (binding == null || blender == null || attacher == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "BindingInvalid", "MeshBinding, DynamicBoneBlender, and OutfitAttacher are required.");
                return false;
            }
            if (blender.gameObject != attacher.gameObject)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "BindingInvalid", "DynamicBoneBlender and OutfitAttacher must belong to the same Figure root.");
                return false;
            }
            if (!TryValidateDocumentNormalOwners(binding, out diagnostic)) return false;

            var targetByName = new Dictionary<string, DynamicBoneBlendTarget>(StringComparer.Ordinal);
            IReadOnlyList<DynamicBoneBlendTarget> targets = blender.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                if (target == null || string.IsNullOrEmpty(target.blendName)) continue;
                if (targetByName.ContainsKey(target.blendName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MorphAmbiguous", "DynamicBoneBlender has duplicate morph name.", bindingName: target.blendName);
                    return false;
                }
                targetByName.Add(target.blendName, target);
            }

            var handles = new Dictionary<string, int>(StringComparer.Ordinal);
            var morphs = new Dictionary<int, MorphBinding>();
            var outfits = new Dictionary<int, OutfitBinding>();
            var normals = new Dictionary<int, NormalEntry>();
            var mappedMorphNames = new HashSet<string>(StringComparer.Ordinal);
            var mappedRegistryIds = new HashSet<string>(StringComparer.Ordinal);
            int next = 1;
            IReadOnlyList<MeshMorphBindingEntry> configuredMorphs = binding.Morphs;
            for (int i = 0; i < configuredMorphs.Count; i++)
            {
                MeshMorphBindingEntry entry = configuredMorphs[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.logicalName) || string.IsNullOrWhiteSpace(entry.targetName) || handles.ContainsKey(entry.logicalName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "DuplicateLogicalBinding", "MeshBinding morph logical bindings must be complete and unique.");
                    return false;
                }
                if (!targetByName.TryGetValue(entry.targetName, out DynamicBoneBlendTarget target))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MorphNotFound", "Configured morph does not exist in DynamicBoneBlender.", bindingName: entry.logicalName, detail: entry.targetName);
                    return false;
                }
                if (!mappedMorphNames.Add(entry.targetName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "DuplicateMorph", "MeshBinding maps one morph target more than once.", bindingName: entry.logicalName, detail: entry.targetName);
                    return false;
                }
                handles.Add(entry.logicalName, next);
                morphs.Add(next++, new MorphBinding(entry.logicalName, entry.targetName, target));
            }

            IReadOnlyList<MeshOutfitBindingEntry> configuredOutfits = binding.Outfits;
            for (int i = 0; i < configuredOutfits.Count; i++)
            {
                MeshOutfitBindingEntry entry = configuredOutfits[i];
                ShapeSyncOutfit outfit = entry == null || entry.outfitPrefab == null ? null : entry.outfitPrefab.GetComponent<ShapeSyncOutfit>();
                if (entry == null || string.IsNullOrWhiteSpace(entry.logicalName) || outfit == null || handles.ContainsKey(entry.logicalName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "BindingInvalid", "MeshBinding Outfit bindings require a unique word and ShapeSyncOutfit prefab.");
                    return false;
                }
                if (string.IsNullOrEmpty(outfit.RegistryId) || !mappedRegistryIds.Add(outfit.RegistryId))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "DuplicateRegistryId", "MeshBinding Outfit registry IDs must be unique.", bindingName: entry.logicalName, detail: outfit.RegistryId);
                    return false;
                }
                handles.Add(entry.logicalName, next);
                outfits.Add(next++, new OutfitBinding(entry.logicalName, outfit));
            }

            Materials.NormalBlender normalBlender = blender.GetComponent<Materials.NormalBlender>();
            IReadOnlyList<string> configuredNormals = normalBlender != null ? normalBlender.Entries : Array.Empty<string>();
            if (configuredNormals.Count != 0 && (blender.GetComponent<Materials.MaterialProxy>() == null || blender.GetComponent<MeshStackMachine>() == null))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalEntryHostInvalid", "Normal entries require a co-located MaterialProxy and MeshStackMachine.");
                return false;
            }
            var mappedNormals = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < configuredNormals.Count; i++)
            {
                string entryName = configuredNormals[i];
                if (string.IsNullOrWhiteSpace(entryName) || handles.ContainsKey(entryName) || !mappedNormals.Add(entryName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalEntryInvalid", "Each Figure-local Normal entry requires one unique Proxy entry name.", bindingName: entryName);
                    return false;
                }
                handles.Add(entryName, next);
                normals.Add(next++, new NormalEntry(entryName));
            }
            context = new MeshBindingContext(handles, morphs, outfits, normals, blender, attacher);
            return true;
        }

        private static bool TryValidateDocumentNormalOwners(MeshBinding binding, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var owners = new HashSet<string>(StringComparer.Ordinal);
            var entryOwnerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            IReadOnlyList<MeshNormalOwnerBindingEntry> normalOwners = binding.NormalOwners;
            for (int ownerIndex = 0; ownerIndex < normalOwners.Count; ownerIndex++)
            {
                MeshNormalOwnerBindingEntry owner = normalOwners[ownerIndex];
                if (owner == null || owner.targets == null || !owners.Add(owner.outfitRegistryId ?? string.Empty))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureDictionaryInvalid", "Normal owners must be complete and have unique Figure or Outfit RegistryIds.");
                    return false;
                }
                var targets = new HashSet<string>(StringComparer.Ordinal);
                bool hasBase = false;
                for (int targetIndex = 0; targetIndex < owner.targets.Count; targetIndex++)
                {
                    MeshNormalTargetBindingEntry target = owner.targets[targetIndex];
                    if (target == null || target.textures == null)
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureDictionaryInvalid", "Normal targets must include their texture lists.");
                        return false;
                    }
                    if (string.IsNullOrEmpty(target.targetName))
                    {
                        if (hasBase) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureDictionaryInvalid", "Each Normal owner has exactly one Base target."); return false; }
                        hasBase = true;
                    }
                    else if (target.targetName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal) || !targets.Add(target.targetName))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureDictionaryInvalid", "Normal targets must have unique non-PBM target names.");
                        return false;
                    }
                    var targetEntryNames = new HashSet<string>(StringComparer.Ordinal);
                    for (int textureIndex = 0; textureIndex < target.textures.Count; textureIndex++)
                    {
                        MeshNormalTextureBindingEntry texture = target.textures[textureIndex];
                        string ownerId = owner.outfitRegistryId ?? string.Empty;
                        if (texture == null || string.IsNullOrWhiteSpace(texture.entryName) || texture.normalTexture == null || !targetEntryNames.Add(texture.entryName))
                        {
                            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureDictionaryInvalid", "Normal texture entries require a texture and a unique entry name within each target.");
                            return false;
                        }
                        if (entryOwnerIds.TryGetValue(texture.entryName, out string existingOwnerId) && existingOwnerId != ownerId)
                        {
                            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTextureDictionaryInvalid", "A Normal entry name must belong to exactly one Figure or Outfit owner.", bindingName: texture.entryName);
                            return false;
                        }
                        entryOwnerIds[texture.entryName] = ownerId;
                    }
                }
                if (!hasBase)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalBaseTextureMissing", "Each Normal owner requires one Base target.");
                    return false;
                }
            }
            return true;
        }

    }

    /// <summary>Registers the closed Mesh StackMachine word set and validates its runtime evaluation boundary.</summary>
    public sealed class MeshWordSet : StackMachineWordSetBase
    {
        /// <summary>Token for staging an Outfit attach command.</summary>
        public const string OutfitAttach = "ATTACH";
        /// <summary>Token for staging an Outfit detach command.</summary>
        public const string OutfitDetach = "DETACH";
        /// <summary>Token for staging one logical FBM or PBM raw weight.</summary>
        public const string FbmSet = "FBM_SET";
        /// <summary>Token for staging zero weight for every DDB FBM and PBM target.</summary>
        public const string MorphReset = "MORPH_RESET";
        /// <summary>Token for staging detachment of the transaction-start Outfit snapshot.</summary>
        public const string DetachAll = "DETACH_ALL";
        /// <inheritdoc />
        protected override void RegisterWords(StackMachineWordRegistry registry)
        {
            registry.TryRegisterDomainWord(OutfitAttach, OutfitAttach, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, Array.Empty<StackMachineValueTag>()));
            registry.TryRegisterDomainWord(OutfitDetach, OutfitDetach, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle }, Array.Empty<StackMachineValueTag>()));
            registry.TryRegisterDomainWord(FbmSet, FbmSet, new StackMachineWordSignature(new[] { StackMachineValueTag.ResourceHandle, StackMachineValueTag.Number }, Array.Empty<StackMachineValueTag>()));
            registry.TryRegisterDomainWord(MorphReset, MorphReset, new StackMachineWordSignature(Array.Empty<StackMachineValueTag>(), Array.Empty<StackMachineValueTag>()));
            registry.TryRegisterDomainWord(DetachAll, DetachAll, new StackMachineWordSignature(Array.Empty<StackMachineValueTag>(), Array.Empty<StackMachineValueTag>()));
        }
        /// <inheritdoc />
        public override bool TryEvaluateWord(StackMachineInstruction instruction, StackMachineExecutionContext executionContext, out StackMachineDiagnostic diagnostic)
        { diagnostic = null; return instruction.WordId == OutfitAttach || instruction.WordId == OutfitDetach || instruction.WordId == FbmSet || instruction.WordId == MorphReset || instruction.WordId == DetachAll; }
    }

    internal sealed class MeshTransactionState
    {
        private sealed class MorphState
        {
            public DynamicBoneBlendTarget Target;
            public float Value;
            public bool Dirty;
        }

        private MeshBindingContext configuredContext;
        private readonly List<MorphState> morphs = new List<MorphState>();
        private readonly Dictionary<DynamicBoneBlendTarget, int> morphIndex = new Dictionary<DynamicBoneBlendTarget, int>();
        private readonly List<string> detachAllRegistrySnapshot = new List<string>();
        private bool commitStarted;
        public readonly List<OutfitAttacherDryRunCommand> OutfitQueue = new List<OutfitAttacherDryRunCommand>();

        public void Configure(MeshBindingContext context)
        {
            if (ReferenceEquals(configuredContext, context)) return;
            configuredContext = context;
            morphs.Clear(); morphIndex.Clear(); OutfitQueue.Clear();
            IReadOnlyList<DynamicBoneBlendTarget> targets = context.DynamicBoneBlender.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                if (target == null || morphIndex.ContainsKey(target)) continue;
                morphIndex.Add(target, morphs.Count);
                morphs.Add(new MorphState { Target = target, Value = target.weight, Dirty = false });
            }
        }

        public void Begin(MeshBindingContext context)
        {
            Configure(context);
            commitStarted = false;
            for (int i = 0; i < morphs.Count; i++) { morphs[i].Value = morphs[i].Target.weight; morphs[i].Dirty = false; }
            OutfitQueue.Clear();
            detachAllRegistrySnapshot.Clear();
            IReadOnlyList<AttachedOutfitRegistrySet> attachedOutfits = context.OutfitAttacher.AttachedOutfits;
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                string registryId = attachedOutfits[i] == null ? null : attachedOutfits[i].RegistryId;
                if (!string.IsNullOrEmpty(registryId)) detachAllRegistrySnapshot.Add(registryId);
            }
        }

        public bool TrySet(DynamicBoneBlendTarget target, float value)
        {
            if (!morphIndex.TryGetValue(target, out int index)) return false;
            morphs[index].Value = value; morphs[index].Dirty = true; return true;
        }

        public void ResetAllMorphs()
        {
            for (int i = 0; i < morphs.Count; i++) { morphs[i].Value = 0f; morphs[i].Dirty = true; }
        }

        public int StageDetachAll()
        {
            int staged = 0;
            for (int i = 0; i < detachAllRegistrySnapshot.Count; i++)
            {
                string registryId = detachAllRegistrySnapshot[i];
                bool alreadyQueued = false;
                for (int commandIndex = 0; commandIndex < OutfitQueue.Count; commandIndex++)
                {
                    OutfitAttacherDryRunCommand command = OutfitQueue[commandIndex];
                    if (!command.Attach && command.RegistryId == registryId) { alreadyQueued = true; break; }
                }
                if (alreadyQueued) continue;
                OutfitQueue.Add(OutfitAttacherDryRunCommand.ForDetach(registryId, null));
                staged++;
            }
            return staged;
        }

        public void CommitMorphs()
        {
            for (int i = 0; i < morphs.Count; i++) if (morphs[i].Dirty) morphs[i].Target.weight = morphs[i].Value;
        }

        public void Discard()
        {
            for (int i = 0; i < morphs.Count; i++) morphs[i].Dirty = false;
            OutfitQueue.Clear();
        }

        public void Complete() { Discard(); }
        public void BeginCommit() { commitStarted = true; }
        public bool CanRollback => !commitStarted;
    }

    /// <summary>Immutable Mesh-domain execution artifact compiled from common StackMachine bytecode.</summary>
    public sealed class MeshExecutionPlan : IStackMachineDomainPlan
    {
        private readonly int[] bindingHandlesByInstruction;
        /// <summary>Gets the immutable common StackMachine plan used by this domain artifact.</summary>
        public StackMachinePlan CommonPlan { get; }
        internal MeshExecutionPlan(StackMachinePlan plan, int[] bindingHandlesByInstruction) { CommonPlan = plan; this.bindingHandlesByInstruction = bindingHandlesByInstruction; }
        internal bool TryGetBindingHandle(int instructionPointer, out int handle)
        {
            handle = -1;
            return instructionPointer >= 0 && instructionPointer < bindingHandlesByInstruction.Length && (handle = bindingHandlesByInstruction[instructionPointer]) >= 0;
        }
    }

    /// <summary>Bridges a detached Mesh Core plan into the closed runtime Mesh executor.</summary>
    internal static class MeshRuntimeCoreAdapter
    {
        internal static bool TryCompile(MeshStackMachineCorePlan corePlan, MeshExecutor executor, MeshBindingContext context, out IStackMachineDomainPlan runtimePlan, out StackMachineDiagnostic diagnostic)
        {
            runtimePlan = null;
            diagnostic = null;
            if (corePlan == null || executor == null || context == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "TemplateInvalid", "Mesh Core plan, executor, and binding context are required.");
                return false;
            }
            return executor.TryCompileDomainPlan(corePlan.CommonPlan, context, out runtimePlan, out diagnostic);
        }
    }

    /// <summary>Mesh bytecode transaction executor. It stages all effects until the final commit boundary.</summary>
    public sealed class MeshExecutor : StackMachineDomainExecutorBase
    {
        private readonly MeshWordSet wordSet;
        private readonly MeshTransactionState transactionState = new MeshTransactionState();
        private readonly Func<IReadOnlyList<OutfitAttacherDryRunCommand>, OutfitAttacherDryRunResult> dryRunOverride;
        private readonly Func<OutfitAttacherDryRunCommand, bool> commitOutfitOverride;

        /// <summary>Initializes a Mesh executor with the default word set or a caller-supplied word set.</summary>
        /// <param name="wordSet">The Mesh word set to evaluate. When <see langword="null"/>, the default set is used.</param>
        public MeshExecutor(MeshWordSet wordSet = null) { this.wordSet = wordSet ?? new MeshWordSet(); }
        internal MeshExecutor(
            Func<IReadOnlyList<OutfitAttacherDryRunCommand>, OutfitAttacherDryRunResult> dryRunOverride,
            Func<OutfitAttacherDryRunCommand, bool> commitOutfitOverride)
        {
            wordSet = new MeshWordSet();
            this.dryRunOverride = dryRunOverride;
            this.commitOutfitOverride = commitOutfitOverride;
        }
        internal void InitializeTransactionState(MeshBindingContext context) { transactionState.Configure(context); }
        /// <inheritdoc />
        public override StackMachineDomainExecutionMode ExecutionMode => StackMachineDomainExecutionMode.BytecodeTransaction;
        /// <inheritdoc />
        public override bool TryCompileDomainPlan(StackMachinePlan plan, IStackMachineBindingContext bindingContext, out IStackMachineDomainPlan domainPlan, out StackMachineDiagnostic diagnostic)
        {
            domainPlan = null; diagnostic = null;
            if (!(bindingContext is MeshBindingContext context) || plan == null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "TemplateInvalid", "Mesh binding context and common plan are required."); return false; }
            var symbolic = new List<string>(); var bindingHandlesByInstruction = new int[plan.Instructions.Count]; Array.Fill(bindingHandlesByInstruction, -1); var seenMorphs = new HashSet<string>(StringComparer.Ordinal); var seenRegistryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int ip = 0; ip < plan.Instructions.Count; ip++)
            {
                StackMachineInstruction instruction = plan.Instructions[ip];
                if (instruction.Op == StackMachineOp.PushBinding)
                {
                    if (!context.TryGetHandle(instruction.Binding, out int bindingHandle)) { diagnostic = Failure("TemplateInvalid", "Logical binding is not in the MeshBindingTemplate.", ip, binding: instruction.Binding); return false; }
                    bindingHandlesByInstruction[ip] = bindingHandle;
                    symbolic.Add(instruction.Binding);
                    continue;
                }
                if (instruction.Op == StackMachineOp.PushNumber || instruction.Op == StackMachineOp.PushBoolean) { symbolic.Add(null); continue; }
                if (instruction.Op != StackMachineOp.DomainWord) { ApplySymbolicStack(instruction.Op, symbolic); continue; }
                if (instruction.WordId == MeshWordSet.MorphReset || instruction.WordId == MeshWordSet.DetachAll) continue;
                int need = instruction.Signature.Inputs.Count; string binding = symbolic.Count >= need ? symbolic[symbolic.Count - need] : null;
                if (symbolic.Count >= need) symbolic.RemoveRange(symbolic.Count - need, need);
                if (binding == null || !context.TryGetHandle(binding, out int handle)) { diagnostic = Failure("TemplateInvalid", "Domain word binding cannot be resolved.", ip, instruction.WordId, binding); return false; }
                if (instruction.WordId == MeshWordSet.FbmSet)
                {
                    if (!context.TryGetMorph(handle, out MeshBindingContext.MorphBinding morph)) { diagnostic = Failure("TemplateInvalid", "FBM_SET requires a morph binding.", ip, instruction.WordId, binding); return false; }
                    if (!seenMorphs.Add(morph.TargetName)) { diagnostic = Failure("DuplicateMorph", "The recipe writes the same morph more than once.", ip, instruction.WordId, binding, morph.TargetName); return false; }
                }
                else if (instruction.WordId == MeshWordSet.OutfitAttach || instruction.WordId == MeshWordSet.OutfitDetach)
                {
                    if (!context.TryGetOutfit(handle, out MeshBindingContext.OutfitBinding outfit)) { diagnostic = Failure("TemplateInvalid", "Outfit word requires an outfit binding.", ip, instruction.WordId, binding); return false; }
                    if (!seenRegistryIds.Add(outfit.RegistryId)) { diagnostic = Failure("DuplicateRegistryId", "The recipe references the same Outfit registry more than once.", ip, instruction.WordId, binding, outfit.RegistryId); return false; }
                }
                else { diagnostic = Failure("TemplateInvalid", "Unsupported Mesh word.", ip, instruction.WordId, binding); return false; }
            }
            domainPlan = new MeshExecutionPlan(plan, bindingHandlesByInstruction); return true;
        }

        /// <inheritdoc />
        protected override bool TryExecuteDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!(domainPlan is MeshExecutionPlan meshPlan) || !(bindingContext is MeshBindingContext context)) { diagnostic = Failure("TemplateInvalid", "Mesh execution plan and context are required."); return false; }
            var stack = new List<StackMachineValue>();
            transactionState.Begin(context);
            for (int ip = 0; ip < meshPlan.CommonPlan.Instructions.Count; ip++)
            {
                result.InstructionPointer = ip; StackMachineInstruction instruction = meshPlan.CommonPlan.Instructions[ip];
                if (!TryExecute(instruction, meshPlan, context, stack, transactionState, ip, out diagnostic)) { transactionState.Discard(); result.DataStack = Array.AsReadOnly(stack.ToArray()); return false; }
            }
            try
            {
                transactionState.BeginCommit();
                transactionState.CommitMorphs();
                for (int i = 0; i < transactionState.OutfitQueue.Count; i++)
                {
                    OutfitAttacherDryRunCommand command = transactionState.OutfitQueue[i]; bool ok = commitOutfitOverride != null ? commitOutfitOverride(command) : command.Attach ? context.OutfitAttacher.TryAttach(command.Outfit) : context.OutfitAttacher.Detach(command.RegistryId);
                    if (!ok) { transactionState.Discard(); diagnostic = Failure(command.Attach ? "CommitAttachFailed" : "CommitDetachFailed", "Outfit command failed after successful dry-run.", -1, command.Attach ? MeshWordSet.OutfitAttach : MeshWordSet.OutfitDetach, command.LogicalBinding, command.RegistryId); return false; }
                }
            }
            catch (Exception exception)
            {
                transactionState.Discard();
                diagnostic = Failure("CommitUnexpectedFailure", "Mesh commit threw an unexpected exception.", detail: exception.Message);
                return false;
            }
            transactionState.Complete();
            result.DataStack = Array.AsReadOnly(stack.ToArray()); return true;
        }

        /// <inheritdoc />
        protected override bool TryRollbackDomainPlan(IStackMachineDomainPlan domainPlan, IStackMachineBindingContext bindingContext, StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!transactionState.CanRollback) return false;
            transactionState.Discard();
            return true;
        }

        private bool TryExecute(StackMachineInstruction instruction, MeshExecutionPlan plan, MeshBindingContext context, List<StackMachineValue> stack, MeshTransactionState transaction, int ip, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            switch (instruction.Op)
            {
                case StackMachineOp.PushNumber: stack.Add(StackMachineValue.FromNumber(instruction.Number)); return true;
                case StackMachineOp.PushBoolean: stack.Add(StackMachineValue.FromBoolean(instruction.Boolean)); return true;
                case StackMachineOp.PushBinding:
                    if (plan.TryGetBindingHandle(ip, out int handle)) { stack.Add(StackMachineValue.FromResourceHandle(handle)); return true; }
                    diagnostic = Failure("TemplateInvalid", "Logical binding is not in the MeshBindingTemplate.", ip, binding: instruction.Binding); return false;
                case StackMachineOp.DomainWord: return TryExecuteDomain(instruction, context, stack, transaction, ip, out diagnostic);
                default: return TryExecuteBuiltIn(instruction.Op, stack, ip, out diagnostic);
            }
        }

        private bool TryExecuteDomain(StackMachineInstruction instruction, MeshBindingContext context, List<StackMachineValue> stack, MeshTransactionState transaction, int ip, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!wordSet.TryEvaluateWord(instruction, new StackMachineExecutionContext(stack), out diagnostic))
            {
                diagnostic = diagnostic ?? Failure("TemplateInvalid", "Mesh word evaluator rejected the instruction.", ip, instruction.WordId);
                return false;
            }
            if (instruction.WordId == MeshWordSet.FbmSet)
            {
                if (!Pop(stack, StackMachineValueTag.Number, ip, out StackMachineValue number, out diagnostic) || !Pop(stack, StackMachineValueTag.ResourceHandle, ip, out StackMachineValue resource, out diagnostic)) return false;
                if (!context.TryGetMorph(resource.Handle, out MeshBindingContext.MorphBinding morph)) { diagnostic = Failure("TemplateInvalid", "FBM_SET received a non-morph handle.", ip, instruction.WordId); return false; }
                if (double.IsNaN(number.Number) || double.IsInfinity(number.Number) || number.Number < float.MinValue || number.Number > float.MaxValue) { diagnostic = Failure("InvalidMorphWeight", "Morph weight must be finite float range.", ip, instruction.WordId, morph.LogicalName, morph.TargetName); return false; }
                if (!transaction.TrySet(morph.Target, (float)number.Number)) { diagnostic = Failure("MorphNotFound", "Morph target is not in the initialized transaction state.", ip, instruction.WordId, morph.LogicalName, morph.TargetName); return false; }
                return true;
            }
            if (instruction.WordId == MeshWordSet.MorphReset)
            {
                transaction.ResetAllMorphs();
                return true;
            }
            if (instruction.WordId == MeshWordSet.DetachAll)
            {
                int queueCount = transaction.OutfitQueue.Count;
                if (transaction.StageDetachAll() == 0) return true;
                if (TryDryRun(context, transaction.OutfitQueue, out OutfitAttacherDryRunResult detachAllDryRun)) return true;
                transaction.OutfitQueue.RemoveRange(queueCount, transaction.OutfitQueue.Count - queueCount);
                diagnostic = Failure("OutfitDryRunRejected", detachAllDryRun.Detail, ip, instruction.WordId, detail: detachAllDryRun.Code + "; registryId=" + detachAllDryRun.RegistryId);
                return false;
            }
            if (!Pop(stack, StackMachineValueTag.ResourceHandle, ip, out StackMachineValue outfitValue, out diagnostic)) return false;
            if (!context.TryGetOutfit(outfitValue.Handle, out MeshBindingContext.OutfitBinding outfit)) { diagnostic = Failure("TemplateInvalid", "Outfit word received a non-outfit handle.", ip, instruction.WordId); return false; }
            OutfitAttacherDryRunCommand command = instruction.WordId == MeshWordSet.OutfitAttach ? OutfitAttacherDryRunCommand.ForAttach(outfit.Outfit, outfit.LogicalName) : instruction.WordId == MeshWordSet.OutfitDetach ? OutfitAttacherDryRunCommand.ForDetach(outfit.RegistryId, outfit.LogicalName) : default;
            if (instruction.WordId != MeshWordSet.OutfitAttach && instruction.WordId != MeshWordSet.OutfitDetach) { diagnostic = Failure("TemplateInvalid", "Unsupported Mesh word.", ip, instruction.WordId, outfit.LogicalName); return false; }
            transaction.OutfitQueue.Add(command);
            if (!TryDryRun(context, transaction.OutfitQueue, out OutfitAttacherDryRunResult dryRun)) { transaction.OutfitQueue.RemoveAt(transaction.OutfitQueue.Count - 1); diagnostic = Failure("OutfitDryRunRejected", dryRun.Detail, ip, instruction.WordId, dryRun.LogicalBinding, dryRun.Code + "; registryId=" + dryRun.RegistryId); return false; }
            return true;
        }

        private bool TryDryRun(MeshBindingContext context, IReadOnlyList<OutfitAttacherDryRunCommand> commands, out OutfitAttacherDryRunResult result)
        {
            if (dryRunOverride != null)
            {
                result = dryRunOverride(commands);
                return result.Succeeded;
            }
            return context.OutfitAttacher.TryDryRun(commands, out result);
        }

        private static bool TryExecuteBuiltIn(StackMachineOp op, List<StackMachineValue> s, int ip, out StackMachineDiagnostic d)
        {
            d = null;
            if (op == StackMachineOp.Depth) { s.Add(StackMachineValue.FromNumber(s.Count)); return true; }
            if (op == StackMachineOp.Drop) { if (!Need(s, 1, ip, out d)) return false; s.RemoveAt(s.Count - 1); return true; }
            if (op == StackMachineOp.Dup) { if (!Need(s, 1, ip, out d)) return false; s.Add(s[s.Count - 1]); return true; }
            if (op == StackMachineOp.Swap) { if (!Need(s, 2, ip, out d)) return false; int n = s.Count - 1; StackMachineValue x = s[n]; s[n] = s[n - 1]; s[n - 1] = x; return true; }
            if (op == StackMachineOp.Over) { if (!Need(s, 2, ip, out d)) return false; s.Add(s[s.Count - 2]); return true; }
            if (op == StackMachineOp.Rot) { if (!Need(s, 3, ip, out d)) return false; int n = s.Count - 3; StackMachineValue x = s[n]; s[n] = s[n + 1]; s[n + 1] = s[n + 2]; s[n + 2] = x; return true; }
            if (op == StackMachineOp.Call || op == StackMachineOp.Exit) { d = Failure("TemplateInvalid", "Mesh recipes do not support control-flow instructions.", ip); return false; }
            if (op == StackMachineOp.Not) { if (!Pop(s, StackMachineValueTag.Boolean, ip, out StackMachineValue v, out d)) return false; s.Add(StackMachineValue.FromBoolean(!v.Boolean)); return true; }
            if (op == StackMachineOp.And || op == StackMachineOp.Or || op == StackMachineOp.Xor) { if (!Pop(s, StackMachineValueTag.Boolean, ip, out StackMachineValue right, out d) || !Pop(s, StackMachineValueTag.Boolean, ip, out StackMachineValue left, out d)) return false; s.Add(StackMachineValue.FromBoolean(op == StackMachineOp.And ? left.Boolean && right.Boolean : op == StackMachineOp.Or ? left.Boolean || right.Boolean : left.Boolean ^ right.Boolean)); return true; }
            if (op == StackMachineOp.Equal) { if (!Need(s, 2, ip, out d)) return false; int n = s.Count - 1; StackMachineValue right = s[n], left = s[n - 1]; if (left.Tag != right.Tag || (left.Tag != StackMachineValueTag.Number && left.Tag != StackMachineValueTag.Boolean)) { d = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch, "= requires two Numbers or two Booleans.", ip); return false; } s.RemoveRange(n - 1, 2); s.Add(StackMachineValue.FromBoolean(left.Tag == StackMachineValueTag.Number ? left.Number == right.Number : left.Boolean == right.Boolean)); return true; }
            if (!Pop(s, StackMachineValueTag.Number, ip, out StackMachineValue b, out d)) return false;
            if (op == StackMachineOp.Negate || op == StackMachineOp.Abs || op == StackMachineOp.Increment || op == StackMachineOp.Decrement || op == StackMachineOp.Double || op == StackMachineOp.Halve || op == StackMachineOp.ZeroEqual || op == StackMachineOp.ZeroLess)
            { double a = b.Number; if (op == StackMachineOp.ZeroEqual || op == StackMachineOp.ZeroLess) s.Add(StackMachineValue.FromBoolean(op == StackMachineOp.ZeroEqual ? a == 0 : a < 0)); else if (!PushFinite(s, op == StackMachineOp.Negate ? -a : op == StackMachineOp.Abs ? Math.Abs(a) : op == StackMachineOp.Increment ? a + 1 : op == StackMachineOp.Decrement ? a - 1 : op == StackMachineOp.Double ? a * 2 : a / 2, ip, out d)) return false; return true; }
            if (!Pop(s, StackMachineValueTag.Number, ip, out StackMachineValue aValue, out d)) return false;
            double leftNumber = aValue.Number, rightNumber = b.Number; if (op == StackMachineOp.Divide && rightNumber == 0) { d = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.DivideByZero, "Division by zero.", ip); return false; }
            if (op == StackMachineOp.Less || op == StackMachineOp.Greater) { s.Add(StackMachineValue.FromBoolean(op == StackMachineOp.Less ? leftNumber < rightNumber : leftNumber > rightNumber)); return true; }
            return PushFinite(s, op == StackMachineOp.Add ? leftNumber + rightNumber : op == StackMachineOp.Subtract ? leftNumber - rightNumber : op == StackMachineOp.Multiply ? leftNumber * rightNumber : op == StackMachineOp.Divide ? leftNumber / rightNumber : op == StackMachineOp.Min ? Math.Min(leftNumber, rightNumber) : Math.Max(leftNumber, rightNumber), ip, out d);
        }

        private static bool Pop(List<StackMachineValue> stack, StackMachineValueTag tag, int ip, out StackMachineValue value, out StackMachineDiagnostic diagnostic) { value = default; if (!Need(stack, 1, ip, out diagnostic)) return false; int n = stack.Count - 1; value = stack[n]; if (value.Tag != tag) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.TypeMismatch, tag + " required.", ip); return false; } stack.RemoveAt(n); return true; }
        private static bool Need(List<StackMachineValue> stack, int count, int ip, out StackMachineDiagnostic diagnostic) { diagnostic = null; if (stack.Count >= count) return true; diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.StackUnderflow, "Data stack underflow.", ip); return false; }
        private static bool PushFinite(List<StackMachineValue> stack, double number, int ip, out StackMachineDiagnostic diagnostic) { diagnostic = null; if (double.IsNaN(number) || double.IsInfinity(number)) { diagnostic = StackMachineDiagnostic.Create(StackMachineDiagnosticCode.NonFiniteNumber, "Non-finite numeric result.", ip); return false; } stack.Add(StackMachineValue.FromNumber(number)); return true; }
        private static StackMachineDiagnostic Failure(string code, string message, int ip = -1, string wordId = null, string binding = null, string detail = null) => StackMachineDiagnostic.CreateDomain("mesh", code, message, instructionPointer: ip, wordId: wordId, bindingName: binding, detail: detail);
        private static void ApplySymbolicStack(StackMachineOp op, List<string> s) { if (op == StackMachineOp.Drop && s.Count > 0) s.RemoveAt(s.Count - 1); else if (op == StackMachineOp.Dup && s.Count > 0) s.Add(s[s.Count - 1]); else if (op == StackMachineOp.Swap && s.Count > 1) { int n = s.Count - 1; string x = s[n]; s[n] = s[n - 1]; s[n - 1] = x; } else if (op == StackMachineOp.Over && s.Count > 1) s.Add(s[s.Count - 2]); else if (op == StackMachineOp.Rot && s.Count > 2) { int n = s.Count - 3; string x = s[n]; s[n] = s[n + 1]; s[n + 1] = s[n + 2]; s[n + 2] = x; } else if (op != StackMachineOp.Depth) { int remove = op == StackMachineOp.Not || op == StackMachineOp.Negate || op == StackMachineOp.Abs || op == StackMachineOp.Increment || op == StackMachineOp.Decrement || op == StackMachineOp.Double || op == StackMachineOp.Halve || op == StackMachineOp.ZeroEqual || op == StackMachineOp.ZeroLess ? 1 : 2; if (s.Count >= remove) { s.RemoveRange(s.Count - remove, remove); s.Add(null); } } else s.Add(null); }
    }

    /// <summary>Figure component and the sole public ingress for Mesh StackMachine recipes.</summary>
    /// <remarks>
    /// Mesh transaction ownership remains in this component. When a Normal recipe requires Texture execution, it resolves the active
    /// scene Host through <see cref="TextureStaticMachineFactory"/> and passes that explicit Host to the low-level Texture executor.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MeshStackMachine : MonoBehaviour
    {
        private const string DocumentNormalTemplateSource = "$base CANVAS NORMAL_BASE NORMAL_FINALIZE";
        [SerializeField] private MeshBindingTemplate bindingTemplate;
        private readonly MeshWordSet wordSet = new MeshWordSet();
        private readonly MeshExecutor executor = new MeshExecutor();
        private MeshBindingContext bindingContext;
        private MeshBinding documentBinding;
        private string documentNormalOwnerRegistryId;
        private readonly Dictionary<string, NormalRecipeTemplate> normalTemplates = new Dictionary<string, NormalRecipeTemplate>(StringComparer.Ordinal);
        private bool ready;
        private bool warned;

        internal bool HasNormalTextureWork => TextureStaticMachineFactory.TryGetExistingTSM(out TextureStackMachineHost host) && (host.HasSubmittedRequest || host.PendingRequestCount != 0);

        private void Awake() { BuildBindingContext(); }
        private void Start()
        {
            if (bindingContext == null) return;
            executor.InitializeTransactionState(bindingContext);
            ready = true;
        }

        /// <summary>Ensures the legacy Figure-local Mesh route has constructed its binding context.</summary>
        /// <param name="diagnostic">A structured readiness failure.</param>
        /// <returns><see langword="true"/> when this Machine is ready for its configured route.</returns>
        public bool TryEnsureReady(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (ready) return true;
            BuildBindingContext();
            if (bindingContext == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MachineNotReady", "MeshStackMachine could not construct its configured binding context.");
                return false;
            }
            executor.InitializeTransactionState(bindingContext);
            ready = true;
            return true;
        }

        /// <summary>Ensures this Machine is ready for a Spec15.3 document Mesh binding.</summary>
        /// <param name="binding">The shared Mesh binding that the Machine must resolve.</param>
        /// <param name="diagnostic">A structured readiness or configuration failure.</param>
        /// <returns><see langword="true"/> when the document binding has a valid Figure-local context.</returns>
        public bool TryEnsureReady(MeshBinding binding, out StackMachineDiagnostic diagnostic)
        {
            return TryConfigureDocumentBinding(binding, out diagnostic);
        }

        /// <summary>Accepts one Spec15.3 Mesh payload using its shared document binding.</summary>
        /// <param name="payload">The Figure-owned full document or a target-local payload.</param>
        /// <param name="result">The Mesh execution result when the Mesh domain is non-empty.</param>
        /// <param name="diagnostic">A structured configuration or execution rejection.</param>
        /// <returns><see langword="true"/> when an empty payload no-ops or the Mesh transaction succeeds.</returns>
        public bool TryAcceptRecipePayload(ShapeSyncDocument payload, out StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (payload == null) return Fail(StackMachineDiagnostic.CreateDomain("mesh", "PayloadRequired", "MeshStackMachine requires a ShapeSyncDocument payload."), out result);
            if (payload.MeshRecipe == null || string.IsNullOrWhiteSpace(payload.MeshRecipe.wordSource))
            {
                result = new StackMachineExecutionResult { Stage = StackMachineTransactionStage.Succeeded };
                result.Lifecycle.Add(StackMachineTransactionStage.Started);
                result.Lifecycle.Add(StackMachineTransactionStage.Succeeded);
                return true;
            }
            if (TryAcceptOutfitNormalPayload(payload, out result, out diagnostic)) return result != null && result.Stage == StackMachineTransactionStage.Succeeded;
            if (diagnostic != null) return Fail(diagnostic, out result);
            if (!TryConfigureDocumentBinding(payload.MeshBinding, out diagnostic)) return Fail(diagnostic, out result);
            MeshRecipeDocument runtimeDocument = CreateRuntimeDocument(payload.MeshRecipe, payload.MeshBinding);
            bool accepted = TryExecute(runtimeDocument, out result);
            diagnostic = result == null ? null : result.Diagnostic;
            if (accepted && !TryDispatchOutfitNormalPayloads(payload, out diagnostic)) return Fail(diagnostic, out result);
            return accepted;
        }
        /// <summary>Compiles and executes Mesh recipe source using this Figure's binding template.</summary>
        /// <param name="wordSource">Canonical Forth source containing Mesh words and logical bindings.</param><param name="result">The execution lifecycle and diagnostic result.</param><returns><see langword="true"/> when the transaction commits successfully.</returns>
        public bool TryExecute(string wordSource, out StackMachineExecutionResult result)
        {
            var document = new MeshRecipeDocument { wordSource = wordSource };
            if (bindingTemplate != null)
            {
                AddBindingDeclarations(document, bindingTemplate.Morphs);
                AddBindingDeclarations(document, bindingTemplate.Outfits);
            }
            return TryExecute(document, out result);
        }
        /// <summary>Deserializes, compiles, and executes a Mesh recipe carrier.</summary>
        /// <param name="asset">The serialized Mesh recipe carrier.</param><param name="result">The execution lifecycle and diagnostic result.</param><returns><see langword="true"/> when the transaction commits successfully.</returns>
        public bool TryExecute(MeshRecipeAsset asset, out StackMachineExecutionResult result)
        {
            if (asset == null) return Fail("TemplateInvalid", "Mesh recipe asset is null.", out result);
            MeshRecipeDocument document = asset.ToDocument() as MeshRecipeDocument;
            return document == null ? Fail("TemplateInvalid", "Mesh recipe asset did not produce a MeshRecipeDocument.", out result) : TryExecute(document, out result);
        }
        /// <summary>Compiles and executes an already materialized Mesh recipe document.</summary>
        /// <param name="document">The document to validate and execute.</param><param name="result">The execution lifecycle and diagnostic result.</param><returns><see langword="true"/> when the transaction commits successfully.</returns>
        public bool TryExecute(MeshRecipeDocument document, out StackMachineExecutionResult result)
        {
            result = null;
            if (!ready) return Fail("TemplateInvalid", "MeshStackMachine is not ready.", out result);
            StackMachineDiagnostic diagnostic = null;
            if (document == null || !MeshNormalBlockParser.TryExtract(document.wordSource, out string meshWordSource, out IReadOnlyList<NormalRecipeTemplate> templates, out diagnostic)) return Fail(diagnostic ?? StackMachineDiagnostic.CreateDomain("mesh", "NormalBlockInvalid", "Mesh Normal block extraction failed."), out result);
            for (int i = 0; i < templates.Count; i++)
            {
                if (!bindingContext.TryGetHandle(templates[i].EntryName, out int handle) || !bindingContext.TryGetNormal(handle, out _)) return Fail(StackMachineDiagnostic.CreateDomain("mesh", "NormalEntryInvalid", "NORMAL requires a Figure-local NormalBlender entry for its Proxy entry.", bindingName: templates[i].EntryName), out result);
            }
            if (string.IsNullOrWhiteSpace(meshWordSource))
            {
                StoreNormalTemplates(templates);
                result = new StackMachineExecutionResult { Stage = StackMachineTransactionStage.Succeeded };
                result.Lifecycle.Add(StackMachineTransactionStage.Started); result.Lifecycle.Add(StackMachineTransactionStage.Succeeded);
                return true;
            }
            if (!MeshStackMachineCorePlan.TryCreateRuntime(document, out MeshStackMachineCorePlan corePlan, out diagnostic)) return Fail(diagnostic, out result);
            if (!MeshRuntimeCoreAdapter.TryCompile(corePlan, executor, bindingContext, out IStackMachineDomainPlan meshPlan, out diagnostic)) return Fail(diagnostic, out result);
            if (!executor.TryExecute(meshPlan, bindingContext, out result)) return false;
            StoreNormalTemplates(templates);
            return true;
        }
        /// <summary>Resolves the latest Mesh-owned Normal template registered for one logical Normal entry.</summary>
        /// <param name="entryName">The logical Proxy entry name requesting construction.</param>
        /// <param name="template">The registered detached template.</param>
        /// <param name="diagnostic">A structured failure when no template is registered.</param>
        /// <returns><see langword="true"/> when the entry has a registered template.</returns>
        public bool TryGetNormalTemplate(string entryName, out NormalRecipeTemplate template, out StackMachineDiagnostic diagnostic)
        {
            if (!string.IsNullOrWhiteSpace(entryName) && normalTemplates.TryGetValue(entryName, out template)) { diagnostic = null; return true; }
            template = null;
            diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTemplateMissing", "No NORMAL template is registered for this NormalBlender entry.", bindingName: entryName);
            return false;
        }
        /// <summary>Builds one Normal Texture recipe from the Mesh-owned Base and target texture dictionary and current DDB snapshot.</summary>
        /// <param name="entryName">The configured Proxy entry name.</param>
        /// <param name="snapshot">The latest raw non-PBM DDB weight snapshot.</param>
        /// <param name="stub">The detached Texture StackMachine recipe on success.</param>
        /// <param name="diagnostic">The structured rejection diagnostic on failure.</param>
        /// <returns><see langword="true"/> when the binding and its texture dictionary produce a valid recipe.</returns>
        public bool TryBuildNormalRecipe(string entryName, IReadOnlyList<Materials.NormalTargetWeight> snapshot, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic)
        {
            stub = null;
            NormalRecipeTemplate template;
            if (documentBinding != null)
            {
                if (bindingContext == null || !bindingContext.TryGetHandle(entryName, out int handle) || !bindingContext.TryGetNormal(handle, out _))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("normal", "NormalEntryInvalid", "MeshBinding does not own the requested NormalBlender entry.", bindingName: entryName);
                    return false;
                }
                template = new NormalRecipeTemplate(entryName, DocumentNormalTemplateSource);
            }
            else if (!TryGetNormalTemplate(entryName, out template, out diagnostic)) return false;
            if (!TryResolveNormalTextures(template.EntryName, out Texture2D baseTexture, out List<NormalTargetSource> targets, out diagnostic)) return false;
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(baseTexture.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(baseTexture.height))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("normal", "BaseTextureExtentUnsupported", "Normal Base texture width and height must be supported power-of-two extents.");
                return false;
            }
            return NormalRecipeExpander.TryCreateStub(template, baseTexture, targets, snapshot, out stub, out diagnostic);
        }
        /// <summary>Builds and queues one Normal-entry recipe on the scene Texture StackMachine host.</summary>
        /// <param name="entryName">The configured Proxy entry name.</param>
        /// <param name="snapshot">The latest raw non-PBM DDB weight snapshot.</param>
        /// <param name="origin">The host-issued origin token retained by this logical Normal request series for stale-result control.</param>
        /// <param name="handle">The queued Texture execution handle on success.</param>
        /// <param name="diagnostic">The structured rejection diagnostic on failure.</param>
        /// <returns><see langword="true"/> when the recipe was accepted by the Texture StackMachine host.</returns>
        public bool TrySubmitNormal(string entryName, IReadOnlyList<Materials.NormalTargetWeight> snapshot, TextureExecutionOriginKey origin, TextureExecutionOptions options, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
        {
            handle = null;
            if (!TryBuildNormalRecipe(entryName, snapshot, out TextureRecipeStub stub, out diagnostic)) return false;
            if (!TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost textureHost, out diagnostic)) return false;
            return new TextureExecutor(textureHost).TryExecute(stub, origin, options, out handle, out diagnostic);
        }

        /// <summary>Builds and queues one Normal-entry recipe without retained Texture halls.</summary>
        public bool TrySubmitNormal(string entryName, IReadOnlyList<Materials.NormalTargetWeight> snapshot, TextureExecutionOriginKey origin, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic)
            => TrySubmitNormal(entryName, snapshot, origin, null, out handle, out diagnostic);
        private bool TryResolveNormalTextures(string entryName, out Texture2D baseTexture, out List<NormalTargetSource> sources, out StackMachineDiagnostic diagnostic)
        {
            baseTexture = null; sources = new List<NormalTargetSource>(); diagnostic = null;
            if (documentBinding != null) return TryResolveDocumentNormalTextures(entryName, out baseTexture, out sources, out diagnostic);
            if (bindingTemplate == null) return FailTarget("NormalTextureDictionaryMissing", "MeshStackMachine requires a MeshBindingTemplate Base and target Normal texture dictionary.", out diagnostic);
            var targetNames = new HashSet<string>(StringComparer.Ordinal);
            bool hasBase = false;
            IReadOnlyList<NormalTargetTextureEntry> targets = bindingTemplate.NormalTargetTextures;
            for (int i = 0; i < targets.Count; i++)
            {
                NormalTargetTextureEntry target = targets[i];
                if (target == null) return FailTarget("NormalTextureDictionaryInvalid", "Each Normal texture dictionary entry is required.", out diagnostic);
                if (target.textures == null) return FailTarget("NormalTextureDictionaryInvalid", "Each target Normal texture dictionary entry requires a texture list.", out diagnostic);
                bool isBase = string.IsNullOrEmpty(target.targetName);
                if (isBase)
                {
                    if (hasBase) return FailTarget("NormalTextureDictionaryInvalid", "Normal texture dictionary allows exactly one Base entry with an empty target name.", out diagnostic);
                    hasBase = true;
                }
                else if (target.targetName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal) || !targetNames.Add(target.targetName))
                {
                    return FailTarget("NormalTextureDictionaryInvalid", "Each target Normal texture dictionary entry requires one unique non-PBM target name.", out diagnostic);
                }
                NormalTextureEntry selected = null;
                for (int sourceIndex = 0; sourceIndex < target.textures.Count; sourceIndex++)
                {
                    NormalTextureEntry candidate = target.textures[sourceIndex];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.entryName)) return FailTarget("NormalTextureDictionaryInvalid", "Each target Normal texture dictionary item requires a logical Proxy entry name.", out diagnostic);
                    if (candidate.entryName != entryName) continue;
                    if (selected != null) return FailTarget("NormalTextureDictionaryInvalid", "One target may provide each Proxy entry Normal texture only once.", out diagnostic);
                    selected = candidate;
                }
                if (isBase)
                {
                    if (selected == null || selected.texture == null) return FailTarget("NormalBaseTextureMissing", "The empty-name Base dictionary entry requires a texture for every NORMAL entry.", out diagnostic);
                    baseTexture = selected.texture;
                }
                else if (selected != null) sources.Add(new NormalTargetSource { targetName = target.targetName, texture = selected.texture });
            }
            if (!hasBase) return FailTarget("NormalBaseTextureMissing", "Normal texture dictionary requires exactly one empty-name Base entry.", out diagnostic);
            return true;
        }
        private bool TryResolveDocumentNormalTextures(string entryName, out Texture2D baseTexture, out List<NormalTargetSource> sources, out StackMachineDiagnostic diagnostic)
        {
            baseTexture = null;
            sources = new List<NormalTargetSource>();
            diagnostic = null;
            MeshNormalOwnerBindingEntry figureOwner = null;
            string requestedOwner = documentNormalOwnerRegistryId ?? string.Empty;
            IReadOnlyList<MeshNormalOwnerBindingEntry> owners = documentBinding.NormalOwners;
            for (int i = 0; i < owners.Count; i++)
            {
                MeshNormalOwnerBindingEntry owner = owners[i];
                if (owner == null) return FailTarget("NormalTextureDictionaryInvalid", "Each MeshBinding Normal owner is required.", out diagnostic);
                if ((owner.outfitRegistryId ?? string.Empty) != requestedOwner) continue;
                if (figureOwner != null) return FailTarget("NormalTextureDictionaryInvalid", "MeshBinding allows exactly one Figure Normal owner with an empty RegistryId.", out diagnostic);
                figureOwner = owner;
            }
            if (figureOwner == null) return FailTarget("NormalBaseTextureMissing", "MeshBinding requires the selected Figure or Outfit Normal owner for NORMAL entries.", out diagnostic);

            var targetNames = new HashSet<string>(StringComparer.Ordinal);
            bool hasBase = false;
            for (int i = 0; i < figureOwner.targets.Count; i++)
            {
                MeshNormalTargetBindingEntry target = figureOwner.targets[i];
                if (target == null || target.textures == null) return FailTarget("NormalTextureDictionaryInvalid", "Each Normal target requires its texture list.", out diagnostic);
                bool isBase = string.IsNullOrEmpty(target.targetName);
                if (isBase)
                {
                    if (hasBase) return FailTarget("NormalTextureDictionaryInvalid", "MeshBinding allows exactly one Base Normal target.", out diagnostic);
                    hasBase = true;
                }
                else if (target.targetName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal) || !targetNames.Add(target.targetName))
                {
                    return FailTarget("NormalTextureDictionaryInvalid", "Each Normal target requires one unique non-PBM target name.", out diagnostic);
                }
                MeshNormalTextureBindingEntry selected = null;
                for (int textureIndex = 0; textureIndex < target.textures.Count; textureIndex++)
                {
                    MeshNormalTextureBindingEntry candidate = target.textures[textureIndex];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.entryName)) return FailTarget("NormalTextureDictionaryInvalid", "Each Normal texture requires a logical entry name.", out diagnostic);
                    if (candidate.entryName != entryName) continue;
                    if (selected != null) return FailTarget("NormalTextureDictionaryInvalid", "One Normal target may provide each entry only once.", out diagnostic);
                    selected = candidate;
                }
                if (isBase)
                {
                    if (selected == null || selected.normalTexture == null) return FailTarget("NormalBaseTextureMissing", "The Base Normal target requires a texture for every NORMAL entry.", out diagnostic);
                    baseTexture = selected.normalTexture;
                }
                else if (selected != null) sources.Add(new NormalTargetSource { targetName = target.targetName, texture = selected.normalTexture });
            }
            if (!hasBase) return FailTarget("NormalBaseTextureMissing", "MeshBinding requires exactly one Base Normal target.", out diagnostic);
            return true;
        }
        private static bool FailTarget(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message); return false; }
        private void StoreNormalTemplates(IReadOnlyList<NormalRecipeTemplate> templates)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (bindingContext == null || (bindingContext.TryGetHandle(templates[i].EntryName, out int handle) && bindingContext.TryGetNormal(handle, out _))) normalTemplates[templates[i].EntryName] = templates[i];
            }
        }
        private void BuildBindingContext()
        {
            if (bindingContext != null) return;
            if (bindingTemplate == null) return;
            DynamicBoneBlender blender = GetComponent<DynamicBoneBlender>(); OutfitAttacher attacher = GetComponent<OutfitAttacher>();
            if (MeshBindingContext.TryCreate(bindingTemplate, blender, attacher, out bindingContext, out StackMachineDiagnostic diagnostic)) return;
            if (!warned)
            {
                warned = true;
                string detail = diagnostic == null ? "No structured diagnostic was returned." : diagnostic.domainCode + ": " + diagnostic.message + (string.IsNullOrEmpty(diagnostic.detail) ? string.Empty : " (" + diagnostic.detail + ")");
                Debug.LogWarning("MeshStackMachine is disabled because its binding template or Figure components could not be resolved. " + detail, this);
            }
        }
        private bool TryConfigureDocumentBinding(MeshBinding binding, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (binding == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "BindingRequired", "A non-empty Mesh document payload requires MeshBinding.");
                return false;
            }
            if (bindingTemplate != null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "ConfigurationConflict", "MeshStackMachine cannot use MeshBindingTemplate and a ShapeSyncDocument MeshBinding together.");
                return false;
            }
            if (ReferenceEquals(documentBinding, binding) && bindingContext != null && ready) return true;
            DynamicBoneBlender blender = GetComponent<DynamicBoneBlender>();
            OutfitAttacher attacher = GetComponent<OutfitAttacher>();
            if (!MeshBindingContext.TryCreate(binding, blender, attacher, out MeshBindingContext context, out diagnostic)) return false;
            bindingContext = context;
            documentBinding = binding;
            documentNormalOwnerRegistryId = null;
            executor.InitializeTransactionState(bindingContext);
            ready = true;
            return true;
        }
        private bool TryAcceptOutfitNormalPayload(ShapeSyncDocument payload, out StackMachineExecutionResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            ShapeSyncOutfit outfit = GetComponent<ShapeSyncOutfit>();
            if (outfit == null || GetComponent<DynamicBoneBlender>() != null) return false;
            if (payload.MeshBinding == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "BindingRequired", "Outfit Normal payload requires MeshBinding.");
                return false;
            }
            if (!MeshNormalBlockParser.TryExtract(payload.MeshRecipe.wordSource, out string outerSource, out IReadOnlyList<NormalRecipeTemplate> templates, out diagnostic)) return false;
            if (!string.IsNullOrWhiteSpace(outerSource) || templates.Count == 0)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "OutfitPayloadInvalid", "Outfit Mesh payload may contain only NORMAL blocks.");
                return false;
            }
            Materials.NormalBlender blender = GetComponent<Materials.NormalBlender>();
            if (blender == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalBlenderRequired", "Outfit Normal payload requires a co-located NormalBlender.");
                return false;
            }
            var entries = new HashSet<string>(blender.Entries, StringComparer.Ordinal);
            for (int i = 0; i < templates.Count; i++)
            {
                if (!entries.Contains(templates[i].EntryName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalEntryInvalid", "Outfit Normal payload references an entry not owned by its NormalBlender.", bindingName: templates[i].EntryName);
                    return false;
                }
            }
            documentBinding = payload.MeshBinding;
            documentNormalOwnerRegistryId = outfit.RegistryId;
            StoreNormalTemplates(templates);
            result = new StackMachineExecutionResult { Stage = StackMachineTransactionStage.Succeeded };
            result.Lifecycle.Add(StackMachineTransactionStage.Started);
            result.Lifecycle.Add(StackMachineTransactionStage.Succeeded);
            return true;
        }
        private bool TryDispatchOutfitNormalPayloads(ShapeSyncDocument payload, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (payload.MeshBinding == null || bindingContext == null) return true;
            if (!MeshNormalBlockParser.TryExtract(payload.MeshRecipe.wordSource, out _, out IReadOnlyList<NormalRecipeTemplate> templates, out diagnostic)) return false;
            IReadOnlyList<MeshNormalOwnerBindingEntry> owners = payload.MeshBinding.NormalOwners;
            for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++)
            {
                MeshNormalOwnerBindingEntry owner = owners[ownerIndex];
                if (owner == null || string.IsNullOrEmpty(owner.outfitRegistryId)) continue;
                var selected = new List<NormalRecipeTemplate>();
                for (int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
                {
                    if (OwnerContainsEntry(owner, templates[templateIndex].EntryName)) selected.Add(templates[templateIndex]);
                }
                if (selected.Count == 0) continue;
                if (!bindingContext.OutfitAttacher.TryGetAttachedOutfit(owner.outfitRegistryId, out ShapeSyncOutfit outfit, out StackMachineDiagnostic targetDiagnostic))
                {
                    Debug.LogWarning("MeshStackMachine skipped Outfit Normal payload. " + targetDiagnostic.domainCode + ": " + targetDiagnostic.message, this);
                    continue;
                }
                MeshStackMachine outfitMachine = outfit.GetComponent<MeshStackMachine>();
                if (outfitMachine == null)
                {
                    Debug.LogWarning("MeshStackMachine skipped Outfit Normal payload. No co-located MeshStackMachine exists.", outfit);
                    continue;
                }
                if (!ShapeSyncDocument.TryCreateSnapshot(payload, out ShapeSyncDocument targetPayload, out targetDiagnostic))
                {
                    Debug.LogWarning("MeshStackMachine skipped Outfit Normal payload. " + targetDiagnostic.domainCode + ": " + targetDiagnostic.message, outfit);
                    continue;
                }
                targetPayload.MeshRecipe.wordSource = BuildNormalPayloadSource(selected);
                targetPayload.MaterialRecipe = null;
                targetPayload.MaterialBinding = null;
                if (!outfitMachine.TryAcceptRecipePayload(targetPayload, out _, out targetDiagnostic))
                {
                    Debug.LogWarning("MeshStackMachine skipped Outfit Normal payload. " + targetDiagnostic.domainCode + ": " + targetDiagnostic.message, outfit);
                }
            }
            return true;
        }
        private static bool OwnerContainsEntry(MeshNormalOwnerBindingEntry owner, string entryName)
        {
            for (int targetIndex = 0; targetIndex < owner.targets.Count; targetIndex++)
            {
                MeshNormalTargetBindingEntry target = owner.targets[targetIndex];
                if (target == null) continue;
                for (int textureIndex = 0; textureIndex < target.textures.Count; textureIndex++)
                {
                    MeshNormalTextureBindingEntry texture = target.textures[textureIndex];
                    if (texture != null && texture.entryName == entryName) return true;
                }
            }
            return false;
        }
        private static string BuildNormalPayloadSource(IReadOnlyList<NormalRecipeTemplate> templates)
        {
            var source = new System.Text.StringBuilder();
            for (int i = 0; i < templates.Count; i++)
            {
                if (i != 0) source.Append(' ');
                source.Append('$').Append(templates[i].EntryName).Append(" NORMAL ").Append(templates[i].WordSource).Append(" ENDNORMAL");
            }
            return source.ToString();
        }
        private static void AddBindingDeclarations(MeshRecipeDocument document, IReadOnlyList<MorphEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].word)) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = entries[i].word, declaredKind = StackMachineBindingKind.Resource });
        }
        private static void AddBindingDeclarations(MeshRecipeDocument document, IReadOnlyList<OutfitEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].word)) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = entries[i].word, declaredKind = StackMachineBindingKind.Resource });
        }

        private static MeshRecipeDocument CreateRuntimeDocument(MeshRecipeDocument source, MeshBinding binding)
        {
            var runtime = new MeshRecipeDocument
            {
                recipeFormatVersion = source.recipeFormatVersion,
                wordSource = source.wordSource,
                capabilities = source.capabilities,
                provenance = source.provenance,
                diagnosticSourceMap = source.diagnosticSourceMap
            };
            AddBindingDeclarations(runtime, binding.Morphs);
            AddBindingDeclarations(runtime, binding.Outfits);
            return runtime;
        }

        private static void AddBindingDeclarations(MeshRecipeDocument document, IReadOnlyList<MeshMorphBindingEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].logicalName)) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = entries[i].logicalName, declaredKind = StackMachineBindingKind.Resource });
        }

        private static void AddBindingDeclarations(MeshRecipeDocument document, IReadOnlyList<MeshOutfitBindingEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && !string.IsNullOrWhiteSpace(entries[i].logicalName)) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = entries[i].logicalName, declaredKind = StackMachineBindingKind.Resource });
        }
        private static bool Fail(string code, string message, out StackMachineExecutionResult result) => Fail(StackMachineDiagnostic.CreateDomain("mesh", code, message), out result);
        private static bool Fail(StackMachineDiagnostic diagnostic, out StackMachineExecutionResult result) { result = new StackMachineExecutionResult { Stage = StackMachineTransactionStage.Failed, Diagnostic = diagnostic }; result.Lifecycle.Add(StackMachineTransactionStage.Failed); return false; }
    }
}
