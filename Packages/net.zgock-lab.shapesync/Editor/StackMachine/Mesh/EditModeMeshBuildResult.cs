// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>
    /// Single-take final Mesh handoff.  It owns the complete compiler escrow until the upper Compiler
    /// either transfers the in-memory result to its next phase or disposes it on abort / cancel.
    /// </summary>
    public sealed class EditModeMeshBuildResult : IDisposable
    {
        private HumanoidMeshFbmBakeResult escrow;
        private readonly IReadOnlyList<HumanoidMeshMaterialSlot> materialSlots;
        private EditModeMeshNormalCompletion[] normalCompletions;
        private readonly IReadOnlyList<EditModeMeshNormalPayload> normalPayloads;
        private HumanoidMeshVrmTransportProvenance vrmTransportProvenance;

        internal EditModeMeshBuildResult(HumanoidMeshFbmBakeResult escrow, HumanoidMeshMaterialSlot[] materialSlots, EditModeMeshNormalCompletion[] normalCompletions, EditModeMeshNormalPayload[] normalPayloads, HumanoidMeshVrmTransportProvenance vrmTransportProvenance)
        {
            this.escrow = escrow;
            this.materialSlots = materialSlots == null ? Array.Empty<HumanoidMeshMaterialSlot>() : Array.AsReadOnly(materialSlots);
            this.normalCompletions = normalCompletions ?? Array.Empty<EditModeMeshNormalCompletion>();
            this.normalPayloads = normalPayloads == null ? Array.Empty<EditModeMeshNormalPayload>() : Array.AsReadOnly(normalPayloads);
            this.vrmTransportProvenance = vrmTransportProvenance;
        }

        public Mesh Mesh => escrow?.FinalMesh;
        public HumanoidMeshSkeletonEscrow Skeleton => escrow?.Skeleton;
        public Avatar Avatar => escrow?.Skeleton?.Avatar;
        public HumanoidMeshBoneTable BoneTable => escrow?.BoneTable;
        public IReadOnlyList<HumanoidMeshMaterialSlot> MaterialSlots => materialSlots;
        /// <summary>Gets source Normal textures keyed by MaterialId; Compiler applies these before computed Normal overrides.</summary>
        public IReadOnlyList<HumanoidMeshNormalTextureRegistration> NormalTextureRegistrations => escrow?.NormalTextureRegistrations ?? Array.Empty<HumanoidMeshNormalTextureRegistration>();
        public IReadOnlyList<EditModeMeshNormalPayload> NormalPayloads => normalPayloads;
        internal IReadOnlyList<EditModeMeshNormalCompletion> NormalCompletions => Array.AsReadOnly(normalCompletions);
        /// <summary>Transfers the final Mesh once to the Core compiler carrier.</summary>
        internal Mesh DetachFinalMesh() => escrow?.DetachFinalMesh();
        /// <summary>Transfers the rebuilt Avatar once to the Core compiler carrier.</summary>
        internal Avatar DetachAvatar() => escrow?.DetachAvatar();
        /// <summary>Transfers all pending computed Normal completions exactly once to an upper compiler carrier.</summary>
        internal EditModeMeshNormalCompletion[] DetachNormalCompletions()
        {
            EditModeMeshNormalCompletion[] value = normalCompletions;
            normalCompletions = Array.Empty<EditModeMeshNormalCompletion>();
            return value;
        }

        /// <summary>Transfers the Mesh-lower VRM source-role snapshot exactly once to the upper Compiler backend.</summary>
        internal bool TryDetachVrmTransportProvenance(out HumanoidMeshVrmTransportProvenance provenance)
        {
            provenance = vrmTransportProvenance;
            vrmTransportProvenance = null;
            return provenance != null;
        }

        /// <summary>Resolves one read-only source Material for a final MaterialId without mutating a Proxy or renderer.</summary>
        internal bool TryGetSourceMaterial(MaterialId materialId, out Material sourceMaterial, out StackMachineDiagnostic diagnostic)
        {
            sourceMaterial = null;
            diagnostic = null;
            if (!materialId.IsValid) return Reject("CompilerMaterialIdInvalid", "Compiler Mesh carrier requires a valid MaterialId.", out diagnostic);
            if (escrow == null) return Reject("CompilerMeshResultDisposed", "Compiler Mesh carrier cannot resolve source Materials after disposal.", out diagnostic);

            for (int sourceIndex = 0; sourceIndex < escrow.Sources.Count; sourceIndex++)
            {
                HumanoidMeshSource meshSource = escrow.Sources[sourceIndex].Source;
                if (meshSource.RegistryId != materialId.RegistryId || meshSource.MaterialProxy == null) continue;
                IReadOnlyList<MaterialProxyEntry> entries = meshSource.MaterialProxy.Entries;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    MaterialProxyEntry entry = entries[entryIndex];
                    if (entry == null || entry.entryName != materialId.EntryId) continue;
                    if (entry.renderer != meshSource.Renderer || entry.materialChannel < 0) return Reject("CompilerMaterialEntryInvalid", "Compiler Mesh carrier MaterialProxy entry does not match its collected renderer channel.", out diagnostic);
                    Material[] materials = entry.renderer.sharedMaterials;
                    if (materials == null || entry.materialChannel >= materials.Length || materials[entry.materialChannel] == null) return Reject("CompilerSourceMaterialMissing", "Compiler Mesh carrier MaterialProxy entry has no source Material.", out diagnostic);
                    sourceMaterial = materials[entry.materialChannel];
                    return true;
                }
            }

            return Reject("CompilerSourceMaterialMissing", "Compiler Mesh carrier could not resolve a source Material for MaterialId.", out diagnostic);
        }

        /// <summary>Releases final Mesh, local skeleton, pending Normal completions, and all private temporary Meshes.</summary>
        public void Dispose()
        {
            for (int i = 0; i < normalCompletions.Length; i++) normalCompletions[i]?.Dispose();
            normalCompletions = Array.Empty<EditModeMeshNormalCompletion>();
            vrmTransportProvenance?.Dispose();
            vrmTransportProvenance = null;
            escrow?.Dispose();
            escrow = null;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }
}
