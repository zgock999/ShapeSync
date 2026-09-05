// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>ScriptableObject carrier for serialized Shape Director state and recovery recipes.</summary>
    /// <remarks>This carrier stores value records only. It never stores runtime Shapes, Templates, physical snapshots, plans, deliveries, or scene/GPU state.</remarks>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Shapes/Shape Document", fileName = "ShapeDocument")]
    public sealed class ShapeDocument : ShapeSyncDocumentAsset
    {
        [SerializeField] private List<SerializedMorphShape> morphShapes = new List<SerializedMorphShape>();
        [SerializeField] private List<SerializedSkinShape> skinShapes = new List<SerializedSkinShape>();
        [SerializeField] private List<SerializedHairShape> hairShapes = new List<SerializedHairShape>();
        [SerializeField] private List<SerializedOutfitShape> outfitShapes = new List<SerializedOutfitShape>();

        /// <summary>Gets the serialized Morph records.</summary>
        public IReadOnlyList<SerializedMorphShape> MorphShapes => morphShapes;
        /// <summary>Gets the serialized Skin records.</summary>
        public IReadOnlyList<SerializedSkinShape> SkinShapes => skinShapes;
        /// <summary>Gets the serialized Hair records.</summary>
        public IReadOnlyList<SerializedHairShape> HairShapes => hairShapes;
        /// <summary>Gets the serialized Outfit records.</summary>
        public IReadOnlyList<SerializedOutfitShape> OutfitShapes => outfitShapes;

        /// <summary>Replaces all serialized Shape records owned by this document.</summary>
        /// <param name="morphs">Detached Morph records in mixed-list-position form.</param>
        /// <param name="skins">Detached Skin records in mixed-list-position form.</param>
        /// <param name="hairs">Detached Hair records in mixed-list-position form.</param>
        /// <param name="outfits">Detached Outfit records in mixed-list-position form.</param>
        /// <remarks>This is the storage-boundary mutation API for concrete serializers. Callers must provide detached value records and must not store runtime Shapes, Templates, scene objects, or GPU state.</remarks>
        public void ReplaceShapes(List<SerializedMorphShape> morphs, List<SerializedSkinShape> skins, List<SerializedHairShape> hairs, List<SerializedOutfitShape> outfits)
        {
            morphShapes = morphs ?? new List<SerializedMorphShape>();
            skinShapes = skins ?? new List<SerializedSkinShape>();
            hairShapes = hairs ?? new List<SerializedHairShape>();
            outfitShapes = outfits ?? new List<SerializedOutfitShape>();
        }

    }

    /// <summary>Serialized value record for one runtime Morph shape.</summary>
    [Serializable]
    public sealed class SerializedMorphShape
    {
        [SerializeField] private int listPosition;
        [SerializeField] private string shapeId;
        [SerializeField] private int priority;
        [SerializeField] private List<string> tags = new List<string>();
        [SerializeField] private List<MorphValue> morphs = new List<MorphValue>();

        /// <summary>Gets or sets the original mixed runtime-list position.</summary>
        public int ListPosition { get => listPosition; set => listPosition = value; }
        /// <summary>Gets or sets the logical Shape identity.</summary>
        public string ShapeId { get => shapeId; set => shapeId = value; }
        /// <summary>Gets or sets the Shape priority.</summary>
        public int Priority { get => priority; set => priority = value; }
        /// <summary>Gets the copied exclusion tags.</summary>
        public List<string> Tags => tags;
        /// <summary>Gets the copied Morph values.</summary>
        public List<MorphValue> Morphs => morphs;
    }

    /// <summary>Common serialized value record for one runtime parts-based Shape.</summary>
    [Serializable]
    public abstract class SerializedPartsShape
    {
        [SerializeField] private int listPosition;
        [SerializeField] private string shapeId;
        [SerializeField] private int priority;
        [SerializeField] private List<string> tags = new List<string>();
        [SerializeReference] private List<ShapeEntry> parts = new List<ShapeEntry>();

        /// <summary>Gets or sets the original mixed runtime-list position.</summary>
        public int ListPosition { get => listPosition; set => listPosition = value; }
        /// <summary>Gets or sets the logical Shape identity.</summary>
        public string ShapeId { get => shapeId; set => shapeId = value; }
        /// <summary>Gets or sets the Shape priority.</summary>
        public int Priority { get => priority; set => priority = value; }
        /// <summary>Gets the copied exclusion tags.</summary>
        public List<string> Tags => tags;
        /// <summary>Gets the copied polymorphic physical entries.</summary>
        public List<ShapeEntry> Parts => parts;
    }

    /// <summary>Serialized value record for one runtime Skin shape.</summary>
    [Serializable]
    public sealed class SerializedSkinShape : SerializedPartsShape { }
    /// <summary>Serialized value record for one runtime Hair shape.</summary>
    [Serializable]
    public sealed class SerializedHairShape : SerializedPartsShape { }
    /// <summary>Serialized value record for one runtime Outfit shape.</summary>
    [Serializable]
    public sealed class SerializedOutfitShape : SerializedPartsShape { }
}
