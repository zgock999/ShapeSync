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
    /// Registers VRM Database admission with Core Editor and owns the creation
    /// of the optional registry/marker pair.
    /// </summary>
    [InitializeOnLoad]
    internal static class ShapeSyncVrmDatabaseRegistryRegistration
    {
        static ShapeSyncVrmDatabaseRegistryRegistration()
        {
            ShapeSyncDatabaseOptionalRegistryProvider.RegisterVrmValidator(ValidateDatabase);
        }

        internal static bool TryGetRegistry(string assetPath, out ShapeSyncVrmDatabaseRegistry registry, out string diagnostic)
        {
            registry = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                diagnostic = "VRM Registry lookup requires a Database asset path.";
                return false;
            }

            ShapeSyncVrmDatabaseRegistry[] registries = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<ShapeSyncVrmDatabaseRegistry>().ToArray();
            if (registries.Length > 1)
            {
                diagnostic = "A Database may contain at most one VRM Registry.";
                return false;
            }

            registry = registries.Length == 0 ? null : registries[0];
            return true;
        }

        internal static ShapeSyncVrmDatabaseRegistry EnsureRegistry(
            ShapeSyncDatabase database,
            string assetPath,
            ShapeSyncDatabaseTransaction.EditContext transaction,
            out string diagnostic)
        {
            diagnostic = null;
            if (database == null || transaction == null)
            {
                diagnostic = "VRM Registry creation requires a Database transaction.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                diagnostic = "VRM Registry creation requires the persistent Database asset path.";
                return null;
            }
            if (!TryGetRegistry(assetPath, out ShapeSyncVrmDatabaseRegistry registry, out diagnostic)) return null;
            if (registry == null)
            {
                ShapeSyncDatabaseOptionalFeatureMarker marker = ShapeSyncDatabaseOptionalFeatureMarker.Create(
                    ShapeSyncVrmDatabaseRegistry.FeatureId);
                registry = ScriptableObject.CreateInstance<ShapeSyncVrmDatabaseRegistry>();
                registry.name = "ShapeSyncVrmDatabaseRegistry";
                registry.SetFeatureMarker(marker);
                transaction.AddSubAsset(marker);
                transaction.AddSubAsset(registry);
            }
            else if (registry.FeatureMarker == null)
            {
                ShapeSyncDatabaseOptionalFeatureMarker marker = ShapeSyncDatabaseOptionalFeatureMarker.Create(
                    ShapeSyncVrmDatabaseRegistry.FeatureId);
                registry.SetFeatureMarker(marker);
                transaction.AddSubAsset(marker);
            }
            else if (!registry.HasValidFeatureMarker)
            {
                diagnostic = "VRM Registry contains an invalid optional feature marker.";
                return null;
            }

            return registry;
        }

        private static ShapeSyncDatabaseDiagnostic ValidateDatabase(string assetPath)
        {
            if (!TryGetRegistry(assetPath, out ShapeSyncVrmDatabaseRegistry registry, out string lookupDiagnostic))
            {
                return Invalid(ShapeSyncDatabaseDiagnosticCode.EntityCardinality, lookupDiagnostic);
            }
            if (registry == null)
            {
                return Invalid(ShapeSyncDatabaseDiagnosticCode.RegistryRequired,
                    "Database contains a VRM feature marker but no VRM Registry sub-asset.");
            }
            if (AssetDatabase.GetAssetPath(registry) != assetPath)
            {
                return Invalid(ShapeSyncDatabaseDiagnosticCode.EntityInvalid,
                    "VRM Registry must be a sub-asset of the opened Database.");
            }
            if (!registry.HasValidFeatureMarker)
            {
                return Invalid(ShapeSyncDatabaseDiagnosticCode.EntityInvalid,
                    "VRM Registry is missing its VRM feature marker.");
            }

            if (!TryValidateExpressionReferences(assetPath, registry, out string diagnostic))
                return Invalid(ShapeSyncDatabaseDiagnosticCode.RelationMissing, diagnostic);
            if (!TryValidateFigurePhysicsReferences(assetPath, registry, out diagnostic))
                return Invalid(ShapeSyncDatabaseDiagnosticCode.RelationMissing, diagnostic);
            if (!TryValidateOutfitPhysicsReferences(assetPath, registry, out diagnostic))
                return Invalid(ShapeSyncDatabaseDiagnosticCode.RelationMissing, diagnostic);
            return null;
        }

        private static bool TryValidateExpressionReferences(string assetPath,
            ShapeSyncVrmDatabaseRegistry registry, out string diagnostic)
        {
            diagnostic = null;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncVrmDatabaseRegistry.FigureExpressionReference entry
                in registry.FigureExpressionReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.FigureExpressionReference>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.FigureName) || string.IsNullOrWhiteSpace(entry.ShapeKey))
                {
                    diagnostic = "VRM Expression Reference contains an incomplete Figure/shape relation.";
                    return false;
                }
                if (!keys.Add(entry.FigureName + "\n" + entry.ShapeKey))
                {
                    diagnostic = "VRM Expression Reference contains a duplicate Figure/shape relation.";
                    return false;
                }
                if (!TryValidateReferencePrefab(assetPath, entry.OwnerPrefab, entry.ReferencePrefab, entry.OwnedAssets,
                    "Expression", allowsMesh: true, out diagnostic)) return false;
            }
            return true;
        }

        private static bool TryValidateFigurePhysicsReferences(string assetPath,
            ShapeSyncVrmDatabaseRegistry registry, out string diagnostic)
        {
            diagnostic = null;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference entry
                in registry.FigurePhysicsReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.FigureName))
                {
                    diagnostic = "VRM Figure Physics Reference contains an incomplete Figure relation.";
                    return false;
                }
                if (!keys.Add(entry.FigureName))
                {
                    diagnostic = "VRM Figure Physics Reference contains a duplicate Figure relation.";
                    return false;
                }
                if (!TryValidateReferencePrefab(assetPath, entry.OwnerPrefab, entry.ReferencePrefab, entry.OwnedAssets,
                    "Figure Physics", allowsMesh: false, out diagnostic)) return false;
            }
            return true;
        }

        private static bool TryValidateOutfitPhysicsReferences(string assetPath,
            ShapeSyncVrmDatabaseRegistry registry, out string diagnostic)
        {
            diagnostic = null;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference entry
                in registry.MeshOutfitPhysicsReferences ?? Array.Empty<ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.OutfitIdentity))
                {
                    diagnostic = "VRM Mesh Outfit Physics Reference contains an incomplete Outfit relation.";
                    return false;
                }
                if (!keys.Add(entry.OutfitIdentity))
                {
                    diagnostic = "VRM Mesh Outfit Physics Reference contains a duplicate Outfit relation.";
                    return false;
                }
                if (!TryValidateReferencePrefab(assetPath, entry.OwnerPrefab, entry.ReferencePrefab, entry.OwnedAssets,
                    "Mesh Outfit Physics", allowsMesh: false, out diagnostic)) return false;
            }
            return true;
        }

        private static bool TryValidateReferencePrefab(string assetPath, GameObject owner, GameObject prefab,
            IReadOnlyList<UnityEngine.Object> ownedAssets, string role, bool allowsMesh, out string diagnostic)
        {
            diagnostic = null;
            if (owner == null)
            {
                diagnostic = "VRM " + role + " Reference must retain its explicit Canonical owner.";
                return false;
            }
            if (AssetDatabase.GetAssetPath(owner) != assetPath)
            {
                diagnostic = "VRM " + role + " Canonical owner must be owned by the Database.";
                return false;
            }
            GameObject databaseRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Transform intermediate = databaseRoot == null ? null : databaseRoot.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            if (intermediate == null || owner.transform.parent != intermediate)
            {
                diagnostic = "VRM " + role + " Canonical owner must be a direct Intermediate child.";
                return false;
            }
            if (prefab == null)
            {
                diagnostic = "VRM " + role + " Reference Prefab is missing.";
                return false;
            }
            if (AssetDatabase.GetAssetPath(prefab) != assetPath)
            {
                diagnostic = "VRM " + role + " Reference Prefab must be owned by the Database.";
                return false;
            }

            Vrm10Instance[] instances = prefab.GetComponentsInChildren<Vrm10Instance>(true)
                .Where(value => value != null && value.Vrm != null).ToArray();
            if (instances.Length != 1)
            {
                diagnostic = "VRM " + role + " Reference Prefab must contain exactly one valid Vrm10Instance.";
                return false;
            }
            VRM10Object vrm = instances[0].Vrm;
            if (AssetDatabase.GetAssetPath(vrm) != assetPath)
            {
                diagnostic = "VRM " + role + " Reference Vrm10Instance must reference a Database-owned VRM10Object.";
                return false;
            }
            if (vrm.Expression == null)
            {
                diagnostic = "VRM " + role + " Reference Vrm10Object is missing its Expression definition.";
                return false;
            }
            foreach (var clip in vrm.Expression.Clips)
            {
                if (clip.Clip == null || AssetDatabase.GetAssetPath(clip.Clip) != assetPath)
                {
                    diagnostic = "VRM " + role + " Reference Expression must be a Database-owned sub-asset.";
                    return false;
                }
            }

            if (ownedAssets == null)
            {
                diagnostic = "VRM " + role + " Reference is missing its explicit owned asset list.";
                return false;
            }
            var owned = new HashSet<UnityEngine.Object>();
            foreach (UnityEngine.Object asset in ownedAssets)
            {
                if (asset == null || !owned.Add(asset))
                {
                    diagnostic = "VRM " + role + " Reference contains a null or duplicate owned asset.";
                    return false;
                }
                if (AssetDatabase.GetAssetPath(asset) != assetPath)
                {
                    diagnostic = "VRM " + role + " Reference owned assets must be Database sub-assets.";
                    return false;
                }
                if (asset is Material || asset is Texture)
                {
                    diagnostic = "VRM " + role + " Reference may not own Material or Texture assets.";
                    return false;
                }
                if (asset is Mesh && !allowsMesh)
                {
                    diagnostic = "VRM " + role + " Reference may not own Mesh assets.";
                    return false;
                }
                if (!(asset is Mesh) && !(asset is Avatar) && !(asset is VRM10Object) && !(asset is VRM10Expression))
                {
                    diagnostic = "VRM " + role + " Reference contains an unsupported owned asset type: " + asset.GetType().Name;
                    return false;
                }
            }

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                string relativePath = GetRelativePath(prefab.transform, renderer.transform);
                Transform ownerTransform = FindByRelativePath(owner.transform, relativePath);
                Renderer ownerRenderer = ownerTransform == null ? null : ownerTransform.GetComponent(renderer.GetType()) as Renderer;
                if (ownerRenderer == null || renderer.sharedMaterials.Length != ownerRenderer.sharedMaterials.Length)
                {
                    diagnostic = "VRM " + role + " Reference renderer does not match its Canonical owner at path: " + relativePath;
                    return false;
                }
                for (int index = 0; index < renderer.sharedMaterials.Length; index++)
                {
                    Material material = renderer.sharedMaterials[index];
                    if (material == null || AssetDatabase.GetAssetPath(material) != assetPath
                        || material != ownerRenderer.sharedMaterials[index])
                    {
                        diagnostic = "VRM " + role + " Reference renderer must use the Canonical Material at path: " + relativePath;
                        return false;
                    }
                }

                Mesh referenceMesh = GetMesh(renderer);
                if (referenceMesh == null) continue;
                if (allowsMesh)
                {
                    if (AssetDatabase.GetAssetPath(referenceMesh) != assetPath || !owned.Contains(referenceMesh))
                    {
                        diagnostic = "VRM " + role + " Reference Mesh must be an explicitly owned Database sub-asset at path: " + relativePath;
                        return false;
                    }
                }
                else
                {
                    Mesh ownerMesh = GetMesh(ownerRenderer);
                    if (ownerMesh == null || AssetDatabase.GetAssetPath(ownerMesh) != assetPath || referenceMesh != ownerMesh)
                    {
                        diagnostic = "VRM " + role + " Reference must use the Canonical Mesh at path: " + relativePath;
                        return false;
                    }
                }
            }
            return true;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
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

        private static Transform FindByRelativePath(Transform root, string path)
        {
            return string.IsNullOrEmpty(path) ? root : root.Find(path);
        }

        private static ShapeSyncDatabaseDiagnostic Invalid(ShapeSyncDatabaseDiagnosticCode code, string detail)
        {
            return new ShapeSyncDatabaseDiagnostic(code,
                ShapeSyncDatabaseEntityKind.Registry,
                ShapeSyncDatabaseRelationKind.Registry,
                ShapeSyncVrmDatabaseRegistry.FeatureId,
                "Database",
                null,
                detail);
        }
    }
}
#endif
