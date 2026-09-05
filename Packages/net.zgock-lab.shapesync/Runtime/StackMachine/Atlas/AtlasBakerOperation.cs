// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Reports the caller-visible state of an Atlas Baker Core operation.</summary>
    public enum AtlasBakerOperationStatus { Pending, Succeeded, Failed, Cancelled }

    /// <summary>Classifies a non-terminal Schema reconciliation observation.</summary>
    public enum AtlasBakerReconciliationSeverity { Info, Warning }

    /// <summary>One final candidate material's actual Atlas texture inputs.</summary>
    /// <remarks>This carrier deliberately retains <see cref="Texture"/> rather than Texture2D: compiler-generated RenderTextures are valid inputs.</remarks>
    public sealed class AtlasBakerMaterialInput
    {
        /// <summary>Creates one final material input.</summary>
        public AtlasBakerMaterialInput(MaterialId materialId, Texture baseColor, Texture normal, bool normalIsNeutral = false)
        {
            MaterialId = materialId;
            BaseColor = baseColor;
            Normal = normal;
            NormalIsNeutral = normalIsNeutral;
        }

        /// <summary>Gets the final candidate material key.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the actual final BaseColor texture.</summary>
        public Texture BaseColor { get; }
        /// <summary>Gets the actual final Normal texture.</summary>
        public Texture Normal { get; }
        /// <summary>Gets whether the assigned adapter determined that the Normal semantic is neutral.</summary>
        /// <remarks>A neutral source is still placed and resampled when its extent is supported. A neutral source with an unsupported extent has no source rectangle and is represented by the Normal page clear color.</remarks>
        public bool NormalIsNeutral { get; }
    }

    /// <summary>One informational result of comparing Schema entries with final candidate material inputs.</summary>
    public sealed class AtlasBakerReconciliation
    {
        internal AtlasBakerReconciliation(AtlasBakerReconciliationSeverity severity, string code, MaterialId materialId, string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            MaterialId = materialId;
            Message = message ?? string.Empty;
        }

        /// <summary>Gets the observation severity; reconciliation observations never make a successful operation fail.</summary>
        public AtlasBakerReconciliationSeverity Severity { get; }
        /// <summary>Gets the stable observation code.</summary>
        public string Code { get; }
        /// <summary>Gets the affected MaterialId.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the human-readable observation.</summary>
        public string Message { get; }
    }

    /// <summary>Identifies one page-local operation that an Atlas TSM backend must execute.</summary>
    public enum AtlasBakerPageOperationKind { FillOut, Place }

    /// <summary>One immutable page-local FILL_OUT or PLACE operation.</summary>
    /// <remarks>The Core never assigns hall origins, source binding names, an output binding name, or a recipe partition.</remarks>
    public sealed class AtlasBakerPageOperation
    {
        private AtlasBakerPageOperation(AtlasBakerPageOperationKind kind, MaterialId materialId, Texture source, TextureDispatchRectangle sourceRectangle, TextureDispatchRectangle destinationRectangle, Color fillColor)
        {
            Kind = kind;
            MaterialId = materialId;
            Source = source;
            SourceRectangle = sourceRectangle;
            DestinationRectangle = destinationRectangle;
            FillColor = fillColor;
        }

        internal static AtlasBakerPageOperation Fill(Color color, int extent)
            => new AtlasBakerPageOperation(AtlasBakerPageOperationKind.FillOut, default, null, default, new TextureDispatchRectangle(0, 0, extent, extent), color);
        internal static AtlasBakerPageOperation Place(MaterialId materialId, Texture source, TextureDispatchRectangle sourceRectangle, TextureDispatchRectangle destinationRectangle)
            => new AtlasBakerPageOperation(AtlasBakerPageOperationKind.Place, materialId, source, sourceRectangle, destinationRectangle, default);

        /// <summary>Gets whether this is FILL_OUT or PLACE.</summary>
        public AtlasBakerPageOperationKind Kind { get; }
        /// <summary>Gets the source material key for PLACE, or an invalid key for FILL_OUT.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the source texture for PLACE, or null for FILL_OUT.</summary>
        public Texture Source { get; }
        /// <summary>Gets the source rectangle for PLACE.</summary>
        public TextureDispatchRectangle SourceRectangle { get; }
        /// <summary>Gets the page-local destination rectangle.</summary>
        public TextureDispatchRectangle DestinationRectangle { get; }
        /// <summary>Gets the linear fill color for FILL_OUT.</summary>
        public Color FillColor { get; }
    }

    /// <summary>One actual semantic Atlas page and its ordered, backend-partition-neutral operation sequence.</summary>
    public sealed class AtlasBakerPagePlan
    {
        internal AtlasBakerPagePlan(int pageIndex, AtlasTextureSemantic semantic, int extent, AtlasBakerPageOperation[] operations)
        {
            PageIndex = pageIndex;
            Semantic = semantic;
            Extent = extent;
            Operations = Array.AsReadOnly(operations ?? Array.Empty<AtlasBakerPageOperation>());
        }

        /// <summary>Gets the solved layout page index.</summary>
        public int PageIndex { get; }
        /// <summary>Gets the page texture semantic.</summary>
        public AtlasTextureSemantic Semantic { get; }
        /// <summary>Gets the square page edge in texels.</summary>
        public int Extent { get; }
        /// <summary>Gets FILL_OUT at index zero followed by disjoint PLACE operations in deterministic MaterialId order.</summary>
        public IReadOnlyList<AtlasBakerPageOperation> Operations { get; }
    }

    /// <summary>Successful detached Core result handed to the selected TSM backend adapter.</summary>
    public sealed class AtlasBakerResult
    {
        internal AtlasBakerResult(AtlasLayoutResult layout, AtlasBakerPagePlan[] pages, AtlasBakerReconciliation[] reconciliation)
        {
            Layout = layout;
            Pages = Array.AsReadOnly(pages ?? Array.Empty<AtlasBakerPagePlan>());
            Reconciliation = Array.AsReadOnly(reconciliation ?? Array.Empty<AtlasBakerReconciliation>());
        }

        /// <summary>Gets the deterministic layout solved from the Schema.</summary>
        public AtlasLayoutResult Layout { get; }
        /// <summary>Gets only semantic pages with an actual texture source.</summary>
        public IReadOnlyList<AtlasBakerPagePlan> Pages { get; }
        /// <summary>Gets non-terminal Schema/final-input reconciliation observations.</summary>
        public IReadOnlyList<AtlasBakerReconciliation> Reconciliation { get; }
    }

    /// <summary>UnityEditor-independent, caller-pumped Atlas Baker Core operation.</summary>
    /// <remarks>It validates and derives page-local operations only. GPU dispatch, recipe partitioning, candidate mutation, and resource ownership belong to later adapters.</remarks>
    public sealed class AtlasBakerOperation : IDisposable
    {
        private readonly AtlasSchemaDocument schema;
        private readonly AtlasValidationIdentity currentIdentity;
        private readonly IReadOnlyList<AtlasBakerMaterialInput> materials;
        private bool resultTaken;
        private bool disposed;

        /// <summary>Creates one unstarted operation from a detached Schema, current provenance, and final candidate inputs.</summary>
        public AtlasBakerOperation(AtlasSchemaDocument schema, AtlasValidationIdentity currentIdentity, IReadOnlyList<AtlasBakerMaterialInput> materials)
        {
            this.schema = schema?.Clone();
            this.currentIdentity = currentIdentity?.Clone();
            this.materials = Array.AsReadOnly(Copy(materials));
            Status = AtlasBakerOperationStatus.Pending;
        }

        /// <summary>Gets the current lifecycle state.</summary>
        public AtlasBakerOperationStatus Status { get; private set; }
        /// <summary>Gets the terminal structured diagnostic, or null on success/cancel.</summary>
        public StackMachineDiagnostic Diagnostic { get; private set; }

        /// <summary>Performs the pure Core derivation once. A later backend can pump GPU work independently from this lifecycle.</summary>
        public AtlasBakerOperationStatus Pump()
        {
            if (Status != AtlasBakerOperationStatus.Pending) return Status;
            if (!TryBuild(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic))
            {
                Diagnostic = diagnostic ?? StackMachineDiagnostic.CreateDomain("atlas", "AtlasBakerFailed", "Atlas Baker Core failed without a diagnostic.");
                Status = AtlasBakerOperationStatus.Failed;
                return Status;
            }
            Result = result;
            Status = AtlasBakerOperationStatus.Succeeded;
            return Status;
        }

        /// <summary>Transfers the successful logical result once.</summary>
        public bool TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (disposed) return Reject("AtlasBakerOperationDisposed", "Atlas Baker operation has already been disposed.", out diagnostic);
            if (Status != AtlasBakerOperationStatus.Succeeded) return Reject("AtlasBakerResultUnavailable", "Atlas Baker result is available only after successful completion.", out diagnostic);
            if (resultTaken) return Reject("AtlasBakerResultAlreadyTaken", "Atlas Baker result was already transferred.", out diagnostic);
            result = Result;
            Result = null;
            resultTaken = true;
            return true;
        }

        /// <summary>Cancels a pending Core operation. No Unity resource is owned by this class.</summary>
        public void Cancel()
        {
            if (disposed || Status != AtlasBakerOperationStatus.Pending) return;
            Status = AtlasBakerOperationStatus.Cancelled;
            Diagnostic = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            Result = null;
            disposed = true;
        }

        private AtlasBakerResult Result { get; set; }

        private bool TryBuild(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            if (!AtlasSchemaValidation.TryValidate(schema, out diagnostic)) return false;
            if (!schema.ValidationIdentity.TryMatchCurrent(currentIdentity, out AtlasValidationIdentityDifference difference))
                return Reject("AtlasValidationIdentityMismatch", "Atlas Schema validation provenance does not match the final build input.", DifferenceDetail(difference), out diagnostic);
            if (!AtlasLayoutOracle.Solve(schema, out AtlasLayoutResult layout, out diagnostic)) return false;

            var entries = new Dictionary<MaterialId, AtlasSchemaEntry>();
            foreach (AtlasSchemaEntry entry in schema.Entries) entries.Add(entry.MaterialId.ToMaterialId(), entry);
            var inputs = new Dictionary<MaterialId, AtlasBakerMaterialInput>();
            for (int i = 0; i < materials.Count; i++)
            {
                AtlasBakerMaterialInput input = materials[i];
                if (input == null || !input.MaterialId.IsValid) return Reject("AtlasFinalMaterialInvalid", "Atlas Baker requires one valid final material input per participating slot.", out diagnostic);
                if (!inputs.TryAdd(input.MaterialId, input)) return Reject("AtlasFinalMaterialDuplicate", "Atlas Baker final material inputs contain a duplicate MaterialId.", "materialId=" + input.MaterialId, out diagnostic);
            }
            if (!TryValidateCurrentSourceIdentities(entries, inputs, out diagnostic)) return false;

            var reconciliation = new List<AtlasBakerReconciliation>();
            foreach (KeyValuePair<MaterialId, AtlasSchemaEntry> pair in entries)
            {
                if (!inputs.ContainsKey(pair.Key)) reconciliation.Add(new AtlasBakerReconciliation(AtlasBakerReconciliationSeverity.Info, "AtlasSchemaEntryMissingFromFinal", pair.Key, "Atlas Schema entry is absent from the final candidate and is ignored."));
            }
            foreach (KeyValuePair<MaterialId, AtlasBakerMaterialInput> pair in inputs)
            {
                if (!entries.TryGetValue(pair.Key, out AtlasSchemaEntry entry))
                {
                    reconciliation.Add(new AtlasBakerReconciliation(AtlasBakerReconciliationSeverity.Warning, "AtlasFinalMaterialNotInSchema", pair.Key, "Final candidate material is not assigned by the Atlas Schema and passes through unchanged. owner=" + pair.Key.RegistryId + ";materialId=" + pair.Key + ";schemaDocument=" + schema.ValidationIdentity.DocumentIdentity + ";currentDocument=" + currentIdentity.DocumentIdentity));
                    continue;
                }
                if (entry.Excluded) reconciliation.Add(new AtlasBakerReconciliation(AtlasBakerReconciliationSeverity.Info, "AtlasSchemaEntryExcluded", pair.Key, "Atlas Schema entry is explicitly excluded and passes through unchanged."));
            }

            var sources = new List<Source>();
            foreach (AtlasLayoutCell cell in layout.Cells)
            {
                if (!inputs.TryGetValue(cell.MaterialId, out AtlasBakerMaterialInput input)) continue;
                if (!TryAddSource(input.MaterialId, AtlasTextureSemantic.BaseColor, input.BaseColor, cell, sources, reconciliation, out diagnostic)) return false;
                // A neutral normal still has to participate in the Normal page when it
                // is a real source texture. Otherwise a database-owned neutral texture
                // such as a 1024px _BumpMap would bypass PLACE and remain at its source
                // extent on the generated material. The only source-less case is the
                // unsupported neutral placeholder, which is represented by the Normal
                // page clear color instead. Supported neutral sources still go through
                // PLACE so database-owned textures are resampled to the page extent.
                if (!IsNeutralNormalWithoutSource(input) && !TryAddSource(input.MaterialId, AtlasTextureSemantic.Normal, input.Normal, cell, sources, reconciliation, out diagnostic)) return false;
            }
            sources.Sort(Source.Compare);
            var pageSources = new Dictionary<string, List<Source>>();
            for (int i = 0; i < sources.Count; i++)
            {
                Source source = sources[i];
                string key = source.Cell.PageIndex + ":" + (int)source.Semantic;
                if (!pageSources.TryGetValue(key, out List<Source> group)) { group = new List<Source>(); pageSources.Add(key, group); }
                group.Add(source);
            }
            var pages = new List<AtlasBakerPagePlan>();
            foreach (AtlasSemanticPage semanticPage in layout.SemanticPages)
            {
                string key = semanticPage.PageIndex + ":" + (int)semanticPage.Semantic;
                if (!pageSources.TryGetValue(key, out List<Source> group) || group.Count == 0) continue;
                var operations = new List<AtlasBakerPageOperation> { AtlasBakerPageOperation.Fill(ClearColor(semanticPage.Semantic), layout.PageExtent) };
                for (int i = 0; i < group.Count; i++)
                {
                    Source source = group[i];
                    int innerWidth = source.Cell.Width - source.Cell.Gutter * 2;
                    int innerHeight = source.Cell.Height - source.Cell.Gutter * 2;
                    operations.Add(AtlasBakerPageOperation.Place(source.MaterialId, source.Texture, new TextureDispatchRectangle(0, 0, source.Texture.width, source.Texture.height), new TextureDispatchRectangle(source.Cell.X + source.Cell.Gutter, source.Cell.Y + source.Cell.Gutter, innerWidth, innerHeight)));
                }
                pages.Add(new AtlasBakerPagePlan(semanticPage.PageIndex, semanticPage.Semantic, layout.PageExtent, operations.ToArray()));
            }
            result = new AtlasBakerResult(layout, pages.ToArray(), reconciliation.ToArray());
            diagnostic = null;
            return true;
        }

        private static bool TryAddSource(MaterialId materialId, AtlasTextureSemantic semantic, Texture texture, AtlasLayoutCell cell, List<Source> destination, List<AtlasBakerReconciliation> reconciliation, out StackMachineDiagnostic diagnostic)
        {
            if (texture == null) return Reject("AtlasSemanticTextureRequired", "Atlas Baker requires a resolved texture for every non-excluded semantic.", "materialId=" + materialId + ";semantic=" + semantic, out diagnostic);
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(texture.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(texture.height)) return Reject("AtlasSourceExtentUnsupported", "Atlas source texture extent is unsupported.", "materialId=" + materialId + ";semantic=" + semantic, out diagnostic);
            int innerWidth = cell.Width - cell.Gutter * 2;
            int innerHeight = cell.Height - cell.Gutter * 2;
            if (innerWidth <= 0 || innerHeight <= 0) return Reject("AtlasCellInteriorInvalid", "Atlas Schema cell gutter leaves no texture content area.", "materialId=" + materialId, out diagnostic);
            if ((long)texture.width * innerHeight != (long)texture.height * innerWidth)
                reconciliation.Add(new AtlasBakerReconciliation(AtlasBakerReconciliationSeverity.Warning, "AtlasSourceAspectMismatch", materialId, "Atlas source aspect differs from its Schema cell interior; PLACE resamples into the selected cell. semantic=" + semantic + ";source=" + texture.width + "x" + texture.height + ";cell=" + innerWidth + "x" + innerHeight));
            destination.Add(new Source(materialId, semantic, texture, cell));
            diagnostic = null;
            return true;
        }

        private static bool IsNeutralNormalWithoutSource(AtlasBakerMaterialInput input)
            => input != null && input.NormalIsNeutral && input.Normal != null
                && (!TextureGpuCapabilityProbe.IsPhase0Edge(input.Normal.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(input.Normal.height));

        private bool TryValidateCurrentSourceIdentities(Dictionary<MaterialId, AtlasSchemaEntry> entries, Dictionary<MaterialId, AtlasBakerMaterialInput> inputs, out StackMachineDiagnostic diagnostic)
        {
            var current = new Dictionary<MaterialId, string>();
            if (currentIdentity != null) foreach (AtlasSourceMaterialIdentity source in currentIdentity.SourceMaterials)
            {
                if (source == null) continue;
                MaterialId id = source.MaterialId.ToMaterialId();
                if (entries.ContainsKey(id) && inputs.ContainsKey(id)) current[id] = source.SourceMaterialIdentity;
            }
            foreach (MaterialId id in inputs.Keys)
            {
                if (!entries.ContainsKey(id)) continue;
                if (!current.TryGetValue(id, out string identity) || string.IsNullOrWhiteSpace(identity)) return Reject("AtlasCurrentSourceMaterialIdentityMissing", "Atlas Baker requires current source Material provenance for every final Schema entry.", "materialId=" + id, out diagnostic);
            }
            diagnostic = null;
            return true;
        }

        private static Color ClearColor(AtlasTextureSemantic semantic) => semantic == AtlasTextureSemantic.Normal ? new Color(.5f, .5f, 1f, 1f) : Color.clear;
        private static string DifferenceDetail(AtlasValidationIdentityDifference difference) => difference == null ? null : "kind=" + difference.Kind + ";materialId=" + difference.MaterialId + ";expected=" + difference.ExpectedIdentity + ";actual=" + difference.ActualIdentity;
        private static AtlasBakerMaterialInput[] Copy(IReadOnlyList<AtlasBakerMaterialInput> values)
        {
            if (values == null || values.Count == 0) return Array.Empty<AtlasBakerMaterialInput>();
            var copy = new AtlasBakerMaterialInput[values.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
            return copy;
        }
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) => Reject(code, message, null, out diagnostic);
        private static bool Reject(string code, string message, string detail, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message, detail: detail); return false; }

        private sealed class Source
        {
            internal Source(MaterialId materialId, AtlasTextureSemantic semantic, Texture texture, AtlasLayoutCell cell) { MaterialId = materialId; Semantic = semantic; Texture = texture; Cell = cell; }
            internal MaterialId MaterialId { get; }
            internal AtlasTextureSemantic Semantic { get; }
            internal Texture Texture { get; }
            internal AtlasLayoutCell Cell { get; }
            internal static int Compare(Source left, Source right)
            {
                int page = left.Cell.PageIndex.CompareTo(right.Cell.PageIndex);
                if (page != 0) return page;
                int semantic = ((int)left.Semantic).CompareTo((int)right.Semantic);
                if (semantic != 0) return semantic;
                int registry = string.CompareOrdinal(left.MaterialId.RegistryId, right.MaterialId.RegistryId);
                return registry != 0 ? registry : string.CompareOrdinal(left.MaterialId.EntryId, right.MaterialId.EntryId);
            }
        }
    }
}
