// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Material Shader Adapter for <c>Universal Render Pipeline/Lit</c>.</summary>
    public sealed class UrpLitMaterialShaderAdapter : MaterialShaderAdapter
    {
        /// <inheritdoc />
        public override string ExpectedShaderName => "Universal Render Pipeline/Lit";

        /// <inheritdoc />
        /// <remarks>URP Lit detail textures are sampled from UV0 when the detail keyword is effective and are not ShapeSync-owned semantics.</remarks>
        public override bool TryGetEffectiveNonOwnedUv0Texture(Material material, out string propertyName, out MaterialProxyDiagnostic diagnostic)
        {
            propertyName = null;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            if (!material.IsKeywordEnabled("_DETAIL_MULX2") || (material.HasProperty("_DetailUV") && !Mathf.Approximately(material.GetFloat("_DetailUV"), 0f))) return false;
            if (material.HasProperty("_DetailAlbedoMap") && material.GetTexture("_DetailAlbedoMap") != null) { propertyName = "_DetailAlbedoMap"; return true; }
            if (material.HasProperty("_DetailNormalMap") && material.GetTexture("_DetailNormalMap") != null) { propertyName = "_DetailNormalMap"; return true; }
            return false;
        }

        /// <inheritdoc />
        public override bool TryGetEffectiveNeutralTexture(Material material, string propertyName, Texture texture, out bool isNeutral, out MaterialProxyDiagnostic diagnostic)
        {
            isNeutral = false;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            if (propertyName != "_BumpMap") return base.TryGetEffectiveNeutralTexture(material, propertyName, texture, out isNeutral, out diagnostic);
            if (!material.HasProperty(propertyName)) return base.TryGetEffectiveNeutralTexture(material, propertyName, texture, out isNeutral, out diagnostic);
            if (!material.IsKeywordEnabled("_NORMALMAP")) { isNeutral = true; diagnostic = default; return true; }
            return TryMatchNeutralTextureContent(texture, new Color(.5f, .5f, 1f, 1f), out isNeutral, out diagnostic);
        }

        /// <inheritdoc />
        public override bool TryGetAtlasBaseColorTransform(Material material, out string propertyName, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic diagnostic)
        {
            propertyName = "_BaseMap"; scale = Vector2.one; offset = Vector2.zero;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            scale = material.GetTextureScale(propertyName); offset = material.GetTextureOffset(propertyName); return true;
        }

        /// <inheritdoc />
        protected override void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination)
        {
            AddTextureAndTransform(destination, "_BaseMap", MaterialPropertyValueSource.BaseColorTexture);
            destination.Add(new MaterialPropertyBindingTemplate { propertyName = "_BaseColor", writeKind = MaterialPropertyWriteKind.Color, valueSource = MaterialPropertyValueSource.Color, required = true });
            AddTextureAndTransform(destination, "_BumpMap", MaterialPropertyValueSource.NormalTexture);
        }
    }
}
