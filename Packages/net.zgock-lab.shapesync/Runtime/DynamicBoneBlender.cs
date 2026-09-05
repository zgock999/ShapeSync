// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.Utilities;

namespace zgock.ShapeSync
{
    /// <summary>
    /// Serializable FBM or PBM target definition used by <see cref="DynamicBoneBlender"/>.
    /// It associates a BlendShape name with the Avatar and bone registry required to apply its skeletal state.
    /// </summary>
    [Serializable]
    public class DynamicBoneBlendTarget
    {
        public string blendName;
        public bool enabled = true;
        public float weight;
        public Avatar targetAvatar;
        public CharacterBoneRegistry targetRegistry;
        // PBM_<FBM>_<PBM> stores the absolute FBM+PBM target.  It is resolved to
        // target-index pairs once while building the runtime cache.
        public List<DynamicBonePbmDifferenceTarget> pbmDifferenceTargets = new List<DynamicBonePbmDifferenceTarget>();
    }

    /// <summary>
    /// Absolute FBM-plus-PBM skeletal target owned by one PBM entry.
    /// </summary>
    [Serializable]
    public sealed class DynamicBonePbmDifferenceTarget
    {
        public string fbmBlendName;
        public Avatar targetAvatar;
        public CharacterBoneRegistry targetRegistry;
    }

