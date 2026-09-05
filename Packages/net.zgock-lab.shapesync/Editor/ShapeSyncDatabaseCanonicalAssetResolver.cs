// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Linq;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Resolves the already-admitted Figure / Mesh Outfit Base owner used by an
    /// optional integration.  This is an Editor-only bridge over Core authoring
    /// data; it does not inspect asset names or search the AssetDatabase.
    /// </summary>
    public static class ShapeSyncDatabaseCanonicalAssetResolver
    {
        /// <summary>Resolves the explicit Figure Base or FBM owner for one relation.</summary>
        /// <param name="database">The opened Database containing the canonical Figure hierarchy.</param>
        /// <param name="figureName">The logical Figure identity to resolve.</param>
        /// <param name="shapeKey">The Base shape key or FBM identity to resolve.</param>
        /// <param name="owner">Receives the canonical Figure or FBM owner when resolution succeeds.</param>
        /// <param name="diagnostic">Receives a stable diagnostic when resolution fails.</param>
        /// <returns><see langword="true"/> when the explicit canonical owner is valid and resolved; otherwise, <see langword="false"/>.</returns>
        public static bool TryResolveFigureOwner(ShapeSyncDatabase database, string figureName, string shapeKey,
            out GameObject owner, out string diagnostic)
        {
            owner = null;
            diagnostic = null;
            if (database == null || database.Registry == null)
            {
                diagnostic = "Canonical Figure owner resolution requires an opened Database.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(figureName) || string.IsNullOrWhiteSpace(shapeKey))
            {
                diagnostic = "Canonical Figure owner resolution requires Figure and shape identities.";
                return false;
            }
            if (!database.Registry.TryGetSingleBaseFigureForOpen(database,
                out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out diagnostic)) return false;
            if (!database.Registry.TryValidateFigureAxisState(database, out diagnostic)) return false;
            if (baseFigure == null || !string.Equals(baseFigure.Name, figureName, StringComparison.Ordinal))
            {
                diagnostic = "Canonical Figure owner was not found: " + figureName;
                return false;
            }

            if (string.Equals(shapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal))
            {
                owner = baseFigure.Figure;
            }
            else
            {
                ShapeSyncDatabaseRegistry.FigureAxisEntry axis = database.Registry.FigureAxes
                    .FirstOrDefault(value => value != null
                        && value.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                        && string.Equals(value.Name, shapeKey, StringComparison.Ordinal));
                owner = axis?.Figures?.FirstOrDefault(value => value != null
                    && string.Equals(value.FbmName, shapeKey, StringComparison.Ordinal))?.Figure;
            }

            if (!IsDirectIntermediateChild(database, owner))
            {
                owner = null;
                diagnostic = "Canonical Figure owner must be a direct Database Intermediate child: " + shapeKey;
                return false;
            }
            return true;
        }

        /// <summary>Resolves the explicit Mesh Outfit Base owner for one relation.</summary>
        /// <param name="database">The opened Database containing the canonical Mesh Outfit hierarchy.</param>
        /// <param name="outfitIdentity">The logical Mesh Outfit identity to resolve.</param>
        /// <param name="owner">Receives the canonical Mesh Outfit Base owner when resolution succeeds.</param>
        /// <param name="diagnostic">Receives a stable diagnostic when resolution fails.</param>
        /// <returns><see langword="true"/> when the explicit canonical owner is valid and resolved; otherwise, <see langword="false"/>.</returns>
        public static bool TryResolveMeshOutfitOwner(ShapeSyncDatabase database, string outfitIdentity,
            out GameObject owner, out string diagnostic)
        {
            owner = null;
            diagnostic = null;
            if (database == null || database.Registry == null)
            {
                diagnostic = "Canonical Mesh Outfit owner resolution requires an opened Database.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(outfitIdentity))
            {
                diagnostic = "Canonical Mesh Outfit owner resolution requires an Outfit identity.";
                return false;
            }

            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits
                .FirstOrDefault(value => value != null
                    && value.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh
                    && string.Equals(value.Identity, outfitIdentity, StringComparison.Ordinal));
            if (outfit != null && !database.Registry.TrySetOutfitAxisFigures(database, outfitIdentity, outfit.AxisFigures,
                out diagnostic)) return false;
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit?.AxisFigures
                ?.FirstOrDefault(value => value != null
                    && string.Equals(value.ShapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal));
            owner = baseAxis?.OutfitPrefab;
            if (!IsDirectIntermediateChild(database, owner))
            {
                owner = null;
                diagnostic = "Canonical Mesh Outfit Base owner was not found: " + outfitIdentity;
                return false;
            }
            return true;
        }

        private static bool IsDirectIntermediateChild(ShapeSyncDatabase database, GameObject candidate)
        {
            Transform intermediate = database == null
                ? null
                : database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            return candidate != null && intermediate != null && candidate.transform.parent == intermediate;
        }
    }
}
