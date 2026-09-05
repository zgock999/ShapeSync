// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniVRM10;
using zgock.ShapeSync;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>
    /// Imports one Reference VRM into a Database transaction. The source
    /// Prefab and its VRM graph are never modified.
    /// </summary>
    public static class ShapeSyncVrmReferenceImporter
    {
        private enum ReferenceKind
        {
            Expression,
            FigurePhysics,
            MeshOutfitPhysics
        }

        /// <summary>Imports the Base/FBM Expression Reference row for one Figure.</summary>
        /// <param name="databaseAssetPath">The project-relative Database Prefab path.</param>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="shapeKey">The Base shape key or FBM identity.</param>
        /// <param name="sourcePrefab">The source VRM Prefab to clone into the Database.</param>
        /// <param name="diagnostic">Receives a diagnostic when import is rejected or fails.</param>
        /// <returns><see langword="true"/> when the Reference is committed; otherwise, <see langword="false"/>.</returns>
        public static bool TryImportExpressionReference(string databaseAssetPath, string figureName, string shapeKey,
            GameObject sourcePrefab, out string diagnostic)
        {
            return TryImport(databaseAssetPath, figureName, shapeKey, sourcePrefab, ReferenceKind.Expression, out diagnostic);
        }

        /// <summary>Imports the Figure Physics Reference row for one Figure.</summary>
        /// <param name="databaseAssetPath">The project-relative Database Prefab path.</param>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="sourcePrefab">The source VRM Prefab to clone into the Database.</param>
        /// <param name="diagnostic">Receives a diagnostic when import is rejected or fails.</param>
        /// <returns><see langword="true"/> when the Reference is committed; otherwise, <see langword="false"/>.</returns>
        public static bool TryImportFigurePhysicsReference(string databaseAssetPath, string figureName,
            GameObject sourcePrefab, out string diagnostic)
        {
            return TryImport(databaseAssetPath, figureName, null, sourcePrefab, ReferenceKind.FigurePhysics, out diagnostic);
        }

        /// <summary>Imports the Physics Reference row for one Mesh Outfit.</summary>
        /// <param name="databaseAssetPath">The project-relative Database Prefab path.</param>
        /// <param name="outfitIdentity">The logical Mesh Outfit identity.</param>
        /// <param name="sourcePrefab">The source VRM Prefab to clone into the Database.</param>
        /// <param name="diagnostic">Receives a diagnostic when import is rejected or fails.</param>
        /// <returns><see langword="true"/> when the Reference is committed; otherwise, <see langword="false"/>.</returns>
        public static bool TryImportMeshOutfitPhysicsReference(string databaseAssetPath, string outfitIdentity,
            GameObject sourcePrefab, out string diagnostic)
        {
            return TryImport(databaseAssetPath, outfitIdentity, null, sourcePrefab, ReferenceKind.MeshOutfitPhysics, out diagnostic);
        }

        /// <summary>Removes the Figure Base/FBM Expression Reference row from a Database transaction.</summary>
        /// <param name="databaseAssetPath">The project-relative Database Prefab path.</param>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="shapeKey">The Base shape key or FBM identity.</param>
        /// <param name="diagnostic">Receives a diagnostic when removal is rejected or fails.</param>
        /// <returns><see langword="true"/> when the Reference is removed and the transaction is committed; otherwise, <see langword="false"/>.</returns>
        public static bool TryRemoveFigureExpressionReference(string databaseAssetPath, string figureName,
            string shapeKey, out string diagnostic)
        {
            return TryRemoveReference(databaseAssetPath, figureName, shapeKey, ReferenceKind.Expression, out diagnostic);
        }

        /// <summary>Removes the Figure Physics Reference row from a Database transaction.</summary>
        /// <param name="databaseAssetPath">The project-relative Database Prefab path.</param>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="diagnostic">Receives a diagnostic when removal is rejected or fails.</param>
        /// <returns><see langword="true"/> when the Reference is removed and the transaction is committed; otherwise, <see langword="false"/>.</returns>
        public static bool TryRemoveFigurePhysicsReference(string databaseAssetPath, string figureName,
            out string diagnostic)
        {
            return TryRemoveReference(databaseAssetPath, figureName, null, ReferenceKind.FigurePhysics, out diagnostic);
        }

        /// <summary>Removes the Mesh Outfit Physics Reference row from a Database transaction.</summary>
        /// <param name="databaseAssetPath">The project-relative Database Prefab path.</param>
        /// <param name="outfitIdentity">The logical Mesh Outfit identity.</param>
        /// <param name="diagnostic">Receives a diagnostic when removal is rejected or fails.</param>
        /// <returns><see langword="true"/> when the Reference is removed and the transaction is committed; otherwise, <see langword="false"/>.</returns>
        public static bool TryRemoveMeshOutfitPhysicsReference(string databaseAssetPath, string outfitIdentity,
            out string diagnostic)
        {
            return TryRemoveReference(databaseAssetPath, outfitIdentity, null, ReferenceKind.MeshOutfitPhysics, out diagnostic);
        }

        private static bool TryImport(string databaseAssetPath, string identity, string shapeKey,
            GameObject sourcePrefab, ReferenceKind kind, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath) || !databaseAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "VRM Reference import requires a Database Prefab path.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(identity))
            {
                diagnostic = "VRM Reference import requires a Figure or Outfit identity.";
                return false;
            }
            if (kind == ReferenceKind.Expression && string.IsNullOrWhiteSpace(shapeKey))
            {
                diagnostic = "VRM Expression Reference import requires a Base or FBM shape key.";
                return false;
            }
            if (!TryValidateSource(sourcePrefab, out Vrm10Instance sourceInstance, out diagnostic)) return false;
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out _, out diagnostic)) return false;

            ReferenceNames names = BuildReferenceNames(identity, shapeKey, kind);
            if (!ReferenceClone.TryCreate(sourcePrefab, names, sourceInstance, kind,
                out ReferenceClone staged, out diagnostic)) return false;

            bool committed = false;
            try
            {
                committed = ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath,
                    (database, intermediate, transaction) =>
                    {
                        ShapeSyncVrmDatabaseRegistry registry = ShapeSyncVrmDatabaseRegistryRegistration.EnsureRegistry(
                            database, databaseAssetPath, transaction, out string registryDiagnostic);
                        if (registry == null) throw new InvalidOperationException(registryDiagnostic);

                        GameObject owner;
                        bool ownerResolved = kind == ReferenceKind.MeshOutfitPhysics
                            ? ShapeSyncDatabaseCanonicalAssetResolver.TryResolveMeshOutfitOwner(database, identity,
                                out owner, out registryDiagnostic)
                            : ShapeSyncDatabaseCanonicalAssetResolver.TryResolveFigureOwner(database, identity,
                                shapeKey ?? ShapeSyncDatabaseRegistry.BaseShapeKey, out owner, out registryDiagnostic);
                        if (!ownerResolved) throw new InvalidOperationException(registryDiagnostic);
                        if (!staged.TryBindCanonicalSurface(owner, databaseAssetPath, kind, out registryDiagnostic))
                            throw new InvalidOperationException(registryDiagnostic);

                        RemoveReference(registry, identity, shapeKey, kind, databaseAssetPath, transaction);

                        foreach (UnityEngine.Object subAsset in staged.SubAssets) transaction.AddSubAsset(subAsset);
                        staged.Root.transform.SetParent(intermediate, false);

                        bool updated;
                        switch (kind)
                        {
                            case ReferenceKind.Expression:
                                updated = registry.TryUpsertFigureExpressionReference(identity, shapeKey, owner, staged.Root,
                                    staged.SubAssets, out _, out registryDiagnostic);
                                break;
                            case ReferenceKind.FigurePhysics:
                                updated = registry.TryUpsertFigurePhysicsReference(identity, owner, staged.Root,
                                    staged.SubAssets, out _, out registryDiagnostic);
                                break;
                            default:
                                updated = registry.TryUpsertMeshOutfitPhysicsReference(identity, owner, staged.Root,
                                    staged.SubAssets, out _, out registryDiagnostic);
                                break;
                        }

                        if (!updated) throw new InvalidOperationException(registryDiagnostic);
                    }, out diagnostic);

                if (!committed) return false;
                staged.MarkPersisted();
                return true;
            }
            finally
            {
                if (!committed) staged.Dispose();
            }
        }

        /// <summary>Stores the generated names assigned to one retained Reference VRM graph.</summary>
        private readonly struct ReferenceNames
        {
            /// <summary>Creates a generated-name set for one Reference VRM graph.</summary>
            /// <param name="prefabName">The generated Reference Prefab name.</param>
            /// <param name="meshName">The generated merged Expression Mesh name, or <see langword="null"/> for Physics.</param>
            /// <param name="assetPrefix">The prefix applied to retained VRM sub-assets.</param>
            public ReferenceNames(string prefabName, string meshName, string assetPrefix)
            {
                PrefabName = prefabName;
                MeshName = meshName;
                AssetPrefix = assetPrefix;
            }

            /// <summary>Gets the generated Reference Prefab name.</summary>
            /// <value>The generated Prefab name.</value>
            public string PrefabName { get; }
            /// <summary>Gets the generated merged Expression Mesh name.</summary>
            /// <value>The generated Mesh name, or <see langword="null"/> for Physics references.</value>
            public string MeshName { get; }
            /// <summary>Gets the prefix applied to retained VRM sub-assets.</summary>
            /// <value>The generated sub-asset name prefix.</value>
            public string AssetPrefix { get; }
        }

        private static ReferenceNames BuildReferenceNames(string identity, string shapeKey, ReferenceKind kind)
        {
            if (kind == ReferenceKind.Expression)
            {
                string expressionIdentity = string.Equals(shapeKey, ShapeSyncDatabaseRegistry.BaseShapeKey,
                    StringComparison.Ordinal) ? identity : shapeKey;
                string prefix = "VRM_" + expressionIdentity;
                return new ReferenceNames(prefix, prefix + "_Mesh", prefix);
            }

            string physicsPrefix = "PHYS_" + identity;
            return new ReferenceNames(physicsPrefix, null, physicsPrefix);
        }

        private static bool TryRemoveReference(string databaseAssetPath, string identity, string shapeKey,
            ReferenceKind kind, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath)
                || !databaseAssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "VRM Reference removal requires a Database Prefab path.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(identity))
            {
                diagnostic = "VRM Reference removal requires a Figure or Outfit identity.";
                return false;
            }
            if (kind == ReferenceKind.Expression && string.IsNullOrWhiteSpace(shapeKey))
            {
                diagnostic = "VRM Expression Reference removal requires a Base or FBM shape key.";
                return false;
            }
            if (!ShapeSyncDatabaseAsset.TryOpen(databaseAssetPath, out _, out diagnostic)) return false;
            if (!ShapeSyncVrmDatabaseRegistryRegistration.TryGetRegistry(databaseAssetPath,
                out ShapeSyncVrmDatabaseRegistry registry, out diagnostic)) return false;
            if (registry == null) return true;

            bool exists = kind == ReferenceKind.Expression
                ? registry.FigureExpressionReferences.Any(value => value != null
                    && value.FigureName == identity && value.ShapeKey == shapeKey)
                : kind == ReferenceKind.FigurePhysics
                    ? registry.FigurePhysicsReferences.Any(value => value != null && value.FigureName == identity)
                    : registry.MeshOutfitPhysicsReferences.Any(value => value != null
                        && value.OutfitIdentity == identity);
            if (!exists) return true;

            return ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath,
                (database, _, transaction) =>
                {
                    if (!ShapeSyncVrmDatabaseRegistryRegistration.TryGetRegistry(databaseAssetPath,
                        out ShapeSyncVrmDatabaseRegistry transactionRegistry, out string lookupDiagnostic))
                        throw new InvalidOperationException(lookupDiagnostic);
                    if (transactionRegistry == null) return;
                    RemoveReference(transactionRegistry, identity, shapeKey, kind, databaseAssetPath, transaction);
                }, out diagnostic);
        }

        private static bool TryValidateSource(GameObject sourcePrefab, out Vrm10Instance instance, out string diagnostic)
        {
            instance = null;
            diagnostic = null;
            if (sourcePrefab == null)
            {
                diagnostic = "VRM Reference import requires a source Prefab.";
                return false;
            }

            Vrm10Instance[] instances = sourcePrefab.GetComponentsInChildren<Vrm10Instance>(true)
                .Where(value => value != null && value.Vrm != null).ToArray();
            if (instances.Length != 1)
            {
                diagnostic = "VRM Reference import requires exactly one Vrm10Instance with a VRM10Object.";
                return false;
            }
            if (instances[0].Vrm.Expression == null)
            {
                diagnostic = "VRM Reference import requires a VRM10Object Expression definition.";
                return false;
            }

            instance = instances[0];
            return true;
        }

        private static void RemoveReference(ShapeSyncVrmDatabaseRegistry registry, string identity,
            string shapeKey, ReferenceKind kind, string databaseAssetPath,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            GameObject previous = null;
            IReadOnlyList<UnityEngine.Object> ownedAssets = null;
            switch (kind)
            {
                case ReferenceKind.Expression:
                    ShapeSyncVrmDatabaseRegistry.FigureExpressionReference expression = registry.FigureExpressionReferences
                        .FirstOrDefault(value => value != null && value.FigureName == identity && value.ShapeKey == shapeKey);
                    if (expression != null) { previous = expression.ReferencePrefab; ownedAssets = expression.OwnedAssets; }
                    registry.TryRemoveFigureExpressionReference(identity, shapeKey, out _);
                    break;
                case ReferenceKind.FigurePhysics:
                    ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference figure = registry.FigurePhysicsReferences
                        .FirstOrDefault(value => value != null && value.FigureName == identity);
                    if (figure != null) { previous = figure.ReferencePrefab; ownedAssets = figure.OwnedAssets; }
                    registry.TryRemoveFigurePhysicsReference(identity, out _);
                    break;
                default:
                    ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference outfit = registry.MeshOutfitPhysicsReferences
                        .FirstOrDefault(value => value != null && value.OutfitIdentity == identity);
                    if (outfit != null) { previous = outfit.ReferencePrefab; ownedAssets = outfit.OwnedAssets; }
                    registry.TryRemoveMeshOutfitPhysicsReference(identity, out _);
                    break;
            }

            if (ownedAssets != null)
            {
                foreach (UnityEngine.Object ownedAsset in ownedAssets)
                {
                    if (ownedAsset != null && AssetDatabase.GetAssetPath(ownedAsset) == databaseAssetPath)
                        transaction.RemoveSubAsset(ownedAsset);
                }
            }
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous, true);
        }

        /// <summary>Owns an uncommitted Reference VRM clone and its Database sub-assets.</summary>
        private sealed class ReferenceClone : IDisposable
        {
            private bool persisted;
            private ShapeSyncFigureMeshMerger.Result mergedRendererResult;
            private readonly string expressionMeshName;

            private ReferenceClone(GameObject root, List<UnityEngine.Object> subAssets, string expressionMeshName)
            {
                Root = root;
                SubAssets = subAssets;
                this.expressionMeshName = expressionMeshName;
            }

            /// <summary>Gets the cloned Reference VRM root.</summary>
            /// <value>The uncommitted or persisted cloned Prefab root.</value>
            public GameObject Root { get; private set; }
            /// <summary>Gets the sub-assets retained with the cloned Reference VRM.</summary>
            /// <value>The staged VRM, Expression, Mesh, and other supported sub-assets.</value>
            public List<UnityEngine.Object> SubAssets { get; }

            /// <summary>Creates a detached Reference VRM clone without modifying the source Prefab.</summary>
            /// <param name="sourcePrefab">The source VRM Prefab to clone.</param>
            /// <param name="names">The generated names for the clone and its sub-assets.</param>
            /// <param name="sourceInstance">The VRM instance contained by <paramref name="sourcePrefab"/>.</param>
            /// <param name="kind">The Reference kind being cloned.</param>
            /// <param name="clone">Receives the detached clone when creation succeeds.</param>
            /// <param name="diagnostic">Receives a diagnostic when cloning fails.</param>
            /// <returns><see langword="true"/> when the detached clone is ready for canonical binding; otherwise, <see langword="false"/>.</returns>
            public static bool TryCreate(GameObject sourcePrefab, ReferenceNames names, Vrm10Instance sourceInstance,
                ReferenceKind kind, out ReferenceClone clone, out string diagnostic)
            {
                clone = null;
                diagnostic = null;
                GameObject root = null;
                var subAssets = new List<UnityEngine.Object>();
                try
                {
                    root = UnityEngine.Object.Instantiate(sourcePrefab);
                    root.name = names.PrefabName;
                    Transform clonedInstanceTransform = FindByRelativePath(root.transform,
                        GetRelativePath(sourcePrefab.transform, sourceInstance.transform));
                    Vrm10Instance clonedInstance = clonedInstanceTransform == null
                        ? null
                        : clonedInstanceTransform.GetComponent<Vrm10Instance>();
                    if (clonedInstance == null) throw new InvalidOperationException("Cloned VRM instance could not be resolved.");

                    var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>
                    {
                        [sourcePrefab] = root,
                        [sourceInstance.Vrm] = UnityEngine.Object.Instantiate(sourceInstance.Vrm)
                    };
                    VRM10Object clonedVrm = (VRM10Object)map[sourceInstance.Vrm];
                    clonedVrm.name = names.AssetPrefix + "_" + sourceInstance.Vrm.name;
                    subAssets.Add(clonedVrm);
                    var expressionMap = new Dictionary<VRM10Expression, VRM10Expression>();

                    foreach (var clip in sourceInstance.Vrm.Expression.Clips)
                    {
                        if (clip.Clip == null || map.ContainsKey(clip.Clip)) continue;
                        VRM10Expression clonedClip = UnityEngine.Object.Instantiate(clip.Clip);
                        clonedClip.name = names.AssetPrefix + "_" + clip.Clip.name;
                        map.Add(clip.Clip, clonedClip);
                        expressionMap.Add(clip.Clip, clonedClip);
                        subAssets.Add(clonedClip);
                    }
                    clonedVrm.Expression.Replace(expressionMap);

                    foreach (UnityEngine.Object dependency in CollectCloneableDependencies(sourcePrefab, sourceInstance, kind))
                    {
                        if (dependency == null || map.ContainsKey(dependency)) continue;
                        UnityEngine.Object copied = UnityEngine.Object.Instantiate(dependency);
                        copied.name = names.AssetPrefix + "_" + dependency.name;
                        map.Add(dependency, copied);
                        subAssets.Add(copied);
                    }

                    clonedVrm.Prefab = root;
                    foreach (VRM10Expression expression in subAssets.OfType<VRM10Expression>()) expression.Prefab = root;
                    RebindObjectReferences(root, subAssets, map);
                    clonedInstance.Vrm = clonedVrm;
                    clone = new ReferenceClone(root, subAssets, names.MeshName);
                    return true;
                }
                catch (Exception exception)
                {
                    if (root != null) UnityEngine.Object.DestroyImmediate(root);
                    foreach (UnityEngine.Object subAsset in subAssets)
                        if (subAsset != null && !AssetDatabase.Contains(subAsset)) UnityEngine.Object.DestroyImmediate(subAsset);
                    diagnostic = "VRM Reference import could not clone the source VRM: " + exception.Message;
                    return false;
                }
            }

            /// <summary>Binds cloned Renderers and Mesh-bearing components to the explicit canonical owner surface.</summary>
            /// <param name="owner">The canonical Figure or Mesh Outfit owner.</param>
            /// <param name="databaseAssetPath">The Database Prefab path that must own retained assets.</param>
            /// <param name="kind">The Reference kind whose surface rules should be applied.</param>
            /// <param name="diagnostic">Receives a diagnostic when canonical binding fails.</param>
            /// <returns><see langword="true"/> when every required surface is bound; otherwise, <see langword="false"/>.</returns>
            public bool TryBindCanonicalSurface(GameObject owner, string databaseAssetPath, ReferenceKind kind,
                out string diagnostic)
            {
                diagnostic = null;
                if (owner == null)
                {
                    diagnostic = "VRM Reference requires a Canonical owner before surface binding.";
                    return false;
                }

                if (!TryMergeReferenceRenderers(owner, kind, out diagnostic)) return false;

                foreach (Renderer renderer in Root.GetComponentsInChildren<Renderer>(true))
                {
                    string relativePath = GetRelativePath(Root.transform, renderer.transform);
                    Transform ownerTransform = FindByRelativePath(owner.transform, relativePath);
                    Renderer ownerRenderer = ownerTransform == null ? null : ownerTransform.GetComponent(renderer.GetType()) as Renderer;
                    if (ownerRenderer == null)
                    {
                        diagnostic = "Canonical owner is missing the Reference renderer at path: " + relativePath;
                        return false;
                    }

                    Material[] materials = ownerRenderer.sharedMaterials;
                    if (materials == null)
                    {
                        diagnostic = "Canonical owner materials are missing for the Reference renderer at path: " + relativePath;
                        return false;
                    }
                    if (kind == ReferenceKind.Expression
                        && renderer.sharedMaterials.Length != materials.Length)
                    {
                        diagnostic = "Canonical owner material slots do not match the Reference renderer at path: " + relativePath;
                        return false;
                    }
                    for (int index = 0; index < materials.Length; index++)
                    {
                        if (materials[index] == null || AssetDatabase.GetAssetPath(materials[index]) != databaseAssetPath)
                        {
                            diagnostic = "Canonical owner material must be a Database-owned Material at path: " + relativePath;
                            return false;
                        }
                    }
                    renderer.sharedMaterials = materials;
                }

                if (kind == ReferenceKind.Expression)
                {
                    foreach (MeshFilter filter in Root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh != null && !SubAssets.Contains(filter.sharedMesh))
                        {
                            diagnostic = "Expression Reference Mesh ownership is not isolated to this relation.";
                            return false;
                        }
                    }
                    foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (renderer.sharedMesh != null && !SubAssets.Contains(renderer.sharedMesh))
                        {
                            diagnostic = "Expression Reference Mesh ownership is not isolated to this relation.";
                            return false;
                        }
                    }
                    return true;
                }

                foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (!TryBindCanonicalMesh(owner, renderer, databaseAssetPath, out diagnostic)) return false;
                foreach (MeshFilter filter in Root.GetComponentsInChildren<MeshFilter>(true))
                    if (!TryBindCanonicalMesh(owner, filter, databaseAssetPath, out diagnostic)) return false;
                foreach (MeshCollider collider in Root.GetComponentsInChildren<MeshCollider>(true))
                    if (!TryBindCanonicalMesh(owner, collider, databaseAssetPath, out diagnostic)) return false;
                return true;
            }

            /// <summary>
            /// Figure import leaves one merged SkinnedMeshRenderer at a canonical path,
            /// while a source VRM commonly still has separate Body/Face renderers. When
            /// Expression References always require the same merge as Figure import so
            /// Expression Bake sees the Figure-compatible topology. Physics References
            /// also merge when their renderer paths do not line up, then bind to the
            /// Canonical Mesh as before.
            /// </summary>
            private bool TryMergeReferenceRenderers(GameObject owner, ReferenceKind kind,
                out string diagnostic)
            {
                diagnostic = null;
                SkinnedMeshRenderer[] sourceRenderers = Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (sourceRenderers.Length == 0) return true;

                bool hasUnmatchedRenderer = sourceRenderers.Any(sourceRenderer =>
                {
                    string path = GetRelativePath(Root.transform, sourceRenderer.transform);
                    Transform ownerTransform = FindByRelativePath(owner.transform, path);
                    return ownerTransform == null || ownerTransform.GetComponent<SkinnedMeshRenderer>() == null;
                });
                if (kind != ReferenceKind.Expression && !hasUnmatchedRenderer) return true;

                SkinnedMeshRenderer[] ownerRenderers = owner.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (ownerRenderers.Length != 1)
                {
                    diagnostic = "Canonical owner must have exactly one merged Reference renderer when VRM renderer paths do not match.";
                    return false;
                }

                ShapeSyncFigureMeshMerger.Result merge;
                bool mergeSucceeded;
                if (kind == ReferenceKind.Expression)
                    mergeSucceeded = ShapeSyncFigureMeshMerger.TryMergeOwned(Root, sourceRenderers,
                        out merge, out diagnostic);
                else
                    mergeSucceeded = ShapeSyncFigureMeshMerger.TryMergeOwnedGeometryOnly(Root, sourceRenderers,
                        out merge, out diagnostic);
                if (!mergeSucceeded) return false;

                try
                {
                    string ownerPath = GetRelativePath(owner.transform, ownerRenderers[0].transform);
                    Mesh mergedMesh = merge.Renderer.sharedMesh;
                    if (!TryInstallMergedRendererAtPath(merge.Root, merge.Renderer, ownerPath,
                        out string mergedRendererPath, out diagnostic)) return false;

                    if (kind == ReferenceKind.Expression)
                    {
                        if (mergedMesh == null)
                        {
                            diagnostic = "Expression Reference renderer merge did not produce a Mesh.";
                            return false;
                        }
                        mergedMesh.name = expressionMeshName;
                        if (!TryRemapExpressionBindings(Root, sourceRenderers, mergedMesh, mergedRendererPath,
                            out diagnostic)) return false;
                        if (!SubAssets.Contains(mergedMesh)) SubAssets.Add(mergedMesh);
                        RemoveUnreferencedExpressionMeshes(merge.Root, mergedMesh);
                    }

                    merge.Root.name = Root.name;
                    Vrm10Instance mergedInstance = merge.Root.GetComponentsInChildren<Vrm10Instance>(true)
                        .SingleOrDefault(value => value != null);
                    if (mergedInstance == null || mergedInstance.Vrm == null)
                    {
                        diagnostic = "Merged VRM Reference lost its Vrm10Instance.";
                        return false;
                    }
                    mergedInstance.Vrm.Prefab = merge.Root;
                    foreach (VRM10Expression expression in SubAssets.OfType<VRM10Expression>())
                        expression.Prefab = merge.Root;

                    GameObject previousRoot = Root;
                    Root = merge.Root;
                    mergedRendererResult = merge;
                    merge = null;
                    UnityEngine.Object.DestroyImmediate(previousRoot);
                    return true;
                }
                finally
                {
                    merge?.Dispose();
                }
            }

            private static bool TryInstallMergedRendererAtPath(GameObject root, SkinnedMeshRenderer mergedRenderer,
                string desiredPath, out string rendererPath, out string diagnostic)
            {
                rendererPath = desiredPath;
                diagnostic = null;
                if (root == null || mergedRenderer == null)
                {
                    diagnostic = "Reference renderer merge could not resolve its destination path.";
                    return false;
                }

                int separator = string.IsNullOrEmpty(desiredPath) ? -1 : desiredPath.LastIndexOf('/');
                string parentPath = separator < 0 ? string.Empty : desiredPath.Substring(0, separator);
                string leafName = separator < 0 ? (string.IsNullOrEmpty(desiredPath) ? root.name : desiredPath) : desiredPath.Substring(separator + 1);
                Transform parent = FindOrCreateRelativePath(root.transform, parentPath);
                if (parent == null)
                {
                    diagnostic = "Reference renderer merge could not create the Canonical renderer path: " + desiredPath;
                    return false;
                }

                if (string.IsNullOrEmpty(desiredPath))
                {
                    SkinnedMeshRenderer installed = root.GetComponent<SkinnedMeshRenderer>();
                    if (installed != null)
                    {
                        diagnostic = "Reference renderer merge destination already has a Renderer at the root path.";
                        return false;
                    }
                    installed = root.AddComponent<SkinnedMeshRenderer>();
                    EditorUtility.CopySerialized(mergedRenderer, installed);
                    UnityEngine.Object.DestroyImmediate(mergedRenderer.gameObject);
                    return true;
                }

                Transform existing = parent.Find(leafName);
                if (existing != null && existing != mergedRenderer.transform)
                {
                    if (existing.GetComponent<Renderer>() != null)
                    {
                        diagnostic = "Reference renderer merge destination already has a Renderer at path: " + desiredPath;
                        return false;
                    }

                    SkinnedMeshRenderer installed = existing.gameObject.AddComponent<SkinnedMeshRenderer>();
                    EditorUtility.CopySerialized(mergedRenderer, installed);
                    UnityEngine.Object.DestroyImmediate(mergedRenderer.gameObject);
                    return true;
                }

                mergedRenderer.transform.SetParent(parent, false);
                mergedRenderer.gameObject.name = leafName;
                return true;
            }

            private static Transform FindOrCreateRelativePath(Transform root, string path)
            {
                Transform current = root;
                if (string.IsNullOrEmpty(path)) return current;
                foreach (string segment in path.Split('/'))
                {
                    if (string.IsNullOrEmpty(segment)) continue;
                    Transform child = current.Find(segment);
                    if (child == null)
                    {
                        child = new GameObject(segment).transform;
                        child.SetParent(current, false);
                    }
                    current = child;
                }
                return current;
            }

            private bool TryRemapExpressionBindings(GameObject sourceRoot,
                IReadOnlyList<SkinnedMeshRenderer> sourceRenderers, Mesh mergedMesh, string mergedRendererPath,
                out string diagnostic)
            {
                diagnostic = null;
                var mergedIndices = new Dictionary<SkinnedMeshRenderer, int[]>();
                var usedNames = new HashSet<string>();
                foreach (SkinnedMeshRenderer sourceRenderer in sourceRenderers)
                {
                    Mesh sourceMesh = sourceRenderer.sharedMesh;
                    int[] indices = new int[sourceMesh == null ? 0 : sourceMesh.blendShapeCount];
                    for (int shapeIndex = 0; shapeIndex < indices.Length; shapeIndex++)
                    {
                        string mergedName = MakeMergedBlendShapeName(sourceMesh.GetBlendShapeName(shapeIndex),
                            sourceRenderer.name, usedNames);
                        indices[shapeIndex] = mergedMesh.GetBlendShapeIndex(mergedName);
                    }
                    mergedIndices.Add(sourceRenderer, indices);
                }

                foreach (VRM10Expression expression in SubAssets.OfType<VRM10Expression>())
                {
                    MorphTargetBinding[] bindings = expression.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>();
                    for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                    {
                        MorphTargetBinding binding = bindings[bindingIndex];
                        Transform bindingTransform = FindByRelativePath(sourceRoot.transform, binding.RelativePath);
                        SkinnedMeshRenderer bindingRenderer = bindingTransform == null
                            ? null
                            : bindingTransform.GetComponent<SkinnedMeshRenderer>();
                        if (bindingRenderer == null || !mergedIndices.TryGetValue(bindingRenderer, out int[] indices)
                            || binding.Index < 0 || binding.Index >= indices.Length || indices[binding.Index] < 0)
                        {
                            diagnostic = "VRM Expression binding could not be remapped during Reference renderer merge: "
                                + expression.name;
                            return false;
                        }
                        binding.RelativePath = mergedRendererPath;
                        binding.Index = indices[binding.Index];
                        bindings[bindingIndex] = binding;
                    }
                    expression.MorphTargetBindings = bindings;
                    EditorUtility.SetDirty(expression);
                }
                return true;
            }

            private void RemoveUnreferencedExpressionMeshes(GameObject mergedRoot, Mesh retainedMesh)
            {
                for (int index = SubAssets.Count - 1; index >= 0; index--)
                {
                    Mesh candidate = SubAssets[index] as Mesh;
                    if (candidate == null || candidate == retainedMesh || IsReferencedByRetainedGraph(mergedRoot, candidate)) continue;
                    SubAssets.RemoveAt(index);
                    if (!AssetDatabase.Contains(candidate)) UnityEngine.Object.DestroyImmediate(candidate);
                }
            }

            private bool IsReferencedByRetainedGraph(GameObject mergedRoot, UnityEngine.Object candidate)
            {
                IEnumerable<UnityEngine.Object> targets = mergedRoot.GetComponentsInChildren<Component>(true)
                    .Cast<UnityEngine.Object>()
                    .Concat(SubAssets)
                    .Where(value => value != null && value != candidate)
                    .Distinct();
                foreach (UnityEngine.Object target in targets)
                {
                    SerializedObject serialized = new SerializedObject(target);
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType == SerializedPropertyType.ObjectReference
                            && property.objectReferenceValue == candidate) return true;
                    }
                }
                return false;
            }

            private static string MakeMergedBlendShapeName(string sourceShapeName, string rendererName,
                HashSet<string> usedNames)
            {
                string baseName = string.IsNullOrEmpty(sourceShapeName) ? "BlendShape" : sourceShapeName;
                if (usedNames.Add(baseName)) return baseName;
                string rendererPrefixedName = string.IsNullOrEmpty(rendererName) ? baseName : rendererName + "/" + baseName;
                if (usedNames.Add(rendererPrefixedName)) return rendererPrefixedName;
                int suffix = 1;
                string candidate;
                do
                {
                    candidate = rendererPrefixedName + "_" + suffix;
                    suffix++;
                }
                while (!usedNames.Add(candidate));
                return candidate;
            }

            private bool TryBindCanonicalMesh(GameObject owner, Component sourceComponent, string databaseAssetPath,
                out string diagnostic)
            {
                diagnostic = null;
                string relativePath = GetRelativePath(Root.transform, sourceComponent.transform);
                Transform ownerTransform = FindByRelativePath(owner.transform, relativePath);
                Component ownerComponent = ownerTransform == null ? null : ownerTransform.GetComponent(sourceComponent.GetType());
                Mesh canonicalMesh = ownerComponent is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : ownerComponent is MeshFilter filter ? filter.sharedMesh
                    : ownerComponent is MeshCollider collider ? collider.sharedMesh : null;
                if (canonicalMesh == null || AssetDatabase.GetAssetPath(canonicalMesh) != databaseAssetPath)
                {
                    diagnostic = "Canonical owner Mesh is missing or not Database-owned at path: " + relativePath;
                    return false;
                }
                if (ownerComponent is SkinnedMeshRenderer && sourceComponent is SkinnedMeshRenderer sourceSkinned)
                    sourceSkinned.sharedMesh = canonicalMesh;
                else if (ownerComponent is MeshFilter && sourceComponent is MeshFilter sourceFilter)
                    sourceFilter.sharedMesh = canonicalMesh;
                else if (ownerComponent is MeshCollider && sourceComponent is MeshCollider sourceCollider)
                    sourceCollider.sharedMesh = canonicalMesh;
                return true;
            }

            /// <summary>Marks the clone as transaction-persisted so disposal leaves its retained assets intact.</summary>
            public void MarkPersisted() => persisted = true;

            /// <inheritdoc />
            public void Dispose()
            {
                if (persisted) return;
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
                mergedRendererResult?.Dispose();
                foreach (UnityEngine.Object subAsset in SubAssets)
                    if (subAsset != null && !AssetDatabase.Contains(subAsset)) UnityEngine.Object.DestroyImmediate(subAsset);
            }

            private static IEnumerable<UnityEngine.Object> CollectCloneableDependencies(GameObject root,
                Vrm10Instance sourceInstance, ReferenceKind kind)
            {
                var result = new HashSet<UnityEngine.Object>();
                var targets = root.GetComponentsInChildren<Component>(true).Cast<UnityEngine.Object>()
                    .Concat(new UnityEngine.Object[] { sourceInstance.Vrm })
                    .Concat(sourceInstance.Vrm.Expression.Clips.Where(value => value.Clip != null).Select(value => value.Clip));
                foreach (UnityEngine.Object target in targets)
                {
                    if (target == null) continue;
                    SerializedObject serialized = new SerializedObject(target);
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        UnityEngine.Object reference = property.objectReferenceValue;
                        if (reference is Avatar || (kind == ReferenceKind.Expression && reference is Mesh)) result.Add(reference);
                    }
                }
                return result;
            }

            private static void RebindObjectReferences(GameObject root, IEnumerable<UnityEngine.Object> subAssets,
                IReadOnlyDictionary<UnityEngine.Object, UnityEngine.Object> map)
            {
                var targets = root.GetComponentsInChildren<Component>(true).Cast<UnityEngine.Object>()
                    .Concat(subAssets).Where(value => value != null).Distinct().ToArray();
                foreach (UnityEngine.Object target in targets)
                {
                    SerializedObject serialized = new SerializedObject(target);
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    bool changed = false;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (property.objectReferenceValue == null) continue;
                        if (!map.TryGetValue(property.objectReferenceValue, out UnityEngine.Object replacement)) continue;
                        property.objectReferenceValue = replacement;
                        changed = true;
                    }
                    if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            private static Transform FindByRelativePath(Transform root, string path)
            {
                return string.IsNullOrEmpty(path) ? root : root.Find(path);
            }

            private static string GetRelativePath(Transform root, Transform target)
            {
                if (root == target) return string.Empty;
                var names = new Stack<string>();
                Transform current = target;
                while (current != null && current != root)
                {
                    names.Push(current.name);
                    current = current.parent;
                }
                return current == root ? string.Join("/", names.ToArray()) : null;
            }
        }
    }
}
#endif
