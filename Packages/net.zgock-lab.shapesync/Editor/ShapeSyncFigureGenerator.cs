// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Publishes one fully generated Figure without changing the Database or its source assets.</summary>
    internal static class ShapeSyncFigureGenerator
    {
        // Editor test seam: invoked only after an existing Prefab was overwritten, before the
        // final SaveAssets commit, so rollback remains directly observable.
        internal static Action BeforeFinalSaveForTests;
        // Editor test seams for transaction rollback before Prefab commit. They are internal
        // only and are never invoked by production callers.
        internal static Action<UnityEngine.Object, string> BeforePersistForTests;
        internal static Action<UnityEngine.Object, string> AfterPersistForTests;
        internal static Action<string> BeforePrefabSaveForTests;
        private static ICollection<string> generatedPathSink;

        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath, string registriesPath, string bindingsPath, string materialsPath, string texturesPath, out string diagnostic)
            => TryGenerate(database, rootPath, registriesPath, bindingsPath, materialsPath, texturesPath, null, out diagnostic);

        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath, string registriesPath, string bindingsPath, string materialsPath, string texturesPath, ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            generatedPathSink = generatedPaths;
            ShapeSyncFigureGenerateMeshBuilder.Result result = null;
            GameObject prefabBackup = null;
            string prefabPath = null;
            string failureCode = "Unexpected";
            bool hasPersistedAssets = false;
            var createdPaths = new List<string>();
            var overwrittenAssets = new List<KeyValuePair<UnityEngine.Object, UnityEngine.Object>>();
            var deferredTextureSources = new List<Texture2D>();
            try
            {
                failureCode = "OutputPathInvalid";
                if (!ShapeSyncFigureGenerateOutputPaths.TryCreate(rootPath, registriesPath, bindingsPath, materialsPath, texturesPath, out ShapeSyncFigureGenerateOutputPaths paths, out StackMachineDiagnostic pathDiagnostic))
                    throw new InvalidOperationException(pathDiagnostic.ToString());
                failureCode = "OutputFolderCreateFailed";
                EnsureFolder(paths.RegistriesPath, createdPaths);
                EnsureFolder(paths.BindingsPath, createdPaths);
                EnsureFolder(paths.MaterialsPath, createdPaths);
                EnsureFolder(paths.TexturesPath, createdPaths);
                failureCode = "SnapshotInvalid";
                if (!ShapeSyncFigureGenerateSnapshot.TryCreate(database, out ShapeSyncFigureGenerateSnapshot snapshot, out StackMachineDiagnostic snapshotDiagnostic))
                    throw new InvalidOperationException(snapshotDiagnostic.ToString());
                failureCode = "MeshBuildFailed";
                if (!ShapeSyncFigureGenerateMeshBuilder.TryBuild(snapshot, out result, out StackMachineDiagnostic meshDiagnostic))
                    throw new InvalidOperationException(meshDiagnostic.ToString());
                failureCode = "PbmBuildFailed";
                if (!ShapeSyncFigureGeneratePbmBuilder.TryApply(snapshot, result, out StackMachineDiagnostic pbmDiagnostic))
                    throw new InvalidOperationException(pbmDiagnostic.ToString());
                // Runtime components are configured only after the static mesh/PBM escrow is complete.
                ShapeSyncFigureGenerateMeshBuilder.ConfigureRuntimeGraph(result);
                failureCode = "MaterialConfigureFailed";
                if (!ShapeSyncFigureGenerateMaterialConfigurator.TryConfigure(snapshot, result, out MaterialBinding materialBinding, out MeshBinding meshBinding, out StackMachineDiagnostic materialDiagnostic))
                    throw new InvalidOperationException(materialDiagnostic.ToString());

                string figureName = snapshot.BaseFigure.Name;
                SkinnedMeshRenderer renderer = result.Figure.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                var textureMap = new Dictionary<Texture2D, Texture2D>();
                failureCode = "AssetPersistFailed";
                foreach (MaterialTextureBindingEntry texture in materialBinding.Textures)
                {
                    Texture2D sourceTexture = texture.sourceTexture;
                    Texture2D persistent = Persist(sourceTexture, paths.TexturesPath, sourceTexture.name, createdPaths, overwrittenAssets, ref hasPersistedAssets, false);
                    if (!ReferenceEquals(sourceTexture, persistent)) deferredTextureSources.Add(sourceTexture);
                    textureMap.Add(sourceTexture, persistent);
                    texture.sourceTexture = persistent;
                }
                foreach (var owner in meshBinding.NormalOwners)
                    foreach (var target in owner.targets)
                        foreach (var texture in target.textures)
                            if (textureMap.TryGetValue(texture.normalTexture, out Texture2D persistent)) texture.normalTexture = persistent;

                foreach (Material material in renderer.sharedMaterials)
                    foreach (string property in material.GetTexturePropertyNames())
                        if (material.GetTexture(property) is Texture2D texture && textureMap.TryGetValue(texture, out Texture2D persistent)) material.SetTexture(property, persistent);
                Material[] generatedMaterials = renderer.sharedMaterials;
                var persistedMaterials = new Material[generatedMaterials.Length];
                for (int materialIndex = 0; materialIndex < generatedMaterials.Length; materialIndex++)
                {
                    Material generatedMaterial = generatedMaterials[materialIndex];
                    KeyValuePair<string, Texture>[] textureReferences = generatedMaterial.GetTexturePropertyNames()
                        .Select(property =>
                        {
                            Texture texture = generatedMaterial.GetTexture(property);
                            if (texture is Texture2D texture2D && textureMap.TryGetValue(texture2D, out Texture2D persistentTexture)) texture = persistentTexture;
                            return new KeyValuePair<string, Texture>(property, texture);
                        }).ToArray();
                    Material persistentMaterial = Persist(generatedMaterial, paths.MaterialsPath, generatedMaterial.name, createdPaths, overwrittenAssets, ref hasPersistedAssets);
                    // Set PPtrs only after the Material itself is a persistent asset. Unity may
                    // discard native Material texture references copied from a transient object
                    // while replacing an existing output asset.
                    foreach (KeyValuePair<string, Texture> textureReference in textureReferences)
                        persistentMaterial.SetTexture(textureReference.Key, textureReference.Value);
                    EditorUtility.SetDirty(persistentMaterial);
                    AssetDatabase.SaveAssetIfDirty(persistentMaterial);
                    Material reloadedMaterial = AssetDatabase.LoadAssetAtPath<Material>(paths.MaterialsPath.TrimEnd('/') + "/" + SanitizeFileName(persistentMaterial.name) + ".asset");
                    if (reloadedMaterial == null) throw new InvalidOperationException("Generated Material could not be reloaded: " + persistentMaterial.name);
                    foreach (KeyValuePair<string, Texture> textureReference in textureReferences)
                        reloadedMaterial.SetTexture(textureReference.Key, textureReference.Value);
                    EditorUtility.SetDirty(reloadedMaterial);
                    AssetDatabase.SaveAssetIfDirty(reloadedMaterial);
                    foreach (KeyValuePair<string, Texture> textureReference in textureReferences)
                        if (textureReference.Value != null && reloadedMaterial.GetTexture(textureReference.Key) == null)
                            throw new InvalidOperationException("Generated Material lost a Texture reference during overwrite persistence: " + reloadedMaterial.name + "." + textureReference.Key);
                    AfterPersistForTests?.Invoke(reloadedMaterial, AssetDatabase.GetAssetPath(reloadedMaterial));
                    persistedMaterials[materialIndex] = persistentMaterial;
                }
                renderer.sharedMaterials = persistedMaterials;
                foreach (Texture2D sourceTexture in deferredTextureSources) UnityEngine.Object.DestroyImmediate(sourceTexture);
                deferredTextureSources.Clear();
                AssetDatabase.SaveAssets();
                MaterialProxy proxy = result.Figure.GetComponent<MaterialProxy>();
                var adaptersByShader = new Dictionary<Shader, MaterialShaderAdapter>();
                foreach (MaterialProxyEntry entry in proxy.Entries)
                {
                    Material material = entry.renderer.sharedMaterials[entry.materialChannel];
                    Shader shader = material == null ? null : material.shader;
                    if (shader == null) throw new InvalidOperationException("Generated Material Entry has no Shader: " + entry.entryName);
                    if (!adaptersByShader.TryGetValue(shader, out MaterialShaderAdapter adapter))
                    {
                        adapter = UnityEngine.Object.Instantiate(entry.adapter);
                        adapter.name = entry.adapter.name;
                        adapter = Persist(adapter, paths.BindingsPath, adapter.name, createdPaths, overwrittenAssets, ref hasPersistedAssets);
                        adaptersByShader.Add(shader, adapter);
                    }
                    entry.adapter = adapter;
                }

                renderer.sharedMesh = Persist(result.Mesh, paths.RegistriesPath, figureName + "_Mesh", createdPaths, overwrittenAssets, ref hasPersistedAssets);
                Animator animator = result.Figure.GetComponentsInChildren<Animator>(true).Single();
                Avatar baseAvatar = Persist(result.Avatar, paths.RegistriesPath, figureName + "_Avatar", createdPaths, overwrittenAssets, ref hasPersistedAssets);
                var baseRegistry = Persist(result.BaseRegistry, paths.RegistriesPath, figureName + "_Registry", createdPaths, overwrittenAssets, ref hasPersistedAssets);
                animator.avatar = baseAvatar;

                foreach (DynamicBoneBlendTarget target in result.RuntimeTargets)
                {
                    if (string.IsNullOrWhiteSpace(target.targetAvatar.name)) target.targetAvatar.name = figureName + "_" + target.blendName + "_Avatar";
                    if (string.IsNullOrWhiteSpace(target.targetRegistry.name)) target.targetRegistry.name = figureName + "_" + target.blendName + "_Registry";
                    target.targetAvatar = Persist(target.targetAvatar, paths.RegistriesPath, target.targetAvatar.name, createdPaths, overwrittenAssets, ref hasPersistedAssets);
                    target.targetRegistry = Persist(target.targetRegistry, paths.RegistriesPath, target.targetRegistry.name, createdPaths, overwrittenAssets, ref hasPersistedAssets);
                    foreach (DynamicBonePbmDifferenceTarget difference in target.pbmDifferenceTargets)
                    {
                        if (string.IsNullOrWhiteSpace(difference.targetAvatar.name)) difference.targetAvatar.name = figureName + "_" + target.blendName + "_" + difference.fbmBlendName + "_Avatar";
                        if (string.IsNullOrWhiteSpace(difference.targetRegistry.name)) difference.targetRegistry.name = figureName + "_" + target.blendName + "_" + difference.fbmBlendName + "_Registry";
                        difference.targetAvatar = Persist(difference.targetAvatar, paths.RegistriesPath, difference.targetAvatar.name, createdPaths, overwrittenAssets, ref hasPersistedAssets);
                        difference.targetRegistry = Persist(difference.targetRegistry, paths.RegistriesPath, difference.targetRegistry.name, createdPaths, overwrittenAssets, ref hasPersistedAssets);
                    }
                }

                result.Figure.GetComponent<DynamicBoneBlender>().ConfigureForFigure(renderer, animator, baseAvatar, baseRegistry, result.RuntimeTargets.ToList());
                MaterialBinding persistedMaterialBinding = Persist(materialBinding, paths.BindingsPath, figureName + "_MaterialBinding", createdPaths, overwrittenAssets, ref hasPersistedAssets);
                MeshBinding persistedMeshBinding = Persist(meshBinding, paths.BindingsPath, figureName + "_MeshBinding", createdPaths, overwrittenAssets, ref hasPersistedAssets);
                ShapeDocumentSerializer serializer = result.Figure.AddComponent<ShapeDocumentSerializer>();
                ShapeDocumentDeserializer deserializer = result.Figure.AddComponent<ShapeDocumentDeserializer>();
                ShapeDirector director = result.Figure.AddComponent<ShapeDirector>();
                SerializedObject directorSerialized = new SerializedObject(director);
                directorSerialized.FindProperty("meshBinding").objectReferenceValue = persistedMeshBinding;
                directorSerialized.FindProperty("materialBinding").objectReferenceValue = persistedMaterialBinding;
                directorSerialized.FindProperty("serializer").objectReferenceValue = serializer;
                directorSerialized.FindProperty("deserializer").objectReferenceValue = deserializer;
                directorSerialized.ApplyModifiedPropertiesWithoutUndo();

                prefabPath = paths.RootPath.TrimEnd('/') + "/" + figureName + ".prefab";
                bool prefabExisted = AssetDatabase.LoadMainAssetAtPath(prefabPath) != null;
                if (prefabExisted) prefabBackup = CreatePrefabBackup(prefabPath);
                failureCode = "PrefabSaveFailed";
                BeforePrefabSaveForTests?.Invoke(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(result.Figure, prefabPath, out bool prefabSaved);
                if (!prefabSaved) throw new InvalidOperationException("Unity could not save the generated Figure Prefab.");
                if (!prefabExisted) createdPaths.Add(prefabPath);
                generatedPaths?.Add(prefabPath);
                failureCode = "AssetCommitFailed";
                BeforeFinalSaveForTests?.Invoke();
                AssetDatabase.SaveAssets();
                UnityEngine.Object.DestroyImmediate(result.Figure);
                result = null; // all owned generated assets are persistent and must not be destroyed.
                if (prefabBackup != null) UnityEngine.Object.DestroyImmediate(prefabBackup);
                generatedPathSink = null;
                return true;
            }
            catch (Exception exception)
            {
                if (result != null)
                {
                    if (hasPersistedAssets) UnityEngine.Object.DestroyImmediate(result.Figure);
                    else result.Dispose();
                }
                foreach (Texture2D sourceTexture in deferredTextureSources) if (sourceTexture != null) UnityEngine.Object.DestroyImmediate(sourceTexture);
                for (int index = overwrittenAssets.Count - 1; index >= 0; index--)
                {
                    EditorUtility.CopySerialized(overwrittenAssets[index].Value, overwrittenAssets[index].Key);
                    UnityEngine.Object.DestroyImmediate(overwrittenAssets[index].Value);
                }
                for (int index = createdPaths.Count - 1; index >= 0; index--) AssetDatabase.DeleteAsset(createdPaths[index]);
                if (prefabBackup != null && !string.IsNullOrEmpty(prefabPath))
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabBackup, prefabPath, out bool restored);
                    UnityEngine.Object.DestroyImmediate(prefabBackup);
                    if (!restored) exception = new InvalidOperationException(exception.Message + " Generated Prefab rollback also failed.");
                }
                AssetDatabase.SaveAssets();
                generatedPathSink = null;
                diagnostic = "FigureGenerate" + failureCode + ": " + exception.Message;
                return false;
            }
        }

        private static GameObject CreatePrefabBackup(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try { return UnityEngine.Object.Instantiate(contents); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static T Persist<T>(T asset, string folder, string name, ICollection<string> createdPaths, ICollection<KeyValuePair<UnityEngine.Object, UnityEngine.Object>> overwrittenAssets, ref bool persistedAny, bool destroySource = true) where T : UnityEngine.Object
        {
            if (asset == null) throw new InvalidOperationException("Generated Figure contains a missing output asset.");
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset))) return asset;
            string persistedName = SanitizeFileName(name);
            // Unity requires the main object's name to match the filename.  This is
            // especially important on overwrite Generate: CopySerialized does not
            // repair a stale name left by a previous PBM rebuild or an unnamed
            // transient registry/binding object.
            asset.name = persistedName;
            string path = folder.TrimEnd('/') + "/" + persistedName + ".asset";
            generatedPathSink?.Add(path);
            BeforePersistForTests?.Invoke(asset, path);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                if (existing.GetType() != asset.GetType()) throw new InvalidOperationException("Generate output path has a different asset type: " + path);
                UnityEngine.Object backup = existing is Texture texture
                    ? ShapeSyncEditorTextureUtility.Clone(texture)
                    : existing is Mesh mesh
                        ? ShapeSyncMeshCloneUtility.Clone(mesh)
                        : UnityEngine.Object.Instantiate(existing);
                overwrittenAssets.Add(new KeyValuePair<UnityEngine.Object, UnityEngine.Object>(existing, backup));
                    existing.name = persistedName;
                    // CopySerialized does not preserve Material texture PPtrs when replacing an
                    // existing .asset.  Material owns a dedicated native copy operation; use it
                    // for overwrite Generate so all output Texture references survive serialization.
                    EditorUtility.CopySerialized(asset, existing);
                    EditorUtility.SetDirty(existing);
                    if (existing is Material) AssetDatabase.SaveAssetIfDirty(existing);
                    if (destroySource) UnityEngine.Object.DestroyImmediate(asset);
                persistedAny = true;
                return (T)existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            createdPaths.Add(path);
            persistedAny = true;
            return asset;
        }

        private static void EnsureFolder(string path, ICollection<string> createdPaths)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, segments[index]);
                    if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Could not create output folder: " + next);
                    createdPaths.Add(next);
                }
                current = next;
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Generated output asset has no name.");
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }
    }
}
#endif
