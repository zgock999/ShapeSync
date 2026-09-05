// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Detached Mesh-owned template for one logical Normal binding.</summary>
    /// <remarks>The source contains one <c>NORMAL_BASE</c> and one terminal <c>NORMAL_FINALIZE</c>. Non-PBM target delta lines are expanded between them from the Mesh-owned target texture dictionary and a DDB snapshot; PBM-prefixed source names are rejected.</remarks>
    public sealed class NormalRecipeTemplate
    {
        /// <summary>Creates a template for one logical Normal binding.</summary>
        /// <param name="entryName">The configured Proxy entry name.</param>
        /// <param name="wordSource">The detached Forth source containing the Normal block.</param>
        public NormalRecipeTemplate(string entryName, string wordSource) { EntryName = entryName; WordSource = wordSource; }
        /// <summary>Gets the MeshBindingTemplate logical Normal binding name.</summary>
        public string EntryName { get; }
        /// <summary>Gets the detached Forth template source.</summary>
        public string WordSource { get; }
    }

    /// <summary>Resolved Mesh-owned source map for one non-PBM Normal target.</summary>
    [Serializable]
    public sealed class NormalTargetSource
    {
        /// <summary>Non-PBM target name that supplies this source's raw weight.</summary>
        public string targetName;
        /// <summary>Pre-delta encoded Normal texture.</summary>
        public Texture2D texture;
    }

    /// <summary>Expands a Mesh-owned Normal template into one non-persistent Texture StackMachine stub.</summary>
    public static class NormalRecipeExpander
    {
        /// <summary>Builds a detached Texture recipe using raw DDB weights and resolved Mesh target source maps.</summary>
        /// <remarks>Only active non-PBM source maps are inserted with their raw snapshot weights. PBM targets never contribute a Normal delta. No weight clamping or normalization occurs here.</remarks>
        /// <param name="template">The Mesh-owned Normal block template.</param>
        /// <param name="baseTexture">The required Base Normal texture.</param>
        /// <param name="targets">The Mesh-owned non-PBM target texture maps.</param>
        /// <param name="snapshot">The latest raw non-PBM DDB weight snapshot.</param>
        /// <param name="stub">The detached Texture StackMachine recipe on success.</param>
        /// <param name="diagnostic">The structured validation diagnostic on rejection.</param>
        /// <returns><see langword="true"/> when the template and source maps expand into a valid detached recipe.</returns>
        public static bool TryCreateStub(NormalRecipeTemplate template, Texture2D baseTexture, IReadOnlyList<NormalTargetSource> targets, IReadOnlyList<NormalTargetWeight> snapshot, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic)
        {
            stub = null;
            if (template == null || string.IsNullOrWhiteSpace(template.EntryName) || string.IsNullOrWhiteSpace(template.WordSource) || baseTexture == null) return Fail("NormalTemplateInvalid", "Normal template, logical Proxy entry name, and Base texture are required.", out diagnostic);
            string[] tokens = template.WordSource.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int baseCount = 0, finalizeIndex = -1;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == TextureWordSet.NormalBase) baseCount++;
                if (tokens[i] == TextureWordSet.NormalFinalize) { if (finalizeIndex >= 0) return Fail("NormalTemplateInvalid", "Normal template requires exactly one NORMAL_FINALIZE.", out diagnostic); finalizeIndex = i; }
                if (tokens[i] == TextureWordSet.NormalDeltaAdd || tokens[i] == TextureWordSet.Publish) return Fail("NormalTemplateInvalid", "Normal template reserves delta expansion and publish for Mesh StackMachine.", out diagnostic);
                if (tokens[i].StartsWith("$", StringComparison.Ordinal) && tokens[i] != "$base") return Fail("NormalTemplateInvalid", "Normal template may reference only $base; non-PBM target sources are Mesh-owned target dictionary values.", out diagnostic);
            }
            if (baseCount != 1 || finalizeIndex < 0) return Fail("NormalTemplateInvalid", "Normal template requires exactly one NORMAL_BASE and one NORMAL_FINALIZE.", out diagnostic);
            var weights = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; snapshot != null && i < snapshot.Count; i++) if (!string.IsNullOrWhiteSpace(snapshot[i].TargetName)) weights[snapshot[i].TargetName] = snapshot[i].Enabled ? snapshot[i].Weight : 0f;
            var bindings = new List<TextureBindingEntry> { new TextureBindingEntry { logicalName = "base", kind = TextureBindingKind.SourceTexture, sourceTexture = baseTexture }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } };
            var declarations = new List<StackMachineBindingDeclaration> { new StackMachineBindingDeclaration { logicalName = "base", declaredKind = StackMachineBindingKind.Resource }, new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource } };
            var source = new StringBuilder();
            for (int i = 0; i < finalizeIndex; i++) AppendToken(source, tokens[i]);
            int sourceIndex = 0;
            if (!TryAppendTargets(source, bindings, declarations, targets, weights, ref sourceIndex, out diagnostic)) return false;
            for (int i = finalizeIndex; i < tokens.Length; i++) AppendToken(source, tokens[i]);
            AppendToken(source, TextureWordSet.Publish);
            var document = new MaterialRecipeDocument { wordSource = source.ToString(), outputLogicalName = "out", outputWidth = baseTexture.width, outputHeight = baseTexture.height };
            document.bindings.AddRange(declarations);
            stub = new TextureRecipeStub(document, bindings.ToArray());
            diagnostic = null;
            return true;
        }

        private static bool TryAppendTargets(StringBuilder source, List<TextureBindingEntry> bindings, List<StackMachineBindingDeclaration> declarations, IReadOnlyList<NormalTargetSource> targets, Dictionary<string, float> weights, ref int sourceIndex, out StackMachineDiagnostic diagnostic)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; targets != null && i < targets.Count; i++)
            {
                NormalTargetSource item = targets[i];
                if (item == null || string.IsNullOrWhiteSpace(item.targetName) || item.targetName.StartsWith(BlendShapeReservedPrefixes.Pbm, StringComparison.Ordinal) || !names.Add(item.targetName)) return Fail("NormalSourceMapInvalid", "Normal source names must be nonempty, unique names that do not begin with PBM_.", out diagnostic);
                if (!weights.TryGetValue(item.targetName, out float weight) || weight == 0f) continue;
                if (item.texture == null) return Fail("TargetTextureRequired", "An active Normal target requires a texture.", out diagnostic);
                AppendDelta(source, bindings, declarations, "target" + sourceIndex++, item.texture, weight);
            }
            diagnostic = null;
            return true;
        }

        private static void AppendDelta(StringBuilder source, List<TextureBindingEntry> bindings, List<StackMachineBindingDeclaration> declarations, string name, Texture2D texture, float weight)
        {
            bindings.Add(new TextureBindingEntry { logicalName = name, kind = TextureBindingKind.SourceTexture, sourceTexture = texture });
            declarations.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            AppendToken(source, "$base"); AppendToken(source, "$" + name); AppendToken(source, weight.ToString("R", CultureInfo.InvariantCulture)); AppendToken(source, TextureWordSet.NormalDeltaAdd);
        }

        private static void AppendToken(StringBuilder builder, string token) { if (builder.Length > 0) builder.Append(' '); builder.Append(token); }
        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("normal", code, message); return false; }
    }
}
