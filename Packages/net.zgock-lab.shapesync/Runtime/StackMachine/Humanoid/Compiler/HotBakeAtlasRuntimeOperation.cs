// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Caller-pumped Runtime Atlas phase for one successful Hot Bake candidate.</summary>
    /// <remarks>The Schema is copied on admission. It never mutates source assets, and completed pages transfer only through <see cref="AtlasCandidateApplicator"/>.</remarks>
    internal sealed class HotBakeAtlasRuntimeOperation : IDisposable
    {
        private readonly InMemoryHumanoidMesh candidate;
        private readonly AtlasBakerOperation logicalOperation;
        private readonly AtlasBakerResult logicalResult;
        private readonly AtlasBakerExecutionOperation executionOperation;
        private bool disposed;

        private HotBakeAtlasRuntimeOperation(InMemoryHumanoidMesh candidate, AtlasBakerOperation logicalOperation, AtlasBakerResult logicalResult, AtlasBakerExecutionOperation executionOperation)
        {
            this.candidate = candidate;
            this.logicalOperation = logicalOperation;
            this.logicalResult = logicalResult;
            this.executionOperation = executionOperation;
        }

        /// <summary>Creates the logical Atlas plan and its PlayMode page executor for the final candidate.</summary>
        internal static bool TryCreate(AtlasSchema schemaAsset, InMemoryHumanoidMesh candidate, TextureStackMachineHost host, out HotBakeAtlasRuntimeOperation operation, out StackMachineDiagnostic diagnostic)
        {
            operation = null;
            diagnostic = null;
            if (schemaAsset == null) return Reject("HotBakeAtlasSchemaRequired", "Hot Bake Atlas execution requires an AtlasSchema input.", out diagnostic);
            if (candidate == null || candidate.Mesh == null) return Reject("AtlasCandidateRequired", "Hot Bake Atlas execution requires a successful in-memory candidate.", out diagnostic);
            if (host == null) return Reject("HotBakeAtlasHostRequired", "Hot Bake Atlas execution requires a live TextureStackMachineHost.", out diagnostic);
            AtlasSchemaDocument schema = schemaAsset.ToDocument();
            if (!AtlasSchemaValidation.TryValidate(schema, out diagnostic)) return false;
            if (!host.TryInitialize(out diagnostic)) return false;
            if (!TryCreateInputs(candidate, out List<AtlasBakerMaterialInput> inputs, out diagnostic)) return false;

            var logical = new AtlasBakerOperation(schema, schema.ValidationIdentity.Clone(), inputs);
            if (logical.Pump() != AtlasBakerOperationStatus.Succeeded)
            {
                diagnostic = logical.Diagnostic;
                logical.Dispose();
                return false;
            }
            if (!logical.TryTakeResult(out AtlasBakerResult result, out diagnostic))
            {
                logical.Dispose();
                return false;
            }

            IAtlasBakerPageExecutor executor = result.Pages.Count == 0 ? null : new PlayModeAtlasBakerPageExecutor(host);
            operation = new HotBakeAtlasRuntimeOperation(candidate, logical, result, new AtlasBakerExecutionOperation(result, executor));
            return true;
        }

        /// <summary>Pumps one page-execution step and applies all completed pages atomically on success.</summary>
        internal HumanoidBuildOperationStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Fail("HotBakeAtlasOperationDisposed", "Hot Bake Atlas operation has been disposed.", out diagnostic);
            AtlasBakerExecutionStatus status = executionOperation.Pump();
            if (status == AtlasBakerExecutionStatus.Pending) return HumanoidBuildOperationStatus.Pending;
            if (status != AtlasBakerExecutionStatus.Succeeded)
            {
                diagnostic = executionOperation.Diagnostic;
                return status == AtlasBakerExecutionStatus.Cancelled ? HumanoidBuildOperationStatus.Cancelled : HumanoidBuildOperationStatus.Failed;
            }
            if (!executionOperation.TryTakeResult(out AtlasBakerExecutionResult executionResult, out diagnostic)) return HumanoidBuildOperationStatus.Failed;
            try
            {
                if (!AtlasCandidateApplicator.TryApply(candidate, logicalResult, executionResult, out diagnostic)) return HumanoidBuildOperationStatus.Failed;
                return HumanoidBuildOperationStatus.Succeeded;
            }
            finally { executionResult.Dispose(); }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            executionOperation?.Dispose();
            logicalOperation?.Dispose();
        }

        private static bool TryCreateInputs(InMemoryHumanoidMesh candidate, out List<AtlasBakerMaterialInput> inputs, out StackMachineDiagnostic diagnostic)
        {
            inputs = new List<AtlasBakerMaterialInput>();
            diagnostic = null;
            if (candidate.Materials.Count != candidate.MaterialSlots.Count)
                return Reject("AtlasCandidateMaterialSlotMismatch", "Hot Bake Atlas execution requires one candidate Material for every source slot.", out diagnostic);
            for (int index = 0; index < candidate.MaterialSlots.Count; index++)
            {
                HumanoidBuildMaterialSlot slot = candidate.MaterialSlots[index];
                Material material = candidate.Materials[index];
                if (!slot.MaterialId.IsValid || slot.Adapter == null || material == null)
                    return Reject("AtlasCandidateMaterialInvalid", "Hot Bake Atlas execution requires a valid candidate Material and adapter for every slot.", out diagnostic);
                if (!slot.Adapter.TryGetAtlasBaseColorTransform(material, out string baseColorProperty, out _, out _, out MaterialProxyDiagnostic baseColorDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", baseColorDiagnostic.message, out diagnostic);
                var readPlan = new List<MaterialPropertyReadCommand>();
                if (!slot.Adapter.TryBuildReadPlan(readPlan, out MaterialProxyDiagnostic readPlanDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", readPlanDiagnostic.message, out diagnostic);
                if (!slot.Adapter.TryReadValues(material, readPlan, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic readDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", readDiagnostic.message, out diagnostic);
                var normalTextureProperties = new List<string>();
                if (!slot.Adapter.TryGetPublishTextureProperties(MaterialProxySemantic.NormalTexture, normalTextureProperties, out MaterialProxyDiagnostic normalPropertyDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", normalPropertyDiagnostic.message, out diagnostic);
                bool normalIsNeutral = false;
                if (values.normalTexture != null && normalTextureProperties.Count != 0
                    && !slot.Adapter.TryGetEffectiveNeutralTexture(material, normalTextureProperties[0], values.normalTexture, out normalIsNeutral, out MaterialProxyDiagnostic neutralDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", neutralDiagnostic.message, out diagnostic);
                inputs.Add(new AtlasBakerMaterialInput(slot.MaterialId, material.GetTexture(baseColorProperty), values.normalTexture, normalIsNeutral));
            }
            return true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }

        private static HumanoidBuildOperationStatus Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return HumanoidBuildOperationStatus.Failed;
        }
    }
}
