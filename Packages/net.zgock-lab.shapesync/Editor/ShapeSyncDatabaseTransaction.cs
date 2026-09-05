// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Performs authoring structural changes through Prefab contents rather than Undo.</summary>
    public static class ShapeSyncDatabaseTransaction
    {
        internal static Action<string, string> CopyAssetFile = (sourcePath, destinationPath) => File.Copy(sourcePath, destinationPath, false);
        internal static Action<string, string> RestoreAssetFile = (sourcePath, destinationPath) => File.Copy(sourcePath, destinationPath, true);
        internal static Action<string> DeleteSnapshotFile = File.Delete;
        internal static Action SaveAssets = AssetDatabase.SaveAssets;
        internal static Action<UnityEngine.Object, string> AddObjectToAsset = AssetDatabase.AddObjectToAsset;
        internal static Action<UnityEngine.Object> RemoveObjectFromAsset = AssetDatabase.RemoveObjectFromAsset;
        internal static Action<GameObject> UnloadPrefabContents = PrefabUtility.UnloadPrefabContents;
        internal static Func<Scene, bool> ClosePreviewScene = EditorSceneManager.ClosePreviewScene;
        internal static Action ReleaseCachedFileHandles = AssetDatabase.ReleaseCachedFileHandles;
        internal static Func<string> SnapshotDirectoryProvider = GetDefaultSnapshotDirectory;
        internal static Func<GameObject, string, bool> SavePrefabAsset = (contents, path) =>
        {
            PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
            return saved;
        };

        /// <summary>
        /// Applies one structural change to the Database's intermediate container and persists it to the same Prefab.
        /// </summary>
        /// <remarks>
        /// The callback must modify only the loaded Prefab contents. This method deliberately does not register Undo;
        /// A file snapshot is retained until the save succeeds so a failed save can be rolled back atomically.
        /// </remarks>
        public static bool TryEditStructure(string assetPath, Action<ShapeSyncDatabase, Transform> edit, out string diagnostic)
        {
            if (edit == null)
            {
                diagnostic = "ShapeSync Database structural edit requires a callback.";
                return false;
            }
            return TryEditStructureWithAssets(assetPath, (database, intermediate, _) => edit(database, intermediate), out diagnostic);
        }

        /// <summary>Applies a structural edit and explicitly stages generated sub-assets in the same snapshot transaction.</summary>
        public static bool TryEditStructureWithAssets(string assetPath, Action<ShapeSyncDatabase, Transform, EditContext> edit, out string diagnostic)
        {
            diagnostic = null;
            if (edit == null)
            {
                diagnostic = "ShapeSync Database structural edit requires a callback.";
                return false;
            }

            if (!ShapeSyncDatabaseAsset.TryOpen(assetPath, out _, out diagnostic)) return false;

            GameObject contents = null;
            Scene contentsScene = default;
            string snapshotPath = null;
            bool snapshotCreated = false;
            bool committed = false;
            bool retainSnapshot = false;
            try
            {
                snapshotPath = CreateSnapshotPath(assetPath);
                CopyAssetFile(assetPath, snapshotPath);
                snapshotCreated = true;

                contents = PrefabUtility.LoadPrefabContents(assetPath);
                if (contents == null)
                {
                    diagnostic = "ShapeSync Database Prefab contents could not be loaded.";
                }
                else
                {
                    contentsScene = contents.scene;
                    ShapeSyncDatabase database = contents.GetComponent<ShapeSyncDatabase>();
                    Transform intermediate = contents.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
                    if (!HasExpectedRootAndContainer(contents, database, intermediate))
                    {
                        diagnostic = "ShapeSync Database Prefab contents do not satisfy the root container contract.";
                    }
                    else
                    {
                        edit(database, intermediate, new EditContext(assetPath));
                        if (!HasExpectedRootAndContainer(contents, database, intermediate))
                        {
                            diagnostic = "ShapeSync Database structural edit must preserve the root container contract.";
                        }
                        else
                        {
                            // The fixed registry is a Prefab sub-asset. Mark it explicitly so
                            // registrations created inside loaded Prefab contents are persisted
                            // together with the structural save.
                            EditorUtility.SetDirty(database.Registry);
                            if (!SavePrefabAsset(contents, assetPath))
                            {
                                diagnostic = "ShapeSync Database structural edit could not be saved.";
                            }
                            else
                            {
                                SaveAssets();
                                committed = true;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostic = snapshotCreated
                    ? "ShapeSync Database structural edit failed: " + exception.Message
                    : "ShapeSync Database snapshot could not be created: " + exception.Message;
            }
            finally
            {
                if (!TryCleanupPrefabContents(contents, contentsScene, out string cleanupDiagnostic))
                {
                    committed = false;
                    diagnostic = AppendDiagnostic(diagnostic, cleanupDiagnostic);
                }

                if (snapshotCreated && !committed && !TryRestoreSnapshot(snapshotPath, assetPath, out string restoreDiagnostic))
                {
                    diagnostic = AppendDiagnostic(diagnostic, restoreDiagnostic);
                    retainSnapshot = true;
                }

                if (snapshotCreated && !retainSnapshot)
                {
                    try
                    {
                        ReleaseCachedFileHandles();
                        DeleteSnapshotFile(snapshotPath);
                    }
                    catch (Exception exception)
                    {
                        retainSnapshot = true;
                        if (committed)
                        {
                            committed = false;
                            if (!TryRestoreSnapshot(snapshotPath, assetPath, out string cleanupRollbackDiagnostic))
                                diagnostic = AppendDiagnostic(diagnostic, cleanupRollbackDiagnostic);
                        }

                        diagnostic = AppendDiagnostic(diagnostic, "ShapeSync Database snapshot cleanup failed; snapshot was retained: " + exception.Message);
                    }
                }
            }

            return committed;
        }

        /// <summary>Provides controlled sub-asset operations during one Database transaction.</summary>
        public sealed class EditContext
        {
            private readonly string assetPath;
            internal EditContext(string assetPath) { this.assetPath = assetPath; }
            /// <summary>Adds a non-null Unity object to the transaction's Database asset.</summary>
            /// <param name="asset">Sub-asset to attach.</param>
            public void AddSubAsset(UnityEngine.Object asset)
            {
                if (asset == null) throw new ArgumentNullException(nameof(asset));
                AddObjectToAsset(asset, assetPath);
            }
            /// <summary>Removes a sub-asset from the transaction's Database asset and destroys the detached asset.</summary>
            /// <param name="asset">Sub-asset to detach; null is ignored.</param>
            public void RemoveSubAsset(UnityEngine.Object asset)
            {
                if (asset == null) return;
                RemoveObjectFromAsset(asset);
                // The object is still reported as persistent during this edit until the
                // enclosing Prefab save completes.  Explicitly allow asset destruction;
                // otherwise Unity emits an error log (and EditMode treats it as a test
                // failure) even though it has already been detached from this Database.
                UnityEngine.Object.DestroyImmediate(asset, true);
            }
        }

        private static bool TryCleanupPrefabContents(GameObject contents, Scene contentsScene, out string diagnostic)
        {
            diagnostic = null;
            if (contents != null)
            {
                try
                {
                    UnloadPrefabContents(contents);
                    diagnostic = null;
                    return true;
                }
                catch (Exception exception)
                {
                    if (TryClosePreviewScene(contentsScene, out string closeDiagnostic))
                    {
                        diagnostic = "ShapeSync Database Prefab contents cleanup failed; preview scene was closed: " + exception.Message;
                    }
                    else
                    {
                        diagnostic = "ShapeSync Database Prefab contents cleanup failed; preview scene close also failed: " + exception.Message + " / " + closeDiagnostic;
                    }

                    return false;
                }
            }

            return !contentsScene.IsValid() || TryClosePreviewScene(contentsScene, out diagnostic);
        }

        private static bool TryClosePreviewScene(Scene contentsScene, out string diagnostic)
        {
            try
            {
                if (!ClosePreviewScene(contentsScene))
                {
                    diagnostic = "ShapeSync Database Prefab preview scene could not be closed.";
                    return false;
                }

                diagnostic = null;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.Message;
                return false;
            }
        }

        private static bool TryRestoreSnapshot(string snapshotPath, string assetPath, out string diagnostic)
        {
            try
            {
                ReleaseCachedFileHandles();
                RestoreAssetFile(snapshotPath, assetPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                diagnostic = null;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Database rollback failed; snapshot was retained: " + exception.Message;
                return false;
            }
        }

        private static string CreateSnapshotPath(string assetPath)
        {
            string directory = SnapshotDirectoryProvider();
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("ShapeSync Database snapshot directory is unavailable.");
            Directory.CreateDirectory(directory);
            string fileName = Path.GetFileName(assetPath) + "." + Guid.NewGuid().ToString("N") + ".snapshot";
            return Path.Combine(directory, fileName);
        }

        private static string GetDefaultSnapshotDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new InvalidOperationException("ShapeSync project root is unavailable.");
            return Path.Combine(projectRoot, "Temp", "ShapeSyncDatabaseSnapshots");
        }

        private static string AppendDiagnostic(string existing, string addition)
        {
            if (string.IsNullOrEmpty(addition)) return existing;
            if (string.IsNullOrEmpty(existing)) return addition;
            return existing + "\n" + addition;
        }

        private static bool HasExpectedRootAndContainer(GameObject contents, ShapeSyncDatabase database, Transform intermediate)
        {
            return contents != null
                && database != null
                && contents.GetComponent<ShapeSyncDatabase>() == database
                && intermediate != null
                && intermediate.parent == contents.transform
                && intermediate.name == ShapeSyncDatabaseAsset.IntermediateContainerName;
        }
    }

    /// <summary>
    /// Persists edits which change only the Database registry and do not add or
    /// remove Prefab hierarchy objects or sub-assets.
    /// </summary>
    internal static class ShapeSyncDatabaseDirectEdit
    {
        internal delegate bool RegistryEdit(ShapeSyncDatabaseRegistry registry, out string diagnostic);

        internal static Action<UnityEngine.Object, string> RecordUndo = Undo.RecordObject;
        internal static Func<GameObject, string, bool> SavePrefabAsset = (root, path) =>
            PrefabUtility.SavePrefabAsset(root) != null;
        internal static Func<ShapeSyncDatabaseRegistry, string> SerializeRegistry = EditorJsonUtility.ToJson;
        internal static Action<ShapeSyncDatabaseRegistry, string> RestoreRegistry = (registry, json) => EditorJsonUtility.FromJsonOverwrite(json, registry);
        internal static Action<string> ReimportAsset = path => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        internal static Action<UnityEngine.Object> ClearDirty = EditorUtility.ClearDirty;

        /// <summary>Applies one registry-only edit without loading Prefab contents.</summary>
        internal static bool TryEdit(ShapeSyncDatabase database, string undoLabel, RegistryEdit edit, out string diagnostic)
        {
            diagnostic = null;
            if (database == null || database.Registry == null)
            {
                diagnostic = "ShapeSync Database direct edit requires an opened Database and Registry.";
                return false;
            }
            if (edit == null)
            {
                diagnostic = "ShapeSync Database direct edit requires a callback.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(database);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                diagnostic = "ShapeSync Database direct edit requires a persistent Database Prefab.";
                return false;
            }

            ShapeSyncDatabaseRegistry registry = database.Registry;
            string preimage;
            try
            {
                preimage = SerializeRegistry(registry);
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Database direct edit could not capture its registry state: " + exception.Message;
                return false;
            }

            bool changed = false;
            try
            {
                RecordUndo(registry, string.IsNullOrWhiteSpace(undoLabel) ? "Edit ShapeSync Database" : undoLabel);
                changed = edit(registry, out diagnostic);
                if (!changed)
                {
                    RestoreAndClear(registry, database, preimage, false, out string rollbackDiagnostic);
                    diagnostic = AppendDiagnostic(diagnostic, rollbackDiagnostic);
                    if (string.IsNullOrWhiteSpace(diagnostic)) diagnostic = "ShapeSync Database direct edit was rejected.";
                    return false;
                }

                if (!SavePrefabAsset(database.gameObject, assetPath))
                {
                    string saveDiagnostic = "ShapeSync Database direct edit could not be saved.";
                    RestoreAndClear(registry, database, preimage, true, out string rollbackDiagnostic);
                    diagnostic = AppendDiagnostic(saveDiagnostic, diagnostic);
                    diagnostic = AppendDiagnostic(diagnostic, rollbackDiagnostic);
                    return false;
                }

                ClearDirty(registry);
                ClearDirty(database.gameObject);
                diagnostic = null;
                return true;
            }
            catch (Exception exception)
            {
                RestoreAndClear(registry, database, preimage, changed, out string rollbackDiagnostic);
                diagnostic = AppendDiagnostic(
                    changed ? "ShapeSync Database direct edit failed: " + exception.Message : exception.Message,
                    diagnostic);
                diagnostic = AppendDiagnostic(diagnostic, rollbackDiagnostic);
                return false;
            }
        }

        private static bool RestoreAndClear(ShapeSyncDatabaseRegistry registry, ShapeSyncDatabase database,
            string preimage, bool reimport, out string diagnostic)
        {
            diagnostic = null;
            try
            {
                RestoreRegistry(registry, preimage);
                ClearDirty(registry);
                ClearDirty(database.gameObject);
                if (reimport) ReimportAsset(AssetDatabase.GetAssetPath(database));
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Database direct edit rollback failed: " + exception.Message;
                return false;
            }
        }

        private static string AppendDiagnostic(string primary, string secondary)
        {
            if (string.IsNullOrWhiteSpace(secondary)) return primary;
            if (string.IsNullOrWhiteSpace(primary)) return secondary;
            return primary + " " + secondary;
        }
    }
}
