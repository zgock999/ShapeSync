// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Material Shader Adapter for <c>Universal Render Pipeline/Unlit</c>.</summary>
    public sealed class UrpUnlitMaterialShaderAdapter : MaterialShaderAdapter
    {
        /// <inheritdoc />
        public override string ExpectedShaderName => "Universal Render Pipeline/Unlit";

        /// <inheritdoc />
        /// <remarks>URP Unlit has no additional effective UV0 texture semantic outside the adapter-owned BaseColor mapping.</remarks>
        public override bool TryGetEffectiveNonOwnedUv0Texture(Material material, out string propertyName, out MaterialProxyDiagnostic diagnostic)
        {
            propertyName = null;
            return TryValidateMaterial(material, out diagnostic) && false;
        }

        /// <inheritdoc />
        public override bool TryGetEffectiveNeutralTexture(Material material, string propertyName, Texture texture, out bool isNeutral, out MaterialProxyDiagnostic diagnostic)
        {
            isNeutral = false;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            if (propertyName == "_BumpMap") { isNeutral = true; diagnostic = default; return true; }
            return base.TryGetEffectiveNeutralTexture(material, propertyName, texture, out isNeutral, out diagnostic);
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
        }
    }
}
