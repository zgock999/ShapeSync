// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Builds one execution-free BaseColor Texture plan from a compiler Material block.</summary>
    public static class HumanoidMaterialTexturePlanBuilder
    {
        /// <summary>Creates a Texture plan without GPU dispatch, completion ownership, or Material mutation.</summary>
        public static bool TryCreate(MaterialStackMachineBlock block, MaterialRecipeDocument targetDocument, MaterialBinding binding, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            diagnostic = null;
            if (block == null || string.IsNullOrWhiteSpace(block.TextureSource)) return Fail("MaterialTextureSourceRequired", "Material Texture plan requires a MATERIAL block with a TEXTURE source.", out diagnostic, block == null ? null : block.BindingName);
            if (targetDocument == null) return Fail("MaterialTextureDocumentRequired", "Material Texture plan requires target-local recipe metadata.", out diagnostic, block.BindingName);
            if (binding == null) return Fail("MaterialBindingRequired", "Material Texture plan requires the shared MaterialBinding.", out diagnostic, block.BindingName);

            var document = new MaterialRecipeDocument
            {
                recipeFormatVersion = targetDocument.recipeFormatVersion,
                wordSource = block.TextureSource,
                bindings = new List<StackMachineBindingDeclaration>(),
                capabilities = targetDocument.capabilities,
                provenance = targetDocument.provenance,
                diagnosticSourceMap = targetDocument.diagnosticSourceMap,
                textureDomainVersion = targetDocument.textureDomainVersion,
                outputLogicalName = targetDocument.outputLogicalName,
                outputWidth = targetDocument.outputWidth,
                outputHeight = targetDocument.outputHeight
            };
            var entries = new List<TextureBindingEntry> { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            IReadOnlyList<MaterialTextureBindingEntry> textures = binding.Textures;
            for (int i = 0; i < textures.Count; i++)
            {
                MaterialTextureBindingEntry texture = textures[i];
                if (texture == null || string.IsNullOrWhiteSpace(texture.logicalName) || texture.logicalName == "out" || texture.sourceTexture == null)
                    return Fail("TextureBindingInvalid", "MaterialBinding source textures require a non-reserved logical name and Texture2D.", out diagnostic, block.BindingName);
                entries.Add(new TextureBindingEntry { logicalName = texture.logicalName, kind = TextureBindingKind.SourceTexture, sourceTexture = texture.sourceTexture });
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = texture.logicalName, declaredKind = StackMachineBindingKind.Resource });
            }
            return TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, entries.ToArray()), out plan, out diagnostic);
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("material", code, message, bindingName: binding);
            return false;
        }
    }
}
