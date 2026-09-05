// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Publishes the static Base payload of Mesh Outfits from Database-owned authoring Prefabs.</summary>
    /// <remarks>Runtime components are existing components only.  Registry objects and authoring owners never leave the Database.</remarks>
    internal static class ShapeSyncOutfitGenerator
    {
        private static ICollection<string> generatedPathSink;

        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath, string bindingsPath, string outfitsPath, out string diagnostic)
            => TryGenerate(database, rootPath, bindingsPath, outfitsPath, null, out diagnostic);

        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath, string bindingsPath, string outfitsPath, ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            if (database?.Registry == null) { diagnostic = "OutfitGenerateSnapshotInvalid: Database Registry is required."; return false; }
            if (string.IsNullOrWhiteSpace(rootPath) || !rootPath.StartsWith("Assets/", StringComparison.Ordinal))
            { diagnostic = "OutfitGenerateOutputPathInvalid: Output root must be an Assets path."; return false; }
            string outputPath = rootPath.TrimEnd('/');
            string normalizedOutfitsPath = (outfitsPath ?? string.Empty).Trim('/');
            if (!string.IsNullOrWhiteSpace(normalizedOutfitsPath)) outputPath += "/" + normalizedOutfitsPath;
            generatedPathSink = generatedPaths;
            try
            {
                // Validate every authoring declaration before creating or mutating any output
                // asset.  Generation is synchronous, so a complete preflight is the rollback
                // boundary for malformed Database state; no partial outfit folder is left on
                // an input-contract rejection.
                ValidateSnapshot(database, rootPath, outputPath);
                EnsureFolder(outputPath);
                var generatedNormals = new List<GeneratedNormal>();
                foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in database.Registry.Outfits.Where(value => value != null))
                {
                    if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Material)
                    {
                        // A Material Outfit has no generated mesh Prefab by contract. Its Texture
                        // logical resources are published by the Figure MaterialBinding transfer;
                        // owner remains authoring-only and is not emitted into runtime output.
                        continue;
                    }
                    ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures
                        .FirstOrDefault(axis => axis != null && axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                    if (baseAxis?.OutfitPrefab == null)
                    { generatedPathSink = null; diagnostic = "OutfitGenerateSnapshotInvalid: Mesh Outfit requires a Base Outfit Prefab: " + outfit.Identity; return false; }
                    GameObject instance = UnityEngine.Object.Instantiate(baseAxis.OutfitPrefab);
                    try
                    {
                        instance.name = outfit.Identity;
                        ShapeSyncOutfit runtimeOutfit = instance.GetComponent<ShapeSyncOutfit>() ?? instance.AddComponent<ShapeSyncOutfit>();
                        BuilderRuntimeComponentSetup.Ensure(instance);
                        RemoveGeneratedOutfitSourceArtifacts(instance);
                        OutfitSkinningProfile skinningProfile = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
                        skinningProfile.name = outfit.Identity + "_SkinningProfile";
                        string profilePath = outputPath + "/" + outfit.Identity + "_SkinningProfile.asset";
                        BuildMeshesAndSkinningProfile(instance, outfit, outputPath, skinningProfile);
                        skinningProfile = Persist(skinningProfile, profilePath);
                        ConfigureMaterialProxy(instance, outfit, outputPath);
                        ConfigureNormalBlender(instance, outfit, generatedNormals);
                        ConfigureExtraBoneRegistries(database, runtimeOutfit, outfit, outputPath);
                        CollectionProfiles collectionProfiles = ConfigureCollectionBoneProfiles(database, runtimeOutfit, outfit, outputPath);
                        if (outfit.CollectionKind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full)
                            ConfigureCollectionPcmPayload(database, runtimeOutfit, outfit, collectionProfiles, rootPath, outputPath);
                        SerializedObject serialized = new SerializedObject(runtimeOutfit);
                        serialized.FindProperty("registryId").stringValue = outfit.Identity;
                        serialized.FindProperty("registryName").stringValue = outfit.DisplayName;
                        serialized.FindProperty("skinningProfile").objectReferenceValue = skinningProfile;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        string prefabPath = outputPath + "/" + outfit.Identity + ".prefab";
                        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool saved);
                        if (!saved) { generatedPathSink = null; diagnostic = "OutfitGeneratePrefabSaveFailed: " + outfit.Identity; return false; }
                        generatedPathSink?.Add(prefabPath);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
                ConfigureFigureNormalBindings(database, rootPath, bindingsPath, generatedNormals);
                ConfigureFigureOutfitBindings(database, rootPath, bindingsPath, outputPath);
                AssetDatabase.SaveAssets();
                generatedPathSink = null;
                return true;
            }
            catch (Exception exception) { generatedPathSink = null; diagnostic = "OutfitGenerateUnexpected: " + exception.Message; return false; }
        }

        private static void ConfigureFigureOutfitBindings(ShapeSyncDatabase database, string rootPath, string bindingsPath, string outputPath)
        {
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry figure, out string figureDiagnostic))
                throw new InvalidOperationException("OutfitGenerateBaseFigureInvalid: " + figureDiagnostic);
            if (figure == null || string.IsNullOrWhiteSpace(figure.Name)) return;

            string bindingPath = rootPath.TrimEnd('/') + "/" + (bindingsPath ?? string.Empty).Trim('/') + "/" + figure.Name + "_MeshBinding.asset";
            MeshBinding binding = AssetDatabase.LoadAssetAtPath<MeshBinding>(bindingPath);
            // Direct OutfitGenerator tests may intentionally run without the preceding
            // Figure generation. The normal Generate pipeline creates this binding first.
            if (binding == null) return;

            var generated = database.Registry.Outfits
                .Where(outfit => outfit != null && outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh)
                .OrderBy(outfit => outfit.Identity, StringComparer.Ordinal)
                .Select(outfit => new
                {
                    outfit.Identity,
                    Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath.TrimEnd('/') + "/" + outfit.Identity + ".prefab")
                }).ToArray();
            if (generated.Any(value => value.Prefab == null))
                throw new InvalidOperationException("OutfitGenerateBindingPrefabMissing: Generated Outfit prefab could not be resolved.");

            SerializedObject serialized = new SerializedObject(binding);
            SerializedProperty entries = serialized.FindProperty("outfits");
            entries.arraySize = generated.Length;
            for (int index = 0; index < generated.Length; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("logicalName").stringValue = generated[index].Identity;
                entry.FindPropertyRelative("outfitPrefab").objectReferenceValue = generated[index].Prefab;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(binding);
            AssetDatabase.SaveAssetIfDirty(binding);
        }

        private static void RemoveGeneratedOutfitSourceArtifacts(GameObject output)
        {
            if (output == null) return;

            // Generated Outfit assets contain the resolved mesh and the extra-bone hierarchy,
            // not the source VRM authoring payload.  Remove VRM/UniHumanoid components first;
            // this also removes the source Avatar and Vrm10Instance references before the
            // prefab is serialized.
            var colliderRoots = new HashSet<GameObject>();
            // Re-scan after each destruction pass. Some UniVRM authoring components own
            // editor-time companions, so a single mutable component array can leave a
            // surviving component on the instantiated prefab.
            var deferredHumanoids = new List<Component>();
            for (int pass = 0; pass < 3; pass++)
            {
                bool removed = false;
                Component[] components = output.GetComponentsInChildren<Component>(true);
                foreach (Component component in components)
                {
                    if (component == null || component is Transform) continue;
                    string fullName = component.GetType().FullName ?? string.Empty;
                    bool isVrmComponent = fullName.StartsWith("VRM.", StringComparison.Ordinal)
                        || fullName.StartsWith("VRM10.", StringComparison.Ordinal)
                        || fullName.StartsWith("UniVRM.", StringComparison.Ordinal)
                        || fullName.StartsWith("UniVRM10.", StringComparison.Ordinal)
                        || string.Equals(fullName, "UniHumanoid.Humanoid", StringComparison.Ordinal);
                    bool isVrmCollider = fullName.IndexOf("SpringBoneCollider", StringComparison.OrdinalIgnoreCase) >= 0
                        || fullName.IndexOf("SpringBoneColliderGroup", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isVrmCollider) colliderRoots.Add(component.gameObject);
                    bool isHumanoid = string.Equals(fullName, "UniHumanoid.Humanoid", StringComparison.Ordinal);
                    // Import preserves Animator/Avatar on the source prefab, but an emitted
                    // Outfit shares the Figure's Animator/Avatar and must not carry an
                    // independent authoring Animator or an external Avatar reference.
                    bool isAnimator = component is Animator;
                    if (isVrmComponent && isHumanoid)
                    {
                        deferredHumanoids.Add(component);
                    }
                    else if (isAnimator || isVrmComponent)
                    {
                        UnityEngine.Object.DestroyImmediate(component, true);
                        removed = true;
                    }
                }
                if (!removed) break;
            }
            // Vrm10Instance has a component dependency on Humanoid. Destroy all other
            // VRM authoring components first, then remove the dependency target itself.
            foreach (Component humanoid in deferredHumanoids)
            {
                if (humanoid != null) UnityEngine.Object.DestroyImmediate(humanoid, true);
            }

            // Collider groups outside the preserved skeleton are source-only GameObjects and
            // can be removed as complete hierarchies.  A collider component attached directly
            // to a bone (common for VRM spring-bone authoring) must not remove that bone: the
            // Extra Bone Registry still requires the transform to exist in the generated Outfit.
            Transform preservedSkeleton = output.transform.Find("Root");
            foreach (GameObject colliderRoot in colliderRoots)
            {
                if (colliderRoot == null || colliderRoot == output) continue;
                if (preservedSkeleton != null && (colliderRoot.transform == preservedSkeleton || colliderRoot.transform.IsChildOf(preservedSkeleton))) continue;
                UnityEngine.Object.DestroyImmediate(colliderRoot);
            }

            // Material/mesh merge containers (for example Hair, Face, Body) have no renderer
            // or child bone after the merged renderer is created.  They are not part of the
            // generated Outfit hierarchy. Restrict cleanup to direct children so leaf bones
            // in the preserved Root hierarchy are never removed.
            for (int index = output.transform.childCount - 1; index >= 0; index--)
            {
                Transform child = output.transform.GetChild(index);
                if (child == null || child.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0) continue;
                if (child.GetComponentsInChildren<Transform>(true).Length == 1)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void ValidateSnapshot(ShapeSyncDatabase database, string rootPath, string outputPath)
        {
            foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in database.Registry.Outfits.Where(value => value != null))
            {
                if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Material) continue;
                ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures
                    .FirstOrDefault(axis => axis != null && axis.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                if (baseAxis?.OutfitPrefab == null)
                    throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Mesh Outfit requires a Base Outfit Prefab: " + outfit.Identity);
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(value => value != null))
                {
                    if (axis.OutfitPrefab == null) throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Missing Outfit axis Prefab: " + outfit.Identity + "/" + axis.ShapeKey);
                    if (!TryResolveStructuralRenderers(baseAxis.OutfitPrefab, axis.OutfitPrefab, out _, out string rendererDiagnostic))
                        throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Incompatible Outfit axis renderer: " + outfit.Identity + "/" + axis.ShapeKey + "/" + rendererDiagnostic);
                }
                if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry savedFigure, out string savedFigureDiagnostic)
                    || savedFigure?.Figure == null)
                    throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Base Figure is required: " + outfit.Identity);
                if (outfit.CollectionKind != ShapeSyncDatabaseRegistry.OutfitCollectionKind.None)
                {
                    foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry collection in outfit.CollectionEntries.Where(value => value != null))
                    {
                        if (collection.CollectionPrefab == null) throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Missing Collection Prefab: " + outfit.Identity + "/" + collection.ShapeKey);
                        ShapeSyncHumanoidBoneCorrectionProfile profile = BuildCollectionBoneProfile(ResolveFigureForShape(database.Registry, collection.ShapeKey), collection.CollectionPrefab);
                        UnityEngine.Object.DestroyImmediate(profile);
                    }
                    if (outfit.CollectionKind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full)
                    {
                        if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry figureEntry, out string figureDiagnostic)
                            || figureEntry == null)
                            throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Base Figure is required: " + outfit.Identity + "; " + figureDiagnostic);
                        if (figureEntry == null || AssetDatabase.LoadAssetAtPath<GameObject>(rootPath.TrimEnd('/') + "/" + figureEntry.Name + ".prefab") == null)
                            throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Full Collection requires a generated Figure output: " + outfit.Identity);
                    }
                }
                foreach (ShapeSyncDatabaseRegistry.OutfitNormalEntry normal in outfit.NormalEntries.Where(value => value != null))
                    if (!(normal.Texture is Texture2D) || string.IsNullOrWhiteSpace(normal.TextureResourceName))
                        throw new InvalidOperationException("OutfitGenerateSnapshotInvalid: Normal Texture is incomplete: " + outfit.Identity + "/" + normal.MaterialEntryName + "/" + normal.ShapeKey);
            }
        }

        private static void BuildMeshesAndSkinningProfile(GameObject output, ShapeSyncDatabaseRegistry.OutfitEntry outfit, string folder, OutfitSkinningProfile profile)
        {
            var profiles = new List<OutfitSkinningRendererProfile>();
            SkinnedMeshRenderer[] baseRenderers = output.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (int rendererIndex in Enumerable.Range(0, baseRenderers.Length))
            {
                SkinnedMeshRenderer renderer = baseRenderers[rendererIndex];
                string path = RelativePath(output.transform, renderer.transform);
                Mesh mesh = ShapeSyncMeshCloneUtility.Clone(renderer.sharedMesh);
                mesh.name = output.name + (string.IsNullOrEmpty(path) ? "_Mesh" : "_" + path.Replace('/', '_') + "_Mesh");
                mesh.ClearBlendShapes();
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(value => value != null && value.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey))
                {
                    if (!TryResolveStructuralRenderers(output, axis.OutfitPrefab, out SkinnedMeshRenderer[] targets, out string rendererDiagnostic))
                        throw new InvalidOperationException("Outfit FBM renderer is incompatible: " + outfit.Identity + "/" + axis.ShapeKey + "/" + rendererDiagnostic);
                    SkinnedMeshRenderer target = targets[rendererIndex];
                    if (!TryBuildVertexDelta(renderer.sharedMesh, target.sharedMesh, out Vector3[] delta, out string deltaDiagnostic))
                        throw new InvalidOperationException("Outfit FBM vertex mapping is incompatible: " + outfit.Identity + "/" + axis.ShapeKey + "/" + deltaDiagnostic);
                    BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(mesh, axis.ShapeKey, delta, null, null);
                }
                foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow in outfit.PbmFollows.Where(value => value != null))
                {
                    foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry source in follow.Figures.Where(value => value != null))
                    {
                        if (!TryResolveStructuralRenderers(output, source.Figure, out SkinnedMeshRenderer[] targets, out string rendererDiagnostic))
                            throw new InvalidOperationException("Outfit PBM renderer is incompatible: " + outfit.Identity + "/" + follow.PbmAxisName + "/" + source.ShapeKey + "/" + rendererDiagnostic);
                        SkinnedMeshRenderer target = targets[rendererIndex];
                        Vector3[] delta;
                        string deltaDiagnostic;
                        if (source.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
                        {
                            if (!TryBuildVertexDelta(renderer.sharedMesh, target.sharedMesh, out delta, out deltaDiagnostic))
                                throw new InvalidOperationException("Outfit PBM vertex mapping is incompatible: " + outfit.Identity + "/" + follow.PbmAxisName + "/" + source.ShapeKey + "/" + deltaDiagnostic);
                        }
                        else
                        {
                            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry fbmAxis = outfit.AxisFigures
                                .SingleOrDefault(axis => axis != null && axis.ShapeKey == source.ShapeKey);
                            string fbmRendererDiagnostic = null;
                            if (fbmAxis == null || !TryResolveStructuralRenderers(output, fbmAxis.OutfitPrefab,
                                    out SkinnedMeshRenderer[] fbmTargets, out fbmRendererDiagnostic))
                                throw new InvalidOperationException("Outfit PBM FBM source is incompatible: " + outfit.Identity + "/" + source.ShapeKey + "/" + (fbmRendererDiagnostic ?? "Missing"));
                            ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry baseSource = follow.Figures
                                .SingleOrDefault(value => value != null && value.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                            string basePbmRendererDiagnostic = null;
                            if (baseSource?.Figure == null || !TryResolveStructuralRenderers(output, baseSource.Figure,
                                    out SkinnedMeshRenderer[] basePbmTargets, out basePbmRendererDiagnostic))
                                throw new InvalidOperationException("Outfit PBM Base source is incompatible: " + outfit.Identity + "/" + follow.PbmAxisName + "/" + (basePbmRendererDiagnostic ?? "Missing"));
                            if (!TryBuildPbmDifferenceDelta(renderer.sharedMesh, target.sharedMesh, fbmTargets[rendererIndex].sharedMesh,
                                    basePbmTargets[rendererIndex].sharedMesh,
                                    out delta, out deltaDiagnostic))
                                throw new InvalidOperationException("Outfit PBM difference mapping is incompatible: " + outfit.Identity + "/" + follow.PbmAxisName + "/" + source.ShapeKey + "/" + deltaDiagnostic);
                        }
                        string targetName = source.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey
                            ? BlendShapeReservedPrefixes.Pbm + follow.PbmAxisName
                            : BlendShapeReservedPrefixes.Pbm + source.ShapeKey + "_" + follow.PbmAxisName;
                        BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(mesh, targetName, delta, null, null);
                    }
                }
                Mesh persistedMesh = Persist(mesh, folder + "/" + mesh.name + ".asset");
                renderer.sharedMesh = persistedMesh;
                var rendererProfile = new OutfitSkinningRendererProfile { rendererPath = path, baseBindposes = (Matrix4x4[])persistedMesh.bindposes.Clone() };
                foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(value => value != null && value.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey))
                {
                    if (!TryResolveStructuralRenderers(output, axis.OutfitPrefab, out SkinnedMeshRenderer[] targets, out string rendererDiagnostic))
                        throw new InvalidOperationException("Outfit FBM renderer is incompatible: " + outfit.Identity + "/" + axis.ShapeKey + "/" + rendererDiagnostic);
                    rendererProfile.fbmBindposes.Add(new OutfitSkinningFbmBindposes { blendName = axis.ShapeKey, bindposes = (Matrix4x4[])targets[rendererIndex].sharedMesh.bindposes.Clone() });
                }
                profiles.Add(rendererProfile);
            }
            profile.SetRendererProfiles(profiles);
        }

        private static bool TryBuildPbmDifferenceDelta(Mesh baseMesh, Mesh combinedMesh, Mesh fbmMesh, Mesh basePbmMesh,
            out Vector3[] delta, out string diagnostic)
        {
            delta = null;
            diagnostic = null;
            if (!TryBuildVertexDelta(baseMesh, combinedMesh, out Vector3[] combinedDelta, out diagnostic))
                return false;
            if (!TryBuildVertexDelta(baseMesh, fbmMesh, out Vector3[] fbmDelta, out string fbmDiagnostic))
            {
                diagnostic = "Fbm/" + fbmDiagnostic;
                return false;
            }
            if (!TryBuildVertexDelta(baseMesh, basePbmMesh, out Vector3[] basePbmDelta, out string basePbmDiagnostic))
            {
                diagnostic = "BasePbm/" + basePbmDiagnostic;
                return false;
            }
            delta = new Vector3[combinedDelta.Length];
            for (int index = 0; index < delta.Length; index++)
                delta[index] = combinedDelta[index] - basePbmDelta[index] - fbmDelta[index];
            return true;
        }

        private static bool TryResolveStructuralRenderers(GameObject basePrefab, GameObject targetPrefab, out SkinnedMeshRenderer[] targetRenderers, out string diagnostic)
        {
            targetRenderers = null;
            diagnostic = null;
            if (basePrefab == null || targetPrefab == null)
            {
                diagnostic = "PrefabMissing";
                return false;
            }
            SkinnedMeshRenderer[] baseRenderers = basePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            targetRenderers = targetPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (targetRenderers.Length != baseRenderers.Length)
            {
                diagnostic = "RendererCount";
                return false;
            }
            for (int index = 0; index < baseRenderers.Length; index++)
            {
                SkinnedMeshRenderer source = baseRenderers[index];
                SkinnedMeshRenderer target = targetRenderers[index];
                if (source == null || target == null || source.sharedMesh == null || target.sharedMesh == null)
                {
                    diagnostic = "RendererMeshMissing/" + index;
                    return false;
                }
                if (!TryValidateMeshStructure(source.sharedMesh, target.sharedMesh, out string meshDiagnostic))
                {
                    diagnostic = meshDiagnostic + "/" + index;
                    return false;
                }
                if (source.sharedMaterials.Length != target.sharedMaterials.Length)
                {
                    diagnostic = "MaterialSlotCount/" + index + "/expected=" + source.sharedMaterials.Length + "/actual=" + target.sharedMaterials.Length;
                    return false;
                }
            }
            return true;
        }

        private static bool TryValidateMeshStructure(Mesh source, Mesh target, out string diagnostic)
        {
            diagnostic = null;
            if (source.subMeshCount != target.subMeshCount)
            {
                diagnostic = "SubMeshCount/expected=" + source.subMeshCount + "/actual=" + target.subMeshCount;
                return false;
            }
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                SubMeshDescriptor sourceDescriptor = source.GetSubMesh(submesh);
                SubMeshDescriptor targetDescriptor = target.GetSubMesh(submesh);
                if (sourceDescriptor.topology != targetDescriptor.topology || sourceDescriptor.indexCount != targetDescriptor.indexCount || sourceDescriptor.vertexCount != targetDescriptor.vertexCount)
                {
                    diagnostic = "SubMeshStructure/" + submesh;
                    return false;
                }
                if (!source.GetIndices(submesh).SequenceEqual(target.GetIndices(submesh)))
                {
                    diagnostic = "SubMeshIndices/" + submesh;
                    return false;
                }
            }
            return true;
        }

        private static bool TryBuildVertexDelta(Mesh source, Mesh target, out Vector3[] delta, out string diagnostic)
        {
            delta = null;
            if (!TryValidateMeshStructure(source, target, out diagnostic)) return false;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] targetVertices = target.vertices;
            delta = new Vector3[sourceVertices.Length];
            bool[] mapped = new bool[sourceVertices.Length];
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                SubMeshDescriptor sourceDescriptor = source.GetSubMesh(submesh);
                SubMeshDescriptor targetDescriptor = target.GetSubMesh(submesh);
                for (int local = 0; local < sourceDescriptor.vertexCount; local++)
                {
                    int sourceIndex = sourceDescriptor.firstVertex + local;
                    int targetIndex = targetDescriptor.firstVertex + local;
                    if (sourceIndex < 0 || sourceIndex >= sourceVertices.Length || targetIndex < 0 || targetIndex >= targetVertices.Length)
                    {
                        diagnostic = "VertexRange/" + submesh;
                        delta = null;
                        return false;
                    }
                    delta[sourceIndex] = targetVertices[targetIndex] - sourceVertices[sourceIndex];
                    mapped[sourceIndex] = true;
                }
            }
            return true;
        }

        private static void ConfigureMaterialProxy(GameObject output, ShapeSyncDatabaseRegistry.OutfitEntry outfit, string folder)
        {
            MaterialProxy proxy = output.GetComponent<MaterialProxy>();
            if (proxy == null) throw new InvalidOperationException("OutfitGenerateMaterialProxyMissing: Builder runtime setup did not attach MaterialProxy.");
            SerializedObject serialized = new SerializedObject(proxy);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = 0;
            int entryIndex = 0;
            var copiedTextures = new Dictionary<Texture, Texture2D>();
            var copiedAdapters = new Dictionary<Shader, MaterialShaderAdapter>();
            foreach (ShapeSyncDatabaseRegistry.OutfitMaterialEntry entry in outfit.MaterialEntries.Where(value => value != null))
            {
                bool assigned = false;
                foreach (SkinnedMeshRenderer renderer in output.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int channel = 0; channel < materials.Length; channel++)
                    {
                        if (materials[channel] != entry.Material) continue;
                        if (entry.Adapter == null || entry.Material.shader == null) throw new InvalidOperationException("OutfitGenerateMaterialAdapterMissing: " + entry.LogicalName);
                        Material copy = UnityEngine.Object.Instantiate(entry.Material);
                        copy.name = output.name + "_" + entry.LogicalName + "_Material";
                        foreach (string property in copy.GetTexturePropertyNames())
                        {
                            if (!(copy.GetTexture(property) is Texture2D texture)) continue;
                            if (!copiedTextures.TryGetValue(texture, out Texture2D textureCopy))
                            {
                                textureCopy = UnityEngine.Object.Instantiate(texture);
                                textureCopy.name = output.name + "_" + entry.LogicalName + "_" + texture.name;
                                textureCopy = Persist(textureCopy, folder + "/" + textureCopy.name + ".asset");
                                copiedTextures.Add(texture, textureCopy);
                            }
                            copy.SetTexture(property, textureCopy);
                        }
                        materials[channel] = Persist(copy, folder + "/" + copy.name + ".asset");
                        renderer.sharedMaterials = materials;
                        if (!copiedAdapters.TryGetValue(entry.Material.shader, out MaterialShaderAdapter adapter))
                        {
                            adapter = UnityEngine.Object.Instantiate(entry.Adapter);
                            adapter.name = output.name + "_" + entry.Material.shader.name.Replace('/', '_') + "_Adapter";
                            adapter = Persist(adapter, folder + "/" + adapter.name + ".asset");
                            copiedAdapters.Add(entry.Material.shader, adapter);
                        }
                        entries.arraySize = entryIndex + 1;
                        SerializedProperty proxyEntry = entries.GetArrayElementAtIndex(entryIndex++);
                        proxyEntry.FindPropertyRelative("entryName").stringValue = entry.LogicalName;
                        proxyEntry.FindPropertyRelative("renderer").objectReferenceValue = renderer;
                        proxyEntry.FindPropertyRelative("materialChannel").intValue = channel;
                        proxyEntry.FindPropertyRelative("adapter").objectReferenceValue = adapter;
                        assigned = true;
                        break;
                    }
                    if (assigned) break;
                }
                if (!assigned) throw new InvalidOperationException("Outfit Material Entry could not resolve an output renderer channel: " + entry.LogicalName);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Persist<T>(T asset, string path) where T : UnityEngine.Object
        {
            generatedPathSink?.Add(path);
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            // CopySerialized is not safe for Mesh native shape buffers: after an
            // overwrite the YAML can contain the new sparse shape records while Unity
            // still loads zero frame payloads. Copy the Mesh through its public data
            // API instead, keeping the existing asset object (and therefore GUID).
            if (asset is Mesh sourceMesh && existing is Mesh existingMesh)
            {
                CopyMeshData(sourceMesh, existingMesh);
                EditorUtility.SetDirty(existingMesh);
                UnityEngine.Object.DestroyImmediate(asset);
                return existing;
            }
            EditorUtility.CopySerialized(asset, existing);
            // AssetDatabase may return the same object when a caller reuses an already
            // persisted reference. Never destroy the object that is now the canonical
            // output asset; doing so invalidates prefab references on overwrite Generate.
            if (asset != existing) UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        private static void CopyMeshData(Mesh source, Mesh destination)
        {
            destination.Clear(false);
            destination.indexFormat = source.indexFormat;
            destination.vertices = source.vertices;
            destination.normals = source.normals;
            destination.tangents = source.tangents;
            destination.colors = source.colors;
            destination.colors32 = source.colors32;
            destination.boneWeights = source.boneWeights;
            destination.bindposes = source.bindposes;
            for (int channel = 0; channel < 8; channel++)
            {
                var uvs = new List<Vector4>();
                source.GetUVs(channel, uvs);
                destination.SetUVs(channel, uvs);
            }
            destination.subMeshCount = source.subMeshCount;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                SubMeshDescriptor descriptor = source.GetSubMesh(submesh);
                destination.SetIndices(source.GetIndices(submesh), descriptor.topology, submesh, false, descriptor.baseVertex);
                destination.SetSubMesh(submesh, descriptor, MeshUpdateFlags.DontRecalculateBounds);
            }
            destination.bounds = source.bounds;
            destination.ClearBlendShapes();
            for (int blendShape = 0; blendShape < source.blendShapeCount; blendShape++)
            {
                string blendShapeName = source.GetBlendShapeName(blendShape);
                int frameCount = source.GetBlendShapeFrameCount(blendShape);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    Vector3[] vertices = new Vector3[source.vertexCount];
                    Vector3[] normals = new Vector3[source.vertexCount];
                    Vector3[] tangents = new Vector3[source.vertexCount];
                    source.GetBlendShapeFrameVertices(blendShape, frame, vertices, normals, tangents);
                    destination.AddBlendShapeFrame(blendShapeName,
                        source.GetBlendShapeFrameWeight(blendShape, frame), vertices, normals, tangents);
                }
            }
        }

        private static void ConfigureNormalBlender(GameObject output, ShapeSyncDatabaseRegistry.OutfitEntry outfit, ICollection<GeneratedNormal> generatedNormals)
        {
            NormalBlender blender = output.GetComponent<NormalBlender>();
            if (blender == null) throw new InvalidOperationException("OutfitGenerateNormalBlenderMissing: Builder runtime setup did not attach NormalBlender.");
            ShapeSyncDatabaseRegistry.OutfitNormalEntry[] normals = outfit.NormalEntries.Where(value => value != null).ToArray();
            SerializedObject serialized = new SerializedObject(blender);
            SerializedProperty entries = serialized.FindProperty("entries");
            string[] names = normals.Select(value => value.MaterialEntryName).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            entries.arraySize = names.Length;
            for (int index = 0; index < names.Length; index++) entries.GetArrayElementAtIndex(index).stringValue = names[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            foreach (ShapeSyncDatabaseRegistry.OutfitNormalEntry normal in normals)
            {
                if (!(normal.Texture is Texture2D) || string.IsNullOrWhiteSpace(normal.TextureResourceName))
                    throw new InvalidOperationException("OutfitGenerateNormalTextureInvalid: " + outfit.Identity + "/" + normal.MaterialEntryName + "/" + normal.ShapeKey);
                generatedNormals.Add(new GeneratedNormal(outfit.Identity, normal.MaterialEntryName, normal.ShapeKey, normal.TextureResourceName));
            }
        }

        private static void ConfigureExtraBoneRegistries(ShapeSyncDatabase database, ShapeSyncOutfit runtimeOutfit, ShapeSyncDatabaseRegistry.OutfitEntry outfit, string folder)
        {
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseFigureDiagnostic))
                throw new InvalidOperationException("OutfitGenerateBaseFigureInvalid: " + baseFigureDiagnostic);
            GameObject figure = baseFigure?.Figure;
            if (figure == null) throw new InvalidOperationException("OutfitGenerateBaseFigureMissing: Extra bone registry requires the Database Base Figure.");
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures.Single(value => value.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            CharacterBoneRegistry baseRegistry = BuildExtraBoneRegistry(baseAxis.OutfitPrefab, figure, string.Empty);
            baseRegistry.name = outfit.Identity + "_ExtraBoneRegistry";
            baseRegistry = Persist(baseRegistry, folder + "/" + baseRegistry.name + ".asset");
            SerializedObject serialized = new SerializedObject(runtimeOutfit);
            serialized.FindProperty("baseExtraBoneRegistry").objectReferenceValue = baseRegistry;
            SerializedProperty fbmRegistries = serialized.FindProperty("fbmExtraBoneRegistries");
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry[] axes = outfit.AxisFigures.Where(value => value != null && value.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey).ToArray();
            fbmRegistries.arraySize = axes.Length;
            for (int index = 0; index < axes.Length; index++)
            {
                CharacterBoneRegistry registry = BuildExtraBoneRegistry(axes[index].OutfitPrefab, figure, axes[index].ShapeKey);
                registry.name = outfit.Identity + "_" + axes[index].ShapeKey + "_ExtraBoneRegistry";
                registry = Persist(registry, folder + "/" + registry.name + ".asset");
                SerializedProperty entry = fbmRegistries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("blendName").stringValue = axes[index].ShapeKey;
                entry.FindPropertyRelative("extraBoneRegistry").objectReferenceValue = registry;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Transfers the Bone portion of a Collection declaration. Collection Prefabs deliberately
        /// carry no Animator; their Humanoid bones are resolved by the matching path from the
        /// Database Figure's Animator, so no Avatar or authoring reference is emitted.
        /// </summary>
        private static CollectionProfiles ConfigureCollectionBoneProfiles(ShapeSyncDatabase database, ShapeSyncOutfit runtimeOutfit,
            ShapeSyncDatabaseRegistry.OutfitEntry outfit, string folder)
        {
            if (outfit.CollectionKind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.None) return null;
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry baseEntry = outfit.CollectionEntries
                .Single(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            ShapeSyncHumanoidBoneCorrectionProfile baseProfile = BuildCollectionBoneProfile(
                ResolveFigureForShape(database.Registry, ShapeSyncDatabaseRegistry.BaseShapeKey), baseEntry.CollectionPrefab);
            baseProfile.name = outfit.Identity + "_HumanoidBoneCorrectionProfile";
            baseProfile = Persist(baseProfile, folder + "/" + baseProfile.name + ".asset");

            ShapeSyncDatabaseRegistry.OutfitCollectionEntry[] fbmEntries = outfit.CollectionEntries
                .Where(entry => entry != null && entry.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey)
                .OrderBy(entry => entry.ShapeKey, StringComparer.Ordinal).ToArray();
            var fbmProfiles = new List<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile>(fbmEntries.Length);
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry entry in fbmEntries)
            {
                ShapeSyncHumanoidBoneCorrectionProfile profile = BuildCollectionBoneProfile(ResolveFigureForShape(database.Registry, entry.ShapeKey), entry.CollectionPrefab);
                profile.name = outfit.Identity + "_" + entry.ShapeKey + "_HumanoidBoneCorrectionProfile";
                profile = Persist(profile, folder + "/" + profile.name + ".asset");
                fbmProfiles.Add(new ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile { blendName = entry.ShapeKey, targetProfile = profile });
            }

            SerializedObject serialized = new SerializedObject(runtimeOutfit);
            serialized.FindProperty("humanoidBoneCorrectionProfile").objectReferenceValue = baseProfile;
            SerializedProperty targetProfiles = serialized.FindProperty("fbmHumanoidBoneCorrectionProfiles");
            targetProfiles.arraySize = fbmProfiles.Count;
            for (int index = 0; index < fbmProfiles.Count; index++)
            {
                SerializedProperty target = targetProfiles.GetArrayElementAtIndex(index);
                target.FindPropertyRelative("blendName").stringValue = fbmProfiles[index].blendName;
                target.FindPropertyRelative("targetProfile").objectReferenceValue = fbmProfiles[index].targetProfile;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return new CollectionProfiles(baseProfile, fbmProfiles);
        }

        private static void ConfigureCollectionPcmPayload(ShapeSyncDatabase database, ShapeSyncOutfit runtimeOutfit,
            ShapeSyncDatabaseRegistry.OutfitEntry outfit, CollectionProfiles profiles, string rootPath, string folder)
        {
            if (profiles == null) throw new InvalidOperationException("OutfitGenerateCollectionProfilesMissing: Full Collection requires Bone profiles.");
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry figureEntry, out string figureDiagnostic))
                throw new InvalidOperationException("OutfitGenerateCollectionFigureInvalid: " + figureDiagnostic);
            if (figureEntry == null || string.IsNullOrWhiteSpace(figureEntry.Name)) throw new InvalidOperationException("OutfitGenerateCollectionFigureMissing: Base Figure is required for Full Collection.");
            GameObject generatedFigure = AssetDatabase.LoadAssetAtPath<GameObject>(rootPath.TrimEnd('/') + "/" + figureEntry.Name + ".prefab");
            if (generatedFigure == null) throw new InvalidOperationException("OutfitGenerateCollectionFigureOutputMissing: Generate the Figure output before Full Collection.");
            SkinnedMeshRenderer[] figureRenderers = generatedFigure.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (figureRenderers.Length != 1 || figureRenderers[0].sharedMesh == null)
                throw new InvalidOperationException("OutfitGenerateCollectionPcmRendererInvalid: Full Collection requires exactly one generated Figure SkinnedMeshRenderer.");
            SkinnedMeshRenderer sourceRenderer = figureRenderers[0];
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry baseCollection = outfit.CollectionEntries
                .Single(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            SkinnedMeshRenderer baseTarget = FindCollectionRenderer(outfit, baseCollection, ShapeSyncDatabaseRegistry.BaseShapeKey, RelativePath(generatedFigure.transform, sourceRenderer.transform));
            if (!BlendShapeBakeUtility.TryBuildMeshDifference(sourceMesh, baseTarget.sharedMesh, out Vector3[] baseTargetDelta, out _, out _))
                throw new InvalidOperationException("OutfitGenerateCollectionPcmTopologyInvalid: Base Collection mesh topology does not match the generated Figure.");
            Vector3[] baseBcpDelta = BuildStaticProfileDelta(generatedFigure, sourceRenderer, sourceMesh, profiles.Base);
            Vector3[] basePcmDelta = outfit.UseProjectionForFullCollection
                ? BuildCollectionProjectionDelta(generatedFigure, sourceRenderer, sourceMesh, baseTarget.sharedMesh, profiles.Base, baseBcpDelta, ShapeSyncDatabaseRegistry.BaseShapeKey)
                : Subtract(baseTargetDelta, baseBcpDelta);

            ShapeSyncDatabaseRegistry.OutfitCollectionEntry[] fbmCollections = outfit.CollectionEntries
                .Where(entry => entry != null && entry.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey).OrderBy(entry => entry.ShapeKey, StringComparer.Ordinal).ToArray();
            var fbmNames = new List<string>(fbmCollections.Length);
            var fbmPcmDeltas = new List<Vector3[]>(fbmCollections.Length);
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry collection in fbmCollections)
            {
                SkinnedMeshRenderer target = FindCollectionRenderer(outfit, collection, collection.ShapeKey, RelativePath(generatedFigure.transform, sourceRenderer.transform));
                if (!BlendShapeBakeUtility.TryBuildMeshDifference(sourceMesh, target.sharedMesh, out Vector3[] targetDelta, out _, out _))
                    throw new InvalidOperationException("OutfitGenerateCollectionPcmTopologyInvalid: " + collection.ShapeKey);
                int blendShapeIndex = sourceMesh.GetBlendShapeIndex(collection.ShapeKey);
                if (blendShapeIndex < 0 || !BlendShapeBakeUtility.TryGetBlendShapeDeltaAtUnityWeight(sourceMesh, blendShapeIndex, 100f, out Vector3[] fbmDelta, out _, out _))
                    throw new InvalidOperationException("OutfitGenerateCollectionPcmFbmMissing: " + collection.ShapeKey);
                if (!profiles.TryGetFbm(collection.ShapeKey, out ShapeSyncHumanoidBoneCorrectionProfile profile))
                    throw new InvalidOperationException("OutfitGenerateCollectionFbmProfileMissing: " + collection.ShapeKey);
                Vector3[] targetBcpDelta = BuildStaticProfileDelta(generatedFigure, sourceRenderer, sourceMesh, profile);
                fbmNames.Add(collection.ShapeKey);
                if (outfit.UseProjectionForFullCollection)
                {
                    Vector3[] targetProjectionDelta = BuildCollectionProjectionDelta(
                        generatedFigure, sourceRenderer, sourceMesh, target.sharedMesh, profile,
                        Add(fbmDelta, targetBcpDelta), collection.ShapeKey);
                    fbmPcmDeltas.Add(Subtract(targetProjectionDelta, basePcmDelta));
                }
                else
                {
                    fbmPcmDeltas.Add(Subtract(targetDelta, baseTargetDelta, fbmDelta, targetBcpDelta, baseBcpDelta));
                }
            }

            Mesh payloadMesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            payloadMesh.name = outfit.Identity + "_ProfileControlledMorphMesh";
            payloadMesh.ClearBlendShapes();
            BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(payloadMesh, BlendShapeReservedPrefixes.Pcm + outfit.Identity, basePcmDelta, null, null);
            for (int index = 0; index < fbmNames.Count; index++)
                BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(payloadMesh, BlendShapeReservedPrefixes.Pcm + fbmNames[index] + "_" + outfit.Identity, fbmPcmDeltas[index], null, null);
            payloadMesh = Persist(payloadMesh, folder + "/" + payloadMesh.name + ".asset");
            ProfileControlledMorphAsset payload = ScriptableObject.CreateInstance<ProfileControlledMorphAsset>();
            payload.name = outfit.Identity + "_ProfileControlledMorph";
            payload.ConfigureForBuild(payloadMesh, outfit.Identity, fbmNames, false);
            payload = Persist(payload, folder + "/" + payload.name + ".asset");
            SerializedObject serialized = new SerializedObject(runtimeOutfit);
            serialized.FindProperty("profileControlledMorphEnabled").boolValue = true;
            serialized.FindProperty("profileControlledMorphOutfitName").stringValue = outfit.Identity;
            serialized.FindProperty("profileControlledMorphAsset").objectReferenceValue = payload;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SkinnedMeshRenderer FindCollectionRenderer(ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            ShapeSyncDatabaseRegistry.OutfitCollectionEntry collection, string shapeKey, string figureRendererPath)
        {
            bool useProjection = outfit.UseProjectionForFullCollection;
            GameObject source = useProjection
                ? outfit.AxisFigures.Single(axis => axis != null && axis.ShapeKey == shapeKey).ProjectionPrefab
                : collection.CollectionPrefab;
            if (source == null) throw new InvalidOperationException("OutfitGenerateCollectionSourceMissing: " + shapeKey);

            // A Projection Prefab is an axis-specific geometry artifact. Its renderer
            // name/path is not required to match the generated Figure renderer (for
            // example, a shoes projection may use BasicFemaleShoes1_MergedMesh).
            // Resolve it structurally and reject ambiguous payloads instead of parsing
            // or guessing from names. Collection Prefabs, on the other hand, must stay
            // aligned with the generated Figure hierarchy and retain the path contract.
            if (useProjection)
            {
                SkinnedMeshRenderer[] projectionRenderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer => renderer != null && renderer.sharedMesh != null).ToArray();
                if (projectionRenderers.Length != 1)
                    throw new InvalidOperationException("OutfitGenerateCollectionRendererAmbiguous: " + shapeKey + "/Projection requires exactly one SkinnedMeshRenderer.");
                return projectionRenderers[0];
            }

            Transform transform = string.IsNullOrEmpty(figureRendererPath) ? source.transform : source.transform.Find(figureRendererPath);
            SkinnedMeshRenderer renderer = transform == null ? null : transform.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null)
                throw new InvalidOperationException("OutfitGenerateCollectionRendererMissing: " + shapeKey + "/" + figureRendererPath);
            return renderer;
        }

        private static Vector3[] BuildStaticProfileDelta(GameObject figureRoot, SkinnedMeshRenderer renderer, Mesh sourceMesh, ShapeSyncHumanoidBoneCorrectionProfile profile)
        {
            var delta = new Vector3[sourceMesh.vertexCount];
            if (profile == null) return delta;
            Animator animator = figureRoot.GetComponentInChildren<Animator>(true);
            BoneWeight[] weights = sourceMesh.boneWeights;
            Transform[] bones = renderer.bones;
            if (animator == null || !animator.isHuman || weights == null || weights.Length != sourceMesh.vertexCount || bones == null)
                throw new InvalidOperationException("OutfitGenerateCollectionPcmSkinningInvalid: Figure requires a Humanoid Animator and valid skinning weights.");
            Vector3[] vertices = sourceMesh.vertices;
            Vector3[] corrected = (Vector3[])vertices.Clone();
            foreach (ShapeSyncHumanoidBoneCorrection correction in profile.Corrections ?? Array.Empty<ShapeSyncHumanoidBoneCorrection>())
            {
                if (correction == null) throw new InvalidOperationException("OutfitGenerateCollectionBcpInvalid: null correction.");
                Transform bone = animator.GetBoneTransform(correction.bone);
                if (bone == null) throw new InvalidOperationException("OutfitGenerateCollectionBcpBoneMissing: " + correction.bone);
                Matrix4x4 parentWorld = bone.parent != null ? bone.parent.localToWorldMatrix : Matrix4x4.identity;
                Matrix4x4 correctedWorld = parentWorld * Matrix4x4.TRS(bone.localPosition + correction.localPositionDelta, correction.localRotationDelta * bone.localRotation, bone.localScale + correction.localScaleDelta);
                Matrix4x4 matrix = renderer.transform.worldToLocalMatrix * correctedWorld * bone.localToWorldMatrix.inverse * renderer.transform.localToWorldMatrix;
                for (int index = 0; index < corrected.Length; index++)
                {
                    float weight = GetDescendantWeight(weights[index], bones, bone);
                    if (weight > 0f) corrected[index] += (matrix.MultiplyPoint3x4(corrected[index]) - corrected[index]) * weight;
                }
            }
            for (int index = 0; index < delta.Length; index++) delta[index] = corrected[index] - vertices[index];
            return delta;
        }

        // Keep Full Collection projection identical to Figure PCM Builder's optional
        // surface-fit path: only vertices influenced by the profile's selected bone
        // neighbourhood are projected.  Unrelated bones/vertices intentionally receive
        // zero residual instead of being projected against the whole Projection mesh.
        private static Vector3[] BuildCollectionProjectionDelta(GameObject figureRoot, SkinnedMeshRenderer sourceRenderer,
            Mesh sourceMesh, Mesh targetMesh, ShapeSyncHumanoidBoneCorrectionProfile profile,
            Vector3[] bakedDelta, string profileLabel)
        {
            if (!TryBuildProfileProjectionMask(figureRoot, sourceRenderer, sourceMesh, profile, out bool[] mask, out string error))
                throw new InvalidOperationException("OutfitGenerateCollectionPcmProjectionInvalid: " + profileLabel + ": " + error);
            bool hasProjectedVertex = mask.Any(value => value);
            if (!hasProjectedVertex) return new Vector3[sourceMesh.vertexCount];

            Mesh posedMesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            GameObject projectionSpace = null;
            try
            {
                Vector3[] posedVertices = sourceMesh.vertices;
                for (int index = 0; index < posedVertices.Length; index++) posedVertices[index] += bakedDelta[index];
                posedMesh.vertices = posedVertices;
                projectionSpace = new GameObject("Outfit PCM Projection Space") { hideFlags = HideFlags.HideAndDontSave };
                // These values are the Figure PCM Builder defaults.  Do not broaden the
                // mask: the profile, not the target mesh, defines the affected bones.
                var settings = new ProfileControlledMorphProjection.Settings(0.05f, 0f, mask, Vector3.zero);
                if (!ProfileControlledMorphProjection.TryBuild(posedMesh, projectionSpace.transform, targetMesh,
                    projectionSpace.transform, settings, out ProfileControlledMorphProjection.Result projection, out error))
                    throw new InvalidOperationException("OutfitGenerateCollectionPcmProjectionFailed: " + profileLabel + ": " + error);
                return projection.deltaVertices;
            }
            finally
            {
                if (projectionSpace != null) UnityEngine.Object.DestroyImmediate(projectionSpace);
                UnityEngine.Object.DestroyImmediate(posedMesh);
            }
        }

        private static bool TryBuildProfileProjectionMask(GameObject figureRoot, SkinnedMeshRenderer sourceRenderer,
            Mesh sourceMesh, ShapeSyncHumanoidBoneCorrectionProfile profile, out bool[] mask, out string error)
        {
            mask = new bool[sourceMesh.vertexCount];
            error = null;
            if (profile == null || profile.Corrections == null || profile.Corrections.Count == 0) return true;
            Animator animator = figureRoot != null ? figureRoot.GetComponentInChildren<Animator>(true) : null;
            BoneWeight[] weights = sourceMesh.boneWeights;
            Transform[] bones = sourceRenderer != null ? sourceRenderer.bones : null;
            if (animator == null || !animator.isHuman || weights == null || weights.Length != sourceMesh.vertexCount || bones == null)
            {
                error = "Projection requires a Humanoid Animator and valid skinning weights.";
                return false;
            }

            var selected = new HashSet<Transform>();
            foreach (ShapeSyncHumanoidBoneCorrection correction in profile.Corrections)
            {
                if (correction == null || !IsFiniteCorrection(correction))
                {
                    error = "Projection contains an invalid Humanoid TRS correction.";
                    return false;
                }
                if (ShapeSyncLegacyBuilderContracts.IsPositionOnlyHipsCorrection(correction)) continue;
                Transform bone = animator.GetBoneTransform(correction.bone);
                if (bone == null)
                {
                    error = "Profile bone '" + correction.bone + "' is not mapped by the Figure Animator.";
                    return false;
                }
                selected.Add(bone);
                if (bone.parent != null) selected.Add(bone.parent);
                for (int child = 0; child < bone.childCount; child++) selected.Add(bone.GetChild(child));
            }

            for (int vertex = 0; vertex < mask.Length; vertex++)
            {
                BoneWeight weight = weights[vertex];
                mask[vertex] = IsSelectedInfluence(weight.boneIndex0, weight.weight0, bones, selected)
                    || IsSelectedInfluence(weight.boneIndex1, weight.weight1, bones, selected)
                    || IsSelectedInfluence(weight.boneIndex2, weight.weight2, bones, selected)
                    || IsSelectedInfluence(weight.boneIndex3, weight.weight3, bones, selected);
            }
            return true;
        }

        private static bool IsSelectedInfluence(int boneIndex, float weight, Transform[] bones, HashSet<Transform> selected)
        {
            if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length) return false;
            for (Transform current = bones[boneIndex]; current != null; current = current.parent)
                if (selected.Contains(current)) return true;
            return false;
        }

        private static bool IsFiniteCorrection(ShapeSyncHumanoidBoneCorrection correction)
        {
            return IsFiniteVector(correction.localPositionDelta) && IsFiniteVector(correction.localScaleDelta)
                && IsFiniteQuaternion(correction.localRotationDelta);
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFiniteQuaternion(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);
        }

        private static Vector3[] Add(Vector3[] first, Vector3[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
                throw new InvalidOperationException("OutfitGenerateCollectionPcmDeltaLengthMismatch.");
            Vector3[] result = new Vector3[first.Length];
            for (int index = 0; index < result.Length; index++) result[index] = first[index] + second[index];
            return result;
        }

        private static float GetDescendantWeight(BoneWeight weight, Transform[] bones, Transform target)
        {
            return GetDescendantWeight(weight.boneIndex0, weight.weight0, bones, target)
                + GetDescendantWeight(weight.boneIndex1, weight.weight1, bones, target)
                + GetDescendantWeight(weight.boneIndex2, weight.weight2, bones, target)
                + GetDescendantWeight(weight.boneIndex3, weight.weight3, bones, target);
        }

        private static float GetDescendantWeight(int boneIndex, float weight, Transform[] bones, Transform target)
        {
            if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length) return 0f;
            for (Transform current = bones[boneIndex]; current != null; current = current.parent)
                if (current == target) return weight;
            return 0f;
        }

        private static Vector3[] Subtract(params Vector3[][] values)
        {
            if (values == null || values.Length == 0 || values[0] == null) throw new InvalidOperationException("OutfitGenerateCollectionPcmDeltaInvalid.");
            Vector3[] result = (Vector3[])values[0].Clone();
            for (int source = 1; source < values.Length; source++)
            {
                if (values[source] == null || values[source].Length != result.Length) throw new InvalidOperationException("OutfitGenerateCollectionPcmDeltaInvalid.");
                for (int index = 0; index < result.Length; index++) result[index] -= values[source][index];
            }
            return result;
        }

        private sealed class CollectionProfiles
        {
            internal ShapeSyncHumanoidBoneCorrectionProfile Base { get; }
            private readonly Dictionary<string, ShapeSyncHumanoidBoneCorrectionProfile> fbms;
            internal CollectionProfiles(ShapeSyncHumanoidBoneCorrectionProfile baseProfile, IEnumerable<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile> fbmProfiles)
            {
                Base = baseProfile;
                fbms = (fbmProfiles ?? Array.Empty<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile>()).ToDictionary(value => value.blendName, value => value.targetProfile, StringComparer.Ordinal);
            }
            internal bool TryGetFbm(string name, out ShapeSyncHumanoidBoneCorrectionProfile profile) => fbms.TryGetValue(name, out profile);
        }

        private static GameObject ResolveFigureForShape(ShapeSyncDatabaseRegistry registry, string shapeKey)
        {
            if (shapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
            {
                if (!registry.TryGetSingleBaseFigure(out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out string baseDiagnostic))
                    throw new InvalidOperationException("OutfitGenerateCollectionFigureInvalid: " + baseDiagnostic);
                GameObject figure = baseFigure?.Figure;
                if (figure != null) return figure;
            }
            else
            {
                ShapeSyncDatabaseRegistry.FigureAxisEntry axis = registry.FigureAxes.SingleOrDefault(value => value != null
                    && value.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && value.Name == shapeKey);
                GameObject figure = axis?.Figures.SingleOrDefault()?.Figure;
                if (figure != null) return figure;
            }
            throw new InvalidOperationException("OutfitGenerateCollectionFigureMissing: " + shapeKey);
        }

        private static ShapeSyncHumanoidBoneCorrectionProfile BuildCollectionBoneProfile(GameObject figure, GameObject collection)
        {
            if (figure == null || collection == null) throw new InvalidOperationException("OutfitGenerateCollectionSourceMissing: Figure and Collection Prefabs are required.");
            Animator animator = figure.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("OutfitGenerateCollectionAnimatorInvalid: Figure requires a Humanoid Animator.");
            var corrections = new List<ShapeSyncHumanoidBoneCorrection>();
            for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
            {
                HumanBodyBones bone = (HumanBodyBones)index;
                Transform source = animator.GetBoneTransform(bone);
                if (source == null) continue;
                string path = RelativePath(figure.transform, source);
                Transform target = string.IsNullOrEmpty(path) ? collection.transform : collection.transform.Find(path);
                if (target == null) throw new InvalidOperationException("OutfitGenerateCollectionBoneMissing: " + bone);
                Vector3 positionDelta = target.localPosition - source.localPosition;
                Vector3 scaleDelta = target.localScale - source.localScale;
                Quaternion rotationDelta = Normalize(target.localRotation * Quaternion.Inverse(source.localRotation));
                if (positionDelta.sqrMagnitude <= 0.000001f && scaleDelta.sqrMagnitude <= 0.000001f && QuaternionDistanceFromIdentity(rotationDelta) <= 0.0001f) continue;
                corrections.Add(new ShapeSyncHumanoidBoneCorrection
                {
                    bone = bone,
                    localPositionDelta = positionDelta,
                    localRotationDelta = rotationDelta,
                    localScaleDelta = scaleDelta
                });
            }
            ShapeSyncHumanoidBoneCorrectionProfile profile = ScriptableObject.CreateInstance<ShapeSyncHumanoidBoneCorrectionProfile>();
            profile.SetCorrectionsForEditor(corrections);
            return profile;
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float length = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return length > Mathf.Epsilon ? new Quaternion(value.x / length, value.y / length, value.z / length, value.w / length) : Quaternion.identity;
        }

        private static float QuaternionDistanceFromIdentity(Quaternion value)
        {
            return Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z), Mathf.Abs(value.w - 1f));
        }

        private static CharacterBoneRegistry BuildExtraBoneRegistry(GameObject outfitRoot, GameObject figureRoot, string blendName)
        {
            if (outfitRoot == null) throw new InvalidOperationException("OutfitGenerateExtraBoneSourceMissing: Outfit axis prefab is required.");
            CharacterBoneRegistry registry = ScriptableObject.CreateInstance<CharacterBoneRegistry>();
            registry.fbmBlendName = blendName;
            foreach (Transform transform in outfitRoot.GetComponentsInChildren<Transform>(true))
            {
                // Mesh/material containers such as Face, Hair, and Body are not Extra Bone
                // roots.  Their Renderer is commonly on a descendant, so checking only the
                // current GameObject misclassifies the container itself as a bone root.
                if (transform == outfitRoot.transform
                    || transform.GetComponentsInChildren<Renderer>(true).Length != 0
                    || transform.GetComponent<MeshFilter>() != null) continue;
                string path = RelativePath(outfitRoot.transform, transform);
                // Extra Bone paths are Figure-relative skeleton paths.  Root-level authoring
                // containers (Face/Hair/Body and similar empty grouping objects) are not bone
                // roots and must never become attach roots merely because they are absent from
                // the Figure hierarchy.
                if (string.IsNullOrEmpty(path)
                    || (!string.Equals(path, "Root", StringComparison.Ordinal) && !path.StartsWith("Root/", StringComparison.Ordinal))
                    || figureRoot.transform.Find(path) != null) continue;
                registry.bonePoses.Add(new BonePoseData { boneName = path, localPosition = transform.localPosition, localRotation = transform.localRotation, localScale = transform.localScale, bindposeIndex = -1, hasBindpose = false });
            }
            return registry;
        }

        private static void ConfigureFigureNormalBindings(ShapeSyncDatabase database, string rootPath, string bindingsPath, IReadOnlyList<GeneratedNormal> generatedNormals)
        {
            if (generatedNormals.Count == 0) return;
            if (!database.Registry.TryGetSingleBaseFigure(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry figure, out string figureDiagnostic))
                throw new InvalidOperationException("OutfitGenerateFigureBindingInvalid: " + figureDiagnostic);
            if (figure == null || string.IsNullOrWhiteSpace(figure.Name)) throw new InvalidOperationException("OutfitGenerateFigureBindingInvalid: Base Figure is required for Outfit Normal binding.");
            string folder = rootPath.TrimEnd('/') + "/" + (bindingsPath ?? string.Empty).Trim('/');
            MeshBinding binding = AssetDatabase.LoadAssetAtPath<MeshBinding>(folder + "/" + figure.Name + "_MeshBinding.asset");
            if (binding == null) throw new InvalidOperationException("OutfitGenerateFigureBindingMissing: Generate the Figure MeshBinding before Outfit Normal output.");
            MaterialBinding materialBinding = AssetDatabase.LoadAssetAtPath<MaterialBinding>(folder + "/" + figure.Name + "_MaterialBinding.asset");
            if (materialBinding == null) throw new InvalidOperationException("OutfitGenerateMaterialBindingMissing: Generate the Figure MaterialBinding before Outfit Normal output.");
            var texturesByLogicalName = materialBinding.Textures.ToDictionary(value => value.logicalName, value => value.sourceTexture, StringComparer.Ordinal);
            SerializedObject serialized = new SerializedObject(binding);
            SerializedProperty owners = serialized.FindProperty("normalOwners");
            for (int index = owners.arraySize - 1; index >= 0; index--)
                if (!string.IsNullOrEmpty(owners.GetArrayElementAtIndex(index).FindPropertyRelative("outfitRegistryId").stringValue)) owners.DeleteArrayElementAtIndex(index);
            foreach (IGrouping<string, GeneratedNormal> ownerGroup in generatedNormals.GroupBy(value => value.OutfitIdentity).OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                int ownerIndex = owners.arraySize;
                owners.arraySize++;
                SerializedProperty owner = owners.GetArrayElementAtIndex(ownerIndex);
                owner.FindPropertyRelative("outfitRegistryId").stringValue = ownerGroup.Key;
                IGrouping<string, GeneratedNormal>[] targets = ownerGroup.GroupBy(value => value.ShapeKey).OrderBy(value => value.Key == ShapeSyncDatabaseRegistry.BaseShapeKey ? string.Empty : value.Key, StringComparer.Ordinal).ToArray();
                SerializedProperty targetList = owner.FindPropertyRelative("targets");
                targetList.arraySize = targets.Length;
                for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    SerializedProperty target = targetList.GetArrayElementAtIndex(targetIndex);
                    target.FindPropertyRelative("targetName").stringValue = targets[targetIndex].Key == ShapeSyncDatabaseRegistry.BaseShapeKey ? string.Empty : targets[targetIndex].Key;
                    GeneratedNormal[] values = targets[targetIndex].OrderBy(value => value.EntryName, StringComparer.Ordinal).ToArray();
                    SerializedProperty textures = target.FindPropertyRelative("textures");
                    textures.arraySize = values.Length;
                    for (int textureIndex = 0; textureIndex < values.Length; textureIndex++)
                    {
                        SerializedProperty texture = textures.GetArrayElementAtIndex(textureIndex);
                        texture.FindPropertyRelative("entryName").stringValue = values[textureIndex].EntryName;
                        if (!texturesByLogicalName.TryGetValue(values[textureIndex].TextureResourceName, out Texture2D normalTexture) || normalTexture == null)
                            throw new InvalidOperationException("OutfitGenerateNormalResourceMissing: " + values[textureIndex].TextureResourceName);
                        texture.FindPropertyRelative("normalTexture").objectReferenceValue = normalTexture;
                    }
                }
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(binding);
        }

        private sealed class GeneratedNormal
        {
            internal string OutfitIdentity { get; }
            internal string EntryName { get; }
            internal string ShapeKey { get; }
            internal string TextureResourceName { get; }
            internal GeneratedNormal(string outfitIdentity, string entryName, string shapeKey, string textureResourceName)
            { OutfitIdentity = outfitIdentity; EntryName = entryName; ShapeKey = shapeKey; TextureResourceName = textureResourceName; }
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (value == root) return string.Empty;
            var names = new List<string>();
            for (Transform current = value; current != null && current != root; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[index])))
                    throw new InvalidOperationException("Could not create Outfit output folder: " + next);
                current = next;
            }
        }
    }
}
#endif
