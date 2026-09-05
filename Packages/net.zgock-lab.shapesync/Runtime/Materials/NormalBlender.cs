// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>One immutable raw non-PBM weight sample consumed by the Normal Blender.</summary>
    public readonly struct NormalTargetWeight
    {
        /// <summary>Creates a target-weight sample.</summary>
        /// <param name="targetName">The non-PBM target name.</param>
        /// <param name="weight">The finite raw DDB weight without normalization.</param>
        /// <param name="enabled">Whether the DDB target is enabled.</param>
        public NormalTargetWeight(string targetName, float weight, bool enabled) { TargetName = targetName; Weight = weight; Enabled = enabled; }
        /// <summary>Gets the non-PBM target name.</summary>
        public string TargetName { get; }
        /// <summary>Gets the finite raw DDB weight without clamping or normalization.</summary>
        public float Weight { get; }
        /// <summary>Gets whether the DDB target is enabled.</summary>
        public bool Enabled { get; }
    }

    /// <summary>Builds and applies Mesh-owned Normal textures for its authored Proxy entry names.</summary>
    /// <remarks><see cref="entries"/> is the complete authoring surface: each element is one Proxy entry name. The Blender resolves the co-located Proxy and Mesh StackMachine once, then privately owns per-entry request, stale/cancel, and delivery lifecycle. Disable suspends valid delivery escrow without dispatching; enable synchronously reapplies matching escrow or queues ordinary re-derivation for a changed snapshot. It never uses MaterialAttacher.</remarks>
    [DisallowMultipleComponent]
    public sealed class NormalBlender : MonoBehaviour
    {
        private sealed class EntryState
        {
            public TextureExecutionOriginKey origin;
            public ulong revision;
            public TextureExecutionHandle inFlight;
            public TextureDelivery delivery;
            public TextureSourceLease sourceLease;
            public StackMachineDiagnostic diagnostic;
            public bool pending;
            public bool suspended;
            public NormalTargetWeight[] suspendSnapshot;
        }

        [SerializeField] private DynamicBoneBlender dynamicBoneBlender;
        [SerializeField] private List<string> entries = new List<string>();
        private readonly Dictionary<string, EntryState> states = new Dictionary<string, EntryState>(StringComparer.Ordinal);
        private MaterialProxy proxy;
        private MeshStackMachine meshStackMachine;
        // Retained source halls are caller-owned by this component.  Keep the host lifecycle
        // subscription separate from the ordinary enable/disable suspension path: Unity does
        // not guarantee that this component's OnDestroy runs before the scene-local host.
        private TextureStackMachineHost retainedLeaseHost;
        private IDisposable subscription;
        private NormalTargetWeight[] latestSnapshot = Array.Empty<NormalTargetWeight>();

        /// <summary>Raised when a non-PBM weight snapshot changes.</summary>
        public event Action<NormalTargetWeight[]> SnapshotChanged;
        /// <summary>Gets the latest immutable non-PBM weight snapshot.</summary>
        public IReadOnlyList<NormalTargetWeight> LatestSnapshot => latestSnapshot;
        /// <summary>Gets the authored Proxy entry names whose Normal semantics this Blender manages.</summary>
        public IReadOnlyList<string> Entries => entries;

        /// <summary>Gets the latest structured failure for one authored Normal entry.</summary>
        /// <param name="entryName">The logical Proxy entry name.</param>
        /// <param name="diagnostic">The latest rejection diagnostic, or <see langword="null"/> when the entry is healthy or unknown.</param>
        /// <returns><see langword="true"/> when the named entry currently has a failure diagnostic.</returns>
        public bool TryGetEntryDiagnostic(string entryName, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            return !string.IsNullOrWhiteSpace(entryName) && states.TryGetValue(entryName, out EntryState state) && (diagnostic = state.diagnostic) != null;
        }

        /// <summary>Gets or sets the co-located Figure- or Outfit-local DDB source.</summary>
        public DynamicBoneBlender DynamicBoneBlender
        {
            get => dynamicBoneBlender;
            set { if (dynamicBoneBlender == value) return; StopObserving(); dynamicBoneBlender = value; if (isActiveAndEnabled) StartObserving(true); }
        }

        private void OnEnable()
        {
            proxy = GetComponent<MaterialProxy>();
            meshStackMachine = GetComponent<MeshStackMachine>();
            NormalTargetWeight[] current = BuildSnapshot(dynamicBoneBlender == null ? null : dynamicBoneBlender.CurrentFbmWeightSnapshot);
            bool hadSuspended = ResumeAll(current);
            latestSnapshot = current;
            StartObserving(false);
            if (dynamicBoneBlender != null) SnapshotChanged?.Invoke(current);
            if (!hadSuspended) QueueEntries();
        }
        private void Update() { if (isActiveAndEnabled) TryPumpPending(); }
        private void OnDisable() { StopObserving(); SuspendAll(); }
        private void OnDestroy()
        {
            StopObserving();
            DetachRetainedLeaseHost();
            ReleaseAllOnDestroy();
            proxy = null;
            meshStackMachine = null;
        }

        /// <summary>Retries construction for every configured entry using the latest DDB snapshot.</summary>
        /// <param name="diagnostic">The host-validation rejection diagnostic, or <see langword="null"/> on acceptance.</param>
        /// <returns><see langword="true"/> when all configured entries were queued.</returns>
        public bool TryRetry(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = ValidateHost();
            if (diagnostic != null) return false;
            QueueEntries();
            return true;
        }

        private void StartObserving(bool publishInitial)
        {
            if (subscription != null || dynamicBoneBlender == null) return;
            subscription = dynamicBoneBlender.FbmWeightSnapshotChanged.Subscribe(OnDdbSnapshotChanged);
            PublishSnapshot(dynamicBoneBlender.CurrentFbmWeightSnapshot, publishInitial);
        }
        private void StopObserving() { subscription?.Dispose(); subscription = null; }
        private void OnDdbSnapshotChanged(FbmWeightChange[] snapshot) => PublishSnapshot(snapshot, false);

        private void PublishSnapshot(IReadOnlyList<FbmWeightChange> source, bool force)
        {
            NormalTargetWeight[] next = BuildSnapshot(source);
            if (!force && SnapshotEquals(latestSnapshot, next)) return;
            latestSnapshot = next;
            QueueEntries();
            SnapshotChanged?.Invoke(next);
        }

        private void QueueEntries()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                string entryName = entries[i];
                if (!states.TryGetValue(entryName, out EntryState state)) { state = new EntryState(); states.Add(entryName, state); }
                state.revision++; state.inFlight?.Dispose(); state.inFlight = null; state.diagnostic = null; state.pending = true;
            }
        }

        private void TryPumpPending()
        {
            if (meshStackMachine == null || meshStackMachine.HasNormalTextureWork) return;
            for (int i = 0; i < entries.Count; i++)
            {
                string entryName = entries[i];
                if (!states.TryGetValue(entryName, out EntryState state) || !state.pending || state.inFlight != null) continue;
                state.pending = false;
                if (state.sourceLease != null && !state.sourceLease.IsValid) state.sourceLease = null;
                if (!TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost originHost, out StackMachineDiagnostic originDiagnostic))
                {
                    state.diagnostic = originDiagnostic;
                    continue;
                }
                if (!state.origin.BelongsTo(originHost)) state.origin = originHost.CreateOrigin();
                var options = new TextureExecutionOptions(sourceLease: state.sourceLease, retainSourceLease: true);
                if (!meshStackMachine.TrySubmitNormal(entryName, latestSnapshot, state.origin, options, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic))
                {
                    state.diagnostic = diagnostic;
                    continue;
                }
                AttachRetainedLeaseHost();
                ulong revision = state.revision;
                state.inFlight = handle;
                handle.Completed += completed => Complete(entryName, state, revision, completed);
                return;
            }
        }

        private void Complete(string entryName, EntryState state, ulong revision, TextureExecutionHandle completed)
        {
            if (state.revision != revision || !ReferenceEquals(state.inFlight, completed)) { completed.Dispose(); return; }
            state.inFlight = null;
            if (!completed.Succeeded) { state.diagnostic = completed.Diagnostic ?? StackMachineDiagnostic.CreateDomain("normal", "NormalExecutionFailed", "Texture StackMachine completed the Normal request without a success result.", bindingName: entryName); completed.Dispose(); return; }
            if (completed.Result != null && completed.Result.TryTakeSourceLease(out TextureSourceLease retainedSourceLease)) { state.sourceLease?.Dispose(); state.sourceLease = retainedSourceLease; }
            if (completed.Result == null || !completed.Result.TryTakeDelivery(out TextureDelivery delivery)) { state.diagnostic = StackMachineDiagnostic.CreateDomain("normal", "NormalDeliveryMissing", "Texture StackMachine completed the Normal request without a delivery.", bindingName: entryName); completed.Dispose(); return; }
            if (!proxy.TryDryRunMesh(entryName, new MaterialProxyMeshValues { applyNormalTexture = true, normalTexture = delivery.Texture }, out MaterialProxyMeshDryRunPlan plan, out MaterialProxyDiagnostic proxyDiagnostic)) { state.diagnostic = ProxyFailure("NormalProxyDryRunRejected", entryName, proxyDiagnostic); delivery.Dispose(); completed.Dispose(); return; }
            if (!proxy.TryCommitMesh(plan, out proxyDiagnostic) || plan.NormalTexture != MaterialProxySemanticApplication.Applied) { state.diagnostic = ProxyFailure("NormalProxyCommitRejected", entryName, proxyDiagnostic); delivery.Dispose(); completed.Dispose(); return; }
            state.delivery?.Dispose(); state.delivery = delivery; completed.Dispose();
        }

        private StackMachineDiagnostic ValidateHost()
        {
            if (proxy == null || meshStackMachine == null) return StackMachineDiagnostic.CreateDomain("normal", "NormalHostInvalid", "NormalBlender requires co-located MaterialProxy and MeshStackMachine.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++) if (string.IsNullOrWhiteSpace(entries[i]) || !unique.Add(entries[i])) return StackMachineDiagnostic.CreateDomain("normal", "NormalEntryInvalid", "Normal Blender entries must be non-empty and unique.", bindingName: entries[i]);
            return null;
        }

        private void ReleaseAllOnDestroy()
        {
            foreach (KeyValuePair<string, EntryState> pair in states)
            {
                pair.Value.revision++; pair.Value.inFlight?.Dispose(); pair.Value.inFlight = null;
                pair.Value.pending = false;
                pair.Value.sourceLease?.Dispose(); pair.Value.sourceLease = null;
                if (pair.Value.delivery == null) continue;
                pair.Value.delivery.Dispose();
                pair.Value.delivery = null;
            }
            states.Clear();
        }

        private void AttachRetainedLeaseHost()
        {
            if (!TextureStaticMachineFactory.TryGetExistingTSM(out TextureStackMachineHost host) || host == null || retainedLeaseHost == host) return;
            DetachRetainedLeaseHost();
            retainedLeaseHost = host;
            retainedLeaseHost.Destroying += OnRetainedLeaseHostDestroying;
        }

        private void DetachRetainedLeaseHost()
        {
            if (retainedLeaseHost != null) retainedLeaseHost.Destroying -= OnRetainedLeaseHostDestroying;
            retainedLeaseHost = null;
        }

        private void OnRetainedLeaseHostDestroying()
        {
            // This callback is raised before TextureStackMachineHost starts forced cleanup.
            // Return every caller-owned lease and pending handle while the host can still
            // release its halls; a later OnDestroy is intentionally idempotent.
            retainedLeaseHost = null;
            ReleaseAllOnDestroy();
        }
        private void SuspendAll()
        {
            foreach (KeyValuePair<string, EntryState> pair in states)
            {
                EntryState state = pair.Value;
                state.revision++; state.inFlight?.Dispose(); state.inFlight = null; state.pending = false;
                state.suspended = true;
                state.suspendSnapshot = (NormalTargetWeight[])latestSnapshot.Clone();
            }
        }

        private bool ResumeAll(NormalTargetWeight[] current)
        {
            bool hadSuspended = false;
            foreach (KeyValuePair<string, EntryState> pair in states)
            {
                EntryState state = pair.Value;
                if (!state.suspended) continue;
                hadSuspended = true;
                state.suspended = false;
                StackMachineDiagnostic diagnostic = null;
                if (state.delivery != null && SnapshotEquals(state.suspendSnapshot, current) && TryApplyDelivery(pair.Key, state.delivery, out diagnostic)) { state.diagnostic = null; continue; }
                state.delivery?.Dispose(); state.delivery = null;
                state.diagnostic = diagnostic;
                state.revision++; state.pending = true;
            }
            return hadSuspended;
        }

        private bool TryApplyDelivery(string entryName, TextureDelivery delivery, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (delivery == null || delivery.Texture == null)
            {
                diagnostic = EscrowStale(entryName, "deliveryTexture");
                return false;
            }
            if (proxy == null)
            {
                diagnostic = EscrowStale(entryName, "materialProxy");
                return false;
            }
            if (!proxy.TryDryRunMesh(entryName, new MaterialProxyMeshValues { applyNormalTexture = true, normalTexture = delivery.Texture }, out MaterialProxyMeshDryRunPlan plan, out MaterialProxyDiagnostic proxyDiagnostic)) { diagnostic = ProxyFailure("NormalProxyResumeDryRunRejected", entryName, proxyDiagnostic); return false; }
            if (!proxy.TryCommitMesh(plan, out proxyDiagnostic) || plan.NormalTexture != MaterialProxySemanticApplication.Applied) { diagnostic = ProxyFailure("NormalProxyResumeCommitRejected", entryName, proxyDiagnostic); return false; }
            return true;
        }

        private static NormalTargetWeight[] BuildSnapshot(IReadOnlyList<FbmWeightChange> source)
        {
            var snapshot = new List<NormalTargetWeight>();
            for (int i = 0; source != null && i < source.Count; i++) { FbmWeightChange target = source[i]; if (IsNormalTargetName(target.BlendName)) snapshot.Add(new NormalTargetWeight(target.BlendName, float.IsFinite(target.Weight) ? target.Weight : 0f, target.Enabled)); }
            return snapshot.ToArray();
        }
        private static bool SnapshotEquals(IReadOnlyList<NormalTargetWeight> left, IReadOnlyList<NormalTargetWeight> right)
        {
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++) if (!string.Equals(left[i].TargetName, right[i].TargetName, StringComparison.Ordinal) || left[i].Weight != right[i].Weight || left[i].Enabled != right[i].Enabled) return false;
            return true;
        }
        private static bool IsNormalTargetName(string targetName) => !string.IsNullOrEmpty(targetName) && !targetName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal);
        private static StackMachineDiagnostic EscrowStale(string entryName, string detail) => StackMachineDiagnostic.CreateDomain("normal", "NormalEscrowStale", "Normal escrow became structurally stale and was released for normal re-derivation.", bindingName: entryName, detail: detail);
        private static StackMachineDiagnostic ProxyFailure(string code, string entryName, MaterialProxyDiagnostic proxyDiagnostic) => StackMachineDiagnostic.CreateDomain("normal", code, "MaterialProxy rejected the Mesh-owned Normal update.", bindingName: entryName, detail: proxyDiagnostic.code + ": " + proxyDiagnostic.message);
    }
}
