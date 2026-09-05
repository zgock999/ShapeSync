// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Identifies the importer treatment required by one compiler-produced texture.</summary>
    internal enum HumanoidPublishTextureSemantic { BaseColor, Normal, Preserved }

    /// <summary>One non-owning mapping from a compiler texture to its target material property.</summary>
    internal readonly struct HumanoidTextureReadbackEntry
    {
        internal HumanoidTextureReadbackEntry(MaterialId materialId, HumanoidPublishTextureSemantic semantic, Texture texture, Texture samplerSource, Material targetMaterial, string propertyName, bool isAtlasPage = false)
        {
            MaterialId = materialId; Semantic = semantic; Texture = texture; SamplerSource = samplerSource; TargetMaterial = targetMaterial; PropertyNames = new[] { propertyName }; IsAtlasPage = isAtlasPage;
        }
        internal HumanoidTextureReadbackEntry(MaterialId materialId, HumanoidPublishTextureSemantic semantic, Texture texture, Texture samplerSource, Material targetMaterial, string[] propertyNames, bool isAtlasPage = false)
        { MaterialId = materialId; Semantic = semantic; Texture = texture; SamplerSource = samplerSource; TargetMaterial = targetMaterial; PropertyNames = propertyNames; IsAtlasPage = isAtlasPage; }
        internal MaterialId MaterialId { get; }
        internal HumanoidPublishTextureSemantic Semantic { get; }
        internal Texture Texture { get; }
        internal Texture SamplerSource { get; }
        internal Material TargetMaterial { get; }
        internal IReadOnlyList<string> PropertyNames { get; }
        /// <summary>Gets whether this entry is a Spec18 page and therefore uses the fixed atlas importer policy.</summary>
        internal bool IsAtlasPage { get; }
        /// <summary>Gets the stable identity of the source Texture used by the staged output.</summary>
        internal string SourceTextureKey => HumanoidTexturePublishReadback.GetSourceTextureKey(Texture);
        /// <summary>Gets the source Texture identity together with its importer semantic.</summary>
        internal string OutputTextureKey => Semantic + ":" + SourceTextureKey;
    }

    /// <summary>Editor-only texture collection, GPU readback, and PNG importer configuration for Humanoid publish.</summary>
    internal static class HumanoidTexturePublishReadback
    {
        internal static Func<bool> AsyncGpuReadbackSupported = () => SystemInfo.supportsAsyncGPUReadback;
        internal static Func<Texture2D, byte[]> PngEncoder = ImageConversion.EncodeToPNG;
        internal static Func<RenderTexture, byte[]> ReadbackRgba32 = DefaultReadbackRgba32;
        internal static bool TryCollect(InMemoryHumanoidMesh mesh, out IReadOnlyList<HumanoidTextureReadbackEntry> entries, out StackMachineDiagnostic diagnostic)
        {
            entries = null;
            diagnostic = null;
            if (mesh == null || mesh.Mesh == null) return Reject("PublishMeshRequired", "Texture publish requires a completed in-memory Humanoid Mesh.", out diagnostic);
            if (mesh.Materials.Count != mesh.MaterialSlots.Count) return Reject("PublishMaterialSlotMismatch", "Texture publish requires one material slot mapping for every final material.", out diagnostic);

            var collected = new List<HumanoidTextureReadbackEntry>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var mappedPropertiesByTarget = new Dictionary<int, HashSet<string>>();
            var atlasTextureIds = new HashSet<int>();
            if (mesh.AtlasPages != null)
            {
                for (int i = 0; i < mesh.AtlasPages.Pages.Count; i++)
                {
                    RenderTexture atlasTexture = mesh.AtlasPages.Pages[i]?.Texture;
                    if (atlasTexture != null) atlasTextureIds.Add(atlasTexture.GetInstanceID());
                }
            }
            for (int i = 0; i < mesh.Materials.Count; i++)
            {
                Material target = mesh.Materials[i];
                HumanoidBuildMaterialSlot slot = mesh.MaterialSlots[i];
                if (target == null || slot.SourceMaterial == null || slot.Adapter == null || !slot.MaterialId.IsValid)
                    return Reject("PublishMaterialMappingInvalid", "Texture publish received an invalid final material slot mapping.", out diagnostic, slot.MaterialId.EntryId);
                if (!mappedPropertiesByTarget.TryGetValue(target.GetInstanceID(), out HashSet<string> mappedProperties))
                {
                    mappedProperties = new HashSet<string>(StringComparer.Ordinal);
                    mappedPropertiesByTarget.Add(target.GetInstanceID(), mappedProperties);
                }
                foreach (MaterialProxySemantic semantic in new[] { MaterialProxySemantic.BaseColorTexture, MaterialProxySemantic.NormalTexture })
                {
                    if (!TryGetSemantic(semantic, out HumanoidPublishTextureSemantic publishSemantic)) continue;
                    var properties = new List<string>();
                    if (!slot.Adapter.TryGetPublishTextureProperties(semantic, properties, out MaterialProxyDiagnostic adapterDiagnostic))
                        return Reject("PublishTexturePropertyMappingInvalid", adapterDiagnostic.message, out diagnostic, slot.MaterialId.EntryId);
                    for (int propertyIndex = 0; propertyIndex < properties.Count; propertyIndex++)
                    {
                        string propertyName = properties[propertyIndex];
                        int propertyId = Shader.PropertyToID(propertyName);
                        if (!target.HasProperty(propertyId)) return Reject("PublishTexturePropertyMissing", "Compiler material is missing an adapter-mapped texture property.", out diagnostic, slot.MaterialId.EntryId, PropertyDetail(propertyName));
                        mappedProperties.Add(propertyName);
                        Texture texture = target.GetTexture(propertyId);
                        if (texture == null || atlasTextureIds.Contains(texture.GetInstanceID())) continue;
                        if (!(texture is RenderTexture) && !(texture is Texture2D))
                            return Reject("PublishTextureTypeUnsupported", "Texture publish requires a Texture2D or compiler RenderTexture for an adapter-mapped property.", out diagnostic, slot.MaterialId.EntryId, PropertyDetail(propertyName, texture));
                        Texture samplerSource = slot.SourceMaterial.HasProperty(propertyId) ? slot.SourceMaterial.GetTexture(propertyId) : null;
                        string key = target.GetInstanceID() + ":" + slot.MaterialId.RegistryId + ":" + slot.MaterialId.EntryId + ":" + publishSemantic + ":" + texture.GetInstanceID();
                        if (!indices.TryGetValue(key, out int existingIndex))
                        {
                            indices.Add(key, collected.Count);
                            collected.Add(new HumanoidTextureReadbackEntry(slot.MaterialId, publishSemantic, texture, samplerSource, target, new[] { propertyName }));
                            continue;
                        }
                        HumanoidTextureReadbackEntry existing = collected[existingIndex];
                        var names = new List<string>(existing.PropertyNames);
                        if (!names.Contains(propertyName)) names.Add(propertyName);
                        collected[existingIndex] = new HumanoidTextureReadbackEntry(existing.MaterialId, existing.Semantic, existing.Texture, existing.SamplerSource, existing.TargetMaterial, names.ToArray(), existing.IsAtlasPage);
                    }
                }

                // The adapter owns semantic mappings, but a published Material may also carry
                // shader-specific maps such as MToon Matcap and Emission. Those references are
                // part of the Pure Humanoid output contract as well; leaving them on the clone
                // would make the output Prefab depend on the source Outfit asset folder.
                var allTextureProperties = new List<string>();
                target.GetTexturePropertyNames(allTextureProperties);
                for (int propertyIndex = 0; propertyIndex < allTextureProperties.Count; propertyIndex++)
                {
                    string propertyName = allTextureProperties[propertyIndex];
                    if (mappedProperties.Contains(propertyName)) continue;
                    Texture texture = target.GetTexture(propertyName);
                    if (texture == null || atlasTextureIds.Contains(texture.GetInstanceID())) continue;
                    if (!(texture is RenderTexture) && !(texture is Texture2D))
                        return Reject("PublishTextureTypeUnsupported", "Texture publish requires a Texture2D or compiler RenderTexture for a preserved material property.", out diagnostic, slot.MaterialId.EntryId, PropertyDetail(propertyName, texture));

                    int propertyId = Shader.PropertyToID(propertyName);
                    Texture samplerSource = slot.SourceMaterial.HasProperty(propertyId) ? slot.SourceMaterial.GetTexture(propertyId) : null;
                    int existingIndex = FindTargetTexture(collected, target, HumanoidPublishTextureSemantic.Preserved, texture);
                    if (existingIndex < 0)
                    {
                        collected.Add(new HumanoidTextureReadbackEntry(slot.MaterialId, HumanoidPublishTextureSemantic.Preserved, texture, samplerSource, target, propertyName));
                        continue;
                    }

                    HumanoidTextureReadbackEntry existing = collected[existingIndex];
                    var names = new List<string>(existing.PropertyNames);
                    if (!names.Contains(propertyName)) names.Add(propertyName);
                    collected[existingIndex] = new HumanoidTextureReadbackEntry(existing.MaterialId, existing.Semantic, existing.Texture, existing.SamplerSource ?? samplerSource, existing.TargetMaterial, names.ToArray(), existing.IsAtlasPage);
                }
            }
            entries = collected.AsReadOnly();
            return true;
        }

        internal static bool TryEncodePng(HumanoidTextureReadbackEntry entry, out byte[] png, out StackMachineDiagnostic diagnostic)
        {
            png = null;
            diagnostic = null;
            if (entry.Texture is Texture2D sourceTexture)
                return TryEncodeSourceTexturePng(sourceTexture, entry, out png, out diagnostic);

            if (!(entry.Texture is RenderTexture texture)) return Reject("PublishRenderTextureRequired", "Texture publish requires a compiler RenderTexture or a Texture2D source asset.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry));
            if (texture == null || !texture.IsCreated()) return Reject("PublishRenderTextureRequired", "Texture publish requires a created compiler RenderTexture.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
            if (texture.width <= 0 || texture.height <= 0) return Reject("PublishTextureExtentInvalid", "Texture publish requires a positive RenderTexture extent.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
            if (texture.graphicsFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat) return Reject("PublishTextureFormatInvalid", "Texture publish requires the compiler linear RGBAHalf RenderTexture format.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
            if (!AsyncGpuReadbackSupported()) return Reject("PublishGpuReadbackUnsupported", "Texture publish requires Async GPU readback; CPU fallback is not supported.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));

            byte[] data;
            try { data = ReadbackRgba32(texture); }
            catch (Exception exception) { return Reject("PublishGpuReadbackFailed", exception.Message, out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture)); }
            if (data == null) return Reject("PublishGpuReadbackFailed", "GPU readback failed for a compiler RenderTexture.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
            int expectedBytes = texture.width * texture.height * 4;
            if (data.Length != expectedBytes) return Reject("PublishGpuReadbackLengthInvalid", "GPU readback did not return an RGBA32 texture of the expected extent.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
            var encoded = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            try
            {
                encoded.LoadRawTextureData(data);
                encoded.Apply(false, false);
                if (entry.Semantic == HumanoidPublishTextureSemantic.BaseColor) EncodeLinearRgbToSrgb(encoded);
                png = PngEncoder(encoded);
                if (png == null || png.Length == 0) return Reject("PublishPngEncodeFailed", "PNG encoding produced no texture bytes.", out diagnostic, entry.MaterialId.EntryId);
                return true;
            }
            catch (Exception exception) { return Reject("PublishPngEncodeFailed", exception.Message, out diagnostic, entry.MaterialId.EntryId); }
            finally { UnityEngine.Object.DestroyImmediate(encoded); }
        }

        private static bool TryEncodeSourceTexturePng(Texture2D texture, HumanoidTextureReadbackEntry entry,
            out byte[] png, out StackMachineDiagnostic diagnostic)
        {
            png = null;
            diagnostic = null;
            if (texture == null) return Reject("PublishTextureSourceMissing", "Texture publish requires a Texture2D source.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry));
            if (!texture.isReadable)
            {
                // Importers commonly expose persistent Texture2D assets without
                // CPU-readable pixels. Read those through the GPU so the output can
                // still satisfy the PNG-only publish contract. Runtime/transient
                // textures remain an explicit reject because there is no stable source
                // asset whose pixels can be reproduced after the publish transaction.
                if (!IsPersistentTextureAsset(texture))
                    return Reject("PublishTextureSourceNotReadable", "A non-asset Texture2D must be readable before it can be published independently.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
                return TryEncodePersistentUnreadableTexturePng(texture, entry, out png, out diagnostic);
            }
            if (texture.width <= 0 || texture.height <= 0)
                return Reject("PublishTextureExtentInvalid", "Texture publish requires a positive Texture2D extent.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
            try
            {
                png = PngEncoder(texture);
                if (png == null || png.Length == 0) return Reject("PublishPngEncodeFailed", "PNG encoding produced no texture bytes.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture));
                return true;
            }
            catch (Exception exception) { return Reject("PublishPngEncodeFailed", exception.Message, out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, texture)); }
        }

        private static bool TryEncodePersistentUnreadableTexturePng(Texture2D source, HumanoidTextureReadbackEntry entry,
            out byte[] png, out StackMachineDiagnostic diagnostic)
        {
            png = null;
            diagnostic = null;
            if (source.width <= 0 || source.height <= 0)
                return Reject("PublishTextureExtentInvalid", "Texture publish requires a positive Texture2D extent.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, source));
            if (!AsyncGpuReadbackSupported())
                return Reject("PublishGpuReadbackUnsupported", "Publishing a non-readable persistent Texture2D requires Async GPU readback; CPU fallback is not supported.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, source));

            RenderTexture copy = null;
            Texture2D encoded = null;
            try
            {
                RenderTextureReadWrite readWrite = source.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
                copy = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, readWrite);
                Graphics.Blit(source, copy);
                byte[] data = ReadbackRgba32(copy);
                int expectedBytes = source.width * source.height * 4;
                if (data == null) return Reject("PublishGpuReadbackFailed", "GPU readback failed for a persistent source Texture2D.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, source));
                if (data.Length != expectedBytes) return Reject("PublishGpuReadbackLengthInvalid", "GPU readback did not return an RGBA32 texture of the expected extent.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, source));

                // The readback bytes are already in the source texture's color
                // encoding because the temporary target uses the matching sRGB
                // policy. Keep the encoded Texture2D's data flag aligned with it;
                // EncodeToPNG then writes those bytes without another conversion.
                encoded = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, !source.isDataSRGB);
                encoded.LoadRawTextureData(data);
                encoded.Apply(false, false);
                png = PngEncoder(encoded);
                if (png == null || png.Length == 0) return Reject("PublishPngEncodeFailed", "PNG encoding produced no texture bytes.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, source));
                return true;
            }
            catch (Exception exception)
            {
                return Reject("PublishGpuReadbackFailed", exception.Message, out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry, source));
            }
            finally
            {
                if (encoded != null) UnityEngine.Object.DestroyImmediate(encoded);
                if (copy != null) RenderTexture.ReleaseTemporary(copy);
            }
        }

        private static bool IsPersistentTextureAsset(Texture2D texture)
        {
            string assetPath = AssetDatabase.GetAssetPath(texture);
            return !string.IsNullOrWhiteSpace(assetPath);
        }

        internal static bool TryConfigureImporter(string assetPath, HumanoidTextureReadbackEntry entry, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(assetPath)) return Reject("PublishTextureAssetPathRequired", "Texture publish requires an imported PNG asset path.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry));
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer)) return Reject("PublishTextureImporterMissing", "Texture publish could not load the PNG TextureImporter.", out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry));
            importer.textureType = TextureImporterType.Default;
            if (entry.Semantic == HumanoidPublishTextureSemantic.Preserved)
            {
                string propertyDetail = PropertyDetail(entry.PropertyNames.Count > 0 ? entry.PropertyNames[0] : "<none>");
                if (!(entry.SamplerSource is Texture2D sourceTexture))
                    return Reject("PublishPreservedTextureImporterMissing", "Preserved texture publish requires a source Texture2D so its color semantics can be retained.", out diagnostic, entry.MaterialId.EntryId, propertyDetail);
                string sourcePath = AssetDatabase.GetAssetPath(sourceTexture);
                if (AssetImporter.GetAtPath(sourcePath) is TextureImporter sourceImporter)
                {
                    importer.sRGBTexture = sourceImporter.sRGBTexture;
                    importer.alphaIsTransparency = sourceImporter.alphaIsTransparency;
                }
                else
                {
                    // Imported VRM/GLTF textures are often Texture2D sub-assets. They
                    // share the container path and therefore have no TextureImporter of
                    // their own; Texture.isDataSRGB is the importer result available on
                    // the sub-asset. Alpha transparency dilation is not recoverable from
                    // a sub-asset, so leave it disabled rather than altering pixel edges.
                    importer.sRGBTexture = sourceTexture.isDataSRGB;
                    importer.alphaIsTransparency = false;
                }
            }
            else
            {
                importer.sRGBTexture = entry.Semantic == HumanoidPublishTextureSemantic.BaseColor;
                importer.alphaIsTransparency = entry.Semantic == HumanoidPublishTextureSemantic.BaseColor;
            }
            if (entry.IsAtlasPage)
            {
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 1;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.compressionQuality = 50;
                importer.crunchedCompression = false;
                importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(entry.Texture.width, entry.Texture.height));
            }
            else
            {
                importer.mipmapEnabled = entry.SamplerSource is Texture2D source2D && source2D.mipmapCount > 1;
                if (entry.SamplerSource != null)
                {
                    importer.wrapMode = entry.SamplerSource.wrapMode;
                    importer.filterMode = entry.SamplerSource.filterMode;
                    importer.anisoLevel = entry.SamplerSource.anisoLevel;
                }
            }
            try { importer.SaveAndReimport(); }
            catch (Exception exception) { return Reject("PublishTextureImporterFailed", exception.Message, out diagnostic, entry.MaterialId.EntryId, EntryPropertyDetail(entry)); }
            return true;
        }

        private static bool TryGetSemantic(MaterialPropertyValueSource valueSource, out HumanoidPublishTextureSemantic semantic)
        {
            if (valueSource == MaterialPropertyValueSource.BaseColorTexture) { semantic = HumanoidPublishTextureSemantic.BaseColor; return true; }
            if (valueSource == MaterialPropertyValueSource.NormalTexture) { semantic = HumanoidPublishTextureSemantic.Normal; return true; }
            semantic = default;
            return false;
        }

        private static bool TryGetSemantic(MaterialProxySemantic valueSource, out HumanoidPublishTextureSemantic semantic)
        {
            if (valueSource == MaterialProxySemantic.BaseColorTexture) { semantic = HumanoidPublishTextureSemantic.BaseColor; return true; }
            if (valueSource == MaterialProxySemantic.NormalTexture) { semantic = HumanoidPublishTextureSemantic.Normal; return true; }
            semantic = default;
            return false;
        }

        internal static string GetSourceTextureKey(Texture texture)
        {
            if (texture == null) return "none";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId))
                return "asset:" + guid + ":" + localId;
            return "runtime:" + texture.GetInstanceID();
        }

        private static int FindTargetTexture(IReadOnlyList<HumanoidTextureReadbackEntry> entries, Material target, HumanoidPublishTextureSemantic semantic, Texture texture)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                HumanoidTextureReadbackEntry entry = entries[i];
                if (entry.TargetMaterial == target && entry.Semantic == semantic && entry.Texture == texture) return i;
            }
            return -1;
        }

        private static void EncodeLinearRgbToSrgb(Texture2D texture)
        {
            NativeArray<Color32> pixels = texture.GetRawTextureData<Color32>();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color linear = pixels[i];
                pixels[i] = new Color(Mathf.LinearToGammaSpace(linear.r), Mathf.LinearToGammaSpace(linear.g), Mathf.LinearToGammaSpace(linear.b), linear.a);
            }
            texture.Apply(false, false);
        }

        private static byte[] DefaultReadbackRgba32(RenderTexture texture)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
            request.WaitForCompletion();
            return request.hasError ? null : request.GetData<byte>().ToArray();
        }

        private static string PropertyDetail(string propertyName, Texture texture = null)
        {
            return "property=" + (propertyName ?? string.Empty) + (texture == null ? string.Empty : ";type=" + texture.GetType().Name);
        }

        private static string EntryPropertyDetail(HumanoidTextureReadbackEntry entry, Texture texture = null)
        {
            return PropertyDetail(entry.PropertyNames.Count > 0 ? entry.PropertyNames[0] : "<none>", texture);
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string bindingName = null, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message, bindingName: bindingName, detail: detail);
            return false;
        }
    }
}
