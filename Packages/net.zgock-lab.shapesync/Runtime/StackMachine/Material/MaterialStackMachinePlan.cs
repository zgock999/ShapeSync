// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>One immutable logical Material block compiled before any Texture execution or Material mutation.</summary>
    public sealed class MaterialStackMachineBlock
    {
        internal MaterialStackMachineBlock(string bindingName, string textureSource, bool hasColor, Color color, bool hasUvTransform, Vector2 uvScale, Vector2 uvOffset, bool isReset = false)
        { BindingName = bindingName; TextureSource = textureSource; HasColor = hasColor; Color = color; HasUvTransform = hasUvTransform; UvScale = uvScale; UvOffset = uvOffset; IsReset = isReset; }

        /// <summary>Gets the logical Material binding name without the Forth <c>$</c> prefix.</summary>
        public string BindingName { get; }
        /// <summary>Gets the buffered BaseColor Texture recipe source, or <see langword="null"/> when absent.</summary>
        public string TextureSource { get; }
        /// <summary>Gets whether this block declares a color semantic.</summary>
        public bool HasColor { get; }
        /// <summary>Gets the declared Linear RGBA color.</summary>
        public Color Color { get; }
        /// <summary>Gets whether this block declares a UV transform semantic.</summary>
        public bool HasUvTransform { get; }
        /// <summary>Gets the declared texture coordinate scale.</summary>
        public Vector2 UvScale { get; }
        /// <summary>Gets the declared texture coordinate offset.</summary>
        public Vector2 UvOffset { get; }
        /// <summary>Gets whether this item resets the entry runtime clone from its remembered source Material.</summary>
        public bool IsReset { get; }
    }

    /// <summary>Immutable Material-domain transaction plan containing all blocks before escrow begins.</summary>
    public sealed class MaterialStackMachinePlan
    {
        internal MaterialStackMachinePlan(MaterialStackMachineBlock[] blocks) => Blocks = Array.AsReadOnly(blocks);
        /// <summary>Gets the Material blocks in source order.</summary>
        public IReadOnlyList<MaterialStackMachineBlock> Blocks { get; }
    }

    /// <summary>One Figure or attached-Outfit target-local Material source subset.</summary>
    public sealed class MaterialTargetSource
    {
        internal MaterialTargetSource(string registryId, string source) { OutfitRegistryId = registryId; Source = source; }
        /// <summary>Gets <see langword="null"/> for Figure or the physical attached Outfit RegistryId.</summary>
        public string OutfitRegistryId { get; }
        /// <summary>Gets the source subset with target directives removed.</summary>
        public string Source { get; }
    }

    /// <summary>Splits flat Spec15.3 <c>FIGURE</c> and <c>$registryId OUTFIT</c> scopes into target-local Material sources.</summary>
    public static class MaterialTargetScopeParser
    {
        /// <summary>Parses flat target scopes without compiling or executing Material blocks.</summary>
        /// <param name="wordSource">The complete Material recipe source containing optional target directives.</param>
        /// <param name="targets">Source-order-preserving target-local source subsets with directives removed.</param>
        /// <param name="diagnostic">A structured scope grammar diagnostic on rejection.</param>
        /// <returns><see langword="true"/> when every target directive occurs at a flat Material boundary.</returns>
        public static bool TryParse(string wordSource, out IReadOnlyList<MaterialTargetSource> targets, out StackMachineDiagnostic diagnostic)
        {
            targets = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(wordSource)) return Fail("MaterialSourceRequired", "Material StackMachine source is required.", out diagnostic);
            string[] tokens = wordSource.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var order = new List<string>();
            var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string current = string.Empty;
            order.Add(current);
            sources.Add(current, new List<string>());
            bool texture = false;
            bool material = false;
            int pendingNumbers = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token == "TEXTURE") { texture = true; sources[current].Add(token); continue; }
                if (token == "ENDTEXTURE") { texture = false; sources[current].Add(token); continue; }
                if (token == "FIGURE")
                {
                    if (texture || (material && pendingNumbers != 0)) return Fail("MaterialTargetNestingInvalid", "FIGURE is not valid inside TEXTURE or a Material block with pending scalar values.", out diagnostic);
                    material = false;
                    current = string.Empty;
                    continue;
                }
                if (token.StartsWith("$", StringComparison.Ordinal) && i + 1 < tokens.Length && tokens[i + 1] == "OUTFIT")
                {
                    if (texture || token.Length == 1 || (material && pendingNumbers != 0)) return Fail("MaterialTargetNestingInvalid", "OUTFIT is valid only at a flat target boundary.", out diagnostic);
                    material = false;
                    current = token.Substring(1);
                    if (!sources.ContainsKey(current)) { order.Add(current); sources.Add(current, new List<string>()); }
                    i++;
                    continue;
                }
                if (token == "OUTFIT") return Fail("MaterialTargetInvalid", "OUTFIT requires one preceding $registryId target directive.", out diagnostic);
                if (!texture && token == "MATERIAL") { material = true; pendingNumbers = 0; sources[current].Add(token); continue; }
                if (!texture && (token == "COLOR" || token == "UVSET")) { pendingNumbers = 0; sources[current].Add(token); continue; }
                if (!texture && token.StartsWith("$", StringComparison.Ordinal)) { material = false; pendingNumbers = 0; sources[current].Add(token); continue; }
                if (!texture && material && double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) { pendingNumbers++; sources[current].Add(token); continue; }
                sources[current].Add(token);
            }
            if (texture) return Fail("MaterialTextureInvalid", "TEXTURE requires ENDTEXTURE before the source ends.", out diagnostic);
            var result = new List<MaterialTargetSource>();
            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];
                List<string> source = sources[key];
                if (source.Count == 0) continue;
                result.Add(new MaterialTargetSource(key.Length == 0 ? null : key, string.Join(" ", source)));
            }
            targets = result;
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message); return false; }
    }

    /// <summary>Parses the closed Material StackMachine grammar without touching Texture, Attacher, Proxy, or scene state.</summary>
    public static class MaterialStackMachineParser
    {
        private enum ValueKind { Number, Binding, Color }
        private readonly struct Value
        {
            internal readonly ValueKind Kind;
            internal readonly double Number;
            internal readonly string Binding;
            internal readonly Color Color;
            internal Value(double number) { Kind = ValueKind.Number; Number = number; Binding = null; Color = default; }
            internal Value(string binding) { Kind = ValueKind.Binding; Number = 0d; Binding = binding; Color = default; }
            internal Value(Color color) { Kind = ValueKind.Color; Number = 0d; Binding = null; Color = color; }
        }
        private sealed class Builder { internal string binding; internal string texture; internal bool color; internal Color colorValue; internal bool uv; internal Vector2 scale; internal Vector2 offset; internal bool HasSemantic => texture != null || color || uv; internal MaterialStackMachineBlock Build() => new MaterialStackMachineBlock(binding, texture, color, colorValue, uv, scale, offset); }

        /// <summary>Parses complete Material StackMachine source into a detached transaction plan.</summary>
        /// <param name="wordSource">Canonical Forth source containing one or more Material blocks.</param>
        /// <param name="plan">The detached transaction plan on success.</param>
        /// <param name="diagnostic">The structured grammar diagnostic on rejection.</param>
        /// <returns><see langword="true"/> when the source is valid without executing any lower layer.</returns>
        public static bool TryParse(string wordSource, out MaterialStackMachinePlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null; diagnostic = null;
            if (string.IsNullOrWhiteSpace(wordSource)) return Fail("MaterialSourceRequired", "Material StackMachine source is required.", out diagnostic);
            string[] tokens = wordSource.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var values = new List<Value>(); var blocks = new List<MaterialStackMachineBlock>(); var materialBindings = new HashSet<string>(StringComparer.Ordinal); Builder current = null;
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token == "TEXTURE")
                {
                    if (current == null || current.texture != null || values.Count != 0) return Fail("MaterialTextureInvalid", "TEXTURE requires one active MATERIAL block with no pending values and may appear once.", out diagnostic);
                    var buffer = new List<string>(); bool closed = false;
                    for (++i; i < tokens.Length; i++)
                    {
                        string nested = tokens[i];
                        if (nested == "ENDTEXTURE") { closed = true; break; }
                        if (nested == "TEXTURE" || nested == "MATERIAL" || nested == "COLOR" || nested == "UVSET" || nested == "NORMAL" || nested == "ENDNORMAL") return Fail("MaterialNestingInvalid", "TEXTURE buffer may contain only Texture StackMachine recipe words.", out diagnostic);
                        buffer.Add(nested);
                    }
                    if (!closed || buffer.Count == 0) return Fail("MaterialTextureInvalid", "TEXTURE requires a nonempty ENDTEXTURE-terminated recipe.", out diagnostic);
                    current.texture = string.Join(" ", buffer); continue;
                }
                if (token == "ENDTEXTURE") return Fail("MaterialTextureInvalid", "ENDTEXTURE requires a preceding TEXTURE.", out diagnostic);
                if (token == "NORMAL" || token == "ENDNORMAL") return Fail("MaterialNormalUnsupported", "NORMAL belongs to the Mesh route and is not a Material StackMachine word.", out diagnostic);
                if (token == "MATERIAL")
                {
                    if (values.Count != 1 || values[0].Kind != ValueKind.Binding) return Fail("MaterialBindingRequired", "MATERIAL requires exactly one preceding logical binding.", out diagnostic);
                    if (current != null)
                    {
                        if (!current.HasSemantic) return Fail("MaterialSemanticRequired", "MATERIAL requires at least one TEXTURE, COLOR, or UVSET semantic.", out diagnostic);
                        blocks.Add(current.Build());
                    }
                    if (!materialBindings.Add(values[0].Binding)) return Fail("MaterialBindingDuplicate", "MATERIAL may declare each logical binding once per transaction.", out diagnostic);
                    current = new Builder { binding = values[0].Binding }; values.Clear(); continue;
                }
                if (token == "MATERIAL_RESET")
                {
                    if (current != null || values.Count != 0) return Fail("MaterialResetInvalid", "MATERIAL_RESET is a target-wide word valid only at a flat Material boundary.", out diagnostic);
                    blocks.Add(new MaterialStackMachineBlock(null, null, false, default, false, default, default, true));
                    continue;
                }
                if (token == "COLOR" || token == "UVSET")
                {
                    if (current == null) return Fail("MaterialStackUnderflow", token + " requires values inside MATERIAL.", out diagnostic);
                    if (token == "COLOR" && values.Count == 1 && values[0].Kind == ValueKind.Color)
                    {
                        if (current.color) return Fail("MaterialDuplicateSemantic", "COLOR may appear once per MATERIAL block.", out diagnostic);
                        current.color = true;
                        current.colorValue = values[0].Color;
                    }
                    else
                    {
                        if (values.Count != 4) return Fail("MaterialStackUnderflow", token + " requires exactly four Number values inside MATERIAL.", out diagnostic);
                        for (int v = 0; v < 4; v++) if (values[v].Kind != ValueKind.Number || double.IsNaN(values[v].Number) || double.IsInfinity(values[v].Number)) return Fail("MaterialValueInvalid", token + " requires finite Number values.", out diagnostic);
                        if (token == "COLOR") { if (current.color) return Fail("MaterialDuplicateSemantic", "COLOR may appear once per MATERIAL block.", out diagnostic); for (int v = 0; v < 4; v++) if (values[v].Number < 0d || values[v].Number > 1d) return Fail("MaterialColorInvalid", "COLOR requires Linear RGBA values in [0,1].", out diagnostic); current.color = true; current.colorValue = new Color((float)values[0].Number, (float)values[1].Number, (float)values[2].Number, (float)values[3].Number); }
                        else { if (current.uv) return Fail("MaterialDuplicateSemantic", "UVSET may appear once per MATERIAL block.", out diagnostic); current.uv = true; current.scale = new Vector2((float)values[0].Number, (float)values[1].Number); current.offset = new Vector2((float)values[2].Number, (float)values[3].Number); }
                    }
                    values.Clear(); continue;
                }
                if (token.StartsWith("$", StringComparison.Ordinal)) { if (current != null) { if (values.Count != 0) return Fail("MaterialStackNotEmpty", "A MATERIAL block cannot close with pending values.", out diagnostic); if (!current.HasSemantic) return Fail("MaterialSemanticRequired", "MATERIAL requires at least one TEXTURE, COLOR, or UVSET semantic.", out diagnostic); blocks.Add(current.Build()); current = null; } if (token.Length == 1 || values.Count != 0) return Fail("MaterialBindingRequired", "MATERIAL requires one unique preceding logical binding.", out diagnostic); values.Add(new Value(token.Substring(1))); continue; }
                if (token.StartsWith("#", StringComparison.Ordinal))
                {
                    if (current == null) return Fail("MaterialValueOutsideBlock", "#RRGGBBAA is valid only inside MATERIAL before COLOR.", out diagnostic);
                    if (values.Count != 0) return Fail("MaterialStackNotEmpty", "#RRGGBBAA must be the only pending COLOR value.", out diagnostic);
                    if (!TryParseSrgbHexColor(token, out Color color)) return Fail("MaterialColorHexInvalid", "COLOR hex literals must use exact #RRGGBBAA syntax.", out diagnostic);
                    values.Add(new Value(color));
                    continue;
                }
                if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)) { if (current == null) return Fail("MaterialValueOutsideBlock", "Number values are valid only inside MATERIAL.", out diagnostic); values.Add(new Value(number)); continue; }
                return Fail("MaterialUnknownWord", "Unknown Material StackMachine word: " + token, out diagnostic);
            }
            if (values.Count != 0) return Fail("MaterialStackNotEmpty", "Material StackMachine source ended with pending values.", out diagnostic);
            if (current != null)
            {
                if (!current.HasSemantic) return Fail("MaterialSemanticRequired", "MATERIAL requires at least one TEXTURE, COLOR, or UVSET semantic.", out diagnostic);
                blocks.Add(current.Build());
            }
            if (blocks.Count == 0) return Fail("MaterialBlockRequired", "Material StackMachine source requires one MATERIAL block.", out diagnostic);
            plan = new MaterialStackMachinePlan(blocks.ToArray()); return true;
        }

        private static bool TryParseSrgbHexColor(string token, out Color color)
        {
            color = default;
            if (token == null || token.Length != 9 || token[0] != '#') return false;
            if (!TryHexByte(token, 1, out byte red) || !TryHexByte(token, 3, out byte green) || !TryHexByte(token, 5, out byte blue) || !TryHexByte(token, 7, out byte alpha)) return false;
            color = new Color(red / 255f, green / 255f, blue / 255f, alpha / 255f).linear;
            return true;
        }

        private static bool TryHexByte(string value, int index, out byte result)
        {
            result = 0;
            int high = Hex(value[index]);
            int low = Hex(value[index + 1]);
            if (high < 0 || low < 0) return false;
            result = (byte)((high << 4) | low);
            return true;
        }

        private static int Hex(char value) => value >= '0' && value <= '9' ? value - '0' : value >= 'A' && value <= 'F' ? value - 'A' + 10 : value >= 'a' && value <= 'f' ? value - 'a' + 10 : -1;

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message); return false; }
    }
}