    /// <summary>
    /// Immutable FBM weight notification emitted by <see cref="DynamicBoneBlender"/>.
    /// </summary>
    public readonly struct FbmWeightChange
    {
        /// <summary>Gets the non-PBM blend target name whose raw weight changed.</summary>
        public readonly string BlendName;
        /// <summary>Gets the raw, unnormalized blend target weight.</summary>
        public readonly float Weight;
        /// <summary>Gets whether the blend target is enabled.</summary>
        public readonly bool Enabled;

        /// <summary>Creates one immutable non-PBM weight notification.</summary>
        /// <param name="blendName">The non-PBM blend target name.</param>
        /// <param name="weight">The raw target weight.</param>
        /// <param name="enabled">Whether the target is enabled.</param>
        public FbmWeightChange(string blendName, float weight, bool enabled)
        {
            BlendName = blendName;
            Weight = weight;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// Figure runtime controller that applies FBM/PBM weights to mesh BlendShapes, bindposes, Avatar, and bone pose data.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class DynamicBoneBlender : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer targetSkinnedMeshRenderer;
        [SerializeField] private DynamicMorphAdapter dynamicMorphAdapter;
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private Avatar baseAvatar;
        [SerializeField] private CharacterBoneRegistry baseRegistry;
        [SerializeField] private List<DynamicBoneBlendTarget> targets = new List<DynamicBoneBlendTarget>();
        [SerializeField] private bool applyBindposes = true;
        [SerializeField] private bool applyHumanoidAvatar = true;
        [SerializeField] private bool preserveAnimatorStateOnRebind = true;
        [SerializeField, InspectorName("Apply Bone Transform to Non Humanoid Bones")] private bool applyBoneTransforms = true;
        [SerializeField] private bool applyBonePositions = true;
        [SerializeField] private bool applyBoneRotations = false;
        [SerializeField] private bool applyBoneScales = true;

        private const float WeightEpsilon = 0.0001f;

        private Avatar dynamicAvatar;
        private Mesh runtimeMesh;
        private BindposeTrs[] baseBindposeTrs;
        private BindposeTrs[] blendedBindposeTrs;
        private Matrix4x4[] blendedBindposes;
        private HumanDescription baseHumanDescription;
        private SkeletonBone[] blendedSkeleton;
        private BoneBlendBinding[] boneBindings;
        private BoneBlendBinding[] extraBoneBindings = Array.Empty<BoneBlendBinding>();
        private HumanoidCorrectionBinding[] humanoidCorrectionBindings = Array.Empty<HumanoidCorrectionBinding>();
        private readonly List<AttachedOutfitRegistrySet> attachedOutfitRegistrySets = new List<AttachedOutfitRegistrySet>();
        private readonly Dictionary<string, int> baseSkeletonIndexByName = new Dictionary<string, int>();
        private bool runtimeCacheInitialized;
        private readonly HashSet<string> missingExtraBoneWarningPaths = new HashSet<string>();
        private readonly HashSet<DynamicBoneBlendTarget> invalidWeightWarningTargets = new HashSet<DynamicBoneBlendTarget>();
        private TargetRuntimeCache[] targetCaches;
        private readonly Subject<FbmWeightChange> fbmWeightSubject = new Subject<FbmWeightChange>();
        private readonly Subject<FbmWeightChange[]> fbmWeightSnapshotSubject = new Subject<FbmWeightChange[]>();
        private readonly Subject<float[]> indexedFbmWeightSubject = new Subject<float[]>();
        private float[] currentFbmWeights = Array.Empty<float>();
        private FbmWeightChange[] currentFbmWeightSnapshot = Array.Empty<FbmWeightChange>();
        private PbmDifferenceBlendShapeCache[] pbmDifferenceBlendShapeCaches = Array.Empty<PbmDifferenceBlendShapeCache>();
        private PbmDifferenceBoneCache[] pbmDifferenceBoneCaches = Array.Empty<PbmDifferenceBoneCache>();
        private readonly Subject<float> blendWeightSubject = new Subject<float>();
        private bool lastApplyBoneTransforms;
        private bool lastApplyBonePositions;
        private bool lastApplyBoneRotations;
        private bool lastApplyBoneScales;
        [NonSerialized] private StackMachineDiagnostic lastAnimatorDiagnostic;

        public IReadOnlyList<DynamicBoneBlendTarget> Targets => targets;
        public Observable<FbmWeightChange> FbmWeightChanged => fbmWeightSubject;
        /// <summary>Observes complete raw non-PBM weight snapshots after a DDB weight mutation.</summary>
        public Observable<FbmWeightChange[]> FbmWeightSnapshotChanged => fbmWeightSnapshotSubject;
        /// <summary>Gets the latest complete raw non-PBM weight snapshot.</summary>
        public IReadOnlyList<FbmWeightChange> CurrentFbmWeightSnapshot => currentFbmWeightSnapshot;
        public Observable<float[]> IndexedFbmWeightsChanged => indexedFbmWeightSubject;
        public IReadOnlyList<float> CurrentIndexedFbmWeights => currentFbmWeights;
        public Observable<float[]> TargetWeightsChanged => indexedFbmWeightSubject;
        public IReadOnlyList<float> CurrentTargetWeights => currentFbmWeights;
        public SkinnedMeshRenderer TargetSkinnedMeshRenderer => targetSkinnedMeshRenderer;
        public DynamicMorphAdapter DynamicMorphAdapter => dynamicMorphAdapter;
        /// <summary>Gets the immutable base Avatar used by FBM skeletal interpolation.</summary>
        public Avatar BaseAvatar => baseAvatar;
        /// <summary>Gets the immutable base bone registry used by FBM non-humanoid pose interpolation.</summary>
        public CharacterBoneRegistry BaseRegistry => baseRegistry;
        /// <summary>Gets the latest Animator resolution or Avatar build-root diagnostic, if any.</summary>
        public StackMachineDiagnostic LastAnimatorDiagnostic => lastAnimatorDiagnostic;

        public float BlendWeight => targets != null && targets.Count > 0 && IsApplicableTarget(targets[0]) ? targets[0].weight : 0f;
        public Observable<float> BlendWeightChanged => blendWeightSubject;

        /// <summary>
        /// Configures the Figure Builder references for a created Figure prefab.
        /// The supplied immutable assets are used to initialize runtime caches once in Start.
        /// </summary>
        public void ConfigureForFigure(
            SkinnedMeshRenderer renderer,
            Animator animator,
            Avatar avatar,
            CharacterBoneRegistry registry,
            IList<DynamicBoneBlendTarget> configuredTargets)
        {
            targetSkinnedMeshRenderer = renderer;
            dynamicMorphAdapter = GetComponent<DynamicMorphAdapter>();
            targetAnimator = animator;
            lastAnimatorDiagnostic = null;
            baseAvatar = avatar;
            baseRegistry = registry;
            targets.Clear();
            if (configuredTargets == null)
            {
                return;
            }

            for (int i = 0; i < configuredTargets.Count; i++)
            {
                DynamicBoneBlendTarget source = configuredTargets[i];
                if (source == null)
                {
                    continue;
                }

                targets.Add(new DynamicBoneBlendTarget
                {
                    blendName = source.blendName,
                    enabled = source.enabled,
                    weight = source.weight,
                    targetAvatar = source.targetAvatar,
                    targetRegistry = source.targetRegistry,
                    pbmDifferenceTargets = source.pbmDifferenceTargets != null
                        ? new List<DynamicBonePbmDifferenceTarget>(source.pbmDifferenceTargets)
                        : new List<DynamicBonePbmDifferenceTarget>()
                });
            }
        }

        public void SetAttachedOutfitRegistrySets(IReadOnlyList<AttachedOutfitRegistrySet> attachedOutfits)
        {
            attachedOutfitRegistrySets.Clear();
            if (attachedOutfits != null)
            {
                for (int i = 0; i < attachedOutfits.Count; i++)
                {
                    AttachedOutfitRegistrySet attachedOutfit = attachedOutfits[i];
                    if (attachedOutfit != null)
                    {
                        attachedOutfitRegistrySets.Add(attachedOutfit);
                    }
                }
            }

            if (!runtimeCacheInitialized)
            {
                return;
            }

            CacheExtraBoneBindings();
            CacheHumanoidCorrectionBindings();
            if (applyBoneTransforms)
            {
                ApplyExtraBoneTransforms();
            }

            if (applyHumanoidAvatar)
            {
                UpdateHumanoidAvatar();
            }
        }

        private static bool IsApplicableTarget(DynamicBoneBlendTarget target)
        {
            return target != null
                && target.enabled
                && !string.IsNullOrEmpty(target.blendName)
                && IsFiniteWeight(target.weight);
        }

        private static bool IsFiniteWeight(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private struct BindposeTrs
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool valid;
        }

        private struct TargetRuntimeCache
        {
            public int blendShapeIndex;
            public bool lastEnabled;
            public float lastWeight;
            public BindposeTrs[] bindposes;
            public HumanDescription humanDescription;
            public int[] skeletonIndices;
            public BonePoseData[] bonePoses;
        }

        private struct PbmDifferenceBlendShapeCache
        {
            public int blendShapeIndex;
            public int fbmTargetIndex;
            public int pbmTargetIndex;
        }

        private struct PbmDifferenceBoneCache
        {
            public int fbmTargetIndex;
            public int pbmTargetIndex;
            public BindposeTrs[] bindposes;
            public BonePoseData[] bonePoses;
            public HumanDescription humanDescription;
            public int[] skeletonIndices;
        }

        private struct PbmDifferenceBonePose
        {
            public int differenceIndex;
            public BonePoseData targetPose;
        }

        private struct BoneBlendBinding
        {
            public Transform transform;
            public BonePoseData basePose;
            public BonePoseData[] targetPoses;
            public PbmDifferenceBonePose[] pbmDifferencePoses;
        }

        private struct HumanoidCorrectionBinding
        {
            public int skeletonIndex;
            public Vector3 basePositionDelta;
            public Quaternion baseRotationDelta;
            public Vector3 baseScaleDelta;
            public Vector3[] targetPositionDeltas;
            public Quaternion[] targetRotationDeltas;
            public Vector3[] targetScaleDeltas;
        }

        private struct AnimatorParameterSnapshot
        {
            public int nameHash;
            public AnimatorControllerParameterType type;
            public float floatValue;
            public int intValue;
            public bool boolValue;
        }

        private struct AnimatorLayerSnapshot
        {
            public int layerIndex;
            public int stateHash;
            public float normalizedTime;
            public float layerWeight;
        }

        private struct AnimatorStateSnapshot
        {
            public float speed;
            public AnimatorLayerSnapshot[] layers;
        }

        private void Reset()
        {
            targetSkinnedMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            dynamicMorphAdapter = GetComponent<DynamicMorphAdapter>();
            ShapeSyncAnimatorResolver.TryResolve(transform, out targetAnimator, out lastAnimatorDiagnostic);
            if (targetAnimator != null)
            {
                baseAvatar = targetAnimator.avatar;
            }
        }

        private void Start()
        {
            if (dynamicMorphAdapter == null) dynamicMorphAdapter = GetComponent<DynamicMorphAdapter>();
            InitializeCache();
            ApplyAll(true);
        }

        private void Awake()
        {
            if (targetAnimator != null)
            {
                return;
            }

            ShapeSyncAnimatorResolver.TryResolve(transform, out targetAnimator, out lastAnimatorDiagnostic);
            if (baseAvatar == null && targetAnimator != null)
            {
                baseAvatar = targetAnimator.avatar;
            }
        }

    private void InitializeCache()
        {
            CacheRuntimeMeshAndBindposes();
            CacheAvatarDescriptions();
            CachePbmDifferenceTargets();
            CacheHumanoidCorrectionBindings();
            CacheBoneBindings();
            CacheExtraBoneBindings();
            CacheBlendShapeIndices();
            runtimeCacheInitialized = true;
        }

        private void CacheRuntimeMeshAndBindposes()
        {
            if (targetSkinnedMeshRenderer == null || targetSkinnedMeshRenderer.sharedMesh == null)
            {
                return;
            }

            Mesh sourceMesh = targetSkinnedMeshRenderer.sharedMesh;
            runtimeMesh = dynamicMorphAdapter != null
                ? dynamicMorphAdapter.CreateInitialRuntimeMesh(sourceMesh)
                : ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            if (runtimeMesh == null)
            {
                runtimeMesh = ShapeSyncMeshCloneUtility.Clone(sourceMesh);
            }
            runtimeMesh.name = "ShapeSyncRuntimeMesh";
            targetSkinnedMeshRenderer.sharedMesh = runtimeMesh;

            if (baseRegistry == null || sourceMesh.bindposes == null || sourceMesh.bindposes.Length == 0)
            {
                return;
            }

            if (!TryBuildBaseBindposeTrs(sourceMesh.bindposes.Length, out baseBindposeTrs))
            {
                Debug.LogWarning("DynamicBoneBlender bindpose blending disabled because base registry bindposes are incomplete or incompatible.", this);
                baseBindposeTrs = null;
                return;
            }

            blendedBindposeTrs = new BindposeTrs[baseBindposeTrs.Length];
            blendedBindposes = new Matrix4x4[baseBindposeTrs.Length];
        }

        private bool TryBuildBaseBindposeTrs(int bindposeCount, out BindposeTrs[] bindposes)
        {
            bindposes = new BindposeTrs[bindposeCount];
            bool[] assigned = new bool[bindposeCount];
            int assignedCount = 0;

            for (int i = baseRegistry.bonePoses.Count - 1; i >= 0; i--)
            {
                BonePoseData basePose = baseRegistry.bonePoses[i];
                if (basePose == null || !basePose.hasBindpose || basePose.bindposeIndex < 0 || basePose.bindposeIndex >= bindposeCount)
                {
                    continue;
                }

                int index = basePose.bindposeIndex;
                bindposes[index] = DecomposeMatrix(basePose.bindpose);
                if (!assigned[index])
                {
                    assigned[index] = true;
                    assignedCount++;
                }
            }

            return assignedCount == bindposeCount;
        }

        private BindposeTrs[] BuildTargetBindposeTrs(CharacterBoneRegistry targetRegistry, int bindposeCount)
        {
            if (baseRegistry == null || targetRegistry == null || targetRegistry.bonePoses == null)
            {
                return null;
            }

            BindposeTrs[] bindposes = new BindposeTrs[bindposeCount];
            Dictionary<string, BonePoseData> targetLookup = new Dictionary<string, BonePoseData>(targetRegistry.bonePoses.Count);
            for (int i = targetRegistry.bonePoses.Count - 1; i >= 0; i--)
            {
                BonePoseData targetPose = targetRegistry.bonePoses[i];
                if (targetPose != null && targetPose.hasBindpose && !string.IsNullOrEmpty(targetPose.boneName) && !targetLookup.ContainsKey(targetPose.boneName))
                {
                    targetLookup.Add(targetPose.boneName, targetPose);
                }
            }

            for (int i = baseRegistry.bonePoses.Count - 1; i >= 0; i--)
            {
                BonePoseData basePose = baseRegistry.bonePoses[i];
                if (basePose == null || !basePose.hasBindpose || basePose.bindposeIndex < 0 || basePose.bindposeIndex >= bindposeCount)
                {
                    continue;
                }

                if (!targetLookup.TryGetValue(basePose.boneName, out BonePoseData targetPose) || !targetPose.hasBindpose)
                {
                    continue;
                }

                bindposes[basePose.bindposeIndex] = DecomposeMatrix(targetPose.bindpose);
            }

            return bindposes;
        }

        private void CacheAvatarDescriptions()
        {
            int targetCount = targets != null ? targets.Count : 0;
            targetCaches = new TargetRuntimeCache[targetCount];
            currentFbmWeights = new float[targetCount];

            if (baseAvatar == null || !baseAvatar.isHuman)
            {
                blendedSkeleton = Array.Empty<SkeletonBone>();
                return;
            }

            baseHumanDescription = baseAvatar.humanDescription;
            SkeletonBone[] baseSkeleton = baseHumanDescription.skeleton;
            if (baseSkeleton == null)
            {
                blendedSkeleton = Array.Empty<SkeletonBone>();
                return;
            }

            blendedSkeleton = new SkeletonBone[baseSkeleton.Length];
            baseSkeletonIndexByName.Clear();
            for (int i = baseSkeleton.Length - 1; i >= 0; i--)
            {
                blendedSkeleton[i] = baseSkeleton[i];
                if (!string.IsNullOrEmpty(baseSkeleton[i].name) && !baseSkeletonIndexByName.ContainsKey(baseSkeleton[i].name))
                {
                    baseSkeletonIndexByName.Add(baseSkeleton[i].name, i);
                }
            }

            for (int i = 0; i < targetCount; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                TargetRuntimeCache cache = targetCaches[i];
                cache.blendShapeIndex = -1;
                cache.lastWeight = float.NaN;
                cache.lastEnabled = IsApplicableTarget(target);
                cache.bindposes = baseBindposeTrs != null && target != null && !string.IsNullOrEmpty(target.blendName)
                    ? BuildTargetBindposeTrs(target.targetRegistry, baseBindposeTrs.Length)
                    : null;
                cache.bonePoses = target != null && !string.IsNullOrEmpty(target.blendName)
                    ? BuildTargetPoseArray(target.targetRegistry)
                    : null;

                if (target != null && !string.IsNullOrEmpty(target.blendName) && baseAvatar != null && baseAvatar.isHuman)
                {
                    cache.humanDescription = BuildTargetHumanDescription(target.targetAvatar, target.targetRegistry);
                    cache.skeletonIndices = BuildSkeletonIndexMap(baseSkeleton, cache.humanDescription.skeleton);
                }
                else
                {
                    cache.skeletonIndices = Array.Empty<int>();
                }

                targetCaches[i] = cache;
            }
        }

        private BonePoseData[] BuildTargetPoseArray(CharacterBoneRegistry targetRegistry)
        {
            int baseCount = baseRegistry != null && baseRegistry.bonePoses != null ? baseRegistry.bonePoses.Count : 0;
            BonePoseData[] result = new BonePoseData[baseCount];
            if (targetRegistry == null || targetRegistry.bonePoses == null || baseCount == 0)
            {
                return result;
            }

            Dictionary<string, BonePoseData> targetPoseLookup = new Dictionary<string, BonePoseData>(targetRegistry.bonePoses.Count);
            for (int i = targetRegistry.bonePoses.Count - 1; i >= 0; i--)
            {
                BonePoseData pose = targetRegistry.bonePoses[i];
                if (pose != null && !string.IsNullOrEmpty(pose.boneName) && !targetPoseLookup.ContainsKey(pose.boneName))
                {
                    targetPoseLookup.Add(pose.boneName, pose);
                }
            }

            for (int i = baseCount - 1; i >= 0; i--)
            {
                BonePoseData basePose = baseRegistry.bonePoses[i];
                if (basePose != null && !string.IsNullOrEmpty(basePose.boneName) && targetPoseLookup.TryGetValue(basePose.boneName, out BonePoseData targetPose))
                {
                    result[i] = targetPose;
                }
            }

            return result;
        }

        private void CachePbmDifferenceTargets()
        {
            if (targets == null || targetCaches == null)
            {
                pbmDifferenceBoneCaches = Array.Empty<PbmDifferenceBoneCache>();
                return;
            }

            var caches = new List<PbmDifferenceBoneCache>();
            for (int pbmIndex = 0; pbmIndex < targets.Count; pbmIndex++)
            {
                DynamicBoneBlendTarget pbmTarget = targets[pbmIndex];
                if (pbmTarget == null || string.IsNullOrEmpty(pbmTarget.blendName)
                    || !pbmTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal)
                    || pbmTarget.pbmDifferenceTargets == null)
                {
                    continue;
                }

                for (int differenceIndex = 0; differenceIndex < pbmTarget.pbmDifferenceTargets.Count; differenceIndex++)
                {
                    DynamicBonePbmDifferenceTarget difference = pbmTarget.pbmDifferenceTargets[differenceIndex];
                    if (difference == null || string.IsNullOrEmpty(difference.fbmBlendName))
                    {
                        continue;
                    }

                    int fbmIndex = FindTargetIndex(difference.fbmBlendName);
                    if (fbmIndex < 0)
                    {
                        continue;
                    }

                    PbmDifferenceBoneCache cache = new PbmDifferenceBoneCache
                    {
                        fbmTargetIndex = fbmIndex,
                        pbmTargetIndex = pbmIndex,
                        bindposes = baseBindposeTrs != null ? BuildTargetBindposeTrs(difference.targetRegistry, baseBindposeTrs.Length) : null,
                        bonePoses = BuildTargetPoseArray(difference.targetRegistry),
                        skeletonIndices = Array.Empty<int>()
                    };

                    if (baseAvatar != null && baseAvatar.isHuman && baseHumanDescription.skeleton != null)
                    {
                        cache.humanDescription = BuildTargetHumanDescription(difference.targetAvatar, difference.targetRegistry);
                        cache.skeletonIndices = BuildSkeletonIndexMap(baseHumanDescription.skeleton, cache.humanDescription.skeleton);
                    }

                    caches.Add(cache);
                }
            }

            pbmDifferenceBoneCaches = caches.ToArray();
        }

        // Target meshes used by PBM Baker may share the source Avatar asset.  The
        // Registry is therefore authoritative for target Humanoid skeleton TRS;
        // overlay it onto the target Avatar's HumanDescription before DDB rebuilds
        // its runtime Avatar.
        private HumanDescription BuildTargetHumanDescription(Avatar targetAvatar, CharacterBoneRegistry targetRegistry)
        {
            HumanDescription description = targetAvatar != null && targetAvatar.isHuman
                ? targetAvatar.humanDescription
                : baseHumanDescription;
            SkeletonBone[] skeleton = description.skeleton;
            if (skeleton == null || targetRegistry == null || targetRegistry.bonePoses == null)
            {
                return description;
            }

            var skeletonIndexByName = new Dictionary<string, int>(skeleton.Length);
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (!string.IsNullOrEmpty(skeleton[i].name) && !skeletonIndexByName.ContainsKey(skeleton[i].name))
                {
                    skeletonIndexByName.Add(skeleton[i].name, i);
                }
            }

            SkeletonBone[] result = (SkeletonBone[])skeleton.Clone();
            for (int i = 0; i < targetRegistry.bonePoses.Count; i++)
            {
                BonePoseData pose = targetRegistry.bonePoses[i];
                if (pose == null || string.IsNullOrEmpty(pose.boneName))
                {
                    continue;
                }

                string boneName = GetLeafBoneName(pose.boneName);
                if (!skeletonIndexByName.TryGetValue(boneName, out int skeletonIndex))
                {
                    continue;
                }

                SkeletonBone bone = result[skeletonIndex];
                bone.position = pose.localPosition;
                bone.rotation = pose.localRotation;
                bone.scale = pose.localScale;
                result[skeletonIndex] = bone;
            }

            description.skeleton = result;
            return description;
        }

