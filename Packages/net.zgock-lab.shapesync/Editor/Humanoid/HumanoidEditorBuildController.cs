// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor.Atlas;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Owns one unpublished Pure Humanoid candidate and its caller-driven compiler operation.</summary>
    internal sealed class HumanoidEditorBuildController : IDisposable
    {
        private const string TextureComputeGuid = "9b3984bbd2b6468cbd6bc33d76df21d1";
        private const string NormalComputeGuid = "60c577b8d7a146a3bd3de517353d9e78";
        internal static Action<string> LogInfo = message => Debug.Log(message);
        internal static Action<string> LogWarning = message => Debug.LogWarning(message);

        private readonly Func<IHumanoidBuildBackend> backendFactory;
        private readonly Func<IAtlasBakerPageExecutor> atlasExecutorFactory;
        private TextureEditModeStackMachine normalTextureMachine;
        private TextureEditModeStackMachine materialTextureMachine;
        private HumanoidBuildOperation operation;
        private AtlasBakerOperation atlasBakerOperation;
        private AtlasBakerResult atlasLogicalResult;
        private AtlasBakerExecutionOperation atlasExecutionOperation;
        private HumanoidBuildResult result;
        private HumanoidIndividualAssetStage stagedAssets;
        private IDisposable vrmTransportResult;
        private IReadOnlyList<string> stagedVrmAssetPaths;
        private bool vrmAssetsFinalized;
        private string pendingPrefabAssetPath;
        private string publishedPrefabAssetPath;
        private bool publishCommitted;
        private bool stagedAssetsApplied;
        private readonly List<string> residualArtifactPaths = new List<string>();
        private GameObject candidate;
        private GameObject figureSourceRoot;
        private ShapeSyncDocument sourceDocument;
        private AtlasSchemaDocument atlasSchema;
        private string atlasFigureIdentity;
        private string atlasDocumentIdentity;
        private bool disposed;

        internal HumanoidEditorBuildController() : this(null, null) { }
        internal HumanoidEditorBuildController(Func<IHumanoidBuildBackend> backendFactory) : this(backendFactory, null) { }
        internal HumanoidEditorBuildController(Func<IHumanoidBuildBackend> backendFactory, Func<IAtlasBakerPageExecutor> atlasExecutorFactory)
        {
            this.backendFactory = backendFactory;
            this.atlasExecutorFactory = atlasExecutorFactory;
        }

        internal HumanoidBuildOperationStatus Status { get; private set; }
        internal StackMachineDiagnostic Diagnostic { get; private set; }
        internal GameObject Candidate => candidate;
        internal GameObject FigureSourceRoot => figureSourceRoot;
        internal ShapeSyncDocument SourceDocument => sourceDocument;
        /// <summary>Gets the detached optional Atlas Schema input for the later Atlas phase, or null when Atlas is disabled.</summary>
        internal AtlasSchemaDocument AtlasSchema => atlasSchema;
        /// <summary>Gets staged individual assets while this controller owns the pre-Prefab publish escrow.</summary>
        internal HumanoidIndividualAssetStage StagedAssets => stagedAssets;
        /// <summary>Gets whether the staged assets have been applied to the unpublished candidate for later VRM transport and Prefab commit.</summary>
        internal bool AreStagedAssetsApplied => stagedAssetsApplied;
        internal IDisposable VrmTransportResult => vrmTransportResult;
        internal IReadOnlyList<string> StagedVrmAssetPaths => stagedVrmAssetPaths ?? Array.Empty<string>();
        internal bool AreVrmAssetsFinalized => vrmAssetsFinalized;
        /// <summary>Gets the successfully committed Prefab asset path, if this controller has completed publish.</summary>
        internal string PublishedPrefabAssetPath => publishedPrefabAssetPath;
        /// <summary>Gets persisted assets that a failed or cancelled publish leaves for the UI to report as structured warnings.</summary>
        internal IReadOnlyList<string> ResidualArtifactPaths => residualArtifactPaths.AsReadOnly();
        internal bool IsActive => Status == HumanoidBuildOperationStatus.Pending && (operation != null || atlasExecutionOperation != null);
        /// <summary>Gets the current compiler phase for Editor progress display without exposing the backend.</summary>
        internal HumanoidBuildProgressPhase ProgressPhase => atlasExecutionOperation != null ? HumanoidBuildProgressPhase.Atlas : operation != null ? operation.ProgressPhase : Status == HumanoidBuildOperationStatus.Succeeded ? HumanoidBuildProgressPhase.Completed : Status == HumanoidBuildOperationStatus.Failed ? HumanoidBuildProgressPhase.Failed : Status == HumanoidBuildOperationStatus.Cancelled ? HumanoidBuildProgressPhase.Cancelled : HumanoidBuildProgressPhase.Mesh;

        internal bool TryStart(GameObject figureRoot, ShapeSyncDocumentAsset documentAsset, out StackMachineDiagnostic diagnostic)
            => TryStartInternal(figureRoot, documentAsset, null, out diagnostic);

        /// <summary>Starts an Atlas-enabled build with a required detached Atlas Schema input.</summary>
        internal bool TryStartWithAtlas(GameObject figureRoot, ShapeSyncDocumentAsset documentAsset, AtlasSchema atlasSchemaAsset, out StackMachineDiagnostic diagnostic)
        {
            if (disposed)
                return Reject("EditorBuildControllerDisposed", "Humanoid Editor build controller has been disposed.", out diagnostic);
            if (IsActive || result != null || candidate != null)
                return Reject("EditorBuildControllerBusy", "Dispose or cancel the active Humanoid Editor build before starting another build.", out diagnostic);
            if (atlasSchemaAsset == null)
                return Reject("AtlasSchemaRequired", "Atlas-enabled Humanoid Editor build requires an AtlasSchema asset.", out diagnostic);
            return TryStartInternal(figureRoot, documentAsset, atlasSchemaAsset, out diagnostic);
        }

        /// <summary>Starts one build after the Atlas ON/OFF public boundary has been selected.</summary>
        private bool TryStartInternal(GameObject figureRoot, ShapeSyncDocumentAsset documentAsset, AtlasSchema atlasSchemaAsset, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("EditorBuildControllerDisposed", "Humanoid Editor build controller has been disposed.", out diagnostic);
            if (IsActive || result != null || candidate != null) return Reject("EditorBuildControllerBusy", "Dispose or cancel the active Humanoid Editor build before starting another build.", out diagnostic);
            if (figureRoot == null) return Reject("FigureRequired", "Humanoid Editor build requires a Figure root.", out diagnostic);
            if (documentAsset == null) return Reject("DocumentRequired", "Humanoid Editor build requires a ShapeSyncDocument asset.", out diagnostic);
            if (!documentAsset.TryGetSnapshot(out ShapeSyncDocument snapshot, out diagnostic)) return false;
            if (!TryCreateAtlasSchemaSnapshot(atlasSchemaAsset, out AtlasSchemaDocument atlasSnapshot, out diagnostic)) return false;

            residualArtifactPaths.Clear();
            stagedAssetsApplied = false;
            pendingPrefabAssetPath = null;
            publishedPrefabAssetPath = null;
            publishCommitted = false;
            figureSourceRoot = figureRoot;
            sourceDocument = snapshot;
            atlasSchema = atlasSnapshot;
            atlasFigureIdentity = atlasSchema == null ? null : AtlasEditorIdentityTokenProvider.Create(figureRoot);
            atlasDocumentIdentity = atlasSchema == null ? null : AtlasEditorIdentityTokenProvider.Create(documentAsset);
            IHumanoidBuildBackend backend = backendFactory != null ? backendFactory() : CreateConcreteBackend(out diagnostic);
            if (backend == null)
            {
                DisposeEscrow();
                return diagnostic != null ? false : Reject("EditorBuildBackendRequired", "Humanoid Editor build could not create its EditMode backend.", out diagnostic);
            }
            if (!new HumanoidCompiler().TryCompile(new HumanoidBuildSource(figureRoot, snapshot), backend, out operation, out diagnostic))
            {
                DisposeEscrow();
                return false;
            }
            Status = HumanoidBuildOperationStatus.Pending;
            Diagnostic = null;
            return true;
        }

        /// <summary>Transfers the completed Mesh-lower VRM source-role carrier once to this 17.6 owner.</summary>
        internal bool TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic)
        {
            provenance = null;
            diagnostic = null;
            if (disposed) return Reject("EditorBuildControllerDisposed", "Humanoid Editor build controller has been disposed.", out diagnostic);
            if (operation == null) return Reject("VrmTransportOperationMissing", "Humanoid Editor build has no successful operation from which to take VRM provenance.", out diagnostic);
            if (operation.Status != HumanoidBuildOperationStatus.Succeeded)
                return operation.TryTakeVrmTransportProvenance(out provenance, out diagnostic);
            if (Status != HumanoidBuildOperationStatus.Succeeded || stagedAssets == null || !stagedAssetsApplied || candidate == null)
                return Reject("VrmTransportCandidateNotReady", "VRM transport provenance requires staged assets applied to the successful unpublished candidate.", out diagnostic);
            return operation.TryTakeVrmTransportProvenance(out provenance, out diagnostic);
        }

        /// <summary>Stages individual non-VRM assets without transferring the completed result or committing a Prefab.</summary>
        internal bool TryStageIndividualAssets(string outputFolder, string documentName, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("EditorBuildControllerDisposed", "Humanoid Editor build controller has been disposed.", out diagnostic);
            if (Status != HumanoidBuildOperationStatus.Succeeded || result == null) return Reject("EditorBuildResultNotReady", "Individual assets can be staged only from the controller-owned successful result.", out diagnostic);
            if (stagedAssets != null) return Reject("PublishAssetsAlreadyStaged", "Individual assets have already been staged for this Humanoid Editor build.", out diagnostic);
            if (!HumanoidIndividualAssetStager.TryStage(outputFolder, documentName, result, out stagedAssets, out string[] residuals, out diagnostic))
            {
                AddResidualArtifacts(residuals);
                Diagnostic = diagnostic;
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                return false;
            }
            return true;
        }

        /// <summary>Sets staged assets on the unpublished candidate before optional VRM transport; Prefab commit remains a later phase.</summary>
        internal bool TryApplyStagedAssetsToCandidate(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("EditorBuildControllerDisposed", "Humanoid Editor build controller has been disposed.", out diagnostic);
            if (Status != HumanoidBuildOperationStatus.Succeeded || stagedAssets == null || candidate == null) return Reject("PublishStageNotReady", "Candidate asset application requires a successful staged Humanoid build.", out diagnostic);
            if (stagedAssetsApplied) return Reject("PublishStageAlreadyApplied", "Staged assets have already been applied to this candidate.", out diagnostic);
            if (!HumanoidCandidateAssetApplier.TryApply(candidate, stagedAssets, out diagnostic))
            {
                Diagnostic = diagnostic;
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                return false;
            }
            stagedAssetsApplied = true;
            return true;
        }

        internal bool TryTransportVrmPhysics(IHumanoidVrmTransportExecutor executor, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (executor == null) return Reject("VrmTransportExecutorRequired", "VRM transport requires the optional UniVRM executor.", out diagnostic);
            if (!TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out diagnostic)) return false;
            try
            {
                bool transported = executor.TryTransport(candidate, figureSourceRoot, sourceDocument, provenance, out IDisposable transportResult, out diagnostic);
                if (!transported || transportResult == null)
                {
                    // The optional backend may have allocated a partial in-memory result before
                    // reporting failure. It never escapes this 17.6 transaction.
                    transportResult?.Dispose();
                    provenance.Dispose();
                    Diagnostic = diagnostic ?? StackMachineDiagnostic.CreateDomain("humanoid", "VrmTransportFailed", "VRM transport failed without a diagnostic.");
                    Status = HumanoidBuildOperationStatus.Failed;
                    DisposeEscrow();
                    return false;
                }
                provenance.Dispose();
                vrmTransportResult = transportResult;
                stagedVrmAssetPaths = null;
                vrmAssetsFinalized = false;
                return true;
            }
            catch (Exception exception)
            {
                provenance.Dispose();
                Diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "VrmTransportUnexpectedFailure", "VRM transport executor threw an unexpected exception.", detail: exception.Message);
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                diagnostic = Diagnostic;
                return false;
            }
        }

        /// <summary>Stages optional VRM assets through the UniVRM concrete executor while retaining result ownership here.</summary>
        internal bool TryStageVrmAssets(IHumanoidVrmTransportExecutor executor, string outputFolder, string relativeFolder, string documentName, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (executor == null) return Reject("VrmTransportExecutorRequired", "VRM asset staging requires the optional UniVRM executor.", out diagnostic);
            if (vrmTransportResult == null) return Reject("VrmPublishResultMissing", "VRM asset staging requires a successful in-memory VRM transport result.", out diagnostic);
            if (stagedVrmAssetPaths != null) return Reject("VrmPublishAssetsAlreadyStaged", "VRM assets have already been staged for this Humanoid build.", out diagnostic);
            try
            {
                if (!executor.TryStageAssets(vrmTransportResult, outputFolder, relativeFolder, documentName, out IReadOnlyList<string> paths, out diagnostic))
                {
                    AddResidualArtifacts(paths);
                    Diagnostic = diagnostic ?? StackMachineDiagnostic.CreateDomain("humanoid", "VrmPublishAssetStagingFailed", "VRM asset staging failed without a diagnostic.");
                    Status = HumanoidBuildOperationStatus.Failed;
                    DisposeEscrow();
                    return false;
                }
                stagedVrmAssetPaths = paths ?? Array.Empty<string>();
                return true;
            }
            catch (Exception exception)
            {
                Diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "VrmPublishAssetStagingUnexpectedFailure", "VRM asset staging executor threw an unexpected exception.", detail: exception.Message);
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                diagnostic = Diagnostic;
                return false;
            }
        }

        /// <summary>Completes Prefab references and releases VRM asset ownership only after all VRM assets are staged.</summary>
        internal bool TryFinalizeVrmAssets(IHumanoidVrmTransportExecutor executor, GameObject publishedPrefabRoot, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (executor == null) return Reject("VrmTransportExecutorRequired", "VRM asset finalize requires the optional UniVRM executor.", out diagnostic);
            if (vrmTransportResult == null || stagedVrmAssetPaths == null) return Reject("VrmPublishAssetsNotStaged", "VRM asset finalize requires a staged in-memory VRM result.", out diagnostic);
            if (vrmAssetsFinalized) return Reject("VrmPublishAssetsAlreadyFinalized", "VRM assets have already been finalized for this Humanoid build.", out diagnostic);
            try
            {
                if (!executor.TryFinalizeAssets(vrmTransportResult, publishedPrefabRoot, out diagnostic))
                {
                    Diagnostic = diagnostic ?? StackMachineDiagnostic.CreateDomain("humanoid", "VrmPublishFinalizeFailed", "VRM asset finalize failed without a diagnostic.");
                    Status = HumanoidBuildOperationStatus.Failed;
                    DisposeEscrow();
                    return false;
                }
                vrmAssetsFinalized = true;
                return true;
            }
            catch (Exception exception)
            {
                Diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "VrmPublishFinalizeUnexpectedFailure", "VRM asset finalize executor threw an unexpected exception.", detail: exception.Message);
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                diagnostic = Diagnostic;
                return false;
            }
        }

        /// <summary>Commits the staged candidate to its final Prefab and completes optional VRM binding before releasing escrow.</summary>
        internal bool TryCommitPrefab(string outputFolder, string documentName, IHumanoidVrmTransportExecutor vrmExecutor, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("EditorBuildControllerDisposed", "Humanoid Editor build controller has been disposed.", out diagnostic);
            if (publishCommitted || !string.IsNullOrEmpty(publishedPrefabAssetPath)) return Reject("PublishPrefabAlreadyCommitted", "Pure Humanoid Prefab has already been committed for this build.", out diagnostic);
            if (Status != HumanoidBuildOperationStatus.Succeeded || stagedAssets == null || !stagedAssetsApplied || candidate == null)
                return Reject("PublishStageNotReady", "Prefab commit requires a successful candidate with applied staged assets.", out diagnostic);
            if (vrmTransportResult != null && (stagedVrmAssetPaths == null || vrmExecutor == null))
                return Reject("VrmPublishAssetsNotStaged", "Prefab commit requires staged VRM assets and the executor that created them.", out diagnostic);

            if (!HumanoidPrefabCommitter.TryCommit(candidate, stagedAssets, outputFolder, documentName, out HumanoidPrefabCommit commit, out diagnostic))
            {
                AddResidualPrefabPath(outputFolder, documentName);
                if (vrmTransportResult != null && stagedVrmAssetPaths != null && vrmExecutor != null)
                    vrmExecutor.ReleaseAssetOwnership(vrmTransportResult);
                Diagnostic = diagnostic;
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                return false;
            }

            pendingPrefabAssetPath = commit.AssetPath;
            if (vrmTransportResult != null)
            {
                if (!TryFinalizeVrmAssets(vrmExecutor, commit.PrefabAsset, out diagnostic))
                {
                    AddResidualArtifacts(new[] { pendingPrefabAssetPath });
                    return false;
                }
            }
            if (!HumanoidPrefabCommitter.TryVerifyReload(commit.AssetPath, stagedAssets, out diagnostic))
            {
                AddResidualArtifacts(new[] { pendingPrefabAssetPath });
                Diagnostic = diagnostic;
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                return false;
            }
            string outputPath = System.IO.Path.GetDirectoryName(commit.AssetPath)?.Replace('\\', '/') ?? string.Empty;
            if (!HumanoidPublishPathValidator.TryValidateOutputReferences(commit.AssetPath, outputPath, out diagnostic))
            {
                AddResidualArtifacts(new[] { pendingPrefabAssetPath });
                Diagnostic = diagnostic;
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                return false;
            }

            publishedPrefabAssetPath = pendingPrefabAssetPath;
            pendingPrefabAssetPath = null;
            publishCommitted = true;
            DisposeEscrow();
            Diagnostic = null;
            return true;
        }

        internal HumanoidBuildOperationStatus Pump(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (!IsActive) { diagnostic = Diagnostic; return Status; }
            if (atlasExecutionOperation != null) return PumpAtlas(out diagnostic);
            Status = operation.Pump(out HumanoidBuildResult completed, out diagnostic);
            if (Status == HumanoidBuildOperationStatus.Succeeded)
            {
                result = completed;
                candidate = result?.Mesh?.Root;
                if (candidate == null)
                {
                    Diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "ResolvedHumanoidMissing", "The successful Humanoid build did not transfer its resolved GameObject carrier.");
                    Status = HumanoidBuildOperationStatus.Failed;
                    DisposeEscrow();
                    diagnostic = Diagnostic;
                    return Status;
                }
                if (atlasSchema != null)
                {
                    if (!TryBeginAtlasBake(out diagnostic))
                    {
                        Diagnostic = diagnostic;
                        Status = HumanoidBuildOperationStatus.Failed;
                        DisposeEscrow();
                        return Status;
                    }
                    Diagnostic = null;
                    Status = HumanoidBuildOperationStatus.Pending;
                    return Status;
                }
                Diagnostic = null;
                return Status;
            }
            if (Status == HumanoidBuildOperationStatus.Failed) Diagnostic = diagnostic ?? operation.Diagnostic;
            if (Status != HumanoidBuildOperationStatus.Pending) DisposeEscrow();
            return Status;
        }

        /// <summary>Creates the final-candidate Atlas Core plan and its caller-pumped page executor after Material success.</summary>
        private bool TryBeginAtlasBake(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            InMemoryHumanoidMesh mesh = result?.Mesh;
            if (atlasSchema == null || mesh == null) return Reject("AtlasCandidateRequired", "Atlas bake requires the successful in-memory Humanoid candidate.", out diagnostic);
            if (string.IsNullOrWhiteSpace(atlasFigureIdentity) || string.IsNullOrWhiteSpace(atlasDocumentIdentity))
                return Reject("AtlasCurrentIdentityRequired", "Atlas bake requires persistent Figure and Document identities.", out diagnostic);
            if (!TryCreateAtlasCurrentIdentityAndInputs(mesh, out AtlasValidationIdentity currentIdentity, out List<AtlasBakerMaterialInput> inputs, out diagnostic)) return false;

            atlasBakerOperation = new AtlasBakerOperation(atlasSchema, currentIdentity, inputs);
            if (atlasBakerOperation.Pump() != AtlasBakerOperationStatus.Succeeded)
            {
                diagnostic = atlasBakerOperation.Diagnostic;
                return false;
            }
            if (!atlasBakerOperation.TryTakeResult(out atlasLogicalResult, out diagnostic)) return false;
            ReportAtlasReconciliationWarnings(atlasLogicalResult.Reconciliation);
            if (atlasLogicalResult.Pages.Count == 0)
                LogInfo?.Invoke("Atlas Info [AtlasNoTargets]: Atlasを適用しようとしましたが、対象entryが0件のため何も変更しませんでした。");
            IAtlasBakerPageExecutor executor = null;
            if (atlasLogicalResult.Pages.Count != 0)
            {
                executor = atlasExecutorFactory != null ? atlasExecutorFactory() : CreateConcreteAtlasExecutor(out diagnostic);
                if (executor == null) return diagnostic != null ? false : Reject("AtlasPageExecutorRequired", "Atlas bake requires an EditMode page executor.", out diagnostic);
            }
            atlasExecutionOperation = new AtlasBakerExecutionOperation(atlasLogicalResult, executor);
            return true;
        }

        private static void ReportAtlasReconciliationWarnings(IReadOnlyList<AtlasBakerReconciliation> reconciliation)
        {
            if (reconciliation == null) return;
            for (int i = 0; i < reconciliation.Count; i++)
            {
                AtlasBakerReconciliation entry = reconciliation[i];
                if (entry == null || entry.Severity != AtlasBakerReconciliationSeverity.Warning) continue;
                LogWarning?.Invoke("Atlas Warning [" + entry.Code + "]: " + entry.Message);
            }
        }

        /// <summary>Pumps page execution and transfers completed pages only through the candidate applicator.</summary>
        private HumanoidBuildOperationStatus PumpAtlas(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            AtlasBakerExecutionStatus executionStatus = atlasExecutionOperation.Pump();
            if (executionStatus == AtlasBakerExecutionStatus.Pending) return Status;
            if (executionStatus != AtlasBakerExecutionStatus.Succeeded)
            {
                Diagnostic = atlasExecutionOperation.Diagnostic;
                Status = executionStatus == AtlasBakerExecutionStatus.Cancelled ? HumanoidBuildOperationStatus.Cancelled : HumanoidBuildOperationStatus.Failed;
                diagnostic = Diagnostic;
                DisposeEscrow();
                return Status;
            }
            if (!atlasExecutionOperation.TryTakeResult(out AtlasBakerExecutionResult executionResult, out diagnostic))
            {
                Diagnostic = diagnostic;
                Status = HumanoidBuildOperationStatus.Failed;
                DisposeEscrow();
                return Status;
            }
            try
            {
                if (!AtlasCandidateApplicator.TryApply(result.Mesh, atlasLogicalResult, executionResult, out diagnostic))
                {
                    Diagnostic = diagnostic;
                    Status = HumanoidBuildOperationStatus.Failed;
                    DisposeEscrow();
                    return Status;
                }
            }
            finally { executionResult.Dispose(); }
            atlasExecutionOperation.Dispose(); atlasExecutionOperation = null;
            atlasBakerOperation?.Dispose(); atlasBakerOperation = null;
            atlasLogicalResult = null;
            Diagnostic = null;
            Status = HumanoidBuildOperationStatus.Succeeded;
            return Status;
        }

        /// <summary>Derives current provenance and final candidate texture inputs without retaining any source asset reference.</summary>
        private bool TryCreateAtlasCurrentIdentityAndInputs(InMemoryHumanoidMesh mesh, out AtlasValidationIdentity identity, out List<AtlasBakerMaterialInput> inputs, out StackMachineDiagnostic diagnostic)
        {
            identity = null;
            inputs = new List<AtlasBakerMaterialInput>();
            diagnostic = null;
            if (mesh.Materials.Count != mesh.MaterialSlots.Count) return Reject("AtlasCandidateMaterialSlotMismatch", "Atlas bake requires one final candidate Material for every source slot.", out diagnostic);
            var sources = new List<AtlasSourceMaterialIdentity>();
            for (int i = 0; i < mesh.MaterialSlots.Count; i++)
            {
                HumanoidBuildMaterialSlot slot = mesh.MaterialSlots[i];
                Material material = mesh.Materials[i];
                if (!slot.MaterialId.IsValid || slot.SourceMaterial == null || slot.Adapter == null || material == null)
                    return Reject("AtlasCandidateMaterialInvalid", "Atlas bake requires a valid final Material, source Material, and adapter for every slot.", out diagnostic);
                string sourceIdentity = AtlasEditorIdentityTokenProvider.Create(slot.SourceMaterial);
                if (string.IsNullOrWhiteSpace(sourceIdentity)) return Reject("AtlasCurrentSourceMaterialIdentityRequired", "Atlas bake requires persistent source Material identities.", out diagnostic);
                if (!slot.Adapter.TryGetAtlasBaseColorTransform(material, out string baseColorProperty, out _, out _, out MaterialProxyDiagnostic baseColorDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", baseColorDiagnostic.message, out diagnostic);
                var readPlan = new List<MaterialPropertyReadCommand>();
                if (!slot.Adapter.TryBuildReadPlan(readPlan, out MaterialProxyDiagnostic readPlanDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", readPlanDiagnostic.message, out diagnostic);
                if (!slot.Adapter.TryReadValues(material, readPlan, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic readDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", readDiagnostic.message, out diagnostic);
                var normalTextureProperties = new List<string>();
                if (!slot.Adapter.TryGetPublishTextureProperties(MaterialProxySemantic.NormalTexture, normalTextureProperties, out MaterialProxyDiagnostic normalPropertyDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", normalPropertyDiagnostic.message, out diagnostic);
                bool normalIsNeutral = false;
                if (values.normalTexture != null && normalTextureProperties.Count != 0
                    && !slot.Adapter.TryGetEffectiveNeutralTexture(material, normalTextureProperties[0], values.normalTexture, out normalIsNeutral, out MaterialProxyDiagnostic neutralDiagnostic))
                    return Reject("AtlasCandidateMaterialReadRejected", neutralDiagnostic.message, out diagnostic);
                sources.Add(new AtlasSourceMaterialIdentity(slot.MaterialId, sourceIdentity));
                inputs.Add(new AtlasBakerMaterialInput(slot.MaterialId, material.GetTexture(baseColorProperty), values.normalTexture, normalIsNeutral));
            }
            identity = new AtlasValidationIdentity(atlasFigureIdentity, atlasDocumentIdentity, sources);
            return true;
        }

        private IAtlasBakerPageExecutor CreateConcreteAtlasExecutor(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            ComputeShader texture = LoadComputeShader(TextureComputeGuid);
            ComputeShader normal = LoadComputeShader(NormalComputeGuid);
            if (texture == null || normal == null)
            {
                Reject("EditorComputeProgramRequired", "Atlas bake requires the Texture and Normal ComputeShader assets.", out diagnostic);
                return null;
            }
            return new EditModeAtlasBakerPageExecutor(texture, normal);
        }

        internal void Cancel()
        {
            if (disposed) return;
            bool ownsUnpublishedEscrow = operation != null || atlasBakerOperation != null || atlasExecutionOperation != null || result != null || stagedAssets != null || vrmTransportResult != null || candidate != null;
            if (IsActive)
            {
                operation.Cancel();
                atlasBakerOperation?.Cancel();
                atlasExecutionOperation?.Cancel();
            }
            if (ownsUnpublishedEscrow) { Status = HumanoidBuildOperationStatus.Cancelled; Diagnostic = null; }
            DisposeEscrow();
        }

        /// <summary>Cancels any active build and releases all unpublished controller escrow.</summary>
        /// <remarks>This method never deletes source assets or artifacts that were already published.</remarks>
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            disposed = true;
        }

        private IHumanoidBuildBackend CreateConcreteBackend(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            ComputeShader texture = LoadComputeShader(TextureComputeGuid);
            ComputeShader normal = LoadComputeShader(NormalComputeGuid);
            if (texture == null || normal == null) { Reject("EditorComputeProgramRequired", "Humanoid Editor build requires the Texture and Normal ComputeShader assets.", out diagnostic); return null; }
            normalTextureMachine = new TextureEditModeStackMachine(texture, normal);
            materialTextureMachine = new TextureEditModeStackMachine(texture);
            return new EditModeHumanoidBuildBackend(normalTextureMachine, materialTextureMachine);
        }

        private static ComputeShader LoadComputeShader(string guid)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
        }

        private void DisposeEscrow()
        {
            atlasExecutionOperation?.Dispose(); atlasExecutionOperation = null;
            atlasBakerOperation?.Dispose(); atlasBakerOperation = null;
            atlasLogicalResult = null;
            operation?.Dispose(); operation = null;
            vrmTransportResult?.Dispose(); vrmTransportResult = null;
            if (!publishCommitted && !vrmAssetsFinalized) AddResidualArtifacts(stagedVrmAssetPaths);
            stagedVrmAssetPaths = null;
            vrmAssetsFinalized = false;
            result?.Dispose(); result = null;
            if (!publishCommitted && stagedAssets != null) AddResidualArtifacts(stagedAssets.AssetPaths);
            // Individual assets are already persisted. Later failure / Cancel reports their paths as residual artifacts;
            // only this controller's managed references are released here.
            stagedAssets = null;
            stagedAssetsApplied = false;
            if (!publishCommitted && !string.IsNullOrEmpty(pendingPrefabAssetPath)) AddResidualArtifacts(new[] { pendingPrefabAssetPath });
            pendingPrefabAssetPath = null;
            normalTextureMachine?.Dispose(); normalTextureMachine = null;
            materialTextureMachine?.Dispose(); materialTextureMachine = null;
            if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
            candidate = null;
            figureSourceRoot = null;
            sourceDocument = null;
            atlasSchema = null;
            atlasFigureIdentity = null;
            atlasDocumentIdentity = null;
        }

        /// <summary>Copies and validates the optional Atlas input without starting an Atlas phase or retaining an asset reference.</summary>
        internal static bool TryCreateAtlasSchemaSnapshot(AtlasSchema schemaAsset, out AtlasSchemaDocument snapshot, out StackMachineDiagnostic diagnostic)
        {
            snapshot = null;
            diagnostic = null;
            if (schemaAsset == null) return true;
            AtlasSchemaDocument document = schemaAsset.ToDocument();
            if (!AtlasSchemaValidation.TryValidate(document, out diagnostic)) return false;
            snapshot = document.Clone();
            return true;
        }

        private void AddResidualPrefabPath(string outputFolder, string documentName)
        {
            if (string.IsNullOrWhiteSpace(documentName) || !HumanoidIndividualAssetStager.TryGetOutputFolderName(outputFolder, out string assetPrefix)) return;
            string path = (outputFolder.TrimEnd('/', '\\') + "/" + assetPrefix + ".prefab").Replace('\\', '/');
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AddResidualArtifacts(new[] { path });
        }

        private void AddResidualArtifacts(IReadOnlyList<string> paths)
        {
            if (paths == null) return;
            for (int i = 0; i < paths.Count; i++) if (!string.IsNullOrEmpty(paths[i]) && !residualArtifactPaths.Contains(paths[i])) residualArtifactPaths.Add(paths[i]);
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return false;
        }
    }
}
