// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Serialization-only Mesh recipe carrier. It deliberately owns no Figure or binding references.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/Mesh Recipe")]
    public class MeshRecipeAsset : StackMachineRecipeSerializationBase
    {
        [SerializeField] private int recipeFormatVersion = 1;
        [SerializeField, TextArea] private string wordSource;
        [SerializeField] private List<StackMachineBindingDeclaration> bindings = new List<StackMachineBindingDeclaration>();
        [SerializeField] private List<string> capabilities = new List<string>();
        [SerializeField] private StackMachineProvenance provenance = new StackMachineProvenance();
        [SerializeField] private List<StackMachineSourceMapEntry> diagnosticSourceMap = new List<StackMachineSourceMapEntry>();

        /// <summary>Creates a deep-copied Mesh recipe document from this serialized carrier.</summary>
        /// <returns>A detached <see cref="MeshRecipeDocument"/>.</returns>
        public override StackMachineRecipeDocument ToDocument() => Copy(new MeshRecipeDocument { recipeFormatVersion = recipeFormatVersion, wordSource = wordSource, bindings = bindings, capabilities = capabilities, provenance = provenance, diagnosticSourceMap = diagnosticSourceMap });

        /// <summary>Replaces this carrier's serialized common fields with a deep copy of a document.</summary>
        /// <param name="document">The source document. A <see langword="null"/> value creates an empty default document.</param>
        public override void SetDocument(StackMachineRecipeDocument document)
        {
            MeshRecipeDocument copy = Copy(document);
            recipeFormatVersion = copy.recipeFormatVersion;
            wordSource = copy.wordSource;
            bindings = copy.bindings;
            capabilities = copy.capabilities;
            provenance = copy.provenance;
            diagnosticSourceMap = copy.diagnosticSourceMap;
        }

        private static MeshRecipeDocument Copy(StackMachineRecipeDocument source)
        {
            var copy = new MeshRecipeDocument { recipeFormatVersion = source == null ? 1 : source.recipeFormatVersion, wordSource = source == null ? null : source.wordSource };
            if (source == null) return copy;
            if (source.bindings != null) foreach (StackMachineBindingDeclaration entry in source.bindings) copy.bindings.Add(entry == null ? null : new StackMachineBindingDeclaration { logicalName = entry.logicalName, declaredKind = entry.declaredKind });
            if (source.capabilities != null) copy.capabilities.AddRange(source.capabilities);
            copy.provenance = source.provenance == null ? new StackMachineProvenance() : new StackMachineProvenance { author = source.provenance.author, note = source.provenance.note };
            if (source.diagnosticSourceMap != null) foreach (StackMachineSourceMapEntry entry in source.diagnosticSourceMap) copy.diagnosticSourceMap.Add(entry == null ? null : new StackMachineSourceMapEntry { tokenIndex = entry.tokenIndex, sourceOffset = entry.sourceOffset });
            return copy;
        }
    }
}