        private static string GetLeafBoneName(string bonePath)
        {
            int separator = bonePath.LastIndexOf('/');
            return separator >= 0 && separator + 1 < bonePath.Length ? bonePath.Substring(separator + 1) : bonePath;
        }

        private int FindTargetIndex(string blendName)
        {
            if (targets == null || string.IsNullOrEmpty(blendName))
            {
                return -1;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].blendName == blendName)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int[] BuildSkeletonIndexMap(SkeletonBone[] baseSkeleton, SkeletonBone[] targetSkeleton)
        {
            if (baseSkeleton == null || targetSkeleton == null)
            {
                return Array.Empty<int>();
            }

            int[] indices = new int[baseSkeleton.Length];
            Dictionary<string, int> targetSkeletonLookup = new Dictionary<string, int>(targetSkeleton.Length);
            for (int i = targetSkeleton.Length - 1; i >= 0; i--)
            {
                string boneName = targetSkeleton[i].name;
                if (!string.IsNullOrEmpty(boneName) && !targetSkeletonLookup.ContainsKey(boneName))
                {
                    targetSkeletonLookup.Add(boneName, i);
                }
            }

            for (int i = baseSkeleton.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(baseSkeleton[i].name) && targetSkeletonLookup.TryGetValue(baseSkeleton[i].name, out int targetIndex))
                {
                    indices[i] = targetIndex;
                }
                else
                {
                    indices[i] = -1;
                }
            }

