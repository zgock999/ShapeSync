// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Mesh-route values that update only an entry's Normal texture property.</summary>
    [System.Serializable]
    public struct MaterialProxyMeshValues
    {
        /// <summary>Gets or sets whether to apply <see cref="normalTexture"/>.</summary>
        public bool applyNormalTexture;
        /// <summary>Gets or sets the Normal-map texture owned by NormalBlender.</summary>
        public Texture normalTexture;
    }

    /// <summary>Describes whether a requested Material Proxy semantic will be applied during a dry run.</summary>
    public enum MaterialProxySemanticApplication
    {
        /// <summary>The semantic was not requested.</summary>
        NotRequested,
        /// <summary>The semantic is supported and will be applied when the plan is committed.</summary>
        Applied,
        /// <summary>The semantic was requested but is unsupported by the entry adapter.</summary>
        Ignored
    }

    /// <summary>Immutable transient plan produced by <see cref="MaterialProxy.TryDryRun"/> and consumed by <see cref="MaterialProxy.TryCommit(MaterialProxyDryRunPlan, out MaterialProxyDiagnostic)"/>.</summary>
    /// <remarks>The plan snapshots the issuing Proxy, entry, Adapter identity, and write assignments. It does not mutate a Material or retain itself in the Proxy. Commit revalidates those identities before applying the snapshot.</remarks>
    public sealed class MaterialProxyDryRunPlan
    {
        internal readonly MaterialProxy owner;
        internal readonly MaterialProxyEntry entry;
        internal readonly MaterialShaderAdapter adapter;
        internal readonly MaterialPropertyAssignment[] assignments;

        internal MaterialProxyDryRunPlan(MaterialProxy owner, MaterialProxyEntry entry, MaterialShaderAdapter adapter, MaterialPropertyAssignment[] assignments, MaterialProxySemanticApplication baseColorTexture, MaterialProxySemanticApplication normalTexture, MaterialProxySemanticApplication color, MaterialProxySemanticApplication uvTransform, MaterialProxyDiagnostic diagnostic)
        {
            this.owner = owner;
            this.entry = entry;
            this.adapter = adapter;
            this.assignments = assignments;
            EntryName = entry.entryName;
            BaseColorTexture = baseColorTexture;
            NormalTexture = normalTexture;
            Color = color;
            UvTransform = uvTransform;
            Diagnostic = diagnostic;
        }

        /// <summary>Gets the entry name validated by this plan.</summary>
        public string EntryName { get; }
        /// <summary>Gets the BaseColor texture application outcome.</summary>
        public MaterialProxySemanticApplication BaseColorTexture { get; }
        /// <summary>Gets the Normal texture application outcome.</summary>
        public MaterialProxySemanticApplication NormalTexture { get; }
        /// <summary>Gets the color application outcome.</summary>
        public MaterialProxySemanticApplication Color { get; }
        /// <summary>Gets the UV transform application outcome.</summary>
        public MaterialProxySemanticApplication UvTransform { get; }
        /// <summary>Gets the warning produced by the dry run, if any.</summary>
        public MaterialProxyDiagnostic Diagnostic { get; }
    }

    /// <summary>Immutable transient Mesh-route plan produced by <see cref="MaterialProxy.TryDryRunMesh"/>.</summary>
    /// <remarks>The plan is independent from Material-route plans and contains only Normal semantic assignments.</remarks>
    public sealed class MaterialProxyMeshDryRunPlan
    {
        internal readonly MaterialProxy owner;
        internal readonly MaterialProxyEntry entry;
        internal readonly MaterialShaderAdapter adapter;
        internal readonly MaterialPropertyAssignment[] assignments;

        internal MaterialProxyMeshDryRunPlan(MaterialProxy owner, MaterialProxyEntry entry, MaterialShaderAdapter adapter, MaterialPropertyAssignment[] assignments, MaterialProxySemanticApplication normalTexture, MaterialProxyDiagnostic diagnostic)
        {
            this.owner = owner;
            this.entry = entry;
            this.adapter = adapter;
            this.assignments = assignments;
            EntryName = entry.entryName;
            NormalTexture = normalTexture;
            Diagnostic = diagnostic;
        }

        /// <summary>Gets the entry name validated by this plan.</summary>
        public string EntryName { get; }
        /// <summary>Gets the Normal texture application outcome.</summary>
        public MaterialProxySemanticApplication NormalTexture { get; }
        /// <summary>Gets the warning produced by the dry run, if any.</summary>
        public MaterialProxyDiagnostic Diagnostic { get; }
    }

    /// <summary>Immutable transient reset plan produced by <see cref="MaterialProxy.TryDryRunReset"/>.</summary>
    public sealed class MaterialProxyResetDryRunPlan
    {
        internal readonly MaterialProxy owner;
        internal readonly MaterialProxyEntry entry;
        internal readonly MaterialShaderAdapter adapter;
        internal readonly Material runtimeMaterial;
        internal readonly Material sourceMaterial;

        internal MaterialProxyResetDryRunPlan(MaterialProxy owner, MaterialProxyEntry entry, MaterialShaderAdapter adapter, Material runtimeMaterial, Material sourceMaterial)
        {
            this.owner = owner;
            this.entry = entry;
            this.adapter = adapter;
            this.runtimeMaterial = runtimeMaterial;
            this.sourceMaterial = sourceMaterial;
            EntryName = entry.entryName;
        }

        /// <summary>Gets the entry validated for reset.</summary>
        public string EntryName { get; }
    }
}
