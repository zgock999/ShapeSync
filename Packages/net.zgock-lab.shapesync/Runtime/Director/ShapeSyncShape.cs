// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Runtime logical shape selected by a Shape Director.</summary>
    /// <remarks>This is Director-owned logical state. It is not a Unity asset and does not own scene, GPU, or StackMachine state.</remarks>
    public abstract class ShapeSyncShape
    {
        private readonly List<string> tags;

        /// <summary>Initializes a runtime shape with its authoring identity and ordering data.</summary>
        /// <param name="shapeId">The logical Shape identity.</param>
        /// <param name="priority">The physical composition priority.</param>
        /// <param name="tags">The author-defined exclusion tags to copy.</param>
        protected ShapeSyncShape(string shapeId, int priority, IEnumerable<string> tags)
        {
            ShapeId = shapeId;
            Priority = priority;
            this.tags = tags == null ? new List<string>() : new List<string>(tags);
        }

        /// <summary>Gets the logical Shape identity.</summary>
        public string ShapeId { get; }

        /// <summary>Gets the physical composition priority, where lower values compose first.</summary>
        public int Priority { get; }

        /// <summary>Gets the author-defined exclusion tags.</summary>
        public IReadOnlyList<string> Tags => tags;

        /// <summary>Creates an independent logical-state copy of this shape.</summary>
        public abstract ShapeSyncShape Clone();

        /// <summary>Copies the common Shape identity values into a new tag list.</summary>
        /// <returns>An independent mutable tag list for a derived clone.</returns>
        protected List<string> CopyTags() => new List<string>(tags);
    }

    /// <summary>Runtime shape that requests FBM morph values.</summary>
    public sealed class MorphShape : ShapeSyncShape
    {
        private readonly List<MorphValue> morphs;

        /// <summary>Initializes a Morph shape.</summary>
        public MorphShape(string shapeId, int priority, IEnumerable<string> tags, IEnumerable<MorphValue> morphs)
            : base(shapeId, priority, tags) => this.morphs = morphs == null ? new List<MorphValue>() : new List<MorphValue>(morphs);

        /// <summary>Gets the requested morph values.</summary>
        public IReadOnlyList<MorphValue> Morphs => morphs;

        /// <inheritdoc />
        public override ShapeSyncShape Clone() => new MorphShape(ShapeId, Priority, CopyTags(), morphs);
    }

    /// <summary>Base runtime shape for skin, hair, and outfit parts.</summary>
    public abstract class PartsShape : ShapeSyncShape
    {
        private readonly List<ShapeEntry> parts;

        /// <summary>Initializes a parts-based shape.</summary>
        /// <param name="shapeId">The logical Shape identity.</param>
        /// <param name="priority">The physical composition priority.</param>
        /// <param name="tags">The author-defined exclusion tags to copy.</param>
        /// <param name="parts">The physical entries to clone into this runtime Shape.</param>
        protected PartsShape(string shapeId, int priority, IEnumerable<string> tags, IEnumerable<ShapeEntry> parts)
            : base(shapeId, priority, tags) => this.parts = ShapeEntry.CloneList(parts);

        /// <summary>Gets the ordered physical entries contributed by this shape.</summary>
        public IReadOnlyList<ShapeEntry> Parts => parts;

        /// <summary>Creates an independent copy of the ordered part list.</summary>
        /// <returns>An independent mutable part list for a derived clone.</returns>
        protected List<ShapeEntry> CopyParts() => ShapeEntry.CloneList(parts);
    }

    /// <summary>Runtime shape that contributes skin entries.</summary>
    public sealed class SkinShape : PartsShape
    {
        /// <summary>Initializes a skin shape.</summary>
        public SkinShape(string shapeId, int priority, IEnumerable<string> tags, IEnumerable<ShapeEntry> parts)
            : base(shapeId, priority, tags, parts) { }

        /// <inheritdoc />
        public override ShapeSyncShape Clone() => new SkinShape(ShapeId, Priority, CopyTags(), CopyParts());
    }

    /// <summary>Runtime shape that contributes hair entries.</summary>
    public sealed class HairShape : PartsShape
    {
        /// <summary>Initializes a hair shape.</summary>
        public HairShape(string shapeId, int priority, IEnumerable<string> tags, IEnumerable<ShapeEntry> parts)
            : base(shapeId, priority, tags, parts) { }

        /// <inheritdoc />
        public override ShapeSyncShape Clone() => new HairShape(ShapeId, Priority, CopyTags(), CopyParts());
    }

    /// <summary>Runtime shape that contributes outfit entries.</summary>
    public sealed class OutfitShape : PartsShape
    {
        /// <summary>Initializes an outfit shape.</summary>
        public OutfitShape(string shapeId, int priority, IEnumerable<string> tags, IEnumerable<ShapeEntry> parts)
            : base(shapeId, priority, tags, parts) { }

        /// <inheritdoc />
        public override ShapeSyncShape Clone() => new OutfitShape(ShapeId, Priority, CopyTags(), CopyParts());
    }

    /// <summary>One logical morph target and its requested value.</summary>
    [Serializable]
    public struct MorphValue
    {
        [SerializeField] private string target;
        [SerializeField] private float value;

        /// <summary>Gets or sets the MeshBinding logical morph name.</summary>
        public string Target { get => target; set => target = value; }

        /// <summary>Gets or sets the requested raw morph value.</summary>
        public float Value { get => value; set => this.value = value; }
    }

    /// <summary>Base class for a physical contribution made by a parts-based shape.</summary>
    [Serializable]
    public abstract class ShapeEntry
    {
        /// <summary>Creates an independent copy of this entry.</summary>
        public abstract ShapeEntry Clone();

        internal static List<ShapeEntry> CloneList(IEnumerable<ShapeEntry> source)
        {
            var result = new List<ShapeEntry>();
            if (source == null) return result;
            foreach (ShapeEntry entry in source) result.Add(entry == null ? null : entry.Clone());
            return result;
        }
    }

    /// <summary>Physical Mesh attach or detach contribution.</summary>
    [Serializable]
    public sealed class MeshEntry : ShapeEntry
    {
        [SerializeField] private string logicalName;
        [SerializeField] private List<MeshMaskEntry> masks = new List<MeshMaskEntry>();

        /// <summary>Gets or sets the MeshBinding logical name.</summary>
        public string LogicalName { get => logicalName; set => logicalName = value; }

        /// <summary>Gets the Figure Material Proxy masks owned by this mesh contribution.</summary>
        /// <remarks>The list is Shape-authoring data. Each mask is applied only while this MeshEntry is physically attached.</remarks>
        public List<MeshMaskEntry> Masks => masks ?? (masks = new List<MeshMaskEntry>());

        /// <inheritdoc />
        public override ShapeEntry Clone()
        {
            var clone = new MeshEntry { LogicalName = LogicalName };
            for (int i = 0; i < Masks.Count; i++) clone.Masks.Add(Masks[i]?.Clone());
            return clone;
        }
    }

    /// <summary>One Figure Material Proxy mask owned by a MeshEntry.</summary>
    [Serializable]
    public sealed class MeshMaskEntry
    {
        [SerializeField] private string proxyEntryName;
        [SerializeField] private string maskName;

        /// <summary>Gets or sets the Figure Material Proxy entry receiving this mask.</summary>
        public string ProxyEntryName { get => proxyEntryName; set => proxyEntryName = value; }

        /// <summary>Gets or sets the MaterialBinding logical mask texture name.</summary>
        public string MaskName { get => maskName; set => maskName = value; }

        /// <summary>Creates an independent logical-state copy.</summary>
        public MeshMaskEntry Clone() => new MeshMaskEntry { ProxyEntryName = ProxyEntryName, MaskName = MaskName };
    }

    /// <summary>Base physical contribution addressed to a Material StackMachine proxy entry.</summary>
    [Serializable]
    public abstract class MaterialEntry : ShapeEntry
    {
        [SerializeField] private string registryId;
        [SerializeField] private string proxyEntry;

        /// <summary>Gets or sets the Outfit registry identity; empty addresses the Figure.</summary>
        public string RegistryId { get => registryId; set => registryId = value; }

        /// <summary>Gets or sets the MaterialProxy entry name.</summary>
        public string ProxyEntry { get => proxyEntry; set => proxyEntry = value; }

        /// <summary>Copies this entry's Material target fields into a target clone.</summary>
        /// <param name="destination">The derived Material entry clone to populate.</param>
        protected void CopyTargetTo(MaterialEntry destination)
        {
            destination.RegistryId = RegistryId;
            destination.ProxyEntry = ProxyEntry;
        }
    }

    /// <summary>One Texture source-over contribution and optional mask/colorization data.</summary>
    [Serializable]
    public sealed class TextureEntry : MaterialEntry
    {
        [SerializeField] private string logicalName;
        [SerializeField] private bool useColor;
        [SerializeField] private Color32 color;

        /// <summary>Gets or sets the MaterialBinding Texture logical name.</summary>
        public string LogicalName { get => logicalName; set => logicalName = value; }

        /// <summary>Gets or sets whether <see cref="Color"/> colorizes this Texture contribution.</summary>
        public bool UseColor { get => useColor; set => useColor = value; }

        /// <summary>Gets or sets the authoring sRGB color used for optional colorization.</summary>
        public Color32 Color { get => color; set => color = value; }

        /// <inheritdoc />
        public override ShapeEntry Clone()
        {
            var clone = new TextureEntry { LogicalName = LogicalName, UseColor = UseColor, Color = Color };
            CopyTargetTo(clone);
            return clone;
        }
    }

    /// <summary>One proxy-wide Material color contribution.</summary>
    [Serializable]
    public sealed class ColorEntry : MaterialEntry
    {
        [SerializeField] private Color32 color;

        /// <summary>Gets or sets the authoring sRGB color.</summary>
        public Color32 Color { get => color; set => color = value; }

        /// <inheritdoc />
        public override ShapeEntry Clone()
        {
            var clone = new ColorEntry { Color = Color };
            CopyTargetTo(clone);
            return clone;
        }
    }

    /// <summary>One proxy-wide Material UV transform contribution.</summary>
    [Serializable]
    public sealed class UvsetEntry : MaterialEntry
    {
        [SerializeField] private float scaleX = 1f;
        [SerializeField] private float scaleY = 1f;
        [SerializeField] private float offsetX;
        [SerializeField] private float offsetY;

        /// <summary>Gets or sets the raw U scale.</summary>
        public float ScaleX { get => scaleX; set => scaleX = value; }

        /// <summary>Gets or sets the raw V scale.</summary>
        public float ScaleY { get => scaleY; set => scaleY = value; }

        /// <summary>Gets or sets the raw U offset.</summary>
        public float OffsetX { get => offsetX; set => offsetX = value; }

        /// <summary>Gets or sets the raw V offset.</summary>
        public float OffsetY { get => offsetY; set => offsetY = value; }

        /// <inheritdoc />
        public override ShapeEntry Clone()
        {
            var clone = new UvsetEntry { ScaleX = ScaleX, ScaleY = ScaleY, OffsetX = OffsetX, OffsetY = OffsetY };
            CopyTargetTo(clone);
            return clone;
        }
    }
}
