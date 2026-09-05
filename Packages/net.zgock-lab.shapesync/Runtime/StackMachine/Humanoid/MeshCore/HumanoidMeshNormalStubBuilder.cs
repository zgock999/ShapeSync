// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Builds detached Normal Texture stubs from compiler-owned Mesh source snapshots.</summary>
    public static class HumanoidMeshNormalStubBuilder
    {
        public static bool TryCreate(HumanoidMeshFbmBakeResult bake, HumanoidMeshNormalSource source, out TextureRecipeStub stub, out StackMachineDiagnostic diagnostic)
        {
            stub = null; diagnostic = null;
            var targets = new List<NormalTargetSource>(); var weights = new List<NormalTargetWeight>();
            foreach (HumanoidMeshNormalTargetSource target in source.Targets)
            {
                if (target.TargetName.StartsWith(BlendShapeReservedPrefixes.Pbm)) continue;
                targets.Add(new NormalTargetSource { targetName = target.TargetName, texture = target.Texture });
                if (bake.FbmWeights.TryGetValue(target.TargetName, out float weight)) weights.Add(new NormalTargetWeight(target.TargetName, weight, true));
            }
            NormalRecipeTemplate template = null;
            foreach (NormalRecipeTemplate candidate in bake.LogicalPlan.CorePlan.NormalTemplates) if (candidate.EntryName == source.EntryName) { template = candidate; break; }
            if (template == null) { diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "NormalTemplateMissing", "Normal source has no matching template.", bindingName: source.EntryName); return false; }
            return NormalRecipeExpander.TryCreateStub(template, source.BaseTexture, targets, weights, out stub, out diagnostic);
        }
    }
}
