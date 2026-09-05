// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Converts one taken EditMode Material result into the public detached Compiler carrier.</summary>
    internal static class EditModeHumanoidMaterialPayloadBuilder
    {
        internal static bool TryCreate(EditModeMaterialBuildResult result, out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic)
        {
            payload = null;
            diagnostic = null;
            if (result == null) return Reject("EditModeMaterialResultMissing", "EditMode Material result is missing.", out diagnostic);
            HumanoidMaterialBuildPayload<TextureCompletion>[] entries = result.DetachPayloads();
            var converted = new HumanoidMaterialSemanticPayload[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                HumanoidMaterialBuildPayload<TextureCompletion> entry = entries[i];
                if (!entry.MaterialId.IsValid || (entry.HasMainTex && (entry.MainTex == null || entry.MainTex.Texture == null)))
                {
                    Dispose(entries);
                    return Reject("EditModeMaterialPayloadInvalid", "EditMode Material result has an invalid semantic payload.", out diagnostic);
                }
            }

            for (int i = 0; i < entries.Length; i++)
            {
                HumanoidMaterialBuildPayload<TextureCompletion> entry = entries[i];
                HumanoidOwnedTexture texture = entry.HasMainTex ? new HumanoidOwnedTexture(entry.MainTex.Texture, _ => entry.MainTex.Dispose()) : null;
                converted[i] = new HumanoidMaterialSemanticPayload(entry.MaterialId, texture, entry.HasColor, entry.Color, entry.HasUvSet, entry.UvScale, entry.UvOffset);
            }
            payload = new MaterialBuildPayload(converted);
            return true;
        }

        private static void Dispose(HumanoidMaterialBuildPayload<TextureCompletion>[] entries)
        {
            for (int i = 0; i < entries.Length; i++) if (entries[i].HasMainTex) entries[i].MainTex?.Dispose();
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }
}
