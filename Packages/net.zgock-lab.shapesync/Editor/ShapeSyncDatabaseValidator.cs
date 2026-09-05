// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Non-mutating whole-Database validator for declared authoring relations.
    /// It reports every localized defect in deterministic order and never performs Generate.
    /// </summary>
    public static class ShapeSyncDatabaseValidator
    {
        /// <summary>Returns all declared-relation diagnostics for one Database snapshot.</summary>
        public static IReadOnlyList<ShapeSyncDatabaseDiagnostic> Validate(ShapeSyncDatabase database)
        {
            var diagnostics = new List<ShapeSyncDatabaseDiagnostic>();
            if (database == null)
            {
                Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.DatabaseRequired,
                    ShapeSyncDatabaseEntityKind.Database, ShapeSyncDatabaseRelationKind.None,
                    "Database", null, null, "Database is required.");
                return diagnostics;
            }

            ShapeSyncDatabaseRegistry registry = database.Registry;
            if (registry == null)
            {
                Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RegistryRequired,
                    ShapeSyncDatabaseEntityKind.Registry, ShapeSyncDatabaseRelationKind.Registry,
                    "Registry", "Database", null, "Database Registry relation is missing.");
                return diagnostics;
            }

            ValidateBaseFigures(database, registry, diagnostics);
            ValidateMaterials(registry, diagnostics);
            ValidateTextures(registry, diagnostics);
            ValidateAxes(database, registry, diagnostics);
            ValidateNormals(registry, diagnostics);
            ValidateOutfits(registry, diagnostics);
            ValidateShapes(registry, diagnostics);
            return Order(diagnostics);
        }

        /// <summary>
        /// Returns the non-mutating preflight diagnostics required at the Generate boundary.
        /// In addition to the declared Database relations, this verifies that every Morph
        /// Shape has one serialized value for every registered FBM/PBM axis, including zero.
        /// </summary>
        public static IReadOnlyList<ShapeSyncDatabaseDiagnostic> ValidateForGeneration(ShapeSyncDatabase database)
        {
            IReadOnlyList<ShapeSyncDatabaseDiagnostic> baseDiagnostics = Validate(database);
            if (database == null || database.Registry == null) return baseDiagnostics;

            var diagnostics = baseDiagnostics.ToList();
            ValidateMorphCompleteness(database.Registry, diagnostics);
            return Order(diagnostics);
        }

        /// <summary>Validates all Generate preconditions without mutating Database or assets.</summary>
        public static bool TryValidateForGeneration(ShapeSyncDatabase database,
            out IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            diagnostics = ValidateForGeneration(database);
            return diagnostics.Count == 0;
        }

        private static IReadOnlyList<ShapeSyncDatabaseDiagnostic> Order(IEnumerable<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            return diagnostics
                .OrderBy(item => item.EntityKind)
                .ThenBy(item => item.EntityId, StringComparer.Ordinal)
                .ThenBy(item => item.RelationKind)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.TargetId, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ValidateMorphCompleteness(ShapeSyncDatabaseRegistry registry,
            List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            string[] axisNames = (registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>())
                .Where(axis => axis != null && !string.IsNullOrWhiteSpace(axis.Name))
                .Select(axis => axis.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (axisNames.Length == 0) return;

            foreach (ShapeSyncDatabaseRegistry.ShapeEntry shape in registry.Shapes ?? Array.Empty<ShapeSyncDatabaseRegistry.ShapeEntry>())
            {
                if (shape == null || shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph
                    || string.IsNullOrWhiteSpace(shape.ShapeId)) continue;
                var targets = new HashSet<string>((shape.Morphs ?? Array.Empty<MorphValue>())
                    .Select(value => value.Target), StringComparer.Ordinal);
                foreach (string axisName in axisNames)
                    if (!targets.Contains(axisName))
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                            ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                            shape.ShapeId, shape.ShapeId, axisName,
                            "Morph Shape must declare a serialized value for every Figure FBM/PBM axis, including zero.");
            }
        }

        /// <summary>Validates a Database without mutating it and returns the complete diagnostic set.</summary>
        public static bool TryValidate(ShapeSyncDatabase database, out IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            diagnostics = Validate(database);
            return diagnostics.Count == 0;
        }

        private static void ValidateBaseFigures(ShapeSyncDatabase database, ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            if (!ShapeSyncDatabaseAdmission.TryValidateBaseFigureCardinality(registry.BaseFigures, out ShapeSyncDatabaseDiagnostic cardinality))
                diagnostics.Add(cardinality);
            if (registry.BaseFigures == null) return;
            for (int index = 0; index < registry.BaseFigures.Count; index++)
            {
                ShapeSyncDatabaseRegistry.BaseFigureEntry entry = registry.BaseFigures[index];
                string id = entry?.Name ?? "#" + index;
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || entry.Figure == null)
                {
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.EntityInvalid,
                        ShapeSyncDatabaseEntityKind.BaseFigure, ShapeSyncDatabaseRelationKind.BaseFigure,
                        id, "Database", entry?.Figure == null ? null : entry.Figure.name,
                        "Base Figure entry or its Figure target is missing.");
                    continue;
                }
                Transform intermediate = database.transform.Find("Intermediate");
                if (intermediate == null || entry.Figure.transform.parent != intermediate || entry.Figure.name != entry.Name)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.BaseFigure, ShapeSyncDatabaseRelationKind.BaseFigure,
                        entry.Name, "Database", entry.Figure.name,
                        "Base Figure must target a direct Intermediate child with the registered name.");
            }
        }

        private static void ValidateMaterials(ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<ShapeSyncDatabaseRegistry.MaterialEntry> entries = registry.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialEntry>();
            IReadOnlyList<ShapeSyncDatabaseRegistry.TextureResourceEntry> resources = registry.TextureResources ?? Array.Empty<ShapeSyncDatabaseRegistry.TextureResourceEntry>();
            for (int index = 0; index < entries.Count; index++)
            {
                ShapeSyncDatabaseRegistry.MaterialEntry entry = entries[index];
                string id = entry?.LogicalName ?? "#" + index;
                if (entry == null || string.IsNullOrWhiteSpace(entry.LogicalName) || !names.Add(entry.LogicalName))
                {
                    Add(diagnostics, entry == null || string.IsNullOrWhiteSpace(entry.LogicalName)
                            ? ShapeSyncDatabaseDiagnosticCode.EntityInvalid : ShapeSyncDatabaseDiagnosticCode.EntityDuplicate,
                        ShapeSyncDatabaseEntityKind.MaterialEntry, ShapeSyncDatabaseRelationKind.None,
                        id, "Registry", null, "Material Entry identity is missing or duplicated.");
                    continue;
                }
                if (entry.Material == null || entry.Adapter == null)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                        ShapeSyncDatabaseEntityKind.MaterialEntry, ShapeSyncDatabaseRelationKind.MaterialTarget,
                        entry.LogicalName, "MaterialEntry", null,
                        "Material Entry requires its Material and Adapter relations.");
                foreach (string resourceName in entry.TextureResourceNames ?? Array.Empty<string>())
                    if (!resources.Any(resource => resource != null && resource.LogicalName == resourceName))
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.MaterialEntry, ShapeSyncDatabaseRelationKind.TextureResource,
                            entry.LogicalName, entry.LogicalName, resourceName,
                            "Material Entry references a missing Texture resource.");
            }
        }

        private static void ValidateTextures(ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<ShapeSyncDatabaseRegistry.TextureResourceEntry> resources = registry.TextureResources ?? Array.Empty<ShapeSyncDatabaseRegistry.TextureResourceEntry>();
            for (int index = 0; index < resources.Count; index++)
            {
                ShapeSyncDatabaseRegistry.TextureResourceEntry entry = resources[index];
                string id = entry?.LogicalName ?? "#" + index;
                if (entry == null || string.IsNullOrWhiteSpace(entry.LogicalName) || !names.Add(entry.LogicalName))
                {
                    Add(diagnostics, entry == null || string.IsNullOrWhiteSpace(entry.LogicalName)
                            ? ShapeSyncDatabaseDiagnosticCode.EntityInvalid : ShapeSyncDatabaseDiagnosticCode.EntityDuplicate,
                        ShapeSyncDatabaseEntityKind.TextureResource, ShapeSyncDatabaseRelationKind.None,
                        id, "Registry", null, "Texture resource identity is missing or duplicated.");
                    continue;
                }
                if (entry.Texture == null)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                        ShapeSyncDatabaseEntityKind.TextureResource, ShapeSyncDatabaseRelationKind.TextureResource,
                        entry.LogicalName, "TextureResource", null, "Texture resource payload is missing.");
                if (!Enum.IsDefined(typeof(ShapeSyncDatabaseRegistry.TextureResourceOwnerScope), entry.Owner.Scope))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.EntityInvalid,
                        ShapeSyncDatabaseEntityKind.TextureResource, ShapeSyncDatabaseRelationKind.None,
                        entry.LogicalName, "TextureResource", null, "Texture resource owner scope is invalid.");
                if (entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Outfit
                    && !(registry.Outfits ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>()).Any(outfit => outfit != null && outfit.Identity == entry.Owner.OutfitIdentity))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.TextureResource, ShapeSyncDatabaseRelationKind.TextureResource,
                        entry.LogicalName, entry.LogicalName, entry.Owner.OutfitIdentity,
                        "Texture resource owner Outfit does not exist.");
                if (entry.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure
                    && !string.IsNullOrEmpty(entry.Owner.SourceShapeKey)
                    && !(registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>()).Any(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && axis.Name == entry.Owner.SourceShapeKey))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.TextureResource, ShapeSyncDatabaseRelationKind.FigureAxis,
                        entry.LogicalName, entry.LogicalName, entry.Owner.SourceShapeKey,
                        "Figure Texture resource references a missing FBM axis.");
            }
        }

        private static void ValidateAxes(ShapeSyncDatabase database, ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var fbmNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>())
            {
                if (axis == null || string.IsNullOrWhiteSpace(axis.Name) || !names.Add(axis.Name))
                {
                    Add(diagnostics, axis == null || string.IsNullOrWhiteSpace(axis.Name)
                            ? ShapeSyncDatabaseDiagnosticCode.EntityInvalid : ShapeSyncDatabaseDiagnosticCode.EntityDuplicate,
                        ShapeSyncDatabaseEntityKind.FigureAxis, ShapeSyncDatabaseRelationKind.None,
                        axis?.Name ?? "#null", "Registry", null, "Figure axis identity is missing or duplicated.");
                    continue;
                }
                if (axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm) fbmNames.Add(axis.Name);
            }
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>())
            {
                if (axis == null || string.IsNullOrWhiteSpace(axis.Name)) continue;
                IReadOnlyList<ShapeSyncDatabaseRegistry.AxisFigureEntry> bindings = axis.Figures;
                if (bindings == null || bindings.Count == 0)
                {
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                        ShapeSyncDatabaseEntityKind.FigureAxis, ShapeSyncDatabaseRelationKind.AxisFigure,
                        axis.Name, axis.Name, null, "Figure axis has no declared Figure binding.");
                    continue;
                }
                var bindingNames = new HashSet<string>(StringComparer.Ordinal);
                var expected = axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    ? new HashSet<string>(new[] { axis.Name }, StringComparer.Ordinal)
                    : new HashSet<string>(fbmNames.Concat(new[] { ShapeSyncDatabaseRegistry.BaseShapeKey }), StringComparer.Ordinal);
                foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry binding in bindings)
                {
                    string id = binding?.FbmName ?? "#null";
                    if (binding == null || string.IsNullOrWhiteSpace(binding.FbmName) || !expected.Contains(binding.FbmName)
                        || !bindingNames.Add(binding.FbmName) || binding.Figure == null)
                    {
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.FigureAxis, ShapeSyncDatabaseRelationKind.AxisFigure,
                            axis.Name, axis.Name, id, "Figure axis binding is missing, duplicated, or targets an invalid Figure.");
                        continue;
                    }
                    Transform intermediate = database.transform.Find("Intermediate");
                    if (intermediate == null || binding.Figure.transform.parent != intermediate)
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.FigureAxis, ShapeSyncDatabaseRelationKind.AxisFigure,
                            axis.Name, axis.Name, binding.FbmName, "Figure axis binding must target a direct Intermediate child.");
                }
                foreach (string expectedName in expected)
                    if (!bindingNames.Contains(expectedName))
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                            ShapeSyncDatabaseEntityKind.FigureAxis, ShapeSyncDatabaseRelationKind.AxisFigure,
                            axis.Name, axis.Name, expectedName, "Figure axis binding is declared but missing.");
            }
        }

        private static void ValidateNormals(ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var materialNames = new HashSet<string>((registry.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialEntry>()).Where(entry => entry != null).Select(entry => entry.LogicalName), StringComparer.Ordinal);
            foreach (ShapeSyncDatabaseRegistry.FigureNormalEntry figureNormal in registry.FigureNormalEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureNormalEntry>())
                if (figureNormal == null || !materialNames.Contains(figureNormal.MaterialEntryName))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.NormalEntry, ShapeSyncDatabaseRelationKind.NormalTarget,
                        figureNormal?.MaterialEntryName ?? "#null", "FigureNormal", figureNormal?.MaterialEntryName,
                        "Figure Normal declaration targets a missing Material Entry.");
            foreach (ShapeSyncDatabaseRegistry.NormalEntry normal in registry.NormalEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.NormalEntry>())
            {
                string id = normal == null ? "#null" : normal.MaterialEntryName + "/" + normal.ShapeKey;
                if (normal == null || !materialNames.Contains(normal.MaterialEntryName)
                    || !(registry.TextureResources ?? Array.Empty<ShapeSyncDatabaseRegistry.TextureResourceEntry>()).Any(resource => resource != null && resource.LogicalName == normal.TextureResourceName && resource.Texture == normal.Texture)
                    || (normal.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey
                        && !(registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>()).Any(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && axis.Name == normal.ShapeKey)))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.NormalEntry, ShapeSyncDatabaseRelationKind.NormalTarget,
                        id, normal?.MaterialEntryName, normal?.TextureResourceName,
                        "Normal entry has a missing Material, Texture resource, or Figure axis target.");
            }
        }

        private static void ValidateOutfits(ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var validShapeKeys = new HashSet<string>((registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>()).Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name), StringComparer.Ordinal)
            { ShapeSyncDatabaseRegistry.BaseShapeKey };
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in registry.Outfits ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>())
            {
                string id = outfit?.Identity ?? "#null";
                if (outfit == null || string.IsNullOrWhiteSpace(outfit.Identity) || !identities.Add(outfit.Identity) || !Enum.IsDefined(typeof(ShapeSyncDatabaseRegistry.OutfitKind), outfit.Kind))
                {
                    Add(diagnostics, outfit == null || string.IsNullOrWhiteSpace(outfit.Identity)
                            ? ShapeSyncDatabaseDiagnosticCode.EntityInvalid : ShapeSyncDatabaseDiagnosticCode.EntityDuplicate,
                        ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.None,
                        id, "Registry", null, "Outfit identity or kind is invalid or duplicated.");
                    continue;
                }
                ValidateOutfitMaterialEntries(registry, outfit, diagnostics);
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry>())
                    if (axis == null || !validShapeKeys.Contains(axis.ShapeKey) || axis.SourcePrefab == null || axis.OutfitPrefab == null)
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.FigureAxis,
                            id, id, axis?.ShapeKey, "Outfit axis relation is missing a valid Figure shape key or Prefab.");
                foreach (ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry entry in outfit.MaterialOutfitTextureEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry>())
                    if (entry == null || !(registry.TextureResources ?? Array.Empty<ShapeSyncDatabaseRegistry.TextureResourceEntry>()).Any(resource => resource != null && resource.LogicalName == entry.TextureResourceName))
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.TextureResource,
                            id, id, entry?.TextureResourceName, "Material Outfit Texture relation targets a missing Texture resource.");
                foreach (ShapeSyncDatabaseRegistry.FigureMaskEntry entry in outfit.FigureMaskEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureMaskEntry>())
                    if (entry == null || !(registry.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialEntry>()).Any(material => material != null && material.LogicalName == entry.FigureMaterialEntryName)
                        || !(registry.TextureResources ?? Array.Empty<ShapeSyncDatabaseRegistry.TextureResourceEntry>()).Any(resource => resource != null && resource.LogicalName == entry.TextureResourceName))
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.MaterialTarget,
                            id, entry?.FigureMaterialEntryName, entry?.TextureResourceName, "Figure Mask relation targets a missing Material or Texture resource.");
                foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow in outfit.PbmFollows ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry>())
                {
                    bool axisExists = follow != null && (registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>()).Any(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && axis.Name == follow.PbmAxisName);
                    if (!axisExists)
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.FigureAxis,
                            id, id, follow?.PbmAxisName, "Outfit PBM follow targets a missing PBM axis.");
                    foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry figure in follow?.Figures ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry>())
                        if (figure == null || !validShapeKeys.Contains(figure.ShapeKey) || figure.SourcePrefab == null || figure.Figure == null)
                            Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                                ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.FigureAxis,
                                id, follow?.PbmAxisName, figure?.ShapeKey, "Outfit PBM follow Figure relation is incomplete.");
                }
                if (outfit.CollectionKind != ShapeSyncDatabaseRegistry.OutfitCollectionKind.None && (outfit.CollectionEntries == null || outfit.CollectionEntries.Count == 0))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                        ShapeSyncDatabaseEntityKind.Outfit, ShapeSyncDatabaseRelationKind.FigureAxis,
                        id, id, null, "Outfit Collection declares a collection kind but no collection entries.");
            }
        }

        private static void ValidateOutfitMaterialEntries(ShapeSyncDatabaseRegistry registry, ShapeSyncDatabaseRegistry.OutfitEntry outfit, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncDatabaseRegistry.OutfitMaterialEntry entry in outfit.MaterialEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitMaterialEntry>())
                if (entry == null || string.IsNullOrWhiteSpace(entry.LogicalName) || !names.Add(entry.LogicalName) || entry.Material == null || entry.Adapter == null)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.EntityInvalid,
                        ShapeSyncDatabaseEntityKind.MaterialEntry, ShapeSyncDatabaseRelationKind.MaterialTarget,
                        outfit.Identity, outfit.Identity, entry?.LogicalName, "Outfit Material Entry is invalid or duplicated.");
        }

        private static void ValidateShapes(ShapeSyncDatabaseRegistry registry, List<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var axisNames = new HashSet<string>((registry.FigureAxes ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>()).Where(axis => axis != null).Select(axis => axis.Name), StringComparer.Ordinal);
            foreach (ShapeSyncDatabaseRegistry.ShapeEntry shape in registry.Shapes ?? Array.Empty<ShapeSyncDatabaseRegistry.ShapeEntry>())
            {
                string id = shape?.ShapeId ?? "#null";
                if (shape == null || string.IsNullOrWhiteSpace(shape.ShapeId) || !ids.Add(shape.ShapeId) || !Enum.IsDefined(typeof(ShapeSyncDatabaseRegistry.ShapeKind), shape.Kind))
                {
                    Add(diagnostics, shape == null || string.IsNullOrWhiteSpace(shape.ShapeId)
                            ? ShapeSyncDatabaseDiagnosticCode.EntityInvalid : ShapeSyncDatabaseDiagnosticCode.EntityDuplicate,
                        ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.None,
                        id, "Registry", null, "Shape identity or kind is invalid or duplicated.");
                    continue;
                }
                IReadOnlyList<string> tags = shape.Tags ?? Array.Empty<string>();
                IReadOnlyList<MorphValue> morphs = shape.Morphs ?? Array.Empty<MorphValue>();
                IReadOnlyList<ShapeSyncDatabaseRegistry.ShapeEntryDefinition> parts = shape.Parts ?? Array.Empty<ShapeSyncDatabaseRegistry.ShapeEntryDefinition>();
                if (shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph && tags.Count != 0)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                        id, id, "Tags", "Morph Shape must not declare Tags.");
                else if (shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph
                    && !registry.TryValidateShapeTagsForValidation(tags, out string tagsDiagnostic))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                        id, id, "Tags", tagsDiagnostic);
                var morphTargets = new HashSet<string>(StringComparer.Ordinal);
                foreach (MorphValue morph in morphs)
                    if (!axisNames.Contains(morph.Target) || !morphTargets.Add(morph.Target))
                        Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                            ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                            id, id, morph.Target, "Morph value targets a missing or duplicated Figure axis.");
                if (shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph && parts.Count != 0)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                        ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                        id, id, "Parts", "Morph Shape must not declare Parts entries.");
                else if (shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph && morphs.Count != 0)
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationMissing,
                        ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                        id, id, "Morphs", "Parts Shape must not declare Morph values.");
                if (shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph
                    && !registry.TryValidateShapePartsForGeneration(parts, out string partsDiagnostic))
                    Add(diagnostics, ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing,
                        ShapeSyncDatabaseEntityKind.Shape, ShapeSyncDatabaseRelationKind.ShapeTarget,
                        id, id, "Parts", partsDiagnostic);
            }
        }

        private static void Add(List<ShapeSyncDatabaseDiagnostic> diagnostics,
            ShapeSyncDatabaseDiagnosticCode code, ShapeSyncDatabaseEntityKind entityKind,
            ShapeSyncDatabaseRelationKind relationKind, string entityId, string sourceId,
            string targetId, string detail)
        {
            diagnostics.Add(new ShapeSyncDatabaseDiagnostic(code, entityKind, relationKind, entityId, sourceId, targetId, detail));
        }
    }
}
