// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Detached Figure and ShapeSync document input accepted by <see cref="HumanoidCompiler"/>.</summary>
    public readonly struct HumanoidBuildSource
    {
        /// <summary>Creates one compiler source from a Figure root and detached ShapeSync document.</summary>
        public HumanoidBuildSource(GameObject figureRoot, ShapeSyncDocument document) { FigureRoot = figureRoot; Document = document; }
        /// <summary>Gets the read-only Figure root selected by the caller.</summary>
        public GameObject FigureRoot { get; }
        /// <summary>Gets the detached ShapeSync document snapshot.</summary>
        public ShapeSyncDocument Document { get; }
    }

    /// <summary>Reports the terminal or pending state of a Humanoid build phase.</summary>
    public enum HumanoidBuildPhaseStatus { Pending, Succeeded, Failed, Cancelled }
    /// <summary>Reports the caller-visible lifecycle state of <see cref="HumanoidBuildOperation"/>.</summary>
    public enum HumanoidBuildOperationStatus { Pending, Succeeded, Failed, Cancelled }
    /// <summary>Reports the read-only high-level phase used by an Editor or HotBake caller for progress display.</summary>
    public enum HumanoidBuildProgressPhase { Mesh, Material, Atlas, Completed, Failed, Cancelled }

    /// <summary>Owns one in-memory Texture completion until a build result or terminal cleanup disposes it.</summary>
    public sealed class HumanoidOwnedTexture : IDisposable
    {
        private Action<Texture> release;
        /// <summary>Creates an owned in-memory texture handle with its backend-provided release callback.</summary>
        public HumanoidOwnedTexture(Texture texture, Action<Texture> release) { Texture = texture; this.release = release; }
        /// <summary>Gets the texture while this handle owns it.</summary>
        public Texture Texture { get; private set; }
        /// <summary>Transfers this handle without releasing its texture.</summary>
        public HumanoidOwnedTexture Detach() { HumanoidOwnedTexture value = new HumanoidOwnedTexture(Texture, release); Texture = null; release = null; return value; }
        /// <inheritdoc />
        public void Dispose() { Texture value = Texture; Texture = null; Action<Texture> callback = release; release = null; callback?.Invoke(value); }
    }

    /// <summary>One final Mesh material slot together with its immutable source and adapter identity.</summary>
    public readonly struct HumanoidBuildMaterialSlot
    {
        /// <summary>Creates one material slot carrier.</summary>
        public HumanoidBuildMaterialSlot(MaterialId materialId, int submeshIndex, Material sourceMaterial, MaterialShaderAdapter adapter, bool preserveEffectiveNormal = false) { MaterialId = materialId; SubmeshIndex = submeshIndex; SourceMaterial = sourceMaterial; Adapter = adapter; PreserveEffectiveNormal = preserveEffectiveNormal; }
        /// <summary>Gets the Figure or Outfit material identity.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the final Mesh submesh index.</summary>
        public int SubmeshIndex { get; }
        /// <summary>Gets the read-only source Material to clone.</summary>
        public Material SourceMaterial { get; }
        /// <summary>Gets the shared read-only shader adapter.</summary>
        public MaterialShaderAdapter Adapter { get; }
        /// <summary>Gets whether the source Material already contains the authoritative runtime Normal escrow.</summary>
        public bool PreserveEffectiveNormal { get; }
    }

    /// <summary>One non-owned source Normal texture selected by the Mesh NORMAL semantic.</summary>
    public readonly struct HumanoidBuildSourceNormal
    {
        /// <summary>Creates one source Normal registration.</summary>
        public HumanoidBuildSourceNormal(MaterialId materialId, Texture texture) { MaterialId = materialId; Texture = texture; }
        /// <summary>Gets the receiving material identity.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the read-only source Normal texture.</summary>
        public Texture Texture { get; }
    }

    /// <summary>One owned computed Normal texture produced by the Mesh phase.</summary>
    public readonly struct HumanoidBuildComputedNormal
    {
        /// <summary>Creates one computed Normal completion.</summary>
        public HumanoidBuildComputedNormal(MaterialId materialId, HumanoidOwnedTexture texture) { MaterialId = materialId; Texture = texture; }
        /// <summary>Gets the receiving material identity.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the owned computed Normal completion.</summary>
        public HumanoidOwnedTexture Texture { get; }
    }

    /// <summary>Owns the final in-memory Mesh, candidate Materials, and computed Texture completions after a successful build.</summary>
    public sealed class InMemoryHumanoidMesh : IDisposable
    {
        private readonly List<Material> materials = new List<Material>();
        private readonly List<HumanoidBuildMaterialSlot> materialSlots = new List<HumanoidBuildMaterialSlot>();
        private readonly List<HumanoidOwnedTexture> textures = new List<HumanoidOwnedTexture>();
        private global::zgock.ShapeSync.StackMachine.AtlasBakerCandidatePages atlasPages;
        private bool disposed;
        /// <summary>Creates an in-memory output that owns the supplied final Mesh and optional Avatar.</summary>
        public InMemoryHumanoidMesh(Mesh mesh, Avatar avatar = null) { Mesh = mesh; Avatar = avatar; }
        /// <summary>Creates the resolved Pure Humanoid carrier that can be handed directly to artifact publishing.</summary>
        public InMemoryHumanoidMesh(GameObject root, Mesh mesh, Avatar avatar)
        {
            Root = root;
            Mesh = mesh;
            Avatar = avatar;
        }
        /// <summary>Gets the single 17.2-resolved Humanoid hierarchy. Later phases must not reconstruct it.</summary>
        public GameObject Root { get; private set; }
        internal bool TryReplaceRoot(GameObject value, out GameObject previous, out StackMachineDiagnostic diagnostic)
        {
            previous = null;
            diagnostic = null;
            if (disposed || value == null) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "ResolvedHumanoidRootRequired", "In-memory Humanoid Mesh requires one live resolved root."); return false; }
            previous = Root;
            Root = value;
            return true;
        }
        internal static void DestroyRootForLifecycle(GameObject value) { if (value == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        /// <summary>Gets the final merged Mesh while owned by this result.</summary>
        public Mesh Mesh { get; private set; }
        /// <summary>Gets the output-owned Avatar when the Mesh phase supplied one.</summary>
        public Avatar Avatar { get; private set; }
        /// <summary>Gets the candidate Materials in final submesh order.</summary>
        public IReadOnlyList<Material> Materials => materials.AsReadOnly();
        /// <summary>Gets the owned computed Texture completions retained by this in-memory result.</summary>
        public IReadOnlyList<HumanoidOwnedTexture> OwnedTextures => textures.AsReadOnly();
        /// <summary>Gets the immutable source and adapter identity for each final submesh Material.</summary>
        /// <remarks>This is Mesh-owned publish metadata. It does not add a second output to <see cref="HumanoidBuildResult"/>.</remarks>
        public IReadOnlyList<HumanoidBuildMaterialSlot> MaterialSlots => materialSlots.AsReadOnly();
        /// <summary>Gets the candidate-owned named Atlas pages, or null before an Atlas application succeeds.</summary>
        public global::zgock.ShapeSync.StackMachine.AtlasBakerCandidatePages AtlasPages => atlasPages;
        internal bool TrySetMaterials(Material[] values, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed || Mesh == null) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "InMemoryMeshDisposed", "In-memory Humanoid Mesh is no longer available."); return false; }
            materials.Clear();
            if (values != null) materials.AddRange(values);
            if (Root != null)
            {
                SkinnedMeshRenderer[] renderers = Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length != 1) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "ResolvedHumanoidRendererCountInvalid", "Resolved Humanoid carrier requires exactly one SkinnedMeshRenderer.", detail: renderers.Length.ToString()); return false; }
                renderers[0].sharedMaterials = values;
            }
            return true;
        }
        internal bool TrySetMaterialSlots(HumanoidBuildMaterialSlot[] values, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (disposed || Mesh == null) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "InMemoryMeshDisposed", "In-memory Humanoid Mesh is no longer available."); return false; }
            if (values == null || values.Length != materials.Count) { diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "MaterialSlotCountMismatch", "In-memory Humanoid Mesh requires one source Material slot per final submesh."); return false; }
            materialSlots.Clear();
            materialSlots.AddRange(values);
            return true;
        }
        internal void AddOwnedTexture(HumanoidOwnedTexture texture) { if (texture != null) textures.Add(texture); }
        internal void SetAtlasPages(global::zgock.ShapeSync.StackMachine.AtlasBakerCandidatePages value) { atlasPages = value; }
        internal bool TryReleaseOwnedTexture(Texture texture)
        {
            if (texture == null) return false;
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i] == null || textures[i].Texture != texture) continue;
                HumanoidOwnedTexture owned = textures[i];
                textures.RemoveAt(i);
                owned.Dispose();
                return true;
            }
            return false;
        }
        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            for (int i = 0; i < textures.Count; i++) textures[i]?.Dispose();
            textures.Clear();
            atlasPages?.Dispose(); atlasPages = null;
            for (int i = 0; i < materials.Count; i++) HumanoidMeshResourceCleanup.Destroy(materials[i]);
            materials.Clear();
            materialSlots.Clear();
            HumanoidMeshResourceCleanup.Destroy(Mesh); Mesh = null;
            HumanoidMeshResourceCleanup.Destroy(Avatar); Avatar = null;
            HumanoidMeshResourceCleanup.Destroy(Root); Root = null;
            disposed = true;
        }
    }

    /// <summary>One detached final Mesh payload returned by a backend after its Mesh phase succeeds.</summary>
    public sealed class MeshBuildPayload : IDisposable
    {
        private HumanoidBuildComputedNormal[] computedNormals;
        private HumanoidMeshVrmTransportProvenance vrmTransportProvenance;
        private bool disposed;
        /// <summary>Creates one backend-neutral Mesh phase payload.</summary>
        public MeshBuildPayload(InMemoryHumanoidMesh mesh, HumanoidBuildMaterialSlot[] materialSlots, HumanoidBuildSourceNormal[] sourceNormals, HumanoidBuildComputedNormal[] computedNormals, HumanoidMeshVrmTransportProvenance vrmTransportProvenance = null)
        {
            Mesh = mesh;
            MaterialSlots = Array.AsReadOnly(materialSlots ?? Array.Empty<HumanoidBuildMaterialSlot>());
            SourceNormals = Array.AsReadOnly(sourceNormals ?? Array.Empty<HumanoidBuildSourceNormal>());
            this.computedNormals = computedNormals ?? Array.Empty<HumanoidBuildComputedNormal>();
            this.vrmTransportProvenance = vrmTransportProvenance;
        }
        /// <summary>Gets the output Mesh ownership until transferred to <see cref="HumanoidBuildResult"/>.</summary>
        public InMemoryHumanoidMesh Mesh { get; private set; }
        /// <summary>Gets final submesh source mappings.</summary>
        public IReadOnlyList<HumanoidBuildMaterialSlot> MaterialSlots { get; }
        /// <summary>Gets non-owned source Normal registrations.</summary>
        public IReadOnlyList<HumanoidBuildSourceNormal> SourceNormals { get; }
        /// <summary>Gets owned computed Normal completions.</summary>
        public IReadOnlyList<HumanoidBuildComputedNormal> ComputedNormals => Array.AsReadOnly(computedNormals);
        internal InMemoryHumanoidMesh DetachMesh() { InMemoryHumanoidMesh value = Mesh; Mesh = null; return value; }
        internal HumanoidBuildComputedNormal[] DetachComputedNormals() { HumanoidBuildComputedNormal[] value = computedNormals; computedNormals = Array.Empty<HumanoidBuildComputedNormal>(); return value; }
        internal bool TryDetachVrmTransportProvenance(out HumanoidMeshVrmTransportProvenance provenance)
        {
            provenance = vrmTransportProvenance;
            vrmTransportProvenance = null;
            return provenance != null;
        }
        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            for (int i = 0; i < computedNormals.Length; i++) computedNormals[i].Texture?.Dispose();
            computedNormals = Array.Empty<HumanoidBuildComputedNormal>();
            vrmTransportProvenance?.Dispose(); vrmTransportProvenance = null;
            Mesh?.Dispose(); Mesh = null; disposed = true;
        }
    }

    /// <summary>Operation-owned VRM source-role carrier handed to the 17.6 publisher after a successful build.</summary>
    public sealed class HumanoidVrmTransportProvenance : IDisposable
    {
        private HumanoidMeshVrmTransportProvenance meshProvenance;
        internal HumanoidVrmTransportProvenance(HumanoidMeshVrmTransportProvenance meshProvenance) { this.meshProvenance = meshProvenance; }
        /// <summary>Gets the ATTACH lower order as detached Outfit logical names.</summary>
        public IReadOnlyList<string> AttachedOutfitLogicalNames => meshProvenance?.AttachedOutfitLogicalNames ?? Array.Empty<string>();
        /// <inheritdoc />
        public void Dispose() { meshProvenance?.Dispose(); meshProvenance = null; }
    }

    /// <summary>One pure Material semantic payload returned by a backend after its Material phase succeeds.</summary>
    public sealed class MaterialBuildPayload : IDisposable
    {
        private HumanoidMaterialSemanticPayload[] entries;
        /// <summary>Creates one backend-neutral Material payload list.</summary>
        public MaterialBuildPayload(HumanoidMaterialSemanticPayload[] entries) { this.entries = entries ?? Array.Empty<HumanoidMaterialSemanticPayload>(); }
        /// <summary>Gets the pure Material semantic entries.</summary>
        public IReadOnlyList<HumanoidMaterialSemanticPayload> Entries => Array.AsReadOnly(entries);
        internal HumanoidMaterialSemanticPayload[] DetachEntries() { HumanoidMaterialSemanticPayload[] value = entries; entries = Array.Empty<HumanoidMaterialSemanticPayload>(); return value; }
        /// <inheritdoc />
        public void Dispose() { for (int i = 0; i < entries.Length; i++) entries[i].MainTexture?.Dispose(); entries = Array.Empty<HumanoidMaterialSemanticPayload>(); }
    }

    /// <summary>Pure Material semantic values keyed by one <see cref="MaterialId"/>.</summary>
    public readonly struct HumanoidMaterialSemanticPayload
    {
        /// <summary>Creates one pure Material semantic payload.</summary>
        public HumanoidMaterialSemanticPayload(MaterialId materialId, HumanoidOwnedTexture mainTexture, bool hasColor, Color color, bool hasUvSet, Vector2 uvScale, Vector2 uvOffset)
        { MaterialId = materialId; MainTexture = mainTexture; HasColor = hasColor; Color = color; HasUvSet = hasUvSet; UvScale = uvScale; UvOffset = uvOffset; }
        /// <summary>Gets the receiving material identity.</summary>
        public MaterialId MaterialId { get; }
        /// <summary>Gets the owned BaseColor completion, or null when no TEXTURE clause was present.</summary>
        public HumanoidOwnedTexture MainTexture { get; }
        /// <summary>Gets whether <see cref="Color"/> is present.</summary>
        public bool HasColor { get; }
        /// <summary>Gets the Linear RGBA color.</summary>
        public Color Color { get; }
        /// <summary>Gets whether UV transform values are present.</summary>
        public bool HasUvSet { get; }
        /// <summary>Gets the UV scale.</summary>
        public Vector2 UvScale { get; }
        /// <summary>Gets the UV offset.</summary>
        public Vector2 UvOffset { get; }
    }

    /// <summary>Cross-assembly execution seam implemented by Editor and future HotBake backends.</summary>
    public interface IHumanoidBuildBackend
    {
        /// <summary>Begins the backend Mesh phase for the detached source.</summary>
        bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic);
        /// <summary>Pumps the active Mesh phase and returns its payload only on success.</summary>
        HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic);
        /// <summary>Begins the backend Material phase after Compiler candidate setup succeeds.</summary>
        bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic);
        /// <summary>Pumps the active Material phase and returns its payload only on success.</summary>
        HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic diagnostic);
        /// <summary>Cancels backend-owned active work and unhanded backend escrow.</summary>
        void Cancel();
    }

    /// <summary>Successful in-memory result handed to the Editor UI or a future HotBake caller.</summary>
    public sealed class HumanoidBuildResult : IDisposable
    {
        private InMemoryHumanoidMesh mesh;
        internal HumanoidBuildResult(InMemoryHumanoidMesh mesh) { this.mesh = mesh; }
        /// <summary>Gets the in-memory Mesh result and its owned Material / Texture references.</summary>
        public InMemoryHumanoidMesh Mesh => mesh;
        /// <summary>Gets the single resolved Humanoid GameObject handed through Material and publish without reconstruction.</summary>
        public GameObject Root => mesh?.Root;
        internal InMemoryHumanoidMesh DetachMeshForArtifactSet() { InMemoryHumanoidMesh value = mesh; mesh = null; return value; }
        /// <inheritdoc />
        public void Dispose() { mesh?.Dispose(); mesh = null; }
    }

    /// <summary>UnityEditor-independent compiler that sequences backend Mesh and Material execution.</summary>
    public sealed class HumanoidCompiler
    {
        /// <summary>Begins a caller-driven build. A backend Mesh phase is started only after source validation succeeds.</summary>
        public bool TryCompile(HumanoidBuildSource source, IHumanoidBuildBackend backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic)
        {
            operation = null;
            diagnostic = null;
            if (source.FigureRoot == null) return Reject("FigureRequired", "Humanoid Compiler requires a Figure root.", out diagnostic);
            if (source.Document == null) return Reject("DocumentRequired", "Humanoid Compiler requires a detached ShapeSyncDocument.", out diagnostic);
            if (backend == null) return Reject("BackendRequired", "Humanoid Compiler requires a concrete build backend.", out diagnostic);
            if (!backend.TryBeginMeshPhase(source, out diagnostic))
            {
                diagnostic ??= Failure("MeshBeginFailed", "Humanoid backend rejected the Mesh phase without a diagnostic.");
                return false;
            }
            operation = new HumanoidBuildOperation(backend);
            return true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic) { diagnostic = Failure(code, message); return false; }
        private static StackMachineDiagnostic Failure(string code, string message, string binding = null) => StackMachineDiagnostic.CreateDomain("humanoid", code, message, bindingName: binding);
    }

    /// <summary>Caller-driven build handle. It owns all unhanded compiler candidate and backend payload escrow.</summary>
    public sealed class HumanoidBuildOperation : IDisposable
    {
        private enum Phase { Mesh, Candidate, Material, Terminal }
        private sealed class Candidate
        {
            internal Candidate(Material source, MaterialShaderAdapter adapter, Material clone) { Source = source; Adapter = adapter; Clone = clone; }
            internal Material Source { get; }
            internal MaterialShaderAdapter Adapter { get; }
            internal Material Clone { get; }
        }

        private readonly IHumanoidBuildBackend backend;
        private readonly Dictionary<MaterialId, Candidate> candidates = new Dictionary<MaterialId, Candidate>();
        private readonly HashSet<MaterialId> preserveEffectiveNormals = new HashSet<MaterialId>();
        private Phase phase;
        private MeshBuildPayload meshPayload;
        private HumanoidVrmTransportProvenance vrmTransportProvenance;
        private bool vrmTransportProvenanceTaken;
        private bool backendCancelled;
        private bool disposed;

        internal HumanoidBuildOperation(IHumanoidBuildBackend backend) { this.backend = backend; Status = HumanoidBuildOperationStatus.Pending; phase = Phase.Mesh; }
        /// <summary>Gets the current caller-visible operation state.</summary>
        public HumanoidBuildOperationStatus Status { get; private set; }
        /// <summary>Gets the terminal structured diagnostic, or null after successful completion.</summary>
        public StackMachineDiagnostic Diagnostic { get; private set; }
        /// <summary>Gets the current Mesh or Material progress phase without exposing a backend execution handle.</summary>
        public HumanoidBuildProgressPhase ProgressPhase
        {
            get
            {
                if (Status == HumanoidBuildOperationStatus.Succeeded) return HumanoidBuildProgressPhase.Completed;
                if (Status == HumanoidBuildOperationStatus.Failed) return HumanoidBuildProgressPhase.Failed;
                if (Status == HumanoidBuildOperationStatus.Cancelled) return HumanoidBuildProgressPhase.Cancelled;
                return phase == Phase.Mesh ? HumanoidBuildProgressPhase.Mesh : HumanoidBuildProgressPhase.Material;
            }
        }

        /// <summary>Transfers the completed Mesh lower VRM source-role carrier once to the 17.6 publish owner.</summary>
        public bool TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic diagnostic)
        {
            provenance = null;
            diagnostic = null;
            if (disposed) return Reject("VrmTransportOperationDisposed", "Humanoid build operation has already been disposed.", out diagnostic);
            if (Status != HumanoidBuildOperationStatus.Succeeded) return Reject("VrmTransportBuildNotSucceeded", "VRM transport provenance is available only after successful Humanoid build completion.", out diagnostic);
            if (vrmTransportProvenanceTaken) return Reject("VrmTransportProvenanceAlreadyTaken", "VRM transport provenance was already transferred to the publish owner.", out diagnostic);
            if (vrmTransportProvenance == null) return Reject("VrmTransportProvenanceMissing", "Humanoid backend completed without a VRM transport provenance carrier.", out diagnostic);
            provenance = vrmTransportProvenance;
            vrmTransportProvenance = null;
            vrmTransportProvenanceTaken = true;
            return true;
        }

        /// <summary>Pumps one backend phase and returns the in-memory result only once after success.</summary>
        public HumanoidBuildOperationStatus Pump(out HumanoidBuildResult buildResult, out StackMachineDiagnostic diagnostic)
        {
            buildResult = null;
            diagnostic = null;
            if (Status != HumanoidBuildOperationStatus.Pending)
            {
                diagnostic = Diagnostic;
                return Status;
            }

            if (phase == Phase.Mesh) return PumpMesh(out buildResult, out diagnostic);
            if (phase == Phase.Candidate) return PumpCandidate(out buildResult, out diagnostic);
            return PumpMaterial(out buildResult, out diagnostic);
        }

        /// <summary>Cancels active backend work and releases every compiler-owned unhanded resource.</summary>
        public void Cancel()
        {
            if (disposed || Status != HumanoidBuildOperationStatus.Pending) return;
            CancelBackend();
            DisposeEscrow();
            Status = HumanoidBuildOperationStatus.Cancelled;
            phase = Phase.Terminal;
            Diagnostic = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            if (Status == HumanoidBuildOperationStatus.Pending) Cancel();
            vrmTransportProvenance?.Dispose();
            vrmTransportProvenance = null;
            disposed = true;
        }

        private HumanoidBuildOperationStatus PumpMesh(out HumanoidBuildResult buildResult, out StackMachineDiagnostic diagnostic)
        {
            buildResult = null;
            HumanoidBuildPhaseStatus backendStatus = backend.PumpMeshPhase(out MeshBuildPayload payload, out diagnostic);
            if (backendStatus == HumanoidBuildPhaseStatus.Pending) return Status;
            if (backendStatus == HumanoidBuildPhaseStatus.Cancelled) return Cancelled(out diagnostic);
            if (backendStatus != HumanoidBuildPhaseStatus.Succeeded || payload == null) return Fail(diagnostic ?? Failure("MeshPayloadMissing", "Humanoid backend completed Mesh phase without a payload."), out diagnostic);

            if (payload.TryDetachVrmTransportProvenance(out HumanoidMeshVrmTransportProvenance meshProvenance))
                vrmTransportProvenance = new HumanoidVrmTransportProvenance(meshProvenance);
            meshPayload = payload;
            phase = Phase.Candidate;
            return Status;
        }

        private HumanoidBuildOperationStatus PumpCandidate(out HumanoidBuildResult buildResult, out StackMachineDiagnostic diagnostic)
        {
            buildResult = null;
            if (!TryPrepareCandidates(meshPayload, out diagnostic)) return Fail(diagnostic, out diagnostic);
            if (!backend.TryBeginMaterialPhase(meshPayload, out diagnostic)) return Fail(diagnostic ?? Failure("MaterialBeginFailed", "Humanoid backend rejected Material phase without a diagnostic."), out diagnostic);
            phase = Phase.Material;
            return Status;
        }

        private HumanoidBuildOperationStatus PumpMaterial(out HumanoidBuildResult buildResult, out StackMachineDiagnostic diagnostic)
        {
            buildResult = null;
            HumanoidBuildPhaseStatus backendStatus = backend.PumpMaterialPhase(out MaterialBuildPayload payload, out diagnostic);
            if (backendStatus == HumanoidBuildPhaseStatus.Pending) return Status;
            if (backendStatus == HumanoidBuildPhaseStatus.Cancelled) return Cancelled(out diagnostic);
            if (backendStatus != HumanoidBuildPhaseStatus.Succeeded || payload == null) return Fail(diagnostic ?? Failure("MaterialPayloadMissing", "Humanoid backend completed Material phase without a payload."), out diagnostic);

            using (payload)
            {
                if (!TryApplyMaterialPayload(payload, out diagnostic)) return Fail(diagnostic, out diagnostic);
            }
            if (!TryApplyComputedNormals(out diagnostic)) return Fail(diagnostic, out diagnostic);

            InMemoryHumanoidMesh mesh = meshPayload.DetachMesh();
            meshPayload.Dispose();
            meshPayload = null;
            candidates.Clear(); preserveEffectiveNormals.Clear();
            Status = HumanoidBuildOperationStatus.Succeeded;
            phase = Phase.Terminal;
            buildResult = new HumanoidBuildResult(mesh);
            return Status;
        }

        private bool TryPrepareCandidates(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (payload.Mesh == null || payload.Mesh.Mesh == null) return Reject("MeshRequired", "Mesh phase payload requires an in-memory final Mesh.", out diagnostic);
            int submeshCount = payload.Mesh.Mesh.subMeshCount;
            var materials = new Material[submeshCount];
            var slots = new HumanoidBuildMaterialSlot[submeshCount];
            var assigned = new bool[submeshCount];
            for (int i = 0; i < payload.MaterialSlots.Count; i++)
            {
                HumanoidBuildMaterialSlot slot = payload.MaterialSlots[i];
                if (!slot.MaterialId.IsValid || slot.SubmeshIndex < 0 || slot.SubmeshIndex >= submeshCount || slot.SourceMaterial == null || slot.Adapter == null)
                    return Reject("MaterialSlotInvalid", "Mesh phase returned an invalid Material slot.", out diagnostic, slot.MaterialId.EntryId);
                if (assigned[slot.SubmeshIndex]) return Reject("MaterialSlotDuplicate", "Mesh phase returned duplicate final submesh Material slots.", out diagnostic, slot.MaterialId.EntryId);
                assigned[slot.SubmeshIndex] = true;
                if (slot.PreserveEffectiveNormal) preserveEffectiveNormals.Add(slot.MaterialId);
                if (!candidates.TryGetValue(slot.MaterialId, out Candidate candidate))
                {
                    if (!slot.Adapter.TryCreateInMemoryClone(slot.SourceMaterial, out Material clone, out MaterialProxyDiagnostic adapterDiagnostic))
                        return Reject("MaterialCloneRejected", adapterDiagnostic.message, out diagnostic, slot.MaterialId.EntryId);
                    candidate = new Candidate(slot.SourceMaterial, slot.Adapter, clone);
                    candidates.Add(slot.MaterialId, candidate);
                }
                else if (candidate.Source != slot.SourceMaterial || candidate.Adapter != slot.Adapter)
                    return Reject("MaterialIdSourceMismatch", "One MaterialId resolved to inconsistent source Material or Adapter values.", out diagnostic, slot.MaterialId.EntryId);
                materials[slot.SubmeshIndex] = candidate.Clone;
                slots[slot.SubmeshIndex] = slot;
            }
            for (int i = 0; i < assigned.Length; i++) if (!assigned[i]) return Reject("MaterialSlotMissing", "Mesh phase did not provide a Material slot for every final submesh.", out diagnostic);
            if (!payload.Mesh.TrySetMaterials(materials, out diagnostic)) return false;
            if (!payload.Mesh.TrySetMaterialSlots(slots, out diagnostic)) return false;
            var normals = new HashSet<MaterialId>();
            for (int i = 0; i < payload.SourceNormals.Count; i++)
            {
                HumanoidBuildSourceNormal normal = payload.SourceNormals[i];
                if (!normal.MaterialId.IsValid || normal.Texture == null || !normals.Add(normal.MaterialId) || !candidates.TryGetValue(normal.MaterialId, out Candidate candidate))
                    return Reject("SourceNormalInvalid", "Mesh source Normal registry is missing, duplicate, or does not resolve to a candidate Material.", out diagnostic, normal.MaterialId.EntryId);
                if (!candidate.Adapter.TryApplyInMemory(candidate.Clone, new MaterialProxySemanticValues { applyNormalTexture = true, normalTexture = normal.Texture }, out _, out MaterialProxyDiagnostic adapterDiagnostic))
                    return Reject("SourceNormalRejected", adapterDiagnostic.message, out diagnostic, normal.MaterialId.EntryId);
            }
            return true;
        }

        private bool TryApplyMaterialPayload(MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var ids = new HashSet<MaterialId>();
            HumanoidMaterialSemanticPayload[] entries = payload.DetachEntries();
            for (int i = 0; i < entries.Length; i++)
            {
                HumanoidMaterialSemanticPayload entry = entries[i];
                if (!entry.MaterialId.IsValid || !ids.Add(entry.MaterialId) || !candidates.TryGetValue(entry.MaterialId, out Candidate candidate))
                {
                    DisposeEntries(entries, i);
                    return Reject("MaterialPayloadInvalid", "Material payload contains a duplicate or unknown MaterialId.", out diagnostic, entry.MaterialId.EntryId);
                }
                var values = new MaterialProxySemanticValues { applyBaseColorTexture = entry.MainTexture != null, baseColorTexture = entry.MainTexture?.Texture, applyColor = entry.HasColor, color = entry.Color, applyUvTransform = entry.HasUvSet, uvScale = entry.UvScale, uvOffset = entry.UvOffset };
                if (!candidate.Adapter.TryApplyInMemory(candidate.Clone, values, out _, out MaterialProxyDiagnostic adapterDiagnostic))
                {
                    DisposeEntries(entries, i);
                    return Reject("MaterialApplyRejected", adapterDiagnostic.message, out diagnostic, entry.MaterialId.EntryId);
                }
                if (entry.MainTexture != null) meshPayload.Mesh.AddOwnedTexture(entry.MainTexture.Detach());
            }
            return true;
        }

        private bool TryApplyComputedNormals(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            var ids = new HashSet<MaterialId>();
            HumanoidBuildComputedNormal[] normals = meshPayload.DetachComputedNormals();
            for (int i = 0; i < normals.Length; i++)
            {
                HumanoidBuildComputedNormal normal = normals[i];
                if (!normal.MaterialId.IsValid || normal.Texture == null || !ids.Add(normal.MaterialId) || !candidates.TryGetValue(normal.MaterialId, out Candidate candidate))
                {
                    DisposeNormals(normals, i);
                    return Reject("ComputedNormalInvalid", "Computed Normal payload is missing, duplicate, or does not resolve to a candidate Material.", out diagnostic, normal.MaterialId.EntryId);
                }
                if (preserveEffectiveNormals.Contains(normal.MaterialId)) { normal.Texture.Dispose(); continue; }
                if (!candidate.Adapter.TryApplyInMemory(candidate.Clone, new MaterialProxySemanticValues { applyNormalTexture = true, normalTexture = normal.Texture.Texture }, out _, out MaterialProxyDiagnostic adapterDiagnostic))
                {
                    DisposeNormals(normals, i);
                    return Reject("ComputedNormalRejected", adapterDiagnostic.message, out diagnostic, normal.MaterialId.EntryId);
                }
                meshPayload.Mesh.AddOwnedTexture(normal.Texture.Detach());
            }
            return true;
        }

        private HumanoidBuildOperationStatus Cancelled(out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            CancelBackend();
            DisposeEscrow();
            Status = HumanoidBuildOperationStatus.Cancelled;
            phase = Phase.Terminal;
            return Status;
        }

        private HumanoidBuildOperationStatus Fail(StackMachineDiagnostic failure, out StackMachineDiagnostic diagnostic)
        {
            CancelBackend();
            DisposeEscrow();
            Diagnostic = failure ?? Failure("BackendFailure", "Humanoid backend failed without a diagnostic.");
            diagnostic = Diagnostic;
            Status = HumanoidBuildOperationStatus.Failed;
            phase = Phase.Terminal;
            return Status;
        }

        private void CancelBackend() { if (backendCancelled) return; backend.Cancel(); backendCancelled = true; }
        private void DisposeEscrow()
        {
            meshPayload?.Dispose(); meshPayload = null;
            vrmTransportProvenance?.Dispose(); vrmTransportProvenance = null;
            foreach (Candidate candidate in candidates.Values) HumanoidMeshResourceCleanup.Destroy(candidate.Clone);
            candidates.Clear(); preserveEffectiveNormals.Clear();
        }
        private static void DisposeEntries(HumanoidMaterialSemanticPayload[] entries, int start) { for (int i = start; i < entries.Length; i++) entries[i].MainTexture?.Dispose(); }
        private static void DisposeNormals(HumanoidBuildComputedNormal[] normals, int start) { for (int i = start; i < normals.Length; i++) normals[i].Texture?.Dispose(); }
        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic, string binding = null) { diagnostic = Failure(code, message, binding); return false; }
        private static StackMachineDiagnostic Failure(string code, string message, string binding = null) => StackMachineDiagnostic.CreateDomain("humanoid", code, message, bindingName: binding);
    }
}
