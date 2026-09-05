// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Material Shader Adapter for <c>VRM10/Universal Render Pipeline/MToon10</c>.</summary>
    public sealed class MToon10MaterialShaderAdapter : MaterialShaderAdapter
    {
        /// <inheritdoc />
        public override string ExpectedShaderName => "VRM10/Universal Render Pipeline/MToon10";

        /// <inheritdoc />
        /// <remarks><c>_MainTex</c> and the computed <c>_ShadeTex</c> are both adapter-owned BaseColor outputs; this adapter declares no additional effective UV0 texture.</remarks>
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
            if (propertyName != "_BumpMap") return base.TryGetEffectiveNeutralTexture(material, propertyName, texture, out isNeutral, out diagnostic);
            if (!material.HasProperty(propertyName)) return base.TryGetEffectiveNeutralTexture(material, propertyName, texture, out isNeutral, out diagnostic);
            if (!material.IsKeywordEnabled("_NORMALMAP")) { isNeutral = true; diagnostic = default; return true; }
            return TryMatchNeutralTextureContent(texture, new Color(.5f, .5f, 1f, 1f), out isNeutral, out diagnostic);
        }

        /// <inheritdoc />
        public override bool TryGetAtlasBaseColorTransform(Material material, out string propertyName, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic diagnostic)
        {
            propertyName = "_MainTex"; scale = Vector2.one; offset = Vector2.zero;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            scale = material.GetTextureScale(propertyName); offset = material.GetTextureOffset(propertyName); return true;
        }

        /// <inheritdoc />
        protected override void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination)
        {
            AddTextureAndTransform(destination, "_MainTex", MaterialPropertyValueSource.BaseColorTexture);
            destination.Add(new MaterialPropertyBindingTemplate { propertyName = "_Color", writeKind = MaterialPropertyWriteKind.Color, valueSource = MaterialPropertyValueSource.Color, required = true });
            AddTextureAndTransform(destination, "_BumpMap", MaterialPropertyValueSource.NormalTexture);
        }

        /// <inheritdoc />
        /// <remarks>
        /// MToon uses separate Lit (<c>_MainTex</c>) and Shade (<c>_ShadeTex</c>)
        /// textures. ShapeSync owns one BaseColor texture semantic, so one resolved
        /// texture reference is assigned to both shader properties without cloning or
        /// taking the delivery a second time.
        /// </remarks>
        protected override bool TryAppendComputedAssignments(MaterialProxySemanticValues values, List<MaterialPropertyAssignment> destination, out MaterialProxyDiagnostic diagnostic)
        {
            if (values.applyBaseColorTexture)
            {
                destination.Add(new MaterialPropertyAssignment
                {
                    PropertyId = Shader.PropertyToID("_ShadeTex"),
                    WriteKind = MaterialPropertyWriteKind.Texture,
                    Texture = values.baseColorTexture
                });
            }

            diagnostic = default;
            return true;
        }

        /// <inheritdoc />
        protected override bool TryAppendPublishTextureProperties(MaterialProxySemantic semantic, List<string> destination, out MaterialProxyDiagnostic diagnostic)
        {
            if (semantic == MaterialProxySemantic.BaseColorTexture && !destination.Contains("_ShadeTex")) destination.Add("_ShadeTex");
            diagnostic = default;
            return true;
        }
    }
}
