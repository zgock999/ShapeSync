// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Owns the authoring-only Collection declaration and Database-local Collection Prefabs.</summary>
    internal static class ShapeSyncMeshOutfitCollectionAuthoring
    {
        internal readonly struct Source
        {
            internal string ShapeKey { get; }
            internal GameObject Prefab { get; }
            internal Source(string shapeKey, GameObject prefab) { ShapeKey = shapeKey; Prefab = prefab; }
        }

        internal static bool TrySave(string databaseAssetPath, string outfitIdentity,
            ShapeSyncDatabaseRegistry.OutfitCollectionKind kind, bool useProjectionForFullCollection,
            IReadOnlyList<Source> sources, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath) || string.IsNullOrWhiteSpace(outfitIdentity))
            { diagnostic = "Collection save requires a Database path and Outfit Id."; return false; }
            if (!Enum.IsDefined(typeof(ShapeSyncDatabaseRegistry.OutfitCollectionKind), kind))
            { diagnostic = "Collection kind is invalid."; return false; }
            if (kind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.None)
            {
                if ((sources?.Count ?? 0) != 0 || useProjectionForFullCollection)
                { diagnostic = "No Collection cannot have sources or Projection selection."; return false; }
            }
            else if (sources == null || sources.Any(source => source.Prefab == null || string.IsNullOrWhiteSpace(source.ShapeKey)
                || PrefabUtility.GetPrefabAssetType(source.Prefab) == PrefabAssetType.NotAPrefab
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(source.Prefab))))
            { diagnostic = "Collection requires a persistent Prefab for Base and every FBM."; return false; }

            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (database, intermediate, transaction) =>
            {
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits
                    .FirstOrDefault(entry => entry != null && string.Equals(entry.Identity, outfitIdentity, StringComparison.Ordinal));
                if (outfit == null || outfit.Kind != ShapeSyncDatabaseRegistry.OutfitKind.Mesh)
                    throw new InvalidOperationException("Mesh Outfit was not found: " + outfitIdentity);

                RemoveOldCollections(database, outfit, databaseAssetPath, transaction);
                if (kind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.None)
                {
                    if (!database.Registry.TrySetOutfitCollection(database, outfitIdentity, kind, false, Array.Empty<ShapeSyncDatabaseRegistry.OutfitCollectionEntry>(), out string clearDiagnostic))
                        throw new InvalidOperationException(clearDiagnostic);
                    return;
                }

                var entries = new List<ShapeSyncDatabaseRegistry.OutfitCollectionEntry>();
                foreach (Source source in sources)
                {
                    string prefix = outfitIdentity + "_" + source.ShapeKey + "_Collection";
                    // Registry input must itself be Database-owned: keeping the selected
                    // project Prefab here would violate Database self-containment.
                    GameObject sourceCopy = CreateDatabaseCollectionPrefab(source.Prefab, prefix + "_Source", intermediate, transaction);
                    GameObject copy = CreateDatabaseCollectionPrefab(sourceCopy, prefix, intermediate, transaction);
                    entries.Add(new ShapeSyncDatabaseRegistry.OutfitCollectionEntry(source.ShapeKey, sourceCopy, copy));
                }
                if (!database.Registry.TrySetOutfitCollection(database, outfitIdentity, kind, useProjectionForFullCollection, entries, out string saveDiagnostic))
                    throw new InvalidOperationException(saveDiagnostic);
            }, out diagnostic);
        }

        private static GameObject CreateDatabaseCollectionPrefab(GameObject source, string name, Transform intermediate, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            GameObject copy = UnityEngine.Object.Instantiate(source);
            copy.name = name;
            copy.transform.SetParent(intermediate, false);
            // Collection is a shape-reference artifact.  It shares the Figure's bone
            // contract and never owns an Animator / Avatar; retaining either would
            // retain an external source-Avatar reference in the Database Prefab.
            foreach (Animator animator in copy.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (SkinnedMeshRenderer renderer in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh != null)
                {
                    // Instantiate retains importer/prefab-object bookkeeping for some embedded
                    // meshes.  A fresh Mesh plus serialized copy is required so this direct
                    // Database child owns its Mesh sub-asset after Prefab save/reopen.
                    Mesh mesh = new Mesh();
                    EditorUtility.CopySerialized(renderer.sharedMesh, mesh);
                    mesh.name = name + "_" + renderer.gameObject.name + "_Mesh";
                    transaction.AddSubAsset(mesh);
                    renderer.sharedMesh = mesh;
                    EditorUtility.SetDirty(mesh);
                }
                renderer.sharedMaterials = Array.Empty<Material>();
                EditorUtility.SetDirty(renderer);
            }
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static void RemoveOldCollections(ShapeSyncDatabase database, ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            string databaseAssetPath, ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            var prefabs = new HashSet<GameObject>();
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry entry in outfit.CollectionEntries)
            {
                GameObject source = ResolveDirectIntermediateChild(database, entry?.SourcePrefab);
                GameObject prefab = ResolveDirectIntermediateChild(database, entry?.CollectionPrefab);
                if (source != null) prefabs.Add(source);
                if (prefab != null) prefabs.Add(prefab);
            }
            foreach (GameObject prefab in prefabs)
            {
                foreach (Mesh mesh in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(renderer => renderer.sharedMesh)
                    .Where(mesh => mesh != null && AssetDatabase.GetAssetPath(mesh) == databaseAssetPath).Distinct()) transaction.RemoveSubAsset(mesh);
                transaction.RemoveSubAsset(prefab);
            }
        }

        /// <summary>Invalidates Collection declarations when the Figure Base/FBM shape-key set changes.</summary>
        internal static void ClearAllForFigureAxisChange(ShapeSyncDatabase database, string databaseAssetPath,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            if (database?.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in database.Registry.Outfits
                .Where(entry => entry != null && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh))
            {
                RemoveOldCollections(database, outfit, databaseAssetPath, transaction);
                outfit.SetCollection(ShapeSyncDatabaseRegistry.OutfitCollectionKind.None, false, null);
            }
        }

        /// <summary>Reclaims Collection artifacts before removing their owning Outfit entity.</summary>
        internal static void ClearForOutfitRemoval(ShapeSyncDatabase database, string identity, string databaseAssetPath,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database?.Registry?.Outfits
                .FirstOrDefault(entry => entry != null && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && entry.Identity == identity);
            if (outfit == null) return;
            RemoveOldCollections(database, outfit, databaseAssetPath, transaction);
            outfit.SetCollection(ShapeSyncDatabaseRegistry.OutfitCollectionKind.None, false, null);
        }

        private static GameObject ResolveDirectIntermediateChild(ShapeSyncDatabase database, GameObject candidate)
        {
            if (database == null || candidate == null) return null;
            Transform intermediate = database.transform.Find("Intermediate");
            if (intermediate == null) return null;
            Transform direct = intermediate.Find(candidate.name);
            return direct == null || direct.parent != intermediate ? null : direct.gameObject;
        }
    }
}
#endif
