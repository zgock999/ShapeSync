// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Maps one recipe logical morph word to a DDB blend target name.</summary>
    [Serializable]
    public sealed class MorphEntry
    {
        /// <summary>Recipe logical word without the Forth <c>$</c> prefix.</summary>
        public string word;
        /// <summary>DDB blend target name resolved by the Figure-local DynamicBoneBlender.</summary>
        public string name;
    }

    /// <summary>Maps one recipe logical Outfit word to a ShapeSync Outfit prefab root.</summary>
    [Serializable]
    public sealed class OutfitEntry
    {
        /// <summary>Recipe logical word without the Forth <c>$</c> prefix.</summary>
        public string word;
        /// <summary>Prefab root that must contain the resolved ShapeSyncOutfit component.</summary>
        public GameObject obj;
    }

    /// <summary>One target-unit Normal texture selected for one logical Normal binding.</summary>
    [Serializable]
    public sealed class NormalTextureEntry
    {
        /// <summary>Logical Proxy entry name, such as <c>face</c>.</summary>
        public string entryName;
        /// <summary>Pre-delta encoded Normal texture for this target and binding.</summary>
        public Texture2D texture;
    }

    /// <summary>All Normal textures contributed by one target dictionary entry.</summary>
    [Serializable]
    public sealed class NormalTargetTextureEntry
    {
        /// <summary>Non-PBM DDB target name, such as <c>BasicGirl</c>, or an empty name for the required Base entry.</summary>
        public string targetName;
        /// <summary>Base or target Normal textures keyed by logical Proxy entry name.</summary>
        public List<NormalTextureEntry> textures = new List<NormalTextureEntry>();
    }

    /// <summary>Figure-local logical binding template. Its dictionaries are built once by MeshStackMachine.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/Mesh Binding Template")]
    public sealed class MeshBindingTemplate : ScriptableObject
    {
        [SerializeField] private List<MorphEntry> morphs = new List<MorphEntry>();
        [SerializeField] private List<OutfitEntry> outfits = new List<OutfitEntry>();
        [SerializeField] private List<NormalTargetTextureEntry> normalTargetTextures = new List<NormalTargetTextureEntry>();
        /// <summary>Gets the serialized logical morph bindings.</summary>
        public IReadOnlyList<MorphEntry> Morphs => morphs;
        /// <summary>Gets the serialized logical Outfit bindings.</summary>
        public IReadOnlyList<OutfitEntry> Outfits => outfits;
        /// <summary>Gets the Mesh StackMachine-owned Base and target Normal texture dictionaries.</summary>
        public IReadOnlyList<NormalTargetTextureEntry> NormalTargetTextures => normalTargetTextures;
    }
}
