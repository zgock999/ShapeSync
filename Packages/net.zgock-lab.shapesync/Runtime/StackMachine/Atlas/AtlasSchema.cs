// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Identifies the supported serialized Atlas Schema format.</summary>
    public static class AtlasSchemaVersion
    {
        /// <summary>Current Atlas Phase-0 schema version.</summary>
        public const int Current = 1;
    }

    /// <summary>Names the deterministic placement implementation declared by an Atlas Schema.</summary>
    public static class AtlasPackingAlgorithm
    {
        /// <summary>Deterministic Phase-0 first-fit buddy placement.</summary>
        public const string FirstFitBuddyV1 = "first-fit-buddy-v1";
    }

    /// <summary>Serializable MaterialId representation used by Atlas authoring assets.</summary>
    [Serializable]
    public sealed class AtlasMaterialId
    {
        [SerializeField] private string registryId;
        [SerializeField] private string entryId;

        /// <summary>Creates a serialized material identifier.</summary>
        public AtlasMaterialId(string registryId, string entryId)
        {
            this.registryId = registryId ?? string.Empty;
            this.entryId = entryId ?? string.Empty;
        }

        /// <summary>Gets the Figure-empty or Outfit registry identifier.</summary>
        public string RegistryId => registryId ?? string.Empty;
        /// <summary>Gets the owner-local MaterialProxy entry identifier.</summary>
        public string EntryId => entryId ?? string.Empty;
        /// <summary>Creates the runtime value key represented by this serialized value.</summary>
        public MaterialId ToMaterialId() => new MaterialId(RegistryId, EntryId);
        /// <summary>Creates a deep copy of this value.</summary>
        public AtlasMaterialId Clone() => new AtlasMaterialId(RegistryId, EntryId);
    }

    /// <summary>One user-authored Atlas assignment for a MaterialProxy entry.</summary>
    [Serializable]
    public sealed class AtlasSchemaEntry
    {
        [SerializeField] private AtlasMaterialId materialId = new AtlasMaterialId(string.Empty, string.Empty);
        [SerializeField] private int pageIndex;
        [SerializeField] private int cellLevelX;
        [SerializeField] private int cellLevelY;
        [SerializeField] private bool excluded = true;
        [SerializeField] private int gutter;

        /// <summary>Creates one Atlas assignment.</summary>
        public AtlasSchemaEntry(MaterialId materialId, int pageIndex, int cellLevelX, int cellLevelY, bool excluded, int gutter = 0)
        {
            this.materialId = new AtlasMaterialId(materialId.RegistryId, materialId.EntryId);
            this.pageIndex = pageIndex;
            this.cellLevelX = cellLevelX;
            this.cellLevelY = cellLevelY;
            this.excluded = excluded;
            this.gutter = gutter;
        }

        /// <summary>Gets the stable entry identity. It never contains a submesh index.</summary>
        public AtlasMaterialId MaterialId => materialId ?? new AtlasMaterialId(string.Empty, string.Empty);
        /// <summary>Gets the user grouping key. It is normalized to a dense page index only at evaluation time.</summary>
        public int PageIndex => pageIndex;
        /// <summary>Gets the page-width cell reduction exponent.</summary>
        public int CellLevelX => cellLevelX;
        /// <summary>Gets the page-height cell reduction exponent.</summary>
        public int CellLevelY => cellLevelY;
        /// <summary>Gets whether this entry is intentionally excluded from Atlas evaluation.</summary>
        public bool Excluded => excluded;
        /// <summary>Gets the optional gutter width in texels.</summary>
        public int Gutter => gutter;
        /// <summary>Creates a deep copy of this entry.</summary>
        public AtlasSchemaEntry Clone() => new AtlasSchemaEntry(MaterialId.ToMaterialId(), PageIndex, CellLevelX, CellLevelY, Excluded, Gutter);
    }

    /// <summary>Maps one schema entry to the source Material identity observed during validation.</summary>
    [Serializable]
    public sealed class AtlasSourceMaterialIdentity
    {
        [SerializeField] private AtlasMaterialId materialId = new AtlasMaterialId(string.Empty, string.Empty);
        [SerializeField] private string sourceMaterialIdentity;

        /// <summary>Creates a source Material identity record.</summary>
        public AtlasSourceMaterialIdentity(MaterialId materialId, string sourceMaterialIdentity)
        {
            this.materialId = new AtlasMaterialId(materialId.RegistryId, materialId.EntryId);
            this.sourceMaterialIdentity = sourceMaterialIdentity ?? string.Empty;
        }

        /// <summary>Gets the schema entry identity.</summary>
        public AtlasMaterialId MaterialId => materialId ?? new AtlasMaterialId(string.Empty, string.Empty);
        /// <summary>Gets the validation-time source Material identity.</summary>
        public string SourceMaterialIdentity => sourceMaterialIdentity ?? string.Empty;
        /// <summary>Creates a deep copy of this record.</summary>
        public AtlasSourceMaterialIdentity Clone() => new AtlasSourceMaterialIdentity(MaterialId.ToMaterialId(), SourceMaterialIdentity);
    }

    /// <summary>Identifies the first validation provenance field that differs from a current build input.</summary>
    public enum AtlasValidationIdentityDifferenceKind
    {
        /// <summary>No difference was found.</summary>
        None,
        /// <summary>The Figure identity differs.</summary>
        Figure,
        /// <summary>The Document identity differs.</summary>
        Document,
        /// <summary>The current build did not provide provenance to compare.</summary>
        CurrentIdentityMissing,
        /// <summary>A source Material identity for a shared MaterialId is malformed or duplicated.</summary>
        CurrentSourceMaterialIdentityInvalid,
        /// <summary>A Schema MaterialId resolves to a different current source Material identity.</summary>
        SourceMaterialChanged
    }

    /// <summary>One ordinal comparison difference between Schema validation provenance and a current build input.</summary>
    public sealed class AtlasValidationIdentityDifference
    {
        /// <summary>Creates a comparison difference.</summary>
        public AtlasValidationIdentityDifference(AtlasValidationIdentityDifferenceKind kind, MaterialId materialId, string expectedIdentity, string actualIdentity)
        {
            Kind = kind;
            MaterialId = materialId;
            ExpectedIdentity = expectedIdentity ?? string.Empty;
            ActualIdentity = actualIdentity ?? string.Empty;
        }

        /// <summary>Gets the differing provenance field.</summary>
        public AtlasValidationIdentityDifferenceKind Kind { get; }
        /// <summary>Gets the affected MaterialId, or an invalid value for Figure and Document differences.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the canonical identity recorded by the Schema.</summary>
        public string ExpectedIdentity { get; }
        /// <summary>Gets the canonical identity observed by the current build.</summary>
        public string ActualIdentity { get; }
    }

    /// <summary>Describes the authoring inputs observed when an Atlas Schema was validated.</summary>
    [Serializable]
    public sealed class AtlasValidationIdentity
    {
        [SerializeField] private string figureIdentity;
        [SerializeField] private string documentIdentity;
        [SerializeField] private List<AtlasSourceMaterialIdentity> sourceMaterials = new List<AtlasSourceMaterialIdentity>();

        /// <summary>Creates validation provenance without retaining Unity object references.</summary>
        public AtlasValidationIdentity(string figureIdentity, string documentIdentity, IReadOnlyList<AtlasSourceMaterialIdentity> sourceMaterials = null)
        {
            this.figureIdentity = figureIdentity ?? string.Empty;
            this.documentIdentity = documentIdentity ?? string.Empty;
            this.sourceMaterials = CopySourceMaterials(sourceMaterials);
        }

        /// <summary>Gets the Figure identity captured by validation.</summary>
        public string FigureIdentity => figureIdentity ?? string.Empty;
        /// <summary>Gets the Document identity captured by validation.</summary>
        public string DocumentIdentity => documentIdentity ?? string.Empty;
        /// <summary>Gets source Material identities keyed by their schema MaterialId.</summary>
        public IReadOnlyList<AtlasSourceMaterialIdentity> SourceMaterials => (sourceMaterials ?? new List<AtlasSourceMaterialIdentity>()).AsReadOnly();
        /// <summary>Creates a deep copy of this validation provenance.</summary>
        public AtlasValidationIdentity Clone() => new AtlasValidationIdentity(FigureIdentity, DocumentIdentity, SourceMaterials);

        /// <summary>Finds the first ordinal difference from the current Figure, Document, and shared source Material identity tokens.</summary>
        /// <remarks>The recorded Schema provenance must have passed <see cref="AtlasSchemaValidation.TryValidate"/>. Identity tokens are caller-authored canonical values: they are never normalized, and equality is <see cref="StringComparison.Ordinal"/>. MaterialIds absent from the current build are intentionally ignored here; the Baker owns their Info-level Schema reconciliation. A malformed or duplicate current source token is rejected only when its MaterialId is shared with the Schema. The Editor supplies tokens from its authoritative source; the Baker supplies tokens from its actual build input.</remarks>
        /// <param name="current">Current build provenance to compare with this Schema provenance.</param>
        /// <param name="difference">The first difference, or <c>null</c> when the identity values match.</param>
        /// <returns><see langword="true"/> when every Schema identity matches the current build.</returns>
        public bool TryMatchCurrent(AtlasValidationIdentity current, out AtlasValidationIdentityDifference difference)
        {
            difference = null;
            if (current == null)
            {
                difference = new AtlasValidationIdentityDifference(AtlasValidationIdentityDifferenceKind.CurrentIdentityMissing, default(MaterialId), string.Empty, string.Empty);
                return false;
            }
            if (!string.Equals(FigureIdentity, current.FigureIdentity, StringComparison.Ordinal))
            {
                difference = new AtlasValidationIdentityDifference(AtlasValidationIdentityDifferenceKind.Figure, default(MaterialId), FigureIdentity, current.FigureIdentity);
                return false;
            }
            if (!string.Equals(DocumentIdentity, current.DocumentIdentity, StringComparison.Ordinal))
            {
                difference = new AtlasValidationIdentityDifference(AtlasValidationIdentityDifferenceKind.Document, default(MaterialId), DocumentIdentity, current.DocumentIdentity);
                return false;
            }
            var expectedMaterialIds = new HashSet<MaterialId>();
            IReadOnlyList<AtlasSourceMaterialIdentity> expectedSources = SourceMaterials;
            for (int i = 0; i < expectedSources.Count; i++)
            {
                AtlasSourceMaterialIdentity expected = expectedSources[i];
                if (expected != null) expectedMaterialIds.Add(expected.MaterialId.ToMaterialId());
            }
            var currentSources = new Dictionary<MaterialId, string>();
            IReadOnlyList<AtlasSourceMaterialIdentity> currentSourceMaterials = current.SourceMaterials;
            for (int i = 0; i < currentSourceMaterials.Count; i++)
            {
                AtlasSourceMaterialIdentity source = currentSourceMaterials[i];
                if (source == null) continue;
                MaterialId materialId = source.MaterialId.ToMaterialId();
                if (!expectedMaterialIds.Contains(materialId)) continue;
                if (!materialId.IsValid || string.IsNullOrWhiteSpace(source.SourceMaterialIdentity) || !currentSources.TryAdd(materialId, source.SourceMaterialIdentity))
                {
                    difference = new AtlasValidationIdentityDifference(AtlasValidationIdentityDifferenceKind.CurrentSourceMaterialIdentityInvalid, materialId, string.Empty, source.SourceMaterialIdentity);
                    return false;
                }
            }
            for (int i = 0; i < expectedSources.Count; i++)
            {
                AtlasSourceMaterialIdentity expected = expectedSources[i];
                if (expected == null) continue;
                MaterialId materialId = expected.MaterialId.ToMaterialId();
                if (!currentSources.TryGetValue(materialId, out string actual)) continue;
                if (!string.Equals(expected.SourceMaterialIdentity, actual, StringComparison.Ordinal))
                {
                    difference = new AtlasValidationIdentityDifference(AtlasValidationIdentityDifferenceKind.SourceMaterialChanged, materialId, expected.SourceMaterialIdentity, actual);
                    return false;
                }
            }
            return true;
        }

        private static List<AtlasSourceMaterialIdentity> CopySourceMaterials(IReadOnlyList<AtlasSourceMaterialIdentity> source)
        {
            var copy = new List<AtlasSourceMaterialIdentity>();
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++) copy.Add(source[i]?.Clone());
            return copy;
        }
    }

    /// <summary>Detached user-input document copied from an <see cref="AtlasSchema"/> asset.</summary>
    /// <remarks>This document deliberately contains no solved layout, cell rectangle, Unity object reference, or GPU state.</remarks>
    [Serializable]
    public sealed class AtlasSchemaDocument
    {
        [SerializeField] private int atlasSchemaVersion = global::zgock.ShapeSync.StackMachine.AtlasSchemaVersion.Current;
        [SerializeField] private int pageExtent = 2048;
        [SerializeField] private string packingAlgorithm = AtlasPackingAlgorithm.FirstFitBuddyV1;
        [SerializeField] private bool deterministic = true;
        [SerializeField] private AtlasValidationIdentity validationIdentity = new AtlasValidationIdentity(string.Empty, string.Empty);
        [SerializeField] private List<AtlasSchemaEntry> entries = new List<AtlasSchemaEntry>();

        /// <summary>Creates a detached schema document.</summary>
        public AtlasSchemaDocument(int atlasSchemaVersion, int pageExtent, string packingAlgorithm, bool deterministic, AtlasValidationIdentity validationIdentity, IReadOnlyList<AtlasSchemaEntry> entries)
        {
            this.atlasSchemaVersion = atlasSchemaVersion;
            this.pageExtent = pageExtent;
            this.packingAlgorithm = packingAlgorithm ?? string.Empty;
            this.deterministic = deterministic;
            this.validationIdentity = validationIdentity?.Clone() ?? new AtlasValidationIdentity(string.Empty, string.Empty);
            this.entries = CopyEntries(entries);
        }

        /// <summary>Gets the serialized schema version.</summary>
        public int AtlasSchemaVersion => atlasSchemaVersion;
        /// <summary>Gets the common square page extent.</summary>
        public int PageExtent => pageExtent;
        /// <summary>Gets the declared deterministic placement algorithm.</summary>
        public string PackingAlgorithm => packingAlgorithm ?? string.Empty;
        /// <summary>Gets whether the declared packing algorithm is deterministic.</summary>
        public bool IsDeterministic => deterministic;
        /// <summary>Gets validation provenance captured by the authoring flow.</summary>
        public AtlasValidationIdentity ValidationIdentity => validationIdentity?.Clone() ?? new AtlasValidationIdentity(string.Empty, string.Empty);
        /// <summary>Gets deep-copied user entry assignments.</summary>
        public IReadOnlyList<AtlasSchemaEntry> Entries => (entries ?? new List<AtlasSchemaEntry>()).AsReadOnly();
        /// <summary>Creates a detached deep copy.</summary>
        public AtlasSchemaDocument Clone() => new AtlasSchemaDocument(AtlasSchemaVersion, PageExtent, PackingAlgorithm, IsDeterministic, ValidationIdentity, Entries);

        private static List<AtlasSchemaEntry> CopyEntries(IReadOnlyList<AtlasSchemaEntry> source)
        {
            var copy = new List<AtlasSchemaEntry>();
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++) copy.Add(source[i]?.Clone());
            return copy;
        }
    }

    /// <summary>Serialized Phase-0 Atlas authoring carrier.</summary>
    /// <remarks>The carrier stores only user input and validation provenance. Layout, semantic pages, rectangles, textures, and execution state are always derived.</remarks>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/Atlas Schema", fileName = "AtlasSchema")]
    public sealed class AtlasSchema : ScriptableObject
    {
        [SerializeField] private int atlasSchemaVersion = global::zgock.ShapeSync.StackMachine.AtlasSchemaVersion.Current;
        [SerializeField] private int pageExtent = 2048;
        [SerializeField] private string packingAlgorithm = AtlasPackingAlgorithm.FirstFitBuddyV1;
        [SerializeField] private bool deterministic = true;
        [SerializeField] private AtlasValidationIdentity validationIdentity = new AtlasValidationIdentity(string.Empty, string.Empty);
        [SerializeField] private List<AtlasSchemaEntry> entries = new List<AtlasSchemaEntry>();

        /// <summary>Gets the serialized schema version.</summary>
        public int AtlasSchemaVersion => atlasSchemaVersion;
        /// <summary>Gets the common square page extent.</summary>
        public int PageExtent => pageExtent;
        /// <summary>Gets the declared deterministic placement algorithm.</summary>
        public string PackingAlgorithm => packingAlgorithm ?? string.Empty;
        /// <summary>Gets whether the declared packing algorithm is deterministic.</summary>
        public bool IsDeterministic => deterministic;
        /// <summary>Gets validation provenance captured by the authoring flow.</summary>
        public AtlasValidationIdentity ValidationIdentity => validationIdentity?.Clone() ?? new AtlasValidationIdentity(string.Empty, string.Empty);
        /// <summary>Gets the authored entry assignments.</summary>
        public IReadOnlyList<AtlasSchemaEntry> Entries => (entries ?? new List<AtlasSchemaEntry>()).AsReadOnly();

        /// <summary>Creates a detached copy of this serialized carrier.</summary>
        public AtlasSchemaDocument ToDocument() => new AtlasSchemaDocument(AtlasSchemaVersion, PageExtent, PackingAlgorithm, IsDeterministic, ValidationIdentity, Entries);

        /// <summary>Replaces this asset's user input from a detached document.</summary>
        /// <param name="document">Validated document to copy into this asset.</param>
        /// <param name="diagnostic">A structured validation failure.</param>
        /// <returns><see langword="true"/> when the asset was updated.</returns>
        public bool TrySetDocument(AtlasSchemaDocument document, out StackMachineDiagnostic diagnostic)
        {
            if (!AtlasSchemaValidation.TryValidate(document, out diagnostic)) return false;
            AtlasSchemaDocument copy = document.Clone();
            atlasSchemaVersion = copy.AtlasSchemaVersion;
            pageExtent = copy.PageExtent;
            packingAlgorithm = copy.PackingAlgorithm;
            deterministic = copy.IsDeterministic;
            validationIdentity = copy.ValidationIdentity;
            entries = new List<AtlasSchemaEntry>();
            foreach (AtlasSchemaEntry entry in copy.Entries) entries.Add(entry?.Clone());
            return true;
        }
    }

    /// <summary>Validates Atlas Schema user input without solving layout or accessing UnityEditor.</summary>
    public static class AtlasSchemaValidation
    {
        /// <summary>Validates a detached Atlas Schema document.</summary>
        /// <param name="document">Document containing user input only.</param>
        /// <param name="diagnostic">Structured failure when the input is invalid.</param>
        /// <returns><see langword="true"/> when the user input is valid for Phase-0 evaluation.</returns>
        public static bool TryValidate(AtlasSchemaDocument document, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (document == null) return Fail("AtlasSchemaRequired", "Atlas Schema document is required.", out diagnostic);
            if (document.AtlasSchemaVersion != AtlasSchemaVersion.Current) return Fail("AtlasSchemaVersionUnsupported", "Atlas Schema version is unsupported.", out diagnostic);
            if (!IsSupportedPageExtent(document.PageExtent)) return Fail("AtlasPageExtentUnsupported", "Atlas page extent must be one of 512, 1024, 2048, or 4096.", out diagnostic);
            if (!string.Equals(document.PackingAlgorithm, AtlasPackingAlgorithm.FirstFitBuddyV1, StringComparison.Ordinal)) return Fail("AtlasPackingAlgorithmUnsupported", "Atlas packing algorithm is unsupported.", out diagnostic);
            if (!document.IsDeterministic) return Fail("AtlasPackingDeterminismRequired", "Atlas packing must declare deterministic evaluation.", out diagnostic);

            AtlasValidationIdentity identity = document.ValidationIdentity;
            if (string.IsNullOrWhiteSpace(identity.FigureIdentity)) return Fail("AtlasFigureIdentityRequired", "Atlas validation requires a Figure identity.", out diagnostic);
            if (string.IsNullOrWhiteSpace(identity.DocumentIdentity)) return Fail("AtlasDocumentIdentityRequired", "Atlas validation requires a Document identity.", out diagnostic);
            var sourceIdentities = new Dictionary<MaterialId, AtlasSourceMaterialIdentity>();
            IReadOnlyList<AtlasSourceMaterialIdentity> sourceMaterials = identity.SourceMaterials;
            for (int i = 0; i < sourceMaterials.Count; i++)
            {
                AtlasSourceMaterialIdentity source = sourceMaterials[i];
                if (source == null) return Fail("AtlasSourceMaterialIdentityMissing", "Atlas validation contains a null source Material identity.", out diagnostic, i);
                MaterialId sourceId = source.MaterialId.ToMaterialId();
                if (!sourceId.IsValid || string.IsNullOrWhiteSpace(source.SourceMaterialIdentity) || !sourceIdentities.TryAdd(sourceId, source))
                    return Fail("AtlasSourceMaterialIdentityInvalid", "Atlas validation requires one unique non-empty source Material identity per MaterialId.", out diagnostic, i);
            }

            var seen = new HashSet<MaterialId>();
            IReadOnlyList<AtlasSchemaEntry> entries = document.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                AtlasSchemaEntry entry = entries[i];
                if (entry == null) return Fail("AtlasEntryMissing", "Atlas Schema contains a null entry.", out diagnostic, i);
                MaterialId materialId = entry.MaterialId.ToMaterialId();
                if (!materialId.IsValid) return Fail("AtlasMaterialIdInvalid", "Atlas Schema entry requires a MaterialId entry ID.", out diagnostic, i);
                if (!seen.Add(materialId)) return Fail("AtlasMaterialIdDuplicate", "Atlas Schema contains a duplicate MaterialId '" + materialId + "'.", out diagnostic, i);
                if (!sourceIdentities.ContainsKey(materialId)) return Fail("AtlasSourceMaterialIdentityRequired", "Atlas Schema entry requires its validation-time source Material identity.", out diagnostic, i);
                if (entry.Excluded) continue;
                if (entry.CellLevelX < 0 || entry.CellLevelX > 3 || entry.CellLevelY < 0 || entry.CellLevelY > 3)
                    return Fail("AtlasCellLevelInvalid", "Atlas cell levels must be in the inclusive range 0 through 3.", out diagnostic, i);
                if (entry.Gutter < 0 || entry.Gutter % 4 != 0)
                    return Fail("AtlasGutterInvalid", "Atlas gutter must be zero or a positive multiple of four texels.", out diagnostic, i);
            }
            if (sourceIdentities.Count != seen.Count) return Fail("AtlasSourceMaterialIdentityOrphaned", "Atlas validation contains a source Material identity without a matching Schema entry.", out diagnostic);
            return true;
        }

        /// <summary>Determines whether an extent is a Phase-0 Atlas page size.</summary>
        public static bool IsSupportedPageExtent(int extent) => extent == 512 || extent == 1024 || extent == 2048 || extent == 4096;

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, int entryIndex = -1)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message, detail: entryIndex < 0 ? null : "entryIndex=" + entryIndex);
            return false;
        }
    }
}
