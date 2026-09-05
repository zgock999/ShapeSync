// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Identifies the outcome or failure category of a Material Proxy operation.</summary>
    public enum MaterialProxyDiagnosticCode
    {
        /// <summary>The operation completed without a diagnostic.</summary>
        None,
        /// <summary>The proxy or its requested entry is not ready for the operation.</summary>
        ProxyNotReady,
        /// <summary>More than one entry uses the same entry name.</summary>
        DuplicateEntryName,
        /// <summary>More than one entry owns the same renderer material channel.</summary>
        DuplicateRendererChannel,
        /// <summary>An entry does not have a target renderer.</summary>
        RendererMissing,
        /// <summary>An entry's material channel is outside its renderer's material array.</summary>
        MaterialChannelOutOfRange,
        /// <summary>An entry does not have a source Material to clone or operate on.</summary>
        SourceMaterialMissing,
        /// <summary>An entry does not have a Material Shader Adapter.</summary>
        AdapterMissing,
        /// <summary>The current Material shader differs from the assigned adapter's expected shader.</summary>
        ShaderMismatch,
        /// <summary>A requested semantic is unsupported and was ignored.</summary>
        SemanticUnsupported,
        /// <summary>An adapter-required Material property is missing or invalid.</summary>
        RequiredPropertyMissing,
        /// <summary>A requested color is non-finite or outside the accepted Linear RGBA range.</summary>
        InvalidColor,
        /// <summary>A requested UV transform contains a non-finite value.</summary>
        InvalidUvTransform,
        /// <summary>An entry-owned runtime Material could not be restored to its original slot.</summary>
        RestoreFailed,
        /// <summary>A suspended entry could not safely reuse its escrowed resources and was released for normal re-derivation.</summary>
        EscrowStale,
        /// <summary>The current Material does not expose a usable BaseColor texture for a canvas binding.</summary>
        CurrentTextureUnavailable,
        /// <summary>A texture could not be read back for semantic content inspection.</summary>
        TextureReadbackFailed
    }

    [Serializable]
    /// <summary>Describes a Material Proxy operation result, warning, or failure.</summary>
    public struct MaterialProxyDiagnostic
    {
        /// <summary>Gets the diagnostic category.</summary>
        public MaterialProxyDiagnosticCode code;
        /// <summary>Gets a human-readable description of the result.</summary>
        public string message;
        /// <summary>Gets the entry name associated with the result, when available.</summary>
        public string entryName;
        /// <summary>Creates a diagnostic with the specified category, message, and optional entry name.</summary>
        /// <param name="code">The diagnostic category.</param>
        /// <param name="message">The human-readable diagnostic message.</param>
        /// <param name="entryName">The affected entry name, if known.</param>
        /// <returns>A populated diagnostic value.</returns>
        public static MaterialProxyDiagnostic Fail(MaterialProxyDiagnosticCode code, string message, string entryName = null) => new MaterialProxyDiagnostic { code = code, message = message, entryName = entryName };
    }

    [Serializable]
    /// <summary>Serialized ownership and semantic configuration for one renderer material channel.</summary>
    public sealed class MaterialProxyEntry
    {
        /// <summary>Gets or sets the unique name used to address this entry through <see cref="MaterialProxy"/>.</summary>
        public string entryName;
        /// <summary>Gets or sets the Figure- or Outfit-hierarchy renderer whose material channel this entry owns.</summary>
        public SkinnedMeshRenderer renderer;
        /// <summary>Gets or sets the zero-based material channel on <see cref="renderer"/>.</summary>
        [Min(0)] public int materialChannel;
        /// <summary>Gets or sets the explicit shader semantic adapter for the channel.</summary>
        public MaterialShaderAdapter adapter;
        /// <summary>Gets or sets the serialized semantic values used by configured commits.</summary>
        public MaterialProxySemanticValues configuredValues;

        [NonSerialized] internal Material originalMaterial;
        [NonSerialized] internal Material runtimeMaterial;
        // A BaseColor delivery becomes entry-local Proxy state only after its Material assignment commits.
        [NonSerialized] internal TextureDelivery baseColorDelivery;
        [NonSerialized] internal bool initialized;
        [NonSerialized] internal bool suspended;
        [NonSerialized] internal readonly List<MaterialPropertyAssignment> writeBuffer = new List<MaterialPropertyAssignment>(8);
        [NonSerialized] internal readonly List<MaterialPropertyReadCommand> readBuffer = new List<MaterialPropertyReadCommand>(8);
    }

    [DisallowMultipleComponent]
    /// <summary>
    /// Owns entry-local runtime Material clones and applies explicit semantic assignments without mutating source Materials.
    /// </summary>
    /// <remarks>
    /// Each entry is limited to a Renderer material channel in the same Figure or Outfit hierarchy; the Proxy never owns or restores a slot in an unrelated hierarchy.
    /// A committed entry owns its runtime clone and any transferred BaseColor delivery. Disable suspends valid entry escrow, while <see cref="TryRestore"/> is the public terminal release operation.
    /// </remarks>
    public sealed class MaterialProxy : MonoBehaviour
    {
        [SerializeField] private List<MaterialProxyEntry> entries = new List<MaterialProxyEntry>();
        [NonSerialized] private MaterialProxyDiagnostic lastDiagnostic;

        /// <summary>Gets the serialized entries owned by this proxy.</summary>
        public IReadOnlyList<MaterialProxyEntry> Entries => entries;
        /// <summary>Gets the diagnostic produced by the most recent proxy operation.</summary>
        public MaterialProxyDiagnostic LastDiagnostic => lastDiagnostic;

        /// <summary>Suspends entry-owned runtime Materials by restoring renderer slots while retaining valid escrow resources.</summary>
        private void OnDisable() => SuspendAll();

        /// <summary>Resumes valid suspended entries by returning their retained runtime Materials to their renderer slots.</summary>
        private void OnEnable() => ResumeAll();

        /// <summary>
        /// Performs the terminal lifecycle cleanup: releases runtime Materials and then disposes
        /// any retained deliveries that were not released through an entry.
        /// </summary>
        private void OnDestroy()
        {
            ForceReleaseAll();
        }

        /// <summary>Applies semantic values to an entry-owned runtime clone for the named entry.</summary>
        /// <param name="entryName">The unique entry name to commit.</param>
        /// <param name="values">The semantic values and apply flags to use.</param>
        /// <param name="diagnostic">The operation result. <see cref="MaterialProxyDiagnosticCode.SemanticUnsupported"/> is a warning when the method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> when the requested supported semantics were applied or no-op accepted; otherwise, <see langword="false"/>.</returns>
        public bool TryCommit(string entryName, MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            if (!TryDryRun(entryName, values, out MaterialProxyDryRunPlan plan, out diagnostic)) return false;
            return TryCommit(plan, out diagnostic);
        }

        /// <summary>Validates semantic values and creates an immutable, non-mutating commit plan.</summary>
        /// <param name="entryName">The unique entry name to commit.</param>
        /// <param name="values">The semantic values and apply flags to use.</param>
        /// <param name="plan">The transient plan to pass unchanged to <see cref="TryCommit(MaterialProxyDryRunPlan, out MaterialProxyDiagnostic)"/>.</param>
        /// <param name="diagnostic">The warning or hard failure diagnostic.</param>
        /// <returns><see langword="true"/> when a plan was created; otherwise, <see langword="false"/>.</returns>
        /// <remarks>The caller temporarily retains <paramref name="plan"/> and passes it to <see cref="TryCommit(MaterialProxyDryRunPlan, out MaterialProxyDiagnostic)"/> or discards it. DryRun does not create a runtime Material clone or change a renderer assignment.</remarks>
        public bool TryDryRun(string entryName, MaterialProxySemanticValues values, out MaterialProxyDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            plan = null;
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (!TryValidateValues(entryName, values, out diagnostic)) return false;
            if (!TryValidateEntry(entry, out Material source, out diagnostic)) return false;
            MaterialProxyDiagnostic warning = GetUnsupportedSemanticWarning(entry, values);
            if (!entry.adapter.TryBuildWritePlan(values, entry.writeBuffer, out diagnostic)) return Remember(diagnostic, entryName);
            if (!TryValidateWritePlan(entry, source, entry.writeBuffer, out diagnostic)) return false;
            plan = new MaterialProxyDryRunPlan(this, entry, entry.adapter, entry.writeBuffer.ToArray(), ApplicationFor(values.applyBaseColorTexture, entry.adapter.Supports(MaterialProxySemantic.BaseColorTexture)), ApplicationFor(values.applyNormalTexture, entry.adapter.Supports(MaterialProxySemantic.NormalTexture)), ApplicationFor(values.applyColor, entry.adapter.Supports(MaterialProxySemantic.Color)), ApplicationFor(values.applyUvTransform, entry.adapter.Supports(MaterialProxySemantic.UvTransform)), warning);
            lastDiagnostic = warning;
            return true;
        }

        /// <summary>Applies a previously validated transient plan to its entry-owned runtime Material clone.</summary>
        /// <param name="plan">A plan returned by this Proxy's <see cref="TryDryRun"/> method.</param>
        /// <param name="diagnostic">The warning or hard failure diagnostic.</param>
        /// <returns><see langword="true"/> when the plan was applied; otherwise, <see langword="false"/>.</returns>
        /// <remarks>The issuing Proxy, entry, Adapter identity, current Material, and assignment properties are revalidated before mutation. The Proxy does not retain <paramref name="plan"/> after this call.</remarks>
        public bool TryCommit(MaterialProxyDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            if (plan == null || plan.owner != this) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy dry-run plan belongs to another Proxy or is missing.", plan == null ? null : plan.EntryName, out diagnostic);
            if (!TryFindEntry(plan.EntryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (entry != plan.entry) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry changed after the dry run.", plan.EntryName, out diagnostic);
            if (entry.adapter != plan.adapter) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy adapter changed after the dry run.", plan.EntryName, out diagnostic);
            if (!TryValidateEntry(entry, out Material source, out diagnostic)) return false;
            if (!TryValidateWritePlan(entry, source, plan.assignments, out diagnostic)) return false;
            if (plan.assignments.Length > 0)
            {
                if (!EnsureRuntimeMaterial(entry, source, out diagnostic)) return false;
                for (int i = 0; i < plan.assignments.Length; i++) Apply(entry.runtimeMaterial, plan.assignments[i]);
            }

            diagnostic = plan.Diagnostic;
            lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>Commits a validated Material plan and transfers its BaseColor delivery into this Proxy entry.</summary>
        /// <remarks>This internal Attacher boundary owns and releases a supplied delivery on every outcome. A successfully applied delivery is retained by the Proxy until replacement, reset, or restore.</remarks>
        internal bool TryCommit(MaterialProxyDryRunPlan plan, TextureDelivery baseColorDelivery, out MaterialProxyDiagnostic diagnostic)
        {
            if (plan == null || plan.owner != this)
            {
                baseColorDelivery?.Dispose();
                return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy dry-run plan belongs to another Proxy or is missing.", plan == null ? null : plan.EntryName, out diagnostic);
            }
            if (plan.BaseColorTexture == MaterialProxySemanticApplication.Applied && baseColorDelivery == null)
                return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "A BaseColor delivery is required by the Material Proxy plan.", plan.EntryName, out diagnostic);
            if (plan.BaseColorTexture != MaterialProxySemanticApplication.Applied && baseColorDelivery != null)
            {
                baseColorDelivery.Dispose();
                baseColorDelivery = null;
            }
            if (!TryCommit(plan, out diagnostic))
            {
                baseColorDelivery?.Dispose();
                return false;
            }
            if (baseColorDelivery != null)
            {
                if (!TryFindEntry(plan.EntryName, out MaterialProxyEntry entry, out diagnostic))
                {
                    baseColorDelivery.Dispose();
                    return false;
                }
                entry.baseColorDelivery?.Dispose();
                entry.baseColorDelivery = baseColorDelivery;
            }
            return true;
        }

        /// <summary>Validates an entry-local reset without mutating its runtime clone.</summary>
        /// <param name="entryName">The unique entry name to reset.</param>
        /// <param name="plan">The transient reset plan to pass to <see cref="TryCommitReset"/>.</param>
        /// <param name="diagnostic">The validation failure diagnostic.</param>
        /// <returns><see langword="true"/> when the reset plan was created.</returns>
        public bool TryDryRunReset(string entryName, out MaterialProxyResetDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            plan = null;
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (!TryValidateEntry(entry, out _, out diagnostic)) return false;
            if (entry.runtimeMaterial != null && entry.originalMaterial == null)
                return Fail(MaterialProxyDiagnosticCode.RestoreFailed, "Material Proxy reset cannot locate the clone source Material.", entryName, out diagnostic);
            plan = new MaterialProxyResetDryRunPlan(this, entry, entry.adapter, entry.runtimeMaterial, entry.originalMaterial);
            diagnostic = default;
            return true;
        }

        /// <summary>Copies the clone source Material over an entry runtime clone and releases the Proxy-owned delivery.</summary>
        /// <param name="plan">A reset plan returned by <see cref="TryDryRunReset"/>.</param>
        /// <param name="diagnostic">The commit failure diagnostic.</param>
        /// <returns><see langword="true"/> when reset completed or the entry has no runtime clone.</returns>
        public bool TryCommitReset(MaterialProxyResetDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            if (plan == null || plan.owner != this) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy reset plan belongs to another Proxy or is missing.", plan == null ? null : plan.EntryName, out diagnostic);
            if (!TryFindEntry(plan.EntryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (entry != plan.entry || entry.adapter != plan.adapter) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry or adapter changed after the reset dry run.", plan.EntryName, out diagnostic);
            if (entry.runtimeMaterial == null) { diagnostic = default; lastDiagnostic = diagnostic; return true; }
            if (entry.runtimeMaterial != plan.runtimeMaterial || entry.originalMaterial != plan.sourceMaterial || entry.originalMaterial == null)
                return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy runtime clone or source changed after the reset dry run.", plan.EntryName, out diagnostic);
            entry.runtimeMaterial.CopyPropertiesFromMaterial(entry.originalMaterial);
            entry.baseColorDelivery?.Dispose();
            entry.baseColorDelivery = null;
            diagnostic = default;
            lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>Commits the serialized <see cref="MaterialProxyEntry.configuredValues"/> of the named entry.</summary>
        /// <param name="entryName">The unique entry name to commit.</param>
        /// <param name="diagnostic">The operation result or warning.</param>
        /// <returns><see langword="true"/> when the operation succeeds; otherwise, <see langword="false"/>.</returns>
        public bool TryCommitConfigured(string entryName, out MaterialProxyDiagnostic diagnostic)
        {
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            return TryCommit(entryName, entry.configuredValues, out diagnostic);
        }

        /// <summary>Validates a Normal-only Mesh-route update without mutating the Material.</summary>
        /// <param name="entryName">The unique entry name to update.</param>
        /// <param name="values">The Mesh-route Normal value.</param>
        /// <param name="plan">The immutable plan to pass to <see cref="TryCommitMesh"/>.</param>
        /// <param name="diagnostic">The warning or hard failure diagnostic.</param>
        /// <returns><see langword="true"/> when a plan was created.</returns>
        public bool TryDryRunMesh(string entryName, MaterialProxyMeshValues values, out MaterialProxyMeshDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            plan = null;
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (!values.applyNormalTexture) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Mesh route requires a Normal texture request.", entryName, out diagnostic);
            if (!TryValidateEntry(entry, out Material source, out diagnostic)) return false;
            MaterialProxySemanticApplication application = ApplicationFor(true, entry.adapter.Supports(MaterialProxySemantic.NormalTexture));
            MaterialProxyDiagnostic warning = application == MaterialProxySemanticApplication.Ignored ? MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the Normal texture Mesh semantic; it was ignored.", entryName) : default;
            var proxyValues = new MaterialProxySemanticValues { applyNormalTexture = true, normalTexture = values.normalTexture };
            if (!entry.adapter.TryBuildWritePlan(proxyValues, entry.writeBuffer, out diagnostic)) return Remember(diagnostic, entryName);
            if (!TryValidateWritePlan(entry, source, entry.writeBuffer, out diagnostic)) return false;
            plan = new MaterialProxyMeshDryRunPlan(this, entry, entry.adapter, entry.writeBuffer.ToArray(), application, warning);
            lastDiagnostic = warning;
            return true;
        }

        /// <summary>Commits a previously validated Normal-only Mesh-route plan.</summary>
        /// <param name="plan">A plan returned by <see cref="TryDryRunMesh"/>.</param>
        /// <param name="diagnostic">The warning or hard failure diagnostic.</param>
        /// <returns><see langword="true"/> when the plan was applied or semantically ignored.</returns>
        public bool TryCommitMesh(MaterialProxyMeshDryRunPlan plan, out MaterialProxyDiagnostic diagnostic)
        {
            if (plan == null || plan.owner != this) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy Mesh dry-run plan belongs to another Proxy or is missing.", plan == null ? null : plan.EntryName, out diagnostic);
            if (!TryFindEntry(plan.EntryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (entry != plan.entry || entry.adapter != plan.adapter) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy Mesh entry or adapter changed after the dry run.", plan.EntryName, out diagnostic);
            if (!TryValidateEntry(entry, out Material source, out diagnostic)) return false;
            if (!TryValidateWritePlan(entry, source, plan.assignments, out diagnostic)) return false;
            if (plan.NormalTexture == MaterialProxySemanticApplication.Applied)
            {
                if (!EnsureRuntimeMaterial(entry, source, out diagnostic)) return false;
                for (int i = 0; i < plan.assignments.Length; i++) Apply(entry.runtimeMaterial, plan.assignments[i]);
            }
            diagnostic = plan.Diagnostic;
            lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>Restores only the Normal texture property of a Mesh-route entry to its source Material value.</summary>
        /// <param name="entryName">The unique entry name to restore.</param>
        /// <param name="diagnostic">The operation result.</param>
        /// <returns><see langword="true"/> when the Mesh-route property was restored or no runtime clone exists.</returns>
        /// <remarks>This is a Mesh-route property reset, not an entry release: the runtime Material, original assignment, and retained BaseColor delivery remain owned by this Proxy.</remarks>
        public bool TryRestoreMesh(string entryName, out MaterialProxyDiagnostic diagnostic)
        {
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (entry.runtimeMaterial == null) { diagnostic = default; return true; }
            if (entry.originalMaterial == null) return Fail(MaterialProxyDiagnosticCode.RestoreFailed, "Material Proxy Mesh route cannot locate the original Material.", entryName, out diagnostic);
            IReadOnlyList<MaterialPropertyBindingTemplate> templates = entry.adapter.AssignmentTemplates;
            for (int i = 0; i < templates.Count; i++)
            {
                MaterialPropertyBindingTemplate template = templates[i];
                if (template.valueSource != MaterialPropertyValueSource.NormalTexture || template.writeKind != MaterialPropertyWriteKind.Texture) continue;
                int propertyId = Shader.PropertyToID(template.propertyName);
                if (!entry.runtimeMaterial.HasProperty(propertyId) || !entry.originalMaterial.HasProperty(propertyId)) return Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Normal texture property is not present on the current Material.", entryName, out diagnostic);
                entry.runtimeMaterial.SetTexture(propertyId, entry.originalMaterial.GetTexture(propertyId));
            }
            diagnostic = default;
            lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>Reads explicit adapter-mapped values from an entry's current Material into its configured values.</summary>
        /// <param name="entryName">The unique entry name to read.</param>
        /// <param name="diagnostic">The operation result.</param>
        /// <returns><see langword="true"/> when values were read; otherwise, <see langword="false"/>.</returns>
        public bool TryReadCurrentMaterial(string entryName, out MaterialProxyDiagnostic diagnostic)
        {
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (!TryValidateEntry(entry, out Material material, out diagnostic)) return false;
            if (!entry.adapter.TryBuildReadPlan(entry.readBuffer, out diagnostic)) return Remember(diagnostic, entryName);
            if (!TryValidateReadPlan(entry, material, entry.readBuffer, out diagnostic)) return false;
            if (!entry.adapter.TryReadValues(material, entry.readBuffer, out MaterialProxySemanticValues values, out diagnostic)) return Remember(diagnostic, entryName);

            entry.configuredValues = values;
            diagnostic = default;
            lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>Resolves the current BaseColor texture of an entry without changing Material or delivery state.</summary>
        /// <param name="entryName">The Material Proxy entry whose current texture is required.</param>
        /// <param name="texture">The texture currently assigned to the entry's BaseColor semantic.</param>
        /// <param name="diagnostic">A structured reject when the entry or current texture cannot be resolved.</param>
        /// <returns><see langword="true"/> when a non-null current BaseColor texture was resolved.</returns>
        /// <remarks>This is a read-only binding boundary for mask-only canvas recipes. It does not update configured values, create a clone, retain a delivery, or inspect Texture StackMachine state.</remarks>
        public bool TryGetCurrentBaseColorTexture(string entryName, out Texture texture, out MaterialProxyDiagnostic diagnostic)
        {
            texture = null;
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            if (!TryValidateEntry(entry, out Material material, out diagnostic)) return false;
            if (!entry.adapter.TryBuildReadPlan(entry.readBuffer, out diagnostic)) return Remember(diagnostic, entryName);
            if (!TryValidateReadPlan(entry, material, entry.readBuffer, out diagnostic)) return false;
            if (!entry.adapter.TryReadValues(material, entry.readBuffer, out MaterialProxySemanticValues values, out diagnostic)) return Remember(diagnostic, entryName);
            if (!values.applyBaseColorTexture || values.baseColorTexture == null)
                return Fail(MaterialProxyDiagnosticCode.CurrentTextureUnavailable, "Material Proxy entry has no current BaseColor texture for a canvas binding.", entryName, out diagnostic);
            texture = values.baseColorTexture;
            diagnostic = default;
            lastDiagnostic = diagnostic;
            return true;
        }

        /// <summary>Explicitly releases the named entry by restoring its original Material assignment and destroying its runtime clone.</summary>
        /// <param name="entryName">The unique entry name to restore.</param>
        /// <param name="diagnostic">The operation result.</param>
        /// <returns><see langword="true"/> when the entry is released or was not committed; otherwise, <see langword="false"/>.</returns>
        /// <remarks>This is the public terminal release API. It also disposes an entry-retained BaseColor delivery and is distinct from the internal non-terminal suspend lifecycle.</remarks>
        public bool TryRestore(string entryName, out MaterialProxyDiagnostic diagnostic)
        {
            if (!TryFindEntry(entryName, out MaterialProxyEntry entry, out diagnostic)) return false;
            return TryReleaseEntry(entry, out diagnostic);
        }

        private bool TryFindEntry(string entryName, out MaterialProxyEntry entry, out MaterialProxyDiagnostic diagnostic)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(entryName)) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Entry name is required.", entryName, out diagnostic);
            if (!TryValidateTopology(out diagnostic)) return false;
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null && entries[i].entryName == entryName) { entry = entries[i]; diagnostic = default; return true; }
            return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Material Proxy entry was not found.", entryName, out diagnostic);
        }

        private bool TryValidateTopology(out MaterialProxyDiagnostic diagnostic)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialProxyEntry current = entries[i];
                if (current == null || string.IsNullOrWhiteSpace(current.entryName)) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Each Material Proxy entry requires an entry name.", current == null ? null : current.entryName, out diagnostic);
                for (int j = i + 1; j < entries.Count; j++)
                {
                    MaterialProxyEntry other = entries[j];
                    if (other == null) continue;
                    if (current.entryName == other.entryName) return Fail(MaterialProxyDiagnosticCode.DuplicateEntryName, "Material Proxy entry names must be unique.", current.entryName, out diagnostic);
                    if (current.renderer == other.renderer && current.renderer != null && current.materialChannel == other.materialChannel) return Fail(MaterialProxyDiagnosticCode.DuplicateRendererChannel, "Only one entry may own a renderer material channel.", current.entryName, out diagnostic);
                }
            }

            diagnostic = default;
            return true;
        }

        private bool TryValidateEntry(MaterialProxyEntry entry, out Material source, out MaterialProxyDiagnostic diagnostic)
        {
            source = null;
            if (entry == null || entry.renderer == null) return Fail(MaterialProxyDiagnosticCode.RendererMissing, "Entry renderer is required.", entry == null ? null : entry.entryName, out diagnostic);
            if (entry.adapter == null) return Fail(MaterialProxyDiagnosticCode.AdapterMissing, "Entry adapter is required.", entry.entryName, out diagnostic);
            Material[] current = entry.renderer.sharedMaterials;
            if (entry.materialChannel < 0 || entry.materialChannel >= current.Length) return Fail(MaterialProxyDiagnosticCode.MaterialChannelOutOfRange, "Material channel is outside the renderer material array.", entry.entryName, out diagnostic);
            source = entry.runtimeMaterial != null ? entry.runtimeMaterial : current[entry.materialChannel];
            if (source == null) return Fail(MaterialProxyDiagnosticCode.SourceMaterialMissing, "Source Material is required.", entry.entryName, out diagnostic);
            if (source.shader == null || source.shader.name != entry.adapter.ExpectedShaderName) return Fail(MaterialProxyDiagnosticCode.ShaderMismatch, "Material shader does not match the assigned adapter.", entry.entryName, out diagnostic);
            diagnostic = default;
            return true;
        }

        private bool TryValidateWritePlan(MaterialProxyEntry entry, Material source, IReadOnlyList<MaterialPropertyAssignment> plan, out MaterialProxyDiagnostic diagnostic)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                if (!source.HasProperty(plan[i].PropertyId)) return Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Adapter property is not present on the Material.", entry.entryName, out diagnostic);
            }

            diagnostic = default;
            return true;
        }

        private static MaterialProxyDiagnostic GetUnsupportedSemanticWarning(MaterialProxyEntry entry, MaterialProxySemanticValues values)
        {
            if (values.applyBaseColorTexture && !entry.adapter.Supports(MaterialProxySemantic.BaseColorTexture)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the base-color texture semantic; it was ignored.", entry.entryName);
            if (values.applyNormalTexture && !entry.adapter.Supports(MaterialProxySemantic.NormalTexture)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the normal-texture semantic; it was ignored.", entry.entryName);
            if (values.applyColor && !entry.adapter.Supports(MaterialProxySemantic.Color)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the color semantic; it was ignored.", entry.entryName);
            if (values.applyUvTransform && !entry.adapter.Supports(MaterialProxySemantic.UvTransform)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the UV-transform semantic; it was ignored.", entry.entryName);
            return default;
        }

        private static MaterialProxySemanticApplication ApplicationFor(bool requested, bool supported)
        {
            if (!requested) return MaterialProxySemanticApplication.NotRequested;
            return supported ? MaterialProxySemanticApplication.Applied : MaterialProxySemanticApplication.Ignored;
        }

        private bool TryValidateReadPlan(MaterialProxyEntry entry, Material source, List<MaterialPropertyReadCommand> plan, out MaterialProxyDiagnostic diagnostic)
        {
            var destinations = new HashSet<MaterialPropertyValueSource>();
            for (int i = 0; i < plan.Count; i++)
            {
                if (!destinations.Add(plan[i].Destination) || !source.HasProperty(plan[i].PropertyId)) return Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Adapter read plan is invalid for the current Material.", entry.entryName, out diagnostic);
            }

            diagnostic = default;
            return true;
        }

        private bool EnsureRuntimeMaterial(MaterialProxyEntry entry, Material source, out MaterialProxyDiagnostic diagnostic)
        {
            if (entry.runtimeMaterial != null) { diagnostic = default; return true; }
            Material[] materials = entry.renderer.sharedMaterials;
            entry.originalMaterial = materials[entry.materialChannel];
            entry.runtimeMaterial = new Material(source) { name = source.name + " (ShapeSync Material Proxy)" };
            materials[entry.materialChannel] = entry.runtimeMaterial;
            entry.renderer.sharedMaterials = materials;
            entry.initialized = true;
            diagnostic = default;
            return true;
        }

        /// <summary>Releases every resource owned by one committed entry after restoring its renderer material slot.</summary>
        private bool TryReleaseEntry(MaterialProxyEntry entry, out MaterialProxyDiagnostic diagnostic)
        {
            if (entry == null || entry.runtimeMaterial == null) { diagnostic = default; return true; }
            if (!TryRestoreRendererMaterialSlot(entry, out diagnostic)) return false;
            ReleaseEntryResources(entry);
            diagnostic = default;
            return true;
        }

        private void ReleaseEntryResources(MaterialProxyEntry entry)
        {
            entry.baseColorDelivery?.Dispose();
            entry.baseColorDelivery = null;
            if (Application.isPlaying) Destroy(entry.runtimeMaterial);
            else DestroyImmediate(entry.runtimeMaterial);
            entry.runtimeMaterial = null;
            entry.originalMaterial = null;
            entry.initialized = false;
            entry.suspended = false;
        }

        /// <summary>Restores the renderer slot only. Both terminal release and the later suspend path use this ownership-neutral operation.</summary>
        private bool TryRestoreRendererMaterialSlot(MaterialProxyEntry entry, out MaterialProxyDiagnostic diagnostic)
        {
            if (entry.renderer == null || entry.originalMaterial == null) return Fail(MaterialProxyDiagnosticCode.RestoreFailed, "Material Proxy cannot restore its original material slot.", entry.entryName, out diagnostic);
            Material[] materials = entry.renderer.sharedMaterials;
            if (entry.materialChannel < 0 || entry.materialChannel >= materials.Length) return Fail(MaterialProxyDiagnosticCode.RestoreFailed, "Material Proxy cannot restore its original material slot.", entry.entryName, out diagnostic);
            materials[entry.materialChannel] = entry.originalMaterial;
            entry.renderer.sharedMaterials = materials;
            diagnostic = default;
            return true;
        }

        private void SuspendAll()
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null) TrySuspendEntry(entries[i], out _);
        }

        private bool TrySuspendEntry(MaterialProxyEntry entry, out MaterialProxyDiagnostic diagnostic)
        {
            if (entry == null || entry.runtimeMaterial == null || entry.suspended) { diagnostic = default; return true; }
            if (TryRestoreRendererMaterialSlot(entry, out diagnostic))
            {
                entry.suspended = true;
                return true;
            }

            if (IsRuntimeMaterialStillAssigned(entry))
            {
                return ReportEscrowStale("Material Proxy could not suspend its entry because its original Material is missing while the renderer still references the runtime Material; the entry remains assigned until terminal cleanup.", entry.entryName, out diagnostic);
            }

            ReleaseEntryResources(entry);
            return ReportEscrowStale("Material Proxy could not suspend its entry because the renderer material slot is structurally stale; its escrow was released for normal re-derivation.", entry.entryName, out diagnostic);
        }

        private void ResumeAll()
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i] != null) TryResumeEntry(entries[i], out _);
        }

        private bool TryResumeEntry(MaterialProxyEntry entry, out MaterialProxyDiagnostic diagnostic)
        {
            if (entry == null || !entry.suspended) { diagnostic = default; return true; }
            if (!TryValidateSuspendedEscrow(entry, out string reason))
            {
                ReleaseEntryResources(entry);
                return ReportEscrowStale(reason, entry.entryName, out diagnostic);
            }

            Material[] materials = entry.renderer.sharedMaterials;
            materials[entry.materialChannel] = entry.runtimeMaterial;
            entry.renderer.sharedMaterials = materials;
            entry.suspended = false;
            diagnostic = default;
            return true;
        }

        private static bool TryValidateSuspendedEscrow(MaterialProxyEntry entry, out string reason)
        {
            if (entry.renderer == null) { reason = "Material Proxy cannot resume a suspended entry because its renderer is missing."; return false; }
            if (entry.originalMaterial == null) { reason = "Material Proxy cannot resume a suspended entry because its original Material is missing."; return false; }
            if (entry.runtimeMaterial == null) { reason = "Material Proxy cannot resume a suspended entry because its runtime Material was destroyed."; return false; }
            Material[] materials = entry.renderer.sharedMaterials;
            if (entry.materialChannel < 0 || entry.materialChannel >= materials.Length) { reason = "Material Proxy cannot resume a suspended entry because its material channel is outside the renderer material array."; return false; }
            if (materials[entry.materialChannel] != entry.originalMaterial) { reason = "Material Proxy cannot resume a suspended entry because its renderer material slot changed while suspended."; return false; }
            if (entry.baseColorDelivery != null && entry.baseColorDelivery.Texture == null) { reason = "Material Proxy cannot resume a suspended entry because its retained BaseColor delivery texture was destroyed."; return false; }
            reason = null;
            return true;
        }

        private static bool IsRuntimeMaterialStillAssigned(MaterialProxyEntry entry)
        {
            if (entry.renderer == null || entry.runtimeMaterial == null || entry.materialChannel < 0) return false;
            Material[] materials = entry.renderer.sharedMaterials;
            return entry.materialChannel < materials.Length && materials[entry.materialChannel] == entry.runtimeMaterial;
        }

        /// <summary>Best-effort restores renderer slots, then releases all entry-owned resources during terminal destruction.</summary>
        private void ForceReleaseAll()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialProxyEntry entry = entries[i];
                if (entry == null) continue;
                TryRestoreRendererMaterialSlot(entry, out _);
                ReleaseEntryResources(entry);
            }
        }

        private static void Apply(Material material, MaterialPropertyAssignment assignment)
        {
            switch (assignment.WriteKind)
            {
                case MaterialPropertyWriteKind.Texture: material.SetTexture(assignment.PropertyId, assignment.Texture); break;
                case MaterialPropertyWriteKind.Color: material.SetColor(assignment.PropertyId, assignment.Color); break;
                case MaterialPropertyWriteKind.TextureScale: material.SetTextureScale(assignment.PropertyId, assignment.Vector2); break;
                case MaterialPropertyWriteKind.TextureOffset: material.SetTextureOffset(assignment.PropertyId, assignment.Vector2); break;
            }
        }

        private bool TryValidateValues(string entryName, MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            if (values.applyColor && (!Finite(values.color.r) || !Finite(values.color.g) || !Finite(values.color.b) || !Finite(values.color.a) || values.color.r < 0f || values.color.r > 1f || values.color.g < 0f || values.color.g > 1f || values.color.b < 0f || values.color.b > 1f || values.color.a < 0f || values.color.a > 1f)) return Fail(MaterialProxyDiagnosticCode.InvalidColor, "Color must be finite Linear RGBA in [0, 1].", entryName, out diagnostic);
            if (values.applyUvTransform && (!Finite(values.uvScale.x) || !Finite(values.uvScale.y) || !Finite(values.uvOffset.x) || !Finite(values.uvOffset.y))) return Fail(MaterialProxyDiagnosticCode.InvalidUvTransform, "UV transform values must be finite.", entryName, out diagnostic);
            diagnostic = default;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private bool Remember(MaterialProxyDiagnostic diagnostic, string entryName) { diagnostic.entryName = entryName; lastDiagnostic = diagnostic; return false; }
        private bool Fail(MaterialProxyDiagnosticCode code, string message, string entryName, out MaterialProxyDiagnostic diagnostic) { diagnostic = MaterialProxyDiagnostic.Fail(code, message, entryName); lastDiagnostic = diagnostic; return false; }
        private bool ReportEscrowStale(string message, string entryName, out MaterialProxyDiagnostic diagnostic) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.EscrowStale, message, entryName); lastDiagnostic = diagnostic; return false; }
    }
}
