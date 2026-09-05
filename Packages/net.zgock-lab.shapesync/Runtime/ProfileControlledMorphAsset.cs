// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Serialized Plugable PCM payload for one Outfit, containing the base and normal-FBM correction frames.
    /// The payload is owned by the optional attachment workflow and does not require UniVRM.
    /// </summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/Profile Controlled Morph Asset", fileName = "PCM_Payload")]
    public sealed class ProfileControlledMorphAsset : ScriptableObject
    {
        [SerializeField] private Mesh payloadMesh;
        [SerializeField] private string outfitName;
        [SerializeField] private List<string> fbmBlendNames = new List<string>();
        [SerializeField] private string baseFrameName;
        [SerializeField] private List<string> fbmFrameNames = new List<string>();
        [SerializeField] private bool includesVisualFbmFrames;
        [SerializeField] private ulong baseGeometrySignature;

        public Mesh PayloadMesh => payloadMesh;
        public string OutfitName => outfitName;
        public IReadOnlyList<string> FbmBlendNames => fbmBlendNames;
        public string BaseFrameName => baseFrameName;
        public IReadOnlyList<string> FbmFrameNames => fbmFrameNames;
        public bool IncludesVisualFbmFrames => includesVisualFbmFrames;
        public ulong BaseGeometrySignature => baseGeometrySignature;

    #if UNITY_EDITOR
        public void ConfigureForBuild(Mesh mesh, string configuredOutfitName, IList<string> configuredFbmBlendNames, bool outputVisualFbmFrames)
        {
            payloadMesh = mesh;
            baseGeometrySignature = MeshGeometrySignature.Calculate(mesh);
            outfitName = configuredOutfitName;
            baseFrameName = zgock.ShapeSync.BlendShapeReservedPrefixes.Pcm + configuredOutfitName;
            includesVisualFbmFrames = outputVisualFbmFrames;
            fbmBlendNames.Clear();
            fbmFrameNames.Clear();
            if (configuredFbmBlendNames == null) return;
            for (int i = 0; i < configuredFbmBlendNames.Count; i++)
            {
                string name = configuredFbmBlendNames[i];
                fbmBlendNames.Add(name);
                fbmFrameNames.Add(zgock.ShapeSync.BlendShapeReservedPrefixes.Pcm + name + "_" + configuredOutfitName);
            }
        }
    #endif
    }
}
