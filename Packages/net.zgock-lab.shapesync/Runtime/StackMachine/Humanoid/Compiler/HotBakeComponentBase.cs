// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Shared Startup/API compile admission and lifetime for the three Hot Bake components.</summary>
    public abstract class HotBakeComponentBase : MonoBehaviour
    {
        [SerializeField] private GameObject figurePrefab;
        [SerializeField] private ShapeSyncDocumentAsset document;
        [SerializeField] private UnityEngine.Object atlas;
        [SerializeField] private bool requireAtlas;
        [SerializeField] private TextureStackMachineHost normalHost;
        [SerializeField] private TextureStackMachineHost materialHost;
#if SHAPESYNC_USE_UNIVRM
        [SerializeField] private bool physicsTransport;
        /// <summary>Gets or sets whether a completed transaction transports optional VRM physics before artifact promotion.</summary>
        public bool PhysicsTransport { get => physicsTransport; set => physicsTransport = value; }
#endif
        private HotBakeBuildDriver driver;
        private HotBakeArtifactSet artifactSet;
        private HotBakeAtlasRuntimeOperation atlasOperation;
        private HumanoidBuildResult atlasResult;
        private TextureStackMachineHost resolvedNormalHost;
        private TextureStackMachineHost resolvedMaterialHost;
        private StackMachineDiagnostic lastDiagnostic;
        private ulong diagnosticAttempt;
        private bool diagnosticWasSurfaced;
        /// <summary>Gets the latest structured component admission, execution, or lifecycle diagnostic.</summary>
        public StackMachineDiagnostic LastDiagnostic
        {
            get => lastDiagnostic;
            private set
            {
                lastDiagnostic = value;
                if (value != null) SurfaceDiagnostic(value);
            }
        }
        /// <summary>Gets whether this component currently owns a compile transaction.</summary>
        public bool IsCompileActive => driver?.Operation != null;
        /// <summary>Gets the scene-scoped artifact set promoted by this component's completed transaction.</summary>
        public HotBakeArtifactSet ArtifactSet => artifactSet;
        /// <summary>Gets the driver owned by the current transaction for derived component orchestration.</summary>
        protected HotBakeBuildDriver Driver => driver;
        /// <summary>Gets the resolved NORMAL-phase host for the current or next admitted Hot Bake transaction.</summary>
        protected TextureStackMachineHost ResolvedNormalHost => resolvedNormalHost != null ? resolvedNormalHost : normalHost;
        /// <summary>Gets the resolved MATERIAL-phase host for the current or next admitted Hot Bake transaction.</summary>
        protected TextureStackMachineHost ResolvedMaterialHost => resolvedMaterialHost != null ? resolvedMaterialHost : materialHost;
        /// <summary>Gets the resolved scene host used by the component's artifact scope.</summary>
        protected TextureStackMachineHost ScopeHost => ResolvedNormalHost != null ? ResolvedNormalHost : ResolvedMaterialHost;
        /// <summary>Records one component-lifecycle diagnostic for derived component owners.</summary>
        protected void SetLastDiagnostic(StackMachineDiagnostic diagnostic) { LastDiagnostic = diagnostic; }

        private void BeginDiagnosticAttempt()
        {
            diagnosticAttempt++;
            if (diagnosticAttempt == 0) diagnosticAttempt = 1;
            diagnosticWasSurfaced = false;
        }

        private void SurfaceDiagnostic(StackMachineDiagnostic diagnostic)
        {
            if (diagnosticWasSurfaced || IsIntentionalSupersession(diagnostic)) return;
            diagnosticWasSurfaced = true;
            string code = string.IsNullOrEmpty(diagnostic.domainCode) ? diagnostic.code.ToString() : diagnostic.domainCode;
            Debug.LogError("Hot Bake '" + name + "' failed [" + code + "]: " + StackMachineDiagnostic.Format(diagnostic, "No structured diagnostic was returned."), this);
        }

        // Keep this list exhaustive for supersession diagnostic codes; add new supersession codes here when they are introduced.
        private static bool IsIntentionalSupersession(StackMachineDiagnostic diagnostic)
        {
            string code = diagnostic?.domainCode;
            return code == "RequestCoalesced" || code == "RequestStale" || code == "RequestCancelled" || code == "HotBakeStaleGenerationDiscarded";
        }
#if SHAPESYNC_USE_UNIVRM
        /// <summary>Gets whether the current build configuration requests optional VRM physics transport.</summary>
        protected bool IsPhysicsTransportEnabled => physicsTransport;
#else
        /// <summary>Gets false when the optional UniVRM integration is not compiled into this build.</summary>
        protected bool IsPhysicsTransportEnabled => false;
#endif
        /// <summary>Gets or sets the source Figure Prefab used by Startup and explicit Compile admission.</summary>
        public GameObject FigurePrefab { get => figurePrefab; set => figurePrefab = value; }
        /// <summary>Gets or sets the detached document asset used by Startup and explicit Compile admission.</summary>
        public ShapeSyncDocumentAsset Document { get => document; set => document = value; }
        /// <summary>Gets or sets the optional Atlas Schema input for this component-owned transaction.</summary>
        public UnityEngine.Object Atlas { get => atlas; set => atlas = value; }
        /// <summary>Gets or sets whether Compile rejects when no Atlas input is configured.</summary>
        public bool RequireAtlas { get => requireAtlas; set => requireAtlas = value; }
        /// <summary>Gets or sets the NORMAL-phase Texture StackMachine Host.</summary>
        /// <remarks>Changing either explicit host invalidates both cached resolved hosts; the next admission resolves the common-host contract again.</remarks>
        public TextureStackMachineHost NormalHost { get => normalHost; set { normalHost = value; resolvedNormalHost = null; resolvedMaterialHost = null; } }
        /// <summary>Gets or sets the MATERIAL-phase Texture StackMachine Host.</summary>
        /// <remarks>Changing either explicit host invalidates both cached resolved hosts; the next admission resolves the common-host contract again.</remarks>
        public TextureStackMachineHost MaterialHost { get => materialHost; set { materialHost = value; resolvedNormalHost = null; resolvedMaterialHost = null; } }

        /// <summary>Admits a Startup compile only when the serialized input contract is complete.</summary>
        /// <remarks>
        /// Derived components that override this hook must call <c>base.Start()</c> after their
        /// scene scope is ready. Startup admission is intentionally non-fatal: callers can use
        /// <see cref="Compile(out StackMachineDiagnostic)"/> after late input assignment.
        /// </remarks>
        protected virtual void Start()
        {
            // API compilation may begin before Unity dispatches Start in the same frame.
            // That overlap is normal lifecycle admission, not a user-visible failure.
            if (!IsCompileActive && HasStartupInputs()) Compile(out _);
        }

        /// <summary>Begins one explicit Hot Bake transaction from the current serialized inputs.</summary>
        /// <param name="diagnostic">A structured input, host-resolution, or transaction-admission diagnostic on failure.</param>
        /// <returns><see langword="true"/> when the component accepted the new transaction.</returns>
        /// <remarks>Use this public trigger for late input arrival, retry, and explicit re-bake. It never overlaps an already owned transaction.</remarks>
        public virtual bool Compile(out StackMachineDiagnostic diagnostic)
        {
            BeginDiagnosticAttempt();
            diagnostic = null;
            if (driver != null) { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeCompileActive", "Hot Bake component already owns one compile transaction."); LastDiagnostic = diagnostic; return false; }
            if (figurePrefab == null || document == null || (requireAtlas && atlas == null))
            { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeStartupInputIncomplete", "Hot Bake compile requires Figure, Document, and the configured optional Atlas input."); LastDiagnostic = diagnostic; return false; }
            if (atlas != null && !(atlas is AtlasSchema))
            { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeAtlasSchemaInvalid", "Hot Bake Atlas input must be an AtlasSchema."); LastDiagnostic = diagnostic; return false; }
            if (!TryResolveTextureHosts(out diagnostic)) { LastDiagnostic = diagnostic; return false; }
            driver = new HotBakeBuildDriver(ResolvedNormalHost, ResolvedMaterialHost);
            if (driver.TryBegin(figurePrefab, document, out diagnostic)) return true;
            driver.Dispose(); driver = null; LastDiagnostic = diagnostic; return false;
        }

        /// <summary>Begins one component-owned transaction from a detached runtime document.</summary>
        /// <remarks>Hybrid Hot Bake uses this for the current committed ShapeDirector state; Startup/API asset admission remains unchanged for Spawner and Figure.</remarks>
        protected bool BeginRuntimeDocumentCompile(ShapeSyncDocument runtimeDocument, out StackMachineDiagnostic diagnostic)
        {
            BeginDiagnosticAttempt();
            diagnostic = null;
            if (driver != null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeCompileActive", "Hot Bake component already owns one compile transaction.");
                LastDiagnostic = diagnostic;
                return false;
            }
            if (figurePrefab == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeStartupInputIncomplete", "Hot Bake compile requires a Figure input.");
                LastDiagnostic = diagnostic;
                return false;
            }
            if (!TryResolveTextureHosts(out diagnostic)) { LastDiagnostic = diagnostic; return false; }
            driver = new HotBakeBuildDriver(ResolvedNormalHost, ResolvedMaterialHost);
            if (driver.TryBegin(figurePrefab, runtimeDocument, out diagnostic)) return true;
            driver.Dispose();
            driver = null;
            LastDiagnostic = diagnostic;
            return false;
        }

        /// <summary>Pumps the single API/Startup-started transaction; derived components own successful artifact handoff.</summary>
        protected HumanoidBuildOperationStatus PumpCompile(out HumanoidBuildResult result, out StackMachineDiagnostic diagnostic)
        {
            result = null;
            diagnostic = null;
            if (driver == null) { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeOperationRequired", "Hot Bake component has no active compile transaction."); LastDiagnostic = diagnostic; return HumanoidBuildOperationStatus.Failed; }
            if (atlasOperation != null)
            {
                HumanoidBuildOperationStatus atlasStatus = atlasOperation.Pump(out diagnostic);
                if (atlasStatus == HumanoidBuildOperationStatus.Pending) return atlasStatus;
                atlasOperation.Dispose();
                atlasOperation = null;
                if (atlasStatus == HumanoidBuildOperationStatus.Succeeded)
                {
                    result = atlasResult;
                    atlasResult = null;
                    return atlasStatus;
                }
                atlasResult?.Dispose();
                atlasResult = null;
                LastDiagnostic = diagnostic;
                return atlasStatus;
            }
            HumanoidBuildOperationStatus status = driver.Pump(out result, out diagnostic);
            if (status == HumanoidBuildOperationStatus.Succeeded && atlas is AtlasSchema schema)
            {
                atlasResult = result;
                result = null;
                if (!HotBakeAtlasRuntimeOperation.TryCreate(schema, atlasResult.Mesh, ResolvedMaterialHost != null ? ResolvedMaterialHost : ResolvedNormalHost, out atlasOperation, out diagnostic))
                {
                    atlasResult.Dispose();
                    atlasResult = null;
                    LastDiagnostic = diagnostic;
                    return HumanoidBuildOperationStatus.Failed;
                }
                return HumanoidBuildOperationStatus.Pending;
            }
            if (status != HumanoidBuildOperationStatus.Pending && status != HumanoidBuildOperationStatus.Succeeded) LastDiagnostic = diagnostic;
            return status;
        }

        /// <summary>
        /// Pumps the current transaction and promotes its successful result into the supplied
        /// scene scope. Optional VRM physics is transported immediately before promotion when
        /// that feature is enabled; Core-only builds remain free of optional-package types.
        /// </summary>
        protected HumanoidBuildOperationStatus PumpAndCommitCompile(HotBakeArtifactSceneScope scope, out StackMachineDiagnostic diagnostic)
        {
            HumanoidBuildOperationStatus status = PumpCompile(out HumanoidBuildResult result, out diagnostic);
            if (status == HumanoidBuildOperationStatus.Pending) return status;
            if (status != HumanoidBuildOperationStatus.Succeeded)
            {
                DisposeDriver();
                return status;
            }

            var optionalOwnership = new List<IDisposable>();
            bool handoffCompleted = false;
            try
            {
#if SHAPESYNC_USE_UNIVRM
                if (physicsTransport)
                {
                    if (!driver.TryTransportVrmPhysics(result.Root, out IDisposable ownership, out diagnostic))
                    {
                        LastDiagnostic = diagnostic;
                        return HumanoidBuildOperationStatus.Failed;
                    }
                    optionalOwnership.Add(ownership);
                }
#endif
                if (!driver.TryCommitArtifact(result, optionalOwnership, scope, out HotBakeArtifactSet committed, out diagnostic))
                {
                    LastDiagnostic = diagnostic;
                    return HumanoidBuildOperationStatus.Failed;
                }
                artifactSet = committed;
                handoffCompleted = true;
                return status;
            }
            finally
            {
                if (!handoffCompleted)
                    for (int i = 0; i < optionalOwnership.Count; i++) optionalOwnership[i]?.Dispose();
                result?.Dispose();
                DisposeDriver();
            }
        }

        /// <summary>Promotes a derived component's already-inspected successful result into its scene scope.</summary>
        /// <remarks>The caller must dispose the result and driver after this call. Hybrid uses this seam to validate and rebind the candidate before ownership transfer.</remarks>
        protected bool TryCommitInspectedResult(HumanoidBuildResult result, HotBakeArtifactSceneScope scope, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (driver == null) { diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", "HotBakeOperationRequired", "Hot Bake component has no active compile transaction."); LastDiagnostic = diagnostic; return false; }
            var optionalOwnership = new List<IDisposable>();
            bool handoffCompleted = false;
            try
            {
#if SHAPESYNC_USE_UNIVRM
                if (physicsTransport)
                {
                    if (!driver.TryTransportVrmPhysics(result?.Root, out IDisposable ownership, out diagnostic)) { LastDiagnostic = diagnostic; return false; }
                    optionalOwnership.Add(ownership);
                }
#endif
                if (!driver.TryCommitArtifact(result, optionalOwnership, scope, out HotBakeArtifactSet committed, out diagnostic)) { LastDiagnostic = diagnostic; return false; }
                artifactSet = committed;
                handoffCompleted = true;
                return true;
            }
            finally
            {
                if (!handoffCompleted)
                    for (int i = 0; i < optionalOwnership.Count; i++) optionalOwnership[i]?.Dispose();
            }
        }

        /// <summary>Cancels the component-owned transaction without adding a new compile trigger.</summary>
        public void CancelCompile() { driver?.Cancel(); DisposeDriver(); }

        /// <summary>Releases a terminal driver after the derived component completed or rejected artifact handoff.</summary>
        protected void DisposeDriver()
        {
            atlasOperation?.Dispose();
            atlasOperation = null;
            atlasResult?.Dispose();
            atlasResult = null;
            driver?.Dispose();
            driver = null;
        }

        /// <summary>Releases the current promoted artifact without adding a compile trigger.</summary>
        protected void InvalidateArtifactSet()
        {
            artifactSet?.Dispose();
            artifactSet = null;
        }

        /// <summary>Releases the component-owned transaction and promoted artifact during teardown.</summary>
        /// <remarks>Derived components must release their own scene-scope state before or after this hook, then call <c>base.OnDestroy()</c> exactly once.</remarks>
        protected virtual void OnDestroy()
        {
            DisposeDriver();
            InvalidateArtifactSet();
        }

        /// <summary>Resolves the common Texture StackMachine Host contract before a Hot Bake transaction begins.</summary>
        /// <remarks>An explicitly configured Host is shared with the unconfigured phase. The Factory is called exactly once only when neither phase was configured.</remarks>
        private bool TryResolveTextureHosts(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            TextureStackMachineHost normal = normalHost;
            TextureStackMachineHost material = materialHost;
            if (normal == null && material != null) normal = material;
            else if (material == null && normal != null) material = normal;
            else if (normal == null && !TextureStaticMachineFactory.TryGetTSM(out normal, out diagnostic)) return false;

            material ??= normal;
            resolvedNormalHost = normal;
            resolvedMaterialHost = material;
            return true;
        }
        private bool HasStartupInputs() => figurePrefab != null && document != null && (!requireAtlas || atlas != null);
    }
}
