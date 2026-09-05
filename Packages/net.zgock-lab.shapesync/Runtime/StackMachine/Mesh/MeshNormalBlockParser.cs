// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Extracts Mesh-owned <c>NORMAL / ENDNORMAL</c> declarations before common Mesh bytecode compilation.</summary>
    public static class MeshNormalBlockParser
    {
        /// <summary>Extracts detached Normal templates and returns the remaining Mesh bytecode source.</summary>
        /// <param name="wordSource">Complete Mesh recipe source containing zero or more Normal blocks.</param>
        /// <param name="meshWordSource">The remaining Mesh bytecode source without Normal declarations.</param>
        /// <param name="templates">Detached Normal templates in declaration order.</param>
        /// <param name="diagnostic">The structured parse diagnostic on rejection.</param>
        /// <returns><see langword="true"/> when all Normal blocks are syntactically valid.</returns>
        public static bool TryExtract(string wordSource, out string meshWordSource, out IReadOnlyList<NormalRecipeTemplate> templates, out StackMachineDiagnostic diagnostic)
        {
            meshWordSource = null; templates = null; diagnostic = null;
            if (string.IsNullOrWhiteSpace(wordSource)) return Fail("NormalBlockInvalid", "Mesh recipe source is required.", out diagnostic);
            string[] tokens = wordSource.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var mesh = new List<string>(); var normal = new List<NormalRecipeTemplate>();
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "ENDNORMAL") return Fail("NormalBlockInvalid", "ENDNORMAL requires a preceding NORMAL declaration.", out diagnostic);
                if (tokens[i] != "NORMAL") { mesh.Add(tokens[i]); continue; }
                if (mesh.Count == 0 || !mesh[mesh.Count - 1].StartsWith("$", StringComparison.Ordinal)) return Fail("NormalBlockInvalid", "NORMAL requires one preceding logical NormalBlender entry.", out diagnostic);
                string binding = mesh[mesh.Count - 1].Substring(1);
                mesh.RemoveAt(mesh.Count - 1);
                var body = new List<string>(); bool closed = false;
                for (++i; i < tokens.Length; i++)
                {
                    if (tokens[i] == "NORMAL") return Fail("NormalBlockInvalid", "Nested NORMAL blocks are not supported.", out diagnostic);
                    if (tokens[i] == "ENDNORMAL") { closed = true; break; }
                    body.Add(tokens[i]);
                }
                if (!closed || body.Count == 0) return Fail("NormalBlockInvalid", "NORMAL requires a nonempty ENDNORMAL-terminated template.", out diagnostic);
                for (int j = 0; j < normal.Count; j++) if (normal[j].EntryName == binding) return Fail("NormalBlockInvalid", "A Mesh recipe may declare each Proxy entry Normal once.", out diagnostic);
                normal.Add(new NormalRecipeTemplate(binding, string.Join(" ", body)));
            }
            meshWordSource = string.Join(" ", mesh);
            templates = normal.AsReadOnly();
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", code, message); return false; }
    }
}
