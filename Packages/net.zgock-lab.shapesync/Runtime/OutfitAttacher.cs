// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using R3;
using Unity.Collections;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>One side-effect-free Outfit operation submitted to <see cref="OutfitAttacher.TryDryRun"/>.</summary>
    public readonly struct OutfitAttacherDryRunCommand
    {
        /// <summary>Gets whether this command attaches an Outfit; otherwise it detaches by registry ID.</summary>
        public readonly bool Attach;
        /// <summary>Gets the Outfit source for an attach command; <see langword="null"/> for a detach command.</summary>
        public readonly ShapeSyncOutfit Outfit;
        /// <summary>Gets the target Outfit registry ID.</summary>
        public readonly string RegistryId;
        /// <summary>Gets the originating logical recipe binding, when available.</summary>
        public readonly string LogicalBinding;

        private OutfitAttacherDryRunCommand(bool attach, ShapeSyncOutfit outfit, string registryId, string logicalBinding)
        {
            Attach = attach;
            Outfit = outfit;
            RegistryId = registryId;
            LogicalBinding = logicalBinding;
        }

        /// <summary>Creates a dry-run attach command.</summary>
        /// <param name="outfit">The resolved Outfit source.</param><param name="logicalBinding">The originating logical recipe binding.</param><returns>A command for projected attachment validation.</returns>
        public static OutfitAttacherDryRunCommand ForAttach(ShapeSyncOutfit outfit, string logicalBinding)
            => new OutfitAttacherDryRunCommand(true, outfit, outfit == null ? null : outfit.RegistryId, logicalBinding);
        /// <summary>Creates a dry-run detach command.</summary>
        /// <param name="registryId">The registry ID to detach.</param><param name="logicalBinding">The originating logical recipe binding.</param><returns>A command for projected detachment validation.</returns>
        public static OutfitAttacherDryRunCommand ForDetach(string registryId, string logicalBinding)
            => new OutfitAttacherDryRunCommand(false, null, registryId, logicalBinding);
    }

    /// <summary>Structured outcome of a side-effect-free Outfit operation dry-run.</summary>
    public readonly struct OutfitAttacherDryRunResult
    {
        /// <summary>Gets whether the projected command sequence is valid.</summary>
        public readonly bool Succeeded;
        /// <summary>Gets the failing command index, or <c>-1</c> on success.</summary>
        public readonly int CommandIndex;
        /// <summary>Gets whether the failing command was an attach command.</summary>
        public readonly bool Attach;
        /// <summary>Gets the failing command's registry ID.</summary>
        public readonly string RegistryId;
        /// <summary>Gets the failing command's logical recipe binding.</summary>
        public readonly string LogicalBinding;
        /// <summary>Gets the stable dry-run failure code.</summary>
        public readonly string Code;
        /// <summary>Gets the detailed existing-component validation reason.</summary>
        public readonly string Detail;

        private OutfitAttacherDryRunResult(bool succeeded, int commandIndex, bool attach, string registryId, string logicalBinding, string code, string detail)
        {
            Succeeded = succeeded; CommandIndex = commandIndex; Attach = attach; RegistryId = registryId; LogicalBinding = logicalBinding; Code = code; Detail = detail;
        }

        /// <summary>Creates a successful dry-run result.</summary>
        /// <returns>A result with no failing command.</returns>
        public static OutfitAttacherDryRunResult Success() => new OutfitAttacherDryRunResult(true, -1, false, null, null, null, null);
        /// <summary>Creates a structured dry-run failure result.</summary>
        /// <param name="commandIndex">The zero-based index of the rejected command.</param><param name="command">The rejected command.</param><param name="code">The stable failure code.</param><param name="detail">The detailed validation reason.</param><returns>A failed result.</returns>
        public static OutfitAttacherDryRunResult Failure(int commandIndex, OutfitAttacherDryRunCommand command, string code, string detail)
            => new OutfitAttacherDryRunResult(false, commandIndex, command.Attach, command.RegistryId, command.LogicalBinding, code, detail);
    }

    /// <summary>
    /// Figure-side runtime component that attaches, detaches, and keeps Outfit renderers synchronized with FBM changes.
    /// </summary>
    public sealed class OutfitAttacher : MonoBehaviour
    {
        /// <summary>Raised after a successful runtime Outfit topology change.</summary>
        public event Action TopologyChanged;
        [SerializeField] private DynamicBoneBlender dynamicBoneBlender;
        [SerializeField] private Animator figureAnimator;
        [SerializeField] private FigureMorphSyncCoordinator figureMorphSyncCoordinator;

        private readonly List<AttachedOutfitRegistrySet> attachedOutfits = new List<AttachedOutfitRegistrySet>();
        private IDisposable fbmWeightSubscription;
        private IDisposable indexedFbmWeightSubscription;
        [NonSerialized] private StackMachine.StackMachineDiagnostic lastAnimatorDiagnostic;

        public IReadOnlyList<AttachedOutfitRegistrySet> AttachedOutfits => attachedOutfits;
        public DynamicBoneBlender DynamicBoneBlender => dynamicBoneBlender;
        /// <summary>Gets the latest automatic Figure Animator resolution diagnostic, if any.</summary>
        public StackMachine.StackMachineDiagnostic LastAnimatorDiagnostic => lastAnimatorDiagnostic;

        /// <summary>Resolves one currently attached runtime Outfit by its physical RegistryId.</summary>
        /// <param name="registryId">The attached Outfit RegistryId.</param>
        /// <param name="outfit">The unique active runtime Outfit on success.</param>
        /// <param name="diagnostic">A structured missing, duplicate, or unavailable-target diagnostic.</param>
        /// <returns><see langword="true"/> when exactly one active runtime Outfit is available.</returns>
        public bool TryGetAttachedOutfit(string registryId, out ShapeSyncOutfit outfit, out StackMachine.StackMachineDiagnostic diagnostic)
        {
            outfit = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(registryId))
            {
                diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitRegistryIdRequired", "OUTFIT requires a non-empty attached Outfit RegistryId.");
                return false;
            }
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                AttachedOutfitRegistrySet attached = attachedOutfits[i];
                if (attached == null || attached.RegistryId != registryId) continue;
                if (outfit != null)
                {
                    diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitRegistryAmbiguous", "More than one attached Outfit has the requested RegistryId.", detail: registryId);
                    outfit = null;
                    return false;
                }
                GameObject runtimeRoot = attached.RuntimeOutfitInstance;
                if (runtimeRoot == null)
                {
                    diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitUnavailable", "The attached Outfit is being destroyed or is unavailable.", detail: registryId);
                    return false;
                }
                outfit = runtimeRoot.GetComponent<ShapeSyncOutfit>();
                if (outfit == null || !outfit.isActiveAndEnabled)
                {
                    diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitUnavailable", "The attached Outfit is disabled or inactive.", detail: registryId);
                    outfit = null;
                    return false;
                }
            }
            if (outfit != null) return true;
            diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitRegistryMissing", "No attached Outfit has the requested RegistryId.", detail: registryId);
            return false;
        }

        /// <summary>Resolves one attached Outfit's runtime Material StackMachine by physical RegistryId.</summary>
        /// <param name="registryId">The attached Outfit RegistryId.</param>
        /// <param name="machine">The active Material Machine on the retained runtime Outfit root.</param>
        /// <param name="diagnostic">A structured missing, duplicate, or unavailable-route diagnostic.</param>
        /// <returns><see langword="true"/> when exactly one active runtime Material route is available.</returns>
        public bool TryGetAttachedMaterialStackMachine(string registryId, out StackMachine.MaterialStackMachine machine, out StackMachine.StackMachineDiagnostic diagnostic)
        {
            machine = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(registryId))
            {
                diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitRegistryIdRequired", "OUTFIT requires a non-empty attached Outfit RegistryId.");
                return false;
            }
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                AttachedOutfitRegistrySet attached = attachedOutfits[i];
                if (attached == null || attached.RegistryId != registryId) continue;
                if (machine != null)
                {
                    diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitRegistryAmbiguous", "More than one attached Outfit has the requested RegistryId.", detail: registryId);
                    machine = null;
                    return false;
                }
                GameObject runtimeRoot = attached.RuntimeOutfitInstance;
                machine = runtimeRoot == null ? null : runtimeRoot.GetComponent<StackMachine.MaterialStackMachine>();
                if (machine == null || !machine.isActiveAndEnabled)
                {
                    diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitMaterialMachineUnavailable", "The attached Outfit has no active runtime MaterialStackMachine.", detail: registryId);
                    machine = null;
                    return false;
                }
            }
            if (machine != null) return true;
            diagnostic = StackMachine.StackMachineDiagnostic.CreateDomain("material", "OutfitRegistryMissing", "No attached Outfit has the requested RegistryId.", detail: registryId);
            return false;
        }

        /// <summary>
        /// Configures the required Figure Builder references on a newly created Figure prefab.
        /// </summary>
        public void ConfigureForFigure(DynamicBoneBlender blender, Animator animator)
        {
            dynamicBoneBlender = blender;
            figureAnimator = animator;
            lastAnimatorDiagnostic = null;
            figureMorphSyncCoordinator = GetComponent<FigureMorphSyncCoordinator>();
        }

        private sealed class AttachPlan
        {
            public readonly List<AttachRootPlan> roots = new List<AttachRootPlan>();
        }

        private struct AttachRootPlan
        {
            public string rootPath;
            public Transform anchor;
        }

        private sealed class RendererPlan
        {
            public SkinnedMeshRenderer renderer;
            public string rendererPath;
            public string[] bonePaths;
            public string rootBonePath;
            public Transform sourceRootBone;
            public OutfitSkinningRendererProfile profile;
        }

        private void Reset()
        {
            dynamicBoneBlender = GetComponent<DynamicBoneBlender>();
            ShapeSyncAnimatorResolver.TryResolve(transform, out figureAnimator, out lastAnimatorDiagnostic);
        }

        private void Awake()
        {
            if (dynamicBoneBlender == null)
            {
                dynamicBoneBlender = GetComponent<DynamicBoneBlender>();
            }

            if (figureAnimator == null)
            {
                ShapeSyncAnimatorResolver.TryResolve(transform, out figureAnimator, out lastAnimatorDiagnostic);
            }

            if (figureMorphSyncCoordinator == null)
            {
                figureMorphSyncCoordinator = GetComponent<FigureMorphSyncCoordinator>();
            }
        }

        private void OnEnable()
        {
            SubscribeToFbmWeights();
        }

        private void OnDisable()
        {
            DisposeFbmSubscription();
        }

        private void OnDestroy()
        {
            DisposeFbmSubscription();
        }

        public bool TryAttach(ShapeSyncOutfit outfitPrefab)
        {
            if (figureMorphSyncCoordinator == null)
            {
                figureMorphSyncCoordinator = GetComponent<FigureMorphSyncCoordinator>();
            }
            if (!TryBuildAttachPlan(outfitPrefab, out AttachPlan plan, out string error))
            {
                Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error}", this);
                return false;
            }
            if (!outfitPrefab.TryValidateProfileControlledMorphConfiguration(out error))
            {
                Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error}", this);
                return false;
            }
            if (!TryValidateProfileControlledMorphAttach(outfitPrefab, out error)
                || !TryValidateOptionalVrmAttach(outfitPrefab, out error))
            {
                Debug.LogWarning($"OutfitAttacher rejected outfit attach preflight: {error}", this);
                return false;
            }

            GameObject runtimeInstance = Instantiate(outfitPrefab.gameObject);
            runtimeInstance.name = $"{outfitPrefab.name} (ShapeSync Runtime)";
            ShapeSyncOutfit runtimeOutfit = runtimeInstance.GetComponent<ShapeSyncOutfit>();
            if (runtimeOutfit == null || !TryBuildRendererPlans(runtimeOutfit, out List<RendererPlan> rendererPlans, out error))
            {
                DestroyForLifecycle(runtimeInstance);
                Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error ?? "runtime instance has no ShapeSyncOutfit."}", this);
                return false;
            }
            bool usesStaticBcpBindposes = UsesStaticBcpBindposes(runtimeOutfit);
            List<Transform> attachedRoots = new List<Transform>();
            List<string> attachedRootPaths = new List<string>();
            List<Transform> physicsSourceRoots = new List<Transform>();
            List<OutfitSkinnedMeshBinding> bindings = new List<OutfitSkinnedMeshBinding>(rendererPlans.Count);
            List<Transform> rendererRoots = new List<Transform>(rendererPlans.Count);
            IShapeSyncOptionalVrmAttachment springBoneAttachment = null;
            ProfileControlledMorphBinding profileControlledMorphBinding = null;
            var figureTransformByOutfitTransform = new Dictionary<Transform, Transform>();

            if (runtimeOutfit.ProfileControlledMorphAsset != null && (figureMorphSyncCoordinator == null || !figureMorphSyncCoordinator.IsReady(out error)))
            {
                DestroyForLifecycle(runtimeInstance);
                Debug.LogWarning($"OutfitAttacher rejected Plugable PCM attach: {error ?? "Figure Morph Sync Coordinator is required."}", this);
                return false;
            }
            if (runtimeOutfit.ProfileControlledMorphAsset != null && !ProfileControlledMorphBinding.TryCreate(dynamicBoneBlender.DynamicMorphAdapter, runtimeOutfit.ProfileControlledMorphAsset, runtimeOutfit.GetInstanceID(), out profileControlledMorphBinding, out error))
            {
                DestroyForLifecycle(runtimeInstance);
                Debug.LogWarning($"OutfitAttacher rejected Plugable PCM attach: {error}", this);
                return false;
            }
            if (runtimeOutfit.ProfileControlledMorphAsset == null && runtimeOutfit.ProfileControlledMorphEnabled && !ProfileControlledMorphBinding.TryCreate(dynamicBoneBlender.TargetSkinnedMeshRenderer, runtimeOutfit.ProfileControlledMorphOutfitName, dynamicBoneBlender.Targets, out profileControlledMorphBinding, out error))
            {
                DestroyForLifecycle(runtimeInstance);
                Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error}", this);
                return false;
            }

            // Keep the cloned Outfit root detached while optional VRM transfer resolves its
            // source Vrm10Instance.  Parenting it first would make the Figure hierarchy expose
            // both the Figure and Outfit Vrm10Instance components to that transfer.  Only
            // registered Extra Bone subtrees are transplanted into the Figure hierarchy here.

            for (int i = 0; i < plan.roots.Count; i++)
            {
                AttachRootPlan rootPlan = plan.roots[i];
                Transform runtimeExtraRoot = runtimeInstance.transform.Find(rootPlan.rootPath);
                if (runtimeExtraRoot == null)
                {
                    RollbackAttach(runtimeInstance, attachedRoots, bindings, rendererRoots, springBoneAttachment);
                    profileControlledMorphBinding?.Dispose();
                    Debug.LogWarning($"OutfitAttacher rejected outfit attach because Extra Bone root '{rootPlan.rootPath}' was not found.", this);
                    return false;
                }

                if (!TryCloneExtraBoneSubtree(runtimeExtraRoot, rootPlan.anchor, figureTransformByOutfitTransform, out Transform figureExtraRoot, out error))
                {
                    RollbackAttach(runtimeInstance, attachedRoots, bindings, rendererRoots, springBoneAttachment);
                    profileControlledMorphBinding?.Dispose();
                    Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error}", this);
                    return false;
                }
                attachedRoots.Add(figureExtraRoot);
                attachedRootPaths.Add(rootPlan.rootPath);
                physicsSourceRoots.Add(runtimeExtraRoot);
            }

            if (!TryCreateRendererBindings(rendererPlans, bindings, rendererRoots, out error))
            {
                RollbackAttach(runtimeInstance, attachedRoots, bindings, rendererRoots, springBoneAttachment);
                profileControlledMorphBinding?.Dispose();
                Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error}", this);
                return false;
            }
            if (ShapeSyncOptionalVrmIntegrationRegistry.TryGet(gameObject, out IShapeSyncOptionalVrmIntegration optionalIntegration))
            {
                Func<Transform, Transform> mapper = sourceTransform => MapOutfitTransform(runtimeInstance.transform, figureTransformByOutfitTransform, sourceTransform);
                ShapeSyncOptionalVrmAttachRequest request = new ShapeSyncOptionalVrmAttachRequest(gameObject, figureAnimator, runtimeInstance, mapper);
                if (!optionalIntegration.TryAttachOutfitPhysics(request, out springBoneAttachment, out error))
                {
                    RollbackAttach(runtimeInstance, attachedRoots, bindings, rendererRoots, springBoneAttachment);
                    profileControlledMorphBinding?.Dispose();
                    Debug.LogWarning($"OutfitAttacher rejected outfit Spring Bone attach: {error}", this);
                    return false;
                }

                // A null attachment means the Outfit has no VRM physics payload.
                // Ordinary ShapeSync Outfits remain completely VRM-agnostic.
                springBoneAttachment?.ReconstructOnce();
            }

            // The copied Figure subtree and the optional VRM attachment now own
            // every transferred Extra Bone and Spring entry. Remove the clone's
            // source skeleton roots and the integration-declared direct Collider
            // Group roots before parenting the retained Material route below the
            // Figure. This prevents AvatarBuilder from seeing a duplicate humanoid
            // transform during the next Attach/Detach reconstruction.
            ReleaseRuntimeSourceRoots(runtimeInstance.transform, rendererPlans, physicsSourceRoots, springBoneAttachment);

            // The Outfit root is the retained runtime ownership boundary for its Material route.
            // Parent it only after optional VRM transfer has consumed the detached source tree.
            runtimeInstance.transform.SetParent(transform, false);
            runtimeInstance.transform.localPosition = Vector3.zero;
            runtimeInstance.transform.localRotation = Quaternion.identity;
            runtimeInstance.transform.localScale = Vector3.one;

            ConfigureOutfitNormalBlenders(runtimeInstance.transform);

            if (!usesStaticBcpBindposes && !TryAlignBindingsToFigureSkinning(bindings, out error))
            {
                RollbackAttach(runtimeInstance, attachedRoots, bindings, rendererRoots, springBoneAttachment);
                profileControlledMorphBinding?.Dispose();
                Debug.LogWarning($"OutfitAttacher rejected outfit attach: {error}", this);
                return false;
            }
            if (profileControlledMorphBinding != null && !profileControlledMorphBinding.Commit(out error))
            {
                RollbackAttach(runtimeInstance, attachedRoots, bindings, rendererRoots, springBoneAttachment);
                profileControlledMorphBinding.Dispose();
                Debug.LogWarning($"OutfitAttacher rejected Plugable PCM attach: {error}", this);
                return false;
            }
            if (profileControlledMorphBinding != null) figureMorphSyncCoordinator.SynchronizeAfterPcmMeshReplacement(dynamicBoneBlender.TargetSkinnedMeshRenderer.sharedMesh);

            AttachedOutfitRegistrySet attachedOutfit = new AttachedOutfitRegistrySet(
                runtimeOutfit,
                runtimeInstance,
                attachedRoots,
                attachedRootPaths,
                bindings,
                rendererRoots,
                springBoneAttachment,
                runtimeOutfit.HumanoidBoneCorrectionProfile,
                profileControlledMorphBinding);
                attachedOutfits.Add(attachedOutfit);
                PushAttachedOutfitsToBlender();
            ApplyCurrentFbmState(bindings);
            profileControlledMorphBinding?.ApplyBase();
            ApplyCurrentFbmState(profileControlledMorphBinding);
            ConfigureAndApplyCurrentTargetState(bindings);
            if (!usesStaticBcpBindposes)
            {
                TryAlignBindingsToFigureSkinning(bindings, out _);
            }
            NotifyTopologyChanged();
            return true;
        }

        /// <summary>
        /// Validates the ordered attach/detach projection without instantiating, destroying, parenting,
        /// binding renderers, changing PCM/VRM state, or mutating the attached Outfit registry.
        /// </summary>
        /// <summary>Validates an ordered projected Outfit command list without changing Figure or Outfit state.</summary>
        /// <param name="commands">The attach and detach commands to project from the current attachment state.</param><param name="result">The structured success or failure result.</param><returns><see langword="true"/> when every projected command is valid.</returns>
        public bool TryDryRun(IReadOnlyList<OutfitAttacherDryRunCommand> commands, out OutfitAttacherDryRunResult result)
        {
            result = OutfitAttacherDryRunResult.Success();
            if (commands == null)
            {
                result = OutfitAttacherDryRunResult.Failure(-1, default, "InvalidCommandList", "Command list is null.");
                return false;
            }

            var projectedRegistryIds = new HashSet<string>(StringComparer.Ordinal);
            var projectedRootsByRegistry = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var projectedRootPaths = new HashSet<string>(StringComparer.Ordinal);
            var detachedRootPaths = new HashSet<string>(StringComparer.Ordinal);
            var projectedAttachedOutfits = new List<AttachedOutfitRegistrySet>();
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                AttachedOutfitRegistrySet attached = attachedOutfits[i];
                if (attached == null || string.IsNullOrEmpty(attached.RegistryId)) continue;
                projectedRegistryIds.Add(attached.RegistryId);
                projectedAttachedOutfits.Add(attached);
                var roots = new List<string>();
                IReadOnlyList<string> attachedRoots = attached.ExtraRootPaths;
                for (int rootIndex = 0; attachedRoots != null && rootIndex < attachedRoots.Count; rootIndex++)
                {
                    string root = attachedRoots[rootIndex];
                    if (!string.IsNullOrEmpty(root)) { roots.Add(root); projectedRootPaths.Add(root); }
                }
                projectedRootsByRegistry[attached.RegistryId] = roots;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                OutfitAttacherDryRunCommand command = commands[i];
                if (command.Attach)
                {
                    if (command.Outfit == null)
                    {
                        result = OutfitAttacherDryRunResult.Failure(i, command, "InvalidOutfit", "Outfit Prefab is null.");
                        return false;
                    }
                    if (string.IsNullOrEmpty(command.RegistryId) || projectedRegistryIds.Contains(command.RegistryId))
                    {
                        result = OutfitAttacherDryRunResult.Failure(i, command, "RegistryConflict", "registryId is empty or already attached in the projected state.");
                        return false;
                    }
                    if (!TryBuildAttachPlan(command.Outfit, projectedRegistryIds, projectedRootPaths, detachedRootPaths, projectedAttachedOutfits, out AttachPlan plan, out string error)
                        || !TryBuildRendererPlans(command.Outfit, out _, out error)
                        || !command.Outfit.TryValidateProfileControlledMorphConfiguration(out error)
                        || !TryValidateProfileControlledMorphAttach(command.Outfit, out error)
                        || !TryValidateOptionalVrmAttach(command.Outfit, out error))
                    {
                        result = OutfitAttacherDryRunResult.Failure(i, command, "AttachPreflightRejected", error ?? "Attach preflight failed.");
                        return false;
                    }
                    var roots = new List<string>();
                    for (int rootIndex = 0; rootIndex < plan.roots.Count; rootIndex++)
                    {
                        string root = plan.roots[rootIndex].rootPath;
                        if (!string.IsNullOrEmpty(root) && !projectedRootPaths.Add(root))
                        {
                            result = OutfitAttacherDryRunResult.Failure(i, command, "ProjectedRootConflict", $"Extra Bone root '{root}' conflicts with an earlier projected Outfit command.");
                            return false;
                        }
                        if (!string.IsNullOrEmpty(root)) roots.Add(root);
                    }
                    projectedRegistryIds.Add(command.RegistryId);
                    projectedRootsByRegistry[command.RegistryId] = roots;
                    // This record is a validation-only projection. It carries no runtime
                    // instance or lifecycle state, but makes subsequent commands use the
                    // same Humanoid Bone Correction validation as a real attach.
                    projectedAttachedOutfits.Add(new AttachedOutfitRegistrySet(
                        command.Outfit,
                        null,
                        new List<Transform>(),
                        roots,
                        new List<OutfitSkinnedMeshBinding>(),
                        new List<Transform>(),
                        null,
                        command.Outfit.HumanoidBoneCorrectionProfile,
                        null));
                }
                else
                {
                    if (string.IsNullOrEmpty(command.RegistryId) || !projectedRegistryIds.Remove(command.RegistryId))
                    {
                        result = OutfitAttacherDryRunResult.Failure(i, command, "RegistryNotAttached", "registryId is not attached in the projected state.");
                        return false;
                    }
                    if (projectedRootsByRegistry.TryGetValue(command.RegistryId, out List<string> roots))
                    {
                        for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++) { projectedRootPaths.Remove(roots[rootIndex]); detachedRootPaths.Add(roots[rootIndex]); }
                        projectedRootsByRegistry.Remove(command.RegistryId);
                    }
                    for (int outfitIndex = projectedAttachedOutfits.Count - 1; outfitIndex >= 0; outfitIndex--)
                    {
                        if (projectedAttachedOutfits[outfitIndex] != null && projectedAttachedOutfits[outfitIndex].RegistryId == command.RegistryId) projectedAttachedOutfits.RemoveAt(outfitIndex);
                    }
                }
            }
            return true;
        }

        internal bool TryValidateProfileControlledMorphAttach(ShapeSyncOutfit outfit, out string error)
        {
            error = null;
            if (outfit.ProfileControlledMorphAsset != null)
            {
                if (figureMorphSyncCoordinator == null) figureMorphSyncCoordinator = GetComponent<FigureMorphSyncCoordinator>();
                if (figureMorphSyncCoordinator == null || !figureMorphSyncCoordinator.IsReady(out error))
                {
                    error = error ?? "Figure Morph Sync Coordinator is required.";
                    return false;
                }
                if (!ProfileControlledMorphBinding.TryCreate(dynamicBoneBlender.DynamicMorphAdapter, outfit.ProfileControlledMorphAsset, outfit.GetInstanceID(), out ProfileControlledMorphBinding binding, out error)) return false;
                binding.Dispose();
                return true;
            }
            if (outfit.ProfileControlledMorphAsset == null && outfit.ProfileControlledMorphEnabled)
            {
                // Legacy binding creation is read-only. Do not Dispose it: legacy Dispose
                // clears Figure blendshape weights and would violate the dry-run contract.
                return ProfileControlledMorphBinding.TryCreate(dynamicBoneBlender.TargetSkinnedMeshRenderer, outfit.ProfileControlledMorphOutfitName, dynamicBoneBlender.Targets, out _, out error);
            }
            return true;
        }

        internal bool TryValidateOptionalVrmAttach(ShapeSyncOutfit outfit, out string error)
        {
            error = null;
            if (!ShapeSyncOptionalVrmIntegrationRegistry.TryGet(gameObject, out IShapeSyncOptionalVrmIntegration integration)) return true;
            if (!(integration is IShapeSyncOptionalVrmIntegrationDryRun dryRun))
            {
                error = "The registered optional VRM integration does not support read-only Outfit dry-run validation.";
                return false;
            }
            return dryRun.TryValidateOutfitPhysics(new ShapeSyncOptionalVrmDryRunRequest(gameObject, figureAnimator, outfit.gameObject), out error);
        }

        public bool Detach(string registryId)
        {
            if (string.IsNullOrEmpty(registryId))
            {
                return false;
            }

            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                AttachedOutfitRegistrySet attachedOutfit = attachedOutfits[i];
                if (attachedOutfit == null || attachedOutfit.RegistryId != registryId)
                {
                    continue;
                }

                DisposeBindings(attachedOutfit.SkinnedMeshBindings);
                attachedOutfit.ProfileControlledMorphBinding?.Dispose();
                attachedOutfits.RemoveAt(i);
                PushAttachedOutfitsToBlender();
                // A same-frame replacement must not leave this generated graph
                // under the Figure until Unity processes deferred destruction.
                // Hide only the explicitly owned Extra roots before rebuilding
                // the Figure Spring graph.
                DetachOwnedExtraRoots(attachedOutfit.ExtraRoots);
                // Remove this Outfit's entries and reconstruct after the old
                // graph is no longer visible in the Figure hierarchy.
                attachedOutfit.SpringBoneAttachment?.Dispose();
                DestroyTransforms(attachedOutfit.ExtraRoots);
                if (attachedOutfit.RuntimeOutfitInstance != null)
                {
                    DestroyForLifecycle(attachedOutfit.RuntimeOutfitInstance);
                }
                NotifyTopologyChanged();
                return true;
            }

            return false;
        }

        /// <summary>Publishes the single successful topology-mutation boundary used by attach and detach.</summary>
        internal void NotifyTopologyChanged() => TopologyChanged?.Invoke();

        private void SubscribeToFbmWeights()
        {
            if (fbmWeightSubscription != null || dynamicBoneBlender == null)
            {
                return;
            }

            fbmWeightSubscription = dynamicBoneBlender.FbmWeightChanged.Subscribe(OnFbmWeightChanged);
            indexedFbmWeightSubscription = dynamicBoneBlender.TargetWeightsChanged.Subscribe(OnTargetWeightsChanged);
        }

        private void DisposeFbmSubscription()
        {
            fbmWeightSubscription?.Dispose();
            fbmWeightSubscription = null;
            indexedFbmWeightSubscription?.Dispose();
            indexedFbmWeightSubscription = null;
        }

        private void OnTargetWeightsChanged(float[] weights)
        {
            for (int outfitIndex = 0; outfitIndex < attachedOutfits.Count; outfitIndex++)
            {
                IReadOnlyList<OutfitSkinnedMeshBinding> bindings = attachedOutfits[outfitIndex].SkinnedMeshBindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    bindings[bindingIndex]?.ApplyTargetWeights(weights);
                }
            }
        }

        private void OnFbmWeightChanged(FbmWeightChange change)
        {
            for (int outfitIndex = 0; outfitIndex < attachedOutfits.Count; outfitIndex++)
            {
                attachedOutfits[outfitIndex].ProfileControlledMorphBinding?.ApplyFbmWeight(change);
                IReadOnlyList<OutfitSkinnedMeshBinding> bindings = attachedOutfits[outfitIndex].SkinnedMeshBindings;
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    bindings[bindingIndex]?.ApplyFbmWeight(change);
                }

                if (!attachedOutfits[outfitIndex].UsesBcpBakedBindposes)
                {
                    TryAlignBindingsToFigureSkinning(bindings, out _);
                }

            }
        }

        private void ApplyCurrentFbmState(IReadOnlyList<OutfitSkinnedMeshBinding> bindings)
        {
            if (dynamicBoneBlender == null || dynamicBoneBlender.Targets == null)
            {
                return;
            }

            for (int targetIndex = 0; targetIndex < dynamicBoneBlender.Targets.Count; targetIndex++)
            {
                DynamicBoneBlendTarget target = dynamicBoneBlender.Targets[targetIndex];
                if (target == null || string.IsNullOrEmpty(target.blendName))
                {
                    continue;
                }

                FbmWeightChange change = new FbmWeightChange(target.blendName, target.weight, target.enabled);
                for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
                {
                    bindings[bindingIndex]?.ApplyFbmWeight(change);
                }
            }
        }

        private void ConfigureAndApplyCurrentTargetState(IReadOnlyList<OutfitSkinnedMeshBinding> bindings)
        {
            if (dynamicBoneBlender == null)
            {
                return;
            }

            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                OutfitSkinnedMeshBinding binding = bindings[bindingIndex];
                if (binding == null)
                {
                    continue;
                }

                binding.ConfigureDdbTargets(dynamicBoneBlender.Targets);
                binding.ApplyTargetWeights(dynamicBoneBlender.CurrentTargetWeights);
            }
        }

        private void ConfigureOutfitNormalBlenders(Transform root)
        {
            if (dynamicBoneBlender == null || root == null) return;
            Materials.NormalBlender[] blenders = root.GetComponentsInChildren<Materials.NormalBlender>(true);
            for (int blenderIndex = 0; blenderIndex < blenders.Length; blenderIndex++)
            {
                if (blenders[blenderIndex] != null) blenders[blenderIndex].DynamicBoneBlender = dynamicBoneBlender;
            }
        }

        private void ApplyCurrentFbmState(ProfileControlledMorphBinding binding)
        {
            if (binding == null || dynamicBoneBlender == null || dynamicBoneBlender.Targets == null) return;
            for (int i = 0; i < dynamicBoneBlender.Targets.Count; i++)
            {
                DynamicBoneBlendTarget target = dynamicBoneBlender.Targets[i];
                if (target != null && !string.IsNullOrEmpty(target.blendName)) binding.ApplyFbmWeight(new FbmWeightChange(target.blendName, target.weight, target.enabled));
            }
        }

        private static bool UsesStaticBcpBindposes(ShapeSyncOutfit outfit)
        {
            return outfit != null
                && outfit.SkinningProfile != null
                && outfit.SkinningProfile.UsesBcpBakedBindposes;
        }

        private bool TryAlignBindingsToFigureSkinning(IReadOnlyList<OutfitSkinnedMeshBinding> bindings, out string error)
        {
            error = null;
            SkinnedMeshRenderer figureRenderer = dynamicBoneBlender != null ? dynamicBoneBlender.TargetSkinnedMeshRenderer : null;
            if (figureRenderer == null)
            {
                error = "Figure Target SkinnedMeshRenderer is missing.";
                return false;
            }

            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                OutfitSkinnedMeshBinding binding = bindings[bindingIndex];
                if (binding != null && !binding.TryAlignToFigureSkinning(figureRenderer, out error))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryCreateRendererBindings(
            IReadOnlyList<RendererPlan> rendererPlans,
            List<OutfitSkinnedMeshBinding> bindings,
            List<Transform> rendererRoots,
            out string error)
        {
            error = null;
            if (rendererPlans.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < rendererPlans.Count; i++)
            {
                RendererPlan plan = rendererPlans[i];
                if (!TryResolveFigureBones(plan, out Transform[] bones, out error))
                {
                    return false;
                }

                plan.renderer.bones = bones;
                plan.renderer.rootBone = null;
                rendererRoots.Add(plan.renderer.transform);

                if (!OutfitSkinnedMeshBinding.TryCreate(
                        plan.renderer,
                        plan.profile,
                        out OutfitSkinnedMeshBinding binding,
                        out error))
                {
                    return false;
                }
                bindings.Add(binding);
            }

            return true;
        }

        private bool TryResolveFigureBones(RendererPlan plan, out Transform[] bones, out string error)
        {
            error = null;
            bones = new Transform[plan.bonePaths.Length];
            for (int i = 0; i < plan.bonePaths.Length; i++)
            {
                if (plan.bonePaths[i] == null)
                {
                    continue;
                }

                bones[i] = FindFigureTransform(plan.bonePaths[i]);
                if (bones[i] == null)
                {
                    error = $"Renderer '{plan.rendererPath}' bone path '{plan.bonePaths[i]}' was not found on the Figure after Extra Bone transplant.";
                    return false;
                }
            }
            return true;
        }

        private Transform FindFigureTransform(string path)
        {
            return string.IsNullOrEmpty(path) ? transform : transform.Find(path);
        }

        private bool TryBuildRendererPlans(ShapeSyncOutfit runtimeOutfit, out List<RendererPlan> plans, out string error)
        {
            plans = new List<RendererPlan>();
            error = null;
            SkinnedMeshRenderer[] renderers = runtimeOutfit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return true;
            }

            OutfitSkinningProfile skinningProfile = runtimeOutfit.SkinningProfile;
            if (skinningProfile == null)
            {
                error = "Outfit has SkinnedMeshRenderer components but no OutfitSkinningProfile.";
                return false;
            }

            HashSet<string> rendererPaths = new HashSet<string>();
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                string rendererPath = GetRelativePath(runtimeOutfit.transform, renderer.transform);
                if (string.IsNullOrEmpty(rendererPath) || !rendererPaths.Add(rendererPath))
                {
                    error = "Outfit contains a root renderer or duplicate renderer path.";
                    return false;
                }

                if (!skinningProfile.TryGetRenderer(rendererPath, out OutfitSkinningRendererProfile profile))
                {
                    error = $"OutfitSkinningProfile has no renderer entry for '{rendererPath}'.";
                    return false;
                }

                Transform[] sourceBones = renderer.bones;
                if (sourceBones == null || sourceBones.Length == 0 || renderer.rootBone == null)
                {
                    error = $"Renderer '{rendererPath}' has no bones or rootBone.";
                    return false;
                }

                string[] bonePaths = new string[sourceBones.Length];
                bool[] usedBoneIndices = GetUsedBoneIndices(renderer.sharedMesh, sourceBones.Length);
                for (int boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
                {
                    if (!usedBoneIndices[boneIndex])
                    {
                        // Importers can retain unrelated bones in SkinnedMeshRenderer.bones.
                        // A zero-weight slot has no skinning contribution and does not need a
                        // corresponding Figure transform at attach time.
                        bonePaths[boneIndex] = null;
                        continue;
                    }

                    Transform sourceBone = sourceBones[boneIndex];
                    bonePaths[boneIndex] = sourceBone != null ? GetRelativePath(runtimeOutfit.transform, sourceBone) : null;
                    if (bonePaths[boneIndex] == null)
                    {
                        error = sourceBone == null
                            ? $"Renderer '{rendererPath}' has a used null bone at index {boneIndex}."
                            : $"Renderer '{rendererPath}' has a used bone outside the Outfit hierarchy at index {boneIndex}.";
                        return false;
                    }
                }

                string rootBonePath = GetRelativePath(runtimeOutfit.transform, renderer.rootBone);
                if (rootBonePath == null)
                {
                    error = $"Renderer '{rendererPath}' has a rootBone outside the Outfit hierarchy.";
                    return false;
                }

                plans.Add(new RendererPlan
                {
                    renderer = renderer,
                    rendererPath = rendererPath,
                    bonePaths = bonePaths,
                    rootBonePath = rootBonePath,
                    sourceRootBone = renderer.rootBone,
                    profile = profile
                });
            }

            return true;
        }

        private static bool[] GetUsedBoneIndices(Mesh mesh, int boneCount)
        {
            bool[] used = new bool[boneCount];
            if (mesh == null || boneCount == 0)
            {
                return used;
            }

            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
            try
            {
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight1 weight = weights[i];
                    if (weight.weight > Mathf.Epsilon && weight.boneIndex >= 0 && weight.boneIndex < used.Length)
                    {
                        used[weight.boneIndex] = true;
                    }
                }
            }
            finally
            {
                weights.Dispose();
            }

            return used;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target)
            {
                return string.Empty;
            }

            Stack<string> segments = new Stack<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", segments) : null;
        }

        private static void RollbackAttach(
            GameObject runtimeInstance,
            IReadOnlyList<Transform> attachedRoots,
            IReadOnlyList<OutfitSkinnedMeshBinding> bindings,
            IReadOnlyList<Transform> rendererRoots,
            IShapeSyncOptionalVrmAttachment springBoneAttachment)
        {
            springBoneAttachment?.Rollback();
            DisposeBindings(bindings);
            DestroyTransforms(rendererRoots);
            DestroyTransforms(attachedRoots);
            if (runtimeInstance != null)
            {
                DestroyForLifecycle(runtimeInstance);
            }
        }


        private static bool TryCloneExtraBoneSubtree(
            Transform sourceRoot,
            Transform figureAnchor,
            IDictionary<Transform, Transform> destinationBySource,
            out Transform figureRoot,
            out string error)
        {
            figureRoot = null;
            error = null;
            if (sourceRoot == null || figureAnchor == null || destinationBySource == null)
            {
                error = "Extra Bone source, Figure anchor, or transform map is unavailable.";
                return false;
            }

            // Instantiate is intentional: this copies the complete Extra Bone subtree, including
            // VRM Spring components, colliders, and every child GameObject.  A Transform-only
            // reconstruction would leave Figure Spring entries pointing into the disposable
            // Outfit clone.
            GameObject clone = Instantiate(sourceRoot.gameObject, figureAnchor, false);
            clone.name = sourceRoot.name;
            figureRoot = clone.transform;
            if (!TryMapClonedHierarchy(sourceRoot, figureRoot, destinationBySource))
            {
                DestroyForLifecycle(clone);
                figureRoot = null;
                error = $"Extra Bone subtree '{sourceRoot.name}' did not preserve its child hierarchy during Figure copy.";
                return false;
            }

            return true;
        }

        private static bool TryMapClonedHierarchy(
            Transform source,
            Transform destination,
            IDictionary<Transform, Transform> destinationBySource)
        {
            if (source == null || destination == null || destinationBySource == null || source.childCount != destination.childCount) return false;
            destinationBySource[source] = destination;
            for (int i = 0; i < source.childCount; i++)
            {
                if (!TryMapClonedHierarchy(source.GetChild(i), destination.GetChild(i), destinationBySource)) return false;
            }
            return true;
        }

        private Transform MapOutfitTransform(
            Transform runtimeRoot,
            IReadOnlyDictionary<Transform, Transform> figureTransformByOutfitTransform,
            Transform source)
        {
            if (source == null)
            {
                return null;
            }

            if (figureTransformByOutfitTransform != null
                && figureTransformByOutfitTransform.TryGetValue(source, out Transform figureTransform)
                && figureTransform != null)
            {
                return figureTransform;
            }

            string path = GetRelativePath(runtimeRoot, source);
            Transform named = FindUniqueFigureTransformByName(source.gameObject.name);
            if (named != null)
            {
                return named;
            }

            return path == null ? null : FindFigureTransform(path);
        }

        private Transform FindUniqueFigureTransformByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Transform result = null;
            var stack = new Stack<Transform>();
            stack.Push(transform);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current.name == name)
                {
                    if (result != null) return null;
                    result = current;
                }

                for (int i = 0; i < current.childCount; i++) stack.Push(current.GetChild(i));
            }

            return result;
        }

        private static void DisposeBindings(IReadOnlyList<OutfitSkinnedMeshBinding> bindings)
        {
            if (bindings == null)
            {
                return;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                bindings[i]?.Dispose();
            }
        }

        private static void DestroyTransforms(IReadOnlyList<Transform> transforms)
        {
            if (transforms == null)
            {
                return;
            }

            for (int i = 0; i < transforms.Count; i++)
            {
                if (transforms[i] != null)
                {
                    DestroyForLifecycle(transforms[i].gameObject);
                }
            }
        }

        private static void ReleaseRuntimeSourceRoots(
            Transform runtimeOutfitRoot,
            IReadOnlyList<RendererPlan> rendererPlans,
            IReadOnlyList<Transform> physicsSourceRoots,
            IShapeSyncOptionalVrmAttachment springBoneAttachment)
        {
            if (runtimeOutfitRoot == null)
            {
                return;
            }

            var roots = new HashSet<Transform>();
            if (rendererPlans != null)
            {
                for (int i = 0; i < rendererPlans.Count; i++)
                {
                    AddDirectRuntimeChild(runtimeOutfitRoot, rendererPlans[i]?.sourceRootBone, roots);
                }
            }
            if (physicsSourceRoots != null)
            {
                for (int i = 0; i < physicsSourceRoots.Count; i++)
                {
                    AddDirectRuntimeChild(runtimeOutfitRoot, physicsSourceRoots[i], roots);
                }
            }
            IReadOnlyList<Transform> vrmCleanupRoots = springBoneAttachment?.RuntimeSourceCleanupRoots;
            if (vrmCleanupRoots != null)
            {
                for (int i = 0; i < vrmCleanupRoots.Count; i++)
                {
                    AddDirectRuntimeChild(runtimeOutfitRoot, vrmCleanupRoots[i], roots);
                }
            }

            foreach (Transform root in roots)
            {
                // Detach first so deferred Unity destruction never exposes this
                // transfer-only graph as a second Figure hierarchy for a frame.
                root.SetParent(null, false);
                DestroyForLifecycle(root.gameObject);
            }
        }

        private static void AddDirectRuntimeChild(Transform runtimeOutfitRoot, Transform source, ISet<Transform> roots)
        {
            if (runtimeOutfitRoot == null || source == null || roots == null)
            {
                return;
            }

            Transform current = source;
            while (current.parent != null && current.parent != runtimeOutfitRoot)
            {
                current = current.parent;
            }

            if (current.parent == runtimeOutfitRoot)
            {
                roots.Add(current);
            }
        }

        private static void DetachOwnedExtraRoots(IReadOnlyList<Transform> roots)
        {
            if (roots == null)
            {
                return;
            }

            for (int i = 0; i < roots.Count; i++)
            {
                Transform root = roots[i];
                if (root == null)
                {
                    continue;
                }

                root.SetParent(null, false);
                root.gameObject.SetActive(false);
            }
        }

        private static void DestroyForLifecycle(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }


        private void PushAttachedOutfitsToBlender()
        {
            if (dynamicBoneBlender != null)
            {
                dynamicBoneBlender.SetAttachedOutfitRegistrySets(attachedOutfits);
            }
        }

        private bool TryBuildAttachPlan(ShapeSyncOutfit outfitPrefab, out AttachPlan plan, out string error)
        {
            return TryBuildAttachPlan(outfitPrefab, null, null, null, attachedOutfits, out plan, out error);
        }

        private bool TryBuildAttachPlan(
            ShapeSyncOutfit outfitPrefab,
            HashSet<string> projectedRegistryIds,
            HashSet<string> projectedRootPaths,
            HashSet<string> detachedRootPaths,
            IReadOnlyList<AttachedOutfitRegistrySet> projectedAttachedOutfits,
            out AttachPlan plan,
            out string error)
        {
            plan = null;
            error = null;
            if (outfitPrefab == null)
            {
                error = "Outfit Prefab is null.";
                return false;
            }

            if (dynamicBoneBlender == null)
            {
                error = "DynamicBoneBlender is not assigned.";
                return false;
            }

            if (figureAnimator == null)
            {
                error = "Figure Animator is not assigned.";
                return false;
            }

            if (string.IsNullOrEmpty(outfitPrefab.RegistryId))
            {
                error = "registryId is empty.";
                return false;
            }

            if (projectedRegistryIds != null ? projectedRegistryIds.Contains(outfitPrefab.RegistryId) : ContainsAttachedRegistryId(outfitPrefab.RegistryId))
            {
                error = $"registryId '{outfitPrefab.RegistryId}' is already attached.";
                return false;
            }

            if (!TryValidateRegistry(outfitPrefab.BaseExtraBoneRegistry, "Base Extra Bone Registry", out HashSet<string> basePaths, out error)
                || !TryValidateFbmRegistries(outfitPrefab, out error))
            {
                return false;
            }

            if (!dynamicBoneBlender.TryValidateHumanoidBoneCorrectionProfile(
                    outfitPrefab.HumanoidBoneCorrectionProfile,
                    projectedAttachedOutfits ?? attachedOutfits,
                    out error)
                || !dynamicBoneBlender.TryValidateFbmHumanoidBoneCorrectionProfiles(
                    outfitPrefab.FbmHumanoidBoneCorrectionProfiles,
                    projectedAttachedOutfits ?? attachedOutfits,
                    out error))
            {
                return false;
            }

            plan = new AttachPlan();
            HashSet<string> plannedRootPaths = new HashSet<string>();
            foreach (string basePath in basePaths)
            {
                if (IsPathOwnedByRoots(basePath, projectedRootPaths) || !TryGetAttachRootPlan(basePath, detachedRootPaths, out AttachRootPlan rootPlan, out error))
                {
                    if (error == null) error = $"Extra Bone path '{basePath}' is already owned by an attached Outfit.";
                    return false;
                }

                if (rootPlan.rootPath == null || !plannedRootPaths.Add(rootPlan.rootPath))
                {
                    continue;
                }

                if (outfitPrefab.transform.Find(rootPlan.rootPath) == null || IsRootPathOwnedByRoots(rootPlan.rootPath, projectedRootPaths))
                {
                    error = $"Extra Bone root '{rootPlan.rootPath}' conflicts with the Figure or an attached Outfit.";
                    return false;
                }

                plan.roots.Add(rootPlan);
            }
            return true;
        }

        private bool TryValidateFbmRegistries(ShapeSyncOutfit outfitPrefab, out string error)
        {
            error = null;
            IReadOnlyList<ShapeSyncOutfitFbmExtraBoneRegistry> fbmRegistries = outfitPrefab.FbmExtraBoneRegistries;
            if (fbmRegistries == null)
            {
                error = "FBM Extra Bone Registry list is null.";
                return false;
            }

            HashSet<string> blendNames = new HashSet<string>();
            for (int i = 0; i < fbmRegistries.Count; i++)
            {
                ShapeSyncOutfitFbmExtraBoneRegistry entry = fbmRegistries[i];
                if (entry == null || string.IsNullOrEmpty(entry.blendName) || entry.extraBoneRegistry == null)
                {
                    error = "FBM Extra Bone Registry entry is incomplete.";
                    return false;
                }

                if (!blendNames.Add(entry.blendName))
                {
                    error = $"FBM Extra Bone Registry blendName '{entry.blendName}' is duplicated.";
                    return false;
                }

                if (!ContainsBlendName(entry.blendName) && !IsKnownPbmDifferenceBlendName(entry.blendName))
                {
                    error = $"FBM Extra Bone Registry blendName '{entry.blendName}' is neither a DynamicBoneBlender target nor a resolved PBM difference pair.";
                    return false;
                }

                if (!TryValidateRegistry(entry.extraBoneRegistry, $"FBM Extra Bone Registry '{entry.blendName}'", out HashSet<string> targetPaths, out error))
                {
                    return false;
                }

            }

            return true;
        }

        private bool TryValidateRegistry(CharacterBoneRegistry registry, string label, out HashSet<string> paths, out string error)
        {
            paths = new HashSet<string>();
            error = null;
            if (registry == null || registry.bonePoses == null)
            {
                error = $"{label} is null.";
                return false;
            }

            HashSet<Transform> humanoidBones = BuildHumanoidBoneSet();
            for (int i = 0; i < registry.bonePoses.Count; i++)
            {
                BonePoseData pose = registry.bonePoses[i];
                if (pose == null || string.IsNullOrEmpty(pose.boneName))
                {
                    error = $"{label} contains a null or empty bone path.";
                    return false;
                }

                if (!paths.Add(pose.boneName))
                {
                    error = $"{label} contains duplicate bone path '{pose.boneName}'.";
                    return false;
                }

                if (pose.hasBindpose || pose.bindposeIndex >= 0)
                {
                    error = $"{label} contains bindpose reference bone '{pose.boneName}'.";
                    return false;
                }

                Transform existingTransform = transform.Find(pose.boneName);
                if (existingTransform != null && humanoidBones.Contains(existingTransform))
                {
                    error = $"{label} contains Humanoid Bone path '{pose.boneName}'.";
                    return false;
                }
            }

            return true;
        }

        private bool TryGetAttachRootPlan(string path, HashSet<string> detachedRootPaths, out AttachRootPlan rootPlan, out string error)
        {
            rootPlan = default;
            error = null;
            string[] segments = path.Split('/');
            Transform current = transform;
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i]))
                {
                    error = $"Extra Bone path '{path}' contains an empty segment.";
                    return false;
                }

                string currentPath = JoinPath(segments, 0, i + 1);
                if (detachedRootPaths != null && detachedRootPaths.Contains(currentPath))
                {
                    rootPlan.anchor = current;
                    rootPlan.rootPath = currentPath;
                    return true;
                }
                Transform child = current.Find(segments[i]);
                if (child == null)
                {
                    rootPlan.anchor = current;
                    rootPlan.rootPath = currentPath;
                    return true;
                }

                current = child;
            }

            return true;
        }

        private bool ContainsAttachedRegistryId(string registryId)
        {
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                if (attachedOutfits[i] != null && attachedOutfits[i].RegistryId == registryId) return true;
            }

            return false;
        }

        private bool ContainsBlendName(string blendName)
        {
            IReadOnlyList<DynamicBoneBlendTarget> targets = dynamicBoneBlender.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].blendName == blendName) return true;
            }

            return false;
        }

        private bool IsKnownPbmDifferenceBlendName(string blendName)
        {
            if (string.IsNullOrEmpty(blendName) || !blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<DynamicBoneBlendTarget> targets = dynamicBoneBlender.Targets;
            for (int pbmIndex = 0; pbmIndex < targets.Count; pbmIndex++)
            {
                DynamicBoneBlendTarget pbmTarget = targets[pbmIndex];
                if (pbmTarget == null || string.IsNullOrEmpty(pbmTarget.blendName)
                    || !pbmTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                {
                    continue;
                }

                string pbmName = pbmTarget.blendName.Substring(BlendShapeReservedPrefixes.Pbm.Length);
                for (int fbmIndex = 0; fbmIndex < targets.Count; fbmIndex++)
                {
                    DynamicBoneBlendTarget fbmTarget = targets[fbmIndex];
                    if (fbmTarget == null || string.IsNullOrEmpty(fbmTarget.blendName)
                        || fbmTarget.blendName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (blendName == BlendShapeReservedPrefixes.Pbm + fbmTarget.blendName + "_" + pbmName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsPathOwnedByAttachedOutfit(string path)
        {
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                IReadOnlyList<string> rootPaths = attachedOutfits[i].ExtraRootPaths;
                for (int rootIndex = 0; rootIndex < rootPaths.Count; rootIndex++)
                {
                    if (path == rootPaths[rootIndex] || path.StartsWith(rootPaths[rootIndex] + "/", StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

        private bool IsPathOwnedByRoots(string path, HashSet<string> projectedRootPaths)
        {
            if (projectedRootPaths == null) return IsPathOwnedByAttachedOutfit(path);
            foreach (string rootPath in projectedRootPaths)
            {
                if (path == rootPath || path.StartsWith(rootPath + "/", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private bool IsRootPathOwnedByAttachedOutfit(string rootPath)
        {
            for (int i = 0; i < attachedOutfits.Count; i++)
            {
                IReadOnlyList<string> rootPaths = attachedOutfits[i].ExtraRootPaths;
                for (int rootIndex = 0; rootIndex < rootPaths.Count; rootIndex++)
                {
                    string existingRoot = rootPaths[rootIndex];
                    if (rootPath == existingRoot || rootPath.StartsWith(existingRoot + "/", StringComparison.Ordinal) || existingRoot.StartsWith(rootPath + "/", StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

        private bool IsRootPathOwnedByRoots(string rootPath, HashSet<string> projectedRootPaths)
        {
            if (projectedRootPaths == null) return IsRootPathOwnedByAttachedOutfit(rootPath);
            foreach (string existingRoot in projectedRootPaths)
            {
                if (rootPath == existingRoot || rootPath.StartsWith(existingRoot + "/", StringComparison.Ordinal) || existingRoot.StartsWith(rootPath + "/", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private HashSet<Transform> BuildHumanoidBoneSet()
        {
            HashSet<Transform> humanoidBones = new HashSet<Transform>();
            if (figureAnimator == null || !figureAnimator.isHuman) return humanoidBones;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = figureAnimator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) humanoidBones.Add(bone);
            }

            return humanoidBones;
        }

        private static string JoinPath(string[] segments, int start, int endExclusive)
        {
            string result = segments[start];
            for (int i = start + 1; i < endExclusive; i++) result += "/" + segments[i];
            return result;
        }
    }
}
