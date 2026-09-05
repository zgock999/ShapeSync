// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Resolves Mesh-lower ATTACH logical names to the original Document Outfit source roles without referencing UniVRM or UnityEditor.</summary>
    public static class HumanoidVrmTransportSourceResolver
    {
        /// <summary>Resolves the ordered ATTACH logical-name snapshot to detached Outfit source roots.</summary>
        public static bool TryResolveAttachedOutfitSourceRoots(ShapeSyncDocument document, IReadOnlyList<string> attachedOutfitLogicalNames, out IReadOnlyList<GameObject> sourceRoots, out StackMachineDiagnostic diagnostic)
        {
            sourceRoots = Array.Empty<GameObject>();
            diagnostic = null;
            if (document == null) return Reject("VrmTransportDocumentRequired", "VRM transport source resolution requires the detached ShapeSyncDocument.", out diagnostic);
            if (attachedOutfitLogicalNames == null) return Reject("VrmTransportLogicalNamesRequired", "VRM transport source resolution requires the Mesh-lower ATTACH logical-name snapshot.", out diagnostic);
            // A current-state Director document has no MeshBinding when it has no ATTACH
            // instructions.  Base Figure physics transport still has a complete Figure source;
            // only Outfit source resolution requires the optional binding carrier.
            if (attachedOutfitLogicalNames.Count == 0) return true;
            if (document.MeshBinding == null) return Reject("VrmTransportMeshBindingRequired", "VRM transport source resolution requires the Document MeshBinding when ATTACH Outfits are present.", out diagnostic);

            IReadOnlyList<MeshOutfitBindingEntry> entries = document.MeshBinding.Outfits;
            var seenRequestedNames = new HashSet<string>(StringComparer.Ordinal);
            var resolved = new GameObject[attachedOutfitLogicalNames.Count];
            for (int requestedIndex = 0; requestedIndex < resolved.Length; requestedIndex++)
            {
                string logicalName = attachedOutfitLogicalNames[requestedIndex];
                if (string.IsNullOrWhiteSpace(logicalName))
                    return Reject("VrmTransportOutfitSourceMissing", "VRM transport provenance refers to an Outfit not present in the same Document MeshBinding.", out diagnostic);
                if (!seenRequestedNames.Add(logicalName))
                    return Reject("VrmTransportOutfitLogicalNameDuplicate", "VRM transport provenance cannot refer to the same ATTACH Outfit more than once.", out diagnostic);

                GameObject root = null;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    MeshOutfitBindingEntry entry = entries[entryIndex];
                    if (entry == null || !string.Equals(entry.logicalName, logicalName, StringComparison.Ordinal)) continue;
                    if (entry.outfitPrefab == null)
                        return Reject("VrmTransportOutfitBindingInvalid", "VRM transport source resolution found an ATTACH Outfit binding without a source root.", out diagnostic);
                    if (root != null)
                        return Reject("VrmTransportOutfitLogicalNameDuplicate", "VRM transport source resolution found duplicate bindings for an ATTACH Outfit logical name.", out diagnostic);
                    root = entry.outfitPrefab;
                }
                if (root == null)
                    return Reject("VrmTransportOutfitSourceMissing", "VRM transport provenance refers to an Outfit not present in the same Document MeshBinding.", out diagnostic);
                resolved[requestedIndex] = root;
            }
            sourceRoots = Array.AsReadOnly(resolved);
            return true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }
}
