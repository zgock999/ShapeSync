// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace zgock.ShapeSync
{
    /// <summary>Authoring-only serialized carrier for one imported intermediate Humanoid; it has no runtime behaviour.</summary>
    [DisallowMultipleComponent]
    public sealed class ShapeSyncFigureImportRecord : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer[] confirmedRendererOrder = Array.Empty<SkinnedMeshRenderer>();

#if UNITY_EDITOR
        /// <summary>Gets the human-confirmed renderer order without exposing a mutable serialized array.</summary>
        internal IReadOnlyList<SkinnedMeshRenderer> ConfirmedRendererOrder => Array.AsReadOnly(confirmedRendererOrder);

        /// <summary>Configures the merged renderer order retained by the authoring Database.</summary>
        internal bool TryConfigure(IReadOnlyList<SkinnedMeshRenderer> rendererOrder, out string diagnostic)
        {
            diagnostic = null;
            ShapeSyncDatabase database = GetComponentInParent<ShapeSyncDatabase>();
            if (database == null || database.gameObject == gameObject)
            {
                diagnostic = "ShapeSync Figure import record must be attached below a ShapeSync Database root.";
                return false;
            }

            if (!EditorSceneManager.IsPreviewScene(database.gameObject.scene))
            {
                diagnostic = "ShapeSync Figure import record must be configured in ShapeSync Database Prefab contents.";
                return false;
            }

            Transform intermediate = database.transform.Find("Intermediate");
            if (intermediate == null || !transform.IsChildOf(intermediate))
            {
                diagnostic = "ShapeSync Figure import record must be attached below the Database Intermediate container.";
                return false;
            }

            if (rendererOrder == null || rendererOrder.Count == 0)
            {
                diagnostic = "ShapeSync Figure import record requires at least one source renderer.";
                return false;
            }

            var copiedOrder = new SkinnedMeshRenderer[rendererOrder.Count];
            var uniqueRenderers = new HashSet<SkinnedMeshRenderer>();
            for (int index = 0; index < rendererOrder.Count; index++)
            {
                SkinnedMeshRenderer renderer = rendererOrder[index];
                if (renderer == null || !renderer.transform.IsChildOf(transform) || !uniqueRenderers.Add(renderer))
                {
                    diagnostic = "ShapeSync Figure import record requires non-null, unique renderers below its carrier.";
                    return false;
                }
                copiedOrder[index] = renderer;
            }

            confirmedRendererOrder = copiedOrder;
            return true;
        }
#endif
    }
}
