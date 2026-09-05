// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Editor-only concrete backend for the UnityEditor-independent Humanoid Compiler seam.
    /// It owns caller-driven EditMode Machine start, pump, take, and pre-carrier escrow cleanup;
    /// Compiler Core owns candidate Material construction and semantic application.
    /// </summary>
    internal sealed class EditModeHumanoidBuildBackend : IHumanoidBuildBackend
    {
        private readonly IEditModeMeshBuildPhaseMachine meshMachine;
        private readonly IEditModeMaterialBuildPhaseMachine materialMachine;
        private HumanoidBuildSource source;
        private bool meshActive;
        private bool materialActive;

        internal EditModeHumanoidBuildBackend(TextureEditModeStackMachine normalTextureMachine, TextureEditModeStackMachine materialTextureMachine)
            : this(new EditModeMeshStackMachine(normalTextureMachine, true), new EditModeMaterialStackMachine(materialTextureMachine)) { }

        internal EditModeHumanoidBuildBackend(IEditModeMeshBuildPhaseMachine meshMachine, IEditModeMaterialBuildPhaseMachine materialMachine)
        {
            this.meshMachine = meshMachine;
            this.materialMachine = materialMachine;
        }

        public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (meshMachine == null) return Reject("EditModeMeshMachineRequired", "EditMode Humanoid backend requires an EditMode Mesh StackMachine.", out diagnostic);
            if (meshActive || materialActive) return Reject("EditModeHumanoidBackendBusy", "Cancel the active EditMode Humanoid build before beginning another Mesh phase.", out diagnostic);
            if (!meshMachine.Start(source.FigureRoot, source.Document, out diagnostic)) return false;
            this.source = source;
            meshActive = true;
            return true;
        }

        public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic)
        {
            meshPayload = null;
            diagnostic = null;
            if (!meshActive) return Fail("EditModeMeshPhaseInactive", "EditMode Humanoid backend Mesh phase is not active.", out diagnostic);

            EditModeMeshExecutionStatus status = meshMachine.Pump(out diagnostic);
            if (status == EditModeMeshExecutionStatus.Pending) return HumanoidBuildPhaseStatus.Pending;
            if (status == EditModeMeshExecutionStatus.Cancelled) { meshActive = false; source = default; return HumanoidBuildPhaseStatus.Cancelled; }
            if (status != EditModeMeshExecutionStatus.Succeeded) { meshActive = false; source = default; return HumanoidBuildPhaseStatus.Failed; }

            if (!meshMachine.TryTakeResult(out EditModeMeshBuildResult result))
            {
                meshActive = false;
                source = default;
                return Fail("EditModeMeshResultMissing", "EditMode Mesh StackMachine succeeded without a single-take result.", out diagnostic);
            }

            meshActive = false;
            bool converted = false;
            try
            {
                if (!EditModeHumanoidMeshPayloadBuilder.TryCreate(result, out meshPayload, out diagnostic)) return HumanoidBuildPhaseStatus.Failed;
                converted = true;
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            finally { result.Dispose(); if (!converted) source = default; }
        }

        public bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (materialMachine == null) return Reject("EditModeMaterialMachineRequired", "EditMode Humanoid backend requires an EditMode Material StackMachine.", out diagnostic);
            if (meshActive || materialActive) return Reject("EditModeHumanoidBackendBusy", "EditMode Humanoid backend already has an active phase.", out diagnostic);
            if (source.FigureRoot == null || source.Document == null) return Reject("EditModeBuildSourceMissing", "EditMode Humanoid backend has no accepted Mesh source for Material execution.", out diagnostic);
            if (meshPayload == null || meshPayload.Mesh == null) return Reject("EditModeMeshPayloadMissing", "EditMode Material execution requires the accepted Mesh payload.", out diagnostic);
            if (!materialMachine.Start(source.FigureRoot, source.Document, out diagnostic)) return false;
            materialActive = true;
            return true;
        }

        public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic diagnostic)
        {
            materialPayload = null;
            diagnostic = null;
            if (!materialActive) return Fail("EditModeMaterialPhaseInactive", "EditMode Humanoid backend Material phase is not active.", out diagnostic);

            EditModeMaterialExecutionStatus status = materialMachine.Pump(out diagnostic);
            if (status == EditModeMaterialExecutionStatus.Pending) return HumanoidBuildPhaseStatus.Pending;
            if (status == EditModeMaterialExecutionStatus.Cancelled) { materialActive = false; source = default; return HumanoidBuildPhaseStatus.Cancelled; }
            if (status != EditModeMaterialExecutionStatus.Succeeded) { materialActive = false; source = default; return HumanoidBuildPhaseStatus.Failed; }

            if (!materialMachine.TryTakeResult(out EditModeMaterialBuildResult result))
            {
                materialActive = false;
                source = default;
                return Fail("EditModeMaterialResultMissing", "EditMode Material StackMachine succeeded without a single-take result.", out diagnostic);
            }

            materialActive = false;
            try
            {
                if (!EditModeHumanoidMaterialPayloadBuilder.TryCreate(result, out materialPayload, out diagnostic)) return HumanoidBuildPhaseStatus.Failed;
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            finally { result.Dispose(); source = default; }
        }

        public void Cancel()
        {
            if (meshActive) meshMachine?.Cancel();
            if (materialActive) materialMachine?.Cancel();
            meshActive = false;
            materialActive = false;
            source = default;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }

        private static HumanoidBuildPhaseStatus Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return HumanoidBuildPhaseStatus.Failed;
        }
    }

    /// <summary>Converts one taken EditMode Mesh result into the public detached Compiler carrier.</summary>
    internal static class EditModeHumanoidMeshPayloadBuilder
    {
        internal static bool TryCreate(EditModeMeshBuildResult result, out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic)
        {
            payload = null;
            diagnostic = null;
            if (result == null || result.Mesh == null) return Reject("EditModeFinalMeshMissing", "EditMode Mesh result has no final Mesh to transfer.", out diagnostic);

            IReadOnlyList<HumanoidMeshMaterialSlot> sourceSlots = result.MaterialSlots;
            var slots = new HumanoidBuildMaterialSlot[sourceSlots.Count];
            for (int i = 0; i < sourceSlots.Count; i++)
            {
                HumanoidMeshMaterialSlot slot = sourceSlots[i];
                if (!slot.MaterialId.IsValid || slot.NewSubmeshIndex < 0 || slot.Adapter == null)
                    return Reject("EditModeMaterialSlotInvalid", "EditMode Mesh result has an invalid final material slot.", out diagnostic);
                if (!result.TryGetSourceMaterial(slot.MaterialId, out Material sourceMaterial, out diagnostic)) return false;
                slots[i] = new HumanoidBuildMaterialSlot(slot.MaterialId, slot.NewSubmeshIndex, sourceMaterial, slot.Adapter);
            }

            IReadOnlyList<HumanoidMeshNormalTextureRegistration> sourceRegistrations = result.NormalTextureRegistrations;
            var sourceNormals = new HumanoidBuildSourceNormal[sourceRegistrations.Count];
            for (int i = 0; i < sourceRegistrations.Count; i++)
            {
                HumanoidMeshNormalTextureRegistration registration = sourceRegistrations[i];
                if (!registration.MaterialId.IsValid || registration.NormalTexture == null)
                    return Reject("EditModeSourceNormalInvalid", "EditMode Mesh result has an invalid source Normal registration.", out diagnostic);
                sourceNormals[i] = new HumanoidBuildSourceNormal(registration.MaterialId, registration.NormalTexture);
            }

            IReadOnlyList<EditModeMeshNormalCompletion> pendingNormals = result.NormalCompletions;
            for (int i = 0; i < pendingNormals.Count; i++)
            {
                EditModeMeshNormalCompletion normal = pendingNormals[i];
                if (normal == null || normal.Source == null || normal.Completion == null || normal.Completion.Texture == null || !new MaterialId(normal.Source.Owner.RegistryId, normal.Source.EntryName).IsValid)
                    return Reject("EditModeComputedNormalInvalid", "EditMode Mesh result has an invalid computed Normal completion.", out diagnostic);
            }

            if (!result.TryDetachVrmTransportProvenance(out HumanoidMeshVrmTransportProvenance vrmTransportProvenance))
                return Reject("EditModeVrmTransportProvenanceMissing", "EditMode Mesh result has no VRM transport provenance carrier.", out diagnostic);

            Mesh finalMesh = result.DetachFinalMesh();
            Avatar avatar = result.DetachAvatar();
            if (!TryDetachResolvedHumanoid(result, finalMesh, avatar, out GameObject resolvedRoot, out diagnostic))
            {
                if (finalMesh != null) UnityEngine.Object.DestroyImmediate(finalMesh);
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                return false;
            }
            EditModeMeshNormalCompletion[] detachedNormals = result.DetachNormalCompletions();
            var computedNormals = new HumanoidBuildComputedNormal[detachedNormals.Length];
            var inMemoryMesh = new InMemoryHumanoidMesh(resolvedRoot, finalMesh, avatar);
            try
            {
                for (int i = 0; i < detachedNormals.Length; i++)
                {
                    EditModeMeshNormalCompletion normal = detachedNormals[i];
                    TextureCompletion completion = normal.DetachCompletion();
                    MaterialId materialId = new MaterialId(normal.Source.Owner.RegistryId, normal.Source.EntryName);
                    computedNormals[i] = new HumanoidBuildComputedNormal(materialId, new HumanoidOwnedTexture(completion.Texture, _ => completion.Dispose()));
                }
                payload = new MeshBuildPayload(inMemoryMesh, slots, sourceNormals, computedNormals, vrmTransportProvenance);
                return true;
            }
            catch (Exception)
            {
                for (int i = 0; i < computedNormals.Length; i++) computedNormals[i].Texture?.Dispose();
                for (int i = 0; i < detachedNormals.Length; i++) detachedNormals[i]?.Dispose();
                inMemoryMesh.Dispose();
                vrmTransportProvenance.Dispose();
                return Reject("EditModeMeshCarrierConversionFailed", "EditMode Mesh result could not be converted into a detached Compiler carrier.", out diagnostic);
            }
        }

        /// <summary>
        /// Promotes the 17.2 skeleton escrow into the one final Object carrier.  No later phase
        /// receives bone paths or poses to recreate this hierarchy.
        /// </summary>
        private static bool TryDetachResolvedHumanoid(EditModeMeshBuildResult result, Mesh finalMesh, Avatar avatar, out GameObject root, out StackMachineDiagnostic diagnostic)
        {
            root = null;
            diagnostic = null;
            if (result?.Skeleton == null || result.Skeleton.Root == null || result.BoneTable?.Bones == null || finalMesh == null)
                return Reject("EditModeResolvedHumanoidMissing", "EditMode Mesh result has no final resolved Humanoid hierarchy, bone table, or Mesh.", out diagnostic);
            root = result.Skeleton.DetachRoot();
            if (root == null) return Reject("EditModeResolvedHumanoidMissing", "EditMode Mesh result could not transfer its final Humanoid hierarchy.", out diagnostic);
            if (HumanoidResolvedHumanoidCarrier.TryPromote(root, finalMesh, avatar, result.BoneTable.Bones, out diagnostic)) return true;
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
            }
            return false;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }

}
