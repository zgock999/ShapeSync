// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Stable authoring validation code emitted by the Database validator.</summary>
    public enum ShapeSyncDatabaseDiagnosticCode
    {
        /// <summary>No diagnostic was emitted.</summary>
        None = 0,
        /// <summary>The supplied Database root is missing or invalid.</summary>
        DatabaseRequired = 1,
        /// <summary>The Database registry is missing or invalid.</summary>
        RegistryRequired = 2,
        /// <summary>A Database-owned entity is structurally invalid.</summary>
        EntityInvalid = 3,
        /// <summary>More than one entity claims an identity that must be unique.</summary>
        EntityDuplicate = 4,
        /// <summary>A required relation is absent.</summary>
        RelationMissing = 5,
        /// <summary>A relation names a target that does not exist.</summary>
        RelationTargetMissing = 6,
        /// <summary>A relation resolves to more than one target.</summary>
        RelationAmbiguous = 7,
        /// <summary>An entity count violates a required cardinality.</summary>
        EntityCardinality = 8,
        /// <summary>An optional Database feature cannot be safely opened in this build.</summary>
        OptionalFeatureUnavailable = 9
    }

    /// <summary>Identifies the Database-owned entity at which an authoring diagnostic is localized.</summary>
    public enum ShapeSyncDatabaseEntityKind
    {
        /// <summary>The Database root.</summary>
        Database = 0,
        /// <summary>The authoring registry.</summary>
        Registry = 1,
        /// <summary>The canonical Base Figure.</summary>
        BaseFigure = 2,
        /// <summary>A Figure deformation axis.</summary>
        FigureAxis = 3,
        /// <summary>An Outfit definition.</summary>
        Outfit = 4,
        /// <summary>A Material Entry definition.</summary>
        MaterialEntry = 5,
        /// <summary>An abstract Texture resource.</summary>
        TextureResource = 6,
        /// <summary>A Normal Entry definition.</summary>
        NormalEntry = 7,
        /// <summary>A Shape definition.</summary>
        Shape = 8,
        /// <summary>A part belonging to a Shape.</summary>
        ShapePart = 9
    }

    /// <summary>Identifies the declared relation that is invalid or incomplete.</summary>
    public enum ShapeSyncDatabaseRelationKind
    {
        /// <summary>No relation is involved.</summary>
        None = 0,
        /// <summary>The Database-to-registry relation.</summary>
        Registry = 1,
        /// <summary>The registry-to-Base-Figure relation.</summary>
        BaseFigure = 2,
        /// <summary>The registry-to-Figure-axis relation.</summary>
        FigureAxis = 3,
        /// <summary>The axis-to-Figure relation.</summary>
        AxisFigure = 4,
        /// <summary>A Shape part's Material target relation.</summary>
        MaterialTarget = 5,
        /// <summary>A Texture resource relation.</summary>
        TextureResource = 6,
        /// <summary>A Normal Entry target relation.</summary>
        NormalTarget = 7,
        /// <summary>A Shape part target relation.</summary>
        ShapeTarget = 8
    }

    /// <summary>
    /// Structured, authoring-only Database diagnostic.  It is deliberately independent of
    /// UnityEditor so the same admission seam can be used by Registry and Editor validation.
    /// </summary>
    [Serializable]
    public sealed class ShapeSyncDatabaseDiagnostic
    {
        /// <summary>Gets the stable validation code.</summary>
        public ShapeSyncDatabaseDiagnosticCode Code { get; }
        /// <summary>Gets the Database entity kind at which the diagnostic is localized.</summary>
        public ShapeSyncDatabaseEntityKind EntityKind { get; }
        /// <summary>Gets the relation kind involved in the diagnostic.</summary>
        public ShapeSyncDatabaseRelationKind RelationKind { get; }
        /// <summary>Gets the logical identity of the affected entity, when available.</summary>
        public string EntityId { get; }
        /// <summary>Gets the logical identity from which the invalid relation originates.</summary>
        public string SourceId { get; }
        /// <summary>Gets the logical identity that the relation attempted to resolve.</summary>
        public string TargetId { get; }
        /// <summary>Gets the human-readable detail associated with the diagnostic.</summary>
        public string Detail { get; }

        /// <summary>Creates a structured Database validation diagnostic.</summary>
        /// <param name="code">Stable validation code.</param>
        /// <param name="entityKind">Entity kind at which the diagnostic is localized.</param>
        /// <param name="relationKind">Relation kind involved in the diagnostic.</param>
        /// <param name="entityId">Affected entity identity, when available.</param>
        /// <param name="sourceId">Originating relation identity, when available.</param>
        /// <param name="targetId">Target relation identity, when available.</param>
        /// <param name="detail">Human-readable diagnostic detail.</param>
        public ShapeSyncDatabaseDiagnostic(ShapeSyncDatabaseDiagnosticCode code,
            ShapeSyncDatabaseEntityKind entityKind, ShapeSyncDatabaseRelationKind relationKind,
            string entityId, string sourceId, string targetId, string detail)
        {
            Code = code;
            EntityKind = entityKind;
            RelationKind = relationKind;
            EntityId = entityId;
            SourceId = sourceId;
            TargetId = targetId;
            Detail = detail;
        }

        /// <summary>Converts the authoring diagnostic to the common structured failure envelope.</summary>
        public StackMachineDiagnostic ToStackMachineDiagnostic()
        {
            string binding = string.IsNullOrEmpty(EntityId) ? SourceId : EntityId;
            string relation = RelationKind == ShapeSyncDatabaseRelationKind.None ? string.Empty : "; relation=" + RelationKind;
            string target = string.IsNullOrEmpty(TargetId) ? string.Empty : "; target=" + TargetId;
            string message = string.IsNullOrEmpty(Detail) ? Code.ToString() : Detail;
            return StackMachineDiagnostic.CreateDomain("database", Code.ToString(), message,
                bindingName: binding, detail: "entity=" + EntityKind + relation + target);
        }

        /// <summary>Formats the diagnostic as a stable, inspectable one-line record.</summary>
        public override string ToString()
        {
            return Code + ": entity=" + EntityKind + ":" + (EntityId ?? string.Empty)
                + "; relation=" + RelationKind + "; source=" + (SourceId ?? string.Empty)
                + "; target=" + (TargetId ?? string.Empty) + "; " + (Detail ?? string.Empty);
        }
    }

    /// <summary>Shared, side-effect-free admission predicates used by Registry and whole-Database validation.</summary>
    internal static class ShapeSyncDatabaseAdmission
    {
        internal static bool TryValidateBaseFigureCardinality(
            System.Collections.Generic.IReadOnlyList<ShapeSyncDatabaseRegistry.BaseFigureEntry> entries,
            out ShapeSyncDatabaseDiagnostic diagnostic)
        {
            diagnostic = null;
            if (entries == null || entries.Count <= 1) return true;
            diagnostic = new ShapeSyncDatabaseDiagnostic(
                ShapeSyncDatabaseDiagnosticCode.EntityCardinality,
                ShapeSyncDatabaseEntityKind.BaseFigure,
                ShapeSyncDatabaseRelationKind.BaseFigure,
                "Base", "Database", null,
                "A Database may contain at most one Base Figure.");
            return false;
        }

        internal static bool TryValidateAdditionalBaseFigure(
            System.Collections.Generic.IReadOnlyList<ShapeSyncDatabaseRegistry.BaseFigureEntry> entries,
            string name, UnityEngine.GameObject figure, out ShapeSyncDatabaseDiagnostic diagnostic)
        {
            diagnostic = null;
            if (entries != null && entries.Count != 0)
            {
                diagnostic = new ShapeSyncDatabaseDiagnostic(
                    ShapeSyncDatabaseDiagnosticCode.EntityCardinality,
                    ShapeSyncDatabaseEntityKind.BaseFigure,
                    ShapeSyncDatabaseRelationKind.BaseFigure,
                    name, "Database", figure == null ? null : figure.name,
                    "A Database may contain at most one Base Figure.");
                return false;
            }
            return true;
        }
    }
}
