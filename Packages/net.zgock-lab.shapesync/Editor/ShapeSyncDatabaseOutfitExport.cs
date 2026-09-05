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
    /// Exports one Database-owned Outfit Prefab as an independent Prefab asset.
    /// This is deliberately separate from Figure Export: it copies only the
    /// authored Outfit hierarchy and does not invoke Generate or alter the Database.
    /// </summary>
    internal static class ShapeSyncDatabaseOutfitExport
    {
        internal static Func<GameObject, string, GameObject> SavePrefabAsset =
            (contents, path) => PrefabUtility.SaveAsPrefabAsset(contents, path);
        internal static Action SaveAssets = AssetDatabase.SaveAssets;

        internal static bool TryExport(ShapeSyncDatabase database, GameObject databaseOutfit,
            string destinationPath, out GameObject exportedPrefab, out string diagnostic)
        {
            exportedPrefab = null;
            diagnostic = null;
            if (!TryValidateRequest(database, databaseOutfit, destinationPath, out diagnostic)) return false;

            string databasePath = AssetDatabase.GetAssetPath(database);
            GameObject databaseContents = null;
            GameObject copy = null;
            bool completed = false;
            try
            {
                databaseContents = PrefabUtility.LoadPrefabContents(databasePath);
                ShapeSyncDatabase contentsDatabase = databaseContents == null
                    ? null
                    : databaseContents.GetComponent<ShapeSyncDatabase>();
                Transform intermediate = contentsDatabase == null
                    ? null
                    : contentsDatabase.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
                GameObject contentsOutfit = intermediate == null
                    ? null
                    : intermediate.Find(databaseOutfit.name)?.gameObject;
                if (contentsOutfit == null || contentsOutfit.transform.parent != intermediate
                    || PrefabUtility.IsPartOfPrefabInstance(contentsOutfit))
                {
                    diagnostic = "ShapeSync Outfit Export could not resolve a direct Database-owned Outfit Prefab.";
                    return false;
                }

                copy = Object.Instantiate(contentsOutfit);
                copy.name = contentsOutfit.name;
                exportedPrefab = SavePrefabAsset(copy, destinationPath);
                if (exportedPrefab == null)
                {
                    diagnostic = "ShapeSync Outfit Export could not save the destination Prefab.";
                    return false;
                }

                SaveAssets();
                completed = true;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Outfit Export failed: " + exception.Message;
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

        private static bool TryValidateRequest(ShapeSyncDatabase database, GameObject databaseOutfit,
            string destinationPath, out string diagnostic)
        {
            diagnostic = null;
            string databasePath = database == null ? null : AssetDatabase.GetAssetPath(database);
            if (database == null || string.IsNullOrWhiteSpace(databasePath)
                || !databasePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "ShapeSync Outfit Export requires a persistent ShapeSync Database Prefab.";
                return false;
            }

            Transform intermediate = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            if (databaseOutfit == null || intermediate == null || databaseOutfit.transform.parent != intermediate)
            {
                diagnostic = "ShapeSync Outfit Export requires a direct Database Intermediate Outfit Prefab.";
                return false;
            }
            if (PrefabUtility.IsPartOfPrefabInstance(databaseOutfit)
                || !string.Equals(AssetDatabase.GetAssetPath(databaseOutfit), databasePath, StringComparison.Ordinal))
            {
                diagnostic = "ShapeSync Outfit Export requires an Outfit Prefab stored in the same Database asset.";
                return false;
            }

            string folder = string.IsNullOrWhiteSpace(destinationPath)
                ? null
                : Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(destinationPath) || !destinationPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !destinationPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.IsValidFolder(folder))
            {
                diagnostic = "ShapeSync Outfit Export requires a Prefab destination below an existing Assets folder.";
                return false;
            }
            if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
            {
                diagnostic = "ShapeSync Outfit Export cannot overwrite an existing asset.";
                return false;
            }
            return true;
        }
    }
}
