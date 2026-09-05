// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine.Humanoid
{
    /// <summary>Scene-scoped owned resources produced by one successful Hot Bake and shared by later spawns.</summary>
    /// <remarks>The set owns its immutable spawn template, Mesh, Avatar, Material, computed Texture, and optional package disposables. It never owns spawned GameObjects.</remarks>
    public sealed class HotBakeArtifactSet : IDisposable
    {
        private const int LinearRgbaHalfBytesPerPixel = 8;
        private readonly List<IDisposable> optionalOwnership;
        private InMemoryHumanoidMesh resources;
        private int referenceCount = 1;
        private bool ownerReleased;
        private bool invalidated;

        private HotBakeArtifactSet(InMemoryHumanoidMesh resources, List<IDisposable> optionalOwnership)
        {
            this.resources = resources;
            this.optionalOwnership = optionalOwnership;
        }

        /// <summary>Gets whether the set still owns resources and may produce a retained lease.</summary>
        public bool IsAvailable => !invalidated && resources != null && referenceCount > 0;
        /// <summary>Gets the owner plus retained lease count while the set is alive.</summary>
        public int ReferenceCount => referenceCount;
        /// <summary>Gets the final merged Mesh while the set is alive.</summary>
        public Mesh Mesh => resources?.Mesh;
        /// <summary>Gets the rebuilt Avatar while the set is alive.</summary>
        public Avatar Avatar => resources?.Avatar;
        /// <summary>Gets the set-owned immutable candidate hierarchy from which later spawn code clones instances.</summary>
        public GameObject TemplateRoot => resources?.Root;
        /// <summary>Gets final Materials in submesh order while the set is alive.</summary>
        public IReadOnlyList<Material> Materials => resources?.Materials ?? Array.Empty<Material>();
        /// <summary>Gets the Runtime source identity for each final material slot while the set is alive.</summary>
        public IReadOnlyList<HumanoidBuildMaterialSlot> MaterialSlots => resources?.MaterialSlots ?? Array.Empty<HumanoidBuildMaterialSlot>();
        /// <summary>Gets owned computed textures while the set is alive.</summary>
        public IReadOnlyList<HumanoidOwnedTexture> OwnedTextures => resources?.OwnedTextures ?? Array.Empty<HumanoidOwnedTexture>();
        /// <summary>Gets the exact uncompressed Linear RGBAHalf byte estimate of owned computed textures and completed Atlas pages.</summary>
        public long GpuByteCount
        {
            get
            {
                long bytes = 0;
                var seen = new HashSet<Texture>();
                IReadOnlyList<HumanoidOwnedTexture> textures = OwnedTextures;
                for (int i = 0; i < textures.Count; i++)
                {
                    Texture texture = textures[i]?.Texture;
                    if (texture != null && seen.Add(texture)) bytes += (long)texture.width * texture.height * LinearRgbaHalfBytesPerPixel;
                }

                IReadOnlyList<global::zgock.ShapeSync.StackMachine.AtlasBakerPageCompletion> pages = resources?.AtlasPages?.Pages;
                if (pages != null)
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        Texture texture = pages[i]?.Texture;
                        if (texture != null && seen.Add(texture)) bytes += (long)texture.width * texture.height * LinearRgbaHalfBytesPerPixel;
                    }
                }
                return bytes;
            }
        }

        /// <summary>Transfers a successful build result and optional package-owned disposables into one artifact set.</summary>
        public static bool TryCreate(HumanoidBuildResult result, IReadOnlyList<IDisposable> optionalDisposables, out HotBakeArtifactSet artifactSet, out StackMachineDiagnostic diagnostic)
        {
            artifactSet = null;
            diagnostic = null;
            InMemoryHumanoidMesh candidate = result?.Mesh;
            if (candidate == null || candidate.Mesh == null)
                return Reject("HotBakeArtifactBuildResultRequired", "Hot Bake artifact creation requires one undisposed successful build result.", out diagnostic);
            if (candidate.Root == null)
                return Reject("HotBakeArtifactTemplateRequired", "Hot Bake artifact creation requires the resolved candidate hierarchy as its spawn template.", out diagnostic);
            if (!TryCopyOptionalOwnership(optionalDisposables, out List<IDisposable> optional, out diagnostic)) return false;

            artifactSet = new HotBakeArtifactSet(result.DetachMeshForArtifactSet(), optional);
            return true;
        }

        /// <summary>Acquires one retained lease for a future spawn or warm holder.</summary>
        public bool TryAcquire(out HotBakeArtifactLease lease, out StackMachineDiagnostic diagnostic)
        {
            lease = null;
            diagnostic = null;
            if (!IsAvailable)
                return Reject("HotBakeArtifactUnavailable", "Hot Bake artifact resources were already released and cannot be retained.", out diagnostic);
            if (ownerReleased)
                return Reject("HotBakeArtifactOwnerReleased", "Hot Bake artifact resources cannot be retained after their owning component released the set.", out diagnostic);
            referenceCount++;
            lease = new HotBakeArtifactLease(this);
            return true;
        }

        /// <summary>Releases the set owner's reference once. Retained leases remain valid until disposed.</summary>
        public void Dispose()
        {
            if (ownerReleased) return;
            ownerReleased = true;
            ReleaseReference();
        }

        /// <summary>Immediately releases this set when its scene-scoped source becomes invalid.</summary>
        public void Invalidate()
        {
            if (invalidated) return;
            invalidated = true;
            ownerReleased = true;
            referenceCount = 0;
            for (int i = 0; i < optionalOwnership.Count; i++) optionalOwnership[i]?.Dispose();
            optionalOwnership.Clear();
            resources?.Dispose();
            resources = null;
        }

        internal void ReleaseLease()
        {
            if (referenceCount <= 0) return;
            ReleaseReference();
        }

        private void ReleaseReference()
        {
            referenceCount--;
            if (referenceCount != 0) return;
            for (int i = 0; i < optionalOwnership.Count; i++) optionalOwnership[i]?.Dispose();
            optionalOwnership.Clear();
            resources?.Dispose();
            resources = null;
        }

        private static bool TryCopyOptionalOwnership(IReadOnlyList<IDisposable> values, out List<IDisposable> copied, out StackMachineDiagnostic diagnostic)
        {
            copied = new List<IDisposable>();
            diagnostic = null;
            if (values == null) return true;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null) return Reject("HotBakeArtifactOptionalOwnershipNull", "Hot Bake artifact ownership cannot contain a null optional disposable.", out diagnostic);
                for (int j = 0; j < i; j++)
                    if (ReferenceEquals(values[j], values[i]))
                        return Reject("HotBakeArtifactOptionalOwnershipDuplicate", "Hot Bake artifact ownership cannot transfer the same optional disposable more than once.", out diagnostic);
                copied.Add(values[i]);
            }
            return true;
        }

        private static bool Reject(string code, string message, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("hotbake", code, message);
            return false;
        }
    }

    /// <summary>One retained artifact-set reference. Disposing it never destroys spawned GameObjects.</summary>
    public sealed class HotBakeArtifactLease : IDisposable
    {
        private HotBakeArtifactSet artifactSet;
        internal HotBakeArtifactLease(HotBakeArtifactSet artifactSet) { this.artifactSet = artifactSet; }
        /// <summary>Gets the retained artifact set until this lease is disposed.</summary>
        public HotBakeArtifactSet ArtifactSet => artifactSet;
        /// <inheritdoc />
        public void Dispose()
        {
            HotBakeArtifactSet owner = artifactSet;
            artifactSet = null;
            owner?.ReleaseLease();
        }
    }
}
