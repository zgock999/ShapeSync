// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using zgock.ShapeSync.StackMachine;
namespace zgock.ShapeSync.StackMachine.Humanoid
{
    public static class HumanoidMeshExpressionVariantBaker
    {
        public static bool TryBakeAndRegister(HumanoidMeshFbmBakeResult bake, Mesh finalBase, string expressionName, out StackMachineDiagnostic diagnostic)
        {
            return TryBakeAndRegister(bake, bake == null ? default : bake.LogicalPlan.Figure, finalBase, expressionName, out diagnostic);
        }

        public static bool TryBakeAndRegister(HumanoidMeshFbmBakeResult bake, HumanoidMeshSource owner, Mesh finalBase, string expressionName, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            string vrm = BlendShapeReservedPrefixes.Vrm + expressionName;
            if (bake == null || string.IsNullOrWhiteSpace(expressionName))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "ExpressionExpectedShapeInvalid", "Expression expected-shape build requires a source bake and expression name.");
                return false;
            }
            var names = new System.Collections.Generic.List<string> { vrm };
            var weights = new System.Collections.Generic.List<float> { 1f };
            Mesh source = owner.Renderer == null ? null : owner.Renderer.sharedMesh;
            if (source == null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "VariantSourceMeshRequired", "Expression expected-shape build requires an authoring Mesh."); return false; }
            foreach (var fbm in bake.FbmWeights)
            {
                if (source.GetBlendShapeIndex(fbm.Key) >= 0)
                {
                    names.Add(fbm.Key);
                    weights.Add(fbm.Value);
                }
                string mcm = BlendShapeReservedPrefixes.Mcm + fbm.Key + "_" + expressionName;
                if (source.GetBlendShapeIndex(mcm) >= 0)
                {
                    names.Add(mcm);
                    weights.Add(fbm.Value);
                }
            }
            if (!HumanoidMeshPbmVariantBaker.TryCreateSourceVariant(bake, owner, names.ToArray(), weights.ToArray(), out Mesh variant, out diagnostic)) return false;
            try { return HumanoidMeshPbmVariantBaker.TryRegisterExpectedShape(finalBase, variant, vrm, out diagnostic); }
            finally { HumanoidMeshResourceCleanup.Destroy(variant); }
        }
    }
}
