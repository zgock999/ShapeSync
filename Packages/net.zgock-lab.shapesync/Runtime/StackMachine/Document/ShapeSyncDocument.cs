// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Read-only composite source supplied to ShapeSync Mesh and Material StackMachines.</summary>
    /// <remarks>
    /// This is authoring data only. It does not own validation, compilation, transactions, GPU resources,
    /// deliveries, Material clones, or scene-temporary state.
    /// </remarks>
    public interface IShapeSyncDocument
    {
        /// <summary>Gets the detached Mesh recipe, or an empty domain payload.</summary>
        MeshRecipeDocument MeshRecipe { get; }

        /// <summary>Gets the shared Mesh logical binding declaration, or <see langword="null"/> for an empty domain payload.</summary>
        MeshBinding MeshBinding { get; }

        /// <summary>Gets the detached Material recipe, or an empty domain payload.</summary>
        MaterialRecipeDocument MaterialRecipe { get; }

        /// <summary>Gets the shared Material logical binding declaration, or <see langword="null"/> for an empty domain payload.</summary>
        MaterialBinding MaterialBinding { get; }
    }

    /// <summary>Serializable ShapeSync authoring document that composes detached Mesh and Material recipes.</summary>
    [Serializable]
    public sealed class ShapeSyncDocument : IShapeSyncDocument
    {
        [SerializeField] private MeshRecipeDocument meshRecipe;
        [SerializeField] private MeshBinding meshBinding;
        [SerializeField] private MaterialRecipeDocument materialRecipe;
        [SerializeField] private MaterialBinding materialBinding;

        /// <summary>Gets or sets the detached Mesh recipe.</summary>
        public MeshRecipeDocument MeshRecipe { get => meshRecipe; set => meshRecipe = value; }

        /// <summary>Gets or sets the shared Mesh logical binding declaration.</summary>
        public MeshBinding MeshBinding { get => meshBinding; set => meshBinding = value; }

        /// <summary>Gets or sets the detached Material recipe.</summary>
        public MaterialRecipeDocument MaterialRecipe { get => materialRecipe; set => materialRecipe = value; }

        /// <summary>Gets or sets the shared Material logical binding declaration.</summary>
        public MaterialBinding MaterialBinding { get => materialBinding; set => materialBinding = value; }

        /// <summary>Creates a detached snapshot of a document source.</summary>
        /// <param name="source">The caller-owned document source.</param>
        /// <param name="snapshot">A deep recipe-metadata copy that preserves shared Binding reference identity.</param>
        /// <param name="diagnostic">A structured failure when the source cannot be snapshotted.</param>
        /// <returns><see langword="true"/> when the snapshot was created without mutating <paramref name="source"/>.</returns>
        public static bool TryCreateSnapshot(IShapeSyncDocument source, out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic)
        {
            snapshot = null;
            diagnostic = null;
            if (source == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("document", "DocumentRequired", "ShapeSyncDocument source is required.");
                return false;
            }

            try
            {
                snapshot = new ShapeSyncDocument
                {
                    MeshRecipe = CopyMeshRecipe(source.MeshRecipe),
                    MeshBinding = source.MeshBinding,
                    MaterialRecipe = CopyMaterialRecipe(source.MaterialRecipe),
                    MaterialBinding = source.MaterialBinding
                };
                return true;
            }
            catch (Exception exception)
            {
                snapshot = null;
                diagnostic = StackMachineDiagnostic.CreateDomain("document", "SnapshotFailed", "ShapeSyncDocument snapshot could not be created.", detail: exception.GetType().Name);
                return false;
            }
        }

        private static MeshRecipeDocument CopyMeshRecipe(MeshRecipeDocument source)
        {
            if (source == null) return null;
            StackMachineRecipeDocument copy = StackMachineRecipeClone.Copy(source);
            return new MeshRecipeDocument
            {
                recipeFormatVersion = copy.recipeFormatVersion,
                wordSource = copy.wordSource,
                bindings = copy.bindings,
                capabilities = copy.capabilities,
                provenance = copy.provenance,
                diagnosticSourceMap = copy.diagnosticSourceMap
            };
        }

        private static MaterialRecipeDocument CopyMaterialRecipe(MaterialRecipeDocument source)
        {
            if (source == null) return null;
            StackMachineRecipeDocument copy = StackMachineRecipeClone.Copy(source);
            return new MaterialRecipeDocument
            {
                recipeFormatVersion = copy.recipeFormatVersion,
                wordSource = copy.wordSource,
                bindings = copy.bindings,
                capabilities = copy.capabilities,
                provenance = copy.provenance,
                diagnosticSourceMap = copy.diagnosticSourceMap,
                textureDomainVersion = source.textureDomainVersion,
                outputLogicalName = source.outputLogicalName,
                outputWidth = source.outputWidth,
                outputHeight = source.outputHeight
            };
        }
    }

    /// <summary>Logical Mesh morph declaration stored by <see cref="MeshBinding"/>.</summary>
    [Serializable]
    public sealed class MeshMorphBindingEntry
    {
        /// <summary>Gets or sets the recipe logical name without the Forth <c>$</c> prefix.</summary>
        public string logicalName;

        /// <summary>Gets or sets the Dynamic Bone Blender target name.</summary>
        public string targetName;
    }

    /// <summary>Logical Outfit declaration stored by <see cref="MeshBinding"/>.</summary>
    [Serializable]
    public sealed class MeshOutfitBindingEntry
    {
        /// <summary>Gets or sets the recipe logical name without the Forth <c>$</c> prefix.</summary>
        public string logicalName;

        /// <summary>Gets or sets the source Outfit prefab root.</summary>
        public GameObject outfitPrefab;
    }

    /// <summary>One Normal texture selected for an entry within a Mesh Normal owner and target.</summary>
    [Serializable]
    public sealed class MeshNormalTextureBindingEntry
    {
        /// <summary>Gets or sets the globally unique logical Normal entry name.</summary>
        public string entryName;

        /// <summary>Gets or sets the source Normal texture.</summary>
        public Texture2D normalTexture;
    }

    /// <summary>Normal textures associated with one Base or non-PBM FBM target.</summary>
    [Serializable]
    public sealed class MeshNormalTargetBindingEntry
    {
        /// <summary>Gets or sets an empty Base name or a non-PBM FBM target name.</summary>
        public string targetName;

        /// <summary>Gets the Normal textures keyed by logical entry name.</summary>
        public List<MeshNormalTextureBindingEntry> textures = new List<MeshNormalTextureBindingEntry>();
    }

    /// <summary>Normal texture declarations owned by the Figure or one Outfit RegistryId.</summary>
    [Serializable]
    public sealed class MeshNormalOwnerBindingEntry
    {
        /// <summary>Gets or sets the empty Figure owner or an attached Outfit RegistryId.</summary>
        public string outfitRegistryId;

        /// <summary>Gets the Base and target Normal texture declarations for this owner.</summary>
        public List<MeshNormalTargetBindingEntry> targets = new List<MeshNormalTargetBindingEntry>();
    }

    /// <summary>Logical Material texture declaration stored by <see cref="MaterialBinding"/>.</summary>
    [Serializable]
    public sealed class MaterialTextureBindingEntry
    {
        /// <summary>Gets or sets the recipe logical source name without the Forth <c>$</c> prefix.</summary>
        public string logicalName;

        /// <summary>Gets or sets the read-only source texture.</summary>
        public Texture2D sourceTexture;
    }

}
