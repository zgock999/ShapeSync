// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>
    /// Immutable, execution-free Material recipe artifact shared by the runtime carrier and Humanoid Compiler backends.
    /// </summary>
    /// <remarks>
    /// This Core plan owns only the closed Material grammar result. It intentionally does not resolve a Figure,
    /// MaterialProxy, MaterialShaderAdapter, MaterialId, Texture completion, or any transaction state. Runtime
    /// MaterialStackMachine retains its executor, queue, TextureDelivery, escrow, and Attacher ownership.
    /// </remarks>
    public sealed class MaterialStackMachineCorePlan
    {
        private MaterialStackMachineCorePlan(MaterialStackMachinePlan commonPlan) { CommonPlan = commonPlan; }

        /// <summary>Gets the immutable Material-domain plan produced without execution or source mutation.</summary>
        public MaterialStackMachinePlan CommonPlan { get; }

        /// <summary>Gets the parsed Material blocks in source order.</summary>
        public IReadOnlyList<MaterialStackMachineBlock> Blocks => CommonPlan.Blocks;

        /// <summary>Compiles one detached Material recipe without resolving a physical target or starting Texture work.</summary>
        public static bool TryCreate(MaterialRecipeDocument document, out MaterialStackMachineCorePlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            diagnostic = null;
            if (document == null) return Fail("DocumentRequired", "Material Core plan requires a MaterialRecipeDocument.", out diagnostic);
            if (!MaterialStackMachineParser.TryParse(document.wordSource, out MaterialStackMachinePlan commonPlan, out diagnostic)) return false;
            plan = new MaterialStackMachineCorePlan(commonPlan);
            return true;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message);
            return false;
        }
    }
}
