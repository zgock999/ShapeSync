// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Non-persistent route from a logical <c>MATERIAL</c> word to the identically named co-located Attacher entry.</summary>
    /// <remarks>The context does not add transaction behavior to a Proxy or Attacher and does not duplicate Proxy entry names in another dictionary.</remarks>
    public sealed class MaterialBindingContext
    {
        /// <summary>One resolved Material logical route.</summary>
        public readonly struct Binding
        {
            internal Binding(string logicalName, MaterialAttacher attacher, string entryName)
            { LogicalName = logicalName; Attacher = attacher; EntryName = entryName; }

            /// <summary>Logical recipe name without the Forth <c>$</c> prefix.</summary>
            public string LogicalName { get; }
            /// <summary>Existing Attacher responsible for this one entry.</summary>
            public MaterialAttacher Attacher { get; }
            /// <summary>Existing Proxy entry name passed unchanged to the Attacher.</summary>
            public string EntryName { get; }
        }

        private readonly MaterialAttacher attacher;
        private MaterialBindingContext(MaterialAttacher attacher) => this.attacher = attacher;

        /// <summary>Resolves a Material logical word to its existing one-entry Attacher route.</summary>
        /// <param name="logicalName">Recipe word without the Forth <c>$</c> prefix.</param>
        /// <param name="binding">Resolved route on success.</param>
        /// <returns><see langword="true"/> when the word is configured.</returns>
        public bool TryGetBinding(string logicalName, out Binding binding)
        {
            if (string.IsNullOrWhiteSpace(logicalName)) { binding = default; return false; }
            binding = new Binding(logicalName, attacher, logicalName);
            return true;
        }

        /// <summary>Resolves every entry currently configured by the co-located Proxy for a target-wide reset.</summary>
        /// <param name="bindings">The transient entry routes in Proxy entry order.</param>
        /// <param name="diagnostic">A structured configuration diagnostic on failure.</param>
        /// <returns><see langword="true"/> when all configured Proxy entries were enumerated.</returns>
        /// <remarks>This is execution-time enumeration only. The Material StackMachine neither caches nor owns the Proxy entry list.</remarks>
        public bool TryGetResetBindings(out IReadOnlyList<Binding> bindings, out StackMachineDiagnostic diagnostic)
        {
            bindings = null;
            if (attacher == null || attacher.Proxy == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialProxyRequired", "MATERIAL_RESET requires the co-located Attacher MaterialProxy.");
                return false;
            }
            IReadOnlyList<MaterialProxyEntry> entries = attacher.Proxy.Entries;
            var result = new List<Binding>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                MaterialProxyEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.entryName))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("material", "MaterialProxyEntryInvalid", "MATERIAL_RESET requires every MaterialProxy entry to have a name.", detail: i.ToString());
                    return false;
                }
                result.Add(new Binding(entry.entryName, attacher, entry.entryName));
            }
            bindings = result;
            diagnostic = null;
            return true;
        }

        internal static bool TryCreate(MaterialAttacher attacher, out MaterialBindingContext context, out StackMachineDiagnostic diagnostic)
        {
            context = null;
            if (attacher == null) return Fail("MaterialAttacherRequired", "Material StackMachine requires its co-located MaterialAttacher.", out diagnostic);
            context = new MaterialBindingContext(attacher);
            diagnostic = null;
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        { diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message); return false; }
    }
}
