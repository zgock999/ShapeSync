// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync
{
    /// <summary>Authoring-only Database sub-asset containing Figure, Outfit, Material, Texture, Normal, and Shape declarations.</summary>
    public sealed class ShapeSyncDatabaseRegistry : ScriptableObject
    {
        /// <summary>Classifies one Figure deformation axis without changing its canonical name.</summary>
        public enum FigureAxisKind
        {
            /// <summary>Full-body morph axis.</summary>
            Fbm = 0,
            /// <summary>Partial-body morph axis.</summary>
            Pbm = 1
        }

        /// <summary>Classifies an authoring-only Outfit entity.  Mesh and Material outfits share identity rules but not payload.</summary>
        public enum OutfitKind
        {
            /// <summary>Mesh-backed Outfit.</summary>
            Mesh = 0,
            /// <summary>Material-only Outfit.</summary>
            Material = 1
        }

        /// <summary>Classifies one source Material for a Mesh Outfit.  Only Include later creates logical resources.</summary>
        public enum OutfitMaterialClassification
        {
            /// <summary>Include the source material in the generated Outfit.</summary>
            Include = 0,
            /// <summary>Exclude the source material from generated payload.</summary>
            Exclude = 1,
            /// <summary>Use the source material as a projection-only input.</summary>
            Projection = 2
        }

        /// <summary>One Mesh Outfit's unified authoring declaration for BCP and PCM input.</summary>
        public enum OutfitCollectionKind
        {
            /// <summary>No collection contribution.</summary>
            None = 0,
            /// <summary>Bone-only collection contribution.</summary>
            Bone = 1,
            /// <summary>Full collection contribution.</summary>
            Full = 2
        }

        /// <summary>Classifies a concrete Shape authoring record without changing the Spec16 runtime schema.</summary>
        public enum ShapeKind
        {
            /// <summary>Morph Shape containing named morph values.</summary>
            Morph = 0,
            /// <summary>Skin Shape containing ordered parts.</summary>
            Skin = 1,
            /// <summary>Hair Shape containing ordered parts.</summary>
            Hair = 2,
            /// <summary>Outfit Shape containing ordered parts.</summary>
            Outfit = 3
        }

        /// <summary>Classifies an ordered authoring entry in a parts-based Shape.</summary>
        public enum ShapeEntryKind
        {
            /// <summary>Mesh contribution.</summary>
            Mesh = 0,
            /// <summary>Texture contribution.</summary>
            Texture = 1,
            /// <summary>Color contribution.</summary>
            Color = 2,
            /// <summary>UV transform contribution.</summary>
            Uvset = 3
        }

        /// <summary>Classifies the authoring source which owns a Texture resource aggregation group.</summary>
        public enum TextureResourceOwnerScope
        {
            /// <summary>The Figure layer owns the resource.</summary>
            Figure = 0,
            /// <summary>An Outfit owns the resource.</summary>
            Outfit = 1
        }

        /// <summary>Authoring purpose of a Texture resource.  This is separate from owner:
        /// an Outfit may own both material and Normal resources.</summary>
        internal enum TextureResourceUsage
        {
            General = 0,
            OutfitIncludedMaterial = 1,
            MaterialOutfit = 2,
            FigureMask = 3
        }

        internal enum TextureResourceDiagnosticCode
        {
            None = 0,
            ResourceMissing = 1,
            ReferencedByMaterialEntry = 2,
            ReferencedByNormalEntry = 3,
            OwnerTextureAlreadyRegistered = 4,
            ReferencedByOutfitTextureEntry = 5,
            ReferencedByFigureMask = 6
        }

        /// <summary>Structured authoring diagnostic for Texture resource operations.</summary>
        internal readonly struct TextureResourceDiagnostic
        {
            internal TextureResourceDiagnosticCode Code { get; }
            internal string ResourceName { get; }
            internal string ReferenceName { get; }
            internal string ShapeKey { get; }

            internal TextureResourceDiagnostic(TextureResourceDiagnosticCode code, string resourceName, string referenceName = null, string shapeKey = null)
            { Code = code; ResourceName = resourceName; ReferenceName = referenceName; ShapeKey = shapeKey; }

            public override string ToString()
            {
                switch (Code)
                {
                    case TextureResourceDiagnosticCode.ResourceMissing:
                        return "Texture resource does not exist: " + ResourceName;
                    case TextureResourceDiagnosticCode.ReferencedByMaterialEntry:
                        return "Texture resource is still referenced by Material Entry and cannot be removed: " + ResourceName + "; materialEntry=" + ReferenceName;
                    case TextureResourceDiagnosticCode.ReferencedByNormalEntry:
                        return "Texture resource is still referenced by Normal Entry and cannot be removed: " + ResourceName + "; materialEntry=" + ReferenceName + "; shapeKey=" + ShapeKey;
                    case TextureResourceDiagnosticCode.OwnerTextureAlreadyRegistered:
                        return "Texture resource Texture is already registered by another owner and must be a distinct Database-owned Texture: " + ResourceName;
                    case TextureResourceDiagnosticCode.ReferencedByOutfitTextureEntry:
                        return "Texture resource is still referenced by Material Outfit Texture Entry and cannot be removed: " + ResourceName + "; outfit=" + ReferenceName;
                    case TextureResourceDiagnosticCode.ReferencedByFigureMask:
                        return "Texture resource is still referenced by Figure Mask and cannot be removed: " + ResourceName + "; outfit=" + ReferenceName;
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>Fixed shape key for the Base Figure's Normal definition.</summary>
        public const string BaseShapeKey = "Base";

        /// <summary>Database-owned relative output folders used by the Generate pipeline.</summary>
        [Serializable]
        internal sealed class GenerationPathSettings
        {
            internal const string DefaultRegistriesPath = "Registries/";
            internal const string DefaultBindingsPath = "Bindings/";
            internal const string DefaultMaterialsPath = "Materials/";
            internal const string DefaultTexturesPath = "Textures/";
            internal const string DefaultOutfitsPath = "Outfits/";

            [SerializeField] private string registriesPath = DefaultRegistriesPath;
            [SerializeField] private string bindingsPath = DefaultBindingsPath;
            [SerializeField] private string materialsPath = DefaultMaterialsPath;
            [SerializeField] private string texturesPath = DefaultTexturesPath;
            [SerializeField] private string outfitsPath = DefaultOutfitsPath;

            internal string RegistriesPath => registriesPath;
            internal string BindingsPath => bindingsPath;
            internal string MaterialsPath => materialsPath;
            internal string TexturesPath => texturesPath;
            internal string OutfitsPath => outfitsPath;

            internal GenerationPathSettings() { }

            internal GenerationPathSettings(string registries, string bindings, string materials, string textures, string outfits)
            {
                registriesPath = registries;
                bindingsPath = bindings;
                materialsPath = materials;
                texturesPath = textures;
                outfitsPath = outfits;
            }
        }

        /// <summary>Identifies the canonical Base Figure stored by a Database.</summary>
        [Serializable]
        public sealed class BaseFigureEntry
        {
            [SerializeField] private string name;
            [SerializeField] private GameObject figure;
            /// <summary>Gets the logical Base Figure name.</summary>
            public string Name => name;
            /// <summary>Gets the Database-owned Base Figure object.</summary>
            public GameObject Figure => figure;
            internal BaseFigureEntry(string value, GameObject target) { name = value; figure = target; }
            internal void RebindFigure(GameObject target) { figure = target; }
            internal void Rename(string value) { name = value; }
        }

        /// <summary>One authoring-only Shape entry. It is lowered to the closed Spec16 entry schema only by Generate.</summary>
        [Serializable]
        internal sealed class ShapeEntryDefinition
        {
            [SerializeField] private ShapeEntryKind kind;
            [SerializeField] private string outfitIdentity;
            [SerializeField] private string registryId;
            [SerializeField] private string proxyEntry;
            [SerializeField] private string textureResourceName;
            [SerializeField] private bool useColorize;
            [SerializeField] private Color32 color = new Color32(255, 255, 255, 255);
            [SerializeField] private float scaleX = 1f;
            [SerializeField] private float scaleY = 1f;
            [SerializeField] private float offsetX;
            [SerializeField] private float offsetY;

            internal ShapeEntryKind Kind => kind;
            internal string OutfitIdentity => outfitIdentity;
            internal string RegistryId => registryId;
            internal string ProxyEntry => proxyEntry;
            internal string TextureResourceName => textureResourceName;
            internal bool UseColorize => useColorize;
            internal Color32 Color => color;
            internal float ScaleX => scaleX;
            internal float ScaleY => scaleY;
            internal float OffsetX => offsetX;
            internal float OffsetY => offsetY;

            internal ShapeEntryDefinition(ShapeEntryKind value)
            {
                if (!Enum.IsDefined(typeof(ShapeEntryKind), value)) throw new ArgumentOutOfRangeException(nameof(value));
                kind = value;
            }

            internal void SetMeshOutfit(string value) { outfitIdentity = value; }
            internal void SetMaterialTarget(string ownerId, string entry) { registryId = ownerId; proxyEntry = entry; }
            internal void SetTexture(string resource, bool colorize, Color32 value) { textureResourceName = resource; useColorize = colorize; color = value; }
            internal void SetColor(Color32 value) { color = value; }
            internal void SetUv(float xScale, float yScale, float xOffset, float yOffset) { scaleX = xScale; scaleY = yScale; offsetX = xOffset; offsetY = yOffset; }
            internal ShapeEntryDefinition Clone()
            {
                var copy = new ShapeEntryDefinition(kind);
                copy.SetMeshOutfit(outfitIdentity); copy.SetMaterialTarget(registryId, proxyEntry);
                copy.SetTexture(textureResourceName, useColorize, color); copy.SetUv(scaleX, scaleY, offsetX, offsetY);
                return copy;
            }
            internal bool ContentEquals(ShapeEntryDefinition other) => other != null && kind == other.kind
                && outfitIdentity == other.outfitIdentity && registryId == other.registryId && proxyEntry == other.proxyEntry
                && textureResourceName == other.textureResourceName && useColorize == other.useColorize && color.Equals(other.color)
                && scaleX == other.scaleX && scaleY == other.scaleY && offsetX == other.offsetX && offsetY == other.offsetY;
        }

        /// <summary>One Database-owned Shape declaration. It never represents a runtime ShapeDirector list item.</summary>
        [Serializable]
        internal sealed class ShapeEntry
        {
            [SerializeField] private string shapeId;
            [SerializeField] private string shapeName;
            [SerializeField] private ShapeKind kind;
            [SerializeField] private int priority;
            [SerializeField] private List<string> tags = new List<string>();
            [SerializeField] private List<MorphValue> morphs = new List<MorphValue>();
            [SerializeField] private List<ShapeEntryDefinition> parts = new List<ShapeEntryDefinition>();

            internal string ShapeId => shapeId;
            internal string ShapeName => shapeName;
            internal string DisplayName => string.IsNullOrWhiteSpace(shapeName) ? shapeId : shapeName;
            internal ShapeKind Kind => kind;
            internal int Priority => priority;
            internal IReadOnlyList<string> Tags => tags;
            internal IReadOnlyList<MorphValue> Morphs => morphs;
            internal IReadOnlyList<ShapeEntryDefinition> Parts => parts;

            internal ShapeEntry(string id, string name, ShapeKind value, int valuePriority, IEnumerable<string> valueTags)
            {
                shapeId = id;
                shapeName = name;
                kind = value;
                priority = value == ShapeKind.Morph ? 0 : valuePriority;
                tags = value == ShapeKind.Morph ? new List<string>() : valueTags == null ? new List<string>() : new List<string>(valueTags);
            }

            internal void SetShapeName(string value) { shapeName = value; }
            internal void SetShapeId(string value) { shapeId = value; }
            internal void SetPriority(int value) { priority = kind == ShapeKind.Morph ? 0 : value; }
            internal void SetTags(IEnumerable<string> values) { tags = kind == ShapeKind.Morph ? new List<string>() : values == null ? new List<string>() : new List<string>(values); }
            internal void SetMorphs(IEnumerable<MorphValue> values) { morphs = values == null ? new List<MorphValue>() : new List<MorphValue>(values); }
            internal void AddPart(ShapeEntryDefinition value) { parts.Add(value); }
            internal void SetParts(IEnumerable<ShapeEntryDefinition> values) { parts = values == null ? new List<ShapeEntryDefinition>() : values.Select(value => value.Clone()).ToList(); }
            internal bool RemovePart(int index)
            {
                if (index < 0 || index >= parts.Count) return false;
                parts.RemoveAt(index);
                return true;
            }
            internal bool MovePart(int index, bool moveUp)
            {
                int target = index + (moveUp ? -1 : 1);
                if (index < 0 || index >= parts.Count || target < 0 || target >= parts.Count) return false;
                ShapeEntryDefinition value = parts[index];
                parts[index] = parts[target];
                parts[target] = value;
                return true;
            }
        }

        [Serializable]
        internal sealed class MaterialEntry
        {
            [SerializeField] private string logicalName;
            [SerializeField] private SkinnedMeshRenderer renderer;
            [SerializeField] private string baseRelativeRendererPath;
            [SerializeField] private int materialSlot;
            [SerializeField] private string materialName;
            [SerializeField] private Material material;
            [SerializeField] private MaterialShaderAdapter adapter;
            [SerializeField] private List<string> textureResourceNames = new List<string>();
            internal string LogicalName => logicalName;
            internal SkinnedMeshRenderer Renderer => renderer;
            internal string BaseRelativeRendererPath => baseRelativeRendererPath;
            internal int MaterialSlot => materialSlot;
            internal string MaterialName => materialName;
            internal Material Material => material;
            internal MaterialShaderAdapter Adapter => adapter;
            internal IReadOnlyList<string> TextureResourceNames => textureResourceNames;
            internal MaterialEntry(string name, SkinnedMeshRenderer targetRenderer, string rendererPath, int slot, string displayName, Material targetMaterial, MaterialShaderAdapter targetAdapter)
            { logicalName = name; renderer = targetRenderer; baseRelativeRendererPath = rendererPath; materialSlot = slot; materialName = displayName; material = targetMaterial; adapter = targetAdapter; }
            internal void RebindRenderer(SkinnedMeshRenderer target) { renderer = target; }
            internal void Rename(string name)
            {
                logicalName = name;
                if (material != null) material.name = name + "_Material";
            }
            internal void SetTextureResourceNames(IEnumerable<string> names) { textureResourceNames = new List<string>(names); }
            internal void RebindAdapter(MaterialShaderAdapter value) { adapter = value; }
        }

        /// <summary>Structured authoring provenance for one abstract Texture; never a runtime reference key.</summary>
        [Serializable]
        internal struct TextureResourceOwner : IEquatable<TextureResourceOwner>
        {
            [SerializeField] private TextureResourceOwnerScope scope;
            [SerializeField] private string outfitIdentity;
            [SerializeField] private string sourceShapeKey;

            internal TextureResourceOwnerScope Scope => scope;
            internal string OutfitIdentity => outfitIdentity;
            internal string SourceShapeKey => sourceShapeKey;
            internal static TextureResourceOwner FigureBase => new TextureResourceOwner(TextureResourceOwnerScope.Figure, null, null);
            internal static TextureResourceOwner FigureFbm(string fbmName) => new TextureResourceOwner(TextureResourceOwnerScope.Figure, null, fbmName);
            internal static TextureResourceOwner Outfit(string identity, string shapeKey = null) => new TextureResourceOwner(TextureResourceOwnerScope.Outfit, identity, shapeKey);

            private TextureResourceOwner(TextureResourceOwnerScope value, string outfit, string shape)
            { scope = value; outfitIdentity = outfit; sourceShapeKey = shape; }

            internal void RenameSourceShapeKey(string value) { sourceShapeKey = value; }

            public bool Equals(TextureResourceOwner other)
            {
                return scope == other.scope
                    && string.Equals(outfitIdentity, other.outfitIdentity, StringComparison.Ordinal)
                    && string.Equals(sourceShapeKey, other.sourceShapeKey, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is TextureResourceOwner other && Equals(other);
            public override int GetHashCode() => HashCode.Combine((int)scope, outfitIdentity, sourceShapeKey);
        }

        /// <summary>
        /// Common authoring identity for one Outfit.  Step 1 intentionally holds no
        /// mesh/material payload: later steps own their respective relations.
        /// </summary>
        [Serializable]
        internal sealed class OutfitEntry
        {
            [SerializeField] private string identity;
            [SerializeField] private string displayName;
            [SerializeField] private OutfitKind kind;
            [SerializeField] private List<OutfitAxisFigureEntry> axisFigures = new List<OutfitAxisFigureEntry>();
            [SerializeField] private List<OutfitMaterialClassificationEntry> materialClassifications = new List<OutfitMaterialClassificationEntry>();
            [SerializeField] private List<OutfitMaterialEntry> materialEntries = new List<OutfitMaterialEntry>();
            [SerializeField] private List<MaterialOutfitTextureEntry> materialOutfitTextureEntries = new List<MaterialOutfitTextureEntry>();
            [SerializeField] private List<FigureMaskEntry> figureMaskEntries = new List<FigureMaskEntry>();
            [SerializeField] private List<OutfitNormalDeclaration> normalDeclarations = new List<OutfitNormalDeclaration>();
            [SerializeField] private List<OutfitNormalEntry> normalEntries = new List<OutfitNormalEntry>();
            [SerializeField] private List<OutfitPbmFollowEntry> pbmFollows = new List<OutfitPbmFollowEntry>();
            [SerializeField] private OutfitCollectionKind collectionKind;
            [SerializeField] private bool useProjectionForFullCollection;
            [SerializeField] private List<OutfitCollectionEntry> collectionEntries = new List<OutfitCollectionEntry>();

            internal string Identity => identity;
            internal string DisplayName => string.IsNullOrWhiteSpace(displayName) ? identity : displayName;
            internal string StoredDisplayName => displayName;
            internal OutfitKind Kind => kind;
            internal IReadOnlyList<OutfitAxisFigureEntry> AxisFigures => axisFigures;
            internal IReadOnlyList<OutfitMaterialClassificationEntry> MaterialClassifications => materialClassifications;
            internal IReadOnlyList<OutfitMaterialEntry> MaterialEntries => materialEntries;
            internal IReadOnlyList<MaterialOutfitTextureEntry> MaterialOutfitTextureEntries => materialOutfitTextureEntries;
            internal IReadOnlyList<FigureMaskEntry> FigureMaskEntries => figureMaskEntries;
            internal IReadOnlyList<OutfitNormalDeclaration> NormalDeclarations => normalDeclarations;
            internal IReadOnlyList<OutfitNormalEntry> NormalEntries => normalEntries;
            internal IReadOnlyList<OutfitPbmFollowEntry> PbmFollows => pbmFollows;
            internal OutfitCollectionKind CollectionKind => collectionKind;
            internal bool UseProjectionForFullCollection => useProjectionForFullCollection;
            internal IReadOnlyList<OutfitCollectionEntry> CollectionEntries => collectionEntries;

            internal OutfitEntry(string value, string name, OutfitKind classification)
            {
                identity = value;
                displayName = name;
                kind = classification;
            }

            internal void RenameDisplayName(string value) { displayName = value; }
            internal void SetAxisFigures(IEnumerable<OutfitAxisFigureEntry> values) { axisFigures = values == null ? new List<OutfitAxisFigureEntry>() : new List<OutfitAxisFigureEntry>(values); }
            internal void SetMaterialClassifications(IEnumerable<OutfitMaterialClassificationEntry> values) { materialClassifications = values == null ? new List<OutfitMaterialClassificationEntry>() : new List<OutfitMaterialClassificationEntry>(values); }
            internal void SetMaterialEntries(IEnumerable<OutfitMaterialEntry> values) { materialEntries = values == null ? new List<OutfitMaterialEntry>() : new List<OutfitMaterialEntry>(values); }
            internal void SetMaterialOutfitTextureEntries(IEnumerable<MaterialOutfitTextureEntry> values) { materialOutfitTextureEntries = values == null ? new List<MaterialOutfitTextureEntry>() : new List<MaterialOutfitTextureEntry>(values); }
            internal void SetFigureMaskEntries(IEnumerable<FigureMaskEntry> values) { figureMaskEntries = values == null ? new List<FigureMaskEntry>() : new List<FigureMaskEntry>(values); }
            internal void SetNormalDeclarations(IEnumerable<OutfitNormalDeclaration> values) { normalDeclarations = values == null ? new List<OutfitNormalDeclaration>() : new List<OutfitNormalDeclaration>(values); }
            internal void SetNormalEntries(IEnumerable<OutfitNormalEntry> values) { normalEntries = values == null ? new List<OutfitNormalEntry>() : new List<OutfitNormalEntry>(values); }
            internal void SetPbmFollows(IEnumerable<OutfitPbmFollowEntry> values) { pbmFollows = values == null ? new List<OutfitPbmFollowEntry>() : new List<OutfitPbmFollowEntry>(values); }
            internal void SetCollection(OutfitCollectionKind value, bool useProjection, IEnumerable<OutfitCollectionEntry> values)
            {
                collectionKind = value;
                useProjectionForFullCollection = useProjection;
                collectionEntries = values == null ? new List<OutfitCollectionEntry>() : new List<OutfitCollectionEntry>(values);
            }
        }

        /// <summary>One shape-key Collection source and its Database-owned immutable copy.</summary>
        [Serializable]
        internal sealed class OutfitCollectionEntry
        {
            [SerializeField] private string shapeKey;
            [SerializeField] private GameObject sourcePrefab;
            [SerializeField] private GameObject collectionPrefab;
            internal string ShapeKey => shapeKey;
            internal GameObject SourcePrefab => sourcePrefab;
            internal GameObject CollectionPrefab => collectionPrefab;
            internal OutfitCollectionEntry(string key, GameObject source, GameObject prefab)
            {
                shapeKey = key;
                sourcePrefab = source;
                collectionPrefab = prefab;
            }
            internal void RebindArtifacts(GameObject source, GameObject prefab)
            {
                sourcePrefab = source;
                collectionPrefab = prefab;
            }
        }

        /// <summary>Database-owned Mesh Outfit artifacts for one Figure shape key.</summary>
        [Serializable]
        internal sealed class OutfitAxisFigureEntry
        {
            [SerializeField] private string shapeKey;
            [SerializeField] private GameObject sourcePrefab;
            [SerializeField] private GameObject mergedPrefab;
            [SerializeField] private GameObject outfitPrefab;
            [SerializeField] private GameObject projectionPrefab;
            [SerializeField] private List<string> sourceMaterialNames = new List<string>();
            internal string ShapeKey => shapeKey;
            internal GameObject SourcePrefab => sourcePrefab;
            internal GameObject MergedPrefab => mergedPrefab;
            internal GameObject OutfitPrefab => outfitPrefab;
            internal GameObject ProjectionPrefab => projectionPrefab;
            internal IReadOnlyList<string> SourceMaterialNames => sourceMaterialNames;
            internal OutfitAxisFigureEntry(string key, GameObject source, GameObject merged, GameObject outfit, GameObject projection, IEnumerable<string> materialNames)
            {
                shapeKey = key; sourcePrefab = source; mergedPrefab = merged; outfitPrefab = outfit; projectionPrefab = projection;
                sourceMaterialNames = materialNames == null ? new List<string>() : new List<string>(materialNames);
            }
            internal void ReplaceDerivedPrefabs(GameObject outfit, GameObject projection)
            { outfitPrefab = outfit; projectionPrefab = projection; }
            internal void ClearSourceMaterialNames()
            { sourceMaterialNames.Clear(); }
            internal void RemoveMergedPrefab() { mergedPrefab = null; }
            internal void RebindArtifacts(GameObject source, GameObject merged, GameObject outfit, GameObject projection)
            {
                sourcePrefab = source;
                mergedPrefab = merged;
                outfitPrefab = outfit;
                projectionPrefab = projection;
            }
        }

        /// <summary>One Outfit-wide classification keyed by the admitted source Material name.</summary>
        [Serializable]
        internal sealed class OutfitMaterialClassificationEntry
        {
            [SerializeField] private string sourceMaterialName;
            [SerializeField] private OutfitMaterialClassification classification;
            [SerializeField] private string entryName;
            internal string SourceMaterialName => sourceMaterialName;
            internal OutfitMaterialClassification Classification => classification;
            internal string EntryName => entryName;
            internal OutfitMaterialClassificationEntry(string sourceName, OutfitMaterialClassification value, string logicalEntryName)
            { sourceMaterialName = sourceName; classification = value; entryName = logicalEntryName; }
        }

        /// <summary>One Include-derived Outfit Material Entry; its logical-name space is local to its Outfit.</summary>
        [Serializable]
        internal sealed class OutfitMaterialEntry
        {
            [SerializeField] private string logicalName;
            [SerializeField] private Material material;
            [SerializeField] private MaterialShaderAdapter adapter;
            internal string LogicalName => logicalName;
            internal Material Material => material;
            internal MaterialShaderAdapter Adapter => adapter;
            internal OutfitMaterialEntry(string name, Material value, MaterialShaderAdapter resolvedAdapter)
            { logicalName = name; material = value; adapter = resolvedAdapter; }
            internal void RebindAdapter(MaterialShaderAdapter value) { adapter = value; }
        }

        /// <summary>One Material Outfit Texture Entry.  It names an authoring-only abstract Texture resource.</summary>
        [Serializable]
        internal sealed class MaterialOutfitTextureEntry
        {
            [SerializeField] private string entryName;
            [SerializeField] private string textureResourceName;
            internal string EntryName => entryName;
            internal string TextureResourceName => textureResourceName;
            internal MaterialOutfitTextureEntry(string name, string resourceName) { entryName = name; textureResourceName = resourceName; }
            internal void RenameTextureResourceName(string value) { textureResourceName = value; }
        }

        /// <summary>One optional Figure Material Entry mask owned by an Outfit; applying it belongs to Spec20.8.</summary>
        [Serializable]
        internal sealed class FigureMaskEntry
        {
            [SerializeField] private string figureMaterialEntryName;
            [SerializeField] private string textureResourceName;
            internal string FigureMaterialEntryName => figureMaterialEntryName;
            internal string TextureResourceName => textureResourceName;
            internal FigureMaskEntry(string materialEntryName, string resourceName) { figureMaterialEntryName = materialEntryName; textureResourceName = resourceName; }
            internal void RenameTextureResourceName(string value) { textureResourceName = value; }
        }

        [Serializable]
        internal sealed class OutfitNormalDeclaration
        {
            [SerializeField] private string materialEntryName;
            internal string MaterialEntryName => materialEntryName;
            internal OutfitNormalDeclaration(string value) { materialEntryName = value; }
        }

        [Serializable]
        internal sealed class OutfitNormalEntry
        {
            [SerializeField] private string materialEntryName;
            [SerializeField] private string shapeKey;
            [SerializeField] private string textureResourceName;
            [SerializeField] private Texture texture;
            internal string MaterialEntryName => materialEntryName;
            internal string ShapeKey => shapeKey;
            internal string TextureResourceName => textureResourceName;
            internal Texture Texture => texture;
            internal OutfitNormalEntry(string material, string shape, string resourceName, Texture value)
            { materialEntryName = material; shapeKey = shape; textureResourceName = resourceName; texture = value; }
            internal void RenameTextureResourceName(string value) { textureResourceName = value; }
        }

        /// <summary>One selected Figure PBM axis and its explicit Base/FBM Outfit variants.</summary>
        [Serializable]
        internal sealed class OutfitPbmFollowEntry
        {
            [SerializeField] private string pbmAxisName;
            [SerializeField] private List<OutfitPbmFollowFigureEntry> figures = new List<OutfitPbmFollowFigureEntry>();
            internal string PbmAxisName => pbmAxisName;
            internal IReadOnlyList<OutfitPbmFollowFigureEntry> Figures => figures;
            internal OutfitPbmFollowEntry(string name, IEnumerable<OutfitPbmFollowFigureEntry> values)
            { pbmAxisName = name; figures = values == null ? new List<OutfitPbmFollowFigureEntry>() : new List<OutfitPbmFollowFigureEntry>(values); }
            internal void RebindFigures(IEnumerable<OutfitPbmFollowFigureEntry> values)
            { figures = values == null ? new List<OutfitPbmFollowFigureEntry>() : new List<OutfitPbmFollowFigureEntry>(values); }
        }

        /// <summary>Database-owned Include-only Outfit Prefab for one selected PBM and one Figure shape key.</summary>
        [Serializable]
        internal sealed class OutfitPbmFollowFigureEntry
        {
            [SerializeField] private string shapeKey;
            // Both sides are Database-owned: SourcePrefab preserves the complete merged
            // source, while Figure is the Include-only Outfit artifact.
            [SerializeField] private GameObject sourcePrefab;
            [SerializeField] private GameObject figure;
            internal string ShapeKey => shapeKey;
            internal GameObject SourcePrefab => sourcePrefab;
            internal GameObject Figure => figure;
            internal OutfitPbmFollowFigureEntry(string key, GameObject source, GameObject artifact)
            { shapeKey = key; sourcePrefab = source; figure = artifact; }
            internal void RebindSourcePrefab(GameObject value) { sourcePrefab = value; }
            internal void RebindFigure(GameObject value) { figure = value; }
        }

        [Serializable]
        internal sealed class TextureResourceEntry
        {
            [SerializeField] private string logicalName;
            [SerializeField] private Texture texture;
            [SerializeField] private TextureResourceOwner owner;
            [SerializeField] private TextureResourceUsage usage;
            // Import provenance is authoring metadata only.  It preserves the source
            // asset identity without retaining an external UnityEngine.Object reference.
            [SerializeField] private string sourceAssetGuid;
            [SerializeField] private long sourceAssetLocalFileId;
            internal string LogicalName => logicalName;
            internal Texture Texture => texture;
            internal TextureResourceOwner Owner => owner;
            internal TextureResourceUsage Usage => usage;
            internal string SourceAssetGuid => sourceAssetGuid;
            internal long SourceAssetLocalFileId => sourceAssetLocalFileId;
            internal TextureResourceEntry(string name, Texture value, TextureResourceOwner valueOwner, TextureResourceUsage valueUsage = TextureResourceUsage.General,
                string importSourceGuid = null, long importSourceLocalFileId = 0)
            { logicalName = name; texture = value; owner = valueOwner; usage = valueUsage; sourceAssetGuid = importSourceGuid; sourceAssetLocalFileId = importSourceLocalFileId; }
            internal void Rename(string name) { logicalName = name; }
            internal void SetTexture(Texture value) { texture = value; }
            internal void SetOwner(TextureResourceOwner value) { owner = value; }
            internal bool MatchesImportSource(string guid, long localFileId)
            {
                return !string.IsNullOrWhiteSpace(guid)
                    && string.Equals(sourceAssetGuid, guid, StringComparison.Ordinal)
                    && sourceAssetLocalFileId == localFileId;
            }
        }

        [Serializable]
        internal sealed class NormalEntry
        {
            [SerializeField] private string materialEntryName;
            [SerializeField] private string shapeKey;
            [SerializeField] private string textureResourceName;
            [SerializeField] private Texture texture;
            internal string MaterialEntryName => materialEntryName;
            internal string ShapeKey => shapeKey;
            internal string TextureResourceName => textureResourceName;
            internal Texture Texture => texture;
            internal NormalEntry(string material, string shape, string textureResource, Texture value) { materialEntryName = material; shapeKey = shape; textureResourceName = textureResource; texture = value; }
            internal void RenameMaterialEntry(string value) { materialEntryName = value; }
            internal void RenameShapeKey(string value) { shapeKey = value; }
            internal void RenameTextureResourceName(string value) { textureResourceName = value; }
            internal void SetTexture(Texture value) { texture = value; }
        }

        /// <summary>Declares one Material Entry as a Figure-owned Normal authoring relation.</summary>
        [Serializable]
        internal sealed class FigureNormalEntry
        {
            [SerializeField] private string materialEntryName;
            internal string MaterialEntryName => materialEntryName;
            internal FigureNormalEntry(string value) { materialEntryName = value; }
            internal void RenameMaterialEntry(string value) { materialEntryName = value; }
        }

        /// <summary>One canonical Figure axis.  Step 2 associates its Database-owned Figure assets.</summary>
        [Serializable]
        internal sealed class FigureAxisEntry
        {
            [SerializeField] private string name;
            [SerializeField] private FigureAxisKind kind;
            [SerializeField] private bool importAllMaterialsAndTextures;
            [SerializeField] private List<AxisFigureEntry> figures = new List<AxisFigureEntry>();
            internal string Name => name;
            internal FigureAxisKind Kind => kind;
            internal bool ImportAllMaterialsAndTextures => importAllMaterialsAndTextures;
            /// <summary>FBM has exactly one entry keyed by its own name; PBM has a Base entry and one entry for every FBM name.</summary>
            internal IReadOnlyList<AxisFigureEntry> Figures => figures;
            internal FigureAxisEntry(string value, FigureAxisKind classification, IEnumerable<AxisFigureEntry> targets = null, bool importMaterialsAndTextures = false)
            {
                name = value;
                kind = classification;
                importAllMaterialsAndTextures = importMaterialsAndTextures;
                if (targets != null) figures = new List<AxisFigureEntry>(targets);
            }
            internal void Rename(string value) { name = value; }
        }

        /// <summary>Database-owned merged Figure that realizes an axis for one shape key (Base or FBM).</summary>
        [Serializable]
        internal sealed class AxisFigureEntry
        {
            [SerializeField] private string fbmName;
            [SerializeField] private GameObject figure;
            internal string FbmName => fbmName;
            internal GameObject Figure => figure;
            internal AxisFigureEntry(string sourceFbmName, GameObject target) { fbmName = sourceFbmName; figure = target; }
            internal void RebindFigure(GameObject value) { figure = value; }
            internal void RenameFbmName(string value) { fbmName = value; }
        }

        /// <summary>One staged Database Figure binding passed to the atomic axis commit.</summary>
        internal readonly struct FigureAxisFigureBinding
        {
            internal FigureAxisFigureBinding(string sourceFbmName, GameObject figure)
            {
                SourceFbmName = sourceFbmName;
                Figure = figure;
            }
            internal string SourceFbmName { get; }
            internal GameObject Figure { get; }
        }

        /// <summary>Uncommitted axis input supplied by the Step 2 Figure transaction.</summary>
        internal readonly struct FigureAxisDraft
        {
            internal FigureAxisDraft(string value, FigureAxisKind classification, bool importMaterialsAndTextures = false) { Name = value; Kind = classification; ImportAllMaterialsAndTextures = importMaterialsAndTextures; }
            internal string Name { get; }
            internal FigureAxisKind Kind { get; }
            internal bool ImportAllMaterialsAndTextures { get; }
        }

        /// <summary>Immutable axis identity admitted by this registry before a transaction stages owned assets.</summary>
        internal readonly struct FigureAxisAdmission
        {
            internal FigureAxisAdmission(string value, FigureAxisKind classification, object token, bool importMaterialsAndTextures = false)
            {
                Name = value;
                Kind = classification;
                ImportAllMaterialsAndTextures = importMaterialsAndTextures;
                issuerToken = token;
            }
            internal string Name { get; }
            internal FigureAxisKind Kind { get; }
            internal bool ImportAllMaterialsAndTextures { get; }
            private readonly object issuerToken;
            internal bool IsIssuedBy(object token) => token != null && ReferenceEquals(issuerToken, token);
        }

        [NonSerialized] private object axisAdmissionToken;
        private object AxisAdmissionToken => axisAdmissionToken ?? (axisAdmissionToken = new object());
        [SerializeField] private List<BaseFigureEntry> baseFigures = new List<BaseFigureEntry>();
        [SerializeField] private List<OutfitEntry> outfits = new List<OutfitEntry>();
        [SerializeField] private List<MaterialEntry> materialEntries = new List<MaterialEntry>();
        [SerializeField] private List<TextureResourceEntry> textureResources = new List<TextureResourceEntry>();
        [SerializeField] private List<FigureNormalEntry> figureNormalEntries = new List<FigureNormalEntry>();
        [SerializeField] private List<NormalEntry> normalEntries = new List<NormalEntry>();
        [SerializeField] private List<FigureAxisEntry> figureAxes = new List<FigureAxisEntry>();
        [SerializeField] private List<string> shapeTags = new List<string>();
        [SerializeField] private List<ShapeEntry> shapes = new List<ShapeEntry>();
        [SerializeField] private GenerationPathSettings generationPaths = new GenerationPathSettings();
        [SerializeField] private int pcmSlots = 10;
        [SerializeField] private List<string> keptRawBlendShapeNames = new List<string>();
        // The first axis transaction seals the complete FBM set. PBM may be added later,
        // but FBM may not: Step 3's raw-BlendShape intersection depends on this boundary.
        [SerializeField] private bool fbmAxesFinalized;
        internal IReadOnlyList<BaseFigureEntry> BaseFigures => baseFigures;
        internal IReadOnlyList<OutfitEntry> Outfits => outfits;
        internal IReadOnlyList<MaterialEntry> MaterialEntries => materialEntries;
        internal IReadOnlyList<TextureResourceEntry> TextureResources => textureResources;
        internal IReadOnlyList<FigureNormalEntry> FigureNormalEntries => figureNormalEntries;
        internal IReadOnlyList<NormalEntry> NormalEntries => normalEntries;
        internal IReadOnlyList<string> ShapeTags => shapeTags;
        internal IReadOnlyList<ShapeEntry> Shapes => shapes;
        internal GenerationPathSettings GenerationPaths => generationPaths ?? new GenerationPathSettings();

        internal bool TrySetGenerationPaths(string registries, string bindings, string materials, string textures, string outfits, out string diagnostic)
        {
            if (!TryValidateGenerationPaths(registries, bindings, materials, textures, outfits, out diagnostic)) return false;
            generationPaths = new GenerationPathSettings(registries, bindings, materials, textures, outfits);
            return true;
        }

        internal static bool TryValidateGenerationPaths(string registries, string bindings, string materials, string textures, string outfits, out string diagnostic)
        {
            diagnostic = null;
            string[] values = { registries, bindings, materials, textures, outfits };
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Length; index++)
            {
                string value = values[index];
                if (string.IsNullOrWhiteSpace(value))
                { diagnostic = "GenerationPathEmpty: Generation output paths must not be empty."; return false; }
                string normalized = value.Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("Assets/", StringComparison.Ordinal)
                    || System.IO.Path.IsPathRooted(value)
                    || normalized.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."))
                { diagnostic = "GenerationPathInvalid: Generation output paths must be relative folders below the selected output root."; return false; }
                if (!resolved.Add(normalized))
                { diagnostic = "GenerationPathDuplicate: Generation output paths must be distinct."; return false; }
            }
            return true;
        }

        internal bool TrySetShapeTags(IReadOnlyList<string> values, out string diagnostic)
        {
            diagnostic = null;
            if (values == null) { diagnostic = "Shape Tag vocabulary is required."; return false; }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]) || !unique.Add(values[i]))
                { diagnostic = "Shape Tag vocabulary contains an empty or duplicate value."; return false; }
            }
            for (int i = 0; i < shapes.Count; i++)
            {
                ShapeEntry shape = shapes[i];
                if (shape == null) continue;
                for (int tagIndex = 0; tagIndex < shape.Tags.Count; tagIndex++)
                    if (!unique.Contains(shape.Tags[tagIndex]))
                    { diagnostic = "Shape Tag is still referenced and cannot be removed: " + shape.Tags[tagIndex] + "; shapeId=" + shape.ShapeId; return false; }
            }
            shapeTags = new List<string>(values);
            return true;
        }

        internal bool TryAddShape(string shapeId, string shapeName, ShapeKind kind, int priority, IReadOnlyList<string> tags, out string diagnostic)
        {
            diagnostic = null;
            if (!IsValidUserName(shapeId)) { diagnostic = "Shape Id must not be empty or contain whitespace."; return false; }
            if (!Enum.IsDefined(typeof(ShapeKind), kind)) { diagnostic = "Shape kind is invalid."; return false; }
            if (shapes.Any(entry => entry != null && string.Equals(entry.ShapeId, shapeId, StringComparison.Ordinal)))
            { diagnostic = "Shape Id already exists: " + shapeId; return false; }
            if (kind == ShapeKind.Morph && tags != null && tags.Count != 0)
            { diagnostic = "Morph Shape does not accept Tags."; return false; }
            if (!TryValidateShapeTags(tags, out diagnostic)) return false;
            shapes.Add(new ShapeEntry(shapeId, shapeName, kind, priority, tags));
            return true;
        }

        internal bool TryUpdateShape(string shapeId, string shapeName, int priority, IReadOnlyList<string> tags, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null) { diagnostic = "Shape was not found: " + shapeId; return false; }
            if (shape.Kind == ShapeKind.Morph && tags != null && tags.Count != 0)
            { diagnostic = "Morph Shape does not accept Tags."; return false; }
            if (!TryValidateShapeTags(tags, out diagnostic)) return false;
            shape.SetShapeName(shapeName);
            shape.SetPriority(priority);
            shape.SetTags(tags);
            return true;
        }

        internal bool TryUpdateShapeAndParts(string shapeId, string shapeName, int priority, IReadOnlyList<string> tags, IReadOnlyList<ShapeEntryDefinition> parts, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            return TryUpdateShapeAndContents(shapeId, shapeName, priority, tags, shape == null ? null : shape.Morphs, parts, out diagnostic);
        }

        // Commits every editable field of a Shape Detail in one structure transaction.
        // The editor deliberately keeps these values in an on-memory draft until this call.
        internal bool TryUpdateShapeAndContents(string shapeId, string shapeName, int priority, IReadOnlyList<string> tags, IReadOnlyList<MorphValue> morphs, IReadOnlyList<ShapeEntryDefinition> parts, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null) { diagnostic = "Shape was not found: " + shapeId; return false; }
            if (shape.Kind == ShapeKind.Morph)
            {
                if (parts == null || parts.Count != 0 || tags != null && tags.Count != 0)
                { diagnostic = "Morph Shape does not accept Tags or Parts entries."; return false; }
                if (!TryValidateShapeMorphs(shape, morphs, out diagnostic)) return false;
            }
            else if (morphs == null || morphs.Count != 0)
            { diagnostic = "Only Morph Shape accepts Morph values."; return false; }
            if (!TryValidateShapeTags(tags, out diagnostic) || !TryValidateShapeParts(parts, out diagnostic)) return false;
            shape.SetShapeName(shapeName); shape.SetPriority(priority); shape.SetTags(tags); shape.SetMorphs(morphs); shape.SetParts(parts);
            return true;
        }

        internal bool TryRemoveShape(string shapeId, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null) { diagnostic = "Shape was not found: " + shapeId; return false; }
            shapes.Remove(shape);
            diagnostic = null;
            return true;
        }

        internal bool TryMoveShape(string shapeId, bool moveUp, out string diagnostic)
        {
            int current = shapes.FindIndex(entry => entry != null && string.Equals(entry.ShapeId, shapeId, StringComparison.Ordinal));
            if (current < 0) { diagnostic = "Shape was not found: " + shapeId; return false; }
            int target = current + (moveUp ? -1 : 1);
            if (target < 0 || target >= shapes.Count) { diagnostic = moveUp ? "Shape is already first." : "Shape is already last."; return false; }
            ShapeEntry value = shapes[current];
            shapes[current] = shapes[target];
            shapes[target] = value;
            diagnostic = null;
            return true;
        }

        internal bool TryAddShapePart(string shapeId, ShapeEntryKind kind, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null) { diagnostic = "Shape was not found: " + shapeId; return false; }
            if (shape.Kind == ShapeKind.Morph) { diagnostic = "Morph Shape does not accept Parts entries."; return false; }
            if (!Enum.IsDefined(typeof(ShapeEntryKind), kind)) { diagnostic = "Shape entry kind is invalid."; return false; }
            shape.AddPart(new ShapeEntryDefinition(kind));
            diagnostic = null;
            return true;
        }

        internal bool TrySetShapeMorphs(string shapeId, IReadOnlyList<MorphValue> values, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null) { diagnostic = "Shape was not found: " + shapeId; return false; }
            if (shape.Kind != ShapeKind.Morph) { diagnostic = "Only Morph Shape accepts Morph values."; return false; }
            if (!TryValidateShapeMorphs(shape, values, out diagnostic)) return false;
            shape.SetMorphs(values);
            diagnostic = null;
            return true;
        }

        private bool TryValidateShapeMorphs(ShapeEntry shape, IReadOnlyList<MorphValue> values, out string diagnostic)
        {
            if (values == null) { diagnostic = "Morph values are required."; return false; }
            var validTargets = new HashSet<string>(figureAxes.Where(axis => axis != null).Select(axis => axis.Name), StringComparer.Ordinal);
            var selectedTargets = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Count; index++)
            {
                MorphValue value = values[index];
                if (!validTargets.Contains(value.Target) || !selectedTargets.Add(value.Target))
                { diagnostic = "Morph values must target unique Figure FBM or PBM axes."; return false; }
            }
            diagnostic = null;
            return true;
        }

        internal bool TrySetShapePartMeshOutfit(string shapeId, int index, string outfitIdentity, out string diagnostic)
        {
            ShapeEntryDefinition part = FindShapePart(shapeId, index, out ShapeEntry shape);
            if (part == null) { diagnostic = "Shape Part was not found."; return false; }
            if (part.Kind != ShapeEntryKind.Mesh) { diagnostic = "Only Mesh entry accepts a Mesh Outfit target."; return false; }
            if (!outfits.Any(outfit => outfit != null && outfit.Kind == OutfitKind.Mesh && string.Equals(outfit.Identity, outfitIdentity, StringComparison.Ordinal)))
            { diagnostic = "Mesh entry target must be a registered Mesh Outfit."; return false; }
            part.SetMeshOutfit(outfitIdentity);
            diagnostic = null;
            return true;
        }

        internal bool TrySetShapePartMaterialTarget(string shapeId, int index, string registryId, string materialEntryName, out string diagnostic)
        {
            ShapeEntryDefinition part = FindShapePart(shapeId, index, out ShapeEntry shape);
            if (part == null) { diagnostic = "Shape Part was not found."; return false; }
            if (part.Kind == ShapeEntryKind.Mesh) { diagnostic = "Mesh entry does not accept a Material Entry target."; return false; }
            bool found = string.IsNullOrEmpty(registryId)
                ? materialEntries.Any(entry => entry != null && string.Equals(entry.LogicalName, materialEntryName, StringComparison.Ordinal))
                : outfits.Any(outfit => outfit != null && outfit.Kind == OutfitKind.Mesh && string.Equals(outfit.Identity, registryId, StringComparison.Ordinal)
                    && outfit.MaterialEntries.Any(entry => entry != null && string.Equals(entry.LogicalName, materialEntryName, StringComparison.Ordinal)));
            if (!found) { diagnostic = "Parts target must be a Figure or Mesh Outfit Material Entry."; return false; }
            part.SetMaterialTarget(registryId, materialEntryName);
            diagnostic = null;
            return true;
        }

        internal bool TrySetShapePartTexture(string shapeId, int index, string textureResourceName, bool useColorize, Color32 color, out string diagnostic)
        {
            ShapeEntryDefinition part = FindShapePart(shapeId, index, out ShapeEntry shape);
            if (part == null) { diagnostic = "Shape Part was not found."; return false; }
            if (part.Kind != ShapeEntryKind.Texture) { diagnostic = "Only Texture entry accepts a Texture resource."; return false; }
            if (!textureResources.Any(entry => entry != null && string.Equals(entry.LogicalName, textureResourceName, StringComparison.Ordinal)))
            { diagnostic = "Texture entry must select a Database Texture resource."; return false; }
            part.SetTexture(textureResourceName, useColorize, color);
            diagnostic = null;
            return true;
        }

        internal bool TrySetShapePartColor(string shapeId, int index, Color32 color, out string diagnostic)
        {
            ShapeEntryDefinition part = FindShapePart(shapeId, index, out ShapeEntry shape);
            if (part == null) { diagnostic = "Shape Part was not found."; return false; }
            if (part.Kind != ShapeEntryKind.Color) { diagnostic = "Only Color entry accepts a Color value."; return false; }
            part.SetColor(color);
            diagnostic = null;
            return true;
        }

        internal bool TrySetShapePartUv(string shapeId, int index, float scaleX, float scaleY, float offsetX, float offsetY, out string diagnostic)
        {
            ShapeEntryDefinition part = FindShapePart(shapeId, index, out ShapeEntry shape);
            if (part == null) { diagnostic = "Shape Part was not found."; return false; }
            if (part.Kind != ShapeEntryKind.Uvset) { diagnostic = "Only UVSet entry accepts UV scale and offset."; return false; }
            if (float.IsNaN(scaleX) || float.IsInfinity(scaleX) || float.IsNaN(scaleY) || float.IsInfinity(scaleY)
                || float.IsNaN(offsetX) || float.IsInfinity(offsetX) || float.IsNaN(offsetY) || float.IsInfinity(offsetY))
            { diagnostic = "UV scale and offset must be finite values."; return false; }
            part.SetUv(scaleX, scaleY, offsetX, offsetY);
            diagnostic = null;
            return true;
        }

        internal bool TryRemoveShapePart(string shapeId, int index, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null || !shape.RemovePart(index)) { diagnostic = "Shape Part was not found."; return false; }
            diagnostic = null;
            return true;
        }

        internal bool TryMoveShapePart(string shapeId, int index, bool moveUp, out string diagnostic)
        {
            ShapeEntry shape = FindShape(shapeId);
            if (shape == null || !shape.MovePart(index, moveUp)) { diagnostic = moveUp ? "Shape Part is already first." : "Shape Part is already last."; return false; }
            diagnostic = null;
            return true;
        }

        private ShapeEntry FindShape(string shapeId) => shapes.FirstOrDefault(entry => entry != null && string.Equals(entry.ShapeId, shapeId, StringComparison.Ordinal));

        private ShapeEntryDefinition FindShapePart(string shapeId, int index, out ShapeEntry shape)
        {
            shape = FindShape(shapeId);
            return shape == null || index < 0 || index >= shape.Parts.Count ? null : shape.Parts[index];
        }

        private bool TryValidateShapeTags(IReadOnlyList<string> values, out string diagnostic)
        {
            diagnostic = null;
            if (values == null) { diagnostic = "Shape Tags are required."; return false; }
            var selected = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (!shapeTags.Contains(values[i]) || !selected.Add(values[i]))
                { diagnostic = "Shape Tags must be unique values from the Database Tag vocabulary."; return false; }
            return true;
        }

        private bool TryValidateShapeParts(IReadOnlyList<ShapeEntryDefinition> values, out string diagnostic)
        {
            diagnostic = null;
            if (values == null) { diagnostic = "Shape Parts are required."; return false; }
            foreach (ShapeEntryDefinition part in values)
            {
                if (part == null || !Enum.IsDefined(typeof(ShapeEntryKind), part.Kind)) { diagnostic = "Shape Part is invalid."; return false; }
                if (part.Kind == ShapeEntryKind.Mesh)
                {
                    if (string.IsNullOrWhiteSpace(part.OutfitIdentity))
                    { diagnostic = "Mesh entry requires a Mesh Outfit target."; return false; }
                    if (!outfits.Any(outfit => outfit != null && outfit.Kind == OutfitKind.Mesh && outfit.Identity == part.OutfitIdentity))
                    { diagnostic = "Mesh entry target must be a registered Mesh Outfit."; return false; }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(part.ProxyEntry))
                    { diagnostic = "Shape entry requires a Material Entry target."; return false; }
                    bool found = string.IsNullOrEmpty(part.RegistryId)
                        ? materialEntries.Any(entry => entry != null && entry.LogicalName == part.ProxyEntry)
                        : outfits.Any(outfit => outfit != null && outfit.Kind == OutfitKind.Mesh && outfit.Identity == part.RegistryId && outfit.MaterialEntries.Any(entry => entry != null && entry.LogicalName == part.ProxyEntry));
                    if (!found) { diagnostic = "Parts target must be a Figure or Mesh Outfit Material Entry."; return false; }
                    if (part.Kind == ShapeEntryKind.Texture)
                    {
                        if (string.IsNullOrWhiteSpace(part.TextureResourceName))
                        { diagnostic = "Texture entry requires a Database Texture resource."; return false; }
                        if (!textureResources.Any(entry => entry != null && entry.LogicalName == part.TextureResourceName))
                        { diagnostic = "Texture entry must select a Database Texture resource."; return false; }
                    }
                }
            }
            return true;
        }

        // Generator preflight uses the same admission contract as a saved Shape Detail.
        // Keeping this seam internal prevents legacy/direct Registry callers from emitting
        // partially configured entries while preserving their incremental authoring API.
        internal bool TryValidateShapePartsForGeneration(IReadOnlyList<ShapeEntryDefinition> values, out string diagnostic)
            => TryValidateShapeParts(values, out diagnostic);

        internal bool TryAddOutfit(string identity, string displayName, OutfitKind kind, out string diagnostic)
        {
            diagnostic = null;
            if (!IsValidUserName(identity))
            {
                diagnostic = "Outfit Id must not be empty or contain whitespace.";
                return false;
            }
            if (outfits.Any(entry => entry != null && string.Equals(entry.Identity, identity, StringComparison.Ordinal)))
            {
                diagnostic = "Outfit Id already exists: " + identity;
                return false;
            }
            if (!Enum.IsDefined(typeof(OutfitKind), kind))
            {
                diagnostic = "Outfit kind is invalid.";
                return false;
            }
            outfits.Add(new OutfitEntry(identity, displayName, kind));
            return true;
        }

        internal bool TryRenameOutfit(string identity, string displayName, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry entry = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (entry == null)
            {
                diagnostic = "Outfit was not found: " + identity;
                return false;
            }
            entry.RenameDisplayName(displayName);
            return true;
        }

        internal bool TryRemoveOutfit(string identity, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry entry = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (entry == null)
            {
                diagnostic = "Outfit was not found: " + identity;
                return false;
            }
            outfits.Remove(entry);
            return true;
        }

        /// <summary>Moves an Outfit within its own TreeView kind ordering without changing its identity or contents.</summary>
        internal bool TryMoveOutfit(string identity, bool moveUp, out string diagnostic)
        {
            diagnostic = null;
            int currentIndex = outfits.FindIndex(entry => entry != null && string.Equals(entry.Identity, identity, StringComparison.Ordinal));
            if (currentIndex < 0)
            {
                diagnostic = "Outfit was not found: " + identity;
                return false;
            }
            OutfitEntry current = outfits[currentIndex];
            int targetIndex = -1;
            if (moveUp)
            {
                for (int index = currentIndex - 1; index >= 0; index--)
                    if (outfits[index] != null && outfits[index].Kind == current.Kind) { targetIndex = index; break; }
            }
            else
            {
                for (int index = currentIndex + 1; index < outfits.Count; index++)
                    if (outfits[index] != null && outfits[index].Kind == current.Kind) { targetIndex = index; break; }
            }
            if (targetIndex < 0)
            {
                diagnostic = moveUp ? "Outfit is already first in its TreeView group." : "Outfit is already last in its TreeView group.";
                return false;
            }
            outfits.RemoveAt(currentIndex);
            outfits.Insert(Math.Min(targetIndex, outfits.Count), current);
            return true;
        }

        internal bool TrySetOutfitAxisFigures(ShapeSyncDatabase database, string identity, IReadOnlyList<OutfitAxisFigureEntry> entries, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh)
            { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (entries == null || entries.Count == 0)
            { diagnostic = "Mesh Outfit requires at least its Base source."; return false; }
            var knownShapeKeys = new HashSet<string>(figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Fbm)
                .Select(axis => axis.Name), StringComparer.Ordinal) { BaseShapeKey };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (OutfitAxisFigureEntry entry in entries)
            {
                if (entry != null && database != null)
                {
                    entry.RebindArtifacts(
                        ResolveDirectIntermediateChild(database, entry.SourcePrefab),
                        ResolveDirectIntermediateChild(database, entry.MergedPrefab),
                        ResolveDirectIntermediateChild(database, entry.OutfitPrefab),
                        ResolveDirectIntermediateChild(database, entry.ProjectionPrefab));
                }
                if (entry == null || string.IsNullOrWhiteSpace(entry.ShapeKey) || !keys.Add(entry.ShapeKey) || !knownShapeKeys.Contains(entry.ShapeKey))
                { diagnostic = "Mesh Outfit axis shape key is invalid or duplicated."; return false; }
                if (entry.SourcePrefab == null || entry.OutfitPrefab == null)
                { diagnostic = "Mesh Outfit axis requires Source and Outfit Prefabs."; return false; }
                bool isBaseAxis = entry.ShapeKey == BaseShapeKey;
                if (isBaseAxis && (entry.SourceMaterialNames == null || entry.SourceMaterialNames.Count == 0 || entry.SourceMaterialNames.Any(string.IsNullOrWhiteSpace)))
                { diagnostic = "Mesh Outfit Base axis requires recorded source Material identities."; return false; }
                if (!IsDirectIntermediateChild(database, entry.SourcePrefab))
                { diagnostic = "Mesh Outfit Source Prefab must be a direct Database Intermediate child: " + entry.SourcePrefab.name; return false; }
                if (!IsDirectIntermediateChild(database, entry.OutfitPrefab))
                { diagnostic = "Mesh Outfit Prefab must be a direct Database Intermediate child: " + entry.OutfitPrefab.name; return false; }
                if (entry.MergedPrefab != null && !IsDirectIntermediateChild(database, entry.MergedPrefab))
                { diagnostic = "Mesh Outfit Merged Prefab must be a direct Database Intermediate child: " + entry.MergedPrefab.name; return false; }
                if (entry.ProjectionPrefab != null && !IsDirectIntermediateChild(database, entry.ProjectionPrefab))
                { diagnostic = "Mesh Outfit Projection Prefab must be a direct Database Intermediate child: " + entry.ProjectionPrefab.name; return false; }
            }
            if (!keys.Contains(BaseShapeKey)) { diagnostic = "Mesh Outfit requires a Base source."; return false; }
            outfit.SetAxisFigures(entries);
            return true;
        }

        /// <summary>Saves the Outfit-wide source Material classification table without inferring source names or entries.</summary>
        internal bool TrySetOutfitMaterialClassifications(string identity, IReadOnlyList<OutfitMaterialClassificationEntry> entries, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh)
            { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (outfit.MaterialClassifications.Count != 0)
            { diagnostic = "Mesh Outfit Material classifications are fixed after Save. Remove and recreate the Outfit to classify it again."; return false; }
            if (entries == null || entries.Count == 0)
            { diagnostic = "Mesh Outfit Material classifications are required."; return false; }
            var sourceNames = new HashSet<string>(StringComparer.Ordinal);
            var includeEntryNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (OutfitMaterialClassificationEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.SourceMaterialName) || !sourceNames.Add(entry.SourceMaterialName)
                    || !Enum.IsDefined(typeof(OutfitMaterialClassification), entry.Classification))
                { diagnostic = "Mesh Outfit Material classifications must have distinct source Materials and valid classifications."; return false; }
                if (entry.Classification == OutfitMaterialClassification.Include)
                {
                    if (!IsValidUserName(entry.EntryName) || !includeEntryNames.Add(entry.EntryName))
                    { diagnostic = "Included Outfit Material Entry Names must be distinct and contain no whitespace."; return false; }
                }
                else if (!string.IsNullOrEmpty(entry.EntryName))
                { diagnostic = "Excluded or Projection Materials must not define an Entry Name."; return false; }
            }
            outfit.SetMaterialClassifications(entries);
            return true;
        }

        /// <summary>
        /// Saves the Texture Entry collection of a Material Outfit.  The collection is
        /// deliberately a list of abstract Texture names: it has no source Prefab,
        /// Material classification, shape key, or runtime payload.
        /// </summary>
        internal bool TrySetMaterialOutfitTextureEntries(string identity, IReadOnlyList<MaterialOutfitTextureEntry> entries, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Material)
            { diagnostic = "Material Outfit was not found: " + identity; return false; }
            if (entries == null) { diagnostic = "Material Outfit Texture Entries are required as a collection."; return false; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var resources = new HashSet<string>(StringComparer.Ordinal);
            foreach (MaterialOutfitTextureEntry entry in entries)
            {
                TextureResourceEntry resource = entry == null ? null : textureResources.FirstOrDefault(value => value != null && value.LogicalName == entry.TextureResourceName);
                if (entry == null || !IsValidUserName(entry.EntryName) || !names.Add(entry.EntryName)
                    || string.IsNullOrWhiteSpace(entry.TextureResourceName) || !resources.Add(entry.TextureResourceName)
                    || resource == null || resource.Owner.Scope != TextureResourceOwnerScope.Outfit
                    || !string.Equals(resource.Owner.OutfitIdentity, identity, StringComparison.Ordinal)
                    || resource.Usage != TextureResourceUsage.MaterialOutfit)
                { diagnostic = "Material Outfit Texture Entries must be distinct, owner-matched abstract Textures."; return false; }
            }
            outfit.SetMaterialOutfitTextureEntries(entries);
            return true;
        }

        /// <summary>
        /// Saves optional Figure Masks for one Outfit.  The target is always a Figure
        /// Material Entry; the mask itself is an Outfit-owned abstract Texture.
        /// </summary>
        internal bool TrySetFigureMaskEntries(string identity, IReadOnlyList<FigureMaskEntry> entries, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh) { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (entries == null) { diagnostic = "Figure Masks are required as a collection."; return false; }
            var targets = new HashSet<string>(StringComparer.Ordinal);
            var resources = new HashSet<string>(StringComparer.Ordinal);
            foreach (FigureMaskEntry entry in entries)
            {
                TextureResourceEntry resource = entry == null ? null : textureResources.FirstOrDefault(value => value != null && value.LogicalName == entry.TextureResourceName);
                if (entry == null || !ContainsMaterialEntryName(entry.FigureMaterialEntryName) || !targets.Add(entry.FigureMaterialEntryName)
                    || string.IsNullOrWhiteSpace(entry.TextureResourceName) || !resources.Add(entry.TextureResourceName)
                    || resource == null || resource.Owner.Scope != TextureResourceOwnerScope.Outfit
                    || !string.Equals(resource.Owner.OutfitIdentity, identity, StringComparison.Ordinal)
                    || resource.Usage != TextureResourceUsage.FigureMask)
                { diagnostic = "Figure Masks must target distinct existing Figure Material Entries and owner-matched abstract Textures."; return false; }
            }
            outfit.SetFigureMaskEntries(entries);
            return true;
        }

        internal bool TrySetFigureNormalEntries(IReadOnlyList<string> materialEntryNames, out Texture[] removedTextures, out string diagnostic)
        {
            removedTextures = Array.Empty<Texture>();
            diagnostic = null;
            if (materialEntryNames == null) { diagnostic = "Figure Normal Entry list is required."; return false; }
            var selected = new HashSet<string>(materialEntryNames, StringComparer.Ordinal);
            if (selected.Any(string.IsNullOrWhiteSpace) || selected.Count != materialEntryNames.Count
                || selected.Any(name => !materialEntries.Any(entry => entry != null && entry.LogicalName == name)))
            { diagnostic = "Figure Normal Entries must be distinct existing Material Entries."; return false; }
            removedTextures = normalEntries.Where(entry => entry != null && !selected.Contains(entry.MaterialEntryName))
                .Select(entry => entry.Texture).Where(texture => texture != null).Distinct().ToArray();
            normalEntries.RemoveAll(entry => entry != null && !selected.Contains(entry.MaterialEntryName));
            figureNormalEntries = materialEntryNames.Select(name => new FigureNormalEntry(name)).ToList();
            return true;
        }

        internal bool TrySetNormalEntry(string materialEntryName, string shapeKey, Texture texture, string textureResourceName, out string diagnostic)
        {
            diagnostic = null;
            if (!materialEntries.Any(entry => entry != null && entry.LogicalName == materialEntryName)) { diagnostic = "Normal requires an existing Material Entry."; return false; }
            if (!figureNormalEntries.Any(entry => entry != null && entry.MaterialEntryName == materialEntryName)) { diagnostic = "Normal requires a declared Figure Normal Entry."; return false; }
            bool isBase = string.Equals(shapeKey, BaseShapeKey, StringComparison.Ordinal);
            bool isFbm = figureAxes.Any(axis => axis != null && axis.Kind == FigureAxisKind.Fbm && axis.Name == shapeKey);
            if (!isBase && !isFbm) { diagnostic = "Normal Shape key must be Base or an existing FBM."; return false; }
            NormalEntry entry = normalEntries.Find(item => item != null && item.MaterialEntryName == materialEntryName && item.ShapeKey == shapeKey);
            if (entry != null) normalEntries.Remove(entry);
            if (!string.IsNullOrWhiteSpace(textureResourceName))
            {
                if (!textureResources.Any(resource => resource != null && resource.LogicalName == textureResourceName)) { diagnostic = "Normal requires an existing Texture Entry logical name."; return false; }
                TextureResourceEntry resource = textureResources.FirstOrDefault(item => item != null && item.LogicalName == textureResourceName);
                if (resource == null || resource.Texture != texture) { diagnostic = "Normal Texture Entry logical name does not resolve its Texture."; return false; }
                normalEntries.Add(new NormalEntry(materialEntryName, shapeKey, textureResourceName, texture));
            }
            return true;
        }

        internal bool TrySetNormalEntry(string materialEntryName, string shapeKey, Texture texture, out string diagnostic)
        {
            string textureResourceName = texture == null ? null : textureResources.FirstOrDefault(resource => resource != null && resource.Texture == texture)?.LogicalName;
            return TrySetNormalEntry(materialEntryName, shapeKey, texture, textureResourceName, out diagnostic);
        }

        /// <summary>Removes one FBM axis and invalidates its PBM / Extra Morph dependents.</summary>
        internal bool TryRemoveFbmAxis(ShapeSyncDatabase database, string axisName, out GameObject[] removedFigures, out Texture[] orphanedTextures, out string diagnostic)
        {
            removedFigures = Array.Empty<GameObject>();
            orphanedTextures = Array.Empty<Texture>();
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            FigureAxisEntry target = figureAxes.FirstOrDefault(axis => axis != null && axis.Kind == FigureAxisKind.Fbm && axis.Name == axisName);
            if (target == null)
            {
                diagnostic = "FBM entry was not found: " + axisName;
                return false;
            }
            GameObject[] removedPbmFigures = figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Pbm)
                .SelectMany(axis => axis.Figures ?? Array.Empty<AxisFigureEntry>())
                .Where(binding => binding != null && binding.Figure != null)
                .Select(binding => binding.Figure)
                .ToArray();
            removedFigures = target.Figures.Where(binding => binding != null && binding.Figure != null).Select(binding => binding.Figure)
                .Concat(removedPbmFigures).Distinct().ToArray();
            figureAxes.RemoveAll(axis => axis != null && axis.Kind == FigureAxisKind.Pbm);
            keptRawBlendShapeNames.Clear();
            Texture[] removedTextures = normalEntries.Where(entry => entry != null && entry.ShapeKey == axisName).Select(entry => entry.Texture).Where(texture => texture != null).ToArray();
            normalEntries.RemoveAll(entry => entry != null && entry.ShapeKey == axisName);
            Texture[] importedTextures = textureResources
                .Where(entry => IsFbmImportedTextureResource(entry, axisName))
                .Select(entry => entry.Texture)
                .Where(texture => texture != null)
                .ToArray();
            string[] importedResourceNames = textureResources
                .Where(entry => IsFbmImportedTextureResource(entry, axisName))
                .Select(entry => entry.LogicalName)
                .ToArray();
            if (importedResourceNames.Length != 0)
            {
                var removedResourceNameSet = new HashSet<string>(importedResourceNames, StringComparer.Ordinal);
                textureResources.RemoveAll(entry => entry != null && removedResourceNameSet.Contains(entry.LogicalName));
                foreach (MaterialEntry material in materialEntries)
                    if (material != null && material.TextureResourceNames != null)
                        material.SetTextureResourceNames(material.TextureResourceNames.Where(name => !removedResourceNameSet.Contains(name)));
            }
            figureAxes.Remove(target);
            fbmAxesFinalized = figureAxes.Any(axis => axis != null && axis.Kind == FigureAxisKind.Fbm);
            if (!fbmAxesFinalized) keptRawBlendShapeNames.Clear();
            orphanedTextures = removedTextures.Concat(importedTextures)
                .Where(texture => !normalEntries.Any(entry => entry != null && entry.Texture == texture)
                    && !textureResources.Any(entry => entry != null && entry.Texture == texture))
                .Distinct()
                .ToArray();
            return true;
        }

        /// <summary>Removes one saved PBM axis and every Base/FBM Figure it owns.</summary>
        internal bool TryRemovePbmAxis(ShapeSyncDatabase database, string axisName, out GameObject[] removedFigures, out string diagnostic)
        {
            removedFigures = Array.Empty<GameObject>();
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            FigureAxisEntry target = figureAxes.FirstOrDefault(axis => axis != null && axis.Kind == FigureAxisKind.Pbm && axis.Name == axisName);
            if (target == null) { diagnostic = "PBM entry was not found: " + axisName; return false; }
            removedFigures = target.Figures.Where(binding => binding != null && binding.Figure != null)
                .Select(binding => binding.Figure).Distinct().ToArray();
            figureAxes.Remove(target);
            return true;
        }

        /// <summary>Renames a saved PBM and its Base/FBM Database Figures without changing their payload.</summary>
        internal bool TryRenamePbmAxis(ShapeSyncDatabase database, string currentName, string replacementName, out string diagnostic)
        {
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            FigureAxisEntry target = figureAxes.FirstOrDefault(axis => axis != null && axis.Kind == FigureAxisKind.Pbm && axis.Name == currentName);
            if (target == null) { diagnostic = "PBM entry was not found: " + currentName; return false; }
            if (!IsValidUserName(replacementName) || string.Equals(replacementName, BaseShapeKey, StringComparison.Ordinal)
                || BlendShapeReservedPrefixes.IsReserved(replacementName)
                || figureAxes.Any(axis => axis != null && axis != target && axis.Name == replacementName))
            { diagnostic = "Replacement PBM name is invalid or already exists: " + replacementName; return false; }
            foreach (AxisFigureEntry binding in target.Figures)
            {
                string nextFigureName = GetPbmFigureName(binding.FbmName, replacementName);
                Transform existing = database.transform.Find("Intermediate/" + nextFigureName);
                if (existing != null && existing.gameObject != binding.Figure)
                { diagnostic = "Database Figure name already exists: " + nextFigureName; return false; }
            }
            target.Rename(replacementName);
            foreach (AxisFigureEntry binding in target.Figures) binding.Figure.name = GetPbmFigureName(binding.FbmName, replacementName);
            return true;
        }

        internal bool TryPreparePbmReplacement(ShapeSyncDatabase database, string currentName, string replacementName,
            IReadOnlyList<FigureAxisFigureBinding> bindings, out int replacementIndex, out GameObject[] removedFigures, out string diagnostic)
        {
            replacementIndex = -1; removedFigures = Array.Empty<GameObject>(); diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            FigureAxisEntry target = figureAxes.FirstOrDefault(axis => axis != null && axis.Kind == FigureAxisKind.Pbm && axis.Name == currentName);
            if (target == null) { diagnostic = "PBM entry was not found: " + currentName; return false; }
            if (!IsValidUserName(replacementName) || string.Equals(replacementName, BaseShapeKey, StringComparison.Ordinal)
                || BlendShapeReservedPrefixes.IsReserved(replacementName)
                || figureAxes.Any(axis => axis != null && axis != target && axis.Name == replacementName))
            { diagnostic = "Replacement PBM name is invalid or already exists: " + replacementName; return false; }
            var expected = new HashSet<string>(figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Fbm).Select(axis => axis.Name), StringComparer.Ordinal) { BaseShapeKey };
            if (bindings == null || !new HashSet<string>(bindings.Select(binding => binding.SourceFbmName), StringComparer.Ordinal).SetEquals(expected)
                || bindings.Any(binding => binding.Figure == null))
            { diagnostic = "PBM replacement requires one staged Base Figure and one staged Figure for every FBM."; return false; }
            replacementIndex = figureAxes.IndexOf(target);
            removedFigures = target.Figures.Where(binding => binding?.Figure != null).Select(binding => binding.Figure).ToArray();
            figureAxes.Remove(target);
            return true;
        }

        internal bool CommitPbmReplacement(ShapeSyncDatabase database, string replacementName,
            IReadOnlyList<FigureAxisFigureBinding> bindings, int replacementIndex, out string diagnostic)
        {
            diagnostic = null;
            if (bindings == null || bindings.Count == 0) { diagnostic = "PBM replacement requires staged Figure bindings."; return false; }
            var expected = new HashSet<string>(figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Fbm).Select(axis => axis.Name), StringComparer.Ordinal) { BaseShapeKey };
            var keys = new HashSet<string>(bindings.Select(binding => binding.SourceFbmName), StringComparer.Ordinal);
            if (!keys.SetEquals(expected) || bindings.Any(binding => binding.Figure == null || binding.Figure.name != GetPbmFigureName(binding.SourceFbmName, replacementName) || !IsDirectIntermediateChild(database, binding.Figure)))
            { diagnostic = "PBM replacement bindings are invalid."; return false; }
            figureAxes.Insert(Mathf.Clamp(replacementIndex, 0, figureAxes.Count), new FigureAxisEntry(replacementName, FigureAxisKind.Pbm,
                bindings.Select(binding => new AxisFigureEntry(binding.SourceFbmName, binding.Figure))));
            return true;
        }

        /// <summary>
        /// Begins replacing one FBM definition.  PBM and persisted Extra Morph choices are
        /// derived from the complete FBM set and therefore become invalid.  Figure-owned
        /// Normal relations are retained (and re-keyed on rename); PCM is independent.
        /// The caller must remove returned assets, attach the replacement Figure, then call
        /// <see cref="CommitFbmReplacement"/> inside the same snapshot transaction.
        /// </summary>
        internal bool TryPrepareFbmReplacement(ShapeSyncDatabase database, string currentName, string replacementName,
            out int replacementIndex, out GameObject[] removedFigures, out Texture[] orphanedTextures, out string diagnostic)
        {
            replacementIndex = -1;
            removedFigures = Array.Empty<GameObject>();
            orphanedTextures = Array.Empty<Texture>();
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            FigureAxisEntry target = figureAxes.FirstOrDefault(axis => axis != null && axis.Kind == FigureAxisKind.Fbm && axis.Name == currentName);
            if (target == null) { diagnostic = "FBM entry was not found: " + currentName; return false; }
            replacementIndex = figureAxes.IndexOf(target);
            if (!IsValidUserName(replacementName) || string.Equals(replacementName, BaseShapeKey, StringComparison.Ordinal)
                || BlendShapeReservedPrefixes.IsReserved(replacementName))
            { diagnostic = "Replacement FBM name is invalid: " + replacementName; return false; }
            if (figureAxes.Any(axis => axis != null && axis != target && axis.Name == replacementName))
            { diagnostic = "Replacement FBM name already exists: " + replacementName; return false; }

            GameObject[] removedPbmFigures = figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Pbm)
                .SelectMany(axis => axis.Figures ?? Array.Empty<AxisFigureEntry>())
                .Where(binding => binding != null && binding.Figure != null).Select(binding => binding.Figure).ToArray();
            removedFigures = target.Figures.Where(binding => binding != null && binding.Figure != null).Select(binding => binding.Figure)
                .Concat(removedPbmFigures).Distinct().ToArray();
            figureAxes.RemoveAll(axis => axis != null && (axis == target || axis.Kind == FigureAxisKind.Pbm));
            keptRawBlendShapeNames.Clear();

            foreach (NormalEntry normal in normalEntries.Where(entry => entry != null && entry.ShapeKey == currentName))
                normal.RenameShapeKey(replacementName);

            string[] importedResourceNames = textureResources.Where(entry => IsFbmImportedTextureResource(entry, currentName))
                .Select(entry => entry.LogicalName).ToArray();
            Texture[] importedTextures = textureResources.Where(entry => IsFbmImportedTextureResource(entry, currentName))
                .Select(entry => entry.Texture).Where(texture => texture != null).ToArray();
            if (importedResourceNames.Length != 0)
            {
                var removedNames = new HashSet<string>(importedResourceNames, StringComparer.Ordinal);
                textureResources.RemoveAll(entry => entry != null && removedNames.Contains(entry.LogicalName));
                foreach (MaterialEntry material in materialEntries)
                    if (material != null) material.SetTextureResourceNames(material.TextureResourceNames.Where(name => !removedNames.Contains(name)));
            }
            orphanedTextures = importedTextures.Where(texture => !normalEntries.Any(entry => entry != null && entry.Texture == texture)
                    && !textureResources.Any(entry => entry != null && entry.Texture == texture)).Distinct().ToArray();
            return true;
        }

        /// <summary>Completes a replacement prepared by <see cref="TryPrepareFbmReplacement"/>.</summary>
        internal bool CommitFbmReplacement(ShapeSyncDatabase database, string replacementName, GameObject replacementFigure,
            bool importMaterialsAndTextures, int replacementIndex, out string diagnostic)
        {
            diagnostic = null;
            if (replacementFigure == null || replacementFigure.name != replacementName || !IsDirectIntermediateChild(database, replacementFigure))
            { diagnostic = "Replacement FBM Figure must be a named direct child of Database Intermediate."; return false; }
            if (figureAxes.Any(axis => axis == null || axis.Name == replacementName))
            { diagnostic = "Replacement FBM name already exists or registry entry is invalid: " + replacementName; return false; }
            var replacement = new FigureAxisEntry(replacementName, FigureAxisKind.Fbm,
                new[] { new AxisFigureEntry(replacementName, replacementFigure) }, importMaterialsAndTextures);
            figureAxes.Insert(Mathf.Clamp(replacementIndex, 0, figureAxes.Count), replacement);
            fbmAxesFinalized = true;
            return true;
        }

        /// <summary>Renames an FBM without changing its Database-owned Figure payload.</summary>
        internal bool TryRenameFbmAxis(ShapeSyncDatabase database, string currentName, string replacementName,
            out GameObject[] removedPbmFigures, out string diagnostic)
        {
            removedPbmFigures = Array.Empty<GameObject>();
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            FigureAxisEntry target = figureAxes.FirstOrDefault(axis => axis != null && axis.Kind == FigureAxisKind.Fbm && axis.Name == currentName);
            if (target == null) { diagnostic = "FBM entry was not found: " + currentName; return false; }
            if (!IsValidUserName(replacementName) || string.Equals(replacementName, BaseShapeKey, StringComparison.Ordinal) || BlendShapeReservedPrefixes.IsReserved(replacementName)
                || figureAxes.Any(axis => axis != target && axis != null && axis.Name == replacementName))
            { diagnostic = "Replacement FBM name is invalid or already exists: " + replacementName; return false; }
            GameObject figure = target.Figures.Single().Figure;
            if (database.transform.Find("Intermediate/" + replacementName) != null) { diagnostic = "Database Figure name already exists: " + replacementName; return false; }
            TextureResourceEntry[] ownedResources = textureResources.Where(entry => IsFbmImportedTextureResource(entry, currentName)).ToArray();
            // A user may have renamed an imported Texture Entry.  Its ownership remains
            // the FBM relation; a display name is not an ownership key and must not be
            // parsed as though it still had the generated <FBM>_ prefix.
            string prefix = currentName + "_";
            var resourceRenames = ownedResources.Where(entry => entry.LogicalName.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(entry => entry.LogicalName, entry => replacementName + entry.LogicalName.Substring(prefix.Length), StringComparer.Ordinal);
            if (resourceRenames.Values.Distinct(StringComparer.Ordinal).Count() != resourceRenames.Count
                || resourceRenames.Values.Any(next => textureResources.Any(entry => entry != null && !resourceRenames.ContainsKey(entry.LogicalName) && entry.LogicalName == next)))
            { diagnostic = "Replacement FBM Texture Entry name already exists."; return false; }
            if (!TryClearPbmAndExtraMorphsForFbmRedefinition(database, out removedPbmFigures, out diagnostic)) return false;
            figure.name = replacementName;
            target.Rename(replacementName);
            foreach (AxisFigureEntry binding in target.Figures) binding.RenameFbmName(replacementName);
            foreach (NormalEntry normal in normalEntries.Where(entry => entry != null && entry.ShapeKey == currentName)) normal.RenameShapeKey(replacementName);
            foreach (TextureResourceEntry resource in ownedResources)
            {
                if (resourceRenames.TryGetValue(resource.LogicalName, out string nextName)) resource.Rename(nextName);
                TextureResourceOwner owner = resource.Owner;
                owner.RenameSourceShapeKey(replacementName);
                resource.SetOwner(owner);
                if (resource.Texture != null) resource.Texture.name = resource.LogicalName;
            }
            foreach (MaterialEntry material in materialEntries)
                if (material != null) material.SetTextureResourceNames(material.TextureResourceNames.Select(name => resourceRenames.TryGetValue(name, out string next) ? next : name));
            // NormalEntry keeps the Texture Resource logical name as its authoring
            // relation.  FBM-owned Normal resources now use the same <FBM>_ prefix
            // as every other FBM resource, so an FBM rename must propagate this
            // reference together with the Material Entry resource table.
            foreach (NormalEntry normal in normalEntries)
                if (normal != null && resourceRenames.TryGetValue(normal.TextureResourceName, out string nextName)) normal.RenameTextureResourceName(nextName);
            return true;
        }

        internal bool TrySetOutfitNormalDeclarations(string identity, IReadOnlyList<string> materialEntryNames, out Texture[] removedTextures, out string diagnostic)
        {
            removedTextures = Array.Empty<Texture>();
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh) { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (materialEntryNames == null) { diagnostic = "Outfit Normal Entry list is required."; return false; }
            var names = new HashSet<string>(materialEntryNames, StringComparer.Ordinal);
            if (names.Count != materialEntryNames.Count || names.Any(string.IsNullOrWhiteSpace)
                || names.Any(name => !outfit.MaterialEntries.Any(entry => entry != null && entry.LogicalName == name)))
            { diagnostic = "Outfit Normal Entries must be distinct existing Include Material Entries."; return false; }
            removedTextures = outfit.NormalEntries.Where(entry => entry != null && !names.Contains(entry.MaterialEntryName))
                .Select(entry => entry.Texture).Where(texture => texture != null).Distinct().ToArray();
            outfit.SetNormalEntries(outfit.NormalEntries.Where(entry => entry != null && names.Contains(entry.MaterialEntryName)));
            outfit.SetNormalDeclarations(materialEntryNames.Select(name => new OutfitNormalDeclaration(name)));
            return true;
        }

        internal bool TrySetOutfitNormalEntry(string identity, string materialEntryName, string shapeKey, Texture texture, string textureResourceName, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh) { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (!outfit.NormalDeclarations.Any(entry => entry != null && entry.MaterialEntryName == materialEntryName))
            { diagnostic = "Outfit Normal requires a declared Include Material Entry."; return false; }
            if (!outfit.AxisFigures.Any(axis => axis != null && axis.ShapeKey == shapeKey))
            { diagnostic = "Outfit Normal Shape key must be Base or an imported FBM."; return false; }
            OutfitNormalEntry existing = outfit.NormalEntries.FirstOrDefault(entry => entry != null && entry.MaterialEntryName == materialEntryName && entry.ShapeKey == shapeKey);
            var values = outfit.NormalEntries.Where(entry => entry != existing).ToList();
            if (texture != null)
            {
                TextureResourceEntry resource = textureResources.FirstOrDefault(entry => entry != null && entry.LogicalName == textureResourceName);
                if (resource == null || resource.Texture != texture || resource.Owner.Scope != TextureResourceOwnerScope.Outfit
                    || !string.Equals(resource.Owner.OutfitIdentity, identity, StringComparison.Ordinal)
                    || !string.Equals(resource.Owner.SourceShapeKey, shapeKey, StringComparison.Ordinal))
                { diagnostic = "Outfit Normal requires a matching Outfit-owned Texture Entry."; return false; }
                values.Add(new OutfitNormalEntry(materialEntryName, shapeKey, textureResourceName, texture));
            }
            outfit.SetNormalEntries(values);
            return true;
        }

        /// <summary>Persists only explicit Figure-PBM selections and their complete Base/FBM Outfit Prefab set.</summary>
        internal bool TrySetOutfitPbmFollows(ShapeSyncDatabase database, string identity,
            IReadOnlyList<OutfitPbmFollowEntry> entries, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh)
            { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (entries == null) { diagnostic = "PBM follow selection is required."; return false; }

            var knownPbmNames = new HashSet<string>(figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Pbm)
                .Select(axis => axis.Name), StringComparer.Ordinal);
            var expectedShapeKeys = new HashSet<string>(figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Fbm)
                .Select(axis => axis.Name), StringComparer.Ordinal) { BaseShapeKey };
            var selectedPbmNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (OutfitPbmFollowEntry entry in entries)
            {
                if (entry == null || !knownPbmNames.Contains(entry.PbmAxisName) || !selectedPbmNames.Add(entry.PbmAxisName))
                { diagnostic = "PBM follow selection must contain distinct existing Figure PBM axes."; return false; }
                var shapeKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (OutfitPbmFollowFigureEntry figure in entry.Figures ?? Array.Empty<OutfitPbmFollowFigureEntry>())
                {
                    if (figure != null && database != null)
                    {
                        figure.RebindSourcePrefab(ResolveDirectIntermediateChild(database, figure.SourcePrefab));
                        figure.RebindFigure(ResolveDirectIntermediateChild(database, figure.Figure));
                    }
                    if (figure == null || !expectedShapeKeys.Contains(figure.ShapeKey) || !shapeKeys.Add(figure.ShapeKey)
                        || figure.SourcePrefab == null || !IsDirectIntermediateChild(database, figure.SourcePrefab)
                        || figure.Figure == null || !IsDirectIntermediateChild(database, figure.Figure))
                    { diagnostic = "PBM follow requires Database-owned Source and Outfit Prefabs for Base and every FBM."; return false; }
                }
                if (!shapeKeys.SetEquals(expectedShapeKeys))
                { diagnostic = "PBM follow requires one Outfit Prefab for Base and every FBM."; return false; }
            }
            outfit.SetPbmFollows(entries);
            return true;
        }

        /// <summary>Persists the single Collection declaration and its complete Base/FBM Database-owned prefab set.</summary>
        internal bool TrySetOutfitCollection(ShapeSyncDatabase database, string identity, OutfitCollectionKind kind,
            bool useProjectionForFullCollection, IReadOnlyList<OutfitCollectionEntry> entries, out string diagnostic)
        {
            diagnostic = null;
            OutfitEntry outfit = outfits.FirstOrDefault(item => item != null && string.Equals(item.Identity, identity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != OutfitKind.Mesh) { diagnostic = "Mesh Outfit was not found: " + identity; return false; }
            if (!Enum.IsDefined(typeof(OutfitCollectionKind), kind)) { diagnostic = "Collection kind is invalid."; return false; }
            if (kind == OutfitCollectionKind.None)
            {
                if (useProjectionForFullCollection || (entries?.Count ?? 0) != 0)
                { diagnostic = "No Collection cannot retain Collection Prefabs or Projection selection."; return false; }
                outfit.SetCollection(kind, false, Array.Empty<OutfitCollectionEntry>());
                return true;
            }
            bool hasProjection = outfit.AxisFigures.Any(axis => axis != null && axis.ProjectionPrefab != null);
            if (useProjectionForFullCollection && (kind != OutfitCollectionKind.Full || !hasProjection))
            { diagnostic = "Projection can be selected only for Full Collection when a Projection Prefab exists."; return false; }
            var expectedShapeKeys = new HashSet<string>(figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Fbm)
                .Select(axis => axis.Name), StringComparer.Ordinal) { BaseShapeKey };
            var actualShapeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (OutfitCollectionEntry entry in entries ?? Array.Empty<OutfitCollectionEntry>())
            {
                if (entry != null && database != null)
                {
                    entry.RebindArtifacts(
                        ResolveDirectIntermediateChild(database, entry.SourcePrefab),
                        ResolveDirectIntermediateChild(database, entry.CollectionPrefab));
                }
                if (entry == null || !expectedShapeKeys.Contains(entry.ShapeKey) || !actualShapeKeys.Add(entry.ShapeKey)
                    || entry.SourcePrefab == null || !IsDirectIntermediateChild(database, entry.SourcePrefab)
                    || entry.CollectionPrefab == null || !IsDirectIntermediateChild(database, entry.CollectionPrefab))
                { diagnostic = "Collection requires one Database-owned Prefab for Base and every FBM."; return false; }
            }
            if (!actualShapeKeys.SetEquals(expectedShapeKeys))
            { diagnostic = "Collection requires one Prefab for Base and every FBM."; return false; }
            outfit.SetCollection(kind, useProjectionForFullCollection, entries);
            return true;
        }

        internal bool TryValidateNormalEntries(out string diagnostic)
        {
            diagnostic = null;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (FigureNormalEntry entry in figureNormalEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.MaterialEntryName)
                    || !declared.Add(entry.MaterialEntryName)
                    || !materialEntries.Any(material => material != null && material.LogicalName == entry.MaterialEntryName))
                { diagnostic = "Figure Normal Entry is invalid."; return false; }
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (NormalEntry entry in normalEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.MaterialEntryName) || string.IsNullOrWhiteSpace(entry.ShapeKey)
                    || entry.Texture == null || string.IsNullOrWhiteSpace(entry.TextureResourceName)
                    || !textureResources.Any(resource => resource != null && resource.LogicalName == entry.TextureResourceName && resource.Texture == entry.Texture)
                    || !materialEntries.Any(material => material != null && material.LogicalName == entry.MaterialEntryName)
                    || !figureNormalEntries.Any(figureNormal => figureNormal != null && figureNormal.MaterialEntryName == entry.MaterialEntryName)
                    || (!string.Equals(entry.ShapeKey, BaseShapeKey, StringComparison.Ordinal) && !figureAxes.Any(axis => axis != null && axis.Kind == FigureAxisKind.Fbm && axis.Name == entry.ShapeKey))
                    || !keys.Add(entry.MaterialEntryName + "\u001f" + entry.ShapeKey))
                { diagnostic = "Normal matrix entry is invalid."; return false; }
            }
            return true;
        }
        internal IReadOnlyList<FigureAxisEntry> FigureAxes => figureAxes;
        // The admitted FBM axis set is the authoritative finalization fact.  Keep the
        // serialized marker for backward-compatible asset data, but never let a
        // duplicated Prefab/sub-asset serialization path redefine the UI contract.
        internal bool FbmAxesFinalized => figureAxes.Any(axis => axis != null && axis.Kind == FigureAxisKind.Fbm);
        internal int PcmSlots => pcmSlots;
        internal IReadOnlyList<string> KeptRawBlendShapeNames => keptRawBlendShapeNames;

        /// <summary>Returns raw BlendShape names common to every sealed FBM Figure; PBM never contributes candidates.</summary>
        internal bool TryGetCommonFbmRawBlendShapeNames(ShapeSyncDatabase database, out string[] names, out string diagnostic)
            => TryGetCommonFbmRawBlendShapeNamesCore(database, false, out names, out diagnostic);

        /// <summary>
        /// Returns the same FBM namespace while opening an editable Database.  Material
        /// relations are deliberately not part of Open admission; the validator and
        /// Generate preflight report them after the window has been opened.
        /// </summary>
        internal bool TryGetCommonFbmRawBlendShapeNamesForOpen(ShapeSyncDatabase database, out string[] names, out string diagnostic)
            => TryGetCommonFbmRawBlendShapeNamesCore(database, true, out names, out diagnostic);

        private bool TryGetCommonFbmRawBlendShapeNamesCore(ShapeSyncDatabase database, bool allowBrokenMaterialRelations, out string[] names, out string diagnostic)
        {
            names = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            if (!FbmAxesFinalized)
            {
                diagnostic = "Raw BlendShape candidates require a finalized FBM set.";
                return false;
            }
            bool resolved = allowBrokenMaterialRelations
                ? TryGetSingleBaseFigureForOpen(database, out BaseFigureEntry baseEntry, out diagnostic)
                : TryGetSingleBaseFigure(database, out baseEntry, out diagnostic);
            if (!resolved) return false;
            var candidateFigures = new List<GameObject> { baseEntry.Figure };
            foreach (FigureAxisEntry axis in figureAxes.Where(entry => entry != null && entry.Kind == FigureAxisKind.Fbm))
            {
                if (axis.Figures == null || axis.Figures.Count != 1 || axis.Figures[0] == null || axis.Figures[0].Figure == null)
                {
                    diagnostic = "FBM Figure binding is invalid while deriving raw BlendShape candidates.";
                    return false;
                }
                candidateFigures.Add(axis.Figures[0].Figure);
            }
            HashSet<string> common = null;
            foreach (GameObject figure in candidateFigures)
            {
                var figureNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (SkinnedMeshRenderer renderer in figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.sharedMesh == null) continue;
                    for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    {
                        string name = renderer.sharedMesh.GetBlendShapeName(i);
                        if (!string.IsNullOrWhiteSpace(name) && !BlendShapeReservedPrefixes.IsReserved(name)) figureNames.Add(name);
                    }
                }
                if (common == null) common = figureNames;
                else common.IntersectWith(figureNames);
            }
            names = (common ?? new HashSet<string>(StringComparer.Ordinal)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            diagnostic = null;
            return true;
        }

        /// <summary>Stores the Figure-owned PCM capacity without any FBM dependency.</summary>
        internal bool TrySetPcmSlots(int nextPcmSlots, out string diagnostic)
        {
            diagnostic = null;
            if (nextPcmSlots < 0) { diagnostic = "PCM Slots must be zero or greater."; return false; }
            pcmSlots = nextPcmSlots;
            return true;
        }

        /// <summary>Stores the Extra Morph subset of the current FBM raw-BlendShape intersection.</summary>
        internal bool TrySetKeptRawBlendShapeNames(ShapeSyncDatabase database, IReadOnlyList<string> keptNames, out string diagnostic)
        {
            diagnostic = null;
            if (keptNames == null) { diagnostic = "Raw BlendShape keep selection is required."; return false; }
            if (!TryGetCommonFbmRawBlendShapeNames(database, out string[] candidates, out diagnostic)) return false;
            var candidateSet = new HashSet<string>(candidates, StringComparer.Ordinal);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in keptNames)
            {
                if (string.IsNullOrWhiteSpace(name) || !candidateSet.Contains(name) || !selected.Add(name))
                { diagnostic = "Raw BlendShape keep selection contains an invalid or duplicate candidate."; return false; }
            }
            keptRawBlendShapeNames = selected.OrderBy(value => value, StringComparer.Ordinal).ToList();
            return true;
        }

        /// <summary>Verifies the independent PCM capacity and the Extra Morph keep selection.</summary>
        internal bool TryValidateFigureMorphAuthoring(ShapeSyncDatabase database, out string diagnostic)
            => TryValidateFigureMorphAuthoringCore(database, false, out diagnostic);

        /// <summary>Validates Figure morph authoring while allowing relation defects to be repaired after Open.</summary>
        internal bool TryValidateFigureMorphAuthoringForOpen(ShapeSyncDatabase database, out string diagnostic)
            => TryValidateFigureMorphAuthoringCore(database, true, out diagnostic);

        private bool TryValidateFigureMorphAuthoringCore(ShapeSyncDatabase database, bool allowBrokenMaterialRelations, out string diagnostic)
        {
            diagnostic = null;
            if (pcmSlots < 0) { diagnostic = "PCM Slots must be zero or greater."; return false; }
            if (keptRawBlendShapeNames == null) { diagnostic = "Raw BlendShape keep selection is missing."; return false; }
            // A Base-only Database is valid before Step 2 seals its FBM set.  No raw keep
            // selection may exist yet, but its default PCM capacity remains persistable.
            if (!FbmAxesFinalized)
            {
                if (keptRawBlendShapeNames.Count == 0) return true;
                diagnostic = "Raw BlendShape keep selection requires a finalized FBM set.";
                return false;
            }
            if (!(allowBrokenMaterialRelations
                ? TryGetCommonFbmRawBlendShapeNamesForOpen(database, out string[] candidates, out diagnostic)
                : TryGetCommonFbmRawBlendShapeNames(database, out candidates, out diagnostic))) return false;
            var candidateSet = new HashSet<string>(candidates, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in keptRawBlendShapeNames)
            {
                if (string.IsNullOrWhiteSpace(name) || !candidateSet.Contains(name) || !seen.Add(name))
                { diagnostic = "Persisted raw BlendShape keep selection is invalid."; return false; }
            }
            return true;
        }

        /// <summary>Validates one Step 2 Figure-axis draft against this Database's canonical namespace.</summary>
        internal bool TryValidateFigureAxis(ShapeSyncDatabase database, string name, FigureAxisKind kind, out string diagnostic)
        {
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            if (!TryGetSingleBaseFigure(database, out BaseFigureEntry baseEntry, out diagnostic)) return false;
            if (baseEntry == null)
            {
                diagnostic = "Figure axis requires one registered Base Figure candidate.";
                return false;
            }
            if (!IsValidUserName(name))
            {
                diagnostic = "Figure axis name must not be empty or contain whitespace.";
                return false;
            }
            if (string.Equals(name, BaseShapeKey, StringComparison.Ordinal))
            {
                diagnostic = "Figure axis name is reserved for the Base Shape key.";
                return false;
            }
            if (BlendShapeReservedPrefixes.IsReserved(name))
            {
                diagnostic = "Figure axis name uses a reserved prefix: " + name;
                return false;
            }
            if (kind != FigureAxisKind.Fbm && kind != FigureAxisKind.Pbm)
            {
                diagnostic = "Figure axis kind is invalid.";
                return false;
            }
            if (figureAxes.Exists(entry => entry == null || string.IsNullOrWhiteSpace(entry.Name) || entry.Name == name))
            {
                diagnostic = "Figure axis name already exists or registry entry is invalid: " + name;
                return false;
            }
            return true;
        }

        /// <summary>Rejects persisted state in which the sealed FBM boundary and its entries disagree.</summary>
        internal bool TryValidateFigureAxisState(out string diagnostic) => TryValidateFigureAxisState(null, out diagnostic);

        /// <summary>Validates every persisted axis binding against its Database hierarchy when supplied.</summary>
        internal bool TryValidateFigureAxisState(ShapeSyncDatabase database, out string diagnostic)
        {
            diagnostic = null;
            var fbmNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (FigureAxisEntry entry in figureAxes)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                {
                    diagnostic = "Figure axis registry entry is invalid.";
                    return false;
                }
                if (entry.Kind != FigureAxisKind.Fbm && entry.Kind != FigureAxisKind.Pbm)
                {
                    diagnostic = "Figure axis registry kind is invalid.";
                    return false;
                }
                if (!fbmNames.Add(entry.Name))
                {
                    diagnostic = "Figure axis registry names are duplicated.";
                    return false;
                }
            }
            if (database == null) return true;

            var allBoundFigures = new HashSet<GameObject>();
            foreach (FigureAxisEntry entry in figureAxes)
            {
                IReadOnlyList<AxisFigureEntry> bindings = entry.Figures;
                if (bindings == null || bindings.Count == 0)
                {
                    diagnostic = "Figure axis registry Figure bindings are missing.";
                    return false;
                }
                var bindingFbmNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (AxisFigureEntry binding in bindings)
                {
                    string expectedFigureName = entry.Kind == FigureAxisKind.Fbm ? entry.Name : GetPbmFigureName(binding?.FbmName, entry.Name);
                    Transform resolved = binding == null ? null : database.transform.Find("Intermediate/" + expectedFigureName);
                    if (resolved != null && IsDirectIntermediateChild(database, resolved.gameObject)) binding.RebindFigure(resolved.gameObject);
                    bool isBaseBinding = binding != null && string.Equals(binding.FbmName, BaseShapeKey, StringComparison.Ordinal);
                    if (binding == null || string.IsNullOrWhiteSpace(binding.FbmName) || (!isBaseBinding && !fbmNames.Contains(binding.FbmName))
                        || binding.Figure == null || !IsDirectIntermediateChild(database, binding.Figure)
                        || !bindingFbmNames.Add(binding.FbmName) || !allBoundFigures.Add(binding.Figure))
                    {
                        diagnostic = "Figure axis registry Figure binding is invalid.";
                        return false;
                    }
                    if (!string.Equals(binding.Figure.name, expectedFigureName, StringComparison.Ordinal))
                    {
                        diagnostic = "Figure axis registry Figure binding name is invalid.";
                        return false;
                    }
                }
                if (entry.Kind == FigureAxisKind.Fbm && (bindings.Count != 1 || !bindingFbmNames.SetEquals(new[] { entry.Name })))
                {
                    diagnostic = "Figure axis registry FBM binding is incomplete.";
                    return false;
                }
                if (entry.Kind == FigureAxisKind.Pbm && !bindingFbmNames.SetEquals(fbmNames.Where(name => figureAxes.Any(axis => axis.Kind == FigureAxisKind.Fbm && axis.Name == name)).Append(BaseShapeKey)))
                {
                    diagnostic = "Figure axis registry PBM bindings are incomplete.";
                    return false;
                }
            }
            return true;
        }

        private string GetPbmFigureName(string sourceFbmName, string pbmName)
        {
            string ownerName = sourceFbmName;
            if (string.Equals(sourceFbmName, BaseShapeKey, StringComparison.Ordinal))
            {
                if (!TryGetSingleBaseFigure(out BaseFigureEntry baseFigure, out _)) return null;
                ownerName = baseFigure?.Name;
            }
            return string.IsNullOrWhiteSpace(ownerName) || string.IsNullOrWhiteSpace(pbmName) ? null : ownerName + "_" + pbmName;
        }

        /// <summary>
        /// Invalidates data whose complete-FBM relation becomes stale when an FBM is appended.
        /// PCM Slots are a separate Figure attribute and deliberately remain unchanged.
        /// </summary>
        internal bool TryClearPbmAndExtraMorphsForFbmRedefinition(ShapeSyncDatabase database, out GameObject[] removedPbmFigures, out string diagnostic)
        {
            removedPbmFigures = Array.Empty<GameObject>();
            diagnostic = null;
            if (!TryValidateFigureAxisState(database, out diagnostic)) return false;
            removedPbmFigures = figureAxes.Where(entry => entry != null && entry.Kind == FigureAxisKind.Pbm)
                .SelectMany(entry => entry.Figures ?? Array.Empty<AxisFigureEntry>())
                .Where(binding => binding != null && binding.Figure != null)
                .Select(binding => binding.Figure)
                .Distinct()
                .ToArray();
            figureAxes.RemoveAll(entry => entry != null && entry.Kind == FigureAxisKind.Pbm);
            keptRawBlendShapeNames.Clear();
            return true;
        }

        /// <summary>Admits an entire axis set without mutating this registry.</summary>
        internal bool TryAdmitFigureAxes(ShapeSyncDatabase database, IReadOnlyList<FigureAxisDraft> drafts, out FigureAxisAdmission[] admissions, out string diagnostic)
        {
            admissions = null;
            diagnostic = null;
            if (drafts == null || drafts.Count == 0)
            {
                diagnostic = "Figure axis admission requires at least one axis draft.";
                return false;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new FigureAxisAdmission[drafts.Count];
            bool includesFbm = false;
            for (int i = 0; i < drafts.Count; i++)
            {
                FigureAxisDraft draft = drafts[i];
                if (!TryValidateFigureAxis(database, draft.Name, draft.Kind, out diagnostic)) return false;
                if (!names.Add(draft.Name))
                {
                    diagnostic = "Figure axis name is duplicated in this transaction: " + draft.Name;
                    return false;
                }
                if (draft.Kind == FigureAxisKind.Fbm) includesFbm = true;
                result[i] = new FigureAxisAdmission(draft.Name, draft.Kind, AxisAdmissionToken, draft.ImportAllMaterialsAndTextures);
            }
            if (!FbmAxesFinalized && !includesFbm)
            {
                diagnostic = "The first Figure-axis admission must include the complete FBM set.";
                return false;
            }
            admissions = result;
            return true;
        }

        /// <summary>Commits a fully admitted axis set atomically after Step 2 has staged every owned Figure asset.</summary>
        internal bool TryCommitFigureAxes(ShapeSyncDatabase database, IReadOnlyList<FigureAxisAdmission> admissions, out string diagnostic)
        {
            diagnostic = "Figure axis commit requires Database-owned Figure bindings.";
            return false;
        }

        /// <summary>Atomically commits admitted axes with their already-staged Database-owned Figure children.</summary>
        internal bool TryCommitFigureAxes(ShapeSyncDatabase database, IReadOnlyList<FigureAxisAdmission> admissions, IReadOnlyList<GameObject> figures, out string diagnostic)
        {
            if (figures == null)
            {
                diagnostic = "Figure axis commit requires Database-owned Figure bindings.";
                return false;
            }
            IReadOnlyList<FigureAxisFigureBinding>[] bindings = null;
            if (figures != null)
            {
                bindings = new IReadOnlyList<FigureAxisFigureBinding>[figures.Count];
                for (int i = 0; i < figures.Count; i++)
                {
                    string sourceFbmName = admissions != null && i < admissions.Count ? admissions[i].Name : null;
                    bindings[i] = new[] { new FigureAxisFigureBinding(sourceFbmName, figures[i]) };
                }
            }
            return TryCommitFigureAxes(database, admissions, bindings, out diagnostic);
        }

        /// <summary>
        /// Atomically commits admitted axes and all Database-owned Figure bindings.
        /// FBM owns exactly its one Figure; PBM owns one Base Figure and one Figure for every registered or co-admitted FBM.
        /// </summary>
        internal bool TryCommitFigureAxes(ShapeSyncDatabase database, IReadOnlyList<FigureAxisAdmission> admissions, IReadOnlyList<IReadOnlyList<FigureAxisFigureBinding>> bindingsByAxis, out string diagnostic)
        {
            diagnostic = null;
            if (!TryValidateFigureAxisAdmissions(database, admissions, out diagnostic)) return false;
            if (bindingsByAxis != null && bindingsByAxis.Count != admissions.Count)
            {
                diagnostic = "Figure axis commit requires Figure bindings for every admitted axis.";
                return false;
            }
            if (bindingsByAxis == null)
            {
                diagnostic = "Figure axis commit requires Database-owned Figure bindings.";
                return false;
            }
            bool includesFbm = admissions.Any(admission => admission.Kind == FigureAxisKind.Fbm);
            var committedFbmNames = new HashSet<string>(figureAxes.Where(entry => entry != null && entry.Kind == FigureAxisKind.Fbm).Select(entry => entry.Name), StringComparer.Ordinal);
            for (int i = 0; i < admissions.Count; i++) if (admissions[i].Kind == FigureAxisKind.Fbm) committedFbmNames.Add(admissions[i].Name);
            for (int i = 0; i < admissions.Count; i++)
            {
                FigureAxisAdmission admission = admissions[i];
                if (bindingsByAxis == null) continue;
                IReadOnlyList<FigureAxisFigureBinding> bindings = bindingsByAxis[i];
                if (bindings == null || bindings.Count == 0)
                {
                    diagnostic = "Figure axis commit requires staged Figure bindings.";
                    return false;
                }
                var sourceFbmNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (FigureAxisFigureBinding binding in bindings)
                {
                    bool isBaseBinding = string.Equals(binding.SourceFbmName, BaseShapeKey, StringComparison.Ordinal);
                    if (string.IsNullOrWhiteSpace(binding.SourceFbmName) || (!isBaseBinding && !committedFbmNames.Contains(binding.SourceFbmName)) || !sourceFbmNames.Add(binding.SourceFbmName))
                    {
                        diagnostic = "Figure axis Figure binding must name one unique Base or committed FBM.";
                        return false;
                    }
                    if (!IsDirectIntermediateChild(database, binding.Figure))
                    {
                        diagnostic = "Figure axis Figure must be a direct child of Database Intermediate.";
                        return false;
                    }
                }
                if (admission.Kind == FigureAxisKind.Fbm && (bindings.Count != 1 || !string.Equals(bindings[0].SourceFbmName, admission.Name, StringComparison.Ordinal)))
                {
                    diagnostic = "FBM requires exactly one Figure binding keyed by its own FBM name.";
                    return false;
                }
                if (admission.Kind == FigureAxisKind.Pbm && !sourceFbmNames.SetEquals(committedFbmNames.Append(BaseShapeKey)))
                {
                    diagnostic = "PBM requires one Base Figure binding and one Figure binding for every committed FBM.";
                    return false;
                }
            }
            for (int i = 0; i < admissions.Count; i++)
            {
                IEnumerable<AxisFigureEntry> entries = bindingsByAxis == null
                    ? null
                    : bindingsByAxis[i].Select(binding => new AxisFigureEntry(binding.SourceFbmName, binding.Figure));
                figureAxes.Add(new FigureAxisEntry(admissions[i].Name, admissions[i].Kind, entries, admissions[i].ImportAllMaterialsAndTextures));
            }
            if (includesFbm) fbmAxesFinalized = true;
            return true;
        }

        /// <summary>Validates registry provenance and batch semantics before any source Figure is cloned or merged.</summary>
        internal bool TryValidateFigureAxisAdmissions(ShapeSyncDatabase database, IReadOnlyList<FigureAxisAdmission> admissions, out string diagnostic)
        {
            diagnostic = null;
            if (admissions == null || admissions.Count == 0)
            {
                diagnostic = "Figure axis commit requires admitted axes.";
                return false;
            }
            bool includesFbm = false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < admissions.Count; i++)
            {
                FigureAxisAdmission admission = admissions[i];
                if (!admission.IsIssuedBy(AxisAdmissionToken))
                {
                    diagnostic = "Figure axis admission was not issued by this registry.";
                    return false;
                }
                if (!TryValidateFigureAxis(database, admission.Name, admission.Kind, out diagnostic)) return false;
                if (!names.Add(admission.Name))
                {
                    diagnostic = "Figure axis admission is duplicated in this transaction: " + admission.Name;
                    return false;
                }
                if (admission.Kind == FigureAxisKind.Fbm) includesFbm = true;
            }
            if (!FbmAxesFinalized && !includesFbm)
            {
                diagnostic = "The first Figure-axis transaction must register the complete FBM set.";
                return false;
            }
            return true;
        }

        internal bool TryRegisterBaseFigure(ShapeSyncDatabase database, string name, GameObject figure, out string diagnostic)
        {
            diagnostic = null;
            if (!IsDirectIntermediateChild(database, figure) || !IsValidUserName(name) || figure.name != name)
            {
                diagnostic = "Database Base Figure must be a named direct child of Intermediate.";
                return false;
            }
            if (!ShapeSyncDatabaseAdmission.TryValidateAdditionalBaseFigure(baseFigures, name, figure, out ShapeSyncDatabaseDiagnostic cardinality))
            { diagnostic = cardinality.ToString(); return false; }
            if (baseFigures.Exists(entry => entry.Name == name || entry.Figure == figure)) { diagnostic = "Database Base Figure name already exists: " + name; return false; }
            baseFigures.Add(new BaseFigureEntry(name, figure));
            return true;
        }

        // Whole-Database validation reuses the same vocabulary admission as Shape Detail Save.
        internal bool TryValidateShapeTagsForValidation(IReadOnlyList<string> values, out string diagnostic)
            => TryValidateShapeTags(values, out diagnostic);

        /// <summary>Resolves the sole Base Figure without touching Unity hierarchy or serialized references.</summary>
        internal bool TryGetSingleBaseFigure(out BaseFigureEntry entry, out string diagnostic)
        {
            entry = null; diagnostic = null;
            if (!ShapeSyncDatabaseAdmission.TryValidateBaseFigureCardinality(baseFigures, out ShapeSyncDatabaseDiagnostic cardinality))
            { diagnostic = cardinality.ToString(); return false; }
            if (baseFigures.Count == 0) return true;
            entry = baseFigures[0];
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            { entry = null; diagnostic = "ShapeSync Database Base Figure registry entry is invalid."; return false; }
            return true;
        }

        internal bool TryGetSingleBaseFigure(ShapeSyncDatabase database, out BaseFigureEntry entry, out string diagnostic)
        {
            if (!TryGetSingleBaseFigureForOpen(database, out entry, out diagnostic)) return false;
            if (entry == null) return true;
            foreach (MaterialEntry materialEntry in materialEntries)
            {
                if (materialEntry == null || string.IsNullOrWhiteSpace(materialEntry.LogicalName) || materialEntry.BaseRelativeRendererPath == null)
                { entry = null; diagnostic = "ShapeSync Database Material Entry registry entry is invalid."; return false; }
                Transform materialTransform = TryResolveRelativePath(entry.Figure.transform, materialEntry.BaseRelativeRendererPath);
                SkinnedMeshRenderer materialRenderer = materialTransform == null ? null : materialTransform.GetComponent<SkinnedMeshRenderer>();
                if (materialRenderer == null || materialEntry.MaterialSlot < 0 || materialEntry.MaterialSlot >= materialRenderer.sharedMaterials.Length || materialRenderer.sharedMaterials[materialEntry.MaterialSlot] != materialEntry.Material || materialEntry.Adapter == null)
                { entry = null; diagnostic = "ShapeSync Database Material Entry registry entry is invalid."; return false; }
                materialEntry.RebindRenderer(materialRenderer);
            }
            return true;
        }

        /// <summary>
        /// Resolves the sole Base Figure for editing.  Structural failures still reject
        /// Open, while malformed Material relations are left untouched for validation
        /// and repair in the Database window.
        /// </summary>
        internal bool TryGetSingleBaseFigureForOpen(ShapeSyncDatabase database, out BaseFigureEntry entry, out string diagnostic)
        {
            if (!TryGetSingleBaseFigure(out entry, out diagnostic)) return false;
            if (entry == null) return true;
            // Prefab contents are reloaded for each transaction. Resolve the direct Intermediate
            // child by the registry's stable name before validating the instance reference.
            // This also repairs a valid entry whose serialized GameObject points at an older
            // Prefab-contents instance; a missing or nested name remains invalid.
            Transform resolved = database == null ? null : database.transform.Find("Intermediate/" + entry.Name);
            if (resolved != null && IsDirectIntermediateChild(database, resolved.gameObject)) entry.RebindFigure(resolved.gameObject);
            if (entry.Figure == null || entry.Figure.name != entry.Name || !IsDirectIntermediateChild(database, entry.Figure))
            { entry = null; diagnostic = "ShapeSync Database Base Figure registry entry is invalid."; return false; }
            foreach (MaterialEntry materialEntry in materialEntries)
            {
                if (materialEntry == null || string.IsNullOrWhiteSpace(materialEntry.LogicalName) || materialEntry.BaseRelativeRendererPath == null
                    || materialEntry.Material == null || materialEntry.Adapter == null) continue;
                Transform materialTransform = TryResolveRelativePath(entry.Figure.transform, materialEntry.BaseRelativeRendererPath);
                SkinnedMeshRenderer materialRenderer = materialTransform == null ? null : materialTransform.GetComponent<SkinnedMeshRenderer>();
                if (materialRenderer == null || materialEntry.MaterialSlot < 0 || materialEntry.MaterialSlot >= materialRenderer.sharedMaterials.Length || materialRenderer.sharedMaterials[materialEntry.MaterialSlot] != materialEntry.Material) continue;
                materialEntry.RebindRenderer(materialRenderer);
            }
            return true;
        }

        /// <summary>Validates a transient Material Entry admission against the sole 20.3 Base candidate.</summary>
        internal bool TryValidateMaterialEntry(ShapeSyncDatabase database, string logicalName, SkinnedMeshRenderer renderer, int materialSlot, Material material, out string diagnostic)
        {
            diagnostic = null;
            if (!TryGetSingleBaseFigure(database, out BaseFigureEntry baseEntry, out diagnostic)) return false;
            if (baseEntry == null)
            {
                diagnostic = "Material Entry requires one registered Base Figure candidate.";
                return false;
            }
            if (!IsValidUserName(logicalName))
            {
                diagnostic = "Material Entry name must not be empty or contain whitespace.";
                return false;
            }
            if (renderer == null || !renderer.transform.IsChildOf(baseEntry.Figure.transform))
            {
                diagnostic = "Material Entry renderer must belong to the registered Base Figure.";
                return false;
            }
            Material[] materials = renderer.sharedMaterials;
            if (materialSlot < 0 || materialSlot >= materials.Length)
            {
                diagnostic = "Material Entry material slot is outside the Base renderer range.";
                return false;
            }
            if (material == null || materials[materialSlot] != material)
            {
                diagnostic = "Material Entry material must match the Base renderer material slot.";
                return false;
            }
            return true;
        }

        /// <summary>Registers one fully Database-owned Material Entry after the Step 2 transaction has staged its assets.</summary>
        internal bool TryRegisterMaterialEntry(ShapeSyncDatabase database, string logicalName, SkinnedMeshRenderer renderer, int materialSlot, string materialName, Material material, MaterialShaderAdapter adapter, out string diagnostic)
        {
            if (!TryGetSingleBaseFigure(database, out BaseFigureEntry baseEntry, out diagnostic)) return false;
            if (baseEntry == null || !IsValidUserName(logicalName) || string.IsNullOrWhiteSpace(materialName) || material == null || adapter == null)
            { diagnostic = "Material Entry requires a Base, name, display name, Material, and Adapter."; return false; }
            if (materialEntries.Exists(entry => entry != null && entry.LogicalName == logicalName))
            { diagnostic = "Material Entry name already exists: " + logicalName; return false; }
            if (renderer == null || !renderer.transform.IsChildOf(baseEntry.Figure.transform))
            { diagnostic = "Material Entry renderer must belong to the registered Base Figure."; return false; }
            Material[] materials = renderer.sharedMaterials;
            if (materialSlot < 0 || materialSlot >= materials.Length || materials[materialSlot] != material)
            { diagnostic = "Material Entry must reference its Database-owned renderer material slot."; return false; }
            if (!TryGetRelativePath(baseEntry.Figure.transform, renderer.transform, out string rendererPath))
            { diagnostic = "Material Entry renderer could not be addressed from the registered Base Figure."; return false; }
            materialEntries.Add(new MaterialEntry(logicalName, renderer, rendererPath, materialSlot, materialName, material, adapter));
            return true;
        }

        internal bool ContainsMaterialEntryName(string logicalName)
        {
            return !string.IsNullOrWhiteSpace(logicalName) && materialEntries.Exists(entry => entry != null && entry.LogicalName == logicalName);
        }

        /// <summary>Renames one Material Entry identity while retaining its renderer, slot, Material, Adapter, and Texture references.</summary>
        internal bool TryRenameMaterialEntry(string currentName, string nextName, out string diagnostic)
        {
            diagnostic = null;
            MaterialEntry entry = materialEntries.Find(item => item != null && item.LogicalName == currentName);
            if (entry == null) { diagnostic = "Material Entry does not exist: " + currentName; return false; }
            if (!IsValidUserName(nextName)) { diagnostic = "Material Entry name must not be empty or contain whitespace."; return false; }
            if (string.Equals(currentName, nextName, StringComparison.Ordinal)) return true;
            if (materialEntries.Exists(item => item != null && item != entry && item.LogicalName == nextName))
            { diagnostic = "Material Entry name already exists: " + nextName; return false; }
            entry.Rename(nextName);
            foreach (NormalEntry normal in normalEntries)
            {
                if (normal != null && string.Equals(normal.MaterialEntryName, currentName, StringComparison.Ordinal))
                    normal.RenameMaterialEntry(nextName);
            }
            foreach (FigureNormalEntry normal in figureNormalEntries)
            {
                if (normal != null && string.Equals(normal.MaterialEntryName, currentName, StringComparison.Ordinal))
                    normal.RenameMaterialEntry(nextName);
            }
            // Shape parts use the Figure Material Entry namespace when RegistryId is
            // empty.  Keep those authoring-only name references in lockstep with the
            // canonical Figure entry; Outfit-local namespaces must remain untouched.
            foreach (ShapeEntry shape in shapes)
            {
                if (shape == null || shape.Parts == null) continue;
                foreach (ShapeEntryDefinition part in shape.Parts)
                {
                    if (part != null && string.IsNullOrEmpty(part.RegistryId)
                        && string.Equals(part.ProxyEntry, currentName, StringComparison.Ordinal))
                        part.SetMaterialTarget(part.RegistryId, nextName);
                }
            }
            return true;
        }

        internal bool TryRegisterTextureResource(string logicalName, Texture texture, out string diagnostic)
        {
            return TryRegisterTextureResource(logicalName, texture, TextureResourceOwner.FigureBase, out diagnostic);
        }

        internal bool TryRegisterTextureResource(string logicalName, Texture texture, TextureResourceOwner owner, out string diagnostic)
        {
            return TryRegisterTextureResource(logicalName, texture, owner, TextureResourceUsage.General, out diagnostic);
        }

        internal bool TryRegisterTextureResource(string logicalName, Texture texture, TextureResourceOwner owner, TextureResourceUsage usage, out string diagnostic)
        {
            return TryRegisterTextureResource(logicalName, texture, owner, usage, null, 0, out diagnostic);
        }

        /// <summary>Registers a Database-owned Texture with optional source-asset provenance for authoring deduplication.
        /// The provenance is a GUID/local-id value, never an external asset reference or runtime key.</summary>
        internal bool TryRegisterTextureResource(string logicalName, Texture texture, TextureResourceOwner owner, TextureResourceUsage usage,
            string sourceAssetGuid, long sourceAssetLocalFileId, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(logicalName) || texture == null)
            { diagnostic = "Texture resource requires a logical name and Database-owned Texture."; return false; }
            if (!TryValidateTextureResourceOwner(owner, out diagnostic)) return false;
            if (textureResources.Exists(entry => entry != null && entry.LogicalName == logicalName))
            { diagnostic = "Texture resource logical name or Texture already exists: " + logicalName; return false; }
            if (textureResources.Exists(entry => entry != null && entry.Texture == texture))
            {
                diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.OwnerTextureAlreadyRegistered, logicalName).ToString();
                return false;
            }
            textureResources.Add(new TextureResourceEntry(logicalName, texture, owner, usage, sourceAssetGuid, sourceAssetLocalFileId));
            return true;
        }

        /// <summary>Finds an authoring resource only when its recorded owner and imported source identity both match.</summary>
        internal TextureResourceEntry FindTextureResourceByImportSource(TextureResourceOwner owner, string sourceAssetGuid, long sourceAssetLocalFileId)
        {
            if (string.IsNullOrWhiteSpace(sourceAssetGuid)) return null;
            return textureResources.FirstOrDefault(entry => entry != null && entry.Owner.Equals(owner)
                && entry.MatchesImportSource(sourceAssetGuid, sourceAssetLocalFileId));
        }

        /// <summary>Removes the Include-material resources of one Outfit before those derived Materials are rebuilt.
        /// This does not use owner alone as a deletion key: later Normal resources share the owner classification but retain General usage.</summary>
        internal Texture[] RemoveIncludedTextureResourcesOwnedByOutfit(string outfitIdentity, string shapeKey = null)
        {
            Texture[] removed = textureResources.Where(entry => entry != null
                && entry.Owner.Scope == TextureResourceOwnerScope.Outfit
                && string.Equals(entry.Owner.OutfitIdentity, outfitIdentity, StringComparison.Ordinal)
                && (shapeKey == null || string.Equals(entry.Owner.SourceShapeKey, shapeKey, StringComparison.Ordinal))
                && entry.Usage == TextureResourceUsage.OutfitIncludedMaterial)
                .Select(entry => entry.Texture).Where(texture => texture != null).Distinct().ToArray();
            textureResources.RemoveAll(entry => entry != null
                && entry.Owner.Scope == TextureResourceOwnerScope.Outfit
                && string.Equals(entry.Owner.OutfitIdentity, outfitIdentity, StringComparison.Ordinal)
                && (shapeKey == null || string.Equals(entry.Owner.SourceShapeKey, shapeKey, StringComparison.Ordinal))
                && entry.Usage == TextureResourceUsage.OutfitIncludedMaterial);
            return removed;
        }

        /// <summary>Removes every Texture resource classified to an Outfit when that Outfit is deleted.
        /// Owner is a classification boundary, not a runtime reference key; deleting the Outfit therefore removes
        /// both Include-material and explicit Normal resources in the same authoring transaction.</summary>
        internal Texture[] RemoveTextureResourcesOwnedByOutfit(string outfitIdentity)
        {
            Texture[] removed = textureResources.Where(entry => entry != null
                && entry.Owner.Scope == TextureResourceOwnerScope.Outfit
                && string.Equals(entry.Owner.OutfitIdentity, outfitIdentity, StringComparison.Ordinal))
                .Select(entry => entry.Texture).Where(texture => texture != null).Distinct().ToArray();
            textureResources.RemoveAll(entry => entry != null
                && entry.Owner.Scope == TextureResourceOwnerScope.Outfit
                && string.Equals(entry.Owner.OutfitIdentity, outfitIdentity, StringComparison.Ordinal));
            return removed;
        }

        /// <summary>Reclaims General-purpose Outfit Texture resources after Normal relations have been removed or replaced.
        /// Include-material resources are deliberately retained here because their references live on derived Materials,
        /// not on the Normal relation table.</summary>
        internal Texture[] RemoveUnreferencedOutfitNormalTextureResources(IEnumerable<string> candidateResourceNames)
        {
            if (candidateResourceNames == null) return Array.Empty<Texture>();
            var candidates = new HashSet<string>(candidateResourceNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
            if (candidates.Count == 0) return Array.Empty<Texture>();
            var referenced = new HashSet<string>(outfits.SelectMany(outfit => outfit?.NormalEntries ?? Array.Empty<OutfitNormalEntry>())
                .Where(entry => entry != null).Select(entry => entry.TextureResourceName), StringComparer.Ordinal);
            Texture[] removed = textureResources.Where(entry => entry != null && candidates.Contains(entry.LogicalName)
                && entry.Owner.Scope == TextureResourceOwnerScope.Outfit && entry.Usage == TextureResourceUsage.General
                && !referenced.Contains(entry.LogicalName)).Select(entry => entry.Texture).Where(texture => texture != null).Distinct().ToArray();
            textureResources.RemoveAll(entry => entry != null && candidates.Contains(entry.LogicalName)
                && entry.Owner.Scope == TextureResourceOwnerScope.Outfit && entry.Usage == TextureResourceUsage.General
                && !referenced.Contains(entry.LogicalName));
            return removed;
        }

        internal bool TryRenameBaseFigure(ShapeSyncDatabase database, string currentName, string replacementName, out string diagnostic)
        {
            diagnostic = null;
            if (!TryGetSingleBaseFigure(database, out BaseFigureEntry entry, out diagnostic) || entry == null) { diagnostic ??= "Base Figure was not found."; return false; }
            if (entry.Name != currentName || !IsValidUserName(replacementName)
                || figureAxes.Any(axis => axis != null && axis.Name == replacementName)
                || database.transform.Find("Intermediate/" + replacementName) != null)
            { diagnostic = "Replacement Figure name is invalid or already exists: " + replacementName; return false; }
            // A PBM Base row is named from the Figure master name. Validate all target
            // names before mutating either the Base Figure or any PBM binding.
            foreach (FigureAxisEntry axis in figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Pbm))
            {
                AxisFigureEntry binding = axis.Figures.SingleOrDefault(candidate => candidate != null && candidate.FbmName == BaseShapeKey);
                if (binding == null) { diagnostic = "PBM Base Figure binding is missing: " + axis.Name; return false; }
                string currentPbmFigureName = currentName + "_" + axis.Name;
                Transform current = database.transform.Find("Intermediate/" + currentPbmFigureName);
                if (current == null) { diagnostic = "PBM Base Figure binding is invalid: " + axis.Name; return false; }
                string nextPbmFigureName = replacementName + "_" + axis.Name;
                Transform existing = database.transform.Find("Intermediate/" + nextPbmFigureName);
                if (existing != null && existing.gameObject != binding.Figure)
                { diagnostic = "Replacement Figure name conflicts with a PBM Figure: " + nextPbmFigureName; return false; }
            }
            entry.Figure.name = replacementName;
            entry.Rename(replacementName);
            foreach (FigureAxisEntry axis in figureAxes.Where(axis => axis != null && axis.Kind == FigureAxisKind.Pbm))
            {
                AxisFigureEntry binding = axis.Figures.Single(candidate => candidate != null && candidate.FbmName == BaseShapeKey);
                Transform current = database.transform.Find("Intermediate/" + currentName + "_" + axis.Name);
                current.name = replacementName + "_" + axis.Name;
                binding.RebindFigure(current.gameObject);
            }
            return true;
        }

        internal static bool IsValidUserName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace);
        }

        /// <summary>Renames a logical Texture resource and preserves every Material Entry reference to it.</summary>
        internal bool TryRenameTextureResource(string currentName, string nextName, out string diagnostic)
        {
            diagnostic = null;
            TextureResourceEntry resource = textureResources.Find(entry => entry != null && entry.LogicalName == currentName);
            if (resource == null) { diagnostic = "Texture resource does not exist: " + currentName; return false; }
            if (string.IsNullOrWhiteSpace(nextName)) { diagnostic = "Texture resource name must not be empty."; return false; }
            if (string.Equals(currentName, nextName, StringComparison.Ordinal)) return true;
            if (textureResources.Exists(entry => entry != null && entry != resource && entry.LogicalName == nextName))
            { diagnostic = "Texture resource name already exists: " + nextName; return false; }
            resource.Rename(nextName);
            resource.Texture.name = nextName;
            foreach (MaterialEntry entry in materialEntries)
            {
                if (entry == null || entry.TextureResourceNames == null) continue;
                entry.SetTextureResourceNames(entry.TextureResourceNames.Select(name => name == currentName ? nextName : name));
            }
            foreach (NormalEntry entry in normalEntries)
                if (entry != null && entry.TextureResourceName == currentName) entry.RenameTextureResourceName(nextName);
            foreach (OutfitEntry outfit in outfits)
            {
                foreach (OutfitNormalEntry entry in outfit?.NormalEntries ?? Array.Empty<OutfitNormalEntry>())
                    if (entry.TextureResourceName == currentName) entry.RenameTextureResourceName(nextName);
                foreach (MaterialOutfitTextureEntry entry in outfit?.MaterialOutfitTextureEntries ?? Array.Empty<MaterialOutfitTextureEntry>())
                    if (entry.TextureResourceName == currentName) entry.RenameTextureResourceName(nextName);
                foreach (FigureMaskEntry entry in outfit?.FigureMaskEntries ?? Array.Empty<FigureMaskEntry>())
                    if (entry.TextureResourceName == currentName) entry.RenameTextureResourceName(nextName);
            }
            // Shape Texture entries also store the abstract resource's logical name.
            // Unlike Material Entry targets, Texture resources have one shared
            // namespace, so both Figure- and Outfit-targeted Shape parts follow.
            foreach (ShapeEntry shape in shapes)
            {
                if (shape == null || shape.Parts == null) continue;
                foreach (ShapeEntryDefinition part in shape.Parts)
                {
                    if (part != null && string.Equals(part.TextureResourceName, currentName, StringComparison.Ordinal))
                        part.SetTexture(nextName, part.UseColorize, part.Color);
                }
            }
            return true;
        }

        /// <summary>Removes an unreferenced Texture resource and returns its owned sub-asset.</summary>
        internal bool TryRemoveTextureResource(string logicalName, out Texture removedTexture, out string diagnostic)
        {
            bool result = TryRemoveTextureResource(logicalName, out removedTexture, out TextureResourceDiagnostic structuredDiagnostic);
            diagnostic = structuredDiagnostic.ToString();
            return result;
        }

        /// <summary>Removes an unreferenced Texture resource and returns a structured rejection diagnostic.</summary>
        internal bool TryRemoveTextureResource(string logicalName, out Texture removedTexture, out TextureResourceDiagnostic diagnostic)
        {
            removedTexture = null;
            diagnostic = default;
            TextureResourceEntry resource = textureResources.Find(entry => entry != null && entry.LogicalName == logicalName);
            if (resource == null) { diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.ResourceMissing, logicalName); return false; }
            MaterialEntry material = materialEntries.FirstOrDefault(entry => entry != null && entry.TextureResourceNames != null && entry.TextureResourceNames.Contains(logicalName));
            if (material != null) { diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.ReferencedByMaterialEntry, logicalName, material.LogicalName); return false; }
            NormalEntry normal = normalEntries.FirstOrDefault(entry => entry != null && entry.TextureResourceName == logicalName);
            if (normal != null) { diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.ReferencedByNormalEntry, logicalName, normal.MaterialEntryName, normal.ShapeKey); return false; }
            OutfitNormalEntry outfitNormal = outfits.SelectMany(outfit => outfit?.NormalEntries ?? Array.Empty<OutfitNormalEntry>())
                .FirstOrDefault(entry => entry != null && entry.TextureResourceName == logicalName);
            if (outfitNormal != null) { diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.ReferencedByNormalEntry, logicalName, outfitNormal.MaterialEntryName, outfitNormal.ShapeKey); return false; }
            OutfitEntry materialOutfit = outfits.FirstOrDefault(outfit => outfit != null && outfit.MaterialOutfitTextureEntries.Any(entry => entry != null && entry.TextureResourceName == logicalName));
            if (materialOutfit != null) { diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.ReferencedByOutfitTextureEntry, logicalName, materialOutfit.Identity); return false; }
            OutfitEntry figureMaskOutfit = outfits.FirstOrDefault(outfit => outfit != null && outfit.FigureMaskEntries.Any(entry => entry != null && entry.TextureResourceName == logicalName));
            if (figureMaskOutfit != null) { diagnostic = new TextureResourceDiagnostic(TextureResourceDiagnosticCode.ReferencedByFigureMask, logicalName, figureMaskOutfit.Identity); return false; }
            removedTexture = resource.Texture;
            textureResources.Remove(resource);
            return true;
        }

        internal bool TrySetMaterialEntryTextureResources(string materialEntryName, IReadOnlyList<string> resourceNames, out string diagnostic)
        {
            diagnostic = null;
            MaterialEntry materialEntry = materialEntries.Find(entry => entry != null && entry.LogicalName == materialEntryName);
            if (materialEntry == null || resourceNames == null) { diagnostic = "Texture resource assignment requires an existing Material Entry."; return false; }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in resourceNames)
            {
                if (string.IsNullOrWhiteSpace(name) || !unique.Add(name) || !textureResources.Exists(entry => entry != null && entry.LogicalName == name))
                { diagnostic = "Material Entry Texture resource assignment is invalid."; return false; }
            }
            materialEntry.SetTextureResourceNames(resourceNames);
            return true;
        }

        private bool IsFbmImportedTextureResource(TextureResourceEntry entry, string fbmName)
        {
            return entry != null && entry.Owner.Scope == TextureResourceOwnerScope.Figure
                && string.Equals(entry.Owner.SourceShapeKey, fbmName, StringComparison.Ordinal);
        }

        private bool TryValidateTextureResourceOwner(TextureResourceOwner owner, out string diagnostic)
        {
            diagnostic = null;
            if (owner.Scope == TextureResourceOwnerScope.Figure)
            {
                if (!string.IsNullOrEmpty(owner.OutfitIdentity))
                { diagnostic = "Figure Texture owner cannot contain an Outfit identity."; return false; }
                if (!string.IsNullOrEmpty(owner.SourceShapeKey)
                    && !figureAxes.Any(axis => axis != null && axis.Kind == FigureAxisKind.Fbm && axis.Name == owner.SourceShapeKey))
                { diagnostic = "Figure Texture owner requires an existing FBM shape key."; return false; }
                return true;
            }
            if (owner.Scope == TextureResourceOwnerScope.Outfit)
            {
                if (!IsValidUserName(owner.OutfitIdentity))
                { diagnostic = "Outfit Texture owner requires a valid Outfit identity."; return false; }
                return true;
            }
            diagnostic = "Texture resource owner scope is invalid.";
            return false;
        }

        private static bool TryGetRelativePath(Transform root, Transform target, out string path)
        {
            path = null;
            if (root == null || target == null || (target != root && !target.IsChildOf(root))) return false;
            if (target == root) { path = string.Empty; return true; }
            var segments = new Stack<string>();
            for (Transform current = target; current != root; current = current.parent) segments.Push(current.GetSiblingIndex().ToString(System.Globalization.CultureInfo.InvariantCulture));
            path = string.Join("/", segments);
            return true;
        }

        private static Transform TryResolveRelativePath(Transform root, string path)
        {
            if (root == null || path == null) return null;
            if (path.Length == 0) return root;
            Transform current = root;
            string[] segments = path.Split('/');
            foreach (string segment in segments)
            {
                if (!int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index) || index < 0 || index >= current.childCount) return null;
                current = current.GetChild(index);
            }
            return current;
        }

        private static bool IsDirectIntermediateChild(ShapeSyncDatabase database, GameObject figure)
        {
            return database != null && figure != null && figure.transform.parent != null &&
                figure.transform.parent.name == "Intermediate" && figure.transform.parent.parent == database.transform;
        }

        private static GameObject ResolveDirectIntermediateChild(ShapeSyncDatabase database, GameObject value)
        {
            if (database == null || value == null) return null;
            // A loaded Prefab-contents instance is already the authoritative object
            // for this transaction.  Prefer it over a name lookup: stale duplicate
            // children can exist in a Database left by an earlier failed import, and
            // Transform.Find would otherwise silently bind the first (possibly null-
            // mesh) duplicate instead of the object supplied by the caller.
            if (IsDirectIntermediateChild(database, value)) return value;
            Transform resolved = database.transform.Find("Intermediate/" + value.name);
            return resolved != null && IsDirectIntermediateChild(database, resolved.gameObject) ? resolved.gameObject : value;
        }
    }
}
