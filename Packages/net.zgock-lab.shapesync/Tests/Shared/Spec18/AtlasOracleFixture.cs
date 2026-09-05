// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

// Shared Oracle asset.

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Names the comparison rule owned by one Atlas Oracle layer.</summary>
    internal enum AtlasOracleComparisonMode
    {
        ExactStructure,
        PixelTolerance
    }

    /// <summary>Names one independent Atlas Oracle layer.</summary>
    internal enum AtlasOracleLayer
    {
        Layout,
        MeshUv,
        Image
    }

    /// <summary>States whether a semantic page context is a potential layout or an actual Baker input.</summary>
    internal enum AtlasOracleSemanticParticipation
    {
        Potential,
        Actual
    }

    /// <summary>Detached context identifying one MaterialId for one Oracle layer on one potential semantic page.</summary>
    internal sealed class AtlasOracleEntryMetadata
    {
        internal AtlasOracleEntryMetadata(int schemaVersion, string packingAlgorithm, int pageExtent, string figureIdentity, string documentIdentity, MaterialId materialId, string sourceMaterialIdentity, AtlasTextureSemantic semantic, AtlasOracleLayer layer, AtlasOracleSemanticParticipation participation, AtlasLayoutCell cell, AtlasOracleComparisonMode comparisonMode)
        {
            SchemaVersion = schemaVersion;
            PackingAlgorithm = packingAlgorithm ?? string.Empty;
            PageExtent = pageExtent;
            FigureIdentity = figureIdentity ?? string.Empty;
            DocumentIdentity = documentIdentity ?? string.Empty;
            MaterialId = materialId;
            SourceMaterialIdentity = sourceMaterialIdentity ?? string.Empty;
            Semantic = semantic;
            Layer = layer;
            Participation = participation;
            PageIndex = cell?.PageIndex ?? -1;
            X = cell?.X ?? -1;
            Y = cell?.Y ?? -1;
            Width = cell?.Width ?? 0;
            Height = cell?.Height ?? 0;
            Gutter = cell?.Gutter ?? -1;
            ComparisonMode = comparisonMode;
        }

        internal int SchemaVersion { get; }
        internal string PackingAlgorithm { get; }
        internal int PageExtent { get; }
        internal string FigureIdentity { get; }
        internal string DocumentIdentity { get; }
        internal MaterialId MaterialId { get; }
        internal string SourceMaterialIdentity { get; }
        internal AtlasTextureSemantic Semantic { get; }
        internal AtlasOracleLayer Layer { get; }
        internal AtlasOracleSemanticParticipation Participation { get; }
        internal int PageIndex { get; }
        internal int X { get; }
        internal int Y { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int Gutter { get; }
        internal AtlasOracleComparisonMode ComparisonMode { get; }
    }

    /// <summary>Builds detached, test-only input and metadata shared by the three Atlas Oracle layers.</summary>
    internal sealed class AtlasOracleFixture
    {
        private readonly AtlasSchemaDocument document;
        private readonly AtlasLayoutResult layout;
        private readonly IReadOnlyList<AtlasOracleEntryMetadata> metadata;

        private AtlasOracleFixture(AtlasSchemaDocument document, AtlasLayoutResult layout, IReadOnlyList<AtlasOracleEntryMetadata> metadata)
        {
            this.document = document?.Clone();
            this.layout = layout;
            this.metadata = new List<AtlasOracleEntryMetadata>(metadata ?? Array.Empty<AtlasOracleEntryMetadata>()).AsReadOnly();
        }

        internal AtlasSchemaDocument Document => document?.Clone();
        internal AtlasLayoutResult Layout => layout;
        internal IReadOnlyList<AtlasOracleEntryMetadata> Metadata => metadata;

        internal static bool TryCreate(AtlasSchemaDocument document, out AtlasOracleFixture fixture, out StackMachineDiagnostic diagnostic)
        {
            fixture = null;
            if (!AtlasSchemaValidation.TryValidate(document, out diagnostic)) return false;
            if (!AtlasLayoutOracle.Solve(document, out AtlasLayoutResult layout, out diagnostic)) return false;

            var metadata = new List<AtlasOracleEntryMetadata>();
            AtlasValidationIdentity identity = document.ValidationIdentity;
            var sources = new Dictionary<MaterialId, string>();
            foreach (AtlasSourceMaterialIdentity source in identity.SourceMaterials)
                sources.Add(source.MaterialId.ToMaterialId(), source.SourceMaterialIdentity);
            foreach (AtlasLayoutCell cell in layout.Cells)
            {
                if (cell == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasOracleFixtureCellInvalid", "Atlas Oracle fixture requires non-null solved cells.");
                    return false;
                }
                if (!sources.TryGetValue(cell.MaterialId, out string sourceMaterialIdentity))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasOracleFixtureSourceIdentityMissing", "Atlas Oracle fixture requires source Material provenance for every solved cell.");
                    return false;
                }
                AddPotentialContexts(metadata, document, layout, identity, cell, sourceMaterialIdentity, AtlasTextureSemantic.BaseColor);
                AddPotentialContexts(metadata, document, layout, identity, cell, sourceMaterialIdentity, AtlasTextureSemantic.Normal);
            }

            fixture = new AtlasOracleFixture(document, layout, metadata);
            diagnostic = null;
            return true;
        }

        private static void AddPotentialContexts(List<AtlasOracleEntryMetadata> metadata, AtlasSchemaDocument document, AtlasLayoutResult layout, AtlasValidationIdentity identity, AtlasLayoutCell cell, string sourceMaterialIdentity, AtlasTextureSemantic semantic)
        {
            metadata.Add(new AtlasOracleEntryMetadata(document.AtlasSchemaVersion, document.PackingAlgorithm, layout.PageExtent, identity.FigureIdentity, identity.DocumentIdentity, cell.MaterialId, sourceMaterialIdentity, semantic, AtlasOracleLayer.Layout, AtlasOracleSemanticParticipation.Potential, cell, AtlasOracleComparisonMode.ExactStructure));
            metadata.Add(new AtlasOracleEntryMetadata(document.AtlasSchemaVersion, document.PackingAlgorithm, layout.PageExtent, identity.FigureIdentity, identity.DocumentIdentity, cell.MaterialId, sourceMaterialIdentity, semantic, AtlasOracleLayer.MeshUv, AtlasOracleSemanticParticipation.Potential, cell, AtlasOracleComparisonMode.ExactStructure));
            metadata.Add(new AtlasOracleEntryMetadata(document.AtlasSchemaVersion, document.PackingAlgorithm, layout.PageExtent, identity.FigureIdentity, identity.DocumentIdentity, cell.MaterialId, sourceMaterialIdentity, semantic, AtlasOracleLayer.Image, AtlasOracleSemanticParticipation.Potential, cell, AtlasOracleComparisonMode.PixelTolerance));
        }
    }
}
