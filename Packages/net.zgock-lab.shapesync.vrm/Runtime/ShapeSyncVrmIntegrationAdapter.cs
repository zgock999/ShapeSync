// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.VrmIntegration
{
    /// <summary>
    /// Optional UniVRM physics integration for a Figure root.
    /// Registers with the figure lifecycle without using a global singleton.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShapeSyncVrmIntegrationAdapter : MonoBehaviour, IShapeSyncOptionalVrmIntegration, IShapeSyncOptionalVrmIntegrationDryRun
    {
        private Vrm10Instance figureInstance;
        private readonly Dictionary<string, ExpressionKey> expressionKeys = new Dictionary<string, ExpressionKey>();

        private void Awake()
        {
            CacheExpressionKeys();
        }

        private void OnEnable()
        {
            CacheExpressionKeys();
            EnsureRegistered();
        }

        private void OnDisable()
        {
            ShapeSyncOptionalVrmIntegrationRegistry.Unregister(gameObject, this);
        }

        private void OnDestroy()
        {
            ShapeSyncOptionalVrmIntegrationRegistry.Unregister(gameObject, this);
        }

        public void EnsureRegistered()
        {
            ShapeSyncOptionalVrmIntegrationRegistry.Register(gameObject, this);
        }

        public bool TrySetExpressionWeight(string expressionName, float weight)
        {
            if (!isActiveAndEnabled || string.IsNullOrEmpty(expressionName)
                || !expressionKeys.TryGetValue(expressionName, out ExpressionKey key)
                || figureInstance == null || figureInstance.Runtime == null || figureInstance.Runtime.Expression == null)
            {
                return false;
            }

            figureInstance.Runtime.Expression.SetWeight(key, weight);
            return true;
        }

        public bool TryAttachOutfitPhysics(ShapeSyncOptionalVrmAttachRequest request, out IShapeSyncOptionalVrmAttachment attachment, out string error)
        {
            attachment = null;
            error = null;
            if (request.FigureRoot != gameObject || request.RuntimeOutfitRoot == null)
            {
                error = "The VRM integration adapter does not own this FigureRoot.";
                return false;
            }

            ShapeSyncOutfitSpringBoneData sourceData = request.RuntimeOutfitRoot.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            if (sourceData == null || sourceData.Springs == null || sourceData.Springs.Count == 0)
            {
                // This is a normal non-VRM Outfit.  The core caller must not know
                // UniVRM types just to distinguish this from a transport failure.
                return true;
            }
            if (!ShapeSyncVrmInstanceUtility.TryGetOrCreateFigureInstance(request.FigureRoot, request.FigureAnimator, out Vrm10Instance figure, out error)) return false;

            ShapeSyncVrmSpringBoneAttachment concrete;
            bool created = ShapeSyncVrmSpringBoneAttachment.TryCreate(sourceData.ColliderGroups, sourceData.Springs, sourceData.SpringColliderGroupNames, request.RuntimeOutfitRoot.transform, figure, request.TransformMapper, out concrete, out error);
            if (!created) return false;
            attachment = concrete;
            return true;
        }

        /// <inheritdoc />
        public bool TryValidateOutfitPhysics(ShapeSyncOptionalVrmDryRunRequest request, out string error)
        {
            error = null;
            if (request.FigureRoot != gameObject || request.OutfitSourceRoot == null)
            {
                error = "The VRM integration adapter does not own this FigureRoot.";
                return false;
            }
            ShapeSyncOutfitSpringBoneData sourceData = request.OutfitSourceRoot.GetComponentInChildren<ShapeSyncOutfitSpringBoneData>(true);
            if (sourceData == null || sourceData.Springs == null || sourceData.Springs.Count == 0) return true;
            if (!isActiveAndEnabled || request.FigureAnimator == null)
            {
                error = "VRM Outfit physics requires an active Figure integration and Animator.";
                return false;
            }
            return true;
        }

        private void CacheExpressionKeys()
        {
            figureInstance = GetComponent<Vrm10Instance>();
            expressionKeys.Clear();
            if (figureInstance == null || figureInstance.Vrm == null || figureInstance.Vrm.Expression == null)
            {
                return;
            }

            foreach (var pair in figureInstance.Vrm.Expression.Clips)
            {
                if (pair.Clip == null)
                {
                    continue;
                }

                string name;
                ExpressionKey key;
                if (pair.Preset == ExpressionPreset.custom)
                {
                    name = pair.Clip.name;
                    key = ExpressionKey.CreateCustom(name);
                }
                else
                {
                    name = pair.Preset.ToString();
                    key = ExpressionKey.CreateFromPreset(pair.Preset);
                }

                if (!string.IsNullOrEmpty(name) && !expressionKeys.ContainsKey(name))
                {
                    expressionKeys.Add(name, key);
                }
                if (pair.Preset != ExpressionPreset.custom && !string.IsNullOrEmpty(pair.Clip.name) && !expressionKeys.ContainsKey(pair.Clip.name))
                {
                    // Source VRMs may give a standard preset a localized or authored
                    // asset name. The baked VRM_* shape follows that authored name.
                    expressionKeys.Add(pair.Clip.name, key);
                }
            }
        }
    }
}
#endif
