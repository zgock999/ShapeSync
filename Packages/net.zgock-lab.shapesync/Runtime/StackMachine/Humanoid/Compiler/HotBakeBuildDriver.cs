// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>
    /// Runtime-only admission and lifetime owner for one Hot Bake compiler operation.
    /// Later HotBake components own this driver and must dispose it during teardown.
    /// </summary>
    public sealed class HotBakeBuildDriver : IDisposable
    {
        private readonly TextureStackMachineHost normalHost;
        private readonly TextureStackMachineHost materialHost;
        private readonly bool normalHostWasAssigned;
        private readonly bool materialHostWasAssigned;
        private PlayModeHumanoidBuildBackend backend;
        private HumanoidBuildOperation operation;
        private GameObject figureSourceRoot;
        private GameObject successfulCandidateRoot;
        private ShapeSyncDocument document;
        private bool disposed;

        /// <summary>Creates a driver using the scene-local Texture StackMachine hosts selected by its component owner.</summary>
        public HotBakeBuildDriver(TextureStackMachineHost normalHost, TextureStackMachineHost materialHost)
        {
            this.normalHost = normalHost;
            this.materialHost = materialHost;
            normalHostWasAssigned = normalHost != null;
            materialHostWasAssigned = materialHost != null;
        }

        /// <summary>Gets the owned compiler operation until cancellation, failure, or explicit driver disposal.</summary>
        /// <remarks>After success this remains available so the artifact transaction can take VRM provenance exactly once.</remarks>
        public HumanoidBuildOperation Operation => operation;

        /// <summary>
        /// Validates immutable Prefab/document input and begins its caller-pumped Hot Bake operation.
        /// The source prefab is never instantiated, modified, or saved by this method.
        /// </summary>
        public bool TryBegin(GameObject figurePrefab, ShapeSyncDocumentAsset documentAsset, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("HotBakeDriverDisposed", "Hot Bake driver has been disposed.", out diagnostic);
            if (operation != null) return Reject("HotBakeOperationActive", "Hot Bake driver already owns an operation; dispose or cancel it before beginning another.", out diagnostic);
            if (!TryValidateInput(figurePrefab, documentAsset, out ShapeSyncDocument document, out diagnostic)) return false;

            backend = new PlayModeHumanoidBuildBackend(normalHost, materialHost);
            var compiler = new HumanoidCompiler();
            if (!compiler.TryCompile(new HumanoidBuildSource(figurePrefab, document), backend, out operation, out diagnostic))
            {
                backend.Dispose();
                backend = null;
                operation = null;
                return false;
            }
            figureSourceRoot = figurePrefab;
            this.document = document;
            return true;
        }

        /// <summary>Begins one Hot Bake operation from a detached runtime document.</summary>
        /// <remarks>This overload is for Hybrid Hot Bake's Director current-state document. It snapshots the supplied value document and never retains or mutates an asset carrier.</remarks>
        public bool TryBegin(GameObject figurePrefab, ShapeSyncDocument runtimeDocument, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed) return Reject("HotBakeDriverDisposed", "Hot Bake driver has been disposed.", out diagnostic);
            if (operation != null) return Reject("HotBakeOperationActive", "Hot Bake driver already owns an operation; dispose or cancel it before beginning another.", out diagnostic);
            if (!TryValidateRuntimeDocument(figurePrefab, runtimeDocument, out ShapeSyncDocument document, out diagnostic)) return false;

            backend = new PlayModeHumanoidBuildBackend(normalHost, materialHost);
            var compiler = new HumanoidCompiler();
            if (!compiler.TryCompile(new HumanoidBuildSource(figurePrefab, document), backend, out operation, out diagnostic))
            {
                backend.Dispose();
                backend = null;
                operation = null;
                return false;
            }
            figureSourceRoot = figurePrefab;
            this.document = document;
            return true;
        }

        /// <summary>Pumps one compiler step and transfers the completed result exactly once on success.</summary>
        public HumanoidBuildOperationStatus Pump(out HumanoidBuildResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (operation == null) return Fail("HotBakeOperationRequired", "Hot Bake driver has no active operation.", out diagnostic);
            if (operation.Status == HumanoidBuildOperationStatus.Pending && IsRequiredHostDestroyed())
            {
                operation.Cancel();
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "HotBakeTextureHostDestroyed", "A Hot Bake Texture StackMachine host was destroyed while its operation was pending.");
                CleanupTerminalOperation();
                return HumanoidBuildOperationStatus.Cancelled;
            }

            HumanoidBuildOperationStatus status = operation.Pump(out result, out diagnostic);
            if (status == HumanoidBuildOperationStatus.Succeeded)
            {
                successfulCandidateRoot = result?.Root;
                // The successful operation also owns optional VRM provenance.  Keep it alive
                // for the artifact transaction until it explicitly takes that carrier or the
                // driver is disposed; only the no-longer-needed backend is released here.
                ReleaseBackend();
                return status;
            }
            if (status != HumanoidBuildOperationStatus.Pending) CleanupTerminalOperation();
            return status;
        }

        /// <summary>Transfers successful Mesh-lower VRM provenance once to the later artifact transaction.</summary>
        public bool TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic)
        {
            provenance = null;
            diagnostic = null;
            if (operation == null) return Reject("HotBakeOperationRequired", "Hot Bake driver has no completed operation that can transfer VRM provenance.", out diagnostic);
            return operation.TryTakeVrmTransportProvenance(out provenance, out diagnostic);
        }

        /// <summary>
        /// Transports optional VRM physics onto the successful candidate and transfers the
        /// in-memory asset ownership to the later artifact-set transaction.
        /// </summary>
        /// <remarks>
        /// Core Runtime keeps the optional result opaque as <see cref="IDisposable"/>. The
        /// receiving artifact set must retain it without calling any persistence-only release API.
        /// </remarks>
        public bool TryTransportVrmPhysics(GameObject candidateRoot, out IDisposable ownership, out StackMachineDiagnostic diagnostic)
        {
            ownership = null;
            diagnostic = null;
            if (operation == null) return Reject("HotBakeOperationRequired", "Hot Bake driver has no completed operation for VRM transport.", out diagnostic);
            if (operation.Status != HumanoidBuildOperationStatus.Succeeded)
                return Reject("HotBakeVrmTransportBuildNotSucceeded", "VRM transport is available only after successful Hot Bake completion.", out diagnostic);
            if (candidateRoot == null) return Reject("HotBakeVrmTransportCandidateRequired", "VRM transport requires the successful Hot Bake candidate root.", out diagnostic);
            if (candidateRoot != successfulCandidateRoot)
                return Reject("HotBakeVrmTransportCandidateMismatch", "VRM transport requires the exact candidate root produced by this successful Hot Bake operation.", out diagnostic);
            if (figureSourceRoot == null || document == null)
                return Reject("HotBakeVrmTransportSourceMissing", "Hot Bake driver no longer retains the source roles required for VRM transport.", out diagnostic);
            if (!HumanoidVrmPhysicsTransportProvider.TryCreate(out IHumanoidVrmPhysicsTransporter transporter))
                return Reject("HotBakeVrmTransportUnavailable", "VRM transport was requested but the optional UniVRM runtime integration is unavailable.", out diagnostic);
            if (!operation.TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out diagnostic)) return false;

            try
            {
                if (!HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(document, provenance.AttachedOutfitLogicalNames, out IReadOnlyList<GameObject> outfits, out diagnostic)) return false;
                if (!transporter.TryTransport(candidateRoot, figureSourceRoot, outfits, out ownership, out diagnostic))
                {
                    ownership?.Dispose();
                    ownership = null;
                    return false;
                }
                if (ownership != null) return true;
                return Reject("HotBakeVrmTransportOwnershipMissing", "Optional VRM transport succeeded without transferring in-memory asset ownership.", out diagnostic);
            }
            finally
            {
                provenance.Dispose();
            }
        }

        /// <summary>Atomically promotes the successful result into the scene scope's owned artifact set.</summary>
        public bool TryCommitArtifact(HumanoidBuildResult result, IReadOnlyList<IDisposable> optionalOwnership, HotBakeArtifactSceneScope scope, out HotBakeArtifactSet artifactSet, out StackMachineDiagnostic diagnostic)
        {
            artifactSet = null;
            diagnostic = null;
            if (operation == null || operation.Status != HumanoidBuildOperationStatus.Succeeded)
                return Reject("HotBakeArtifactBuildNotSucceeded", "Hot Bake artifact commit requires one successful driver operation.", out diagnostic);
            if (result == null || result.Root != successfulCandidateRoot)
                return Reject("HotBakeArtifactResultMismatch", "Hot Bake artifact commit requires the exact successful build result.", out diagnostic);
            if (scope == null) return Reject("HotBakeArtifactSceneScopeRequired", "Hot Bake artifact commit requires one scene scope.", out diagnostic);
            if (!scope.TryValidateForArtifact(out diagnostic)) return false;
            if (result.Root.scene != scope.HostScene)
            {
                GameObject template = UnityEngine.Object.Instantiate(result.Root);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(template, scope.HostScene);
                if (!result.Mesh.TryReplaceRoot(template, out GameObject previousRoot, out diagnostic)) { UnityEngine.Object.Destroy(template); return false; }
                successfulCandidateRoot = template;
                if (!HotBakeArtifactSet.TryCreate(result, optionalOwnership, out artifactSet, out diagnostic) || !scope.TrySetArtifact(artifactSet, out diagnostic))
                {
                    artifactSet?.Dispose(); artifactSet = null;
                    result.Mesh.TryReplaceRoot(previousRoot, out _, out _); successfulCandidateRoot = previousRoot;
                    InMemoryHumanoidMesh.DestroyRootForLifecycle(template);
                    return false;
                }
                InMemoryHumanoidMesh.DestroyRootForLifecycle(previousRoot);
                return true;
            }
            if (!HotBakeArtifactSet.TryCreate(result, optionalOwnership, out artifactSet, out diagnostic)) return false;
            if (scope.TrySetArtifact(artifactSet, out diagnostic)) return true;
            artifactSet.Dispose();
            artifactSet = null;
            return false;
        }

        /// <summary>Cancels and releases every unhanded backend resource. Safe during owner or host teardown.</summary>
        public void Cancel()
        {
            operation?.Cancel();
            CleanupTerminalOperation();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            Cancel();
            disposed = true;
        }

        /// <summary>Performs the Hot Bake source admission checks without creating runtime artifacts.</summary>
        public static bool TryValidateInput(GameObject figurePrefab, ShapeSyncDocumentAsset documentAsset, out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            document = null;
            diagnostic = null;
            if (figurePrefab == null) return Reject("HotBakePrefabRequired", "Hot Bake requires an in-project Figure Prefab input.", out diagnostic);
            if (figurePrefab.scene.IsValid())
            {
                ShapeDirector director = figurePrefab.GetComponent<ShapeDirector>();
                if (director != null && director.isActiveAndEnabled)
                    return Reject("HotBakeLiveShapeSyncFigureRejected", "A live ShapeSync Figure instance cannot be used as Hot Bake input; assign its immutable Prefab instead.", out diagnostic);
                return Reject("HotBakePrefabRequired", "Hot Bake accepts an in-project Figure Prefab, not a Scene object.", out diagnostic);
            }
            if (documentAsset == null) return Reject("HotBakeDocumentAssetRequired", "Hot Bake requires a ShapeSyncDocumentAsset input.", out diagnostic);
            if (!documentAsset.TryGetSnapshot(out document, out diagnostic)) return false;
            if (!HumanoidMeshLogicalCollector.TryCreate(figurePrefab, document, out HumanoidMeshLogicalPlan plan, out diagnostic)) return false;
            if (!TryValidateMeshes(plan, out diagnostic)) return false;
            return TryValidateTextures(plan, out diagnostic);
        }

        /// <summary>Validates one detached runtime document without requiring a document asset carrier.</summary>
        public static bool TryValidateRuntimeDocument(GameObject figurePrefab, ShapeSyncDocument runtimeDocument, out ShapeSyncDocument document, out StackMachineDiagnostic diagnostic)
        {
            document = null;
            diagnostic = null;
            if (figurePrefab == null) return Reject("HotBakePrefabRequired", "Hot Bake requires an in-project Figure Prefab input.", out diagnostic);
            if (figurePrefab.scene.IsValid())
            {
                ShapeDirector director = figurePrefab.GetComponent<ShapeDirector>();
                if (director != null && director.isActiveAndEnabled)
                    return Reject("HotBakeLiveShapeSyncFigureRejected", "A live ShapeSync Figure instance cannot be used as Hot Bake input; assign its immutable Prefab instead.", out diagnostic);
                return Reject("HotBakePrefabRequired", "Hot Bake accepts an in-project Figure Prefab, not a Scene object.", out diagnostic);
            }
            if (runtimeDocument == null) return Reject("HotBakeDocumentRequired", "Hot Bake requires a detached ShapeSyncDocument.", out diagnostic);
            if (!ShapeSyncDocument.TryCreateSnapshot(runtimeDocument, out document, out diagnostic)) return false;
            if (!HumanoidMeshLogicalCollector.TryCreate(figurePrefab, document, out HumanoidMeshLogicalPlan plan, out diagnostic)) return false;
            if (!TryValidateMeshes(plan, out diagnostic)) return false;
            return TryValidateTextures(plan, out diagnostic);
        }

        private bool IsRequiredHostDestroyed()
        {
            // A Unity destroyed-object reference compares equal to null.  Scope the check to
            // the compiler phase that can consume that semantic so destroying a Material host
            // cannot cancel a Mesh-only/Normal phase (and conversely). A deliberately absent
            // host remains null from the start and is diagnosed by its lower phase as HostRequired.
            if (operation == null) return false;
            if (operation.ProgressPhase == HumanoidBuildProgressPhase.Mesh) return normalHostWasAssigned && normalHost == null;
            if (operation.ProgressPhase == HumanoidBuildProgressPhase.Material) return materialHostWasAssigned && materialHost == null;
            return false;
        }

        private void CleanupTerminalOperation()
        {
            HumanoidBuildOperation completed = operation;
            operation = null;
            completed?.Dispose();
            ReleaseBackend();
            ReleaseBuildSources();
        }

        private void ReleaseBuildSources()
        {
            figureSourceRoot = null;
            successfulCandidateRoot = null;
            document = null;
        }

        private void ReleaseBackend()
        {
            backend?.Dispose();
            backend = null;
        }

        private static bool TryValidateMeshes(HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic)
        {
            if (!TryValidateMesh(plan.Figure, "Figure", out diagnostic)) return false;
            for (int i = 0; i < plan.AttachedOutfits.Count; i++)
                if (!TryValidateMesh(plan.AttachedOutfits[i], "Outfit", out diagnostic)) return false;
            return true;
        }

        private static bool TryValidateMesh(HumanoidMeshSource source, string role, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Mesh mesh = source.Renderer == null ? null : source.Renderer.sharedMesh;
            if (mesh == null) return Reject("HotBakeSourceMeshRequired", "Hot Bake source " + role + " has no Mesh.", out diagnostic, source.LogicalName);
            if (!mesh.isReadable) return Reject("HotBakeSourceMeshNotReadable", "Hot Bake source " + role + " Mesh must enable Read/Write.", out diagnostic, source.LogicalName, mesh.name);
            return true;
        }

        private static bool TryValidateTextures(HumanoidMeshLogicalPlan plan, out StackMachineDiagnostic diagnostic)
        {
            var seen = new HashSet<Texture>();
            if (!TryValidateRendererTextures(plan.Figure, seen, out diagnostic)) return false;
            for (int i = 0; i < plan.AttachedOutfits.Count; i++)
                if (!TryValidateRendererTextures(plan.AttachedOutfits[i], seen, out diagnostic)) return false;
            for (int i = 0; i < plan.NormalSources.Count; i++)
            {
                HumanoidMeshNormalSource normal = plan.NormalSources[i];
                if (!TryValidateTexture(normal.BaseTexture, normal.EntryName, seen, out diagnostic)) return false;
                for (int j = 0; j < normal.Targets.Count; j++)
                    if (!TryValidateTexture(normal.Targets[j].Texture, normal.EntryName, seen, out diagnostic)) return false;
            }
            return true;
        }

        private static bool TryValidateRendererTextures(HumanoidMeshSource source, HashSet<Texture> seen, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            Material[] materials = source.Renderer == null ? null : source.Renderer.sharedMaterials;
            if (materials == null) return true;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                string[] names = material.GetTexturePropertyNames();
                for (int j = 0; j < names.Length; j++)
                    if (!TryValidateTexture(material.GetTexture(names[j]), names[j], seen, out diagnostic)) return false;
            }
            return true;
        }

        private static bool TryValidateTexture(Texture texture, string binding, HashSet<Texture> seen, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (texture == null || !seen.Add(texture)) return true;
            if (texture is Texture2D source && source.mipmapCount > 1 && source.streamingMipmaps)
                return Reject("HotBakeStreamingMipSourceRejected", "Hot Bake does not accept a source Texture with streaming mipmaps.", out diagnostic, binding, texture.name);
            return true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null, string detail = null)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message, bindingName: binding, detail: detail);
            return false;
        }

        private static HumanoidBuildOperationStatus Fail(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", code, message);
            return HumanoidBuildOperationStatus.Failed;
        }
    }
}
