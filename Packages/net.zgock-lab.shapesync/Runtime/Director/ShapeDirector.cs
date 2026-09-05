// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Owns logical runtime Shapes and dispatches compiled Figure transactions to co-located StackMachines.</summary>
    [DisallowMultipleComponent]
    public sealed class ShapeDirector : MonoBehaviour
    {
        /// <summary>Raised after a transaction commits a new physical Figure state.</summary>
        public event Action TransactionCommitted;
        /// <summary>Raised immediately before a Mesh or Material transaction may mutate the live Figure.</summary>
        /// <remarks>
        /// This event is raised only before a Mesh or Material execution can mutate live Figure
        /// topology. A successful empty-recipe commit has no such execution and raises only
        /// <see cref="TransactionCommitted"/>. Listeners that retain derived Figure hierarchies
        /// must release them here, rather than after topology callbacks observe a partially changed hierarchy.
        /// </remarks>
        public event Action TransactionStarting;
        private enum TransactionPhase { None, Material, Recovery }
        /// <summary>Inspector-facing Template input list used only by Start and explicit synchronization.</summary>
        /// <remarks>Runtime Shapes are the Director's authoritative logical state. Direct edits to this list do not affect Runtime Shapes until <see cref="TrySynchronizeTemplateList"/> is called.</remarks>
        public List<ShapeSyncShapeTemplate> TemplateList = new List<ShapeSyncShapeTemplate>();
        [SerializeField] private bool autoCompile = true;
        [SerializeField] private bool abortOnOutfitMaterialFailure;
        [SerializeField] private MeshBinding meshBinding;
        [SerializeField] private MaterialBinding materialBinding;
        [SerializeField] private ShapeSerializer serializer;
        [SerializeField] private ShapeDeserializer deserializer;
        private readonly List<ShapeSyncShape> runtimeShapes = new List<ShapeSyncShape>();
        private readonly List<ShapeSyncShape> currentPhysicalShapes = new List<ShapeSyncShape>();
        private MeshStackMachine meshStackMachine;
        private MaterialStackMachine materialStackMachine;
        private bool transactionInFlight;
        private TransactionPhase transactionPhase;
        private List<ShapeSyncShape> pendingDesiredPhysical;
        private bool recoveryRequestedOnEnable;
        private string lastMeshSource = string.Empty;
        private string lastMaterialSource = string.Empty;
        private StackMachineDiagnostic lastTransactionDiagnostic;
        private bool runtimeMutationBlocked;

        /// <summary>Gets the read-only logical runtime Shape list.</summary>
        public IReadOnlyList<ShapeSyncShape> RuntimeShapes => runtimeShapes;
        internal string LastMeshSource => lastMeshSource;
        internal string LastMaterialSource => lastMaterialSource;
        /// <summary>Gets or sets whether successful C/U/D requests immediately issue a compile transaction.</summary>
        public bool AutoCompile { get => autoCompile; set => autoCompile = value; }
        /// <summary>Gets or sets the Outfit Material failure policy used by the later transaction phase.</summary>
        public bool AbortOnOutfitMaterialFailure { get => abortOnOutfitMaterialFailure; set => abortOnOutfitMaterialFailure = value; }
        /// <summary>Gets the latest unrecovered transaction or recovery diagnostic, if one occurred.</summary>
        public StackMachineDiagnostic LastTransactionDiagnostic => lastTransactionDiagnostic;
        /// <summary>Gets the configured or co-located Figure serializer component.</summary>
        public ShapeSerializer Serializer => serializer != null ? serializer : GetComponent<ShapeSerializer>();
        /// <summary>Gets the configured or co-located Figure deserializer component.</summary>
        public ShapeDeserializer Deserializer => deserializer != null ? deserializer : GetComponent<ShapeDeserializer>();
        /// <summary>Gets whether runtime Shape mutations are temporarily rejected by a live display owner.</summary>
        public bool IsRuntimeMutationBlocked => runtimeMutationBlocked;

        /// <summary>Sets the temporary runtime mutation gate used by a Hybrid Hot Bake Run Mode.</summary>
        /// <remarks>This does not alter the logical Shape list.  It only rejects later C/U/D and Template synchronization requests.</remarks>
        public void SetRuntimeMutationBlocked(bool blocked) => runtimeMutationBlocked = blocked;

        /// <summary>Adds a validated Template-derived Shape to the authoritative runtime list.</summary>
        /// <param name="template">The authoring Template to convert into a runtime Shape.</param>
        /// <param name="diagnostic">A structured rejection or auto-compile diagnostic.</param>
        /// <returns><see langword="true"/> when the runtime list accepted the Shape and any requested auto-compile succeeded.</returns>
        /// <remarks>
        /// A concrete Shape type and Priority form a logical wearing slot: existing Runtime Shapes
        /// of the added Shape's concrete type at the same priority are removed before it is added.
        /// Shapes of other concrete types, including Morph Shapes, remain in the logical state.
        /// The supplied Template is then appended to
        /// <see cref="TemplateList"/> as an inspector input convenience; replacement never mutates
        /// the existing TemplateList entries.
        /// </remarks>
        public bool TryAddTemplate(ShapeSyncShapeTemplate template, out StackMachineDiagnostic diagnostic)
        {
            if (TryRejectRuntimeMutation(out diagnostic)) return false;
            diagnostic = null;
            lastTransactionDiagnostic = null;
            if (template == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "TemplateRequired", "Shape Director requires a Template."); return false; }
            ShapeSyncShape runtime = template.CreateRuntimeShape();
            if (runtime == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "RuntimeShapeRequired", "Shape Director Template did not create a runtime Shape."); return false; }
            for (int i = 0; i < runtimeShapes.Count; i++)
            {
                ShapeSyncShape existing = runtimeShapes[i];
                if (existing.ShapeId == runtime.ShapeId &&
                    (existing.Priority != runtime.Priority || existing.GetType() != runtime.GetType()))
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateShapeId", "Shape Director cannot add a duplicate ShapeId in another logical wearing slot.", bindingName: runtime.ShapeId);
                    return false;
                }
            }

            for (int i = runtimeShapes.Count - 1; i >= 0; i--)
            {
                if (runtimeShapes[i].Priority == runtime.Priority && runtimeShapes[i].GetType() == runtime.GetType()) runtimeShapes.RemoveAt(i);
            }
            TemplateList.Add(template); runtimeShapes.Add(runtime); return TryAutoCompile(out diagnostic);
        }

        /// <summary>Removes a matching Shape from the authoritative runtime list.</summary>
        /// <param name="shapeId">The logical Shape identity to remove.</param>
        /// <param name="diagnostic">A structured rejection or auto-compile diagnostic.</param>
        /// <returns><see langword="true"/> when the runtime Shape was removed and any requested auto-compile succeeded.</returns>
        /// <remarks>This D operation never changes <see cref="TemplateList"/>.</remarks>
        public bool TryRemoveRuntimeShape(string shapeId, out StackMachineDiagnostic diagnostic)
        {
            if (TryRejectRuntimeMutation(out diagnostic)) return false;
            diagnostic = null; int index = IndexOf(shapeId);
            if (index < 0) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "ShapeIdNotFound", "Shape Director could not remove the requested ShapeId.", bindingName: shapeId); return false; }
            runtimeShapes.RemoveAt(index);
            return TryAutoCompile(out diagnostic);
        }

        /// <summary>Rebuilds Runtime Shapes from the current inspector-facing <see cref="TemplateList"/>.</summary>
        /// <param name="diagnostic">A compile diagnostic; invalid Template rows are logged and skipped locally.</param>
        /// <returns><see langword="true"/> when synchronization and any requested auto-compile succeeded.</returns>
        /// <remarks>Invalid Template entries produce warnings and are skipped. This is the only validation performed for direct TemplateList edits.</remarks>
        public bool TrySynchronizeTemplateList(out StackMachineDiagnostic diagnostic)
        {
            if (TryRejectRuntimeMutation(out diagnostic)) return false;
            diagnostic = null;
            var synchronized = new List<ShapeSyncShape>();
            var observedIds = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < TemplateList.Count; i++)
            {
                ShapeSyncShapeTemplate template = TemplateList[i];
                if (template == null) { WarnTemplateSync("TemplateRequired", "TemplateList entry is null.", i); continue; }
                ShapeSyncShape runtime = template.CreateRuntimeShape();
                if (runtime == null) { WarnTemplateSync("RuntimeShapeRequired", "Template did not create a runtime Shape.", i); continue; }
                if (!observedIds.Add(runtime.ShapeId)) { WarnTemplateSync("DuplicateShapeId", "TemplateList produced a duplicate ShapeId.", i); continue; }
                synchronized.Add(runtime);
            }
            runtimeShapes.Clear();
            runtimeShapes.AddRange(synchronized);
            return TryAutoCompile(out diagnostic);
        }

        /// <summary>Configures the explicit serializer and deserializer used by this Director.</summary>
        /// <param name="serializer">The Figure-owned serializer component.</param>
        /// <param name="deserializer">The Figure-owned deserializer component.</param>
        /// <param name="diagnostic">A structured rejection when either component is missing.</param>
        /// <returns><see langword="true"/> when both components were accepted.</returns>
        public bool TryConfigureSerialization(ShapeSerializer serializer, ShapeDeserializer deserializer, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (serializer == null || deserializer == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "SerializationConfigurationRequired", "Shape Director requires both serializer and deserializer."); return false; }
            this.serializer = serializer;
            this.deserializer = deserializer;
            return true;
        }

        /// <summary>Serializes the current runtime list through the configured Figure serializer to the supplied file name.</summary>
        /// <param name="fileName">The serializer-defined destination identifier.</param>
        /// <returns><see langword="true"/> when the configured serializer stored the current runtime list.</returns>
        public bool TrySerialize(string fileName)
        {
            return TrySerializeWith(fileName, Serializer);
        }

        private bool TrySerializeWith(string fileName, ShapeSerializer activeSerializer)
        {
            if (string.IsNullOrWhiteSpace(fileName) || activeSerializer == null) return false;
            var documentSerializer = activeSerializer as ShapeDocumentSerializer;
            if (documentSerializer != null)
            {
                if (!TryBuildRecoverySources(out string meshSource, out string materialSource)) return false;
                documentSerializer.ConfigureRecoveryRecipeSources(meshSource, meshBinding, materialSource, materialBinding);
            }
            return activeSerializer.TrySerialize(fileName, new List<ShapeSyncShape>(runtimeShapes));
        }

        /// <summary>Deserializes and replaces the runtime list through the configured Figure deserializer from the supplied file name.</summary>
        /// <param name="fileName">The deserializer-defined source identifier.</param>
        /// <returns><see langword="true"/> when the runtime list was replaced and any requested dispatch succeeded.</returns>
        public bool TryDeserialize(string fileName)
        {
            if (TryRejectRuntimeMutation(out StackMachineDiagnostic diagnostic))
            {
                lastTransactionDiagnostic = diagnostic;
                return false;
            }
            return TryDeserializeWith(fileName, Deserializer);
        }

        /// <summary>Loads a standard in-memory <see cref="ShapeDocument"/> through the configured deserializer.</summary>
        /// <param name="document">The caller-owned standard document asset to decode.</param>
        /// <param name="diagnostic">A structured mutation gate, unsupported-deserializer, decode, or transaction failure.</param>
        /// <returns><see langword="true"/> when the Document was decoded and its transaction was accepted.</returns>
        /// <remarks>
        /// This Player-safe API never uses an asset path or <c>AssetDatabase</c>.  Record decoding
        /// remains the configured deserializer's responsibility; custom deserializers that do not
        /// implement <see cref="IShapeDocumentSourceDeserializer"/> are explicitly rejected.
        /// </remarks>
        public bool TryLoadDocument(ShapeDocument document, out StackMachineDiagnostic diagnostic)
        {
            if (TryRejectRuntimeMutation(out diagnostic))
            {
                lastTransactionDiagnostic = diagnostic;
                return false;
            }
            if (!(Deserializer is IShapeDocumentSourceDeserializer sourceDeserializer))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "InMemoryDocumentUnsupported", "The configured ShapeDeserializer does not support in-memory ShapeDocument sources.");
                lastTransactionDiagnostic = diagnostic;
                return false;
            }
            if (!sourceDeserializer.TryDeserialize(document, out List<ShapeSyncShape> shapes, out ShapeSyncDocument payload, out diagnostic))
            {
                lastTransactionDiagnostic = diagnostic;
                return false;
            }
            if (!TryValidateRuntimeShapes(shapes, out diagnostic))
            {
                lastTransactionDiagnostic = diagnostic;
                return false;
            }
            runtimeShapes.Clear();
            for (int i = 0; i < shapes.Count; i++) runtimeShapes.Add(shapes[i].Clone());
            if (!autoCompile) return true;
            bool accepted = TryDispatchLoadedDocument(shapes, payload, out diagnostic);
            if (!accepted) lastTransactionDiagnostic = diagnostic;
            return accepted;
        }

        private bool TryDeserializeWith(string fileName, ShapeDeserializer activeDeserializer)
        {
            if (string.IsNullOrWhiteSpace(fileName) || activeDeserializer == null || !activeDeserializer.TryDeserialize(fileName, out List<ShapeSyncShape> shapes) || !TryValidateRuntimeShapes(shapes, out _)) return false;
            runtimeShapes.Clear();
            for (int i = 0; i < shapes.Count; i++) runtimeShapes.Add(shapes[i].Clone());
            if (!autoCompile) return true;
            if (activeDeserializer is ShapeDocumentDeserializer documentDeserializer &&
                documentDeserializer.LastLoadedDocument != null &&
                ShapeSyncDocument.TryCreateSnapshot(documentDeserializer.LastLoadedDocument, out ShapeSyncDocument snapshot, out _))
            {
                return TryDispatchLoadedDocument(shapes, snapshot, out _);
            }
            return TryCompile(out _);
        }

        /// <summary>Gets one runtime Shape by ShapeId.</summary>
        /// <param name="shapeId">The logical Shape identity to locate.</param>
        /// <param name="runtimeShape">The current runtime Shape on success.</param>
        /// <param name="diagnostic">A structured missing-Shape diagnostic.</param>
        /// <returns><see langword="true"/> when the requested Shape is present.</returns>
        public bool TryGetRuntimeShape(string shapeId, out ShapeSyncShape runtimeShape, out StackMachineDiagnostic diagnostic)
        { runtimeShape = Find(shapeId); diagnostic = runtimeShape == null ? StackMachineDiagnostic.CreateDomain("director", "ShapeIdNotFound", "Shape Director could not find the requested ShapeId.", bindingName: shapeId) : null; return runtimeShape != null; }

        /// <summary>Replaces one runtime Shape without mutating its Template asset.</summary>
        /// <param name="runtimeShape">The replacement logical Shape.</param>
        /// <param name="diagnostic">A structured rejection or auto-compile diagnostic.</param>
        /// <returns><see langword="true"/> when the Shape was replaced and any requested auto-compile succeeded.</returns>
        public bool TryReplaceRuntimeShape(ShapeSyncShape runtimeShape, out StackMachineDiagnostic diagnostic)
        {
            if (TryRejectRuntimeMutation(out diagnostic)) return false;
            diagnostic = null; if (runtimeShape == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "RuntimeShapeRequired", "Shape Director requires a runtime Shape."); return false; }
            int index = IndexOf(runtimeShape.ShapeId); if (index < 0) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "ShapeIdNotFound", "Shape Director could not replace the requested ShapeId.", bindingName: runtimeShape.ShapeId); return false; }
            runtimeShapes[index] = runtimeShape.Clone(); return TryAutoCompile(out diagnostic);
        }

        /// <summary>Resolves, compiles, and dispatches the current Mesh then Material transaction.</summary>
        /// <param name="diagnostic">A structured resolve, compile, dispatch, or in-flight rejection diagnostic.</param>
        /// <returns><see langword="true"/> when the transaction was accepted for execution or completed synchronously.</returns>
        public bool TryCompile(out StackMachineDiagnostic diagnostic)
        {
            if (TryRejectRuntimeMutation(out diagnostic)) return false;
            diagnostic = null;
            if (transactionInFlight)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "TransactionInFlight", "Shape Director cannot start a second transaction before the current Material phase completes.");
                return false;
            }
            if (!ShapeSyncShapeResolver.TryResolve(runtimeShapes, out List<ShapeSyncShape> desiredPhysical, out diagnostic) ||
                !ShapeSyncEntryMerge.TryMerge(currentPhysicalShapes, out List<ShapeSyncMergedEntry> currentMesh, out List<ShapeSyncMergedEntry> currentMaterial, out diagnostic) ||
                !ShapeSyncEntryMerge.TryMerge(desiredPhysical, out List<ShapeSyncMergedEntry> desiredMesh, out List<ShapeSyncMergedEntry> desiredMaterial, out diagnostic) ||
                !ShapeSyncMeshRecipeCompiler.TryCompile(currentPhysicalShapes, desiredPhysical, out string meshSource, out diagnostic) ||
                !ShapeSyncMaterialRecipeCompiler.TryCompile(FilterDetachedOutfitMaterialEntries(currentMaterial, desiredMaterial), desiredMaterial, currentMesh, desiredMesh, out string materialSource, out diagnostic)) return false;
            lastMeshSource = meshSource;
            lastMaterialSource = materialSource;
            return TryDispatchCompiledSources(desiredPhysical, meshSource, materialSource, out diagnostic);
        }

        /// <summary>Records a successfully committed physical state for the later transaction owner.</summary>
        internal void CommitCurrentPhysicalShapes(IReadOnlyList<ShapeSyncShape> physicalShapes)
        {
            currentPhysicalShapes.Clear();
            if (physicalShapes == null) return;
            for (int i = 0; i < physicalShapes.Count; i++) currentPhysicalShapes.Add(physicalShapes[i].Clone());
            TransactionCommitted?.Invoke();
        }

        /// <summary>Builds a detached absolute document for the current committed physical Figure state.</summary>
        /// <param name="document">The current-state document on success. It is not an asset and does not mutate Director inputs.</param>
        /// <param name="diagnostic">A structured recipe or binding diagnostic when the current state cannot be represented.</param>
        /// <returns><see langword="true"/> when the document was created.</returns>
        /// <remarks>
        /// This is intentionally distinct from recovery. It compiles empty-to-current Mesh and Material
        /// recipes and must not add recovery-only reset prefixes such as <c>DETACH_ALL</c>,
        /// <c>MORPH_RESET</c>, or <c>MATERIAL_RESET</c>.
        /// </remarks>
        public bool TryBuildCurrentStateDocument(out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            document = null;
            diagnostic = null;
            if (!ShapeSyncMeshRecipeCompiler.TryCompile(new List<ShapeSyncShape>(), currentPhysicalShapes, out string meshSource, out diagnostic) ||
                !ShapeSyncEntryMerge.TryMerge(currentPhysicalShapes, out List<ShapeSyncMergedEntry> mesh, out List<ShapeSyncMergedEntry> material, out diagnostic) ||
                !ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), material, new List<ShapeSyncMergedEntry>(), mesh, out string materialSource, out diagnostic))
            {
                return false;
            }

            return TryCreateRuntimeDocument(
                meshSource,
                materialSource,
                !string.IsNullOrWhiteSpace(meshSource),
                HasMaterialTargets(materialSource),
                out document,
                out diagnostic);
        }

        private bool TryAutoCompile(out StackMachineDiagnostic diagnostic) { diagnostic = null; return !autoCompile || TryCompile(out diagnostic); }

        private bool TryRejectRuntimeMutation(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = runtimeMutationBlocked
                ? StackMachineDiagnostic.CreateDomain("director", "DirectorRunModeMutationRejected", "Shape Director runtime Shape changes are rejected while Hybrid Hot Bake Run Mode is active.")
                : null;
            return diagnostic != null;
        }

        private bool TryDispatchLoadedDocument(IReadOnlyList<ShapeSyncShape> loadedShapes, ShapeSyncDocument payload, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (transactionInFlight)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "TransactionInFlight", "Shape Director cannot load a ShapeDocument before the current Material phase completes.");
                return false;
            }
            if (payload == null || !ShapeSyncShapeResolver.TryResolve(loadedShapes, out List<ShapeSyncShape> desiredPhysical, out diagnostic)) return false;
            string meshSource = payload.MeshRecipe?.wordSource ?? string.Empty;
            string materialSource = payload.MaterialRecipe?.wordSource ?? string.Empty;
            bool hasMesh = !string.IsNullOrWhiteSpace(meshSource);
            bool hasMaterial = HasMaterialTargets(materialSource);
            lastMeshSource = meshSource;
            lastMaterialSource = materialSource;
            return TryDispatchPayload(desiredPhysical, payload, hasMesh, hasMaterial, out diagnostic);
        }

        private static List<ShapeSyncMergedEntry> FilterDetachedOutfitMaterialEntries(
            IReadOnlyList<ShapeSyncMergedEntry> currentMaterial,
            IReadOnlyList<ShapeSyncMergedEntry> desiredMaterial)
        {
            var activeOutfitRegistryIds = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; desiredMaterial != null && i < desiredMaterial.Count; i++)
            {
                if (desiredMaterial[i]?.Entry is MaterialEntry material && !string.IsNullOrEmpty(material.RegistryId))
                {
                    activeOutfitRegistryIds.Add(material.RegistryId);
                }
            }

            var filtered = new List<ShapeSyncMergedEntry>();
            for (int i = 0; currentMaterial != null && i < currentMaterial.Count; i++)
            {
                ShapeSyncMergedEntry merged = currentMaterial[i];
                if (!(merged?.Entry is MaterialEntry material))
                {
                    continue;
                }

                string registryId = material.RegistryId ?? string.Empty;
                if (registryId.Length == 0 || activeOutfitRegistryIds.Contains(registryId))
                {
                    filtered.Add(merged);
                }
            }

            return filtered;
        }

        private bool TryDispatchCompiledSources(List<ShapeSyncShape> desiredPhysical, string meshSource, string materialSource, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            bool hasMesh = !string.IsNullOrWhiteSpace(meshSource);
            bool hasMaterial = !string.IsNullOrWhiteSpace(materialSource) && materialSource != "FIGURE";
            if (!hasMesh && !hasMaterial)
            {
                CommitCurrentPhysicalShapes(desiredPhysical);
                return true;
            }
            if (!TryCreateRuntimeDocument(meshSource, materialSource, hasMesh, hasMaterial, out ShapeSyncDocument payload, out diagnostic)) return false;
            return TryDispatchPayload(desiredPhysical, payload, hasMesh, hasMaterial, out diagnostic);
        }

        private bool TryDispatchPayload(List<ShapeSyncShape> desiredPhysical, ShapeSyncDocument payload, bool hasMesh, bool hasMaterial, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!hasMesh && !hasMaterial)
            {
                CommitCurrentPhysicalShapes(desiredPhysical);
                return true;
            }
            // The StackMachine execution below can synchronously detach/attach Outfit roots.
            // Notify derived-runtime owners before that mutation makes their retained clone
            // visible to Avatar/VRM hierarchy resolution.
            TransactionStarting?.Invoke();
            if (hasMesh)
            {
                if (meshStackMachine == null)
                {
                    diagnostic = StackMachineDiagnostic.CreateDomain("director", "MeshMachineRequired", "Shape Director requires a co-located MeshStackMachine for a non-empty Mesh recipe.");
                    return false;
                }
                if (!meshStackMachine.TryAcceptRecipePayload(payload, out StackMachineExecutionResult meshResult, out diagnostic))
                {
                    diagnostic ??= meshResult?.Diagnostic;
                    if (RequiresRecovery(diagnostic))
                    {
                        transactionInFlight = true;
                        TryStartRecovery();
                    }
                    return false;
                }
            }
            if (!hasMaterial)
            {
                CommitCurrentPhysicalShapes(desiredPhysical);
                return true;
            }
            if (materialStackMachine == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaterialMachineRequired", "Shape Director requires a co-located MaterialStackMachine for a non-empty Material recipe.");
                transactionInFlight = true;
                TryStartRecovery();
                return false;
            }
            transactionInFlight = true;
            transactionPhase = TransactionPhase.Material;
            if (!materialStackMachine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation operation, out diagnostic))
            {
                TryStartRecovery();
                return false;
            }
            if (operation == null)
            {
                transactionInFlight = false;
                transactionPhase = TransactionPhase.None;
                CommitCurrentPhysicalShapes(desiredPhysical);
                return true;
            }
            pendingDesiredPhysical = CloneShapes(desiredPhysical);
            operation.Completed += CompleteMaterialTransaction;
            if (operation.IsCompleted) CompleteMaterialTransaction(operation);
            return true;
        }

        private void CompleteMaterialTransaction(MaterialStackMachineDispatchOperation operation)
        {
            if (!transactionInFlight || transactionPhase != TransactionPhase.Material) return;
            if (!gameObject.activeInHierarchy || !isActiveAndEnabled)
            {
                recoveryRequestedOnEnable = true;
                return;
            }
            bool figureFailed = false;
            var failedOutfits = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < operation.TargetCompletions.Count; i++)
            {
                MaterialTargetCompletion completion = operation.TargetCompletions[i];
                if (!IsSuccess(completion.Result.Code))
                {
                    if (string.IsNullOrEmpty(completion.RegistryId)) figureFailed = true;
                    else failedOutfits.Add(completion.RegistryId);
                }
            }
            if (figureFailed || (abortOnOutfitMaterialFailure && failedOutfits.Count != 0))
            {
                TryStartRecovery();
                return;
            }
            if (failedOutfits.Count == 0) CommitCurrentPhysicalShapes(pendingDesiredPhysical);
            else CommitCurrentPhysicalShapes(ApplyOutfitMaterialFailures(pendingDesiredPhysical, currentPhysicalShapes, failedOutfits));
            transactionInFlight = false;
            transactionPhase = TransactionPhase.None;
            pendingDesiredPhysical = null;
            recoveryRequestedOnEnable = false;
        }

        private void CompleteRecovery(MaterialStackMachineDispatchOperation operation)
        {
            if (!transactionInFlight || transactionPhase != TransactionPhase.Recovery) return;
            bool succeeded = true;
            for (int i = 0; i < operation.TargetCompletions.Count; i++)
                if (!IsSuccess(operation.TargetCompletions[i].Result.Code)) { succeeded = false; break; }
            if (!succeeded)
            {
                StackMachineDiagnostic diagnostic = null;
                for (int i = 0; i < operation.TargetCompletions.Count; i++)
                    if (!IsSuccess(operation.TargetCompletions[i].Result.Code)) { diagnostic = operation.TargetCompletions[i].Result.Diagnostic; break; }
                RecordRecoveryFailure(diagnostic, "Shape Director recovery Material phase failed.");
            }
            transactionInFlight = false;
            transactionPhase = TransactionPhase.None;
            pendingDesiredPhysical = null;
        }

        private bool TryStartRecovery()
        {
            if (!gameObject.activeInHierarchy || !isActiveAndEnabled)
            {
                recoveryRequestedOnEnable = true;
                return false;
            }
            transactionInFlight = true;
            transactionPhase = TransactionPhase.Recovery;
            StackMachineDiagnostic diagnostic = null;
            if (!TryBuildRecoverySources(out string meshSource, out string materialSource) ||
                !TryCreateRuntimeDocument(meshSource, materialSource, !string.IsNullOrWhiteSpace(meshSource), HasMaterialTargets(materialSource), out ShapeSyncDocument recovery, out diagnostic))
            {
                RecordRecoveryFailure(diagnostic, "Shape Director recovery could not create its current physical snapshot document.");
                transactionInFlight = false;
                transactionPhase = TransactionPhase.None;
                pendingDesiredPhysical = null;
                return false;
            }
            StackMachineExecutionResult meshResult = null;
            if (!string.IsNullOrWhiteSpace(meshSource) && (meshStackMachine == null || !meshStackMachine.TryAcceptRecipePayload(recovery, out meshResult, out diagnostic)))
            {
                diagnostic ??= meshResult?.Diagnostic;
                RecordRecoveryFailure(diagnostic, "Shape Director recovery Mesh phase failed.");
                transactionInFlight = false;
                transactionPhase = TransactionPhase.None;
                pendingDesiredPhysical = null;
                return false;
            }
            if (!HasMaterialTargets(materialSource))
            {
                transactionInFlight = false;
                transactionPhase = TransactionPhase.None;
                pendingDesiredPhysical = null;
                return true;
            }
            if (materialStackMachine == null || !materialStackMachine.TryAcceptRecipePayloadWithCompletion(recovery, out MaterialStackMachineDispatchOperation operation, out diagnostic))
            {
                RecordRecoveryFailure(diagnostic, "Shape Director recovery Material phase failed.");
                transactionInFlight = false;
                transactionPhase = TransactionPhase.None;
                pendingDesiredPhysical = null;
                return false;
            }
            if (operation == null)
            {
                transactionInFlight = false;
                transactionPhase = TransactionPhase.None;
                pendingDesiredPhysical = null;
                return true;
            }
            operation.Completed += CompleteRecovery;
            if (operation.IsCompleted) CompleteRecovery(operation);
            return true;
        }

        private bool TryCreateRuntimeDocument(string meshSource, string materialSource, bool hasMesh, bool hasMaterial, out ShapeSyncDocument payload, out StackMachineDiagnostic diagnostic)
        {
            payload = null;
            diagnostic = null;
            if (hasMesh && meshBinding == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "MeshBindingRequired", "Shape Director requires a MeshBinding for a non-empty Mesh recipe.");
                return false;
            }
            if (hasMaterial && materialBinding == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "MaterialBindingRequired", "Shape Director requires a MaterialBinding for a non-empty Material recipe.");
                return false;
            }
            payload = new ShapeSyncDocument
            {
                MeshRecipe = hasMesh ? new MeshRecipeDocument { wordSource = meshSource } : null,
                // Binding is Director の解決済み Figure 文脈であり、空レシピ時にも後段
                // (HotBake / VRM transport) が同一の Figure を解決するために保持する。
                MeshBinding = meshBinding,
                MaterialRecipe = hasMaterial ? new MaterialRecipeDocument { wordSource = materialSource } : null,
                MaterialBinding = materialBinding
            };
            return true;
        }
        private bool TryBuildRecoverySources(out string meshSource, out string materialSource)
        {
            meshSource = string.Empty;
            materialSource = string.Empty;
            if (!ShapeSyncMeshRecipeCompiler.TryCompile(new List<ShapeSyncShape>(), currentPhysicalShapes, out string meshBody, out _) ||
                !ShapeSyncEntryMerge.TryMerge(currentPhysicalShapes, out List<ShapeSyncMergedEntry> mesh, out List<ShapeSyncMergedEntry> material, out _) ||
                !ShapeSyncMaterialRecipeCompiler.TryCompileReset(out string resetSource, out _) ||
                !ShapeSyncMaterialRecipeCompiler.TryCompile(new List<ShapeSyncMergedEntry>(), material, new List<ShapeSyncMergedEntry>(), mesh, out string restoreSource, out _)) return false;
            meshSource = "DETACH_ALL\nMORPH_RESET" + (string.IsNullOrEmpty(meshBody) ? string.Empty : "\n" + meshBody);
            materialSource = resetSource + (string.IsNullOrWhiteSpace(restoreSource) || restoreSource == "FIGURE" ? string.Empty : "\n" + restoreSource);
            return true;
        }
        private static List<ShapeSyncShape> CloneShapes(IReadOnlyList<ShapeSyncShape> source)
        {
            var result = new List<ShapeSyncShape>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++) result.Add(source[i].Clone());
            return result;
        }
        private static bool IsSuccess(MaterialStackMachineResultCode code) => code == MaterialStackMachineResultCode.Applied || code == MaterialStackMachineResultCode.PartialApplied;
        private static bool HasMaterialTargets(string source) => !string.IsNullOrWhiteSpace(source) && source != "FIGURE";
        private static bool RequiresRecovery(StackMachineDiagnostic diagnostic) => diagnostic != null &&
            (diagnostic.domainCode == "CommitAttachFailed" || diagnostic.domainCode == "CommitDetachFailed" || diagnostic.domainCode == "CommitUnexpectedFailure");
        private static string FormatDiagnostic(StackMachineDiagnostic diagnostic)
        {
            if (diagnostic == null) return "No structured diagnostic was returned.";
            string formatted = diagnostic.domainCode + ": " + diagnostic.message;
            if (!string.IsNullOrEmpty(diagnostic.bindingName)) formatted += " binding=" + diagnostic.bindingName + ".";
            if (!string.IsNullOrEmpty(diagnostic.detail)) formatted += " detail=" + diagnostic.detail + ".";
            return formatted;
        }
        private static void WarnTemplateSync(string code, string message, int index)
            => Debug.LogWarning("Shape Director TemplateList sync skipped entry " + index + ". " + code + ": " + message);
        private void RecordRecoveryFailure(StackMachineDiagnostic diagnostic, string prefix)
        {
            lastTransactionDiagnostic = diagnostic ?? StackMachineDiagnostic.CreateDomain("director", "RecoveryFailed", prefix);
            Debug.LogWarning(prefix + " " + FormatDiagnostic(lastTransactionDiagnostic), this);
        }

        private static List<ShapeSyncShape> ApplyOutfitMaterialFailures(IReadOnlyList<ShapeSyncShape> desired, IReadOnlyList<ShapeSyncShape> current, HashSet<string> failedOutfits)
        {
            var result = new List<ShapeSyncShape>();
            for (int shapeIndex = 0; shapeIndex < desired.Count; shapeIndex++)
            {
                ShapeSyncShape shape = desired[shapeIndex];
                if (!(shape is PartsShape parts)) { result.Add(shape.Clone()); continue; }
                var entries = new List<ShapeEntry>();
                for (int entryIndex = 0; entryIndex < parts.Parts.Count; entryIndex++)
                {
                    ShapeEntry entry = parts.Parts[entryIndex];
                    if (!(entry is MaterialEntry material) || !failedOutfits.Contains(material.RegistryId ?? string.Empty)) entries.Add(entry.Clone());
                }
                result.Add(ClonePartsShape(parts, entries));
            }
            for (int shapeIndex = 0; shapeIndex < current.Count; shapeIndex++)
            {
                if (!(current[shapeIndex] is PartsShape parts)) continue;
                var entries = new List<ShapeEntry>();
                for (int entryIndex = 0; entryIndex < parts.Parts.Count; entryIndex++)
                {
                    if (parts.Parts[entryIndex] is MaterialEntry material && failedOutfits.Contains(material.RegistryId ?? string.Empty)) entries.Add(material.Clone());
                }
                if (entries.Count != 0) result.Add(ClonePartsShape(parts, entries));
            }
            return result;
        }

        private static ShapeSyncShape ClonePartsShape(PartsShape source, List<ShapeEntry> entries)
        {
            if (source is SkinShape) return new SkinShape(source.ShapeId, source.Priority, source.Tags, entries);
            if (source is HairShape) return new HairShape(source.ShapeId, source.Priority, source.Tags, entries);
            return new OutfitShape(source.ShapeId, source.Priority, source.Tags, entries);
        }
        private static bool TryValidateRuntimeShapes(IReadOnlyList<ShapeSyncShape> shapes, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (shapes == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "RuntimeShapesRequired", "Shape Director requires a runtime Shape list."); return false; }
            var ids = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < shapes.Count; i++)
            {
                if (shapes[i] == null) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "RuntimeShapeRequired", "Shape Director runtime list cannot contain null.", detail: i.ToString()); return false; }
                if (!ids.Add(shapes[i].ShapeId)) { diagnostic = StackMachineDiagnostic.CreateDomain("director", "DuplicateShapeId", "Shape Director cannot accept a duplicate ShapeId.", bindingName: shapes[i].ShapeId); return false; }
            }
            return true;
        }
        private ShapeSyncShape Find(string shapeId) { int index = IndexOf(shapeId); return index < 0 ? null : runtimeShapes[index]; }
        private int IndexOf(string shapeId) { for (int i = 0; i < runtimeShapes.Count; i++) if (runtimeShapes[i].ShapeId == shapeId) return i; return -1; }
        private void Awake()
        {
            meshStackMachine = GetComponent<MeshStackMachine>();
            materialStackMachine = GetComponent<MaterialStackMachine>();
        }
        private void Start()
        {
            if (!TrySynchronizeTemplateList(out StackMachineDiagnostic diagnostic))
                Debug.LogWarning("Shape Director initial TemplateList synchronization rejected. " + FormatDiagnostic(diagnostic), this);
        }
        private void OnDisable()
        {
            if (transactionInFlight && transactionPhase == TransactionPhase.Material)
            {
                // The Material Machine may still own escrow or commit work. Director never cancels it;
                // preserve the desired snapshot and issue recovery only after this component is enabled again.
                recoveryRequestedOnEnable = true;
            }
            else ClearTransactionBookkeeping();
        }
        private void OnDestroy()
        {
            recoveryRequestedOnEnable = false;
            ClearTransactionBookkeeping();
        }
        private void ClearTransactionBookkeeping()
        {
            transactionInFlight = false;
            transactionPhase = TransactionPhase.None;
            pendingDesiredPhysical = null;
        }
        private void OnEnable()
        {
            if (recoveryRequestedOnEnable)
            {
                recoveryRequestedOnEnable = false;
                TryStartRecovery();
            }
        }
    }
}
