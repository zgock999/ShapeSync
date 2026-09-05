// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Owns the fixed post-PCM-mesh-replacement write order for a Figure.
    /// BlendShape indices are invariant, so this coordinator only reapplies cached values;
    /// it never asks DDB or UEP to rebuild their caches.
    /// </summary>
    public sealed class FigureMorphSyncCoordinator : MonoBehaviour
    {
        [SerializeField] private DynamicBoneBlender dynamicBoneBlender;
        [SerializeField] private UniversalExpressionProxy universalExpressionProxy;

        public void ConfigureForFigure(DynamicBoneBlender blender, UniversalExpressionProxy expressions)
        {
            dynamicBoneBlender = blender;
            universalExpressionProxy = expressions;
        }

        private void Awake()
        {
            if (dynamicBoneBlender == null) dynamicBoneBlender = GetComponent<DynamicBoneBlender>();
            if (universalExpressionProxy == null) universalExpressionProxy = GetComponent<UniversalExpressionProxy>();
        }

        public bool IsReady(out string error)
        {
            error = null;
            if (dynamicBoneBlender == null || universalExpressionProxy == null)
            {
                error = "Figure Morph Sync Coordinator requires DynamicBoneBlender and UniversalExpressionProxy.";
                return false;
            }
            return true;
        }

        public void SynchronizeAfterPcmMeshReplacement(Mesh mesh)
        {
            if (mesh == null) return;

            // This order is the Spec10 physical-write contract.
            dynamicBoneBlender.ReapplyFigureMorphWeightsAfterMeshReplacement(mesh);
            universalExpressionProxy.ReapplyExpressionWeightsAfterMeshReplacement(mesh);
        }
    }
}
