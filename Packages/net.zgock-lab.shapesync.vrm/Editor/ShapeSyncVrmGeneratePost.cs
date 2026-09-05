// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniHumanoid;
using UniVRM10;
using zgock.ShapeSync;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>Registers the Database-side VRM Generate post without making Core depend on UniVRM.</summary>
    [InitializeOnLoad]
    internal static class ShapeSyncVrmGenerateRegistration
    {
        static ShapeSyncVrmGenerateRegistration()
        {
            ShapeSyncDatabaseOptionalRegistryProvider.RegisterVrmGenerate(ShapeSyncVrmGeneratePost.TryGenerate);
            ShapeSyncDatabaseOptionalRegistryProvider.RegisterVrmGenerateFinalize(ShapeSyncVrmGeneratePost.FinalizeGenerate);
        }
    }

    internal static class ShapeSyncVrmGeneratePost
    {
        internal static bool FinalizeGenerate(ShapeSyncDatabase database, string rootPath,
            ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            string databasePath = AssetDatabase.GetAssetPath(database);
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                diagnostic = "VrmGenerateFinalizeDatabasePersistentRequired: Final VRM wiring requires a persistent Database Prefab.";
                return false;
            }

            if (!ShapeSyncVrmDatabaseRegistryRegistration.TryGetRegistry(databasePath,
                out ShapeSyncVrmDatabaseRegistry registry, out diagnostic)) return false;
            if (registry == null) return true;

            string vrmFolder = ResolveVrmFolder(rootPath, registry.GenerationVrmPath);

            if (database == null || database.Registry == null || database.Registry.BaseFigures == null
                || database.Registry.BaseFigures.Count != 1 || database.Registry.BaseFigures[0] == null)
            {
                diagnostic = "VrmGenerateFinalizeBaseFigureRequired: Final VRM wiring requires exactly one Base Figure.";
                return false;
            }

            string figureName = database.Registry.BaseFigures[0].Name;
            if (!TryValidateExpressionCompleteness(database, registry, figureName, out diagnostic)) return false;
            string prefabPath = rootPath.TrimEnd('/') + "/" + figureName + ".prefab";
            if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
            {
                diagnostic = "VrmGenerateFinalizeFigureOutputMissing: Final VRM wiring could not resolve the generated Figure Prefab.";
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contents == null)
            {
                diagnostic = "VrmGenerateFinalizeFigureOpenFailed: Final VRM wiring could not open the generated Figure Prefab.";
                return false;
            }

            try
            {
                SkinnedMeshRenderer[] renderers = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0)
                    return ValidateVrmFolderContents(vrmFolder, null, out diagnostic);
                if (renderers.Length != 1)
                {
                    diagnostic = "VrmGenerateFinalizeRendererInvalid: Final VRM wiring requires exactly one merged Figure Renderer.";
                    return false;
                }

                DynamicBoneBlender blender = contents.GetComponent<DynamicBoneBlender>();
                UniversalExpressionProxy expressionProxy = contents.GetComponent<UniversalExpressionProxy>()
                    ?? contents.AddComponent<UniversalExpressionProxy>();
                expressionProxy.ConfigureForFigure(renderers[0], blender);
                expressionProxy.RebuildExpressionList();
                EditorUtility.SetDirty(expressionProxy);
                if (PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved) == null || !saved)
                {
                    diagnostic = "VrmGenerateFinalizeSaveFailed: Final VRM wiring could not save the generated Figure Prefab.";
                    return false;
                }
                AssetDatabase.SaveAssets();
                if (!ValidateVrmFolderContents(vrmFolder, null, out diagnostic)) return false;
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        internal static bool TryGenerate(ShapeSyncDatabase database, string rootPath,
            ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            if (database == null)
            {
                diagnostic = "VrmGenerateDatabaseRequired: VRM Generate requires an opened ShapeSync Database.";
                return false;
            }

            string databasePath = AssetDatabase.GetAssetPath(database);
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                diagnostic = "VrmGenerateDatabasePersistentRequired: VRM Generate requires a persistent Database Prefab.";
                return false;
            }

            if (!ShapeSyncVrmDatabaseRegistryRegistration.TryGetRegistry(databasePath,
                out ShapeSyncVrmDatabaseRegistry registry, out diagnostic)) return false;
            if (registry == null) return true;
            if (!registry.HasValidFeatureMarker)
            {
                diagnostic = "VrmGenerateRegistryInvalid: VRM Registry has no valid feature marker.";
                return false;
            }
            if (!ShapeSyncVrmDatabaseRegistry.TryValidateGenerationVrmPath(registry.GenerationVrmPath, out diagnostic))
                return false;
            if (database.Registry == null || database.Registry.BaseFigures == null || database.Registry.BaseFigures.Count != 1
                || database.Registry.BaseFigures[0] == null || string.IsNullOrWhiteSpace(database.Registry.BaseFigures[0].Name))
            {
                diagnostic = "VrmGenerateBaseFigureRequired: VRM Generate requires exactly one registered Base Figure.";
                return false;
            }

            string figureName = database.Registry.BaseFigures[0].Name;
            if (!TryValidateExpressionCompleteness(database, registry, figureName, out diagnostic)) return false;
            string prefabPath = rootPath.TrimEnd('/') + "/" + figureName + ".prefab";
            if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
            {
                diagnostic = "VrmGenerateFigureOutputMissing: VRM Generate could not resolve the generated Figure Prefab.";
                return false;
            }

            try
            {
                string vrmFolder = ResolveVrmFolder(rootPath, registry.GenerationVrmPath);
                EnsureFolder(vrmFolder);
                AddGeneratedPath(generatedPaths, prefabPath);

                var legacyCatalogPaths = new HashSet<string>(StringComparer.Ordinal);
                if (ShapeSyncGenerateCatalog.TryRead(rootPath, out List<string> previousCatalogPaths,
                    out _, out _))
                {
                    foreach (string path in previousCatalogPaths) legacyCatalogPaths.Add(path);
                }

                InitializeFigurePrefab(prefabPath);
                GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabRoot == null) throw new InvalidOperationException("Initialized Figure Prefab could not be reloaded.");

                Vrm10Instance instance = prefabRoot.GetComponent<Vrm10Instance>();
                if (instance == null) throw new InvalidOperationException("Initialized Figure Prefab has no Vrm10Instance.");

                BakedExpressionSet baked = BakeExpressions(database, registry, figureName, prefabRoot,
                    vrmFolder, generatedPaths, out string expressionDiagnostic);
                if (baked == null)
                    throw new InvalidOperationException(expressionDiagnostic ?? "Expression Bake failed.");

                // BakeExpressions appends the final VRM_* and MCM_* BlendShapes
                // to the generated Figure mesh.  Rebuild the serialized proxy
                // list only after that mesh is final; otherwise a valid VRM graph
                // can coexist with an empty UniversalExpressionProxy.Expressions.
                UniversalExpressionProxy expressionProxy = prefabRoot.GetComponent<UniversalExpressionProxy>();
                if (expressionProxy != null)
                {
                    expressionProxy.RebuildExpressionList();
                    EditorUtility.SetDirty(expressionProxy);
                }

                if (!TryTransferPhysics(database, registry, figureName, prefabPath,
                    generatedPaths, out string physicsDiagnostic))
                    throw new InvalidOperationException(physicsDiagnostic ?? "Physics transfer failed.");

                VRM10Object vrm = ScriptableObject.CreateInstance<VRM10Object>();
                vrm.Prefab = prefabRoot;
                if (baked.HasExpressionIntersection)
                {
                    foreach (ExpressionPreset preset in Enum.GetValues(typeof(ExpressionPreset)))
                    {
                        if (preset == ExpressionPreset.custom) continue;
                        if (!baked.Standard.TryGetValue(preset, out VRM10Expression expression))
                            expression = CreateAndPersistEmptyStandardExpression(figureName, preset,
                                prefabRoot, vrmFolder, generatedPaths);
                        vrm.Expression.AddClip(preset, expression);
                    }
                }
                foreach (VRM10Expression custom in baked.Custom)
                    vrm.Expression.AddClip(ExpressionPreset.custom, custom);
                string vrmName = "VRM_" + figureName + "_VRM10Object";
                VRM10Object persistedVrm = PersistAsset(vrm,
                    vrmFolder + "/" + vrmName + ".asset", generatedPaths);

                instance.Vrm = persistedVrm;
                EditorUtility.SetDirty(instance);
                if (PrefabUtility.SavePrefabAsset(prefabRoot) == null)
                    throw new InvalidOperationException("Initialized VRM Figure Prefab could not be saved.");
                AssetDatabase.SaveAssets();
                ValidateGeneratedOutput(prefabRoot, persistedVrm, baked, vrmFolder, legacyCatalogPaths);
                diagnostic = expressionDiagnostic;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "VrmGenerateInitializeFailed: " + exception.Message;
                return false;
            }
        }

        private static bool TryTransferPhysics(ShapeSyncDatabase database,
            ShapeSyncVrmDatabaseRegistry registry, string figureName, string figurePrefabPath,
            ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference figureRelation =
                (registry.FigurePhysicsReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference>())
                .FirstOrDefault(value => value != null
                    && string.Equals(value.FigureName, figureName, StringComparison.Ordinal));
            if (figureRelation != null)
            {
                if (!ShapeSyncDatabaseCanonicalAssetResolver.TryResolveFigureOwner(database, figureName,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, out GameObject owner, out diagnostic)) return false;
                if (figureRelation.OwnerPrefab != owner)
                {
                    diagnostic = "VrmGeneratePhysicsOwnerMismatch: Figure Physics Registry owner does not match the canonical Figure owner.";
                    return false;
                }
                if (!ShapeSyncVrmPhysicsGenerateTransfer.TryTransferFigure(figureRelation,
                    figurePrefabPath, generatedPaths, out diagnostic)) return false;
            }

            ShapeSyncDatabaseRegistry.GenerationPathSettings paths = database.Registry.GenerationPaths;
            string outfitsFolder = paths == null ? null : paths.OutfitsPath;
            if (string.IsNullOrWhiteSpace(outfitsFolder))
            {
                diagnostic = "VrmGenerateOutfitPhysicsOutputPathInvalid: Mesh Outfit output path is unavailable.";
                return false;
            }

            foreach (ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference relation in
                registry.MeshOutfitPhysicsReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference>())
            {
                if (relation == null) continue;
                if (!ShapeSyncDatabaseCanonicalAssetResolver.TryResolveMeshOutfitOwner(database,
                    relation.OutfitIdentity, out GameObject owner, out diagnostic)) return false;
                if (relation.OwnerPrefab != owner)
                {
                    diagnostic = "VrmGeneratePhysicsOwnerMismatch: Mesh Outfit Physics Registry owner does not match the canonical Outfit owner: "
                        + relation.OutfitIdentity;
                    return false;
                }

                string outputFolder = rootPathForOutfit(figurePrefabPath, outfitsFolder);
                string outputPath = outputFolder + "/" + relation.OutfitIdentity + ".prefab";
                if (!ShapeSyncVrmPhysicsGenerateTransfer.TryTransferOutfit(relation,
                    outputPath, generatedPaths, out diagnostic)) return false;
            }
            return true;
        }

        private static string rootPathForOutfit(string figurePrefabPath, string outfitsFolder)
        {
            string rootPath = Path.GetDirectoryName(figurePrefabPath)?.Replace('\\', '/') ?? string.Empty;
            return rootPath.TrimEnd('/') + "/" + outfitsFolder.Replace('\\', '/').Trim('/');
        }

        private static void InitializeFigurePrefab(string prefabPath)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contents == null) throw new InvalidOperationException("Generated Figure Prefab contents could not be loaded.");
            try
            {
                foreach (Vrm10Instance existing in contents.GetComponentsInChildren<Vrm10Instance>(true))
                    if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

                Animator[] animators = contents.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1 || animators[0] == null || animators[0].avatar == null
                    || !animators[0].isHuman || !animators[0].avatar.isValid)
                    throw new InvalidOperationException("VRM Initialize requires exactly one valid Humanoid Animator.");

                Humanoid humanoid = contents.GetComponent<Humanoid>() ?? contents.AddComponent<Humanoid>();
                if (!humanoid.AssignBonesFromAnimator())
                    throw new InvalidOperationException("VRM Initialize could not assign Humanoid bones.");
                foreach (var issue in humanoid.Validate())
                    if (issue.IsError) throw new InvalidOperationException("VRM Initialize Humanoid validation failed: " + issue.Message);

                SkinnedMeshRenderer[] renderers = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length > 1)
                    throw new InvalidOperationException("Generated Figure must contain exactly one merged SkinnedMeshRenderer.");
                if (renderers.Length == 1)
                {
                    DynamicBoneBlender blender = contents.GetComponent<DynamicBoneBlender>();
                    UniversalExpressionProxy expressionProxy = contents.GetComponent<UniversalExpressionProxy>()
                        ?? contents.AddComponent<UniversalExpressionProxy>();
                    expressionProxy.ConfigureForFigure(renderers[0], blender);
                    expressionProxy.ClearExpressionList();
                }

                contents.AddComponent<Vrm10Instance>();
                ShapeSyncVrmIntegrationAdapter adapter = contents.GetComponent<ShapeSyncVrmIntegrationAdapter>()
                    ?? contents.AddComponent<ShapeSyncVrmIntegrationAdapter>();
                adapter.EnsureRegistered();

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("VRM Initialize could not save the initialized Figure Prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private sealed class BakedExpressionSet
        {
            internal readonly Dictionary<ExpressionPreset, VRM10Expression> Standard =
                new Dictionary<ExpressionPreset, VRM10Expression>();
            internal readonly List<VRM10Expression> Custom = new List<VRM10Expression>();
            internal bool HasExpressionIntersection { get; set; }
        }

        private sealed class ExpressionReferenceSource
        {
            internal ExpressionReferenceSource(ShapeSyncVrmDatabaseRegistry.FigureExpressionReference relation,
                Vrm10Instance instance, SkinnedMeshRenderer renderer, Dictionary<string, SourceExpression> expressions)
            {
                Relation = relation;
                Instance = instance;
                Renderer = renderer;
                Expressions = expressions;
            }

            internal ShapeSyncVrmDatabaseRegistry.FigureExpressionReference Relation { get; }
            internal Vrm10Instance Instance { get; }
            internal SkinnedMeshRenderer Renderer { get; }
            internal Dictionary<string, SourceExpression> Expressions { get; }
        }

        private readonly struct SourceExpression
        {
            internal SourceExpression(ExpressionPreset preset, string name, VRM10Expression clip)
            {
                Preset = preset;
                Name = name;
                Clip = clip;
            }

            internal ExpressionPreset Preset { get; }
            internal string Name { get; }
            internal VRM10Expression Clip { get; }
        }

        private static BakedExpressionSet BakeExpressions(ShapeSyncDatabase database,
            ShapeSyncVrmDatabaseRegistry registry, string figureName, GameObject prefabRoot,
            string vrmFolder, ICollection<string> generatedPaths, out string diagnostic)
        {
            diagnostic = null;
            var result = new BakedExpressionSet();
            IReadOnlyList<ShapeSyncVrmDatabaseRegistry.FigureExpressionReference> allRelations =
                registry.FigureExpressionReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.FigureExpressionReference>();
            List<ShapeSyncVrmDatabaseRegistry.FigureExpressionReference> relations = allRelations
                .Where(value => value != null && string.Equals(value.FigureName, figureName, StringComparison.Ordinal))
                .ToList();
            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference baseRelation = relations.FirstOrDefault(value =>
                string.Equals(value.ShapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal));

            // No Base Reference is an intentional no-bake configuration.  It must
            // not create empty Expression assets which would look like generated
            // output and would survive catalog ownership checks.
            if (baseRelation == null)
            {
                diagnostic = "VrmGenerateExpressionBakeInfo: Base Expression Reference is not registered; no Expression assets were generated.";
                return result;
            }

            if (!TryCreateExpressionReferenceSource(database, figureName, baseRelation, out ExpressionReferenceSource baseSource,
                out diagnostic)) return null;

            var fbmSources = new List<ExpressionReferenceSource>();
            foreach (ShapeSyncVrmDatabaseRegistry.FigureExpressionReference relation in relations
                .Where(value => !string.Equals(value.ShapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal)))
            {
                if (!database.Registry.FigureAxes.Any(axis => axis != null
                    && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    && string.Equals(axis.Name, relation.ShapeKey, StringComparison.Ordinal)))
                {
                    diagnostic = "VrmGenerateExpressionReferenceShapeInvalid: Expression Reference shape is not a registered FBM: "
                        + relation.ShapeKey;
                    return null;
                }

                if (!TryCreateExpressionReferenceSource(database, figureName, relation,
                    out ExpressionReferenceSource fbmSource, out diagnostic)) return null;
                fbmSources.Add(fbmSource);
            }

            var commonNames = new HashSet<string>(baseSource.Expressions.Keys, StringComparer.Ordinal);
            foreach (ExpressionReferenceSource source in fbmSources)
                commonNames.IntersectWith(source.Expressions.Keys);
            if (commonNames.Count == 0)
            {
                diagnostic = "VrmGenerateExpressionBakeInfo: Base/FBM Expression intersection is empty; no Expression assets were generated.";
                return result;
            }
            result.HasExpressionIntersection = true;

            SkinnedMeshRenderer outputRenderer = FindSingleSkinnedMeshRenderer(prefabRoot,
                "Generated Figure");
            Mesh outputMesh = outputRenderer.sharedMesh;
            if (outputMesh == null) throw new InvalidOperationException("Generated Figure Renderer has no Mesh for Expression Bake.");

            foreach (ExpressionReferenceSource source in new[] { baseSource }.Concat(fbmSources))
                EnsureSameTopology(outputMesh, source.Renderer.sharedMesh, source.Relation.ShapeKey);

            foreach (string expressionName in commonNames.OrderBy(value => value, StringComparer.Ordinal))
            {
                SourceExpression baseExpression = baseSource.Expressions[expressionName];
                var baseDelta = TransformExpressionDelta(baseSource, outputRenderer,
                    ReadExpressionDelta(baseSource.Relation.ReferencePrefab, baseSource.Renderer, baseExpression.Clip));
                string baseBlendShapeName = BlendShapeReservedPrefixes.Vrm + expressionName;
                BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(outputMesh, baseBlendShapeName,
                    baseDelta.Vertices, baseDelta.Normals, baseDelta.Tangents);

                VRM10Expression persisted = CreateAndPersistExpression(figureName, expressionName,
                    prefabRoot, outputRenderer, baseBlendShapeName, vrmFolder, generatedPaths);
                if (baseExpression.Preset == ExpressionPreset.custom)
                    result.Custom.Add(persisted);
                else
                    result.Standard.Add(baseExpression.Preset, persisted);

                foreach (ExpressionReferenceSource fbmSource in fbmSources)
                {
                    SourceExpression fbmExpression = fbmSource.Expressions[expressionName];
                    var fbmDelta = TransformExpressionDelta(fbmSource, outputRenderer,
                        ReadExpressionDelta(fbmSource.Relation.ReferencePrefab, fbmSource.Renderer, fbmExpression.Clip));
                    var difference = SubtractDelta(fbmDelta, baseDelta);
                    string mcmBlendShapeName = BlendShapeReservedPrefixes.Mcm
                        + fbmSource.Relation.ShapeKey + "_" + expressionName;
                    BlendShapeBakeUtility.AddBlendShapeFrameOrThrow(outputMesh, mcmBlendShapeName,
                        difference.Vertices, difference.Normals, difference.Tangents);
                }
            }

            EditorUtility.SetDirty(outputMesh);
            return result;
        }

        private static bool TryValidateExpressionCompleteness(ShapeSyncDatabase database,
            ShapeSyncVrmDatabaseRegistry registry, string figureName, out string diagnostic)
        {
            diagnostic = null;
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                ShapeSyncDatabaseRegistry.BaseShapeKey
            };
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes
                ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureAxisEntry>())
            {
                if (axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    && !string.IsNullOrWhiteSpace(axis.Name)) expected.Add(axis.Name);
            }

            List<ShapeSyncVrmDatabaseRegistry.FigureExpressionReference> relations =
                (registry.FigureExpressionReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.FigureExpressionReference>())
                .Where(value => value != null && string.Equals(value.FigureName, figureName, StringComparison.Ordinal))
                .ToList();
            if (relations.Count == 0) return true;

            var actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncVrmDatabaseRegistry.FigureExpressionReference relation in relations)
            {
                if (string.IsNullOrWhiteSpace(relation.ShapeKey) || !actual.Add(relation.ShapeKey))
                {
                    diagnostic = "VrmGenerateExpressionReferencesIncomplete: Figure Expression References contain a null or duplicate shape relation.";
                    return false;
                }
            }

            if (expected.SetEquals(actual)) return true;

            string missing = string.Join(", ", expected.Except(actual).OrderBy(value => value, StringComparer.Ordinal));
            string unexpected = string.Join(", ", actual.Except(expected).OrderBy(value => value, StringComparer.Ordinal));
            diagnostic = "VrmGenerateExpressionReferencesIncomplete: Base and all registered FBM Expression References must be present before Generate."
                + (string.IsNullOrEmpty(missing) ? string.Empty : " Missing: " + missing + ".")
                + (string.IsNullOrEmpty(unexpected) ? string.Empty : " Unexpected: " + unexpected + ".");
            return false;
        }

        private static bool TryCreateExpressionReferenceSource(ShapeSyncDatabase database, string figureName,
            ShapeSyncVrmDatabaseRegistry.FigureExpressionReference relation,
            out ExpressionReferenceSource source, out string diagnostic)
        {
            source = null;
            diagnostic = null;
            if (!ShapeSyncDatabaseCanonicalAssetResolver.TryResolveFigureOwner(database, figureName,
                relation.ShapeKey, out GameObject owner, out diagnostic)) return false;
            if (relation.OwnerPrefab != owner)
            {
                diagnostic = "VrmGenerateExpressionOwnerMismatch: VRM Registry owner does not match the canonical Figure owner for "
                    + relation.ShapeKey + ".";
                return false;
            }
            if (relation.ReferencePrefab == null)
            {
                diagnostic = "VrmGenerateExpressionReferenceMissing: Expression Reference Prefab is missing for "
                    + relation.ShapeKey + ".";
                return false;
            }

            Vrm10Instance[] instances = relation.ReferencePrefab.GetComponentsInChildren<Vrm10Instance>(true)
                .Where(value => value != null && value.Vrm != null).ToArray();
            if (instances.Length != 1 || instances[0].Vrm.Expression == null)
            {
                diagnostic = "VrmGenerateExpressionReferenceInvalid: Expression Reference must contain exactly one VRM graph with Expression data.";
                return false;
            }
            SkinnedMeshRenderer renderer = FindSingleSkinnedMeshRenderer(relation.ReferencePrefab,
                "Expression Reference " + relation.ShapeKey);
            string customExpressionPrefix = "VRM_"
                + (string.Equals(relation.ShapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal)
                    ? figureName
                    : relation.ShapeKey)
                + "_";
            if (!TryGetExpressionMap(instances[0], customExpressionPrefix,
                out Dictionary<string, SourceExpression> expressions,
                out diagnostic)) return false;
            source = new ExpressionReferenceSource(relation, instances[0], renderer, expressions);
            return true;
        }

        private static SkinnedMeshRenderer FindSingleSkinnedMeshRenderer(GameObject root, string role)
        {
            SkinnedMeshRenderer[] renderers = root == null
                ? Array.Empty<SkinnedMeshRenderer>()
                : root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
                throw new InvalidOperationException(role + " must contain exactly one merged SkinnedMeshRenderer.");
            if (renderers[0].sharedMesh == null)
                throw new InvalidOperationException(role + " Renderer has no Mesh.");
            return renderers[0];
        }

        private static bool TryGetExpressionMap(Vrm10Instance instance, string customExpressionPrefix,
            out Dictionary<string, SourceExpression> map, out string diagnostic)
        {
            map = new Dictionary<string, SourceExpression>(StringComparer.Ordinal);
            diagnostic = null;
            if (instance == null || instance.Vrm == null || instance.Vrm.Expression == null)
            {
                diagnostic = "VrmGenerateExpressionReferenceInvalid: VRM graph has no Expression definition.";
                return false;
            }

            foreach (var pair in instance.Vrm.Expression.Clips)
            {
                VRM10Expression clip = pair.Clip;
                if (clip == null) continue;
                if (pair.Preset != ExpressionPreset.custom
                    && (clip.MorphTargetBindings == null || clip.MorphTargetBindings.Length == 0)) continue;
                string name = pair.Preset == ExpressionPreset.custom
                    ? NormalizeCustomExpressionName(clip.name, customExpressionPrefix)
                    : pair.Preset.ToString();
                if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name))
                    map.Add(name, new SourceExpression(pair.Preset, name, clip));
            }
            return true;
        }

        private static string NormalizeCustomExpressionName(string clipName, string customExpressionPrefix)
        {
            if (string.IsNullOrWhiteSpace(clipName)) return null;
            if (!string.IsNullOrWhiteSpace(customExpressionPrefix)
                && clipName.StartsWith(customExpressionPrefix, StringComparison.Ordinal))
            {
                string logicalName = clipName.Substring(customExpressionPrefix.Length);
                return string.IsNullOrWhiteSpace(logicalName) ? null : logicalName;
            }
            return clipName;
        }

        private readonly struct ExpressionDelta
        {
            internal ExpressionDelta(Vector3[] vertices, Vector3[] normals, Vector3[] tangents)
            {
                Vertices = vertices;
                Normals = normals;
                Tangents = tangents;
            }

            internal Vector3[] Vertices { get; }
            internal Vector3[] Normals { get; }
            internal Vector3[] Tangents { get; }
        }

        private static ExpressionDelta ReadExpressionDelta(GameObject root, SkinnedMeshRenderer expectedRenderer,
            VRM10Expression expression)
        {
            Mesh mesh = expectedRenderer.sharedMesh;
            var vertices = new Vector3[mesh.vertexCount];
            var normals = new Vector3[mesh.vertexCount];
            var tangents = new Vector3[mesh.vertexCount];
            foreach (MorphTargetBinding binding in expression.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>())
            {
                Transform bindingTransform = string.IsNullOrEmpty(binding.RelativePath)
                    ? root.transform
                    : root.transform.Find(binding.RelativePath);
                SkinnedMeshRenderer bindingRenderer = bindingTransform == null
                    ? null
                    : bindingTransform.GetComponent<SkinnedMeshRenderer>();
                if (bindingRenderer != expectedRenderer || bindingRenderer.sharedMesh == null)
                    throw new InvalidOperationException("Expression binding must target the merged Reference Renderer.");
                Mesh bindingMesh = bindingRenderer.sharedMesh;
                if (binding.Index < 0 || binding.Index >= bindingMesh.blendShapeCount
                    || bindingMesh.GetBlendShapeFrameCount(binding.Index) == 0)
                    throw new InvalidOperationException("Expression binding index is invalid: " + expression.name);

                var sourceVertices = new Vector3[bindingMesh.vertexCount];
                var sourceNormals = new Vector3[bindingMesh.vertexCount];
                var sourceTangents = new Vector3[bindingMesh.vertexCount];
                bindingMesh.GetBlendShapeFrameVertices(binding.Index,
                    bindingMesh.GetBlendShapeFrameCount(binding.Index) - 1,
                    sourceVertices, sourceNormals, sourceTangents);
                BlendShapeBakeUtility.AddScaled(vertices, sourceVertices, binding.Weight);
                BlendShapeBakeUtility.AddScaled(normals, sourceNormals, binding.Weight);
                BlendShapeBakeUtility.AddScaled(tangents, sourceTangents, binding.Weight);
            }
            return new ExpressionDelta(vertices, normals, tangents);
        }

        private static ExpressionDelta TransformExpressionDelta(ExpressionReferenceSource source,
            SkinnedMeshRenderer outputRenderer, ExpressionDelta delta)
        {
            Matrix4x4 sourceToOutput = outputRenderer.transform.worldToLocalMatrix
                * source.Renderer.transform.localToWorldMatrix;
            if (sourceToOutput == Matrix4x4.identity) return delta;
            var vertices = new Vector3[delta.Vertices.Length];
            var normals = new Vector3[delta.Normals.Length];
            var tangents = new Vector3[delta.Tangents.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = sourceToOutput.MultiplyVector(delta.Vertices[index]);
                normals[index] = sourceToOutput.MultiplyVector(delta.Normals[index]);
                tangents[index] = sourceToOutput.MultiplyVector(delta.Tangents[index]);
            }
            return new ExpressionDelta(vertices, normals, tangents);
        }

        private static ExpressionDelta SubtractDelta(ExpressionDelta from, ExpressionDelta subtract)
        {
            return new ExpressionDelta(
                BlendShapeBakeUtility.Subtract(from.Vertices, subtract.Vertices),
                BlendShapeBakeUtility.Subtract(from.Normals, subtract.Normals),
                BlendShapeBakeUtility.Subtract(from.Tangents, subtract.Tangents));
        }

        private static void EnsureSameTopology(Mesh output, Mesh source, string shapeKey)
        {
            if (output == null || source == null || output.vertexCount != source.vertexCount
                || output.subMeshCount != source.subMeshCount)
                throw new InvalidOperationException("Expression Reference topology does not match Figure for " + shapeKey + ".");
            for (int subMesh = 0; subMesh < output.subMeshCount; subMesh++)
            {
                if (output.GetTopology(subMesh) != source.GetTopology(subMesh))
                    throw new InvalidOperationException("Expression Reference topology does not match Figure for " + shapeKey + ".");
                int[] outputIndices = output.GetIndices(subMesh);
                int[] sourceIndices = source.GetIndices(subMesh);
                if (outputIndices.Length != sourceIndices.Length)
                    throw new InvalidOperationException("Expression Reference topology does not match Figure for " + shapeKey + ".");
                for (int index = 0; index < outputIndices.Length; index++)
                    if (outputIndices[index] != sourceIndices[index])
                        throw new InvalidOperationException("Expression Reference topology does not match Figure for " + shapeKey + ".");
            }
        }

        private static VRM10Expression CreateAndPersistExpression(string figureName, string expressionName,
            GameObject prefabRoot, SkinnedMeshRenderer renderer, string blendShapeName, string vrmFolder,
            ICollection<string> generatedPaths)
        {
            int index = renderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
            if (index < 0) throw new InvalidOperationException("Baked VRM BlendShape is missing: " + blendShapeName);
            string assetName = "VRM_" + figureName + "_" + expressionName;
            VRM10Expression expression = ScriptableObject.CreateInstance<VRM10Expression>();
            expression.name = assetName;
            expression.Prefab = prefabRoot;
            expression.MorphTargetBindings = new[]
            {
                new MorphTargetBinding(GetRelativePath(prefabRoot.transform, renderer.transform), index, 1f)
            };
            expression.MaterialColorBindings = Array.Empty<MaterialColorBinding>();
            expression.MaterialUVBindings = Array.Empty<MaterialUVBinding>();
            expression.NodeTransformBindings = Array.Empty<NodeTransformBinding>();
            return PersistAsset(expression, vrmFolder + "/" + assetName + ".asset", generatedPaths);
        }

        private static VRM10Expression CreateAndPersistEmptyStandardExpression(string figureName,
            ExpressionPreset preset, GameObject prefabRoot, string vrmFolder, ICollection<string> generatedPaths)
        {
            string assetName = "VRM_" + figureName + "_" + preset;
            VRM10Expression expression = ScriptableObject.CreateInstance<VRM10Expression>();
            expression.name = assetName;
            expression.Prefab = prefabRoot;
            expression.MorphTargetBindings = Array.Empty<MorphTargetBinding>();
            expression.MaterialColorBindings = Array.Empty<MaterialColorBinding>();
            expression.MaterialUVBindings = Array.Empty<MaterialUVBinding>();
            expression.NodeTransformBindings = Array.Empty<NodeTransformBinding>();
            return PersistAsset(expression, vrmFolder + "/" + assetName + ".asset", generatedPaths);
        }

        private static void ValidateBakedExpression(GameObject prefabRoot, VRM10Expression expression)
        {
            MorphTargetBinding[] bindings = expression.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>();
            if (bindings.Length != 1 || Mathf.Abs(bindings[0].Weight - 1f) > 0.0001f)
                throw new InvalidOperationException("Generated VRM Expression must have exactly one weight-1 MorphTargetBinding: " + expression.name);
            Transform target = string.IsNullOrEmpty(bindings[0].RelativePath)
                ? prefabRoot.transform
                : prefabRoot.transform.Find(bindings[0].RelativePath);
            SkinnedMeshRenderer renderer = target == null ? null : target.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null
                || bindings[0].Index < 0 || bindings[0].Index >= renderer.sharedMesh.blendShapeCount)
                throw new InvalidOperationException("Generated VRM Expression binding is invalid: " + expression.name);
            if ((expression.MaterialColorBindings ?? Array.Empty<MaterialColorBinding>()).Length != 0
                || (expression.MaterialUVBindings ?? Array.Empty<MaterialUVBinding>()).Length != 0
                || (expression.NodeTransformBindings ?? Array.Empty<NodeTransformBinding>()).Length != 0)
                throw new InvalidOperationException("Generated VRM Expression contains non-morph bindings: " + expression.name);
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var names = new Stack<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names);
        }

        private static void ValidateGeneratedOutput(GameObject prefabRoot, VRM10Object vrm,
            BakedExpressionSet baked, string vrmFolder, IReadOnlyCollection<string> legacyCatalogPaths)
        {
            Vrm10Instance instance = prefabRoot == null ? null : prefabRoot.GetComponent<Vrm10Instance>();
            if (instance == null || instance.Vrm != vrm || vrm == null || vrm.Prefab != prefabRoot)
                throw new InvalidOperationException("Initialized VRM graph is not owned by the generated Figure Prefab.");
            foreach (KeyValuePair<ExpressionPreset, VRM10Expression> pair in baked.Standard)
            {
                VRM10Expression expression = pair.Value;
                if (expression == null || expression.Prefab != prefabRoot)
                    throw new InvalidOperationException("Initialized VRM Expression is not owned by the generated Figure Prefab.");
                if (!IsAssetUnderFolder(AssetDatabase.GetAssetPath(expression), vrmFolder))
                    throw new InvalidOperationException("Initialized VRM Expression is outside the configured VRM folder.");
                ValidateBakedExpression(prefabRoot, expression);
            }
            foreach (VRM10Expression expression in baked.Custom)
            {
                if (expression == null || expression.Prefab != prefabRoot)
                    throw new InvalidOperationException("Generated custom VRM Expression is not owned by the generated Figure Prefab.");
                if (!IsAssetUnderFolder(AssetDatabase.GetAssetPath(expression), vrmFolder))
                    throw new InvalidOperationException("Generated custom VRM Expression is outside the configured VRM folder.");
                ValidateBakedExpression(prefabRoot, expression);
            }
            foreach (var pair in vrm.Expression.Clips)
            {
                if (pair.Clip == null || pair.Preset == ExpressionPreset.custom
                    || baked.Standard.ContainsKey(pair.Preset)) continue;
                if (pair.Clip.Prefab != prefabRoot
                    || !IsAssetUnderFolder(AssetDatabase.GetAssetPath(pair.Clip), vrmFolder)
                    || (pair.Clip.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>()).Length != 0
                    || (pair.Clip.MaterialColorBindings ?? Array.Empty<MaterialColorBinding>()).Length != 0
                    || (pair.Clip.MaterialUVBindings ?? Array.Empty<MaterialUVBinding>()).Length != 0
                    || (pair.Clip.NodeTransformBindings ?? Array.Empty<NodeTransformBinding>()).Length != 0)
                    throw new InvalidOperationException("Initialized empty VRM Expression is invalid: " + pair.Clip.name);
            }
            if (!IsAssetUnderFolder(AssetDatabase.GetAssetPath(vrm), vrmFolder))
                throw new InvalidOperationException("Initialized VRM10Object is outside the configured VRM folder.");
            if (!ValidateVrmFolderContents(vrmFolder, legacyCatalogPaths, out string folderDiagnostic))
                throw new InvalidOperationException(folderDiagnostic);
        }

        private static bool ValidateVrmFolderContents(string vrmFolder,
            IReadOnlyCollection<string> legacyCatalogPaths, out string diagnostic)
        {
            diagnostic = null;
            if (!AssetDatabase.IsValidFolder(vrmFolder))
            {
                diagnostic = "VrmGenerateOutputFolderMissing: Configured VRM output folder does not exist.";
                return false;
            }

            var legacyPaths = legacyCatalogPaths == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(legacyCatalogPaths, StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { vrmFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
                if (!IsAssetUnderFolder(path, vrmFolder)) continue;
                if (legacyPaths.Contains(path) && IsLegacyPhysicsCarrier(path)) continue;

                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                    || !(asset is VRM10Object) && !(asset is VRM10Expression))
                {
                    diagnostic = "VrmGenerateOutputContractViolation: VRM output folder contains a non-UniVRM asset: " + path;
                    return false;
                }
            }
            return true;
        }

        private static bool IsLegacyPhysicsCarrier(string path)
        {
            return path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(path).StartsWith("PHYS_", StringComparison.Ordinal);
        }

        private static T PersistAsset<T>(T asset, string path, ICollection<string> generatedPaths) where T : UnityEngine.Object
        {
            if (asset == null) throw new InvalidOperationException("VRM Generate received a null asset.");
            asset.name = Path.GetFileNameWithoutExtension(path);
            AddGeneratedPath(generatedPaths, path);
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                if (existing.GetType() != asset.GetType())
                    throw new InvalidOperationException("VRM Generate output path has a different asset type: " + path);
                EditorUtility.CopySerialized(asset, existing);
                existing.name = asset.name;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                UnityEngine.Object.DestroyImmediate(asset);
                return (T)existing;
            }

            AssetDatabase.CreateAsset(asset, path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string ResolveVrmFolder(string rootPath, string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/').Trim('/');
            return rootPath.TrimEnd('/') + "/" + normalized;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(parent)) throw new InvalidOperationException("VRM output folder has no valid parent: " + path);
            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)
                && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new InvalidOperationException("VRM output folder could not be created: " + path);
        }

        private static void AddGeneratedPath(ICollection<string> generatedPaths, string path)
        {
            if (generatedPaths == null || string.IsNullOrWhiteSpace(path)) return;
            if (!generatedPaths.Contains(path)) generatedPaths.Add(path);
        }

        private static bool IsAssetUnderFolder(string assetPath, string folder)
        {
            return !string.IsNullOrEmpty(assetPath)
                && assetPath.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal)
                && !assetPath.Contains("../");
        }
    }
}
#endif
