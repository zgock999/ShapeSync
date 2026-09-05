// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Optional VRM attachment lifecycle used by the Core without referencing UniVRM types.
    /// The companion integration owns the implementation so Core remains independently compilable.
    /// </summary>
    public interface IShapeSyncOptionalVrmAttachment : IDisposable
    {
        /// <summary>
        /// Gets the direct children of the transient Outfit root that were used solely as
        /// source VRM physics objects and may be destroyed after physics transfer completes.
        /// Core consumes only Transform ownership; the optional integration remains solely
        /// responsible for deciding which UniVRM components qualify.
        /// </summary>
        IReadOnlyList<Transform> RuntimeSourceCleanupRoots { get; }

        void ReconstructOnce();
        void Rollback();
    }

    /// <summary>
    /// Core-facing optional VRM integration contract implemented only by the UniVRM companion assembly.
    /// </summary>
    public interface IShapeSyncOptionalVrmIntegration
    {
        bool TryAttachOutfitPhysics(ShapeSyncOptionalVrmAttachRequest request, out IShapeSyncOptionalVrmAttachment attachment, out string error);

        /// <summary>
        /// Applies a ShapeSync expression value through the optional VRM runtime.
        /// The Core passes only its logical expression name, so it never needs a
        /// UniVRM type reference.
        /// </summary>
        bool TrySetExpressionWeight(string expressionName, float weight);
    }

    /// <summary>Optional read-only VRM capability check used by transactional Outfit dry-runs.</summary>
    public interface IShapeSyncOptionalVrmIntegrationDryRun
    {
        /// <summary>Checks optional VRM Outfit physics capability without constructing or attaching physics objects.</summary>
        /// <param name="request">The Figure and Outfit-source validation request.</param><param name="error">The failure reason on rejection.</param><returns><see langword="true"/> when runtime attachment may proceed.</returns>
        bool TryValidateOutfitPhysics(ShapeSyncOptionalVrmDryRunRequest request, out string error);
    }

    /// <summary>
    /// Core-only data supplied to an optional VRM physics attachment operation.
    /// It deliberately exposes no UniVRM types.
    /// </summary>
    public readonly struct ShapeSyncOptionalVrmAttachRequest
    {
        public readonly GameObject FigureRoot;
        public readonly Animator FigureAnimator;
        public readonly GameObject RuntimeOutfitRoot;
        public readonly Func<Transform, Transform> TransformMapper;

        public ShapeSyncOptionalVrmAttachRequest(GameObject figureRoot, Animator figureAnimator, GameObject runtimeOutfitRoot, Func<Transform, Transform> transformMapper)
        {
            FigureRoot = figureRoot;
            FigureAnimator = figureAnimator;
            RuntimeOutfitRoot = runtimeOutfitRoot;
            TransformMapper = transformMapper;
        }
    }

    /// <summary>Core-only read-only request for optional VRM Outfit capability validation.</summary>
    public readonly struct ShapeSyncOptionalVrmDryRunRequest
    {
        /// <summary>Gets the Figure root that owns the optional integration.</summary>
        public readonly GameObject FigureRoot;
        /// <summary>Gets the Figure Animator used by the eventual attachment.</summary>
        public readonly Animator FigureAnimator;
        /// <summary>Gets the Outfit prefab root inspected by the read-only check.</summary>
        public readonly GameObject OutfitSourceRoot;

        /// <summary>Initializes a read-only optional VRM Outfit validation request.</summary>
        /// <param name="figureRoot">The Figure root.</param><param name="figureAnimator">The Figure Animator.</param><param name="outfitSourceRoot">The Outfit prefab root.</param>
        public ShapeSyncOptionalVrmDryRunRequest(GameObject figureRoot, Animator figureAnimator, GameObject outfitSourceRoot)
        {
            FigureRoot = figureRoot;
            FigureAnimator = figureAnimator;
            OutfitSourceRoot = outfitSourceRoot;
        }
    }

    /// <summary>
    /// Per-Figure registry that lets Core discover an optional VRM companion without a direct assembly reference.
    /// </summary>
    public static class ShapeSyncOptionalVrmIntegrationRegistry
    {
        private static readonly Dictionary<int, IShapeSyncOptionalVrmIntegration> integrationsByFigureRoot = new Dictionary<int, IShapeSyncOptionalVrmIntegration>();

        public static void Register(GameObject figureRoot, IShapeSyncOptionalVrmIntegration integration)
        {
            if (figureRoot == null || integration == null) return;
            integrationsByFigureRoot[figureRoot.GetInstanceID()] = integration;
        }

        public static void Unregister(GameObject figureRoot, IShapeSyncOptionalVrmIntegration integration)
        {
            if (figureRoot == null) return;
            int key = figureRoot.GetInstanceID();
            if (integrationsByFigureRoot.TryGetValue(key, out IShapeSyncOptionalVrmIntegration registered)
                && ReferenceEquals(registered, integration)) integrationsByFigureRoot.Remove(key);
        }

        public static bool TryGet(GameObject figureRoot, out IShapeSyncOptionalVrmIntegration integration)
        {
            integration = null;
            if (figureRoot == null) return false;
            int key = figureRoot.GetInstanceID();
            if (!integrationsByFigureRoot.TryGetValue(key, out integration) || integration == null) return false;
            if (integration is UnityEngine.Object unityObject && unityObject == null)
            {
                integrationsByFigureRoot.Remove(key);
                integration = null;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Maps one normal FBM name to the Outfit extra-bone registry required for that variant.
    /// </summary>
    [Serializable]
    public sealed class ShapeSyncOutfitFbmExtraBoneRegistry
    {
        public string blendName;
        public CharacterBoneRegistry extraBoneRegistry;
    }

    /// <summary>
    /// Maps one normal FBM name to its optional Humanoid bone correction profile.
    /// </summary>
    [Serializable]
    public sealed class ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile
    {
        public string blendName;
        public ShapeSyncHumanoidBoneCorrectionProfile targetProfile;
    }

    /// <summary>
    /// Serialized Outfit descriptor consumed by <see cref="OutfitAttacher"/>.
    /// It owns registry identity, skinning data, BC Profiles, and optional Plugable PCM metadata.
    /// </summary>
    public sealed class ShapeSyncOutfit : MonoBehaviour
    {
        [SerializeField] private string registryId;
        [SerializeField] private string registryName;
        [SerializeField] private CharacterBoneRegistry baseExtraBoneRegistry;
        [SerializeField] private List<ShapeSyncOutfitFbmExtraBoneRegistry> fbmExtraBoneRegistries = new List<ShapeSyncOutfitFbmExtraBoneRegistry>();
        [SerializeField] private OutfitSkinningProfile skinningProfile;
        [SerializeField] private ShapeSyncHumanoidBoneCorrectionProfile humanoidBoneCorrectionProfile;
        [SerializeField] private List<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile> fbmHumanoidBoneCorrectionProfiles = new List<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile>();
        [SerializeField] private bool profileControlledMorphEnabled;
        [SerializeField] private string profileControlledMorphOutfitName;
        [SerializeField] private ProfileControlledMorphAsset profileControlledMorphAsset;

        public string RegistryId => registryId;
        public string RegistryName => registryName;
        public CharacterBoneRegistry BaseExtraBoneRegistry => baseExtraBoneRegistry;
        public IReadOnlyList<ShapeSyncOutfitFbmExtraBoneRegistry> FbmExtraBoneRegistries => fbmExtraBoneRegistries;
        public OutfitSkinningProfile SkinningProfile => skinningProfile;
        public ShapeSyncHumanoidBoneCorrectionProfile HumanoidBoneCorrectionProfile => humanoidBoneCorrectionProfile;
        public IReadOnlyList<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile> FbmHumanoidBoneCorrectionProfiles => fbmHumanoidBoneCorrectionProfiles;
        public bool ProfileControlledMorphEnabled => profileControlledMorphEnabled;
        public string ProfileControlledMorphOutfitName => profileControlledMorphOutfitName;
        public ProfileControlledMorphAsset ProfileControlledMorphAsset => profileControlledMorphAsset;

        public bool TryValidateProfileControlledMorphConfiguration(out string error)
        {
            error = null;
            if (profileControlledMorphAsset != null)
            {
                if (!profileControlledMorphEnabled)
                {
                    error = "Plugable PCM Outfit must enable PCM.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(profileControlledMorphAsset.OutfitName))
                {
                    error = "Plugable PCM payload has no Outfit name.";
                    return false;
                }
                if (profileControlledMorphOutfitName != profileControlledMorphAsset.OutfitName)
                {
                    error = "Plugable PCM Outfit name must match the payload Outfit name.";
                    return false;
                }
            }
            else if (profileControlledMorphEnabled && string.IsNullOrWhiteSpace(profileControlledMorphOutfitName))
            {
                error = "Legacy PCM Outfit must specify an Outfit name.";
                return false;
            }
            return true;
        }

        public bool TryGetFbmExtraBoneRegistry(string blendName, out CharacterBoneRegistry registry)
        {
            registry = null;
            if (string.IsNullOrEmpty(blendName) || fbmExtraBoneRegistries == null)
            {
                return false;
            }

            for (int i = 0; i < fbmExtraBoneRegistries.Count; i++)
            {
                ShapeSyncOutfitFbmExtraBoneRegistry entry = fbmExtraBoneRegistries[i];
                if (entry != null && entry.blendName == blendName)
                {
                    registry = entry.extraBoneRegistry;
                    return registry != null;
                }
            }

            return false;
        }

        /// <summary>
        /// Sets the extra-bone registry required by Outfit Builder or PBM Baker for a target.
        /// The target name matches the FBM/PBM target name and is resolved by DynamicBoneBlender.
        /// </summary>
        public void SetExtraBoneRegistry(string blendName, CharacterBoneRegistry registry)
        {
            if (string.IsNullOrEmpty(blendName))
            {
                return;
            }

            for (int i = 0; i < fbmExtraBoneRegistries.Count; i++)
            {
                ShapeSyncOutfitFbmExtraBoneRegistry entry = fbmExtraBoneRegistries[i];
                if (entry != null && entry.blendName == blendName)
                {
                    entry.extraBoneRegistry = registry;
                    return;
                }
            }

            fbmExtraBoneRegistries.Add(new ShapeSyncOutfitFbmExtraBoneRegistry
            {
                blendName = blendName,
                extraBoneRegistry = registry
            });
        }

        /// <summary>Sets the Skinning Profile copied to PBM Baker output.</summary>
        public void SetSkinningProfile(OutfitSkinningProfile profile)
        {
            skinningProfile = profile;
        }

        /// <summary>Sets the optional cross-rig BCP projection used by independent Outfit previews.</summary>
        public bool TryGetFbmHumanoidBoneCorrectionProfile(string blendName, out ShapeSyncHumanoidBoneCorrectionProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(blendName) || fbmHumanoidBoneCorrectionProfiles == null) return false;
            for (int i = 0; i < fbmHumanoidBoneCorrectionProfiles.Count; i++)
            {
                ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile entry = fbmHumanoidBoneCorrectionProfiles[i];
                if (entry != null && entry.blendName == blendName)
                {
                    profile = entry.targetProfile;
                    return profile != null;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Runtime record for one attached Outfit and the registry identity used to detach or replace it.
    /// </summary>
    public sealed class AttachedOutfitRegistrySet
    {
        private readonly List<Transform> extraRoots;
        private readonly List<string> extraRootPaths;
        private readonly List<OutfitSkinnedMeshBinding> skinnedMeshBindings;
        private readonly List<Transform> rendererRoots;

        public string RegistryId { get; }
        public string RegistryName { get; }
        public GameObject RuntimeOutfitInstance { get; }
        public CharacterBoneRegistry BaseExtraBoneRegistry { get; }
        public IReadOnlyList<ShapeSyncOutfitFbmExtraBoneRegistry> FbmExtraBoneRegistries { get; }
        public IReadOnlyList<Transform> ExtraRoots => extraRoots;
        public IReadOnlyList<string> ExtraRootPaths => extraRootPaths;
        public IReadOnlyList<OutfitSkinnedMeshBinding> SkinnedMeshBindings => skinnedMeshBindings;
        public IReadOnlyList<Transform> RendererRoots => rendererRoots;
        public IShapeSyncOptionalVrmAttachment SpringBoneAttachment { get; }
        public ShapeSyncHumanoidBoneCorrectionProfile HumanoidBoneCorrectionProfile { get; }
        public IReadOnlyList<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile> FbmHumanoidBoneCorrectionProfiles { get; }
        public bool UsesBcpBakedBindposes { get; }
        public ProfileControlledMorphBinding ProfileControlledMorphBinding { get; }

        public AttachedOutfitRegistrySet(
            ShapeSyncOutfit outfit,
            GameObject runtimeOutfitInstance,
            List<Transform> attachedExtraRoots,
            List<string> attachedExtraRootPaths,
            List<OutfitSkinnedMeshBinding> attachedSkinnedMeshBindings,
            List<Transform> attachedRendererRoots,
            IShapeSyncOptionalVrmAttachment springBoneAttachment,
            ShapeSyncHumanoidBoneCorrectionProfile humanoidBoneCorrectionProfile,
            ProfileControlledMorphBinding profileControlledMorphBinding)
        {
            RegistryId = outfit.RegistryId;
            RegistryName = outfit.RegistryName;
            RuntimeOutfitInstance = runtimeOutfitInstance;
            BaseExtraBoneRegistry = outfit.BaseExtraBoneRegistry;
            FbmExtraBoneRegistries = outfit.FbmExtraBoneRegistries;
            extraRoots = attachedExtraRoots;
            extraRootPaths = attachedExtraRootPaths;
            skinnedMeshBindings = attachedSkinnedMeshBindings;
            rendererRoots = attachedRendererRoots;
            SpringBoneAttachment = springBoneAttachment;
            HumanoidBoneCorrectionProfile = humanoidBoneCorrectionProfile;
            FbmHumanoidBoneCorrectionProfiles = outfit.FbmHumanoidBoneCorrectionProfiles;
            UsesBcpBakedBindposes = outfit.SkinningProfile != null && outfit.SkinningProfile.UsesBcpBakedBindposes;
            ProfileControlledMorphBinding = profileControlledMorphBinding;
        }

        public bool TryGetFbmExtraBoneRegistry(string blendName, out CharacterBoneRegistry registry)
        {
            registry = null;
            if (string.IsNullOrEmpty(blendName) || FbmExtraBoneRegistries == null)
            {
                return false;
            }

            for (int i = 0; i < FbmExtraBoneRegistries.Count; i++)
            {
                ShapeSyncOutfitFbmExtraBoneRegistry entry = FbmExtraBoneRegistries[i];
                if (entry != null && entry.blendName == blendName)
                {
                    registry = entry.extraBoneRegistry;
                    return registry != null;
                }
            }

            return false;
        }

        public bool TryGetFbmHumanoidBoneCorrectionProfile(string blendName, out ShapeSyncHumanoidBoneCorrectionProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(blendName) || FbmHumanoidBoneCorrectionProfiles == null) return false;
            for (int i = 0; i < FbmHumanoidBoneCorrectionProfiles.Count; i++)
            {
                ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile entry = FbmHumanoidBoneCorrectionProfiles[i];
                if (entry != null && entry.blendName == blendName)
                {
                    profile = entry.targetProfile;
                    return profile != null;
                }
            }
            return false;
        }
    }
}
