// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Builds the one-recipe PlayMode / EditMode lowering selected for one logical Atlas page.</summary>
    /// <remarks>Recipe partitioning remains a concrete executor policy; this helper only preserves a page-local Core plan's operation order and bindings.</remarks>
    public static class AtlasBakerPageRecipeBuilder
    {
        /// <summary>Creates one Texture execution plan containing the page's initial FILL_OUT and ordered PLACE operations.</summary>
        public static bool TryCreate(AtlasBakerPagePlan page, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
            => TryCreate(page, page == null ? null : page.Operations, true, out plan, out diagnostic);

        /// <summary>Creates one cumulative-output segment. Only the first segment may initialize <c>$out</c> with FILL_OUT.</summary>
        public static bool TryCreate(AtlasBakerPagePlan page, IReadOnlyList<AtlasBakerPageOperation> operations, bool initializeOutput, out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic)
        {
            plan = null;
            if (page == null) return Reject("AtlasBakerPageRequired", "Atlas page recipe creation requires a page plan.", out diagnostic);
            if (page.Extent <= 0 || operations == null || operations.Count == 0)
                return Reject("AtlasBakerPageInvalid", "Atlas page recipe creation requires a non-empty page plan.", out diagnostic);

            var words = new StringBuilder();
            words.Append(page.Extent).Append(' ').Append(page.Extent).Append(" RECTSIZE ");
            var bindings = new List<TextureBindingEntry>();
            var document = new MaterialRecipeDocument { outputLogicalName = "out", outputWidth = page.Extent, outputHeight = page.Extent };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            bindings.Add(new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall });

            int sourceIndex = 0;
            bool sawFillOut = false;
            for (int i = 0; i < operations.Count; i++)
            {
                AtlasBakerPageOperation operation = operations[i];
                if (operation == null) return Reject("AtlasBakerPageOperationInvalid", "Atlas page contains a null operation.", out diagnostic);
                if (operation.Kind == AtlasBakerPageOperationKind.FillOut)
                {
                    if (!initializeOutput || i != 0 || sawFillOut) return Reject("AtlasBakerFillOrderInvalid", "Atlas FILL_OUT must appear exactly once as the first operation of the initializing page segment.", out diagnostic);
                    AppendFill(words, operation.FillColor);
                    sawFillOut = true;
                    continue;
                }
                if (operation.Kind != AtlasBakerPageOperationKind.Place || operation.Source == null)
                    return Reject("AtlasBakerPlaceInvalid", "Atlas PLACE requires a source Texture.", out diagnostic);

                string logicalName = "source" + sourceIndex++.ToString(CultureInfo.InvariantCulture);
                document.bindings.Add(new StackMachineBindingDeclaration { logicalName = logicalName, declaredKind = StackMachineBindingKind.Resource });
                bindings.Add(new TextureBindingEntry { logicalName = logicalName, kind = TextureBindingKind.SourceTexture, sourceTexture = operation.Source });
                TextureDispatchRectangle source = operation.SourceRectangle;
                TextureDispatchRectangle destination = operation.DestinationRectangle;
                words.Append('$').Append(logicalName).Append(' ')
                    .Append(source.X).Append(' ').Append(source.Y).Append(' ').Append(source.Width).Append(' ').Append(source.Height).Append(' ')
                    .Append(destination.X).Append(' ').Append(destination.Y).Append(' ').Append(destination.Width).Append(' ').Append(destination.Height).Append(" PLACE ");
            }

            if (initializeOutput && !sawFillOut) return Reject("AtlasBakerFillRequired", "The first Atlas page segment requires one initial FILL_OUT operation.", out diagnostic);
            if (!initializeOutput && sawFillOut) return Reject("AtlasBakerFillOrderInvalid", "A cumulative Atlas page segment may not initialize FILL_OUT.", out diagnostic);
            document.wordSource = words.ToString().TrimEnd();
            return TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, bindings.ToArray()), out plan, out diagnostic);
        }

        private static void AppendFill(StringBuilder words, Color color)
        {
            words.Append("$out ")
                .Append(color.r.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(color.g.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(color.b.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(color.a.ToString("R", CultureInfo.InvariantCulture)).Append(" FILL_OUT ");
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message);
            return false;
        }
    }
}
