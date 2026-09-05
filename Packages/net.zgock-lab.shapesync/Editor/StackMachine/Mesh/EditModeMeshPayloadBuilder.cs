// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Maps TSM-owned Normal completions onto Core-resolved Material slots.</summary>
    public static class EditModeMeshPayloadBuilder
    {
        internal static bool TryCreateNormalPayloads(HumanoidMeshFbmBakeResult bake, IReadOnlyList<EditModeMeshNormalCompletion> normalCompletions, out EditModeMeshNormalPayload[] normalPayloads, out StackMachineDiagnostic diagnostic)
        {
            normalPayloads = null;
            diagnostic = null;
            if (bake == null) return Fail("MeshPayloadInputInvalid", "Mesh payload build requires a completed Core Mesh escrow.", out diagnostic);
            normalCompletions ??= Array.Empty<EditModeMeshNormalCompletion>();
            var normals = new EditModeMeshNormalPayload[normalCompletions.Count];
            for (int i = 0; i < normalCompletions.Count; i++)
            {
                EditModeMeshNormalCompletion normal = normalCompletions[i];
                if (normal == null || normal.Completion == null) return Fail("MeshPayloadNormalMissing", "Mesh payload contains a missing Normal completion.", out diagnostic, i.ToString());
                var id = new MaterialId(normal.Source.Owner.RegistryId, normal.Source.EntryName);
                bool found = false;
                for (int slot = 0; slot < bake.MaterialSlots.Count; slot++) if (bake.MaterialSlots[slot].MaterialId.Equals(id)) { found = true; break; }
                if (!found) return Fail("MeshPayloadNormalMaterialMissing", "Mesh Normal completion does not resolve to a final Core MaterialId.", out diagnostic, id.ToString());
                normals[i] = new EditModeMeshNormalPayload(id, normal.Completion);
            }
            normalPayloads = normals;
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("HumanoidMesh", code, message, detail: detail);
            return false;
        }
    }
}
