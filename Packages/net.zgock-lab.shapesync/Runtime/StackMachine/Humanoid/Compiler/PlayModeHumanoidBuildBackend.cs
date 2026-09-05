// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>
    /// PlayMode concrete backend for <see cref="HumanoidCompiler"/>.  It owns only runtime
    /// execution handles and unpublished carrier escrow; compiler sequencing remains shared.
    /// </summary>
    public sealed class PlayModeHumanoidBuildBackend : IHumanoidBuildBackend, IDisposable
    {
        private readonly PlayModeHumanoidMeshStackMachine meshMachine;
        private readonly PlayModeHumanoidMaterialStackMachine materialMachine;
        private HumanoidBuildSource source;
        private bool meshActive;
        private bool materialActive;
        private HumanoidMeshFbmBakeResult pendingMesh;
        private TextureDelivery[] pendingNormals;
        private HumanoidResolvedHumanoidCarrierOperation carrierOperation;
        private HumanoidBuildMaterialSlot[] pendingSlots;
        private HumanoidBuildSourceNormal[] pendingSourceNormals;
        private HumanoidMeshVrmTransportProvenance pendingProvenance;
        private GameObject pendingRoot;
        private Mesh pendingFinalMesh;
        private Avatar pendingAvatar;
        private bool disposed;

        /// <summary>Creates a backend using independent scene-local hosts for NORMAL and MATERIAL textures.</summary>
        /// <remarks>The runtime Hot Bake product is a Pure Humanoid, so its Mesh machine publishes the resolved Humanoid rest pose.</remarks>
        public PlayModeHumanoidBuildBackend(TextureStackMachineHost normalHost, TextureStackMachineHost materialHost)
            : this(new PlayModeHumanoidMeshStackMachine(normalHost), new PlayModeHumanoidMaterialStackMachine(materialHost)) { }

        /// <summary>Creates a backend from explicit runtime phase machines, primarily for PlayMode integration hosts.</summary>
        public PlayModeHumanoidBuildBackend(PlayModeHumanoidMeshStackMachine meshMachine, PlayModeHumanoidMaterialStackMachine materialMachine)
        {
            this.meshMachine = meshMachine;
            this.materialMachine = materialMachine;
        }

        /// <inheritdoc />
        public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("PlayModeHumanoidBackendDisposed", "PlayMode Humanoid backend has been disposed.", out diagnostic);
            if (meshMachine == null) return Reject("PlayModeMeshMachineRequired", "PlayMode Humanoid backend requires a Mesh StackMachine.", out diagnostic);
            if (meshActive || materialActive || pendingMesh != null) return Reject("PlayModeHumanoidBackendBusy", "Cancel the active PlayMode Humanoid build before beginning another Mesh phase.", out diagnostic);
            if (!meshMachine.Start(source.FigureRoot, source.Document, out diagnostic)) return false;
            this.source = source;
            meshActive = true;
            return true;
        }

        /// <inheritdoc />
        public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic)
        {
            meshPayload = null;
            diagnostic = null;
            if (!meshActive) return Fail("PlayModeMeshPhaseInactive", "PlayMode Humanoid backend Mesh phase is not active.", out diagnostic);

            if (pendingMesh == null)
            {
                HumanoidMeshBuildStatus status = meshMachine.Pump(out diagnostic);
                if (status == HumanoidMeshBuildStatus.Pending) return HumanoidBuildPhaseStatus.Pending;
                if (status == HumanoidMeshBuildStatus.Cancelled) { ClearSource(); meshActive = false; return HumanoidBuildPhaseStatus.Cancelled; }
                if (status != HumanoidMeshBuildStatus.Succeeded) { ClearSource(); meshActive = false; return HumanoidBuildPhaseStatus.Failed; }
                if (!meshMachine.TryTake(out HumanoidMeshBuildEscrow<TextureDelivery> escrow))
                {
                    meshActive = false;
                    ClearSource();
                    return Fail("PlayModeMeshResultMissing", "PlayMode Mesh StackMachine succeeded without a single-take result.", out diagnostic);
                }
                pendingMesh = escrow.DetachMesh();
                pendingNormals = escrow.DetachNormals();
                escrow.Dispose();
                if (!TryBeginCarrierPromotion(out diagnostic))
                {
                    meshActive = false;
                    ClearPendingMesh();
                    ClearSource();
                    return HumanoidBuildPhaseStatus.Failed;
                }
            }

            HumanoidResolvedHumanoidCarrierStatus carrierStatus = carrierOperation.Pump(out diagnostic);
            if (carrierStatus == HumanoidResolvedHumanoidCarrierStatus.Pending) return HumanoidBuildPhaseStatus.Pending;
            if (carrierStatus != HumanoidResolvedHumanoidCarrierStatus.Succeeded)
            {
                meshActive = false;
                ClearPendingMesh();
                ClearSource();
                return HumanoidBuildPhaseStatus.Failed;
            }

            try
            {
                meshPayload = CreateMeshPayload();
                meshActive = false;
                ClearCarrierFields();
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            catch (Exception exception)
            {
                meshActive = false;
                ClearPendingMesh();
                ClearSource();
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PlayModeMeshCarrierConversionFailed", "PlayMode Mesh escrow could not be converted into a Compiler carrier.", detail: exception.Message);
                return HumanoidBuildPhaseStatus.Failed;
            }
        }

        /// <inheritdoc />
        public bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("PlayModeHumanoidBackendDisposed", "PlayMode Humanoid backend has been disposed.", out diagnostic);
            if (materialMachine == null) return Reject("PlayModeMaterialMachineRequired", "PlayMode Humanoid backend requires a Material StackMachine.", out diagnostic);
            if (meshActive || materialActive) return Reject("PlayModeHumanoidBackendBusy", "PlayMode Humanoid backend already has an active phase.", out diagnostic);
            if (source.FigureRoot == null || source.Document == null) return Reject("PlayModeBuildSourceMissing", "PlayMode Humanoid backend has no accepted Mesh source for Material execution.", out diagnostic);
            if (meshPayload?.Mesh == null) return Reject("PlayModeMeshPayloadMissing", "PlayMode Material execution requires the accepted Mesh payload.", out diagnostic);
            if (!materialMachine.Start(source.FigureRoot, source.Document, out diagnostic)) return false;
            materialActive = true;
            return true;
        }

        /// <inheritdoc />
        public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic diagnostic)
        {
            materialPayload = null;
            diagnostic = null;
            if (!materialActive) return Fail("PlayModeMaterialPhaseInactive", "PlayMode Humanoid backend Material phase is not active.", out diagnostic);
            HumanoidMaterialBuildStatus status = materialMachine.Pump(out diagnostic);
            if (status == HumanoidMaterialBuildStatus.Pending) return HumanoidBuildPhaseStatus.Pending;
            materialActive = false;
            if (status == HumanoidMaterialBuildStatus.Cancelled) { ClearSource(); return HumanoidBuildPhaseStatus.Cancelled; }
            if (status != HumanoidMaterialBuildStatus.Succeeded) { ClearSource(); return HumanoidBuildPhaseStatus.Failed; }
            if (!materialMachine.TryTake(out HumanoidMaterialBuildEscrow<TextureDelivery> escrow))
            {
                ClearSource();
                return Fail("PlayModeMaterialResultMissing", "PlayMode Material StackMachine succeeded without a single-take result.", out diagnostic);
            }
            try
            {
                materialPayload = PlayModeHumanoidMaterialPayloadBuilder.Create(escrow.DetachPayloads());
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            catch (Exception exception)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "PlayModeMaterialPayloadInvalid", "PlayMode Material result has an invalid semantic payload.", detail: exception.Message);
                return HumanoidBuildPhaseStatus.Failed;
            }
            finally { escrow.Dispose(); ClearSource(); }
        }

        /// <inheritdoc />
        public void Cancel()
        {
            if (disposed) return;
            if (meshActive) meshMachine?.Cancel();
            if (materialActive) materialMachine?.Cancel();
            meshActive = false;
            materialActive = false;
            ClearPendingMesh();
            ClearSource();
        }

        /// <summary>Cancels unhanded work and releases backend-owned runtime phase machines exactly once.</summary>
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            meshMachine?.Dispose();
            materialMachine?.Dispose();
            disposed = true;
        }

        private bool TryBeginCarrierPromotion(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (pendingMesh == null || pendingMesh.FinalMesh == null || pendingMesh.Skeleton?.Root == null || pendingMesh.BoneTable?.Bones == null)
                return Reject("PlayModeResolvedHumanoidMissing", "PlayMode Mesh result has no final resolved Humanoid hierarchy, bone table, or Mesh.", out diagnostic);
            if (!TryCreateMeshMetadata(pendingMesh, pendingNormals, out pendingSlots, out pendingSourceNormals, out pendingProvenance, out diagnostic)) return false;
            pendingFinalMesh = pendingMesh.DetachFinalMesh();
            pendingAvatar = pendingMesh.DetachAvatar();
            pendingRoot = pendingMesh.Skeleton.DetachRoot();
            if (HumanoidResolvedHumanoidCarrier.TryBeginPromote(pendingRoot, pendingFinalMesh, pendingAvatar, pendingMesh.BoneTable.Bones, out carrierOperation, out diagnostic)) return true;
            return false;
        }

        private MeshBuildPayload CreateMeshPayload()
        {
            var computedNormals = new HumanoidBuildComputedNormal[pendingNormals?.Length ?? 0];
            var inMemoryMesh = new InMemoryHumanoidMesh(pendingRoot, pendingFinalMesh, pendingAvatar);
            try
            {
                for (int i = 0; i < computedNormals.Length; i++)
                {
                    TextureDelivery delivery = pendingNormals[i];
                    if (delivery?.Texture == null) throw new InvalidOperationException("PlayMode computed Normal delivery is missing.");
                    MaterialId id = ResolveComputedNormalMaterialId(pendingMesh.LogicalPlan, i);
                    computedNormals[i] = new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(delivery.Texture, _ => delivery.Dispose()));
                }
                pendingNormals = Array.Empty<TextureDelivery>();
                pendingMesh.Dispose(); pendingMesh = null;
                return new MeshBuildPayload(inMemoryMesh, pendingSlots, pendingSourceNormals, computedNormals, pendingProvenance);
            }
            catch
            {
                for (int i = 0; i < computedNormals.Length; i++) computedNormals[i].Texture?.Dispose();
                inMemoryMesh.Dispose();
                throw;
            }
        }

        private static MaterialId ResolveComputedNormalMaterialId(HumanoidMeshLogicalPlan plan, int index)
        {
            if (plan == null || index < 0 || index >= plan.NormalSources.Count) throw new InvalidOperationException("PlayMode computed Normal source is missing.");
            HumanoidMeshNormalSource normal = plan.NormalSources[index];
            var id = new MaterialId(normal.Owner.RegistryId, normal.EntryName);
            if (!id.IsValid) throw new InvalidOperationException("PlayMode computed Normal material identity is invalid.");
            return id;
        }

        private static bool TryCreateMeshMetadata(HumanoidMeshFbmBakeResult result, TextureDelivery[] normals, out HumanoidBuildMaterialSlot[] slots, out HumanoidBuildSourceNormal[] sourceNormals, out HumanoidMeshVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic)
        {
            slots = null; sourceNormals = null; provenance = null; diagnostic = null;
            slots = new HumanoidBuildMaterialSlot[result.MaterialSlots.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                HumanoidMeshMaterialSlot slot = result.MaterialSlots[i];
                if (!slot.MaterialId.IsValid || slot.NewSubmeshIndex < 0 || slot.Adapter == null || !TryGetSourceMaterial(result, slot.MaterialId, out Material sourceMaterial, out bool preserveEffectiveNormal, out diagnostic)) return false;
                slots[i] = new HumanoidBuildMaterialSlot(slot.MaterialId, slot.NewSubmeshIndex, sourceMaterial, slot.Adapter, preserveEffectiveNormal);
            }
            sourceNormals = new HumanoidBuildSourceNormal[result.NormalTextureRegistrations.Count];
            for (int i = 0; i < sourceNormals.Length; i++)
            {
                HumanoidMeshNormalTextureRegistration registration = result.NormalTextureRegistrations[i];
                if (!registration.MaterialId.IsValid || registration.NormalTexture == null) return Reject("PlayModeSourceNormalInvalid", "PlayMode Mesh result has an invalid source Normal registration.", out diagnostic);
                HumanoidBuildMaterialSlot slot = default;
                bool foundSlot = false;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    if (slots[slotIndex].MaterialId != registration.MaterialId) continue;
                    slot = slots[slotIndex]; foundSlot = true; break;
                }
                if (!foundSlot) return Reject("PlayModeSourceNormalMaterialMissing", "PlayMode Mesh result has a Normal registration without a final Material slot.", out diagnostic);
                var readPlan = new List<MaterialPropertyReadCommand>();
                if (!slot.Adapter.TryBuildReadPlan(readPlan, out MaterialProxyDiagnostic adapterDiagnostic) || !slot.Adapter.TryReadValues(slot.SourceMaterial, readPlan, out MaterialProxySemanticValues values, out adapterDiagnostic))
                    return Reject("PlayModeSourceNormalReadRejected", adapterDiagnostic.message, out diagnostic);
                // The cloned candidate already starts from the effective Proxy material.  The
                // explicit SourceNormal phase must preserve that same semantic value rather
                // than reintroduce the logical collector's original imported texture.
                Texture effectiveNormal = values.applyNormalTexture ? values.normalTexture : registration.NormalTexture;
                if (effectiveNormal == null) return Reject("PlayModeSourceNormalInvalid", "PlayMode effective source Material has no Normal texture.", out diagnostic);
                sourceNormals[i] = new HumanoidBuildSourceNormal(registration.MaterialId, effectiveNormal);
            }
            if ((normals?.Length ?? 0) != result.LogicalPlan.NormalSources.Count) return Reject("PlayModeComputedNormalCountMismatch", "PlayMode Mesh result does not retain one computed Normal delivery per requested source.", out diagnostic);
            for (int i = 0; i < normals.Length; i++) if (normals[i]?.Texture == null) return Reject("PlayModeComputedNormalInvalid", "PlayMode Mesh result has an invalid computed Normal delivery.", out diagnostic);
            return HumanoidMeshVrmTransportProvenance.TryCreate(result.LogicalPlan, out provenance, out diagnostic);
        }

        private static bool TryGetSourceMaterial(HumanoidMeshFbmBakeResult result, MaterialId materialId, out Material sourceMaterial, out bool preserveEffectiveNormal, out StackMachineDiagnostic diagnostic)
        {
            sourceMaterial = null; preserveEffectiveNormal = false; diagnostic = null;
            for (int sourceIndex = 0; sourceIndex < result.Sources.Count; sourceIndex++)
            {
                HumanoidMeshSource meshSource = result.Sources[sourceIndex].Source;
                if (meshSource.RegistryId != materialId.RegistryId || meshSource.MaterialProxy == null) continue;
                IReadOnlyList<MaterialProxyEntry> entries = meshSource.MaterialProxy.Entries;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    MaterialProxyEntry entry = entries[entryIndex];
                    if (entry == null || entry.entryName != materialId.EntryId) continue;
                    Material[] materials = entry.renderer?.sharedMaterials;
                    if (entry.renderer != meshSource.Renderer || entry.materialChannel < 0 || materials == null || entry.materialChannel >= materials.Length || materials[entry.materialChannel] == null)
                        return Reject("PlayModeSourceMaterialMissing", "PlayMode Mesh carrier MaterialProxy entry has no source Material.", out diagnostic);
                    // A MaterialProxy owns the effective runtime material, including a
                    // NormalBlender delivery retained in its escrow.  The renderer slot is
                    // only the fallback because Hybrid Run Mode restores that slot to the
                    // original material when the Proxy is suspended.  Compiling from the
                    // slot would therefore discard the current Normal state from the baked
                    // artifact.
                    preserveEffectiveNormal = entry.runtimeMaterial != null;
                    sourceMaterial = entry.runtimeMaterial ?? materials[entry.materialChannel];
                    return true;
                }
            }
            return Reject("PlayModeSourceMaterialMissing", "PlayMode Mesh carrier could not resolve a source Material for MaterialId.", out diagnostic);
        }

        private void ClearPendingMesh()
        {
            for (int i = 0; i < (pendingNormals?.Length ?? 0); i++) pendingNormals[i]?.Dispose();
            pendingNormals = null;
            pendingProvenance?.Dispose(); pendingProvenance = null;
            pendingMesh?.Dispose(); pendingMesh = null;
            HumanoidMeshResourceCleanup.Destroy(pendingFinalMesh); pendingFinalMesh = null;
            HumanoidMeshResourceCleanup.Destroy(pendingAvatar); pendingAvatar = null;
            HumanoidMeshResourceCleanup.Destroy(pendingRoot); pendingRoot = null;
            pendingSlots = null; pendingSourceNormals = null; carrierOperation = null;
        }

        private void ClearCarrierFields()
        {
            pendingRoot = null; pendingFinalMesh = null; pendingAvatar = null;
            pendingSlots = null; pendingSourceNormals = null; pendingProvenance = null; carrierOperation = null;
        }
        private void ClearSource() => source = default;
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message); return false; }
        private static HumanoidBuildPhaseStatus Fail(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message); return HumanoidBuildPhaseStatus.Failed; }
    }

    /// <summary>Converts PlayMode Texture deliveries to the compiler's owned Material semantic payload.</summary>
    public static class PlayModeHumanoidMaterialPayloadBuilder
    {
        internal static MaterialBuildPayload Create(HumanoidMaterialBuildPayload<TextureDelivery>[] entries)
        {
            entries ??= Array.Empty<HumanoidMaterialBuildPayload<TextureDelivery>>();
            var converted = new HumanoidMaterialSemanticPayload[entries.Length];
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    HumanoidMaterialBuildPayload<TextureDelivery> entry = entries[i];
                    if (!entry.MaterialId.IsValid || (entry.HasMainTex && entries[i].MainTex?.Texture == null)) throw new InvalidOperationException("PlayMode Material semantic payload is invalid.");
                    HumanoidOwnedTexture texture = entry.HasMainTex ? new HumanoidOwnedTexture(entry.MainTex.Texture, _ => entry.MainTex.Dispose()) : null;
                    converted[i] = new HumanoidMaterialSemanticPayload(entry.MaterialId, texture, entry.HasColor, entry.Color, entry.HasUvSet, entry.UvScale, entry.UvOffset);
                }
                return new MaterialBuildPayload(converted);
            }
            catch
            {
                for (int i = 0; i < converted.Length; i++) converted[i].MainTexture?.Dispose();
                for (int i = 0; i < entries.Length; i++) if (entries[i].HasMainTex) entries[i].MainTex?.Dispose();
                throw;
            }
        }
    }
}