            return indices;
        }

        private void CacheBoneBindings()
        {
            if (baseRegistry == null || baseRegistry.bonePoses == null)
            {
                boneBindings = Array.Empty<BoneBlendBinding>();
                return;
            }

            int targetCount = targetCaches != null ? targetCaches.Length : 0;
            HashSet<Transform> humanoidBones = BuildHumanoidBoneSet();
            List<BoneBlendBinding> bindings = new List<BoneBlendBinding>(baseRegistry.bonePoses.Count);
            for (int i = baseRegistry.bonePoses.Count - 1; i >= 0; i--)
            {
                BonePoseData basePose = baseRegistry.bonePoses[i];
                if (basePose == null || string.IsNullOrEmpty(basePose.boneName))
                {
                    continue;
                }

                Transform boneTransform = FindBoneTransform(basePose.boneName);
                if (boneTransform == null)
                {
                    continue;
                }

                if (humanoidBones.Contains(boneTransform))
                {
                    continue;
                }

                BonePoseData[] targetPoses = new BonePoseData[targetCount];
                bool hasAnyTarget = false;
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    BonePoseData[] cachePoses = targetCaches[targetIndex].bonePoses;
                    if (cachePoses != null && i < cachePoses.Length && cachePoses[i] != null)
                    {
                        targetPoses[targetIndex] = cachePoses[i];
                        hasAnyTarget = true;
                    }
                }

                if (!hasAnyTarget)
                {
                    continue;
                }

                bindings.Add(new BoneBlendBinding
                {
                    transform = boneTransform,
                    basePose = basePose,
                    targetPoses = targetPoses,
                    pbmDifferencePoses = BuildFigurePbmDifferencePoses(i)
                });
            }

            boneBindings = bindings.ToArray();
        }

        private PbmDifferenceBonePose[] BuildFigurePbmDifferencePoses(int basePoseIndex)
        {
            if (pbmDifferenceBoneCaches == null || pbmDifferenceBoneCaches.Length == 0)
            {
                return Array.Empty<PbmDifferenceBonePose>();
            }

            var result = new List<PbmDifferenceBonePose>();
            for (int i = 0; i < pbmDifferenceBoneCaches.Length; i++)
            {
                BonePoseData[] poses = pbmDifferenceBoneCaches[i].bonePoses;
                if (poses != null && basePoseIndex >= 0 && basePoseIndex < poses.Length && poses[basePoseIndex] != null)
                {
                    result.Add(new PbmDifferenceBonePose { differenceIndex = i, targetPose = poses[basePoseIndex] });
                }
            }

            return result.ToArray();
        }


        private HashSet<Transform> BuildHumanoidBoneSet()
        {
            HashSet<Transform> humanoidBones = new HashSet<Transform>();
            if (targetAnimator == null || !targetAnimator.isHuman)
            {
                return humanoidBones;
            }

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = targetAnimator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    humanoidBones.Add(bone);
                }
            }

            return humanoidBones;
        }

        private Transform FindBoneTransform(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return transform;
            }

            return transform.Find(relativePath);
        }

        private void CacheExtraBoneBindings()
        {
            missingExtraBoneWarningPaths.Clear();
            if (attachedOutfitRegistrySets.Count == 0)
            {
                extraBoneBindings = Array.Empty<BoneBlendBinding>();
                return;
            }

            int targetCount = targets != null ? targets.Count : 0;
            HashSet<Transform> humanoidBones = BuildHumanoidBoneSet();
            List<BoneBlendBinding> bindings = new List<BoneBlendBinding>();

            for (int outfitIndex = 0; outfitIndex < attachedOutfitRegistrySets.Count; outfitIndex++)
            {
                AttachedOutfitRegistrySet attachedOutfit = attachedOutfitRegistrySets[outfitIndex];
                CharacterBoneRegistry baseExtraRegistry = attachedOutfit.BaseExtraBoneRegistry;
                if (baseExtraRegistry == null || baseExtraRegistry.bonePoses == null)
                {
                    continue;
                }

                Dictionary<string, BonePoseData>[] targetPoseLookups = new Dictionary<string, BonePoseData>[targetCount];
                Dictionary<string, BonePoseData>[] differencePoseLookups = new Dictionary<string, BonePoseData>[pbmDifferenceBoneCaches.Length];
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    DynamicBoneBlendTarget target = targets[targetIndex];
                    if (target == null || string.IsNullOrEmpty(target.blendName)
                        || !attachedOutfit.TryGetFbmExtraBoneRegistry(target.blendName, out CharacterBoneRegistry targetExtraRegistry))
                    {
                        continue;
                    }

                    targetPoseLookups[targetIndex] = BuildPoseLookup(targetExtraRegistry);
                }

                for (int differenceIndex = 0; differenceIndex < pbmDifferenceBoneCaches.Length; differenceIndex++)
                {
                    PbmDifferenceBoneCache difference = pbmDifferenceBoneCaches[differenceIndex];
                    DynamicBoneBlendTarget pbmTarget = difference.pbmTargetIndex >= 0 && difference.pbmTargetIndex < targets.Count
                        ? targets[difference.pbmTargetIndex]
                        : null;
                    DynamicBoneBlendTarget fbmTarget = difference.fbmTargetIndex >= 0 && difference.fbmTargetIndex < targets.Count
                        ? targets[difference.fbmTargetIndex]
                        : null;
                    if (pbmTarget == null || fbmTarget == null)
                    {
                        continue;
                    }

                    string differenceName = BlendShapeReservedPrefixes.Pbm + fbmTarget.blendName + "_" + pbmTarget.blendName.Substring(BlendShapeReservedPrefixes.Pbm.Length);
                    if (attachedOutfit.TryGetFbmExtraBoneRegistry(differenceName, out CharacterBoneRegistry differenceRegistry))
                    {
                        differencePoseLookups[differenceIndex] = BuildPoseLookup(differenceRegistry);
                    }
                }

                for (int poseIndex = 0; poseIndex < baseExtraRegistry.bonePoses.Count; poseIndex++)
                {
                    BonePoseData basePose = baseExtraRegistry.bonePoses[poseIndex];
                    if (basePose == null || string.IsNullOrEmpty(basePose.boneName))
                    {
                        continue;
                    }

                    Transform boneTransform = FindBoneTransform(basePose.boneName);
                    if (boneTransform == null)
                    {
                        Debug.LogWarning($"DynamicBoneBlender skipped missing Extra Bone path '{basePose.boneName}'.", this);
                        continue;
                    }

                    if (humanoidBones.Contains(boneTransform))
                    {
                        Debug.LogWarning($"DynamicBoneBlender skipped Humanoid Extra Bone path '{basePose.boneName}'.", this);
                        continue;
                    }

                    BonePoseData[] targetPoses = new BonePoseData[targetCount];
                    for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    {
                        Dictionary<string, BonePoseData> targetLookup = targetPoseLookups[targetIndex];
                        if (targetLookup != null)
                        {
                            targetLookup.TryGetValue(basePose.boneName, out targetPoses[targetIndex]);
                        }
                    }

                    var differencePoses = new List<PbmDifferenceBonePose>();
                    for (int differenceIndex = 0; differenceIndex < differencePoseLookups.Length; differenceIndex++)
                    {
                        Dictionary<string, BonePoseData> lookup = differencePoseLookups[differenceIndex];
                        if (lookup != null && lookup.TryGetValue(basePose.boneName, out BonePoseData targetPose))
                        {
                            differencePoses.Add(new PbmDifferenceBonePose { differenceIndex = differenceIndex, targetPose = targetPose });
                        }
                    }

                    bindings.Add(new BoneBlendBinding
                    {
                        transform = boneTransform,
                        basePose = basePose,
                        targetPoses = targetPoses,
                        pbmDifferencePoses = differencePoses.ToArray()
                    });
                }
            }

            extraBoneBindings = bindings.ToArray();
        }

        private static Dictionary<string, BonePoseData> BuildPoseLookup(CharacterBoneRegistry registry)
        {
            if (registry == null || registry.bonePoses == null)
            {
                return null;
            }

            Dictionary<string, BonePoseData> lookup = new Dictionary<string, BonePoseData>(registry.bonePoses.Count);
            for (int i = 0; i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose != null && !string.IsNullOrEmpty(pose.boneName) && !lookup.ContainsKey(pose.boneName))
                {
                    lookup.Add(pose.boneName, pose);
                }
            }

            return lookup;
        }

        public bool TryValidateHumanoidBoneCorrectionProfile(
            ShapeSyncHumanoidBoneCorrectionProfile profile,
            IReadOnlyList<AttachedOutfitRegistrySet> attachedOutfits,
            out string error)
        {
            error = null;
            if (profile == null)
            {
                return true;
            }

            if (targetAnimator == null || !targetAnimator.isHuman)
            {
                error = "Figure Animator must be a valid Humanoid for Humanoid Bone Correction.";
                return false;
            }

            IReadOnlyList<ShapeSyncHumanoidBoneCorrection> corrections = profile.Corrections;
            if (corrections == null)
            {
                error = "Humanoid Bone Correction Profile correction list is null.";
                return false;
            }

            HashSet<HumanBodyBones> ownedBones = new HashSet<HumanBodyBones>();
            if (attachedOutfits != null)
            {
                for (int outfitIndex = 0; outfitIndex < attachedOutfits.Count; outfitIndex++)
                {
                    AttachedOutfitRegistrySet attachedOutfit = attachedOutfits[outfitIndex];
                    ShapeSyncHumanoidBoneCorrectionProfile attachedProfile = attachedOutfit?.HumanoidBoneCorrectionProfile;
                    AddOwnedHumanoidCorrectionBones(attachedProfile, ownedBones);
                    IReadOnlyList<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile> attachedFbmProfiles = attachedOutfit?.FbmHumanoidBoneCorrectionProfiles;
                    if (attachedFbmProfiles == null) continue;
                    for (int fbmIndex = 0; fbmIndex < attachedFbmProfiles.Count; fbmIndex++)
                    {
                        AddOwnedHumanoidCorrectionBones(attachedFbmProfiles[fbmIndex]?.targetProfile, ownedBones);
                    }
                }
            }

            HashSet<HumanBodyBones> profileBones = new HashSet<HumanBodyBones>();
            for (int i = 0; i < corrections.Count; i++)
            {
                ShapeSyncHumanoidBoneCorrection correction = corrections[i];
                if (correction == null || correction.bone == HumanBodyBones.LastBone)
                {
                    error = "Humanoid Bone Correction Profile contains an invalid correction entry.";
                    return false;
                }

                if (!profileBones.Add(correction.bone))
                {
                    error = $"Humanoid Bone Correction Profile contains duplicated bone '{correction.bone}'.";
                    return false;
                }

                if (ownedBones.Contains(correction.bone))
                {
                    error = $"Humanoid Bone Correction bone '{correction.bone}' is already owned by an attached Outfit.";
                    return false;
                }

                if (!IsFinite(correction.localPositionDelta) || !IsFinite(correction.localScaleDelta) || !IsFinite(correction.localRotationDelta)
                    || QuaternionLengthSquared(correction.localRotationDelta) <= Mathf.Epsilon)
                {
                    error = $"Humanoid Bone Correction bone '{correction.bone}' has a non-finite or zero rotation/TRS delta.";
                    return false;
                }

                if (targetAnimator.GetBoneTransform(correction.bone) == null || !TryGetHumanoidSkeletonIndex(correction.bone, out _))
                {
                    error = $"Figure Humanoid mapping or skeleton entry is missing for correction bone '{correction.bone}'.";
                    return false;
                }
            }

            return true;
        }

        private static void AddOwnedHumanoidCorrectionBones(ShapeSyncHumanoidBoneCorrectionProfile profile, HashSet<HumanBodyBones> ownedBones)
        {
            IReadOnlyList<ShapeSyncHumanoidBoneCorrection> corrections = profile?.Corrections;
            if (corrections == null) return;
            for (int correctionIndex = 0; correctionIndex < corrections.Count; correctionIndex++)
            {
                ShapeSyncHumanoidBoneCorrection correction = corrections[correctionIndex];
                if (correction != null) ownedBones.Add(correction.bone);
            }
        }

        public bool TryValidateFbmHumanoidBoneCorrectionProfiles(
            IReadOnlyList<ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile> profiles,
            IReadOnlyList<AttachedOutfitRegistrySet> attachedOutfits,
            out string error)
        {
            error = null;
            if (profiles == null) return true;
            HashSet<string> names = new HashSet<string>();
            for (int i = 0; i < profiles.Count; i++)
            {
                ShapeSyncOutfitFbmHumanoidBoneCorrectionProfile entry = profiles[i];
                if (entry == null || string.IsNullOrEmpty(entry.blendName) || entry.targetProfile == null)
                {
                    error = "FBM Humanoid Bone Correction Profiles contains an incomplete entry.";
                    return false;
                }
                if (!names.Add(entry.blendName))
                {
                    error = $"FBM Humanoid Bone Correction Profiles contains duplicated blendName '{entry.blendName}'.";
                    return false;
                }
                if (!ContainsBlendTarget(entry.blendName))
                {
                    error = $"FBM Humanoid Bone Correction Profile '{entry.blendName}' has no matching Figure FBM target.";
                    return false;
                }
                if (!TryValidateHumanoidBoneCorrectionProfile(entry.targetProfile, attachedOutfits, out error)) return false;
            }
            return true;
        }

        private bool ContainsBlendTarget(string blendName)
        {
            if (targets == null) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].blendName == blendName) return true;
            }
            return false;
        }

        private void CacheHumanoidCorrectionBindings()
        {
            if (attachedOutfitRegistrySets.Count == 0 || targetAnimator == null)
            {
                humanoidCorrectionBindings = Array.Empty<HumanoidCorrectionBinding>();
                return;
            }

            int targetCount = targets != null ? targets.Count : 0;
            List<HumanoidCorrectionBinding> bindings = new List<HumanoidCorrectionBinding>();
            for (int outfitIndex = 0; outfitIndex < attachedOutfitRegistrySets.Count; outfitIndex++)
            {
                AttachedOutfitRegistrySet outfit = attachedOutfitRegistrySets[outfitIndex];
                int firstBindingIndex = bindings.Count;
                Dictionary<HumanBodyBones, int> bindingByBone = new Dictionary<HumanBodyBones, int>();
                AddHumanoidCorrectionProfileBindings(outfit.HumanoidBoneCorrectionProfile, targetCount, bindingByBone, bindings, -1);

                // Each target profile is an absolute final correction for that FBM. Convert it to
                // Target - Base below, so the runtime path remains Base + sum(FBM * delta).
                for (int bindingIndex = firstBindingIndex; bindingIndex < bindings.Count; bindingIndex++)
                {
                    HumanoidCorrectionBinding binding = bindings[bindingIndex];
                    for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    {
                        binding.targetPositionDeltas[targetIndex] = binding.basePositionDelta;
                        binding.targetRotationDeltas[targetIndex] = binding.baseRotationDelta;
                        binding.targetScaleDeltas[targetIndex] = binding.baseScaleDelta;
                    }
                    bindings[bindingIndex] = binding;
                }

                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    DynamicBoneBlendTarget target = targets[targetIndex];
                    if (target != null && outfit.TryGetFbmHumanoidBoneCorrectionProfile(target.blendName, out ShapeSyncHumanoidBoneCorrectionProfile profile))
                    {
                        AddHumanoidCorrectionProfileBindings(profile, targetCount, bindingByBone, bindings, targetIndex);
                    }
                }

                for (int bindingIndex = firstBindingIndex; bindingIndex < bindings.Count; bindingIndex++)
                {
                    HumanoidCorrectionBinding binding = bindings[bindingIndex];
                    for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    {
                        binding.targetPositionDeltas[targetIndex] -= binding.basePositionDelta;
                        binding.targetScaleDeltas[targetIndex] -= binding.baseScaleDelta;
                        binding.targetRotationDeltas[targetIndex] = NormalizeQuaternion(binding.targetRotationDeltas[targetIndex] * Quaternion.Inverse(binding.baseRotationDelta));
                    }
                    bindings[bindingIndex] = binding;
                }
            }

            humanoidCorrectionBindings = bindings.ToArray();
        }

        private void AddHumanoidCorrectionProfileBindings(
            ShapeSyncHumanoidBoneCorrectionProfile profile,
            int targetCount,
            Dictionary<HumanBodyBones, int> bindingByBone,
            List<HumanoidCorrectionBinding> bindings,
            int targetIndex)
        {
            IReadOnlyList<ShapeSyncHumanoidBoneCorrection> corrections = profile?.Corrections;
            if (corrections == null) return;
            for (int correctionIndex = 0; correctionIndex < corrections.Count; correctionIndex++)
            {
                ShapeSyncHumanoidBoneCorrection correction = corrections[correctionIndex];
                if (correction == null || !TryGetHumanoidSkeletonIndex(correction.bone, out int skeletonIndex)) continue;
                if (!bindingByBone.TryGetValue(correction.bone, out int bindingIndex))
                {
                    bindingIndex = bindings.Count;
                    bindingByBone.Add(correction.bone, bindingIndex);
                    bindings.Add(new HumanoidCorrectionBinding
                    {
                        skeletonIndex = skeletonIndex,
                        baseRotationDelta = Quaternion.identity,
                        targetPositionDeltas = new Vector3[targetCount],
                        targetRotationDeltas = CreateIdentityQuaternionArray(targetCount),
                        targetScaleDeltas = new Vector3[targetCount]
                    });
                }

                HumanoidCorrectionBinding binding = bindings[bindingIndex];
                if (targetIndex < 0)
                {
                    binding.basePositionDelta = correction.localPositionDelta;
                    binding.baseRotationDelta = NormalizeQuaternion(correction.localRotationDelta);
                    binding.baseScaleDelta = correction.localScaleDelta;
                }
                else
                {
                    binding.targetPositionDeltas[targetIndex] = correction.localPositionDelta;
                    binding.targetRotationDeltas[targetIndex] = NormalizeQuaternion(correction.localRotationDelta);
                    binding.targetScaleDeltas[targetIndex] = correction.localScaleDelta;
                }
                bindings[bindingIndex] = binding;
            }
        }

        private static Quaternion[] CreateIdentityQuaternionArray(int length)
        {
            Quaternion[] result = new Quaternion[length];
            for (int i = 0; i < result.Length; i++) result[i] = Quaternion.identity;
            return result;
        }

        private bool TryGetHumanoidSkeletonIndex(HumanBodyBones bone, out int skeletonIndex)
        {
            skeletonIndex = -1;
            Transform transform = targetAnimator != null ? targetAnimator.GetBoneTransform(bone) : null;
            if (transform == null)
            {
                return false;
            }

            if (baseSkeletonIndexByName.TryGetValue(transform.name, out skeletonIndex))
            {
                return true;
            }

            SkeletonBone[] skeleton = baseAvatar != null && baseAvatar.isHuman ? baseAvatar.humanDescription.skeleton : null;
            if (skeleton == null)
            {
                return false;
            }

            for (int i = 0; i < skeleton.Length; i++)
            {
                if (skeleton[i].name == transform.name)
                {
                    skeletonIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFiniteWeight(value.x) && IsFiniteWeight(value.y) && IsFiniteWeight(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFiniteWeight(value.x) && IsFiniteWeight(value.y) && IsFiniteWeight(value.z) && IsFiniteWeight(value.w);
        }

        private static float QuaternionLengthSquared(Quaternion value)
        {
            return value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
        }

    private void ApplyExtraBoneTransforms()
        {
            ApplyBoneBindingTransforms(extraBoneBindings, true);
        }

    private void ApplyBoneBindingTransforms(BoneBlendBinding[] bindings, bool warnWhenMissing)
        {
            if (bindings == null || targets == null)
            {
                return;
            }

            int targetCount = targets.Count;
            for (int i = bindings.Length - 1; i >= 0; i--)
            {
                BoneBlendBinding binding = bindings[i];
                Transform boneTransform = binding.transform;
                if (boneTransform == null)
                {
                    if (warnWhenMissing && missingExtraBoneWarningPaths.Add(binding.basePose.boneName))
                    {
                        Debug.LogWarning($"DynamicBoneBlender skipped missing Extra Bone path '{binding.basePose.boneName}'.", this);
                    }

                    continue;
                }

                Vector3 position = binding.basePose.localPosition;
                Vector3 scale = binding.basePose.localScale;
                Quaternion rotation = binding.basePose.localRotation;

                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    DynamicBoneBlendTarget target = targets[targetIndex];
                    if (!IsApplicableTarget(target) || binding.targetPoses == null || targetIndex >= binding.targetPoses.Length)
                    {
                        continue;
                    }

                    BonePoseData targetPose = binding.targetPoses[targetIndex];
                    if (targetPose == null)
                    {
                        continue;
                    }

                    float fbm = target.weight;
                    position += (targetPose.localPosition - binding.basePose.localPosition) * fbm;
                    scale += (targetPose.localScale - binding.basePose.localScale) * fbm;
                    Quaternion deltaR = targetPose.localRotation * Quaternion.Inverse(binding.basePose.localRotation);
                    Quaternion weightedDeltaR = Quaternion.SlerpUnclamped(Quaternion.identity, deltaR, fbm);
                    rotation = weightedDeltaR * rotation;
                }

                if (binding.pbmDifferencePoses != null)
                {
                    for (int differencePoseIndex = 0; differencePoseIndex < binding.pbmDifferencePoses.Length; differencePoseIndex++)
                    {
                        PbmDifferenceBonePose differencePose = binding.pbmDifferencePoses[differencePoseIndex];
                        if (differencePose.differenceIndex < 0 || differencePose.differenceIndex >= pbmDifferenceBoneCaches.Length || differencePose.targetPose == null)
                        {
                            continue;
                        }

                        PbmDifferenceBoneCache difference = pbmDifferenceBoneCaches[differencePose.differenceIndex];
                        BonePoseData fbmPose = GetTargetPose(binding.targetPoses, difference.fbmTargetIndex);
                        BonePoseData pbmPose = GetTargetPose(binding.targetPoses, difference.pbmTargetIndex);
                        if (fbmPose == null || pbmPose == null)
                        {
                            continue;
                        }

                        float product = GetEffectiveTargetWeight(difference.fbmTargetIndex) * GetEffectiveTargetWeight(difference.pbmTargetIndex);
                        position += (differencePose.targetPose.localPosition - binding.basePose.localPosition
                            - (fbmPose.localPosition - binding.basePose.localPosition)
                            - (pbmPose.localPosition - binding.basePose.localPosition)) * product;
                        scale += (differencePose.targetPose.localScale - binding.basePose.localScale
                            - (fbmPose.localScale - binding.basePose.localScale)
                            - (pbmPose.localScale - binding.basePose.localScale)) * product;

                        Quaternion fullDirect = ComposePairRotation(binding.basePose.localRotation, fbmPose.localRotation, pbmPose.localRotation,
                            difference.fbmTargetIndex, difference.pbmTargetIndex);
                        Quaternion correction = differencePose.targetPose.localRotation * Quaternion.Inverse(fullDirect);
                        rotation = Quaternion.SlerpUnclamped(Quaternion.identity, correction, product) * rotation;
                    }
                }

                if (applyBonePositions)
                {
                    boneTransform.localPosition = position;
                }

                if (applyBoneRotations)
                {
                    boneTransform.localRotation = NormalizeQuaternion(rotation);
                }

                if (applyBoneScales)
                {
                    boneTransform.localScale = scale;
                }
            }
        }


        private void CacheBlendShapeIndices()
        {
            Mesh mesh = targetSkinnedMeshRenderer != null ? targetSkinnedMeshRenderer.sharedMesh : null;
            if (mesh == null || targetCaches == null || targets == null)
            {
                return;
            }

            for (int i = 0; i < targetCaches.Length; i++)
            {
                TargetRuntimeCache cache = targetCaches[i];
                DynamicBoneBlendTarget target = targets[i];
                cache.blendShapeIndex = target != null && !string.IsNullOrEmpty(target.blendName) ? mesh.GetBlendShapeIndex(target.blendName) : -1;
                targetCaches[i] = cache;
            }

            List<PbmDifferenceBlendShapeCache> differences = new List<PbmDifferenceBlendShapeCache>();
            for (int pbmTargetIndex = 0; pbmTargetIndex < targets.Count; pbmTargetIndex++)
            {
                DynamicBoneBlendTarget pbmTarget = targets[pbmTargetIndex];
                if (pbmTarget == null || string.IsNullOrEmpty(pbmTarget.blendName) || !pbmTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                {
                    continue;
                }

                string pbmName = pbmTarget.blendName.Substring(BlendShapeReservedPrefixes.Pbm.Length);
                for (int fbmTargetIndex = 0; fbmTargetIndex < targets.Count; fbmTargetIndex++)
                {
                    DynamicBoneBlendTarget fbmTarget = targets[fbmTargetIndex];
                    if (fbmTarget == null || string.IsNullOrEmpty(fbmTarget.blendName) || fbmTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int blendShapeIndex = mesh.GetBlendShapeIndex($"{BlendShapeReservedPrefixes.Pbm}{fbmTarget.blendName}_{pbmName}");
                    if (blendShapeIndex >= 0)
                    {
                        differences.Add(new PbmDifferenceBlendShapeCache
                        {
                            blendShapeIndex = blendShapeIndex,
                            fbmTargetIndex = fbmTargetIndex,
                            pbmTargetIndex = pbmTargetIndex
                        });
                    }
                }
            }

            pbmDifferenceBlendShapeCaches = differences.ToArray();
        }

        private void LateUpdate()
        {
            bool changed = DetectTargetChanges(false);
            if (changed)
            {
                ApplyAll(false);
                PublishChangedWeights();
                return;
            }

            if (applyBoneTransforms)
            {
                ApplyBoneTransforms();
            }
        }

        private bool DetectTargetChanges(bool force)
        {
            if (targets == null || targetCaches == null)
            {
                return false;
            }

            bool changed = force;
            if (force
                || lastApplyBoneTransforms != applyBoneTransforms
                || lastApplyBonePositions != applyBonePositions
                || lastApplyBoneRotations != applyBoneRotations
                || lastApplyBoneScales != applyBoneScales)
            {
                changed = true;
                lastApplyBoneTransforms = applyBoneTransforms;
                lastApplyBonePositions = applyBonePositions;
                lastApplyBoneRotations = applyBoneRotations;
                lastApplyBoneScales = applyBoneScales;
            }

            int count = Mathf.Min(targets.Count, targetCaches.Length);
            for (int i = 0; i < count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                TargetRuntimeCache cache = targetCaches[i];
                WarnIfInvalidWeight(target);
                bool enabled = IsApplicableTarget(target);
                float weight = enabled ? target.weight : 0f;
                if (force || enabled != cache.lastEnabled || Mathf.Abs(weight - cache.lastWeight) > WeightEpsilon || float.IsNaN(cache.lastWeight))
                {
                    cache.lastEnabled = enabled;
                    cache.lastWeight = weight;
                    targetCaches[i] = cache;
                    changed = true;
                }
            }

            return changed;
        }

        private void PublishChangedWeights()
        {
            if (targets == null || targetCaches == null)
            {
                return;
            }

            int count = Mathf.Min(targets.Count, targetCaches.Length);
            var snapshot = new List<FbmWeightChange>(count);
            for (int i = 0; i < count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                currentFbmWeights[i] = IsApplicableTarget(target) ? target.weight : 0f;
                if (target == null || string.IsNullOrEmpty(target.blendName))
                {
                    continue;
                }

                bool enabled = IsApplicableTarget(target);
                float weight = currentFbmWeights[i];
                snapshot.Add(new FbmWeightChange(target.blendName, weight, enabled));
            }
            currentFbmWeightSnapshot = snapshot.ToArray();
            fbmWeightSnapshotSubject.OnNext(currentFbmWeightSnapshot);
            for (int i = 0; i < currentFbmWeightSnapshot.Length; i++)
            {
                FbmWeightChange change = currentFbmWeightSnapshot[i];
                fbmWeightSubject.OnNext(change);
                if (i == 0)
                {
                    blendWeightSubject.OnNext(change.Weight);
                }
            }
            indexedFbmWeightSubject.OnNext(currentFbmWeights);
        }

        private void ApplyAll(bool force)
        {
            if (force)
            {
                DetectTargetChanges(true);
            }

            ApplyBodyBlendShapes();

            if (applyBindposes)
            {
                UpdateBindposes();
            }

            if (applyHumanoidAvatar)
            {
                UpdateHumanoidAvatar();
            }

            if (applyBoneTransforms)
            {
                ApplyBoneTransforms();
            }

            if (force)
            {
                PublishChangedWeights();
            }
        }

        private void ApplyBodyBlendShapes()
        {
            if (targetSkinnedMeshRenderer == null || targets == null || targetCaches == null)
            {
                return;
            }

            int count = Mathf.Min(targets.Count, targetCaches.Length);
            for (int i = 0; i < count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                if (target == null || string.IsNullOrEmpty(target.blendName))
                {
                    continue;
                }

                int blendShapeIndex = targetCaches[i].blendShapeIndex;
                if (blendShapeIndex >= 0)
                {
                    float fbm = IsApplicableTarget(target) ? target.weight : 0f;
                    WriteFigureBlendShapeWeight(blendShapeIndex, fbm * 100f);
                }
            }

            for (int i = 0; i < pbmDifferenceBlendShapeCaches.Length; i++)
            {
                PbmDifferenceBlendShapeCache cache = pbmDifferenceBlendShapeCaches[i];
                float fbmWeight = GetEffectiveTargetWeight(cache.fbmTargetIndex);
                float pbmWeight = GetEffectiveTargetWeight(cache.pbmTargetIndex);
                WriteFigureBlendShapeWeight(cache.blendShapeIndex, fbmWeight * pbmWeight * 100f);
            }
        }

        private void WriteFigureBlendShapeWeight(int blendShapeIndex, float weight)
        {
            if (dynamicMorphAdapter != null && dynamicMorphAdapter.WriteFigureBlendShapeWeight(blendShapeIndex, weight))
            {
                return;
            }

            if (targetSkinnedMeshRenderer != null)
            {
                targetSkinnedMeshRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
            }
        }

        public void ReapplyFigureMorphWeightsAfterMeshReplacement(Mesh mesh)
        {
            runtimeMesh = mesh;
            // Slot replacement preserves every index. Reapply logical FBM/PBM values without
            // rebuilding the DDB cache so the new renderer Mesh is immediately coherent.
            ApplyBodyBlendShapes();
        }

        private static BonePoseData GetTargetPose(BonePoseData[] poses, int targetIndex)
        {
            return poses != null && targetIndex >= 0 && targetIndex < poses.Length ? poses[targetIndex] : null;
        }

        private static Quaternion ComposePairRotation(Quaternion baseRotation, Quaternion fbmRotation, Quaternion pbmRotation, int fbmIndex, int pbmIndex)
        {
            Quaternion fbmDelta = fbmRotation * Quaternion.Inverse(baseRotation);
            Quaternion pbmDelta = pbmRotation * Quaternion.Inverse(baseRotation);
            return fbmIndex <= pbmIndex
                ? pbmDelta * fbmDelta * baseRotation
                : fbmDelta * pbmDelta * baseRotation;
        }

        /// <summary>
        /// Ensures a target entry required by PBM Baker. The supplied avatar and registry
        /// are associated with the target name as Figure Builder target data.
        /// </summary>
        public void EnsureTarget(string blendName, Avatar targetAvatar, CharacterBoneRegistry targetRegistry)
        {
            if (string.IsNullOrEmpty(blendName))
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                DynamicBoneBlendTarget target = targets[i];
                if (target != null && target.blendName == blendName)
                {
                    target.targetAvatar = targetAvatar;
                    target.targetRegistry = targetRegistry;
                    return;
                }
            }

            targets.Add(new DynamicBoneBlendTarget
            {
                blendName = blendName,
                enabled = true,
                targetAvatar = targetAvatar,
                targetRegistry = targetRegistry
            });
        }

        /// <summary>
        /// Registers the absolute FBM+PBM bone target under its PBM owner.  Names
        /// are used only here/editor cache construction; runtime application uses
        /// resolved target-index pairs.
        /// </summary>
        public void SetPbmDifferenceTarget(string pbmBlendName, string fbmBlendName, Avatar targetAvatar, CharacterBoneRegistry targetRegistry)
        {
            if (string.IsNullOrEmpty(pbmBlendName) || string.IsNullOrEmpty(fbmBlendName))
            {
                return;
            }

            DynamicBoneBlendTarget pbmTarget = null;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].blendName == pbmBlendName)
                {
                    pbmTarget = targets[i];
                    break;
                }
            }

            if (pbmTarget == null)
            {
                return;
            }

            if (pbmTarget.pbmDifferenceTargets == null)
            {
                pbmTarget.pbmDifferenceTargets = new List<DynamicBonePbmDifferenceTarget>();
            }

            for (int i = 0; i < pbmTarget.pbmDifferenceTargets.Count; i++)
            {
                DynamicBonePbmDifferenceTarget entry = pbmTarget.pbmDifferenceTargets[i];
                if (entry != null && entry.fbmBlendName == fbmBlendName)
                {
                    entry.targetAvatar = targetAvatar;
                    entry.targetRegistry = targetRegistry;
                    return;
                }
            }

            pbmTarget.pbmDifferenceTargets.Add(new DynamicBonePbmDifferenceTarget
            {
                fbmBlendName = fbmBlendName,
                targetAvatar = targetAvatar,
                targetRegistry = targetRegistry
            });
        }

        private float GetEffectiveTargetWeight(int targetIndex)
        {
            if (targets == null || targetIndex < 0 || targetIndex >= targets.Count)
            {
                return 0f;
            }

            DynamicBoneBlendTarget target = targets[targetIndex];
            return IsApplicableTarget(target) ? target.weight : 0f;
        }

        private void WarnIfInvalidWeight(DynamicBoneBlendTarget target)
        {
            if (target == null || !target.enabled || string.IsNullOrEmpty(target.blendName) || IsFiniteWeight(target.weight))
            {
                invalidWeightWarningTargets.Remove(target);
                return;
            }

            if (invalidWeightWarningTargets.Add(target))
            {
                Debug.LogWarning($"DynamicBoneBlender ignored non-finite weight for target '{target.blendName}'.", this);
            }
        }

        private void UpdateBindposes()
        {
            if (runtimeMesh == null || baseBindposeTrs == null || blendedBindposeTrs == null || blendedBindposes == null || targets == null || targetCaches == null)
            {
                return;
            }

            for (int i = blendedBindposeTrs.Length - 1; i >= 0; i--)
            {
                BindposeTrs blended = baseBindposeTrs[i];
                if (!blended.valid)
                {
                    continue;
                }

                Quaternion rotation = blended.rotation;
                Vector3 position = blended.position;
                Vector3 scale = blended.scale;

                int targetCount = Mathf.Min(targets.Count, targetCaches.Length);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    DynamicBoneBlendTarget target = targets[targetIndex];
                    BindposeTrs[] targetBindposes = targetCaches[targetIndex].bindposes;
                    if (!IsApplicableTarget(target) || targetBindposes == null || i >= targetBindposes.Length || !targetBindposes[i].valid)
                    {
                        continue;
                    }

                    float fbm = target.weight;
                    BindposeTrs targetTrs = targetBindposes[i];
                    position += (targetTrs.position - blended.position) * fbm;
                    scale += (targetTrs.scale - blended.scale) * fbm;
                    Quaternion deltaR = targetTrs.rotation * Quaternion.Inverse(blended.rotation);
                    Quaternion weightedDeltaR = Quaternion.SlerpUnclamped(Quaternion.identity, deltaR, fbm);
                    rotation = weightedDeltaR * rotation;
                }

                for (int differenceIndex = 0; differenceIndex < pbmDifferenceBoneCaches.Length; differenceIndex++)
                {
                    PbmDifferenceBoneCache difference = pbmDifferenceBoneCaches[differenceIndex];
                    BindposeTrs[] differenceBindposes = difference.bindposes;
                    BindposeTrs[] fbmBindposes = GetTargetBindposes(difference.fbmTargetIndex);
                    BindposeTrs[] pbmBindposes = GetTargetBindposes(difference.pbmTargetIndex);
                    if (differenceBindposes == null || fbmBindposes == null || pbmBindposes == null
                        || i >= differenceBindposes.Length || i >= fbmBindposes.Length || i >= pbmBindposes.Length
                        || !differenceBindposes[i].valid || !fbmBindposes[i].valid || !pbmBindposes[i].valid)
                    {
                        continue;
                    }

                    float product = GetEffectiveTargetWeight(difference.fbmTargetIndex) * GetEffectiveTargetWeight(difference.pbmTargetIndex);
                    BindposeTrs q = differenceBindposes[i];
                    BindposeTrs f = fbmBindposes[i];
                    BindposeTrs p = pbmBindposes[i];
                    position += (q.position - blended.position - (f.position - blended.position) - (p.position - blended.position)) * product;
                    scale += (q.scale - blended.scale - (f.scale - blended.scale) - (p.scale - blended.scale)) * product;
                    Quaternion fullDirect = ComposePairRotation(blended.rotation, f.rotation, p.rotation, difference.fbmTargetIndex, difference.pbmTargetIndex);
                    Quaternion correction = q.rotation * Quaternion.Inverse(fullDirect);
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, correction, product) * rotation;
                }

                blended.position = position;
                blended.rotation = NormalizeQuaternion(rotation);
                blended.scale = scale;
                blendedBindposeTrs[i] = blended;
                blendedBindposes[i] = Matrix4x4.TRS(blended.position, blended.rotation, blended.scale);
            }

            runtimeMesh.bindposes = blendedBindposes;
        }

        private BindposeTrs[] GetTargetBindposes(int targetIndex)
        {
            return targetCaches != null && targetIndex >= 0 && targetIndex < targetCaches.Length ? targetCaches[targetIndex].bindposes : null;
        }

        private static BindposeTrs DecomposeMatrix(Matrix4x4 matrix)
        {
            BindposeTrs result = new BindposeTrs
            {
                position = new Vector3(matrix.m03, matrix.m13, matrix.m23),
                valid = true
            };

            Vector3 right = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);

            result.scale = new Vector3(right.magnitude, up.magnitude, forward.magnitude);
            if (result.scale.x > Mathf.Epsilon)
            {
                right /= result.scale.x;
            }

            if (result.scale.y > Mathf.Epsilon)
            {
                up /= result.scale.y;
            }

            if (result.scale.z > Mathf.Epsilon)
            {
                forward /= result.scale.z;
            }

            if (forward.sqrMagnitude <= Mathf.Epsilon || up.sqrMagnitude <= Mathf.Epsilon)
            {
                result.rotation = Quaternion.identity;
                result.valid = false;
                return result;
            }

            result.rotation = Quaternion.LookRotation(forward, up);
            return result;
        }

        private void UpdateHumanoidAvatar()
        {
            if (targetAnimator == null || baseAvatar == null || blendedSkeleton == null || blendedSkeleton.Length == 0 || targets == null || targetCaches == null)
            {
                return;
            }

            SkeletonBone[] baseSkeleton = baseHumanDescription.skeleton;
            if (baseSkeleton == null)
            {
                return;
            }

            for (int i = blendedSkeleton.Length - 1; i >= 0; i--)
            {
                SkeletonBone baseBone = baseSkeleton[i];
                Vector3 position = baseBone.position;
                Vector3 scale = baseBone.scale;
                Quaternion rotation = baseBone.rotation;

                int targetCount = Mathf.Min(targets.Count, targetCaches.Length);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    DynamicBoneBlendTarget target = targets[targetIndex];
                    TargetRuntimeCache cache = targetCaches[targetIndex];
                    if (!IsApplicableTarget(target) || cache.skeletonIndices == null || i >= cache.skeletonIndices.Length)
                    {
                        continue;
                    }

                    int skeletonIndex = cache.skeletonIndices[i];
                    SkeletonBone[] targetSkeleton = cache.humanDescription.skeleton;
                    if (skeletonIndex < 0 || targetSkeleton == null || skeletonIndex >= targetSkeleton.Length)
                    {
                        continue;
                    }

                    float fbm = target.weight;
                    SkeletonBone targetBone = targetSkeleton[skeletonIndex];
                    position += (targetBone.position - baseBone.position) * fbm;
                    scale += (targetBone.scale - baseBone.scale) * fbm;
                    Quaternion deltaR = targetBone.rotation * Quaternion.Inverse(baseBone.rotation);
                    Quaternion weightedDeltaR = Quaternion.SlerpUnclamped(Quaternion.identity, deltaR, fbm);
                    rotation = weightedDeltaR * rotation;
                }

                for (int differenceIndex = 0; differenceIndex < pbmDifferenceBoneCaches.Length; differenceIndex++)
                {
                    PbmDifferenceBoneCache difference = pbmDifferenceBoneCaches[differenceIndex];
                    if (difference.skeletonIndices == null || i >= difference.skeletonIndices.Length)
                    {
                        continue;
                    }

                    int qIndex = difference.skeletonIndices[i];
                    int fIndex = GetSkeletonIndex(difference.fbmTargetIndex, i);
                    int pIndex = GetSkeletonIndex(difference.pbmTargetIndex, i);
                    SkeletonBone[] qSkeleton = difference.humanDescription.skeleton;
                    SkeletonBone[] fSkeleton = GetTargetSkeleton(difference.fbmTargetIndex);
                    SkeletonBone[] pSkeleton = GetTargetSkeleton(difference.pbmTargetIndex);
                    if (qSkeleton == null || fSkeleton == null || pSkeleton == null
                        || qIndex < 0 || fIndex < 0 || pIndex < 0
                        || qIndex >= qSkeleton.Length || fIndex >= fSkeleton.Length || pIndex >= pSkeleton.Length)
                    {
                        continue;
                    }

                    float product = GetEffectiveTargetWeight(difference.fbmTargetIndex) * GetEffectiveTargetWeight(difference.pbmTargetIndex);
                    SkeletonBone q = qSkeleton[qIndex];
                    SkeletonBone f = fSkeleton[fIndex];
                    SkeletonBone p = pSkeleton[pIndex];
                    position += (q.position - baseBone.position - (f.position - baseBone.position) - (p.position - baseBone.position)) * product;
                    scale += (q.scale - baseBone.scale - (f.scale - baseBone.scale) - (p.scale - baseBone.scale)) * product;
                    Quaternion fullDirect = ComposePairRotation(baseBone.rotation, f.rotation, p.rotation, difference.fbmTargetIndex, difference.pbmTargetIndex);
                    Quaternion correction = q.rotation * Quaternion.Inverse(fullDirect);
                    rotation = Quaternion.SlerpUnclamped(Quaternion.identity, correction, product) * rotation;
                }

                blendedSkeleton[i].name = baseBone.name;
                blendedSkeleton[i].position = position;
                blendedSkeleton[i].rotation = NormalizeQuaternion(rotation);
                blendedSkeleton[i].scale = scale;
            }

            for (int i = 0; i < humanoidCorrectionBindings.Length; i++)
            {
                HumanoidCorrectionBinding correction = humanoidCorrectionBindings[i];
                if (correction.skeletonIndex < 0 || correction.skeletonIndex >= blendedSkeleton.Length)
                {
                    continue;
                }

                SkeletonBone bone = blendedSkeleton[correction.skeletonIndex];
                bone.position += correction.basePositionDelta;
                bone.rotation = NormalizeQuaternion(correction.baseRotationDelta * bone.rotation);
                bone.scale += correction.baseScaleDelta;
                int targetCount = Mathf.Min(targets.Count, correction.targetPositionDeltas.Length);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    DynamicBoneBlendTarget target = targets[targetIndex];
                    if (!IsApplicableTarget(target)) continue;
                    float weight = target.weight;
                    bone.position += correction.targetPositionDeltas[targetIndex] * weight;
                    bone.scale += correction.targetScaleDeltas[targetIndex] * weight;
                    Quaternion weightedRotation = Quaternion.SlerpUnclamped(Quaternion.identity, correction.targetRotationDeltas[targetIndex], weight);
                    bone.rotation = NormalizeQuaternion(weightedRotation * bone.rotation);
                }
                blendedSkeleton[correction.skeletonIndex] = bone;
            }

            HumanDescription blendedDescription = baseHumanDescription;
            blendedDescription.skeleton = blendedSkeleton;

            // Attached Outfit roots intentionally remain below the Figure for their own runtime
            // components.  AvatarBuilder resolves human bones by hierarchy name, so temporarily
            // exclude those duplicate authoring skeletons while rebuilding the Figure Avatar.
            var temporarilyDetachedOutfitRoots = new List<Transform>();
            DetachAvatarBuilderExcludedRoots(temporarilyDetachedOutfitRoots);

            Avatar newAvatar;
            try
            {
                newAvatar = AvatarBuilder.BuildHumanAvatar(gameObject, blendedDescription);
            }
            finally
            {
                for (int i = 0; i < temporarilyDetachedOutfitRoots.Count; i++)
                {
                    Transform outfitRoot = temporarilyDetachedOutfitRoots[i];
                    if (outfitRoot != null) outfitRoot.SetParent(transform, true);
                }
            }
            if (newAvatar == null || !newAvatar.isValid || !newAvatar.isHuman)
            {
                if (newAvatar != null)
                {
                    Destroy(newAvatar);
                }
                lastAnimatorDiagnostic = StackMachineDiagnostic.CreateDomain(
                    "humanoid",
                    "AvatarBuildFailed",
                    "AvatarBuilder could not build a valid Human Avatar from the Figure skeleton hierarchy.",
                    detail: gameObject.name);
                return;
            }

            newAvatar.name = "ShapeSyncDynamicAvatar";
            AnimatorParameterSnapshot[] parameterSnapshots = CaptureAnimatorParameters(targetAnimator);
            AnimatorStateSnapshot stateSnapshot = CaptureAnimatorState(targetAnimator);
            Avatar oldAvatar = dynamicAvatar;
            dynamicAvatar = newAvatar;
            targetAnimator.avatar = dynamicAvatar;
            targetAnimator.Rebind();
            RestoreAnimatorParameters(targetAnimator, parameterSnapshots);
            RestoreAnimatorState(targetAnimator, stateSnapshot);
            targetAnimator.Update(0f);

            if (oldAvatar != null)
            {
                Destroy(oldAvatar);
            }
        }

        // Kept separate for whitebox coverage of the AvatarBuilder hierarchy boundary.
        private void DetachAvatarBuilderExcludedRoots(List<Transform> destination)
        {
            if (destination == null) return;
            ShapeSyncOutfit[] hierarchyOutfits = GetComponentsInChildren<ShapeSyncOutfit>(true);
            for (int i = 0; i < hierarchyOutfits.Length; i++)
            {
                Transform outfitRoot = hierarchyOutfits[i] == null ? null : hierarchyOutfits[i].transform;
                // Start-order can invoke Avatar rebuilding before OutfitAttacher records the
                // attachment. Discover direct Outfit roots from the hierarchy, not the registry.
                if (outfitRoot == null || outfitRoot.parent != transform) continue;
                outfitRoot.SetParent(null, true);
                destination.Add(outfitRoot);
            }
            // Spec19 Hybrid keeps its warm baked Figure as an inactive direct child. It is not
            // an Outfit, but AvatarBuilder includes inactive duplicate humanoid transforms.
            StackMachine.Humanoid.HybridHotBakeFigure[] hybrids = GetComponentsInChildren<StackMachine.Humanoid.HybridHotBakeFigure>(true);
            for (int i = 0; i < hybrids.Length; i++)
            {
                Transform bakedRoot = hybrids[i] == null || hybrids[i].BakedRoot == null ? null : hybrids[i].BakedRoot.transform;
                if (bakedRoot == null || bakedRoot.parent != transform || destination.Contains(bakedRoot)) continue;
                bakedRoot.SetParent(null, true);
                destination.Add(bakedRoot);
            }
        }

        private int GetSkeletonIndex(int targetIndex, int baseSkeletonIndex)
        {
            if (targetCaches == null || targetIndex < 0 || targetIndex >= targetCaches.Length)
            {
                return -1;
            }

            int[] indices = targetCaches[targetIndex].skeletonIndices;
            return indices != null && baseSkeletonIndex >= 0 && baseSkeletonIndex < indices.Length ? indices[baseSkeletonIndex] : -1;
        }

        private SkeletonBone[] GetTargetSkeleton(int targetIndex)
        {
            return targetCaches != null && targetIndex >= 0 && targetIndex < targetCaches.Length
                ? targetCaches[targetIndex].humanDescription.skeleton
                : null;
        }

        private AnimatorStateSnapshot CaptureAnimatorState(Animator animator)
        {
            AnimatorStateSnapshot snapshot = new AnimatorStateSnapshot
            {
                speed = animator != null ? animator.speed : 1f,
                layers = Array.Empty<AnimatorLayerSnapshot>()
            };

            if (!preserveAnimatorStateOnRebind || animator == null || !animator.isActiveAndEnabled || !animator.runtimeAnimatorController)
            {
                return snapshot;
            }

            int layerCount = animator.layerCount;
            AnimatorLayerSnapshot[] layers = new AnimatorLayerSnapshot[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(i);
                layers[i] = new AnimatorLayerSnapshot
                {
                    layerIndex = i,
                    stateHash = stateInfo.fullPathHash,
                    normalizedTime = stateInfo.normalizedTime,
                    layerWeight = animator.GetLayerWeight(i)
                };
            }

            snapshot.layers = layers;
            return snapshot;
        }

        private void RestoreAnimatorState(Animator animator, AnimatorStateSnapshot snapshot)
        {
            if (animator == null)
            {
                return;
            }

            animator.speed = snapshot.speed;
            if (!preserveAnimatorStateOnRebind || snapshot.layers == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.layers.Length; i++)
            {
                AnimatorLayerSnapshot layer = snapshot.layers[i];
                if (layer.layerIndex < 0 || layer.layerIndex >= animator.layerCount || layer.stateHash == 0)
                {
                    continue;
                }

                animator.SetLayerWeight(layer.layerIndex, layer.layerWeight);
                animator.Play(layer.stateHash, layer.layerIndex, layer.normalizedTime);
            }
        }

        private AnimatorParameterSnapshot[] CaptureAnimatorParameters(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return Array.Empty<AnimatorParameterSnapshot>();
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            if (parameters == null || parameters.Length == 0)
            {
                return Array.Empty<AnimatorParameterSnapshot>();
            }

            List<AnimatorParameterSnapshot> snapshots = new List<AnimatorParameterSnapshot>(parameters.Length);
            foreach (AnimatorControllerParameter parameter in parameters)
            {
                AnimatorParameterSnapshot snapshot = new AnimatorParameterSnapshot
                {
                    nameHash = parameter.nameHash,
                    type = parameter.type
                };

                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float:
                        snapshot.floatValue = animator.GetFloat(parameter.nameHash);
                        snapshots.Add(snapshot);
                        break;
                    case AnimatorControllerParameterType.Int:
                        snapshot.intValue = animator.GetInteger(parameter.nameHash);
                        snapshots.Add(snapshot);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        snapshot.boolValue = animator.GetBool(parameter.nameHash);
                        snapshots.Add(snapshot);
                        break;
                }
            }

            return snapshots.ToArray();
        }

        private void RestoreAnimatorParameters(Animator animator, AnimatorParameterSnapshot[] snapshots)
        {
            if (animator == null || snapshots == null)
            {
                return;
            }

            for (int i = 0; i < snapshots.Length; i++)
            {
                AnimatorParameterSnapshot snapshot = snapshots[i];
                switch (snapshot.type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(snapshot.nameHash, snapshot.floatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(snapshot.nameHash, snapshot.intValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(snapshot.nameHash, snapshot.boolValue);
                        break;
                }
            }
        }

    private void ApplyBoneTransforms()
        {
            ApplyBoneBindingTransforms(boneBindings, false);
            ApplyExtraBoneTransforms();
        }

        private static Quaternion NormalizeQuaternion(Quaternion value)
        {
            float length = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (length <= Mathf.Epsilon)
            {
                return Quaternion.identity;
            }

            float inverse = 1f / length;
            return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
        }

        private void OnDestroy()
        {
            if (dynamicAvatar != null)
            {
                Destroy(dynamicAvatar);
            }

            if (runtimeMesh != null && dynamicMorphAdapter == null)
            {
                Destroy(runtimeMesh);
            }

            fbmWeightSubject.OnCompleted();
            fbmWeightSubject.Dispose();
            blendWeightSubject.OnCompleted();
            blendWeightSubject.Dispose();
        }
    }
}
