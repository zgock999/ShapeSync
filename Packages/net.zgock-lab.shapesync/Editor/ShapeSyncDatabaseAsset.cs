// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using System;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Typed asset access for the root component of a ShapeSync Database Prefab.</summary>
    public static class ShapeSyncDatabaseAsset
    {
        /// <summary>Name of the child container reserved for registered intermediate Humanoids.</summary>
        public const string IntermediateContainerName = "Intermediate";

        /// <summary>Creates an empty Database Prefab in an existing folder below <c>Assets</c>.</summary>
        /// <remarks>This is an authoring-only operation. It creates a new asset and never changes source assets.</remarks>
        public static bool TryCreate(string folderPath, out ShapeSyncDatabase database, out string diagnostic)
        {
            database = null;
            diagnostic = null;

            if (string.IsNullOrWhiteSpace(folderPath) || !folderPath.StartsWith("Assets", System.StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(folderPath))
            {
                diagnostic = "ShapeSync Database creation requires an existing folder below Assets.";
                return false;
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folderPath + "/ShapeSyncDatabase.prefab");
            return TryCreateAtPath(assetPath, out database, out diagnostic);
        }

        /// <summary>Creates an empty Database Prefab at an unused, explicit path below <c>Assets</c>.</summary>
        public static bool TryCreateAtPath(string assetPath, out ShapeSyncDatabase database, out string diagnostic)
        {
            database = null;
            diagnostic = null;
            string folderPath = string.IsNullOrEmpty(assetPath) ? null : System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", System.StringComparison.Ordinal) || !assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsValidFolder(folderPath))
            {
                diagnostic = "ShapeSync Database creation requires a Prefab path below an existing Assets folder.";
                return false;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                diagnostic = "ShapeSync Database creation cannot overwrite an existing asset.";
                return false;
            }

            GameObject root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetPath));
            ShapeSyncDatabaseRegistry registry = null;
            try
            {
                ShapeSyncDatabase databaseRoot = root.AddComponent<ShapeSyncDatabase>();
                GameObject intermediate = new GameObject(IntermediateContainerName);
                intermediate.transform.SetParent(root.transform, false);

                if (PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
                {
                    diagnostic = "ShapeSync Database Prefab could not be created.";
                    return false;
                }

                registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
                registry.name = "ShapeSyncDatabaseRegistry";
                AssetDatabase.AddObjectToAsset(registry, assetPath);
                databaseRoot.SetRegistryForAuthoring(registry);
                if (PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
                {
                    diagnostic = "ShapeSync Database registry could not be recorded.";
                    return false;
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return TryLoad(assetPath, out database, out diagnostic);
        }

        /// <summary>Opens a Database through the same typed validation used by all later editor steps.</summary>
        public static bool TryOpen(string assetPath, out ShapeSyncDatabase database, out string diagnostic)
        {
            if (!TryLoad(assetPath, out database, out diagnostic)) return false;

            if (database.transform.Find(IntermediateContainerName) == null)
            {
                database = null;
                diagnostic = "ShapeSync Database is missing its Intermediate root container.";
                return false;
            }

            if (!TryValidateOptionalFeatureAdmission(assetPath, out diagnostic))
            {
                database = null;
                return false;
            }

            if (!TryMigrateExternalOwnedAssets(assetPath, out diagnostic) || !TryLoad(assetPath, out database, out diagnostic))
            {
                database = null;
                return false;
            }

            ShapeSyncDatabaseRegistry registry = database.Registry;
            int registryCount = 0;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath)) if (asset is ShapeSyncDatabaseRegistry) registryCount++;
            if (registry != null && registryCount == 1 && AssetDatabase.GetAssetPath(registry) == assetPath)
            {
                if (registry.TryValidateFigureAxisState(database, out diagnostic)
                    && TryValidateAxisFigurePayloads(registry, out diagnostic)
                    // Opening is an editing operation.  Material relation defects are
                    // reported by the validator/Generate preflight, not used to make the
                    // entire Database inaccessible.
                    && registry.TryValidateFigureMorphAuthoringForOpen(database, out diagnostic)
                    && registry.TryValidateNormalEntries(out diagnostic)
                    && TryValidateNormalReferences(registry, out diagnostic)) return true;
                database = null;
                return false;
            }

            database = null;
            diagnostic = "ShapeSync Database requires exactly one fixed authoring registry.";
            return false;
        }

        private static bool TryValidateOptionalFeatureAdmission(string assetPath, out string diagnostic)
        {
            diagnostic = null;
            Object[] localAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int index = 0; index < localAssets.Length; index++)
            {
                if (!(localAssets[index] is ShapeSyncDatabaseOptionalFeatureMarker marker)
                    || !string.Equals(marker.FeatureId, "VRM", StringComparison.Ordinal)) continue;

                if (!ShapeSyncDatabaseOptionalRegistryProvider.TryValidateVrm(assetPath, out diagnostic)) return false;
            }

            return true;
        }

        /// <summary>
        /// A Unity duplicate can retain references to sub-assets of its source Prefab.
        /// On first open, rebind Database-owned payloads to the corresponding sub-assets
        /// already contained by the duplicate; no new payload assets are created.
        /// </summary>
        private static bool TryMigrateExternalOwnedAssets(string assetPath, out string diagnostic)
        {
            diagnostic = null;
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(assetPath);
                ShapeSyncDatabase root = contents == null ? null : contents.GetComponent<ShapeSyncDatabase>();
                if (root == null) { diagnostic = "ShapeSync Database Prefab contents could not be loaded for migration."; return false; }

                Object[] localAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                bool changed = false;
                if (root.Registry == null || AssetDatabase.GetAssetPath(root.Registry) != assetPath)
                {
                    ShapeSyncDatabaseRegistry localRegistry = localAssets.OfType<ShapeSyncDatabaseRegistry>().SingleOrDefault();
                    if (localRegistry == null) { diagnostic = "Duplicate Database is missing its local Registry sub-asset."; return false; }
                    root.SetRegistryForAuthoring(localRegistry);
                    changed = true;
                }

                if (!TryRebindDatabaseOwnedPayloads(contents, root, assetPath, localAssets, out bool payloadsChanged, out diagnostic)) return false;
                changed |= payloadsChanged;

                if (!changed) return true;
                EditorUtility.SetDirty(root.Registry);
                if (PrefabUtility.SaveAsPrefabAsset(contents, assetPath) == null)
                { diagnostic = "ShapeSync Database external-reference migration could not be saved."; return false; }
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Database external-reference migration failed: " + exception.Message;
                return false;
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // Mesh / Material / Texture are Database-owned payloads and must be rebound
        // when a Database Prefab is duplicated.  Avatar is intentionally excluded:
        // Outfit authoring preserves the source Animator/Avatar reference, while
        // Figure import creates its own local Avatar sub-asset.  Treating every
        // Avatar reference as Database-owned made an unrelated Outfit registration
        // fail with a misleading "missing a local counterpart" diagnostic when the
        // duplicated Database had no same-named Avatar sub-asset.
        private static bool IsDatabaseOwnedPayload(Object value) => value is Mesh || value is Material || value is Texture;

        /// <summary>
        /// Rebinds Database-owned Mesh/Material/Texture references in a duplicated
        /// Prefab before it is saved.  Unity may leave a duplicate pointing at the
        /// source Prefab's local sub-assets; saving without this pass drops those
        /// references and serializes them as fileID 0.
        /// </summary>
        internal static bool TryRebindDatabaseOwnedPayloads(GameObject contents, ShapeSyncDatabase root,
            string assetPath, Object[] localAssets, out bool changed, out string diagnostic)
        {
            changed = false;
            diagnostic = null;
            if (contents == null || root == null || root.Registry == null)
            {
                diagnostic = "Duplicate Database payload rebind requires Prefab contents and a Registry.";
                return false;
            }

            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in root.Registry.TextureResources)
                if (resource != null && TryRebindLocalTexture(resource.Texture, assetPath, localAssets, out Texture local))
                { resource.SetTexture(local); changed = true; }
            foreach (ShapeSyncDatabaseRegistry.NormalEntry normal in root.Registry.NormalEntries)
                if (normal != null && TryRebindLocalTexture(normal.Texture, assetPath, localAssets, out Texture local))
                { normal.SetTexture(local); changed = true; }

            var targets = contents.GetComponentsInChildren<Component>(true).Cast<Object>()
                .Concat(localAssets.Where(asset => asset is ShapeSyncDatabaseRegistry || asset is Material))
                .Where(asset => asset != null).Distinct().ToArray();
            foreach (Object target in targets)
            {
                SerializedObject serialized = new SerializedObject(target);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                bool targetChanged = false;
                while (property.Next(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object reference = property.objectReferenceValue;
                    if (!IsDatabaseOwnedPayload(reference) || AssetDatabase.GetAssetPath(reference) == assetPath) continue;
                    Object local = FindLocalPayload(reference, localAssets);
                    if (local == null)
                    { diagnostic = "Duplicate Database is missing a local counterpart for: " + reference.name; return false; }
                    property.objectReferenceValue = local;
                    targetChanged = changed = true;
                }
                if (targetChanged) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return true;
        }

        private static bool TryRebindLocalTexture(Texture source, string assetPath, Object[] localAssets, out Texture local)
        {
            local = source;
            if (source == null || AssetDatabase.GetAssetPath(source) == assetPath) return false;
            local = FindLocalPayload(source, localAssets) as Texture;
            if (local == null) throw new InvalidOperationException("Duplicate Database is missing a local counterpart for: " + source.name);
            return true;
        }

        private static Object FindLocalPayload(Object external, IEnumerable<Object> localAssets) => localAssets.FirstOrDefault(asset => asset != null && asset.GetType() == external.GetType() && asset.name == external.name);

        /// <summary>Ensures persisted axis bindings still carry the common 20.3 imported-Figure payload.</summary>
        private static bool TryValidateAxisFigurePayloads(ShapeSyncDatabaseRegistry registry, out string diagnostic)
        {
            diagnostic = null;
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes)
            {
                foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry binding in axis.Figures)
                {
                    GameObject figure = binding.Figure;
                    ShapeSyncFigureImportRecord record = figure == null ? null : figure.GetComponent<ShapeSyncFigureImportRecord>();
                    SkinnedMeshRenderer[] renderers = figure == null ? null : figure.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (record == null || renderers == null || renderers.Length != 1 || renderers[0].sharedMesh == null)
                    {
                        diagnostic = "Figure axis binding does not contain a valid imported merged Figure payload.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool TryValidateNormalReferences(ShapeSyncDatabaseRegistry registry, out string diagnostic)
        {
            diagnostic = null;
            foreach (ShapeSyncDatabaseRegistry.NormalEntry entry in registry.NormalEntries)
            {
                if (entry == null || entry.Texture == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(entry.Texture)))
                {
                    diagnostic = "Normal Texture must reference a persistent Texture asset.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Loads the root <see cref="ShapeSyncDatabase"/> component from a persistent Database Prefab.
        /// </summary>
        /// <remarks>
        /// This access path is authoring-only and does not instantiate, modify, or generate a runtime Figure.
        /// </remarks>
        public static bool TryLoad(string assetPath, out ShapeSyncDatabase database, out string diagnostic)
        {
            database = null;
            diagnostic = null;

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                diagnostic = "ShapeSync Database load requires an asset path.";
                return false;
            }

            if (!assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "ShapeSync Database must be stored as a Prefab asset.";
                return false;
            }

            database = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(assetPath);
            if (database == null)
            {
                diagnostic = "Prefab root does not contain a ShapeSyncDatabase component.";
                return false;
            }

            GameObject mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
            if (!TryValidateRootComponent(mainAsset, database, out diagnostic))
            {
                database = null;
                return false;
            }

            return true;
        }

        private static bool TryValidateRootComponent(GameObject mainAsset, ShapeSyncDatabase database, out string diagnostic)
        {
            if (mainAsset != null && mainAsset.GetComponent<ShapeSyncDatabase>() == database)
            {
                diagnostic = null;
                return true;
            }

            diagnostic = "ShapeSyncDatabase must be attached to the Prefab main-asset root.";
            return false;
        }
    }
}
