// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Bindpose set for one normal FBM variant of an Outfit renderer.
    /// PBM targets are represented by their owning Outfit data rather than this list.
    /// </summary>
    [Serializable]
    public sealed class OutfitSkinningFbmBindposes
    {
        public string blendName;
        public Matrix4x4[] bindposes = Array.Empty<Matrix4x4>();
    }

    /// <summary>
    /// Skinning data for one renderer under an Outfit root, addressed by its relative transform path.
    /// </summary>
    [Serializable]
    public sealed class OutfitSkinningRendererProfile
    {
        public string rendererPath;
        public Matrix4x4[] baseBindposes = Array.Empty<Matrix4x4>();
        public List<OutfitSkinningFbmBindposes> fbmBindposes = new List<OutfitSkinningFbmBindposes>();
    }

    /// <summary>
    /// Authoring profile used to align an attached Outfit renderer to the Figure skinning space.
    /// It records base and normal-FBM bindposes without changing the source Outfit mesh at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Outfit Skinning Profile", fileName = "OutfitSkinningProfile")]
    public sealed class OutfitSkinningProfile : ScriptableObject
    {
        [SerializeField] private List<OutfitSkinningRendererProfile> renderers = new List<OutfitSkinningRendererProfile>();
        [SerializeField] private bool usesBcpBakedBindposes;

        public IReadOnlyList<OutfitSkinningRendererProfile> Renderers => renderers;
        public bool UsesBcpBakedBindposes => usesBcpBakedBindposes;

        public void SetRendererProfiles(List<OutfitSkinningRendererProfile> rendererProfiles)
        {
            renderers = rendererProfiles ?? new List<OutfitSkinningRendererProfile>();
        }

        public void SetUsesBcpBakedBindposesForEditor(bool value)
        {
            usesBcpBakedBindposes = value;
        }

        public bool TryGetRenderer(string rendererPath, out OutfitSkinningRendererProfile rendererProfile)
        {
            rendererProfile = null;
            if (string.IsNullOrEmpty(rendererPath) || renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                OutfitSkinningRendererProfile candidate = renderers[i];
                if (candidate != null && candidate.rendererPath == rendererPath)
                {
                    rendererProfile = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
