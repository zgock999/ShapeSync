// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

namespace zgock.ShapeSync.Materials
{
    /// <summary>Immutable transient plan produced by <see cref="MaterialAttacher.TryDryRun"/> and consumed by <see cref="MaterialAttacher.TryCommit(MaterialAttacherDryRunPlan, StackMachine.TextureDelivery, out MaterialAttacherResult)"/>.</summary>
    /// <remarks>This wrapper exposes semantic outcomes from an underlying Proxy plan without exposing its write assignments. It owns no delivery and is not retained by the Attacher after DryRun or Commit.</remarks>
    public sealed class MaterialAttacherDryRunPlan
    {
        internal readonly MaterialAttacher owner;
        internal readonly MaterialProxyDryRunPlan proxyPlan;
        internal readonly MaterialProxySemanticValues values;

        internal MaterialAttacherDryRunPlan(MaterialAttacher owner, MaterialProxyDryRunPlan proxyPlan, MaterialProxySemanticValues values)
        {
            this.owner = owner;
            this.proxyPlan = proxyPlan;
            this.values = values;
        }

        /// <summary>Gets the Proxy entry name validated by this plan.</summary>
        public string EntryName => proxyPlan.EntryName;
        /// <summary>Gets the BaseColor texture application outcome.</summary>
        public MaterialProxySemanticApplication BaseColorTexture => proxyPlan.BaseColorTexture;
        /// <summary>Gets the color application outcome.</summary>
        public MaterialProxySemanticApplication Color => proxyPlan.Color;
        /// <summary>Gets the UV transform application outcome.</summary>
        public MaterialProxySemanticApplication UvTransform => proxyPlan.UvTransform;
        /// <summary>Gets the warning produced by the underlying Proxy dry run, if any.</summary>
        public MaterialProxyDiagnostic Diagnostic => proxyPlan.Diagnostic;
    }

    /// <summary>Transient Attacher relay plan for one non-mutating Proxy material reset validation.</summary>
    public sealed class MaterialAttacherResetDryRunPlan
    {
        internal readonly MaterialAttacher owner;
        internal readonly MaterialProxyResetDryRunPlan proxyPlan;

        internal MaterialAttacherResetDryRunPlan(MaterialAttacher owner, MaterialProxyResetDryRunPlan proxyPlan)
        {
            this.owner = owner;
            this.proxyPlan = proxyPlan;
            EntryName = proxyPlan.EntryName;
        }

        /// <summary>Gets the Proxy entry validated for reset.</summary>
        public string EntryName { get; }
    }
}
