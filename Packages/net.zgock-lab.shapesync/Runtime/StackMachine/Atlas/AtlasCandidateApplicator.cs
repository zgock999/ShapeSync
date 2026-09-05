// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Applies completed in-memory Atlas pages to an already cloned Humanoid candidate.</summary>
    /// <remarks>This class never touches source Meshes, source Materials, assets, or publishing. Its caller retains the execution result on rejection.</remarks>
    public static class AtlasCandidateApplicator
    {
        /// <summary>Atomically remaps target UV0 and replaces target candidate Material semantics with completed page textures.</summary>
        public static bool TryApply(InMemoryHumanoidMesh candidate, AtlasBakerResult logical, AtlasBakerExecutionResult execution, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (candidate == null || candidate.Mesh == null) return Reject("AtlasCandidateRequired", "Atlas application requires an in-memory candidate Mesh.", out diagnostic);
            if (logical == null || execution == null) return Reject("AtlasExecutionResultRequired", "Atlas application requires logical and executed Atlas results.", out diagnostic);
            if (candidate.Materials.Count != candidate.MaterialSlots.Count) return Reject("AtlasCandidateMaterialSlotMismatch", "Atlas candidate material and slot counts differ.", out diagnostic);
            if (candidate.AtlasPages != null) return Reject("AtlasCandidatePagesAlreadyApplied", "Atlas pages are already attached to the in-memory candidate.", out diagnostic);

            var pages = new Dictionary<string, AtlasBakerPageCompletion>();
            for (int i = 0; i < execution.Pages.Count; i++)
            {
                AtlasBakerPageCompletion page = execution.Pages[i];
                if (page == null || page.Texture == null || !pages.TryAdd(Key(page.PageIndex, page.Semantic), page))
                    return Reject("AtlasPageCompletionInvalid", "Atlas execution contains an invalid or duplicate page completion.", out diagnostic);
            }

            var baseTargets = new Dictionary<MaterialId, AtlasBakerPageCompletion>();
            var normalTargets = new Dictionary<MaterialId, AtlasBakerPageCompletion>();
            for (int i = 0; i < logical.Pages.Count; i++)
            {
                AtlasBakerPagePlan plan = logical.Pages[i];
                if (!pages.TryGetValue(Key(plan.PageIndex, plan.Semantic), out AtlasBakerPageCompletion page))
                    return Reject("AtlasPageCompletionMissing", "Atlas execution is missing a logical page completion.", out diagnostic);
                for (int j = 0; j < plan.Operations.Count; j++)
                {
                    AtlasBakerPageOperation operation = plan.Operations[j];
                    if (operation.Kind != AtlasBakerPageOperationKind.Place) continue;
                    (plan.Semantic == AtlasTextureSemantic.BaseColor ? baseTargets : normalTargets).Add(operation.MaterialId, page);
                }
            }
            if (pages.Count != logical.Pages.Count)
                return Reject("AtlasPageCompletionUnexpected", "Atlas execution contains a page not present in the logical result.", out diagnostic);

            var candidateTargetIds = new HashSet<MaterialId>();
            for (int i = 0; i < candidate.MaterialSlots.Count; i++) candidateTargetIds.Add(candidate.MaterialSlots[i].MaterialId);
            foreach (MaterialId materialId in baseTargets.Keys)
                if (!candidateTargetIds.Contains(materialId))
                    return Reject("AtlasCandidateTargetMissing", "Atlas logical target is absent from the in-memory candidate.", out diagnostic);

            // A valid Schema may exclude every entry, or may only name MaterialIds absent from
            // this particular candidate.  In that case Atlas is a deliberate no-op: do not invoke
            // the mesh validator (which correctly requires a non-empty validation target set), and
            // do not attach an empty carrier or take ownership of the empty execution result.
            if (baseTargets.Count == 0) return true;

            Mesh mesh = candidate.Mesh;
            if (!TryValidateResolvedTargets(candidate, baseTargets, logical.Layout, out diagnostic)) return false;
            Vector2[] sourceUv = mesh.uv;
            Vector2[] remapped = (Vector2[])sourceUv.Clone();
            var applications = new List<Application>();
            for (int i = 0; i < candidate.MaterialSlots.Count; i++)
            {
                HumanoidBuildMaterialSlot slot = candidate.MaterialSlots[i];
                if (!baseTargets.TryGetValue(slot.MaterialId, out AtlasBakerPageCompletion basePage)) continue;
                if (!logical.Layout.TryGetCell(slot.MaterialId, out AtlasLayoutCell cell) || slot.Adapter == null || candidate.Materials[i] == null)
                    return Reject("AtlasCandidateTargetInvalid", "Atlas target has no resolved cell, adapter, or candidate Material.", out diagnostic);
                if (!slot.Adapter.TryGetAtlasBaseColorTransform(candidate.Materials[i], out _, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic adapterDiagnostic))
                    return Reject("AtlasCandidateTransformRejected", adapterDiagnostic.message, out diagnostic);
                int[] triangles = mesh.GetTriangles(slot.SubmeshIndex);
                for (int j = 0; j < triangles.Length; j++) remapped[triangles[j]] = AtlasUvTransform.Apply(sourceUv[triangles[j]], scale, offset, cell, logical.Layout.PageExtent);
                normalTargets.TryGetValue(slot.MaterialId, out AtlasBakerPageCompletion normalPage);
                if (normalPage != null && !slot.Adapter.Supports(MaterialProxySemantic.NormalTexture))
                    return Reject("AtlasCandidateNormalSemanticUnsupported", "Atlas has a Normal page but the candidate Material adapter does not support the Normal semantic.", out diagnostic);
                applications.Add(new Application(candidate.Materials[i], slot.Adapter, basePage, normalPage));
            }

            var replacedOwnedTextures = new HashSet<Texture>();
            for (int i = 0; i < applications.Count; i++)
            {
                Application application = applications[i];
                var values = Values(application);
                var writePlan = new List<MaterialPropertyAssignment>();
                if (!application.Adapter.TryBuildWritePlan(values, writePlan, out MaterialProxyDiagnostic validationDiagnostic))
                    return Reject("AtlasCandidateMaterialApplyRejected", validationDiagnostic.message, out diagnostic);
                for (int j = 0; j < writePlan.Count; j++)
                    if (!application.Material.HasProperty(writePlan[j].PropertyId))
                        return Reject("AtlasCandidateMaterialApplyRejected", "Atlas candidate Material is missing a planned shader property.", out diagnostic);
                foreach (MaterialPropertyBindingTemplate binding in application.Adapter.AssignmentTemplates)
                {
                    if (binding.writeKind != MaterialPropertyWriteKind.Texture ||
                        (binding.valueSource != MaterialPropertyValueSource.BaseColorTexture && binding.valueSource != MaterialPropertyValueSource.NormalTexture))
                        continue;
                    Texture previous = application.Material.GetTexture(binding.propertyName);
                    if (previous is RenderTexture) replacedOwnedTextures.Add(previous);
                }
            }
            for (int i = 0; i < applications.Count; i++)
            {
                Application application = applications[i];
                var values = Values(application);
                if (!application.Adapter.TryApplyInMemory(application.Material, values, out _, out MaterialProxyDiagnostic adapterDiagnostic))
                    return Reject("AtlasCandidateMaterialApplyRejected", adapterDiagnostic.message, out diagnostic);
            }
            mesh.uv = remapped;
            foreach (Texture texture in replacedOwnedTextures) candidate.TryReleaseOwnedTexture(texture);
            candidate.SetAtlasPages(new AtlasBakerCandidatePages(execution.DetachPages()));
            return true;
        }

        private static bool TryValidateResolvedTargets(InMemoryHumanoidMesh candidate, Dictionary<MaterialId, AtlasBakerPageCompletion> baseTargets, AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic)
        {
            var targets = new List<AtlasMeshValidator.Target>();
            var readPlan = new List<MaterialPropertyReadCommand>();
            for (int i = 0; i < candidate.MaterialSlots.Count; i++)
            {
                HumanoidBuildMaterialSlot slot = candidate.MaterialSlots[i];
                if (!baseTargets.ContainsKey(slot.MaterialId)) continue;
                if (!layout.TryGetCell(slot.MaterialId, out AtlasLayoutCell cell) || slot.Adapter == null || candidate.Materials[i] == null)
                    return Reject("AtlasCandidateTargetInvalid", "Atlas target has no resolved cell, adapter, or candidate Material.", out diagnostic);
                if (!slot.Adapter.TryGetAtlasBaseColorTransform(candidate.Materials[i], out _, out _, out _, out MaterialProxyDiagnostic transformDiagnostic))
                    return Reject("AtlasCandidateValidationReadRejected", transformDiagnostic.message, out diagnostic);
                if (!slot.Adapter.TryBuildReadPlan(readPlan, out MaterialProxyDiagnostic planDiagnostic))
                    return Reject("AtlasCandidateValidationReadRejected", planDiagnostic.message, out diagnostic);
                if (!slot.Adapter.TryReadValues(candidate.Materials[i], readPlan, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic valuesDiagnostic))
                    return Reject("AtlasCandidateValidationReadRejected", valuesDiagnostic.message, out diagnostic);
                var normalTextureProperties = new List<string>();
                if (!slot.Adapter.TryGetPublishTextureProperties(MaterialProxySemantic.NormalTexture, normalTextureProperties, out MaterialProxyDiagnostic normalPropertyDiagnostic))
                    return Reject("AtlasCandidateValidationReadRejected", normalPropertyDiagnostic.message, out diagnostic);
                targets.Add(new AtlasMeshValidator.Target(slot.MaterialId.RegistryId, slot.MaterialId, slot.SubmeshIndex, false,
                    candidate.Materials[i], slot.Adapter, values.baseColorTexture, values.normalTexture, cell.PageIndex,
                    values.applyUvTransform, values.uvScale, values.uvOffset,
                    normalTextureProperties.Count == 0 ? null : normalTextureProperties[0]));
            }
            return AtlasMeshValidator.TryValidateResolved(candidate.Mesh, targets, out diagnostic);
        }
        private static MaterialProxySemanticValues Values(Application application) => new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = application.Base.Texture, applyNormalTexture = application.Normal != null, normalTexture = application.Normal?.Texture, applyUvTransform = true, uvScale = Vector2.one, uvOffset = Vector2.zero };

        private readonly struct Application
        {
            internal Application(Material material, MaterialShaderAdapter adapter, AtlasBakerPageCompletion @base, AtlasBakerPageCompletion normal) { Material = material; Adapter = adapter; Base = @base; Normal = normal; }
            internal Material Material { get; }
            internal MaterialShaderAdapter Adapter { get; }
            internal AtlasBakerPageCompletion Base { get; }
            internal AtlasBakerPageCompletion Normal { get; }
        }
        private static string Key(int page, AtlasTextureSemantic semantic) => page + ":" + (int)semantic;
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message); return false; }
    }
}
