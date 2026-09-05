// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Utilities;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>One Material output path and the MaterialId that owns its published name.</summary>
    internal readonly struct HumanoidPublishMaterialOutput
    {
        internal HumanoidPublishMaterialOutput(MaterialId materialId, string assetPath)
        {
            MaterialId = materialId;
            AssetPath = assetPath;
        }

        internal MaterialId MaterialId { get; }
        internal string AssetPath { get; }
    }

    /// <summary>One individual Texture output, including the consumed per-Material index.</summary>
    internal readonly struct HumanoidPublishTextureOutput
    {
        internal HumanoidPublishTextureOutput(MaterialId materialId, int index, string outputTextureKey, string assetPath)
        {
            MaterialId = materialId;
            Index = index;
            OutputTextureKey = outputTextureKey;
            AssetPath = assetPath;
        }

        internal MaterialId MaterialId { get; }
        internal int Index { get; }
        internal string OutputTextureKey { get; }
        internal string AssetPath { get; }
    }

    /// <summary>One Atlas page output path and its semantic identity.</summary>
    internal readonly struct HumanoidPublishAtlasOutput
    {
        internal HumanoidPublishAtlasOutput(int pageIndex, string semantic, string assetPath)
        {
            PageIndex = pageIndex;
            Semantic = semantic;
            AssetPath = assetPath;
        }

        internal int PageIndex { get; }
        internal string Semantic { get; }
        internal string AssetPath { get; }
    }

    /// <summary>Immutable naming contract retained from staging through the final Prefab commit.</summary>
    internal sealed class HumanoidPublishOutputContract
    {
        internal HumanoidPublishOutputContract(string outputFolder, string assetPrefix, string meshPath, string avatarPath,
            IReadOnlyList<HumanoidPublishMaterialOutput> materials,
            IReadOnlyList<HumanoidPublishTextureOutput> textures,
            IReadOnlyList<HumanoidPublishAtlasOutput> atlasTextures)
        {
            OutputFolder = outputFolder;
            AssetPrefix = assetPrefix;
            MeshPath = meshPath;
            AvatarPath = avatarPath;
            Materials = materials ?? Array.Empty<HumanoidPublishMaterialOutput>();
            Textures = textures ?? Array.Empty<HumanoidPublishTextureOutput>();
            AtlasTextures = atlasTextures ?? Array.Empty<HumanoidPublishAtlasOutput>();
        }

        internal string OutputFolder { get; }
        internal string AssetPrefix { get; }
        internal string MeshPath { get; }
        internal string AvatarPath { get; }
        internal IReadOnlyList<HumanoidPublishMaterialOutput> Materials { get; }
        internal IReadOnlyList<HumanoidPublishTextureOutput> Textures { get; }
        internal IReadOnlyList<HumanoidPublishAtlasOutput> AtlasTextures { get; }
    }

    /// <summary>One Editor-only individual asset staging result retained by the publish transaction until Prefab commit.</summary>
    internal sealed class HumanoidIndividualAssetStage
    {
        internal HumanoidIndividualAssetStage(Mesh mesh, Avatar avatar, Material[] materials, Texture2D[] textures, string[] assetPaths)
        {
            Mesh = mesh; Avatar = avatar; Materials = materials; Textures = textures; AssetPaths = assetPaths;
        }

        internal HumanoidIndividualAssetStage(Mesh mesh, Avatar avatar, Material[] materials, Texture2D[] textures, string[] assetPaths, HumanoidPublishOutputContract outputContract)
        {
            Mesh = mesh; Avatar = avatar; Materials = materials; Textures = textures; AssetPaths = assetPaths; OutputContract = outputContract;
        }

        internal Mesh Mesh { get; }
        internal Avatar Avatar { get; }
        /// <summary>Gets one persistent Material per final Mesh submesh, in submesh order.</summary>
        internal IReadOnlyList<Material> Materials { get; }
        internal IReadOnlyList<Texture2D> Textures { get; }
        /// <summary>Gets assets left on disk when a later publish phase cancels or fails. They are intentionally not deleted.</summary>
        internal IReadOnlyList<string> AssetPaths { get; }
        /// <summary>Gets the immutable Spec17 §1.3 output naming contract captured during staging.</summary>
        internal HumanoidPublishOutputContract OutputContract { get; }
    }

    /// <summary>Stages non-VRM individual assets from a completed in-memory Humanoid result; Prefab commit remains a later transaction.</summary>
    internal static class HumanoidIndividualAssetStager
    {
        private sealed class AtlasPageBinding
        {
            internal AtlasPageBinding(AtlasBakerPageCompletion page, Material target, string propertyName) { Page = page; Target = target; PropertyName = propertyName; }
            internal AtlasBakerPageCompletion Page { get; }
            internal Material Target { get; }
            internal string PropertyName { get; }
        }

        internal static Action<string, byte[]> WriteAllBytes = File.WriteAllBytes;
        internal static Action<string> ImportAsset = path => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        // Material texture references are part of this stage's persistent artifact, not deferred
        // state for a later Prefab or optional VRM transaction to flush incidentally.
        internal static Action SaveAssets = AssetDatabase.SaveAssets;
        internal static bool TryValidateEmptyOutputFolder(string outputFolder, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
                return Reject("PublishOutputFolderRequired", "Individual asset staging requires an existing Assets/ output folder.", out diagnostic);
            string absolute = ToAbsolutePath(outputFolder);
            if (absolute == null || !Directory.Exists(absolute))
                return Reject("PublishOutputFolderRequired", "Individual asset staging could not resolve the selected output folder.", out diagnostic);
            using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(absolute).GetEnumerator())
            {
                if (entries.MoveNext()) return Reject("PublishOutputFolderNotEmpty", "Pure Humanoid output folder must be empty.", out diagnostic);
            }
            return true;
        }

        internal static bool TryStage(string outputFolder, string documentName, HumanoidBuildResult result, out HumanoidIndividualAssetStage stage, out string[] residualAssetPaths, out StackMachineDiagnostic diagnostic)
        {
            stage = null;
            residualAssetPaths = Array.Empty<string>();
            diagnostic = null;
            if (!TryValidateEmptyOutputFolder(outputFolder, out diagnostic)) return false;
            if (string.IsNullOrWhiteSpace(documentName)) return Reject("PublishDocumentNameRequired", "Individual asset staging requires a document name.", out diagnostic);
            InMemoryHumanoidMesh payload = result?.Mesh;
            if (payload == null || payload.Mesh == null) return Reject("PublishMeshRequired", "Individual asset staging requires a completed in-memory Humanoid Mesh.", out diagnostic);
            if (payload.Materials.Count != payload.MaterialSlots.Count) return Reject("PublishMaterialSlotMismatch", "Individual asset staging requires one material slot mapping for every final material.", out diagnostic);

            if (!HumanoidTexturePublishReadback.TryCollect(payload, out IReadOnlyList<HumanoidTextureReadbackEntry> entries, out diagnostic)) return false;
            var created = new List<string>();
            try
            {
                if (!TryGetOutputFolderName(outputFolder, out string assetPrefix)) return Reject("PublishOutputFolderNameRequired", "Individual asset staging requires an output folder name.", out diagnostic);
                var texturePaths = new Dictionary<HumanoidTextureReadbackEntry, string>();
                if (!TryCollectAtlasPageBindings(payload, out List<AtlasPageBinding> atlasBindings, out diagnostic)) return false;
                var atlasPaths = new Dictionary<AtlasBakerPageCompletion, string>();
                var atlasOutputs = new List<HumanoidPublishAtlasOutput>();
                if (!TryStageAtlasPages(outputFolder, assetPrefix, atlasBindings, atlasPaths, atlasOutputs, created, out diagnostic)) return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
                var textureOutputs = new List<HumanoidPublishTextureOutput>();
                if (!TryStageTextures(outputFolder, assetPrefix, entries, texturePaths, textureOutputs, created, out diagnostic)) return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
                if (!TryStageMeshAndAvatar(outputFolder, assetPrefix, payload, created, out Mesh mesh, out Avatar avatar, out diagnostic)) return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
                if (!TryStageMaterials(outputFolder, assetPrefix, payload, entries, texturePaths, atlasBindings, atlasPaths, created, out Material[] materials, out diagnostic)) return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
                var materialOutputs = new List<HumanoidPublishMaterialOutput>();
                var materialOutputIds = new HashSet<int>();
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !materialOutputIds.Add(material.GetInstanceID())) continue;
                    string materialPath = AssetDatabase.GetAssetPath(material);
                    materialOutputs.Add(new HumanoidPublishMaterialOutput(payload.MaterialSlots[i].MaterialId, materialPath));
                }
                var outputContract = new HumanoidPublishOutputContract(
                    outputFolder,
                    assetPrefix,
                    CombineAssetPath(outputFolder, assetPrefix + ".asset"),
                    avatar == null ? null : CombineAssetPath(outputFolder, assetPrefix + "_avatar.asset"),
                    materialOutputs,
                    textureOutputs,
                    atlasOutputs);
                if (!HumanoidPublishPathValidator.TryValidateOutputNaming(outputContract, out diagnostic))
                    return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
                var textures = new List<Texture2D>();
                var stagedTexturePaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (HumanoidTextureReadbackEntry entry in entries)
                {
                    string texturePath = texturePaths[entry];
                    if (!stagedTexturePaths.Add(texturePath)) continue;
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (texture == null)
                    {
                        Reject("PublishTextureAssetMissing", "Individual asset staging could not reload an imported texture asset.", out diagnostic, entry.MaterialId.EntryId);
                        return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
                    }
                    textures.Add(texture);
                }
                foreach (KeyValuePair<AtlasBakerPageCompletion, string> pair in atlasPaths)
                {
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pair.Value);
                    if (texture == null) return FailWithResidual(created, StackMachineDiagnostic.CreateDomain("humanoid", "PublishTextureAssetMissing", "Individual asset staging could not reload an imported Atlas page texture asset."), out residualAssetPaths, out diagnostic);
                    textures.Add(texture);
                }
                stage = new HumanoidIndividualAssetStage(mesh, avatar, materials, textures.ToArray(), created.ToArray(), outputContract);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PublishAssetStagingFailed", exception.Message);
                return FailWithResidual(created, diagnostic, out residualAssetPaths, out diagnostic);
            }
        }

        private static bool TryStageTextures(string folder, string assetPrefix, IReadOnlyList<HumanoidTextureReadbackEntry> entries, Dictionary<HumanoidTextureReadbackEntry, string> paths, List<HumanoidPublishTextureOutput> outputs, List<string> created, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var stagedByTexture = new Dictionary<string, string>(StringComparer.Ordinal);
            var indexByMaterial = new Dictionary<MaterialId, int>();
            for (int i = 0; i < entries.Count; i++)
            {
                HumanoidTextureReadbackEntry entry = entries[i];
                int index = indexByMaterial.TryGetValue(entry.MaterialId, out int materialIndex) ? materialIndex : 0;
                indexByMaterial[entry.MaterialId] = index + 1;
                string textureKey = entry.OutputTextureKey;
                if (stagedByTexture.TryGetValue(textureKey, out string existingPath))
                {
                    paths.Add(entry, existingPath);
                    outputs.Add(new HumanoidPublishTextureOutput(entry.MaterialId, index, textureKey, existingPath));
                    continue;
                }
                Texture2D sourceTexture = entry.Texture as Texture2D;
                string sourceAssetPath = sourceTexture != null ? AssetDatabase.GetAssetPath(sourceTexture) : null;
                // A VRM/GLTF importer exposes embedded Texture2D sub-assets with the
                // container path (for example, BasicFemale.vrm). Copying that path would
                // publish the whole source container as a texture and reintroduce every
                // source-model dependency into the Pure Humanoid output. Copy only a
                // standalone PNG main texture asset; every other source is encoded below
                // so the Pure Humanoid naming contract always ends in .png.
                bool copyPersistentPngAsset = sourceTexture != null
                    && !string.IsNullOrWhiteSpace(sourceAssetPath)
                    && AssetDatabase.LoadMainAssetAtPath(sourceAssetPath) == sourceTexture
                    && string.Equals(Path.GetExtension(sourceAssetPath), ".png", StringComparison.OrdinalIgnoreCase);
                string path = CombineAssetPath(folder, TextureAssetBaseName(assetPrefix, entry, index) + ".png");
                if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToAbsolutePath(path))) return Reject("PublishAssetPathOccupied", "Individual asset staging found an occupied output asset path.", out diagnostic, entry.MaterialId.EntryId);
                if (copyPersistentPngAsset)
                {
                    if (!AssetDatabase.CopyAsset(sourceAssetPath, path))
                        return Reject("PublishTextureAssetCopyFailed", "Individual asset staging could not copy a persistent source texture asset.", out diagnostic, sourceAssetPath);
                    created.Add(path);
                    if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                        return Reject("PublishTextureAssetMissing", "Individual asset staging could not reload a copied source texture asset.", out diagnostic, path);
                }
                else
                {
                    if (!HumanoidTexturePublishReadback.TryEncodePng(entry, out byte[] png, out diagnostic)) return false;
                    WriteAllBytes(ToAbsolutePath(path), png);
                    created.Add(path);
                    ImportAsset(path);
                    if (!HumanoidTexturePublishReadback.TryConfigureImporter(path, entry, out diagnostic)) return false;
                }
                stagedByTexture.Add(textureKey, path);
                paths.Add(entry, path);
                outputs.Add(new HumanoidPublishTextureOutput(entry.MaterialId, index, textureKey, path));
            }
            return true;
        }

        private static bool TryStageAtlasPages(string folder, string assetPrefix, IReadOnlyList<AtlasPageBinding> bindings, Dictionary<AtlasBakerPageCompletion, string> paths, List<HumanoidPublishAtlasOutput> outputs, List<string> created, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            for (int i = 0; i < bindings.Count; i++)
            {
                AtlasBakerPageCompletion page = bindings[i].Page;
                if (paths.ContainsKey(page)) continue;
                HumanoidPublishTextureSemantic semantic = page.Semantic == AtlasTextureSemantic.BaseColor ? HumanoidPublishTextureSemantic.BaseColor : HumanoidPublishTextureSemantic.Normal;
                var entry = new HumanoidTextureReadbackEntry(default, semantic, page.Texture, null, null, Array.Empty<string>(), true);
                if (!HumanoidTexturePublishReadback.TryEncodePng(entry, out byte[] png, out diagnostic)) return false;
                string suffix = page.Semantic == AtlasTextureSemantic.BaseColor ? "basecolor" : "normal";
                string path = CombineAssetPath(folder, assetPrefix + "_atlas" + page.PageIndex + "_" + suffix + ".png");
                if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToAbsolutePath(path))) return Reject("PublishAssetPathOccupied", "Individual asset staging found an occupied output asset path.", out diagnostic);
                WriteAllBytes(ToAbsolutePath(path), png);
                created.Add(path);
                ImportAsset(path);
                if (!HumanoidTexturePublishReadback.TryConfigureImporter(path, entry, out diagnostic)) return false;
                paths.Add(page, path);
                outputs.Add(new HumanoidPublishAtlasOutput(page.PageIndex, suffix, path));
            }
            return true;
        }

        private static bool TryStageMeshAndAvatar(string folder, string assetPrefix, InMemoryHumanoidMesh payload, List<string> created, out Mesh mesh, out Avatar avatar, out StackMachineDiagnostic diagnostic)
        {
            mesh = null; avatar = null; diagnostic = null;
            string meshPath = CombineAssetPath(folder, assetPrefix + ".asset");
            if (!TryCreateCloneAsset(payload.Mesh, meshPath, created, out mesh, out diagnostic, "PublishMeshAssetFailed")) return false;
            if (payload.Avatar == null) return true;
            string avatarPath = CombineAssetPath(folder, assetPrefix + "_avatar.asset");
            return TryCreateCloneAsset(payload.Avatar, avatarPath, created, out avatar, out diagnostic, "PublishAvatarAssetFailed");
        }

        private static bool TryStageMaterials(string folder, string assetPrefix, InMemoryHumanoidMesh payload, IReadOnlyList<HumanoidTextureReadbackEntry> entries, Dictionary<HumanoidTextureReadbackEntry, string> texturePaths, IReadOnlyList<AtlasPageBinding> atlasBindings, Dictionary<AtlasBakerPageCompletion, string> atlasPaths, List<string> created, out Material[] materials, out StackMachineDiagnostic diagnostic)
        {
            materials = new Material[payload.Materials.Count]; diagnostic = null;
            var persistentByTarget = new Dictionary<int, Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material target = payload.Materials[i]; HumanoidBuildMaterialSlot slot = payload.MaterialSlots[i];
                if (target == null || !slot.MaterialId.IsValid) return Reject("PublishMaterialMappingInvalid", "Individual asset staging received an invalid material mapping.", out diagnostic, slot.MaterialId.EntryId);
                if (!persistentByTarget.TryGetValue(target.GetInstanceID(), out Material persistent))
                {
                    string path = CombineAssetPath(folder, AssetBaseName(assetPrefix, slot.MaterialId) + ".mat");
                    if (!TryCreateCloneAsset(target, path, created, out persistent, out diagnostic, "PublishMaterialAssetFailed")) return false;
                    persistentByTarget.Add(target.GetInstanceID(), persistent);
                }
                materials[i] = persistent;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                HumanoidTextureReadbackEntry entry = entries[i];
                if (!persistentByTarget.TryGetValue(entry.TargetMaterial.GetInstanceID(), out Material material)) return Reject("PublishMaterialMappingInvalid", "Individual asset staging could not resolve a material texture target.", out diagnostic, entry.MaterialId.EntryId);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePaths[entry]);
                if (texture == null) return Reject("PublishTextureAssetMissing", "Individual asset staging could not reload an imported texture asset.", out diagnostic, entry.MaterialId.EntryId);
                for (int propertyIndex = 0; propertyIndex < entry.PropertyNames.Count; propertyIndex++) material.SetTexture(entry.PropertyNames[propertyIndex], texture);
                EditorUtility.SetDirty(material);
            }
            for (int i = 0; i < atlasBindings.Count; i++)
            {
                AtlasPageBinding binding = atlasBindings[i];
                if (!persistentByTarget.TryGetValue(binding.Target.GetInstanceID(), out Material material)) return Reject("PublishMaterialMappingInvalid", "Individual asset staging could not resolve an Atlas page material target.", out diagnostic);
                if (!atlasPaths.TryGetValue(binding.Page, out string path)) return Reject("PublishAtlasPagePathMissing", "Individual asset staging could not resolve a staged Atlas page.", out diagnostic);
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) return Reject("PublishTextureAssetMissing", "Individual asset staging could not reload an imported Atlas page texture asset.", out diagnostic);
                material.SetTexture(binding.PropertyName, texture);
                EditorUtility.SetDirty(material);
            }
            // Optional VRM staging performs additional AssetDatabase work before Prefab commit.
            // Persist every material here so that work cannot observe or discard a transient
            // texture assignment.
            SaveAssets();
            return true;
        }

        private static bool TryCollectAtlasPageBindings(InMemoryHumanoidMesh payload, out List<AtlasPageBinding> bindings, out StackMachineDiagnostic diagnostic)
        {
            bindings = new List<AtlasPageBinding>();
            diagnostic = null;
            if (payload.AtlasPages == null) return true;
            var pageKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int pageIndex = 0; pageIndex < payload.AtlasPages.Pages.Count; pageIndex++)
            {
                AtlasBakerPageCompletion page = payload.AtlasPages.Pages[pageIndex];
                if (page == null || page.Texture == null || (page.Semantic != AtlasTextureSemantic.BaseColor && page.Semantic != AtlasTextureSemantic.Normal)) return Reject("PublishAtlasPageInvalid", "Individual asset staging received an invalid Atlas page carrier.", out diagnostic);
                string pageKey = page.PageIndex + ":" + (int)page.Semantic;
                if (!pageKeys.Add(pageKey)) return Reject("PublishAtlasPageDuplicate", "Individual asset staging received duplicate Atlas page index and semantic identity.", out diagnostic);
                MaterialProxySemantic semantic = page.Semantic == AtlasTextureSemantic.BaseColor ? MaterialProxySemantic.BaseColorTexture : MaterialProxySemantic.NormalTexture;
                int countBefore = bindings.Count;
                for (int slotIndex = 0; slotIndex < payload.Materials.Count; slotIndex++)
                {
                    Material material = payload.Materials[slotIndex];
                    HumanoidBuildMaterialSlot slot = payload.MaterialSlots[slotIndex];
                    var properties = new List<string>();
                    if (material == null || slot.Adapter == null) return Reject("PublishMaterialMappingInvalid", "Individual asset staging received an invalid Atlas page material mapping.", out diagnostic, slot.MaterialId.EntryId);
                    if (!slot.Adapter.TryGetPublishTextureProperties(semantic, properties, out MaterialProxyDiagnostic adapterDiagnostic))
                        return Reject("PublishTexturePropertyMappingInvalid", adapterDiagnostic.message, out diagnostic, slot.MaterialId.EntryId);
                    for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                    {
                        string propertyName = properties[propertyIndex];
                        if (material.HasProperty(propertyName) && material.GetTexture(propertyName) == page.Texture) bindings.Add(new AtlasPageBinding(page, material, propertyName));
                    }

                    // Atlas output owns the page texture, so every final Material property
                    // pointing at that page must be rebound to the published page asset too.
                    var allTextureProperties = new List<string>();
                    material.GetTexturePropertyNames(allTextureProperties);
                    for (int propertyIndex = 0; propertyIndex < allTextureProperties.Count; propertyIndex++)
                    {
                        string propertyName = allTextureProperties[propertyIndex];
                        if (material.GetTexture(propertyName) != page.Texture) continue;
                        bool alreadyBound = false;
                        for (int bindingIndex = countBefore; bindingIndex < bindings.Count; bindingIndex++)
                        {
                            AtlasPageBinding binding = bindings[bindingIndex];
                            if (binding.Target == material && binding.PropertyName == propertyName) { alreadyBound = true; break; }
                        }
                        if (!alreadyBound) bindings.Add(new AtlasPageBinding(page, material, propertyName));
                    }
                }
                if (bindings.Count == countBefore) return Reject("PublishAtlasPageUnreferenced", "Individual asset staging received an Atlas page that no final candidate Material references.", out diagnostic);
            }
            return true;
        }

        private static bool TryCreateCloneAsset<T>(T source, string path, List<string> created, out T persisted, out StackMachineDiagnostic diagnostic, string failureCode) where T : UnityEngine.Object
        {
            persisted = null; diagnostic = null;
            if (source == null) return Reject(failureCode, "Individual asset staging cannot clone a null source asset.", out diagnostic);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(ToAbsolutePath(path))) return Reject("PublishAssetPathOccupied", "Individual asset staging found an occupied output asset path.", out diagnostic);
            T clone = source is Mesh mesh
                ? (T)(UnityEngine.Object)ShapeSyncMeshCloneUtility.Clone(mesh)
                : UnityEngine.Object.Instantiate(source);
            try
            {
                clone.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(clone, path);
                created.Add(path);
                persisted = AssetDatabase.LoadAssetAtPath<T>(path);
                if (persisted == null) return Reject(failureCode, "Individual asset staging could not reload a created asset.", out diagnostic);
                return true;
            }
            catch
            {
                if (!AssetDatabase.Contains(clone)) UnityEngine.Object.DestroyImmediate(clone);
                throw;
            }
        }

        internal static bool TryGetOutputFolderName(string outputFolder, out string folderName)
        {
            folderName = null;
            if (string.IsNullOrWhiteSpace(outputFolder)) return false;
            string normalized = outputFolder.Trim().Replace('\\', '/').TrimEnd('/');
            int separator = normalized.LastIndexOf('/');
            string candidate = separator < 0 ? normalized : normalized.Substring(separator + 1);
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            folderName = candidate;
            return true;
        }

        private static string AssetBaseName(string assetPrefix, MaterialId materialId) => string.IsNullOrEmpty(materialId.RegistryId) ? assetPrefix + "_" + materialId.EntryId : assetPrefix + "_" + materialId.RegistryId + "_" + materialId.EntryId;
        private static string TextureAssetBaseName(string assetPrefix, HumanoidTextureReadbackEntry entry, int index) => AssetBaseName(assetPrefix, entry.MaterialId) + "_" + index;
        private static string CombineAssetPath(string folder, string file) => (folder.TrimEnd('/', '\\') + "/" + file).Replace('\\', '/');
        private static string ToAbsolutePath(string assetPath) => Path.GetFullPath(assetPath);
        private static bool FailWithResidual(List<string> created, StackMachineDiagnostic source, out string[] residualAssetPaths, out StackMachineDiagnostic diagnostic)
        {
            residualAssetPaths = created.ToArray();
            diagnostic = source;
            if (residualAssetPaths.Length == 0) return false;
            string paths = string.Join("|", residualAssetPaths);
            diagnostic.detail = string.IsNullOrEmpty(diagnostic.detail) ? paths : diagnostic.detail + "|" + paths;
            return false;
        }
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string bindingName = null) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message, bindingName: bindingName); return false; }
    }
}
