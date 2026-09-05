// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Shared physical Mesh source binding referenced by one or more ShapeSync documents.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/Mesh Binding")]
    public sealed class MeshBinding : ScriptableObject
    {
        [SerializeField] private List<MeshMorphBindingEntry> morphs = new List<MeshMorphBindingEntry>();
        [SerializeField] private List<MeshOutfitBindingEntry> outfits = new List<MeshOutfitBindingEntry>();
        [SerializeField] private List<MeshNormalOwnerBindingEntry> normalOwners = new List<MeshNormalOwnerBindingEntry>();
        /// <summary>Gets the logical morph declarations.</summary>
        public IReadOnlyList<MeshMorphBindingEntry> Morphs => morphs;
        /// <summary>Gets the logical Outfit declarations.</summary>
        public IReadOnlyList<MeshOutfitBindingEntry> Outfits => outfits;
        /// <summary>Gets the Figure and Outfit Normal source declarations.</summary>
        public IReadOnlyList<MeshNormalOwnerBindingEntry> NormalOwners => normalOwners;
    }
}
