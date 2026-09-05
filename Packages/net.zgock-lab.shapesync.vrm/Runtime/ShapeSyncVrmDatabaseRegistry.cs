// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UniVRM10;
using zgock.ShapeSync;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// Database-owned VRM definitions.  This registry is deliberately separate
    /// from <see cref="ShapeSyncDatabaseRegistry"/> so Core Runtime never needs
    /// to reference UniVRM types.
    /// </summary>
    public sealed class ShapeSyncVrmDatabaseRegistry : ScriptableObject
    {
        /// <summary>Stable optional feature marker used by Core Database admission.</summary>
        public const string FeatureId = "VRM";

        /// <summary>Default VRM asset output folder relative to the selected Generate root.</summary>
        public const string DefaultGenerationVrmPath = "VRM/";

        /// <summary>Describes one Database-owned Base or FBM Expression Reference relation.</summary>
        /// <remarks>The owner is the explicit canonical Figure or FBM prefab used for Generate reverse lookup.</remarks>
        [Serializable]
        public sealed class FigureExpressionReference
        {
            [SerializeField] private string figureName;
            [SerializeField] private string shapeKey;
            [SerializeField] private GameObject ownerPrefab;
            [SerializeField] private GameObject referencePrefab;
            [SerializeField] private List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();

            /// <summary>Logical Figure identity.</summary>
            /// <value>The Figure identity owning this Expression Reference.</value>
            public string FigureName => figureName;
            /// <summary>Base or FBM shape key.</summary>
            /// <value>The Base shape key or FBM identity represented by the reference.</value>
            public string ShapeKey => shapeKey;
            /// <summary>Canonical Figure Base or FBM owner used by Generate reverse lookup.</summary>
            /// <value>The canonical owner prefab in the Database Intermediate hierarchy.</value>
            public GameObject OwnerPrefab => ownerPrefab;
            /// <summary>Database-owned Reference VRM child tree.</summary>
            /// <value>The cloned Reference VRM prefab retained by this relation.</value>
            public GameObject ReferencePrefab => referencePrefab;
            /// <summary>Sub-assets owned exclusively by this Reference relation.</summary>
            /// <value>The VRM and Expression assets retained by the relation.</value>
            public IReadOnlyList<UnityEngine.Object> OwnedAssets => ownedAssets;

            internal FigureExpressionReference(string figure, string shape, GameObject owner, GameObject prefab,
                IReadOnlyList<UnityEngine.Object> assets)
            {
                figureName = figure;
                shapeKey = shape;
                ownerPrefab = owner;
                referencePrefab = prefab;
                ownedAssets = assets == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(assets);
            }

            internal void Rebind(GameObject owner, GameObject prefab, IReadOnlyList<UnityEngine.Object> assets)
            {
                ownerPrefab = owner;
                referencePrefab = prefab;
                ownedAssets = assets == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(assets);
            }
        }

        /// <summary>Describes one Database-owned Figure Physics Reference relation.</summary>
        /// <remarks>The owner is the explicit canonical Figure Base prefab used for Generate reverse lookup.</remarks>
        [Serializable]
        public sealed class FigurePhysicsReference
        {
            [SerializeField] private string figureName;
            [SerializeField] private GameObject ownerPrefab;
            [SerializeField] private GameObject referencePrefab;
            [SerializeField] private List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();

            /// <summary>Logical Figure identity.</summary>
            /// <value>The Figure identity owning this Physics Reference.</value>
            public string FigureName => figureName;
            /// <summary>Canonical Figure Base owner used by Generate reverse lookup.</summary>
            /// <value>The canonical Figure Base prefab in the Database Intermediate hierarchy.</value>
            public GameObject OwnerPrefab => ownerPrefab;
            /// <summary>Database-owned Reference VRM child tree.</summary>
            /// <value>The cloned Figure Physics Reference prefab retained by this relation.</value>
            public GameObject ReferencePrefab => referencePrefab;
            /// <summary>Sub-assets owned exclusively by this Reference relation.</summary>
            /// <value>The VRM assets retained by the relation; the canonical Mesh remains on the owner.</value>
            public IReadOnlyList<UnityEngine.Object> OwnedAssets => ownedAssets;

            internal FigurePhysicsReference(string figure, GameObject owner, GameObject prefab,
                IReadOnlyList<UnityEngine.Object> assets)
            {
                figureName = figure;
                ownerPrefab = owner;
                referencePrefab = prefab;
                ownedAssets = assets == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(assets);
            }

            internal void Rebind(GameObject owner, GameObject prefab, IReadOnlyList<UnityEngine.Object> assets)
            {
                ownerPrefab = owner;
                referencePrefab = prefab;
                ownedAssets = assets == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(assets);
            }
        }

        /// <summary>Describes one Database-owned Mesh Outfit Physics Reference relation.</summary>
        /// <remarks>The owner is the explicit canonical Mesh Outfit Base prefab used for Generate reverse lookup.</remarks>
        [Serializable]
        public sealed class MeshOutfitPhysicsReference
        {
            [SerializeField] private string outfitIdentity;
            [SerializeField] private GameObject ownerPrefab;
            [SerializeField] private GameObject referencePrefab;
            [SerializeField] private List<UnityEngine.Object> ownedAssets = new List<UnityEngine.Object>();

            /// <summary>Logical Mesh Outfit identity.</summary>
            /// <value>The Mesh Outfit identity owning this Physics Reference.</value>
            public string OutfitIdentity => outfitIdentity;
            /// <summary>Canonical Mesh Outfit Base owner used by Generate reverse lookup.</summary>
            /// <value>The canonical Mesh Outfit Base prefab in the Database Intermediate hierarchy.</value>
            public GameObject OwnerPrefab => ownerPrefab;
            /// <summary>Database-owned Reference VRM child tree.</summary>
            /// <value>The cloned Mesh Outfit Physics Reference prefab retained by this relation.</value>
            public GameObject ReferencePrefab => referencePrefab;
            /// <summary>Sub-assets owned exclusively by this Reference relation.</summary>
            /// <value>The VRM assets retained by the relation; the canonical Mesh remains on the owner.</value>
            public IReadOnlyList<UnityEngine.Object> OwnedAssets => ownedAssets;

            internal MeshOutfitPhysicsReference(string outfit, GameObject owner, GameObject prefab,
                IReadOnlyList<UnityEngine.Object> assets)
            {
                outfitIdentity = outfit;
                ownerPrefab = owner;
                referencePrefab = prefab;
                ownedAssets = assets == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(assets);
            }

            internal void Rebind(GameObject owner, GameObject prefab, IReadOnlyList<UnityEngine.Object> assets)
            {
                ownerPrefab = owner;
                referencePrefab = prefab;
                ownedAssets = assets == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(assets);
            }
        }

        [SerializeField] private ShapeSyncDatabaseOptionalFeatureMarker featureMarker;
        [SerializeField] private List<FigureExpressionReference> figureExpressionReferences = new List<FigureExpressionReference>();
        [SerializeField] private List<FigurePhysicsReference> figurePhysicsReferences = new List<FigurePhysicsReference>();
        [SerializeField] private List<MeshOutfitPhysicsReference> meshOutfitPhysicsReferences = new List<MeshOutfitPhysicsReference>();
        [SerializeField] private string generationVrmPath = DefaultGenerationVrmPath;

        /// <summary>Gets the Core marker which proves that this Database contains VRM data.</summary>
        /// <value>The Database-local VRM feature marker, or <see langword="null"/> before VRM registration.</value>
        public ShapeSyncDatabaseOptionalFeatureMarker FeatureMarker => featureMarker;
        /// <summary>Gets all Figure Base/FBM Expression Reference rows.</summary>
        /// <value>The serialized Expression Reference relations.</value>
        public IReadOnlyList<FigureExpressionReference> FigureExpressionReferences => figureExpressionReferences;
        /// <summary>Gets all Figure Physics Reference rows.</summary>
        /// <value>The serialized Figure Physics Reference relations.</value>
        public IReadOnlyList<FigurePhysicsReference> FigurePhysicsReferences => figurePhysicsReferences;
        /// <summary>Gets all Mesh Outfit Physics Reference rows.</summary>
        /// <value>The serialized Mesh Outfit Physics Reference relations.</value>
        public IReadOnlyList<MeshOutfitPhysicsReference> MeshOutfitPhysicsReferences => meshOutfitPhysicsReferences;
        /// <summary>Gets the VRM asset output folder relative to the selected Generate root.</summary>
        /// <value>The normalized relative output folder, including its trailing separator.</value>
        public string GenerationVrmPath => string.IsNullOrWhiteSpace(generationVrmPath)
            ? DefaultGenerationVrmPath
            : generationVrmPath;

        /// <summary>Gets whether the registry carries the expected Core feature marker.</summary>
        /// <value><see langword="true"/> when the VRM feature marker is present and valid.</value>
        public bool HasValidFeatureMarker => featureMarker != null
            && string.Equals(featureMarker.FeatureId, FeatureId, StringComparison.Ordinal);

        /// <summary>Assigns the Core marker during the same Database transaction as this registry.</summary>
        /// <param name="marker">The VRM feature marker to assign.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="marker"/> is null or identifies another feature.</exception>
        public void SetFeatureMarker(ShapeSyncDatabaseOptionalFeatureMarker marker)
        {
            if (marker == null || !string.Equals(marker.FeatureId, FeatureId, StringComparison.Ordinal))
            {
                throw new ArgumentException("VRM Registry requires a VRM feature marker.", nameof(marker));
            }

            featureMarker = marker;
        }

        /// <summary>Sets and validates the VRM asset output folder for Generate.</summary>
        /// <param name="value">The relative folder below the selected Generate root.</param>
        /// <param name="diagnostic">Receives a validation diagnostic when <paramref name="value"/> is invalid.</param>
        /// <returns><see langword="true"/> when the normalized path is stored; otherwise, <see langword="false"/>.</returns>
        public bool TrySetGenerationVrmPath(string value, out string diagnostic)
        {
            if (!TryValidateGenerationVrmPath(value, out diagnostic)) return false;
            generationVrmPath = value.Replace('\\', '/').Trim('/') + "/";
            return true;
        }

        /// <summary>Validates a VRM output folder relative to the selected Generate root.</summary>
        /// <param name="value">The candidate relative folder path.</param>
        /// <param name="diagnostic">Receives a stable validation diagnostic when the path is invalid.</param>
        /// <returns><see langword="true"/> when <paramref name="value"/> is a valid relative folder; otherwise, <see langword="false"/>.</returns>
        public static bool TryValidateGenerationVrmPath(string value, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostic = "VrmGenerationPathEmpty: VRM output path must not be empty.";
                return false;
            }

            string normalized = value.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || System.IO.Path.IsPathRooted(value)
                || normalized.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."))
            {
                diagnostic = "VrmGenerationPathInvalid: VRM output path must be a relative folder below the selected Generate root.";
                return false;
            }

            diagnostic = null;
            return true;
        }

        /// <summary>Upserts one Figure Base/FBM Expression Reference relation.</summary>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="shapeKey">The Base shape key or FBM identity.</param>
        /// <param name="owner">The explicit canonical Figure or FBM owner.</param>
        /// <param name="prefab">The Database-owned cloned Reference VRM prefab.</param>
        /// <param name="ownedAssets">The Database-owned VRM sub-assets for the relation.</param>
        /// <param name="replacedPrefab">Receives the previous Reference VRM prefab when an existing relation is replaced.</param>
        /// <param name="diagnostic">Receives a validation diagnostic when the relation is rejected.</param>
        /// <returns><see langword="true"/> when the relation is inserted or replaced; otherwise, <see langword="false"/>.</returns>
        public bool TryUpsertFigureExpressionReference(string figureName, string shapeKey, GameObject owner,
            GameObject prefab, IReadOnlyList<UnityEngine.Object> ownedAssets,
            out GameObject replacedPrefab, out string diagnostic)
        {
            replacedPrefab = null;
            if (!TryValidateIdentity(figureName, nameof(figureName), out diagnostic)
                || !TryValidateIdentity(shapeKey, nameof(shapeKey), out diagnostic)
                || !TryValidateOwner(owner, out diagnostic)
                || !TryValidatePrefab(prefab, out diagnostic)
                || !TryValidateOwnedAssets(ownedAssets, allowsMesh: true, out diagnostic)) return false;

            FigureExpressionReference existing = figureExpressionReferences.Find(value => value != null
                && value.FigureName == figureName && value.ShapeKey == shapeKey);
            if (existing != null)
            {
                replacedPrefab = existing.ReferencePrefab;
                existing.Rebind(owner, prefab, ownedAssets);
            }
            else
            {
                figureExpressionReferences.Add(new FigureExpressionReference(figureName, shapeKey, owner, prefab, ownedAssets));
            }

            diagnostic = null;
            return true;
        }

        /// <summary>Upserts the single Figure Physics Reference relation for one Figure.</summary>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="owner">The explicit canonical Figure Base owner.</param>
        /// <param name="prefab">The Database-owned cloned Figure Physics Reference prefab.</param>
        /// <param name="ownedAssets">The Database-owned VRM sub-assets for the relation; Mesh assets are not allowed.</param>
        /// <param name="replacedPrefab">Receives the previous Reference VRM prefab when an existing relation is replaced.</param>
        /// <param name="diagnostic">Receives a validation diagnostic when the relation is rejected.</param>
        /// <returns><see langword="true"/> when the relation is inserted or replaced; otherwise, <see langword="false"/>.</returns>
        public bool TryUpsertFigurePhysicsReference(string figureName, GameObject owner, GameObject prefab,
            IReadOnlyList<UnityEngine.Object> ownedAssets,
            out GameObject replacedPrefab, out string diagnostic)
        {
            replacedPrefab = null;
            if (!TryValidateIdentity(figureName, nameof(figureName), out diagnostic)
                || !TryValidateOwner(owner, out diagnostic)
                || !TryValidatePrefab(prefab, out diagnostic)
                || !TryValidateOwnedAssets(ownedAssets, allowsMesh: false, out diagnostic)) return false;

            FigurePhysicsReference existing = figurePhysicsReferences.Find(value => value != null && value.FigureName == figureName);
            if (existing != null)
            {
                replacedPrefab = existing.ReferencePrefab;
                existing.Rebind(owner, prefab, ownedAssets);
            }
            else
            {
                figurePhysicsReferences.Add(new FigurePhysicsReference(figureName, owner, prefab, ownedAssets));
            }

            diagnostic = null;
            return true;
        }

        /// <summary>Upserts the single Mesh Outfit Physics Reference relation for one Outfit.</summary>
        /// <param name="outfitIdentity">The logical Mesh Outfit identity.</param>
        /// <param name="owner">The explicit canonical Mesh Outfit Base owner.</param>
        /// <param name="prefab">The Database-owned cloned Mesh Outfit Physics Reference prefab.</param>
        /// <param name="ownedAssets">The Database-owned VRM sub-assets for the relation; Mesh assets are not allowed.</param>
        /// <param name="replacedPrefab">Receives the previous Reference VRM prefab when an existing relation is replaced.</param>
        /// <param name="diagnostic">Receives a validation diagnostic when the relation is rejected.</param>
        /// <returns><see langword="true"/> when the relation is inserted or replaced; otherwise, <see langword="false"/>.</returns>
        public bool TryUpsertMeshOutfitPhysicsReference(string outfitIdentity, GameObject owner, GameObject prefab,
            IReadOnlyList<UnityEngine.Object> ownedAssets,
            out GameObject replacedPrefab, out string diagnostic)
        {
            replacedPrefab = null;
            if (!TryValidateIdentity(outfitIdentity, nameof(outfitIdentity), out diagnostic)
                || !TryValidateOwner(owner, out diagnostic)
                || !TryValidatePrefab(prefab, out diagnostic)
                || !TryValidateOwnedAssets(ownedAssets, allowsMesh: false, out diagnostic)) return false;

            MeshOutfitPhysicsReference existing = meshOutfitPhysicsReferences.Find(value => value != null
                && value.OutfitIdentity == outfitIdentity);
            if (existing != null)
            {
                replacedPrefab = existing.ReferencePrefab;
                existing.Rebind(owner, prefab, ownedAssets);
            }
            else
            {
                meshOutfitPhysicsReferences.Add(new MeshOutfitPhysicsReference(outfitIdentity, owner, prefab, ownedAssets));
            }

            diagnostic = null;
            return true;
        }

        /// <summary>Removes an Expression Reference relation and returns its old child tree.</summary>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="shapeKey">The Base shape key or FBM identity.</param>
        /// <param name="removedPrefab">Receives the removed Database-owned Reference VRM prefab.</param>
        /// <returns><see langword="true"/> when a matching relation is removed; otherwise, <see langword="false"/>.</returns>
        public bool TryRemoveFigureExpressionReference(string figureName, string shapeKey,
            out GameObject removedPrefab)
        {
            int index = figureExpressionReferences.FindIndex(value => value != null
                && value.FigureName == figureName && value.ShapeKey == shapeKey);
            if (index < 0)
            {
                removedPrefab = null;
                return false;
            }

            removedPrefab = figureExpressionReferences[index].ReferencePrefab;
            figureExpressionReferences.RemoveAt(index);
            return true;
        }

        /// <summary>Removes a Figure Physics relation and returns its old child tree.</summary>
        /// <param name="figureName">The logical Figure identity.</param>
        /// <param name="removedPrefab">Receives the removed Database-owned Reference VRM prefab.</param>
        /// <returns><see langword="true"/> when a matching relation is removed; otherwise, <see langword="false"/>.</returns>
        public bool TryRemoveFigurePhysicsReference(string figureName, out GameObject removedPrefab)
        {
            int index = figurePhysicsReferences.FindIndex(value => value != null && value.FigureName == figureName);
            if (index < 0)
            {
                removedPrefab = null;
                return false;
            }

            removedPrefab = figurePhysicsReferences[index].ReferencePrefab;
            figurePhysicsReferences.RemoveAt(index);
            return true;
        }

        /// <summary>Removes a Mesh Outfit Physics relation and returns its old child tree.</summary>
        /// <param name="outfitIdentity">The logical Mesh Outfit identity.</param>
        /// <param name="removedPrefab">Receives the removed Database-owned Reference VRM prefab.</param>
        /// <returns><see langword="true"/> when a matching relation is removed; otherwise, <see langword="false"/>.</returns>
        public bool TryRemoveMeshOutfitPhysicsReference(string outfitIdentity, out GameObject removedPrefab)
        {
            int index = meshOutfitPhysicsReferences.FindIndex(value => value != null
                && value.OutfitIdentity == outfitIdentity);
            if (index < 0)
            {
                removedPrefab = null;
                return false;
            }

            removedPrefab = meshOutfitPhysicsReferences[index].ReferencePrefab;
            meshOutfitPhysicsReferences.RemoveAt(index);
            return true;
        }

        private static bool TryValidateIdentity(string value, string parameterName, out string diagnostic)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                diagnostic = null;
                return true;
            }

            diagnostic = "VRM Registry relation requires a non-empty " + parameterName + ".";
            return false;
        }

        private static bool TryValidatePrefab(GameObject prefab, out string diagnostic)
        {
            if (prefab != null)
            {
                diagnostic = null;
                return true;
            }

            diagnostic = "VRM Registry relation requires a Reference VRM Prefab.";
            return false;
        }

        private static bool TryValidateOwner(GameObject owner, out string diagnostic)
        {
            if (owner != null)
            {
                diagnostic = null;
                return true;
            }

            diagnostic = "VRM Registry relation requires its explicit Canonical owner.";
            return false;
        }

        private static bool TryValidateOwnedAssets(IReadOnlyList<UnityEngine.Object> assets, bool allowsMesh,
            out string diagnostic)
        {
            if (assets == null)
            {
                diagnostic = "VRM Registry relation requires an explicit owned asset list.";
                return false;
            }

            var seen = new HashSet<UnityEngine.Object>();
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null || !seen.Add(asset))
                {
                    diagnostic = "VRM Registry relation contains a null or duplicate owned asset.";
                    return false;
                }
                if (asset is Material || asset is Texture)
                {
                    diagnostic = "VRM Reference Prefab may not own Material or Texture assets.";
                    return false;
                }
                if (asset is Mesh && !allowsMesh)
                {
                    diagnostic = "VRM Physics Reference may not own Mesh assets; it must use its Canonical Mesh owner.";
                    return false;
                }
                if (!(asset is Mesh) && !(asset is Avatar) && !(asset is VRM10Object) && !(asset is VRM10Expression))
                {
                    diagnostic = "VRM Registry relation contains an unsupported owned asset type: " + asset.GetType().Name;
                    return false;
                }
            }

            diagnostic = null;
            return true;
        }
    }
}
#endif
