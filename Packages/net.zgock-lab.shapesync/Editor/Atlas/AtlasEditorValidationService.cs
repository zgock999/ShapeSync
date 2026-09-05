// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor.Atlas
{
    /// <summary>Runs non-mutating Atlas Editor Dry Run validation for the listed candidate snapshot.</summary>
    public static class AtlasEditorValidationService
    {
        /// <summary>Validates current editor input and records a successful verification only after every check succeeds.</summary>
        public static bool TryDryRun(AtlasEditorState state, out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic)
        {
            layout = null;
            if (state == null || state.Snapshot == null) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorEntriesRequired", "Atlas Editor requires listed entries before Dry Run."); return false; }
            AtlasSchemaDocument document = CreateDocument(state);
            if (!AtlasLayoutOracle.Solve(document, out layout, out diagnostic)) { state.MarkDryRunFailed(); return false; }
            var targetsByMesh = new Dictionary<Mesh, List<AtlasMeshValidator.Target>>();
            for (int i = 0; i < state.Entries.Count; i++)
            {
                AtlasEditorEntryState entry = state.Entries[i]; if (entry.Excluded) continue;
                MaterialProxyEntry binding = entry.Candidate.ValidationBinding; Mesh mesh = binding?.renderer?.sharedMesh;
                if (mesh == null) { state.MarkDryRunFailed(); diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorMeshRequired", "Atlas Editor candidate requires a readable SkinnedMeshRenderer mesh.", detail: "owner=" + entry.Candidate.Owner + ";materialId=" + entry.Candidate.MaterialId); return false; }
                if (!AtlasEditorMaterialSourceResolver.TryResolve(binding, out Material material, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic sourceDiagnostic))
                {
                    state.MarkDryRunFailed();
                    diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorMaterialReadRejected", sourceDiagnostic.message, detail: "owner=" + entry.Candidate.Owner + ";materialId=" + entry.Candidate.MaterialId + ";cause=" + sourceDiagnostic.code);
                    return false;
                }
                var normalTextureProperties = new List<string>();
                if (!binding.adapter.TryGetPublishTextureProperties(MaterialProxySemantic.NormalTexture, normalTextureProperties, out MaterialProxyDiagnostic normalPropertyDiagnostic))
                {
                    state.MarkDryRunFailed();
                    diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorMaterialReadRejected", normalPropertyDiagnostic.message, detail: "owner=" + entry.Candidate.Owner + ";materialId=" + entry.Candidate.MaterialId + ";cause=" + normalPropertyDiagnostic.code);
                    return false;
                }
                string normalTexturePropertyName = normalTextureProperties.Count == 0 ? null : normalTextureProperties[0];
                if (!layout.TryGetCell(entry.Candidate.MaterialId, out AtlasLayoutCell cell)) { state.MarkDryRunFailed(); diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorLayoutCellRequired", "Atlas Editor layout did not assign an included candidate.", detail: "materialId=" + entry.Candidate.MaterialId); return false; }
                if (!targetsByMesh.TryGetValue(mesh, out List<AtlasMeshValidator.Target> targets)) { targets = new List<AtlasMeshValidator.Target>(); targetsByMesh.Add(mesh, targets); }
                targets.Add(new AtlasMeshValidator.Target(entry.Candidate.Owner, entry.Candidate.MaterialId, binding.materialChannel, false,
                    material, binding.adapter, values.baseColorTexture, values.normalTexture, cell.PageIndex,
                    values.applyUvTransform, values.uvScale, values.uvOffset, normalTexturePropertyName));
            }
            foreach (KeyValuePair<Mesh, List<AtlasMeshValidator.Target>> pair in targetsByMesh)
                if (!AtlasMeshValidator.TryValidateResolved(pair.Key, pair.Value, out diagnostic)) { state.MarkDryRunFailed(); layout = null; return false; }
            state.SetLayoutPreview(layout); state.TryMarkDryRunSucceeded(out _); diagnostic = null; return true;
        }

        internal static AtlasSchemaDocument CreateDocument(AtlasEditorState state)
        {
            var entries = new List<AtlasSchemaEntry>(); var identities = new List<AtlasSourceMaterialIdentity>();
            for (int i = 0; i < state.Entries.Count; i++)
            {
                AtlasEditorEntryState entry = state.Entries[i];
                entries.Add(new AtlasSchemaEntry(entry.Candidate.MaterialId, entry.PageGroupingKey, entry.Excluded ? 0 : entry.CellLevelX, entry.Excluded ? 0 : entry.CellLevelY, entry.Excluded));
                identities.Add(new AtlasSourceMaterialIdentity(entry.Candidate.MaterialId, entry.Candidate.SourceMaterialIdentity));
            }
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, state.PageExtent, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity(state.Snapshot.FigureIdentity, state.Snapshot.DocumentIdentity, identities), entries);
        }
    }

    /// <summary>Resolves the effective semantic values from a current Material without mutating MaterialProxy authoring data.</summary>
    internal static class AtlasEditorMaterialSourceResolver
    {
        /// <summary>Reads the current renderer Material through its adapter's read plan.</summary>
        internal static bool TryResolve(MaterialProxyEntry binding, out Material material, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            material = null;
            values = default;
            diagnostic = default;
            if (binding == null) return Fail(MaterialProxyDiagnosticCode.ProxyNotReady, "Atlas Editor candidate has no MaterialProxy entry.", out diagnostic);
            if (binding.renderer == null) return Fail(MaterialProxyDiagnosticCode.RendererMissing, "Atlas Editor candidate has no renderer.", out diagnostic);
            Material[] materials = binding.renderer.sharedMaterials;
            if (binding.materialChannel < 0 || binding.materialChannel >= materials.Length)
                return Fail(MaterialProxyDiagnosticCode.MaterialChannelOutOfRange, "Atlas Editor candidate Material channel is outside the renderer material array.", out diagnostic);
            material = materials[binding.materialChannel];
            if (material == null) return Fail(MaterialProxyDiagnosticCode.SourceMaterialMissing, "Atlas Editor candidate has no current source Material.", out diagnostic);
            if (binding.adapter == null) return Fail(MaterialProxyDiagnosticCode.AdapterMissing, "Atlas Editor candidate has no Material Shader Adapter.", out diagnostic);
            // Keep this as an intentional adapter gate even though the transform outputs are
            // consumed later by AtlasMeshValidator: an Atlas candidate must expose the same
            // BaseColor transform contract before its semantic read plan can be trusted.
            if (!binding.adapter.TryGetAtlasBaseColorTransform(material, out _, out _, out _, out diagnostic)) return false;
            var readPlan = new List<MaterialPropertyReadCommand>();
            if (!binding.adapter.TryBuildReadPlan(readPlan, out diagnostic)) return false;
            if (!binding.adapter.TryReadValues(material, readPlan, out values, out diagnostic)) return false;

            // Each apply flag is an independent authoring override. Database-generated figures
            // leave the payload at its default, so the current material values above remain the
            // effective fallback without mutating the proxy configuration.
            MaterialProxySemanticValues configured = binding.configuredValues;
            if (configured.applyBaseColorTexture)
            {
                values.applyBaseColorTexture = true;
                values.baseColorTexture = configured.baseColorTexture;
            }
            if (configured.applyNormalTexture)
            {
                values.applyNormalTexture = true;
                values.normalTexture = configured.normalTexture;
            }
            if (configured.applyColor)
            {
                values.applyColor = true;
                values.color = configured.color;
            }
            if (configured.applyUvTransform)
            {
                values.applyUvTransform = true;
                values.uvScale = configured.uvScale;
                values.uvOffset = configured.uvOffset;
            }

            return true;
        }

        /// <summary>Chooses the BaseColor texture for the row display, falling back to the resolved Normal texture.</summary>
        internal static Texture GetDisplaySourceTexture(MaterialProxySemanticValues values)
            => values.baseColorTexture != null ? values.baseColorTexture : values.normalTexture;

        private static bool Fail(MaterialProxyDiagnosticCode code, string message, out MaterialProxyDiagnostic diagnostic)
        {
            diagnostic = MaterialProxyDiagnostic.Fail(code, message);
            return false;
        }
    }
}
