// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Figure bootstrap component that applies a ShapeSync document in Mesh then Material order.</summary>
    /// <remarks>
    /// This component does not schedule, retry, cancel, coalesce, aggregate, or roll back Machine requests.
    /// Those cross-request policies belong to the Spec16 Shape Director.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RecipeAttacher : MonoBehaviour
    {
        [SerializeField] private ShapeSyncDocumentAsset documentAsset;
        private MeshStackMachine meshStackMachine;
        private MaterialStackMachine materialStackMachine;
        private NormalBlender normalBlender;

        /// <summary>Gets or sets the serialized ShapeSync document carrier applied by this bootstrap component.</summary>
        public ShapeSyncDocumentAsset DocumentAsset { get => documentAsset; set => documentAsset = value; }

        private void Awake()
        {
            meshStackMachine = GetComponent<MeshStackMachine>();
            materialStackMachine = GetComponent<MaterialStackMachine>();
            normalBlender = GetComponent<NormalBlender>();
        }

        private void Start()
        {
            if (documentAsset == null) return;
            if (!TryApply(out StackMachineDiagnostic diagnostic))
            {
                Debug.LogWarning("RecipeAttacher rejected document application. " + FormatDiagnostic(diagnostic), this);
            }
        }

        /// <summary>Snapshots and applies the assigned serialized document carrier.</summary>
        /// <param name="diagnostic">A structured carrier, Mesh, or Material rejection.</param>
        /// <returns><see langword="true"/> when Mesh succeeds and Material accepts its request.</returns>
        public bool TryApply(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (documentAsset == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("document", "CarrierRequired", "RecipeAttacher requires a ShapeSyncDocumentAsset.");
                return false;
            }
            if (!documentAsset.TryGetSnapshot(out ShapeSyncDocument snapshot, out diagnostic)) return false;
            return TryApplySnapshot(snapshot, out diagnostic);
        }

        /// <summary>Snapshots and applies a caller-supplied ShapeSync document.</summary>
        /// <param name="document">The caller-owned document source.</param>
        /// <param name="diagnostic">A structured snapshot, Mesh, or Material rejection.</param>
        /// <returns><see langword="true"/> when Mesh succeeds and Material accepts its request.</returns>
        public bool TryApply(IShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            if (!ShapeSyncDocument.TryCreateSnapshot(document, out ShapeSyncDocument snapshot, out diagnostic)) return false;
            return TryApplySnapshot(snapshot, out diagnostic);
        }

        private bool TryApplySnapshot(ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (meshStackMachine == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("document", "MeshMachineRequired", "RecipeAttacher requires a co-located MeshStackMachine.");
                return false;
            }
            if (materialStackMachine == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("document", "MaterialMachineRequired", "RecipeAttacher requires a co-located MaterialStackMachine.");
                return false;
            }
            if (snapshot.MeshRecipe != null && !string.IsNullOrWhiteSpace(snapshot.MeshRecipe.wordSource)
                && !meshStackMachine.TryEnsureReady(snapshot.MeshBinding, out diagnostic)) return false;
            if (!materialStackMachine.TryEnsureReady(out diagnostic)) return false;
            if (!meshStackMachine.TryAcceptRecipePayload(snapshot, out StackMachineExecutionResult meshResult, out diagnostic))
            {
                diagnostic ??= meshResult == null ? null : meshResult.Diagnostic;
                return false;
            }
            if (snapshot.MeshBinding != null && normalBlender != null)
            {
                normalBlender.TryRetry(out _);
            }
            return materialStackMachine.TryAcceptRecipePayload(snapshot, out _, out diagnostic);
        }

        private static string FormatDiagnostic(StackMachineDiagnostic diagnostic)
        {
            if (diagnostic == null) return "No structured diagnostic was returned.";
            return diagnostic.domainCode + ": " + diagnostic.message;
        }
    }
}
