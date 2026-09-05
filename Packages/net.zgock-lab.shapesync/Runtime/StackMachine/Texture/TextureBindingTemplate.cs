// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    [Serializable]
    /// <summary>One authorable logical binding used to construct a Texture recipe stub.</summary>
    public sealed class TextureTemplateEntry
    {
        /// <summary>Gets or sets the logical name without the Forth <c>$</c> prefix.</summary>
        public string word;
        /// <summary>Gets or sets the source texture when <see cref="kind"/> is <see cref="TextureBindingKind.SourceTexture"/>.</summary>
        public Texture texture;
        /// <summary>Gets or sets whether this entry is a source texture or the reserved output hall.</summary>
        public TextureBindingKind kind;
    }

    /// <summary>ScriptableObject authoring template for non-persistent Texture StackMachine bindings.</summary>
    [CreateAssetMenu(menuName = "zgock/ShapeSync/StackMachine/Texture Binding Template")]
    public sealed class TextureBindingTemplate : ScriptableObject
    {
        [SerializeField] private List<TextureTemplateEntry> bindings = new List<TextureTemplateEntry>
        {
            new TextureTemplateEntry { word = "out", kind = TextureBindingKind.OutputHall }
        };

        /// <summary>Gets the configured binding entries in authoring order.</summary>
        public IReadOnlyList<TextureTemplateEntry> Bindings => bindings;

        /// <summary>Replaces the configured bindings with copies of the supplied entries.</summary>
        /// <param name="entries">Entries to copy, or <see langword="null"/> to clear the template.</param>
        public void SetBindings(IReadOnlyList<TextureTemplateEntry> entries)
        {
            bindings.Clear();
            if (entries == null) return;

            for (int i = 0; i < entries.Count; i++)
            {
                TextureTemplateEntry entry = entries[i];
                bindings.Add(entry == null ? null : new TextureTemplateEntry
                {
                    word = entry.word,
                    texture = entry.texture,
                    kind = entry.kind
                });
            }
        }

        /// <summary>Builds a validated non-persistent recipe stub from this template.</summary>
        /// <param name="document">Recipe document to pair with the configured bindings.</param>
        /// <param name="stub">Validated stub on success; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic">Validation diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when this template supplies exactly one reserved output hall and valid bindings.</returns>
        public bool TryCreateStub(MaterialRecipeDocument document, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic)
        {
            stub = null;
            if (document == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TemplateDocumentRequired", "TextureBindingTemplate requires a MaterialRecipeDocument.");
                return false;
            }

            TextureTemplateEntry output = null;
            for (int i = 0; i < bindings.Count; i++)
            {
                TextureTemplateEntry entry = bindings[i];
                if (entry != null && entry.kind == TextureBindingKind.OutputHall)
                {
                    if (output != null)
                    {
                        diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputBindingInvalid", "TextureBindingTemplate requires exactly one OutputHall.");
                        return false;
                    }

                    output = entry;
                }
            }

            if (output == null || output.word != "out")
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("texture", "OutputBindingInvalid", "TextureBindingTemplate requires its one OutputHall to use the reserved logical name out.");
                return false;
            }

            var entries = new TextureBindingEntry[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                TextureTemplateEntry entry = bindings[i];
                if (entry == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("texture", "TemplateEntryInvalid", "TextureBindingTemplate contains a null entry.");
                    return false;
                }

                entries[i] = new TextureBindingEntry
                {
                    logicalName = entry.word,
                    sourceTexture = entry.texture,
                    kind = entry.kind
                };
            }

            var candidate = new TextureRecipeStub(document, entries);
            if (!TextureBindingContext.TryCreate(candidate, out _, out diagnostic)) return false;

            stub = candidate;
            return true;
        }
    }
}
