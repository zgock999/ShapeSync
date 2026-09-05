// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Immutable, execution-free Texture compilation artifact shared by PlayMode and EditMode backends.</summary>
    /// <remarks>
    /// This plan owns only normalized recipe metadata, validated logical bindings, and the lowered dispatch plan.
    /// GPU resources, queueing, completion, cancellation, disposal, and transaction ownership remain with the
    /// concrete backend that receives this plan.
    /// </remarks>
    public sealed class TextureExecutionPlan
    {
        private TextureExecutionPlan(TextureBindingContext bindingContext, TextureDispatchPlan dispatchPlan)
        {
            BindingContext = bindingContext;
            DispatchPlan = dispatchPlan;
        }

        /// <summary>Gets the validated logical binding context required by a concrete execution backend.</summary>
        public TextureBindingContext BindingContext { get; }

        /// <summary>Gets the immutable Texture-domain dispatch plan without GPU allocations.</summary>
        public TextureDispatchPlan DispatchPlan { get; }

        /// <summary>Normalizes, validates, and lowers one Texture recipe without starting execution.</summary>
        /// <param name="stub">Caller-owned in-memory Texture recipe and bindings.</param>
        /// <param name="plan">Execution-free immutable plan on success; otherwise <see langword="null"/>.</param>
        /// <param name="diagnostic">Structured compile diagnostic on failure; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the recipe can be executed by a concrete backend.</returns>
        public static bool TryCreate(TextureRecipeStub stub, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            if (!TextureColorLiteralNormalizer.TryNormalizeForCompile(stub == null ? null : stub.Document, out MaterialRecipeDocument document, out diagnostic)) return false;

            var normalized = new TextureRecipeStub(document, stub.Bindings);
            if (!TextureBindingContext.TryCreate(normalized, out TextureBindingContext context, out diagnostic)) return false;

            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            if (!StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan commonPlan, out diagnostic)) return false;
            if (!TexturePlanCompiler.TryCompile(commonPlan, document, context, out TextureDispatchPlan dispatchPlan, out diagnostic)) return false;

            plan = new TextureExecutionPlan(context, dispatchPlan);
            return true;
        }
    }
}
