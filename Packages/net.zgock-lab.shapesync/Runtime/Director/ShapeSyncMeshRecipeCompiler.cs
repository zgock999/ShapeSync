// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Builds a detached Mesh StackMachine source from current and desired physical Shapes.</summary>
    public static class ShapeSyncMeshRecipeCompiler
    {
        /// <summary>Compiles current-to-desired Mesh changes in DETACH, FBM_SET, ATTACH order.</summary>
        /// <param name="current">The committed physical Shape snapshot.</param>
        /// <param name="desired">The desired resolved physical Shape snapshot.</param>
        /// <param name="source">Detached Mesh StackMachine source on success.</param>
        /// <param name="diagnostic">A structured reject for invalid compiler input.</param>
        /// <returns><see langword="true"/> when a detached source was compiled.</returns>
        public static bool TryCompile(IReadOnlyList<ShapeSyncShape> current, IReadOnlyList<ShapeSyncShape> desired, out string source, out StackMachineDiagnostic diagnostic)
        {
            source = string.Empty;
            diagnostic = null;
            if (!TryCollect(current, out SortedDictionary<string, float> currentMorphs, out List<string> currentEntries, out diagnostic) ||
                !TryCollect(desired, out SortedDictionary<string, float> desiredMorphs, out List<string> desiredEntries, out diagnostic)) return false;

            var builder = new StringBuilder();
            for (int i = 0; i < currentEntries.Count; i++) if (!desiredEntries.Contains(currentEntries[i])) Append(builder, "$" + currentEntries[i] + " DETACH");
            var morphTargets = new SortedSet<string>(currentMorphs.Keys, System.StringComparer.Ordinal);
            morphTargets.UnionWith(desiredMorphs.Keys);
            foreach (string target in morphTargets)
            {
                float desiredValue = desiredMorphs.TryGetValue(target, out float value) ? value : 0f;
                float currentValue = currentMorphs.TryGetValue(target, out value) ? value : 0f;
                if (currentValue != desiredValue) Append(builder, "$" + target + " " + desiredValue.ToString("G7", CultureInfo.InvariantCulture) + " FBM_SET");
            }
            for (int i = 0; i < desiredEntries.Count; i++) if (!currentEntries.Contains(desiredEntries[i])) Append(builder, "$" + desiredEntries[i] + " ATTACH");
            source = builder.ToString();
            return true;
        }

        private static bool TryCollect(IReadOnlyList<ShapeSyncShape> shapes, out SortedDictionary<string, float> morphs, out List<string> entries, out StackMachineDiagnostic diagnostic)
        {
            morphs = new SortedDictionary<string, float>(System.StringComparer.Ordinal); entries = new List<string>(); diagnostic = null;
            if (shapes == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "PhysicalShapesRequired", "Mesh compile requires physical Shapes."); return false; }
            for (int i = 0; i < shapes.Count; i++)
            {
                if (shapes[i] is MorphShape morph) foreach (MorphValue value in morph.Morphs)
                { if (!morphs.TryAdd(value.Target, value.Value)) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateMorphTarget", "Mesh compile cannot resolve duplicate morph targets.", bindingName: value.Target); return false; } }
                if (shapes[i] is PartsShape parts) foreach (ShapeEntry entry in parts.Parts) if (entry is MeshEntry mesh)
                {
                    if (string.IsNullOrEmpty(mesh.LogicalName)) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "MeshLogicalNameRequired", "MeshEntry requires a logical name."); return false; }
                    if (entries.Contains(mesh.LogicalName)) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateMeshEntry", "Mesh compile cannot resolve duplicate Mesh entries.", bindingName: mesh.LogicalName); return false; }
                    entries.Add(mesh.LogicalName);
                }
            }
            return true;
        }

        private static void Append(StringBuilder builder, string line) { if (builder.Length != 0) builder.Append('\n'); builder.Append(line); }
    }
}
