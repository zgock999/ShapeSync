// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Exports one Database-owned Base or FBM Figure as a separate Prefab asset.
    /// This deliberately copies the authored Figure only; it does not invoke BCP,
    /// Generate, Figure Builder, or alter the Database/source hierarchy.
    /// </summary>
    internal static class ShapeSyncDatabaseFigureExport
    {
        internal static Func<GameObject, string, GameObject> SavePrefabAsset =
            (contents, path) => PrefabUtility.SaveAsPrefabAsset(contents, path);
        internal static Action SaveAssets = AssetDatabase.SaveAssets;

        internal static bool TryExport(ShapeSyncDatabase database, GameObject databaseFigure, string destinationPath,
            out GameObject exportedPrefab, out string diagnostic)
        {
            exportedPrefab = null;
            diagnostic = null;

            if (!TryValidateRequest(database, databaseFigure, destinationPath, out diagnostic)) return false;

            string databasePath = AssetDatabase.GetAssetPath(database);
            string figureName = databaseFigure.name;
            GameObject databaseContents = null;
            GameObject copy = null;
            bool completed = false;
            try
            {
                databaseContents = PrefabUtility.LoadPrefabContents(databasePath);
                ShapeSyncDatabase contentsDatabase = databaseContents == null ? null : databaseContents.GetComponent<ShapeSyncDatabase>();
                if (!TryResolveOwnedFigureFromContents(contentsDatabase, figureName, out GameObject contentsFigure, out diagnostic)) return false;

                copy = Object.Instantiate(contentsFigure);
                copy.name = contentsFigure.name;
                exportedPrefab = SavePrefabAsset(copy, destinationPath);
                if (exportedPrefab == null)
                {
                    diagnostic = "ShapeSync Figure Export could not save the destination Prefab.";
                    return false;
                }

                SaveAssets();
                completed = true;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Figure Export failed: " + exception.Message;
                return false;
            }
            finally
            {
                if (copy != null) Object.DestroyImmediate(copy);
                if (databaseContents != null) PrefabUtility.UnloadPrefabContents(databaseContents);
                if (!completed)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
                        AssetDatabase.DeleteAsset(destinationPath);
                    exportedPrefab = null;
                }
            }
        }

        private static bool TryValidateRequest(ShapeSyncDatabase database, GameObject databaseFigure, string destinationPath, out string diagnostic)
        {
            diagnostic = null;
            string databasePath = database == null ? null : AssetDatabase.GetAssetPath(database);
            if (database == null || string.IsNullOrWhiteSpace(databasePath) || !databasePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "ShapeSync Figure Export requires a persistent ShapeSync Database Prefab.";
                return false;
            }

            if (!IsDatabaseOwnedBaseOrFbm(database, databaseFigure, out diagnostic)) return false;

            string folder = string.IsNullOrWhiteSpace(destinationPath) ? null : Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(destinationPath) || !destinationPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !destinationPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsValidFolder(folder))
            {
                diagnostic = "ShapeSync Figure Export requires a Prefab destination below an existing Assets folder.";
                return false;
            }

            if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
            {
                diagnostic = "ShapeSync Figure Export cannot overwrite an existing asset.";
                return false;
            }

            return true;
        }

        private static bool TryResolveOwnedFigureFromContents(ShapeSyncDatabase database, string figureName, out GameObject figure, out string diagnostic)
        {
            figure = null;
            diagnostic = null;
            if (database == null || string.IsNullOrWhiteSpace(figureName))
            {
                diagnostic = "ShapeSync Figure Export could not load the Database Prefab contents.";
                return false;
            }

            Transform intermediate = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            GameObject candidate = intermediate == null ? null : intermediate.Find(figureName)?.gameObject;
            if (candidate == null || candidate.transform.parent != intermediate || PrefabUtility.IsPartOfPrefabInstance(candidate))
            {
                diagnostic = "ShapeSync Figure Export could not resolve a direct Database-owned Figure from the Prefab contents.";
                return false;
            }

            ShapeSyncDatabaseRegistry registry = database.Registry;
            if (registry == null)
            {
                diagnostic = "ShapeSync Figure Export requires the fixed Database registry.";
                return false;
            }

            // Registry is a Database sub-asset and can be shared by Prefab contents.
            // Therefore this path must remain pure as well: resolving validators would
            // rebind serialized references on that shared Registry instance.
            if (IsRegisteredBaseOrFbmByStableName(registry, candidate.name))
            {
                figure = candidate;
                return true;
            }

            diagnostic = "ShapeSync Figure Export could not resolve a registered Base Figure or FBM Figure from the Prefab contents.";
            return false;
        }

        private static bool IsDatabaseOwnedBaseOrFbm(ShapeSyncDatabase database, GameObject databaseFigure, out string diagnostic)
        {
            diagnostic = null;
            if (databaseFigure == null)
            {
                diagnostic = "ShapeSync Figure Export requires a Database Figure.";
                return false;
            }

            Transform intermediate = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            if (intermediate == null || databaseFigure.transform.parent != intermediate)
            {
                diagnostic = "ShapeSync Figure Export requires a direct Database Intermediate Figure.";
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(databaseFigure))
            {
                diagnostic = "ShapeSync Figure Export requires a Figure owned directly by the Database Prefab, not a nested external Prefab instance.";
                return false;
            }

            if (!string.Equals(AssetDatabase.GetAssetPath(databaseFigure), AssetDatabase.GetAssetPath(database), StringComparison.Ordinal))
            {
                diagnostic = "ShapeSync Figure Export requires a Figure stored in the same Database Prefab asset.";
                return false;
            }

            ShapeSyncDatabaseRegistry registry = database.Registry;
            if (registry == null)
            {
                diagnostic = "ShapeSync Figure Export requires the fixed Database registry.";
                return false;
            }

            // Do not call the Registry's resolving validators here.  They repair stale
            // serialized references by rebinding them, which is valid for temporary
            // Prefab contents below but would mutate the caller's Database instance.
            if (IsRegisteredBaseOrFbmByStableName(registry, databaseFigure.name)) return true;

            diagnostic = "ShapeSync Figure Export accepts only the registered Base Figure or an FBM Figure.";
            return false;
        }

        private static bool IsRegisteredBaseOrFbmByStableName(ShapeSyncDatabaseRegistry registry, string figureName)
        {
            if (registry == null || string.IsNullOrWhiteSpace(figureName)) return false;

            if (registry.BaseFigures.Count != 1) return false;
            ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure = registry.BaseFigures[0];
            if (baseFigure == null || string.IsNullOrWhiteSpace(baseFigure.Name)) return false;
            if (string.Equals(baseFigure.Name, figureName, StringComparison.Ordinal)) return true;

            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes)
            {
                if (axis == null || axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    || !string.Equals(axis.Name, figureName, StringComparison.Ordinal)) continue;
                if (axis.Figures == null || axis.Figures.Count != 1) return false;
                ShapeSyncDatabaseRegistry.AxisFigureEntry binding = axis.Figures[0];
                return binding != null && string.Equals(binding.FbmName, axis.Name, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
