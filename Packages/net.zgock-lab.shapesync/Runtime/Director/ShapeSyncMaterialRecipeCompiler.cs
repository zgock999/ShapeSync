// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Builds detached Material StackMachine source from sorted Material entries.</summary>
    public static class ShapeSyncMaterialRecipeCompiler
    {
        /// <summary>Compiles the Material-entry changes from current to desired into FIGURE/OUTFIT flat-cursor source.</summary>
        /// <param name="current">The committed current Material entry snapshot.</param>
        /// <param name="desired">The desired Material entries in merged ordering.</param>
        /// <param name="source">Detached Material StackMachine source on success.</param>
        /// <param name="diagnostic">A structured reject for invalid or ambiguous Material entries.</param>
        /// <returns><see langword="true"/> when a detached source was compiled.</returns>
        public static bool TryCompile(IReadOnlyList<ShapeSyncMergedEntry> current, IReadOnlyList<ShapeSyncMergedEntry> desired, out string source, out StackMachineDiagnostic diagnostic)
            => TryCompile(current, desired, null, null, out source, out diagnostic);

        /// <summary>Compiles Material changes and Mesh-owned Figure masks into one detached Material source.</summary>
        /// <param name="current">The committed current Material entries.</param>
        /// <param name="desired">The desired Material entries.</param>
        /// <param name="currentMesh">The committed current Mesh entries whose masks remain active.</param>
        /// <param name="desiredMesh">The desired Mesh entries whose masks become active.</param>
        /// <param name="source">Detached Material StackMachine source on success.</param>
        /// <param name="diagnostic">A structured reject for invalid or ambiguous Material or Mesh mask entries.</param>
        /// <returns><see langword="true"/> when a detached source was compiled.</returns>
        public static bool TryCompile(
            IReadOnlyList<ShapeSyncMergedEntry> current,
            IReadOnlyList<ShapeSyncMergedEntry> desired,
            IReadOnlyList<ShapeSyncMergedEntry> currentMesh,
            IReadOnlyList<ShapeSyncMergedEntry> desiredMesh,
            out string source,
            out StackMachineDiagnostic diagnostic)
        {
            source = string.Empty; diagnostic = null;
            if (current == null || desired == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaterialEntriesRequired", "Material compile requires current and desired merged entries."); return false; }
            if (!TryGroup(current, out Dictionary<string, MaterialGroup> currentGroups, out diagnostic) || !TryGroup(desired, out Dictionary<string, MaterialGroup> desiredGroups, out diagnostic)) return false;
            if (!TryCollectMasks(currentMesh, out Dictionary<string, List<MaskContribution>> currentMasks, out diagnostic) ||
                !TryCollectMasks(desiredMesh, out Dictionary<string, List<MaskContribution>> desiredMasks, out diagnostic)) return false;
            ApplyMasks(currentGroups, currentMasks);
            ApplyMasks(desiredGroups, desiredMasks);

            var resetTargets = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (KeyValuePair<string, MaterialGroup> pair in currentGroups)
            {
                if (!desiredGroups.TryGetValue(pair.Key, out MaterialGroup desiredGroup) || RequiresReset(pair.Value, desiredGroup)) resetTargets.Add(pair.Value.RegistryId);
            }

            var targets = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (MaterialGroup group in currentGroups.Values) targets.Add(group.RegistryId);
            foreach (MaterialGroup group in desiredGroups.Values) targets.Add(group.RegistryId);
            var orderedDesired = new List<MaterialGroup>(desiredGroups.Values);
            orderedDesired.Sort(CompareGroup);

            var builder = new StringBuilder("FIGURE");
            foreach (string target in targets)
            {
                if (target.Length != 0) Line(builder, "$" + target + " OUTFIT");
                bool targetReset = resetTargets.Contains(target);
                if (targetReset) Line(builder, "MATERIAL_RESET");
                for (int i = 0; i < orderedDesired.Count; i++)
                {
                    MaterialGroup desiredGroup = orderedDesired[i];
                    if (desiredGroup.RegistryId != target) continue;
                    currentGroups.TryGetValue(GroupKey(desiredGroup.RegistryId, desiredGroup.ProxyEntry), out MaterialGroup currentGroup);
                    bool textureChanged = currentGroup == null || !TexturesEqual(currentGroup.Textures, desiredGroup.Textures) || !MasksEqual(currentGroup.Masks, desiredGroup.Masks);
                    bool colorChanged = currentGroup == null || !ColorEqual(currentGroup.Color, desiredGroup.Color);
                    bool uvsetChanged = currentGroup == null || !UvsetEqual(currentGroup.Uvset, desiredGroup.Uvset);
                    if (!targetReset && !textureChanged && !colorChanged && !uvsetChanged) continue;

                    Line(builder, "$" + desiredGroup.ProxyEntry + " MATERIAL");
                    if ((targetReset || textureChanged) && !AppendTexture(builder, desiredGroup.Textures, desiredGroup.Masks, out diagnostic)) return false;
                    if ((targetReset || colorChanged) && desiredGroup.Color != null) Line(builder, ColorLine(desiredGroup.Color.Color) + " COLOR");
                    if ((targetReset || uvsetChanged) && desiredGroup.Uvset != null) Line(builder, UvsetLine(desiredGroup.Uvset) + " UVSET");
                }
            }
            source = builder.ToString(); return true;
        }

        /// <summary>Compiles the Figure-only target-wide reset prefix for a recovery Material recipe.</summary>
        /// <param name="source">Detached Figure reset source on success.</param>
        /// <param name="diagnostic">A structured compilation diagnostic.</param>
        /// <returns><see langword="true"/> when the reset source was created.</returns>
        /// <remarks>Recovery Mesh processing performs <c>DETACH_ALL</c> then re-attaches the physical Outfit snapshot, so each Outfit starts from its source Material and requires no reset.</remarks>
        public static bool TryCompileReset(out string source, out StackMachineDiagnostic diagnostic)
        {
            source = string.Empty;
            diagnostic = null;
            source = "FIGURE\nMATERIAL_RESET";
            return true;
        }

        private static bool TryGroup(IReadOnlyList<ShapeSyncMergedEntry> entries, out Dictionary<string, MaterialGroup> groups, out StackMachineDiagnostic diagnostic)
        {
            groups = new Dictionary<string, MaterialGroup>(System.StringComparer.Ordinal); diagnostic = null;
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialEntry entry = entries[i]?.Entry as MaterialEntry;
                if (entry == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaterialEntryRequired", "Material compile requires MaterialEntry values."); return false; }
                string registryId = entry.RegistryId ?? string.Empty;
                string proxyEntry = entry.ProxyEntry ?? string.Empty;
                if (proxyEntry.Length == 0) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaterialProxyEntryRequired", "MaterialEntry requires a proxy entry."); return false; }
                string key = GroupKey(registryId, proxyEntry);
                if (!groups.TryGetValue(key, out MaterialGroup group)) { group = new MaterialGroup(registryId, proxyEntry); groups.Add(key, group); }
                if (entry is TextureEntry texture) group.Textures.Add(texture);
                else if (entry is ColorEntry color)
                {
                    if (group.Color != null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateColorEntry", "A Material target may contain only one ColorEntry.", bindingName: proxyEntry); return false; }
                    group.Color = color;
                }
                else if (entry is UvsetEntry uvset)
                {
                    if (group.Uvset != null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateUvsetEntry", "A Material target may contain only one UvsetEntry.", bindingName: proxyEntry); return false; }
                    group.Uvset = uvset;
                }
                else { diagnostic = StackMachineDiagnostic.CreateDomain("director", "UnsupportedMaterialEntry", "Material compile received an unsupported MaterialEntry."); return false; }
            }
            return true;
        }

        private static bool TryCollectMasks(IReadOnlyList<ShapeSyncMergedEntry> entries, out Dictionary<string, List<MaskContribution>> masks, out StackMachineDiagnostic diagnostic)
        {
            masks = new Dictionary<string, List<MaskContribution>>(System.StringComparer.Ordinal);
            diagnostic = null;
            if (entries == null) return true;
            for (int i = 0; i < entries.Count; i++)
            {
                ShapeSyncMergedEntry merged = entries[i];
                if (merged == null || !(merged.Entry is MeshEntry mesh))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "MeshEntryRequired", "Mesh mask lower requires MeshEntry values.", detail: i.ToString(CultureInfo.InvariantCulture));
                    return false;
                }
                if (mesh.Masks == null) continue;
                for (int maskIndex = 0; maskIndex < mesh.Masks.Count; maskIndex++)
                {
                    MeshMaskEntry mask = mesh.Masks[maskIndex];
                    if (mask == null)
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("director", "MeshMaskEntryRequired", "MeshEntry mask lists cannot contain null entries.", detail: i.ToString(CultureInfo.InvariantCulture));
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(mask.ProxyEntryName))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaterialProxyEntryRequired", "MeshMaskEntry requires a Figure Material Proxy entry.", detail: i.ToString(CultureInfo.InvariantCulture));
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(mask.MaskName))
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaskLogicalNameRequired", "MeshMaskEntry requires a mask logical name.", bindingName: mask.ProxyEntryName, detail: maskIndex.ToString(CultureInfo.InvariantCulture));
                        return false;
                    }
                    string key = GroupKey(string.Empty, mask.ProxyEntryName);
                    if (!masks.TryGetValue(key, out List<MaskContribution> list))
                    {
                        list = new List<MaskContribution>();
                        masks.Add(key, list);
                    }
                    list.Add(new MaskContribution(mask.MaskName, merged.Priority, merged.ListPosition, merged.ShapeId, maskIndex));
                }
            }
            foreach (List<MaskContribution> list in masks.Values) list.Sort(CompareMask);
            return true;
        }

        private static void ApplyMasks(Dictionary<string, MaterialGroup> groups, Dictionary<string, List<MaskContribution>> masks)
        {
            foreach (KeyValuePair<string, List<MaskContribution>> pair in masks)
            {
                int separator = pair.Key.IndexOf('\n');
                string registryId = separator < 0 ? string.Empty : pair.Key.Substring(0, separator);
                string proxyEntry = separator < 0 ? pair.Key : pair.Key.Substring(separator + 1);
                if (!groups.TryGetValue(pair.Key, out MaterialGroup group))
                {
                    group = new MaterialGroup(registryId, proxyEntry);
                    groups.Add(pair.Key, group);
                }
                group.Masks.Clear();
                for (int i = 0; i < pair.Value.Count; i++) group.Masks.Add(pair.Value[i].LogicalName);
            }
        }

        private static int CompareMask(MaskContribution left, MaskContribution right)
        {
            int result = left.Priority.CompareTo(right.Priority);
            if (result != 0) return result;
            result = left.ListPosition.CompareTo(right.ListPosition);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.ShapeId, right.ShapeId);
            return result != 0 ? result : left.DeclarationIndex.CompareTo(right.DeclarationIndex);
        }

        private static bool AppendTexture(StringBuilder builder, List<TextureEntry> textures, List<string> masks, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (textures.Count == 0 && masks.Count == 0) return true;
            Line(builder, "TEXTURE");
            if (textures.Count == 0)
            {
                Line(builder, "$current CANVAS");
            }
            else
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    TextureEntry t = textures[i];
                    if (string.IsNullOrEmpty(t.LogicalName)) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "TextureLogicalNameRequired", "TextureEntry requires a logical name."); return false; }
                    string line = "$" + t.LogicalName + (i == 0 ? " CANVAS" : "");
                    if (t.UseColor) line += " " + ColorizeLine(t.Color);
                    if (i != 0) line += " ACOPY";
                    Line(builder, line);
                }
            }
            for (int i = 0; i < masks.Count; i++) Line(builder, "$" + masks[i] + (i == 0 ? string.Empty : " MULTIPLY"));
            if (masks.Count != 0) Line(builder, "ALPHA");
            Line(builder, "."); Line(builder, "ENDTEXTURE"); return true;
        }

        private static bool TexturesEqual(List<TextureEntry> left, List<TextureEntry> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++) if (left[i].LogicalName != right[i].LogicalName || left[i].UseColor != right[i].UseColor || !left[i].Color.Equals(right[i].Color)) return false;
            return true;
        }

        private static bool MasksEqual(List<string> left, List<string> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static bool ColorEqual(ColorEntry left, ColorEntry right) => left == null ? right == null : right != null && left.Color.Equals(right.Color);
        private static bool UvsetEqual(UvsetEntry left, UvsetEntry right) => left == null ? right == null : right != null && left.ScaleX == right.ScaleX && left.ScaleY == right.ScaleY && left.OffsetX == right.OffsetX && left.OffsetY == right.OffsetY;
        private static bool RequiresReset(MaterialGroup current, MaterialGroup desired)
            => current.Textures.Count != 0 && desired.Textures.Count == 0 || current.Masks.Count != 0 && desired.Masks.Count == 0 || current.Color != null && desired.Color == null || current.Uvset != null && desired.Uvset == null;
        private static int CompareGroup(MaterialGroup left, MaterialGroup right)
        {
            int result = string.CompareOrdinal(left.RegistryId, right.RegistryId);
            return result != 0 ? result : string.CompareOrdinal(left.ProxyEntry, right.ProxyEntry);
        }
        private static string GroupKey(string registryId, string proxyEntry) => registryId + "\n" + proxyEntry;
        private static string ColorizeLine(Color32 color) { Color linear = ((Color)color).linear; Color.RGBToHSV(linear, out float h, out float s, out _); return F(h) + " " + F(s) + " 0 COLORIZE"; }
        private static string ColorLine(Color32 color) { Color linear = ((Color)color).linear; return F(linear.r) + " " + F(linear.g) + " " + F(linear.b) + " " + F(color.a / 255f); }
        private static string UvsetLine(UvsetEntry uvset) => F(uvset.ScaleX) + " " + F(uvset.ScaleY) + " " + F(uvset.OffsetX) + " " + F(uvset.OffsetY);
        private static string F(float value) => value.ToString("G7", CultureInfo.InvariantCulture);
        private static void Line(StringBuilder builder, string value) { builder.Append('\n').Append(value); }

        private sealed class MaterialGroup
        {
            public MaterialGroup(string registryId, string proxyEntry) { RegistryId = registryId; ProxyEntry = proxyEntry; }
            public string RegistryId { get; }
            public string ProxyEntry { get; }
            public List<TextureEntry> Textures { get; } = new List<TextureEntry>();
            public List<string> Masks { get; } = new List<string>();
            public ColorEntry Color { get; set; }
            public UvsetEntry Uvset { get; set; }
        }

        private sealed class MaskContribution
        {
            public MaskContribution(string logicalName, int priority, int listPosition, string shapeId, int declarationIndex)
            { LogicalName = logicalName; Priority = priority; ListPosition = listPosition; ShapeId = shapeId; DeclarationIndex = declarationIndex; }
            public string LogicalName { get; }
            public int Priority { get; }
            public int ListPosition { get; }
            public string ShapeId { get; }
            public int DeclarationIndex { get; }
        }
    }
}
