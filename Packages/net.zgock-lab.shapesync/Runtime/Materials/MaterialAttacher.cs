// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Identifies the overall outcome of one <see cref="MaterialAttacher"/> operation.</summary>
    /// <remarks><see cref="PartialApplied"/> reports a successful commit in which an unsupported semantic was ignored.</remarks>
    public enum MaterialAttacherResultCode
    {
        /// <summary>The requested semantics were applied or restored.</summary>
        Applied,
        /// <summary>The operation succeeded, but one or more requested semantics were unsupported and ignored.</summary>
        PartialApplied,
        /// <summary>The request was rejected and its received delivery candidates were released.</summary>
        Rejected,
        /// <summary>The entry was already applying to its Material Proxy and the request was rejected.</summary>
        EntryBusy,
        /// <summary>The Proxy entry could not be restored, so its current deliveries remain owned by the Attacher.</summary>
        RestoreFailed
    }

    /// <summary>Reports one Material Attacher operation.</summary>
    /// <remarks>The result never owns a <see cref="TextureDelivery"/> or a dry-run plan.</remarks>
    public struct MaterialAttacherResult
    {
        /// <summary>Gets the overall attachment outcome.</summary>
        public MaterialAttacherResultCode code;
        /// <summary>Gets the diagnostic for a rejected or partially applied request.</summary>
        public MaterialProxyDiagnostic diagnostic;
    }

    /// <summary>Relays one-entry Material transactions to a co-located <see cref="MaterialProxy"/>.</summary>
    /// <remarks>This component does not clone, read back, copy, or retain delivery textures. A successful commit transfers delivery ownership into the Proxy.</remarks>
    [DisallowMultipleComponent]
    public sealed class MaterialAttacher : MonoBehaviour
    {
        private sealed class EntryState
        {
            public string name;
            public bool applying;
        }

        [SerializeField] private MaterialProxy proxy;
        private readonly List<EntryState> states = new List<EntryState>();

        /// <summary>Gets or sets the co-located Material Proxy served by this Attacher.</summary>
        /// <value>A Proxy on the same <see cref="GameObject"/>, or <see langword="null"/>.</value>
        /// <remarks>Changing this value releases every retained delivery and does not restore the previous Proxy.</remarks>
        public MaterialProxy Proxy
        {
            get => proxy;
            set
            {
                if (proxy == value) return;
                DisposeAll();
                proxy = value;
            }
        }

        private void OnDisable() => DisposeAll();
        private void OnDestroy() => DisposeAll();
        private void Update() { if (proxy == null || proxy.gameObject != gameObject || !proxy.isActiveAndEnabled) DisposeAll(); }

        /// <summary>Validates a co-located Proxy request without receiving deliveries or changing Material state.</summary>
        /// <param name="entryName">The Proxy entry to update.</param>
        /// <param name="values">The semantic values to validate.</param>
        /// <param name="plan">The transient plan to pass unchanged to <see cref="TryCommit(MaterialAttacherDryRunPlan, TextureDelivery, out MaterialAttacherResult)"/>.</param>
        /// <param name="diagnostic">The warning or hard failure diagnostic.</param>
        /// <returns><see langword="true"/> when a plan was created; otherwise, <see langword="false"/>.</returns>
        /// <remarks>The caller temporarily retains <paramref name="plan"/> and passes it to <see cref="TryCommit(MaterialAttacherDryRunPlan, TextureDelivery, out MaterialAttacherResult)"/> or discards it. The Attacher does not retain the plan.</remarks>
        public bool TryDryRun(string entryName, MaterialProxySemanticValues values, out MaterialAttacherDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            plan = null;
            if (!IsActiveProxy()) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher requires an active co-located Material Proxy.", entryName); return false; }
            if (values.applyNormalTexture) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "Normal is a Mesh route semantic and cannot be applied through Material Attacher.", entryName); return false; }
            EntryState state = Find(entryName);
            if (state != null && state.applying) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry is busy.", entryName); return false; }
            if (!proxy.TryDryRun(entryName, values, out MaterialProxyDryRunPlan proxyPlan, out diagnostic)) return false;
            plan = new MaterialAttacherDryRunPlan(this, proxyPlan, values);
            return true;
        }

        /// <summary>Transfers delivery candidates into one co-located Proxy entry without cloning their Textures.</summary>
        /// <param name="entryName">The Proxy entry to update.</param>
        /// <param name="values">The semantic values, whose texture references must match the supplied deliveries.</param>
        /// <param name="baseColorDelivery">The owned BaseColor delivery, or <see langword="null"/> when BaseColor is not requested.</param>
        /// <param name="result">The overall operation outcome.</param>
        /// <returns><see langword="true"/> when the Proxy accepted the request, including a partial semantic application; otherwise, <see langword="false"/>.</returns>
        /// <remarks>This entry point performs DryRun then plan Commit. Ownership of a non-null BaseColor candidate transfers at invocation, including when the request is rejected.</remarks>
        public bool TryApply(string entryName, MaterialProxySemanticValues values, TextureDelivery baseColorDelivery, out MaterialAttacherResult result)
        {
            if (!ValidateCandidates(values, baseColorDelivery, out MaterialProxyDiagnostic diagnostic)) return Reject(baseColorDelivery, MaterialAttacherResultCode.Rejected, diagnostic, out result);
            if (!TryDryRun(entryName, values, out MaterialAttacherDryRunPlan plan, out diagnostic)) return Reject(baseColorDelivery, IsEntryBusy(entryName) ? MaterialAttacherResultCode.EntryBusy : MaterialAttacherResultCode.Rejected, diagnostic, out result);
            return TryCommit(plan, baseColorDelivery, out result);
        }

        /// <summary>Receives delivery candidates and commits a previously validated Attacher plan.</summary>
        /// <param name="plan">A plan returned by this Attacher's <see cref="TryDryRun"/> method.</param>
        /// <param name="baseColorDelivery">The owned BaseColor delivery, or <see langword="null"/> when BaseColor is not requested.</param>
        /// <param name="result">The overall operation outcome.</param>
        /// <returns><see langword="true"/> when the Proxy accepted the plan, including a partial semantic application; otherwise, <see langword="false"/>.</returns>
        /// <remarks>Ownership of non-null candidate deliveries transfers at invocation. A rejected plan releases those candidates; a successful applied semantic retains its delivery in the Proxy.</remarks>
        public bool TryCommit(MaterialAttacherDryRunPlan plan, TextureDelivery baseColorDelivery, out MaterialAttacherResult result)
        {
            result = default;
            if (plan == null || plan.owner != this) return Reject(baseColorDelivery, MaterialAttacherResultCode.Rejected, MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher dry-run plan belongs to another Attacher or is missing.", plan == null ? null : plan.EntryName), out result);
            if (!ValidateCandidates(plan.values, baseColorDelivery, out MaterialProxyDiagnostic diagnostic)) return Reject(baseColorDelivery, MaterialAttacherResultCode.Rejected, diagnostic, out result);
            if (!HasActiveProxy() || plan.proxyPlan.owner != proxy) return Reject(baseColorDelivery, MaterialAttacherResultCode.Rejected, MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher Proxy changed after the dry run.", plan.EntryName), out result);
            EntryState state = FindOrCreate(plan.EntryName);
            if (state.applying) return Reject(baseColorDelivery, MaterialAttacherResultCode.EntryBusy, MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry is busy.", plan.EntryName), out result);

            state.applying = true;
            try
            {
                // The Proxy accepts ownership at this call boundary, including rejection disposal.
                if (!proxy.TryCommit(plan.proxyPlan, baseColorDelivery, out MaterialProxyDiagnostic proxyCommitDiagnostic))
                {
                    result.code = MaterialAttacherResultCode.Rejected;
                    result.diagnostic = proxyCommitDiagnostic;
                    return false;
                }
                result.diagnostic = proxyCommitDiagnostic;
                result.code = HasIgnoredSemantic(plan) ? MaterialAttacherResultCode.PartialApplied : MaterialAttacherResultCode.Applied;
                return true;
            }
            finally { state.applying = false; }
        }

        /// <summary>Relays restoration of one Proxy entry.</summary>
        /// <param name="entryName">The Proxy entry to restore.</param>
        /// <param name="result">The restore outcome and any Proxy diagnostic.</param>
        /// <returns><see langword="true"/> when the Proxy restore completed; otherwise, <see langword="false"/>.</returns>
        /// <remarks>The Proxy owns the runtime delivery and releases it only when its own restoration succeeds.</remarks>
        public bool TryRestore(string entryName, out MaterialAttacherResult result)
        {
            result = default;
            if (!HasActiveProxy()) { result.code = MaterialAttacherResultCode.RestoreFailed; result.diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher requires an active Material Proxy.", entryName); return false; }
            if (!proxy.TryRestore(entryName, out MaterialProxyDiagnostic diagnostic)) { result.code = MaterialAttacherResultCode.RestoreFailed; result.diagnostic = diagnostic; return false; }
            result.code = MaterialAttacherResultCode.Applied;
            return true;
        }

        /// <summary>Validates an entry-local Proxy reset without mutating its Material.</summary>
        /// <param name="entryName">The Proxy entry to reset.</param>
        /// <param name="plan">The transient plan to pass unchanged to <see cref="TryCommitReset"/>.</param>
        /// <param name="diagnostic">The validation failure diagnostic.</param>
        /// <returns><see langword="true"/> when a reset plan was created.</returns>
        public bool TryDryRunReset(string entryName, out MaterialAttacherResetDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            plan = null;
            if (!IsActiveProxy()) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher requires an active co-located Material Proxy.", entryName); return false; }
            EntryState state = Find(entryName);
            if (state != null && state.applying) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry is busy.", entryName); return false; }
            if (!proxy.TryDryRunReset(entryName, out MaterialProxyResetDryRunPlan proxyPlan, out diagnostic)) return false;
            plan = new MaterialAttacherResetDryRunPlan(this, proxyPlan);
            return true;
        }

        /// <summary>Commits a previously validated entry-local Proxy reset.</summary>
        /// <param name="plan">A plan returned by <see cref="TryDryRunReset"/>.</param>
        /// <param name="result">The reset outcome and any Proxy diagnostic.</param>
        /// <returns><see langword="true"/> when reset completed.</returns>
        public bool TryCommitReset(MaterialAttacherResetDryRunPlan plan, out MaterialAttacherResult result)
        {
            result = default;
            if (plan == null || plan.owner != this) { result.code = MaterialAttacherResultCode.Rejected; result.diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher reset plan belongs to another Attacher or is missing.", plan == null ? null : plan.EntryName); return false; }
            if (!HasActiveProxy() || plan.proxyPlan.owner != proxy) { result.code = MaterialAttacherResultCode.Rejected; result.diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Attacher Proxy changed after the reset dry run.", plan.EntryName); return false; }
            EntryState state = FindOrCreate(plan.EntryName);
            if (state.applying) { result.code = MaterialAttacherResultCode.EntryBusy; result.diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry is busy.", plan.EntryName); return false; }
            state.applying = true;
            try
            {
                if (!proxy.TryCommitReset(plan.proxyPlan, out MaterialProxyDiagnostic diagnostic)) { result.code = MaterialAttacherResultCode.Rejected; result.diagnostic = diagnostic; return false; }
                result.code = MaterialAttacherResultCode.Applied;
                result.diagnostic = diagnostic;
                return true;
            }
            finally { state.applying = false; }
        }

        private static bool ValidateCandidates(MaterialProxySemanticValues values, TextureDelivery baseColor, out MaterialProxyDiagnostic diagnostic)
        {
            if (values.applyBaseColorTexture != (baseColor != null) || (baseColor != null && values.baseColorTexture != baseColor.Texture)) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "BaseColor delivery must exactly match the requested texture value."); return false; }
            if (values.applyNormalTexture) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "Normal is a Mesh route semantic and cannot be applied through Material Attacher."); return false; }
            diagnostic = default;
            return true;
        }

        private bool Reject(TextureDelivery baseColor, MaterialAttacherResultCode code, MaterialProxyDiagnostic diagnostic, out MaterialAttacherResult result)
        {
            Dispose(ref baseColor);
            result = new MaterialAttacherResult { code = code, diagnostic = diagnostic };
            return false;
        }

        private bool HasActiveProxy()
        {
            if (IsActiveProxy()) return true;
            DisposeAll();
            return false;
        }

        private bool IsActiveProxy() => proxy != null && proxy.gameObject == gameObject && proxy.isActiveAndEnabled;

        private static bool HasIgnoredSemantic(MaterialAttacherDryRunPlan plan)
        {
            return plan.BaseColorTexture == MaterialProxySemanticApplication.Ignored ||
                   plan.Color == MaterialProxySemanticApplication.Ignored ||
                   plan.UvTransform == MaterialProxySemanticApplication.Ignored;
        }

        private EntryState FindOrCreate(string entryName)
        {
            EntryState state = Find(entryName);
            if (state != null) return state;
            state = new EntryState { name = entryName };
            states.Add(state);
            return state;
        }

        private EntryState Find(string entryName)
        {
            for (int i = 0; i < states.Count; i++) if (states[i].name == entryName) return states[i];
            return null;
        }

        private bool IsEntryBusy(string entryName)
        {
            EntryState state = Find(entryName);
            return state != null && state.applying;
        }

        private void DisposeAll() { }

        private static void Dispose(ref TextureDelivery delivery) { if (delivery == null) return; delivery.Dispose(); delivery = null; }
    }
}
