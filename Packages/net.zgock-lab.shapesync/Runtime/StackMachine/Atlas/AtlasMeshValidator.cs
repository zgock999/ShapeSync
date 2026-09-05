// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Validates Phase-0 Atlas mesh invariants without mutating source assets.</summary>
    public static class AtlasMeshValidator
    {
        /// <summary>Validates the Phase-0 Atlas texture semantics for one entry.</summary>
        public static bool TryValidateSemantics(Texture baseColor, Texture normal, out StackMachineDiagnostic diagnostic)
            => TryValidateSemantics(baseColor, normal, null, null, null, out diagnostic);

        /// <summary>Validates Atlas texture semantics after asking the assigned adapter about semantic neutrality.</summary>
        public static bool TryValidateSemantics(Texture baseColor, Texture normal, Material material, MaterialShaderAdapter adapter, string normalTexturePropertyName, out StackMachineDiagnostic diagnostic)
        {
            if (baseColor == null) return Fail("AtlasBaseColorRequired", "Atlas requires a BaseColor texture.", out diagnostic);
            if (normal == null) return Fail("AtlasNormalRequired", "Atlas requires a Normal texture.", out diagnostic);
            bool normalIsNeutral = false;
            if (adapter != null && !string.IsNullOrWhiteSpace(normalTexturePropertyName))
            {
                if (!adapter.TryGetEffectiveNeutralTexture(material, normalTexturePropertyName, normal, out normalIsNeutral, out MaterialProxyDiagnostic adapterDiagnostic))
                    return Fail("AtlasAdapterInvalid", adapterDiagnostic.message, out diagnostic);
            }
            if (!TextureGpuCapabilityProbe.IsPhase0Edge(baseColor.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(baseColor.height)
                || (!normalIsNeutral && (!TextureGpuCapabilityProbe.IsPhase0Edge(normal.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(normal.height))))
                return Fail("AtlasSourceExtentUnsupported", "Atlas source texture extent is unsupported.", out diagnostic);
            diagnostic = null;
            return true;
        }

        /// <summary>Describes the owner and stable material key of one Atlas validation target.</summary>
        public sealed class Target
        {
            /// <summary>Creates one validation target.</summary>
            public Target(string owner, MaterialId materialId, int submesh, bool excluded = false, Material material = null, MaterialShaderAdapter adapter = null, Texture baseColor = null, Texture normal = null, int pageIndex = -1, bool hasUvSet = false, Vector2 uvScale = default, Vector2 uvOffset = default, string normalTexturePropertyName = null) { Owner = owner ?? string.Empty; MaterialId = materialId; Submesh = submesh; Excluded = excluded; Material = material; Adapter = adapter; BaseColor = baseColor; Normal = normal; PageIndex = pageIndex; HasUvSet = hasUvSet; UvScale = hasUvSet ? uvScale : Vector2.one; UvOffset = hasUvSet ? uvOffset : Vector2.zero; NormalTexturePropertyName = normalTexturePropertyName; }
            /// <summary>Gets the Figure or Outfit owner.</summary>
            public string Owner { get; }
            /// <summary>Gets the stable material key.</summary>
            public MaterialId MaterialId { get; }
            /// <summary>Gets the resolved final mesh submesh.</summary>
            public int Submesh { get; }
            /// <summary>Gets whether Atlas validation skips this target.</summary>
            public bool Excluded { get; }
            /// <summary>Gets the resolved candidate material for final validation.</summary>
            public Material Material { get; }
            /// <summary>Gets the shader semantic adapter for final validation.</summary>
            public MaterialShaderAdapter Adapter { get; }
            /// <summary>Gets the resolved BaseColor source texture.</summary>
            public Texture BaseColor { get; }
            /// <summary>Gets the resolved Normal source texture.</summary>
            public Texture Normal { get; }
            /// <summary>Gets the adapter property that supplied the resolved Normal source texture.</summary>
            public string NormalTexturePropertyName { get; }
            /// <summary>Gets the dense Atlas page index when resolved.</summary>
            public int PageIndex { get; }
            /// <summary>Gets whether the final candidate material transform comes from a Document UVSET payload.</summary>
            public bool HasUvSet { get; }
            /// <summary>Gets the resolved Document UVSET scale, or identity when absent.</summary>
            public Vector2 UvScale { get; }
            /// <summary>Gets the resolved Document UVSET offset, or zero when absent.</summary>
            public Vector2 UvOffset { get; }
        }

        /// <summary>Validates targets while retaining owner and MaterialId diagnostic context.</summary>
        public static bool TryValidate(Mesh mesh, IReadOnlyList<Target> targets, IReadOnlyList<Texture> sourceTextures, out StackMachineDiagnostic diagnostic)
        {
            if (targets == null || targets.Count == 0) return Fail("AtlasSubmeshRequired", "Atlas validation requires at least one Atlas submesh.", out diagnostic);
            if (sourceTextures != null && sourceTextures.Count != 0) return Fail("AtlasSourceTexturesUnscoped", "Target validation requires semantic source textures on each resolved target.", out diagnostic);
            var usedSubmeshes = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (target == null || !target.MaterialId.IsValid) return Fail("AtlasTargetInvalid", "Atlas validation target is invalid.", out diagnostic);
                if (target.Excluded) continue;
                if (!usedSubmeshes.Add(target.Submesh)) return FailForTarget(target, "AtlasSubmeshDuplicateTarget", "Atlas validation targets must map each submesh once.", null, out diagnostic);
                if (!TryValidate(mesh, new[] { target.Submesh }, null, out StackMachineDiagnostic meshDiagnostic)) return FailForTarget(target, meshDiagnostic.domainCode, meshDiagnostic.message, meshDiagnostic.detail, out diagnostic);
            }
            diagnostic = null;
            return true;
        }

        /// <summary>Validates final Baker targets, including their resolved material, semantic textures, and adapter.</summary>
        public static bool TryValidateResolved(Mesh mesh, IReadOnlyList<Target> targets, out StackMachineDiagnostic diagnostic)
        {
            if (!TryValidate(mesh, targets, null, out diagnostic)) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (target.Excluded) continue;
                if (!TryValidateTargetMaterial(target, true, out StackMachineDiagnostic materialDiagnostic)) return FailForTarget(target, materialDiagnostic.domainCode, materialDiagnostic.message, materialDiagnostic.detail, out diagnostic);
            }
            diagnostic = null;
            return true;
        }
        /// <summary>Validates raw mesh topology and optional unscoped source textures for Oracle Layer 2.</summary>
        /// <remarks>This primitive has no entry ownership. Editor and Baker validation use the target overloads for owner / MaterialId / page diagnostics.</remarks>
        public static bool TryValidate(Mesh mesh, IReadOnlyList<int> atlasSubmeshes, IReadOnlyList<Texture> sourceTextures, out StackMachineDiagnostic diagnostic)
        {
            if (mesh == null) return Fail("AtlasMeshRequired", "Atlas validation requires a mesh.", out diagnostic);
            if (atlasSubmeshes == null || atlasSubmeshes.Count == 0) return Fail("AtlasSubmeshRequired", "Atlas validation requires at least one Atlas submesh.", out diagnostic);
            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length != mesh.vertexCount) return Fail("AtlasUv0Required", "Atlas validation requires one UV0 per vertex.", out diagnostic);
            var atlasSubmeshSet = new HashSet<int>(atlasSubmeshes);
            for (int i = 0; i < atlasSubmeshes.Count; i++)
            {
                int submesh = atlasSubmeshes[i];
                if (submesh < 0 || submesh >= mesh.subMeshCount) return Fail("AtlasSubmeshMissing", "Atlas entry references a missing submesh.", out diagnostic);
                int[] indices = mesh.GetIndices(submesh);
                for (int j = 0; j < indices.Length; j++)
                {
                    int index = indices[j];
                    Vector2 value = uv[index];
                    if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || value.x < 0f || value.x > 1f || value.y < 0f || value.y > 1f) return Fail("AtlasUv0OutOfRange", "Atlas UV0 must be finite and within [0,1].", out diagnostic);
                }
                if (mesh.GetTopology(submesh) == MeshTopology.Triangles) for (int triangle = 0; triangle + 2 < indices.Length; triangle += 3)
                {
                    Vector2 a = uv[indices[triangle]], b = uv[indices[triangle + 1]], c = uv[indices[triangle + 2]];
                    if (Mathf.Approximately((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x), 0f)) return Fail("AtlasUv0Degenerate", "Atlas UV0 triangle is degenerate.", out diagnostic);
                }
            }
            foreach (int atlasSubmesh in atlasSubmeshSet)
            {
                var atlasVertices = new HashSet<int>(mesh.GetIndices(atlasSubmesh));
                for (int otherSubmesh = 0; otherSubmesh < mesh.subMeshCount; otherSubmesh++)
                {
                    if (otherSubmesh == atlasSubmesh) continue;
                    var shared = new HashSet<int>();
                    foreach (int index in mesh.GetIndices(otherSubmesh)) if (atlasVertices.Contains(index)) shared.Add(index);
                    if (shared.Count != 0)
                    {
                        var sample = new List<int>(16);
                        foreach (int index in shared) { if (sample.Count == 16) break; sample.Add(index); }
                        return Fail("AtlasSharedVertex", "An Atlas submesh shares a referenced vertex with another submesh.", out diagnostic, "atlasSubmesh=" + atlasSubmesh + ";otherSubmesh=" + otherSubmesh + ";sharedVertexCount=" + shared.Count + ";vertices=" + string.Join(",", sample));
                    }
                }
            }
            if (sourceTextures != null) for (int i = 0; i < sourceTextures.Count; i++)
            {
                Texture texture = sourceTextures[i];
                if (texture == null || !TextureGpuCapabilityProbe.IsPhase0Edge(texture.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(texture.height)) return Fail("AtlasSourceExtentUnsupported", "Atlas source texture extent is unsupported.", out diagnostic);
            }
            diagnostic = null;
            return true;
        }

        /// <summary>Validates an Atlas material's base-texture transform against its resolved Document UVSET.</summary>
        public static bool TryValidateMainTextureTransform(Material material, MaterialShaderAdapter adapter, out StackMachineDiagnostic diagnostic)
            => TryValidateMainTextureTransform(material, adapter, Vector2.one, Vector2.zero, out diagnostic);

        /// <summary>Validates an Atlas material's base-texture transform against an explicit UVSET value.</summary>
        /// <remarks>The Baker supplies the UVSET that was applied during compiler flow step 4. A source transform not represented by that payload remains unsupported.</remarks>
        public static bool TryValidateMainTextureTransform(Material material, MaterialShaderAdapter adapter, Vector2 expectedScale, Vector2 expectedOffset, out StackMachineDiagnostic diagnostic)
        {
            if (material == null) return Fail("AtlasMaterialRequired", "Atlas validation requires a material.", out diagnostic);
            if (adapter == null) return Fail("AtlasAdapterRequired", "Atlas validation requires a material shader adapter.", out diagnostic);
            if (!adapter.TryGetAtlasBaseColorTransform(material, out string propertyName, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic adapterDiagnostic)) return Fail("AtlasAdapterInvalid", adapterDiagnostic.message, out diagnostic);
            if (!IsAtlasUvTransformInUnitRange(expectedScale, expectedOffset)) return Fail("AtlasMainTextureTilingUnsupported", "Atlas Document UVSET must remain within [0,1] without tiling or inversion.", out diagnostic);
            if (scale != expectedScale || offset != expectedOffset) return Fail("AtlasMainTextureTransformUnsupported", "Atlas material transform does not match the resolved Document UVSET.", out diagnostic);
            diagnostic = null;
            return true;
        }

        // Unity serializes Vector2 as single precision.  Permit only rounding noise at the upper
        // cell boundary; negative offsets and actual tiling/inversion remain rejected.
        private const float UnitRangeEpsilon = 0.000001f;

        private static bool IsAtlasUvTransformInUnitRange(Vector2 scale, Vector2 offset)
            => float.IsFinite(scale.x) && float.IsFinite(scale.y) && float.IsFinite(offset.x) && float.IsFinite(offset.y)
                && scale.x > 0f && scale.x <= 1f && scale.y > 0f && scale.y <= 1f
                && offset.x >= 0f && offset.y >= 0f && offset.x + scale.x <= 1f + UnitRangeEpsilon && offset.y + scale.y <= 1f + UnitRangeEpsilon;

        /// <summary>Validates adapter-declared non-owned UV0 texture usage for one Atlas material.</summary>
        public static bool TryValidateAdapter(Material material, MaterialShaderAdapter adapter, out StackMachineDiagnostic diagnostic)
        {
            if (adapter == null) return Fail("AtlasAdapterRequired", "Atlas validation requires a material shader adapter.", out diagnostic);
            if (!adapter.TryGetEffectiveNonOwnedUv0Texture(material, out string propertyName, out MaterialProxyDiagnostic adapterDiagnostic))
            {
                if (!string.IsNullOrEmpty(adapterDiagnostic.message)) return Fail("AtlasAdapterInvalid", adapterDiagnostic.message, out diagnostic);
                diagnostic = null;
                return true;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasNonOwnedUv0Texture", "Atlas material has an effective non-owned UV0 texture.", detail: "property=" + propertyName);
            return false;
        }

        private static bool TryValidateTargetMaterial(Target target, bool requireComplete, out StackMachineDiagnostic diagnostic)
        {
            // The legacy mesh-only Target constructor remains valid for callers that have not
            // resolved material payloads yet. Once any material validation input is supplied,
            // require the complete entry contract atomically.
            bool hasMaterialInput = target.Material != null || target.Adapter != null || target.BaseColor != null || target.Normal != null;
            if (!hasMaterialInput && !requireComplete) { diagnostic = null; return true; }
            if (!TryValidateSemantics(target.BaseColor, target.Normal, target.Material, target.Adapter, target.NormalTexturePropertyName, out diagnostic)) return false;
            if (!TryValidateMainTextureTransform(target.Material, target.Adapter, target.UvScale, target.UvOffset, out diagnostic)) return false;
            return TryValidateAdapter(target.Material, target.Adapter, out diagnostic);
        }

        private static bool FailForTarget(Target target, string code, string message, string cause, out StackMachineDiagnostic diagnostic)
        {
            string detail = "owner=" + target.Owner + ";materialId=" + target.MaterialId + ";submesh=" + target.Submesh + ";pageIndex=" + target.PageIndex + ";cause=" + (cause ?? string.Empty);
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message, detail: detail);
            return false;
        }

        private static bool Fail(string code, string message, out StackMachineDiagnostic diagnostic, string detail = null) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", code, message, detail: detail); return false; }
    }
}
