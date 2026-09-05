// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    internal static class HumanoidMeshMaterialSlotBuilder
    {
        internal static bool TryCreate(HumanoidMeshFbmBakeResult bake, out HumanoidMeshMaterialSlot[] slots, out StackMachineDiagnostic diagnostic)
        {
            slots = null; diagnostic = null;
            if (bake == null || bake.FirstSubmeshBySource.Count != bake.Sources.Count) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MeshPayloadInputInvalid", "Mesh payload build requires one submesh start for every Figure / Outfit source."); return false; }
            var list = new List<HumanoidMeshMaterialSlot>(); var ids = new HashSet<MaterialId>();
            for (int sourceIndex = 0; sourceIndex < bake.Sources.Count; sourceIndex++)
            {
                var source = bake.Sources[sourceIndex];
                if (source.Source.MaterialProxy == null || source.Mesh == null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MeshPayloadSourceInvalid", "Mesh payload source requires a MaterialProxy and candidate Mesh."); return false; }
                var covered = new bool[source.Mesh.subMeshCount];
                foreach (MaterialProxyEntry entry in source.Source.MaterialProxy.Entries)
                {
                    if (entry == null || entry.adapter == null || entry.materialChannel < 0 || entry.materialChannel >= source.Mesh.subMeshCount) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MeshPayloadMaterialEntryInvalid", "Mesh payload requires a valid MaterialProxy entry for each source submesh."); return false; }
                    var id = new MaterialId(source.Source.RegistryId, entry.entryName);
                    if (!id.IsValid || !ids.Add(id)) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MeshPayloadMaterialIdDuplicate", "Mesh payload MaterialId must be valid and unique."); return false; }
                    list.Add(new HumanoidMeshMaterialSlot(id, bake.FirstSubmeshBySource[sourceIndex] + entry.materialChannel, entry.adapter)); covered[entry.materialChannel] = true;
                }
                for (int i = 0; i < covered.Length; i++) if (!covered[i]) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "MeshPayloadMaterialEntryMissing", "Every final source submesh requires exactly one MaterialProxy entry."); return false; }
            }
            slots = list.ToArray(); return true;
        }
    }
}
