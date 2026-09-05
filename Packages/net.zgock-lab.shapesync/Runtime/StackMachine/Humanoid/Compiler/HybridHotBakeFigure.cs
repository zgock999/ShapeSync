// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using System.Collections.Generic;
using zgock.ShapeSync;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Requested display mode of a <see cref="HybridHotBakeFigure"/>.</summary>
    public enum HybridHotBakeFigureMode
    {
        Edit,
        Run
    }

    /// <summary>
    /// Builds the initial and latest committed <see cref="ShapeDirector"/> state.  Startup
    /// schedules one warm bake after Director runtime-state construction; later commits use a
    /// short quiet window, and <see cref="Compile"/> remains the explicit trigger.
    /// </summary>
    public sealed class HybridHotBakeFigure : HotBakeComponentBase
    {
        [SerializeField] private ShapeDirector director;
        [SerializeField] private HybridHotBakeFigureMode mode = HybridHotBakeFigureMode.Edit;
        [SerializeField, Min(0f)] private float quietWindowSeconds = 0.1f;

        private ulong revision;
        private ulong activeRevision;
        private bool pending;
        private bool startupWarmBakePending;
        private bool subscribed;
        private float lastCommitTime;
        private HotBakeArtifactSceneScope artifactScope;
        private GameObject bakedRoot;
        private readonly List<Behaviour> editModeDisabledBehaviours = new List<Behaviour>();
        private readonly List<bool> editModeEnabledStates = new List<bool>();
        private readonly List<Renderer> editModeDisabledRenderers = new List<Renderer>();
        private readonly List<bool> editModeRendererEnabledStates = new List<bool>();
        private bool runMode;
        private HybridHotBakeFigureMode observedMode;
        private bool modeRunAdmissionRejected;

        /// <summary>Gets or sets the Director whose committed state is baked by this component.</summary>
        public ShapeDirector Director
        {
            get => director;
            set
            {
                if (ReferenceEquals(director, value)) return;
                // A Director swap must never strand the previous Director disabled and mutation
                // gated while this Hybrid is in Run Mode.
                if (runMode) RestoreEditMode();
                Unsubscribe();
                director = value;
                if (isActiveAndEnabled) Subscribe();
            }
        }

        /// <summary>Gets or sets the debounce duration applied after each Director commit.</summary>
        public float QuietWindowSeconds { get => quietWindowSeconds; set => quietWindowSeconds = Mathf.Max(0f, value); }
        /// <summary>Gets the monotonic revision assigned to the latest Director commit or explicit compile request.</summary>
        public ulong Revision => revision;
        /// <summary>Gets the revision currently owned by the active build transaction, if any.</summary>
        public ulong ActiveRevision => activeRevision;
        /// <summary>Gets whether a Director commit is waiting for its quiet window before compile admission.</summary>
        public bool IsBakePending => pending;
        /// <summary>Gets the current generation's Figure-child baked hierarchy after successful rebind and promotion.</summary>
        public GameObject BakedRoot => bakedRoot;
        /// <summary>Gets whether the baked renderer is currently displayed instead of the live Figure renderers.</summary>
        public bool IsRunMode => runMode;
        /// <summary>Gets or sets the Inspector-visible requested display mode.</summary>
        public HybridHotBakeFigureMode Mode { get => mode; set => mode = value; }

        /// <summary>Switches between the live Edit Mode display and the warm baked Run Mode display.</summary>
        /// <param name="enabled">Whether to enter Run Mode.</param>
        /// <param name="diagnostic">A structured synchronous-bake failure when no warm artifact is available.</param>
        /// <returns><see langword="true"/> when the requested display mode is active.</returns>
        /// <remarks>
        /// A warm artifact only changes enabled states.  When Run Mode is requested while stale,
        /// the latest Director state is baked synchronously before the display exchange.
        /// </remarks>
        public bool TrySetRunMode(bool enabled, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (runMode == enabled) return true;
            if (!enabled)
            {
                RestoreEditMode();
                return true;
            }
            if (!TryEnsureWarmArtifact(out diagnostic)) return false;
            EnterRunMode();
            return true;
        }

        private void Awake()
        {
            // A serialized Run request is not a persisted display state.  Each runtime lifetime
            // starts with the live Figure visible; Run can only be entered after warm admission.
            runMode = false;
            mode = HybridHotBakeFigureMode.Edit;
            observedMode = HybridHotBakeFigureMode.Edit;
            modeRunAdmissionRejected = false;
        }

        private void OnEnable() => Subscribe();
        private void OnDisable()
        {
            Unsubscribe();
            // Disabling this owner must never strand the co-located Director behind the Run
            // mutation gate or leave live Figure components hidden while Update is stopped.
            RestoreEditMode();
        }

        /// <summary>Initializes Director subscription and schedules the initial Edit-mode warm bake.</summary>
        /// <remarks>Hybrid deliberately waits until the Director has established its runtime state before compiling; it does not use the asset-document Startup path.</remarks>
        protected override void Start()
        {
            // Do not call base.Start(): this is a Director-current-state startup bake, not the
            // base component's asset-document startup trigger.  Update runs after every Start.
            // Director may already have committed its initial state through a subscription made
            // by the Director property setter.  That pending generation is the startup bake.
            startupWarmBakePending = !pending && !IsCompileActive && ArtifactSet == null;
        }

        private void Update()
        {
            if (artifactScope != null && artifactScope.ArtifactSet == null && ArtifactSet != null)
            {
                // Director / Outfit scope callbacks may invalidate their set synchronously.
                // Normalize component-facing state even when the host itself remains valid.
                SetLastDiagnostic(artifactScope.LastDiagnostic);
                ExitRunModeForInvalidation();
                InvalidateArtifactSet();
                artifactScope.Dispose();
                artifactScope = null;
                bakedRoot = null;
            }
            if (artifactScope != null && !artifactScope.Validate(out StackMachineDiagnostic scopeDiagnostic))
            {
                // Scope invalidation (host destroy / scene migration / topology) releases the
                // artifact internally; clear the component-facing handles in the same frame.
                SetLastDiagnostic(scopeDiagnostic);
                ExitRunModeForInvalidation();
                InvalidateArtifactSet();
                artifactScope.Dispose();
                artifactScope = null;
                bakedRoot = null;
            }
            PumpCurrentGeneration();
            BeginStartupWarmBakeAfterDirectorStart();
            ApplySerializedMode();
            if (!pending || IsCompileActive || Time.realtimeSinceStartup - lastCommitTime < quietWindowSeconds) return;

            if (!TryGetDirector(out StackMachineDiagnostic diagnostic))
            {
                pending = false;
                SetLastDiagnostic(diagnostic);
                return;
            }
            if (!director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out diagnostic))
            {
                pending = false;
                SetLastDiagnostic(diagnostic);
                return;
            }

            // The document is deliberately read here, rather than queued at commit time: latest wins.
            ulong requestedRevision = revision;
            if (BeginRuntimeDocumentCompile(document, out diagnostic))
            {
                activeRevision = requestedRevision;
                pending = false;
            }
            else
            {
                pending = false;
                SetLastDiagnostic(diagnostic);
            }
        }

        /// <summary>Immediately compiles the Director's current committed state without the quiet window.</summary>
        public override bool Compile(out StackMachineDiagnostic diagnostic)
        {
            revision++;
            pending = false;
            ExitRunModeForInvalidation();
            CancelCompile();
            InvalidateArtifactSet();
            artifactScope?.Dispose();
            artifactScope = null;
            bakedRoot = null;
            if (!TryGetDirector(out diagnostic)) return false;
            if (!director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out diagnostic))
            {
                SetLastDiagnostic(diagnostic);
                return false;
            }
            if (!BeginRuntimeDocumentCompile(document, out diagnostic)) return false;
            activeRevision = revision;
            return true;
        }

        /// <summary>Restores Edit mode, unsubscribes from the Director, and releases Hybrid-owned artifacts.</summary>
        protected override void OnDestroy()
        {
            Unsubscribe();
            artifactScope?.Dispose();
            artifactScope = null;
            bakedRoot = null;
            RestoreEditMode();
            base.OnDestroy();
        }

        private void PumpCurrentGeneration()
        {
            if (!IsCompileActive) return;
            HumanoidBuildOperationStatus status = PumpCompile(out HumanoidBuildResult result, out StackMachineDiagnostic diagnostic);
            if (status == HumanoidBuildOperationStatus.Pending) return;
            try
            {
                if (status != HumanoidBuildOperationStatus.Succeeded)
                {
                    SetLastDiagnostic(diagnostic);
                    return;
                }
                if (activeRevision != revision)
                {
                    SetLastDiagnostic(StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeStaleGenerationDiscarded", "A superseded Hybrid Hot Bake result was discarded before promotion."));
                    return;
                }
                GameObject candidateRoot = result.Root;
                if (!TryRebindCandidate(candidateRoot, out diagnostic))
                {
                    SetLastDiagnostic(diagnostic);
                    return;
                }
                artifactScope?.Dispose();
                artifactScope = new HotBakeArtifactSceneScope(gameObject, ScopeHost, director, GetComponent<OutfitAttacher>());
                if (!TryCommitInspectedResult(result, artifactScope, out diagnostic))
                {
                    SetLastDiagnostic(diagnostic);
                    artifactScope.Dispose();
                    artifactScope = null;
                    return;
                }
                // Scene-scope admission may clone the candidate into the host scene.  Use the
                // artifact-owned template after commit, never the pre-commit candidate.
                GameObject promotedRoot = ArtifactSet?.TemplateRoot;
                if (promotedRoot == null)
                {
                    SetLastDiagnostic(StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeArtifactTemplateRequired", "Hybrid Hot Bake promotion did not retain its candidate template."));
                    InvalidateArtifactSet();
                    return;
                }
                PruneReboundCandidate(promotedRoot);
                if (!TrySynchronizeRuntimeProxyNormals(out diagnostic))
                {
                    SetLastDiagnostic(diagnostic);
                    InvalidateArtifactSet();
                    return;
                }
                // Mesh Core creates an escrow candidate with HideAndDontSave. Unlike a Spawner
                // clone, Hybrid promotes that exact template into the live Figure hierarchy, so
                // it must become a normal (albeit inactive in Edit mode) scene hierarchy object.
                HotBakeSpawnPrimitive.PrepareVisibleRuntimeHierarchy(promotedRoot.transform);
                promotedRoot.transform.SetParent(transform, false);
                // A bake always lands in Edit Mode.  The next warm Run request only changes
                // visibility; it must not allocate or create a hierarchy.
                promotedRoot.SetActive(false);
                bakedRoot = promotedRoot;
            }
            finally
            {
                result?.Dispose();
                DisposeDriver();
            }
        }

        private bool TrySynchronizeRuntimeProxyNormals(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (ArtifactSet == null) return Reject("HotBakeArtifactRequired", "Hybrid Hot Bake requires an artifact before runtime Normal synchronization.", out diagnostic);
            MaterialProxy[] proxies = GetComponentsInChildren<MaterialProxy>(true);
            for (int slotIndex = 0; slotIndex < ArtifactSet.MaterialSlots.Count; slotIndex++)
            {
                HumanoidBuildMaterialSlot slot = ArtifactSet.MaterialSlots[slotIndex];
                Material target = ArtifactSet.Materials[slotIndex];
                MaterialProxyEntry resolved = null;
                for (int proxyIndex = 0; proxyIndex < proxies.Length; proxyIndex++)
                {
                    IReadOnlyList<MaterialProxyEntry> entries = proxies[proxyIndex].Entries;
                    for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                    {
                        MaterialProxyEntry entry = entries[entryIndex];
                        if (entry == null || entry.entryName != slot.MaterialId.EntryId || entry.runtimeMaterial == null) continue;
                        if (resolved != null) continue;
                        resolved = entry;
                    }
                }
                if (resolved == null) continue;
                var readPlan = new List<MaterialPropertyReadCommand>();
                if (!resolved.adapter.TryBuildReadPlan(readPlan, out MaterialProxyDiagnostic adapterDiagnostic) || !resolved.adapter.TryReadValues(resolved.runtimeMaterial, readPlan, out MaterialProxySemanticValues values, out adapterDiagnostic))
                    return Reject("HotBakeRuntimeNormalReadRejected", adapterDiagnostic.message, out diagnostic, slot.MaterialId.EntryId);
                if (!values.applyNormalTexture || values.normalTexture == null) continue;
                if (!slot.Adapter.TryApplyInMemory(target, new MaterialProxySemanticValues { applyNormalTexture = true, normalTexture = values.normalTexture }, out _, out adapterDiagnostic))
                    return Reject("HotBakeRuntimeNormalApplyRejected", adapterDiagnostic.message, out diagnostic, slot.MaterialId.EntryId);
            }
            return true;
        }

        private bool TryRebindCandidate(GameObject candidateRoot, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (candidateRoot == null) return Reject("HotBakeHybridCandidateRequired", "Hybrid Hot Bake requires a successful candidate hierarchy.", out diagnostic);
            SkinnedMeshRenderer[] renderers = candidateRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Transform[] bones = renderer.bones;
                Transform[] remapped = new Transform[bones.Length];
                for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                {
                    Transform source = bones[boneIndex];
                    Transform target = source == null ? null : FindLiveBone(candidateRoot.transform, source);
                    if (target == null)
                    {
                        if (HasNonZeroWeight(renderer.sharedMesh, boneIndex)) return Reject("HotBakeHybridWeightedBoneMissing", "Hybrid Hot Bake cannot rebind a weighted candidate bone absent from the live Figure.", out diagnostic, source == null ? null : source.name);
                        target = transform;
                    }
                    remapped[boneIndex] = target;
                }
                renderer.bones = remapped;
                if (renderer.rootBone != null)
                {
                    Transform rootBone = FindLiveBone(candidateRoot.transform, renderer.rootBone);
                    if (rootBone == null) return Reject("HotBakeHybridRootBoneMissing", "Hybrid Hot Bake cannot resolve the candidate root bone in the live Figure.", out diagnostic, renderer.rootBone.name);
                    renderer.rootBone = rootBone;
                }
            }
            // The candidate contributes renderers only.  The live Figure Animator remains the
            // sole animation owner for the re-bound skeleton.
            Animator[] candidateAnimators = candidateRoot.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < candidateAnimators.Length; i++) candidateAnimators[i].enabled = false;
            return true;
        }

        // A generic Hot Bake candidate is a self-contained clone.  Once Hybrid has rebound
        // every renderer to the live Figure skeleton, that clone must no longer retain a
        // second humanoid/spring/VRM hierarchy.  Keep only Transform paths necessary to host
        // the baked renderers, and strip all behaviours from those paths.
        private static void PruneReboundCandidate(GameObject candidateRoot)
        {
            if (candidateRoot == null) return;
            SkinnedMeshRenderer[] renderers = candidateRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var retained = new HashSet<Transform> { candidateRoot.transform };
            for (int index = 0; index < renderers.Length; index++)
                for (Transform current = renderers[index] == null ? null : renderers[index].transform; current != null; current = current.parent)
                {
                    retained.Add(current);
                    if (current == candidateRoot.transform) break;
                }
            for (int index = candidateRoot.transform.childCount - 1; index >= 0; index--)
            {
                Transform child = candidateRoot.transform.GetChild(index);
                if (retained.Contains(child)) continue;
                child.SetParent(null, true);
                DestroyHybridTransientObject(child.gameObject);
            }
            Behaviour[] behaviours = candidateRoot.GetComponentsInChildren<Behaviour>(true);
            // Component dependency order follows the candidate's original Inspector order
            // (Humanoid before Vrm10Instance). Remove in reverse so dependent VRM components
            // disappear before their Humanoid prerequisite.
            for (int index = behaviours.Length - 1; index >= 0; index--)
                if (behaviours[index] != null) DestroyHybridTransientObject(behaviours[index]);
        }

        private static void DestroyHybridTransientObject(Object value)
        {
            if (value == null) return;
            // This is an unpublished candidate, before it can become an active scene object.
            // Immediate removal is required to satisfy Unity component dependencies (for
            // example Vrm10Instance -> Humanoid) in the same promotion frame.
            Object.DestroyImmediate(value);
        }

        private static bool HasNonZeroWeight(Mesh mesh, int boneIndex)
        {
            if (mesh == null) return true;
            BoneWeight[] weights = mesh.boneWeights;
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight weight = weights[i];
                if ((weight.boneIndex0 == boneIndex && weight.weight0 > 0f) || (weight.boneIndex1 == boneIndex && weight.weight1 > 0f) || (weight.boneIndex2 == boneIndex && weight.weight2 > 0f) || (weight.boneIndex3 == boneIndex && weight.weight3 > 0f)) return true;
            }
            return false;
        }

        private static string RelativePath(Transform root, Transform value)
        {
            if (value == root) return string.Empty;
            var names = new List<string>();
            for (Transform current = value; current != null && current != root; current = current.parent) names.Add(current.name);
            if (names.Count == 0 || value == null || !value.IsChildOf(root)) return null;
            names.Reverse();
            return string.Join("/", names);
        }

        private Transform FindLiveBone(Transform candidateRoot, Transform candidateBone)
        {
            string path = RelativePath(candidateRoot, candidateBone);
            return path == string.Empty ? transform : string.IsNullOrEmpty(path) ? null : transform.Find(path);
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", code, message, bindingName: binding);
            return false;
        }

        private void Subscribe()
        {
            if (subscribed || !TryGetDirector(out _)) return;
            director.TransactionStarting += OnDirectorTransactionStarting;
            director.TransactionCommitted += OnDirectorTransactionCommitted;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            director.TransactionStarting -= OnDirectorTransactionStarting;
            director.TransactionCommitted -= OnDirectorTransactionCommitted;
            subscribed = false;
        }

        private bool TryGetDirector(out StackMachineDiagnostic diagnostic)
        {
            if (director == null) director = GetComponent<ShapeDirector>();
            if (director != null)
            {
                diagnostic = null;
                return true;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeDirectorRequired", "Hybrid Hot Bake requires a ShapeDirector input.");
            return false;
        }

        private void OnDirectorTransactionStarting()
        {
            // Release before Mesh execution can detach/attach Outfits.  Keeping the promoted
            // clone below the Figure until a topology callback is too late for AvatarBuilder
            // and VRM source-role resolution performed during that execution.
            ExitRunModeForInvalidation();
            // Destroy is deferred in Play Mode. Detach first so same-stack Mesh execution and
            // AvatarBuilder cannot see the duplicate skeleton even before lifecycle cleanup.
            GameObject previousBakedRoot = bakedRoot;
            if (previousBakedRoot != null) previousBakedRoot.transform.SetParent(null, true);
            CancelCompile();
            InvalidateArtifactSet();
            artifactScope?.Dispose();
            artifactScope = null;
            bakedRoot = null;
            SetLastDiagnostic(null);
        }

        private void OnDirectorTransactionCommitted()
        {
            // Director.Start may commit before or after Hybrid.Start.  A committed generation is
            // already the required initial warm bake, so never start a duplicate startup one.
            startupWarmBakePending = false;
            revision++;
            pending = true;
            lastCommitTime = Time.realtimeSinceStartup;
            // A newer committed state invalidates both the promoted result and any in-flight build.
            OnDirectorTransactionStarting();
        }

        private void BeginStartupWarmBakeAfterDirectorStart()
        {
            if (!startupWarmBakePending || IsCompileActive || pending) return;
            startupWarmBakePending = false;
            if (!TryGetDirector(out StackMachineDiagnostic diagnostic))
            {
                SetLastDiagnostic(diagnostic);
                return;
            }
            if (!director.TryBuildCurrentStateDocument(out ShapeSyncDocument document, out diagnostic))
            {
                SetLastDiagnostic(diagnostic);
                return;
            }

            revision++;
            if (BeginRuntimeDocumentCompile(document, out diagnostic))
            {
                activeRevision = revision;
                return;
            }
            SetLastDiagnostic(diagnostic);
        }

        private bool TryEnsureWarmArtifact(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (ArtifactSet != null && bakedRoot != null) return true;
            if (!Compile(out diagnostic)) return false;
            const int synchronousPumpLimit = 256;
            for (int i = 0; i < synchronousPumpLimit && IsCompileActive; i++) PumpCurrentGeneration();
            if (ArtifactSet != null && bakedRoot != null) return true;
            diagnostic = LastDiagnostic ?? StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeRunBakeDidNotComplete", "Hybrid Hot Bake could not produce a warm artifact for the requested Run Mode.");
            SetLastDiagnostic(diagnostic);
            return false;
        }

        private void ExitRunModeForInvalidation()
        {
            if (runMode) RestoreEditMode();
        }

        private void ApplySerializedMode()
        {
            if (observedMode != mode)
            {
                observedMode = mode;
                modeRunAdmissionRejected = false;
            }
            bool requestedRunMode = mode == HybridHotBakeFigureMode.Run;
            if (!requestedRunMode)
            {
                if (runMode) RestoreEditMode();
                return;
            }
            if (runMode) return;

            // Inspector selection is a persistent Run request, unlike the synchronous public
            // API.  Atlas page execution can require a later frame, so do not turn the enum back
            // to Edit merely because a warm artifact cannot be completed in this Update.
            if (ArtifactSet != null && bakedRoot != null)
            {
                EnterRunMode();
                return;
            }
            if (IsCompileActive || pending || modeRunAdmissionRejected) return;
            if (!Compile(out StackMachineDiagnostic diagnostic))
            {
                modeRunAdmissionRejected = true;
                SetLastDiagnostic(diagnostic);
            }
        }

        private void EnterRunMode()
        {
            editModeDisabledBehaviours.Clear();
            editModeEnabledStates.Clear();
            CaptureAndDisableLiveRenderers();
            CaptureAndDisableComponents<DynamicBoneBlender>();
            CaptureAndDisableComponents<ShapeDirector>();
            CaptureAndDisableComponents<MaterialProxy>();
            CaptureAndDisableComponents<NormalBlender>();
            CaptureAndDisableComponents<MaterialAttacher>();
            if (director != null) director.SetRuntimeMutationBlocked(true);
            bakedRoot.SetActive(true);
            runMode = true;
            mode = HybridHotBakeFigureMode.Run;
        }

        private void RestoreEditMode()
        {
            if (bakedRoot != null) bakedRoot.SetActive(false);
            if (director != null) director.SetRuntimeMutationBlocked(false);
            for (int i = editModeDisabledBehaviours.Count - 1; i >= 0; i--)
            {
                Behaviour behaviour = editModeDisabledBehaviours[i];
                if (behaviour != null) behaviour.enabled = editModeEnabledStates[i];
            }
            editModeDisabledBehaviours.Clear();
            editModeEnabledStates.Clear();
            for (int i = editModeDisabledRenderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = editModeDisabledRenderers[i];
                if (renderer != null) renderer.enabled = editModeRendererEnabledStates[i];
            }
            editModeDisabledRenderers.Clear();
            editModeRendererEnabledStates.Clear();
            runMode = false;
            mode = HybridHotBakeFigureMode.Edit;
        }

        private void CaptureAndDisableLiveRenderers()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || (bakedRoot != null && renderer.transform.IsChildOf(bakedRoot.transform))) continue;
                CaptureAndDisableRenderer(renderer);
            }
        }

        private void CaptureAndDisableComponents<T>() where T : Behaviour
        {
            T[] components = GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++) CaptureAndDisable(components[i]);
        }

        private void CaptureAndDisable(Behaviour behaviour)
        {
            if (behaviour == null || ReferenceEquals(behaviour, this)) return;
            for (int i = 0; i < editModeDisabledBehaviours.Count; i++) if (ReferenceEquals(editModeDisabledBehaviours[i], behaviour)) return;
            editModeDisabledBehaviours.Add(behaviour);
            editModeEnabledStates.Add(behaviour.enabled);
            behaviour.enabled = false;
        }

        private void CaptureAndDisableRenderer(Renderer renderer)
        {
            for (int i = 0; i < editModeDisabledRenderers.Count; i++) if (ReferenceEquals(editModeDisabledRenderers[i], renderer)) return;
            editModeDisabledRenderers.Add(renderer);
            editModeRendererEnabledStates.Add(renderer.enabled);
            renderer.enabled = false;
        }
    }
}
