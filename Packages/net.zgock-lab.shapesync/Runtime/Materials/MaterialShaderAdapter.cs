// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.Materials
{
    /// <summary>Semantic values that a Material Proxy can apply through an adapter.</summary>
    public enum MaterialProxySemantic
    {
        /// <summary>The main or base-color texture.</summary>
        BaseColorTexture,
        /// <summary>The normal-map texture.</summary>
        NormalTexture,
        /// <summary>The primary material color.</summary>
        Color,
        /// <summary>The scale and offset of the base-color texture coordinates.</summary>
        UvTransform
    }

    /// <summary>Specifies the public Material API operation used by a property binding.</summary>
    public enum MaterialPropertyWriteKind
    {
        /// <summary>Sets a texture property.</summary>
        Texture,
        /// <summary>Sets a color property.</summary>
        Color,
        /// <summary>Sets a texture scale.</summary>
        TextureScale,
        /// <summary>Sets a texture offset.</summary>
        TextureOffset
    }

    /// <summary>Identifies the semantic value that supplies or receives a bound Material property.</summary>
    public enum MaterialPropertyValueSource
    {
        /// <summary>The base-color texture value.</summary>
        BaseColorTexture,
        /// <summary>The normal texture value.</summary>
        NormalTexture,
        /// <summary>The primary color value.</summary>
        Color,
        /// <summary>The base-color texture scale.</summary>
        UvScale,
        /// <summary>The base-color texture offset.</summary>
        UvOffset
    }

    [Serializable]
    /// <summary>Serialized explicit mapping from a Material Proxy semantic value to a shader property.</summary>
    public struct MaterialPropertyBindingTemplate
    {
        /// <summary>Gets or sets the explicit shader property name.</summary>
        public string propertyName;
        /// <summary>Gets or sets the Material API operation for the property.</summary>
        public MaterialPropertyWriteKind writeKind;
        /// <summary>Gets or sets the semantic value supplying or receiving the property.</summary>
        public MaterialPropertyValueSource valueSource;
        /// <summary>Gets or sets whether the property must be present for the adapter contract.</summary>
        public bool required;
    }

    /// <summary>Compiled atomic Material write operation produced by an adapter.</summary>
    public struct MaterialPropertyAssignment
    {
        /// <summary>Gets or sets the shader property identifier.</summary>
        public int PropertyId;
        /// <summary>Gets or sets the Material API operation to perform.</summary>
        public MaterialPropertyWriteKind WriteKind;
        /// <summary>Gets or sets the texture payload for texture assignments.</summary>
        public Texture Texture;
        /// <summary>Gets or sets the color payload for color assignments.</summary>
        public Color Color;
        /// <summary>Gets or sets the vector payload for texture scale or offset assignments.</summary>
        public Vector2 Vector2;
    }

    /// <summary>Compiled atomic Material read operation produced by an adapter.</summary>
    public struct MaterialPropertyReadCommand
    {
        /// <summary>Gets or sets the shader property identifier.</summary>
        public int PropertyId;
        /// <summary>Gets or sets the Material API operation used to read the property.</summary>
        public MaterialPropertyWriteKind ReadKind;
        /// <summary>Gets or sets the semantic destination for the read value.</summary>
        public MaterialPropertyValueSource Destination;
        /// <summary>Gets or sets whether the property is required by the adapter contract.</summary>
        public bool Required;
    }

    /// <summary>Reports which requested semantics an in-memory adapter write applied or ignored.</summary>
    public readonly struct MaterialShaderAdapterApplyResult
    {
        internal MaterialShaderAdapterApplyResult(MaterialProxySemanticApplication baseColorTexture, MaterialProxySemanticApplication normalTexture, MaterialProxySemanticApplication color, MaterialProxySemanticApplication uvTransform)
        {
            BaseColorTexture = baseColorTexture;
            NormalTexture = normalTexture;
            Color = color;
            UvTransform = uvTransform;
        }

        /// <summary>Gets the BaseColor Texture write outcome.</summary>
        public MaterialProxySemanticApplication BaseColorTexture { get; }
        /// <summary>Gets the Normal Texture write outcome.</summary>
        public MaterialProxySemanticApplication NormalTexture { get; }
        /// <summary>Gets the Color write outcome.</summary>
        public MaterialProxySemanticApplication Color { get; }
        /// <summary>Gets the UV transform write outcome.</summary>
        public MaterialProxySemanticApplication UvTransform { get; }
    }

    [Serializable]
    /// <summary>Semantic values and explicit apply flags for a Material Proxy commit or read operation.</summary>
    public struct MaterialProxySemanticValues
    {
        /// <summary>Gets or sets whether to apply <see cref="baseColorTexture"/>.</summary>
        public bool applyBaseColorTexture;
        /// <summary>Gets or sets the base-color texture.</summary>
        public Texture baseColorTexture;
        /// <summary>Gets or sets whether to apply <see cref="normalTexture"/>.</summary>
        public bool applyNormalTexture;
        /// <summary>Gets or sets the normal-map texture.</summary>
        public Texture normalTexture;
        /// <summary>Gets or sets whether to apply <see cref="color"/>.</summary>
        public bool applyColor;
        /// <summary>Gets or sets the Linear RGBA color.</summary>
        public Color color;
        /// <summary>Gets or sets whether to apply <see cref="uvScale"/> and <see cref="uvOffset"/>.</summary>
        public bool applyUvTransform;
        /// <summary>Gets or sets the base-color texture coordinate scale.</summary>
        public Vector2 uvScale;
        /// <summary>Gets or sets the base-color texture coordinate offset.</summary>
        public Vector2 uvOffset;
    }

    /// <summary>Explicit, shader-specific mapping from Material Proxy semantics to public Material properties.</summary>
    public abstract class MaterialShaderAdapter : ScriptableObject
    {
        [SerializeField] private List<MaterialPropertyBindingTemplate> assignmentTemplates = new List<MaterialPropertyBindingTemplate>();
        private readonly List<MaterialPropertyBindingTemplate> defaultTemplates = new List<MaterialPropertyBindingTemplate>();

        /// <summary>Gets the exact shader name supported by this adapter.</summary>
        public abstract string ExpectedShaderName { get; }
        /// <summary>Gets the serialized explicit property mappings owned by this adapter.</summary>
        public IReadOnlyList<MaterialPropertyBindingTemplate> AssignmentTemplates => assignmentTemplates;

        /// <summary>Finds an effective UV0-sampled texture property not owned by this adapter for Atlas validation.</summary>
        /// <remarks>The base adapter declares none. Shader-specific adapters override this query when such a property is effective for the current material.</remarks>
        public virtual bool TryGetEffectiveNonOwnedUv0Texture(Material material, out string propertyName, out MaterialProxyDiagnostic diagnostic)
        {
            propertyName = null;
            return TryValidateMaterial(material, out diagnostic) && false;
        }

        /// <summary>Determines whether an adapter-mapped texture is semantically neutral for the current Material.</summary>
        /// <remarks>The adapter first evaluates current Material state in its override and uses content inspection only when state does not decide the result. The property name is deliberately generic so future Emission, Occlusion, and MatCap semantics can use the same query.</remarks>
        public virtual bool TryGetEffectiveNeutralTexture(Material material, string propertyName, Texture texture, out bool isNeutral, out MaterialProxyDiagnostic diagnostic)
        {
            isNeutral = false;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Neutral texture query requires an adapter property name.");
                return false;
            }
            if (!material.HasProperty(propertyName))
            {
                diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Neutral texture query property is not present on the material.");
                return false;
            }
            diagnostic = default;
            return true;
        }

        /// <summary>Compares RGB content with an adapter-owned neutral value using the shared quantization tolerance.</summary>
        protected static bool TryMatchNeutralTextureContent(Texture texture, Color expected, out bool isNeutral, out MaterialProxyDiagnostic diagnostic)
        {
            isNeutral = false;
            diagnostic = default;
            if (texture == null) return true;
            if (!TryReadNeutralTexturePixels(texture, out Color[] pixels, out diagnostic)) return false;
            // BC/DXT RGB endpoint quantization can move a 5-bit channel value by about
            // 4/255 at worst; 8/255 leaves bounded headroom for that quantization while
            // still rejecting visibly non-neutral texels.
            const float channelTolerance = 8f / 255f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color actual = pixels[i];
                if (Mathf.Abs(actual.r - expected.r) > channelTolerance
                    || Mathf.Abs(actual.g - expected.g) > channelTolerance
                    || Mathf.Abs(actual.b - expected.b) > channelTolerance)
                    return true;
            }
            isNeutral = true;
            return true;
        }

        private static bool TryReadNeutralTexturePixels(Texture source, out Color[] pixels, out MaterialProxyDiagnostic diagnostic)
        {
            pixels = null;
            diagnostic = default;
            if (source.width <= 0 || source.height <= 0)
                return FailNeutralTextureRead("Texture has no readable extent.", out diagnostic);
            if (source is Texture2D readableTexture && readableTexture.isReadable)
            {
                try
                {
                    pixels = readableTexture.GetPixels();
                    return true;
                }
                catch (Exception exception)
                {
                    return FailNeutralTextureRead("Texture2D.GetPixels failed: " + exception.Message, out diagnostic);
                }
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture staging = null;
            Texture2D readback = null;
            try
            {
                // This is the same GPU-readback fallback required for unreadable source
                // textures by the editor publish path; the 8x8 probe is intentionally small.
                staging = RenderTexture.GetTemporary(source.width, source.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, staging);
                RenderTexture.active = staging;
                readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
                readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                readback.Apply(false, false);
                pixels = readback.GetPixels();
                return true;
            }
            catch (Exception exception)
            {
                return FailNeutralTextureRead("GPU texture readback failed: " + exception.Message, out diagnostic);
            }
            finally
            {
                RenderTexture.active = previous;
                if (staging != null) RenderTexture.ReleaseTemporary(staging);
                if (readback != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(readback);
                    else UnityEngine.Object.DestroyImmediate(readback);
                }
            }
        }

        private static bool FailNeutralTextureRead(string message, out MaterialProxyDiagnostic diagnostic)
        {
            diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.TextureReadbackFailed, message);
            return false;
        }

        /// <summary>Gets the adapter-owned BaseColor texture transform that Atlas must normalize.</summary>
        /// <remarks>Derived adapters override this for shaders whose BaseColor property is not <c>_MainTex</c>.</remarks>
        public virtual bool TryGetAtlasBaseColorTransform(Material material, out string propertyName, out Vector2 scale, out Vector2 offset, out MaterialProxyDiagnostic diagnostic)
        {
            propertyName = "_MainTex";
            scale = Vector2.one;
            offset = Vector2.zero;
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            if (!material.HasProperty(propertyName)) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Atlas BaseColor texture property is not present on the material."); return false; }
            scale = material.GetTextureScale(propertyName);
            offset = material.GetTextureOffset(propertyName);
            return true;
        }

        /// <summary>Initializes default mappings when the adapter is enabled.</summary>
        protected virtual void OnEnable() => EnsureDefaults();

        /// <summary>Determines whether this adapter maps the specified semantic.</summary>
        /// <param name="semantic">The semantic to query.</param>
        /// <returns><see langword="true"/> when the semantic has an explicit mapping.</returns>
        public bool Supports(MaterialProxySemantic semantic)
        {
            EnsureDefaults();
            for (int i = 0; i < assignmentTemplates.Count; i++)
            {
                if (ToSemantic(assignmentTemplates[i].valueSource) == semantic) return true;
            }

            return false;
        }

        /// <summary>Creates a detached in-memory Material clone for the Compiler without touching a renderer, Proxy, or asset.</summary>
        /// <remarks>The source shader must exactly match this adapter. The clone explicitly preserves the shader and all source properties.</remarks>
        public bool TryCreateInMemoryClone(Material source, out Material clone, out MaterialProxyDiagnostic diagnostic)
        {
            clone = null;
            if (!TryValidateMaterial(source, out diagnostic)) return false;
            clone = new Material(source) { name = source.name + " (ShapeSync Humanoid)" };
            clone.shader = source.shader;
            clone.CopyPropertiesFromMaterial(source);
            diagnostic = default;
            return true;
        }

        /// <summary>Applies requested semantics directly to a detached in-memory Material clone.</summary>
        /// <remarks>Unsupported semantics are successful no-ops reported as <see cref="MaterialProxyDiagnosticCode.SemanticUnsupported"/>. Shader mismatch and missing mapped properties reject before mutation.</remarks>
        public bool TryApplyInMemory(Material material, MaterialProxySemanticValues values, out MaterialShaderAdapterApplyResult result, out MaterialProxyDiagnostic diagnostic)
        {
            result = CreateApplicationResult(values);
            if (!TryValidateMaterial(material, out diagnostic)) return false;
            if (!TryValidateValues(values, out diagnostic)) return false;

            var assignments = new List<MaterialPropertyAssignment>(AssignmentTemplates.Count + 2);
            if (!TryBuildWritePlan(values, assignments, out diagnostic)) return false;
            for (int i = 0; i < assignments.Count; i++)
            {
                if (!material.HasProperty(assignments[i].PropertyId))
                {
                    diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Adapter property is not present on the in-memory Material.");
                    return false;
                }
            }

            for (int i = 0; i < assignments.Count; i++) Apply(material, assignments[i]);
            diagnostic = GetUnsupportedSemanticWarning(values);
            return true;
        }

        /// <summary>Builds atomic write assignments for the enabled semantic values.</summary>
        /// <param name="values">The semantic values and apply flags to compile.</param>
        /// <param name="destination">The caller-owned buffer that receives the assignments.</param>
        /// <param name="diagnostic">The failure diagnostic, if compilation fails.</param>
        /// <returns><see langword="true"/> when the plan was built; otherwise, <see langword="false"/>.</returns>
        public bool TryBuildWritePlan(MaterialProxySemanticValues values, List<MaterialPropertyAssignment> destination, out MaterialProxyDiagnostic diagnostic)
        {
            destination.Clear();
            EnsureDefaults();
            for (int i = 0; i < assignmentTemplates.Count; i++)
            {
                MaterialPropertyBindingTemplate template = assignmentTemplates[i];
                if (!ShouldApply(template.valueSource, values)) continue;
                if (string.IsNullOrWhiteSpace(template.propertyName))
                {
                    diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Adapter contains an empty property name.");
                    return false;
                }

                MaterialPropertyAssignment assignment = new MaterialPropertyAssignment
                {
                    PropertyId = Shader.PropertyToID(template.propertyName),
                    WriteKind = template.writeKind
                };
                switch (template.valueSource)
                {
                    case MaterialPropertyValueSource.BaseColorTexture: assignment.Texture = values.baseColorTexture; break;
                    case MaterialPropertyValueSource.NormalTexture: assignment.Texture = values.normalTexture; break;
                    case MaterialPropertyValueSource.Color: assignment.Color = values.color; break;
                    case MaterialPropertyValueSource.UvScale: assignment.Vector2 = values.uvScale; break;
                    case MaterialPropertyValueSource.UvOffset: assignment.Vector2 = values.uvOffset; break;
                }

                destination.Add(assignment);
            }

            return TryAppendComputedAssignments(values, destination, out diagnostic);
        }

        /// <summary>Builds atomic read commands for every explicit adapter mapping.</summary>
        /// <param name="destination">The caller-owned buffer that receives the read commands.</param>
        /// <param name="diagnostic">The failure diagnostic, if compilation fails.</param>
        /// <returns><see langword="true"/> when the plan was built; otherwise, <see langword="false"/>.</returns>
        public bool TryBuildReadPlan(List<MaterialPropertyReadCommand> destination, out MaterialProxyDiagnostic diagnostic)
        {
            destination.Clear();
            EnsureDefaults();
            for (int i = 0; i < assignmentTemplates.Count; i++)
            {
                MaterialPropertyBindingTemplate template = assignmentTemplates[i];
                if (string.IsNullOrWhiteSpace(template.propertyName))
                {
                    diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.RequiredPropertyMissing, "Adapter contains an empty property name.");
                    return false;
                }

                destination.Add(new MaterialPropertyReadCommand
                {
                    PropertyId = Shader.PropertyToID(template.propertyName),
                    ReadKind = template.writeKind,
                    Destination = template.valueSource,
                    Required = template.required
                });
            }

            return TryAppendComputedReadCommands(destination, out diagnostic);
        }

        /// <summary>Lists every shader texture property that receives one semantic when a Compiler result is published.</summary>
        /// <remarks>This includes adapter-specific computed mappings, which are not necessarily represented by serialized assignment templates.</remarks>
        public bool TryGetPublishTextureProperties(MaterialProxySemantic semantic, List<string> destination, out MaterialProxyDiagnostic diagnostic)
        {
            destination.Clear();
            EnsureDefaults();
            for (int i = 0; i < assignmentTemplates.Count; i++)
            {
                MaterialPropertyBindingTemplate template = assignmentTemplates[i];
                if (template.writeKind != MaterialPropertyWriteKind.Texture || ToSemantic(template.valueSource) != semantic || string.IsNullOrWhiteSpace(template.propertyName)) continue;
                if (!destination.Contains(template.propertyName)) destination.Add(template.propertyName);
            }
            return TryAppendPublishTextureProperties(semantic, destination, out diagnostic);
        }

        /// <summary>Reads semantic values from a Material using a previously built explicit read plan.</summary>
        /// <param name="material">The Material to read without modifying it.</param>
        /// <param name="readPlan">The explicit read commands to execute.</param>
        /// <param name="values">The values read from the Material.</param>
        /// <param name="diagnostic">The failure diagnostic, if reading fails.</param>
        /// <returns><see langword="true"/> when values were read; otherwise, <see langword="false"/>.</returns>
        public bool TryReadValues(Material material, List<MaterialPropertyReadCommand> readPlan, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            values = default;
            for (int i = 0; i < readPlan.Count; i++)
            {
                MaterialPropertyReadCommand command = readPlan[i];
                switch (command.Destination)
                {
                    case MaterialPropertyValueSource.BaseColorTexture: values.applyBaseColorTexture = true; values.baseColorTexture = material.GetTexture(command.PropertyId); break;
                    case MaterialPropertyValueSource.NormalTexture: values.applyNormalTexture = true; values.normalTexture = material.GetTexture(command.PropertyId); break;
                    case MaterialPropertyValueSource.Color: values.applyColor = true; values.color = material.GetColor(command.PropertyId); break;
                    case MaterialPropertyValueSource.UvScale: values.applyUvTransform = true; values.uvScale = material.GetTextureScale(command.PropertyId); break;
                    case MaterialPropertyValueSource.UvOffset: values.applyUvTransform = true; values.uvOffset = material.GetTextureOffset(command.PropertyId); break;
                }
            }

            return TryApplyComputedReadValues(material, readPlan, ref values, out diagnostic);
        }

        /// <summary>Appends adapter-specific computed write assignments after the template mappings.</summary>
        /// <param name="values">The source semantic values.</param>
        /// <param name="destination">The caller-owned write-plan buffer to append to.</param>
        /// <param name="diagnostic">The failure diagnostic, if computation fails.</param>
        /// <returns><see langword="true"/> when computed assignments were appended; otherwise, <see langword="false"/>.</returns>
        protected virtual bool TryAppendComputedAssignments(MaterialProxySemanticValues values, List<MaterialPropertyAssignment> destination, out MaterialProxyDiagnostic diagnostic)
        {
            diagnostic = default;
            return true;
        }

        /// <summary>Appends adapter-specific computed read commands after the template mappings.</summary>
        /// <param name="destination">The caller-owned read-plan buffer to append to.</param>
        /// <param name="diagnostic">The failure diagnostic, if computation fails.</param>
        /// <returns><see langword="true"/> when computed read commands were appended; otherwise, <see langword="false"/>.</returns>
        protected virtual bool TryAppendComputedReadCommands(List<MaterialPropertyReadCommand> destination, out MaterialProxyDiagnostic diagnostic)
        {
            diagnostic = default;
            return true;
        }

        /// <summary>Appends adapter-specific texture properties that receive a semantic during Compiler artifact publish.</summary>
        protected virtual bool TryAppendPublishTextureProperties(MaterialProxySemantic semantic, List<string> destination, out MaterialProxyDiagnostic diagnostic)
        {
            diagnostic = default;
            return true;
        }

        /// <summary>Applies adapter-specific computed semantic values after template read commands have executed.</summary>
        /// <param name="material">The Material being read.</param>
        /// <param name="readPlan">The complete explicit read plan.</param>
        /// <param name="values">The semantic values to augment.</param>
        /// <param name="diagnostic">The failure diagnostic, if computation fails.</param>
        /// <returns><see langword="true"/> when computed values were applied; otherwise, <see langword="false"/>.</returns>
        protected virtual bool TryApplyComputedReadValues(Material material, List<MaterialPropertyReadCommand> readPlan, ref MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            diagnostic = default;
            return true;
        }

        /// <summary>Builds the default explicit property mappings for a concrete adapter.</summary>
        /// <param name="destination">The caller-owned buffer that receives the default mappings.</param>
        protected abstract void BuildDefaultTemplates(List<MaterialPropertyBindingTemplate> destination);

        private void EnsureDefaults()
        {
            if (assignmentTemplates.Count > 0) return;
            defaultTemplates.Clear();
            BuildDefaultTemplates(defaultTemplates);
            assignmentTemplates.AddRange(defaultTemplates);
        }

        /// <summary>Validates that a material is compatible with this adapter.</summary>
        /// <remarks>Derived adapters use this before evaluating shader-specific Atlas semantics.</remarks>
        protected bool TryValidateMaterial(Material material, out MaterialProxyDiagnostic diagnostic)
        {
            if (material == null) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SourceMaterialMissing, "In-memory Material source is required."); return false; }
            if (material.shader == null || material.shader.name != ExpectedShaderName) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.ShaderMismatch, "Material shader does not match the assigned adapter."); return false; }
            diagnostic = default;
            return true;
        }

        private MaterialShaderAdapterApplyResult CreateApplicationResult(MaterialProxySemanticValues values)
            => new MaterialShaderAdapterApplyResult(ApplicationFor(values.applyBaseColorTexture, Supports(MaterialProxySemantic.BaseColorTexture)), ApplicationFor(values.applyNormalTexture, Supports(MaterialProxySemantic.NormalTexture)), ApplicationFor(values.applyColor, Supports(MaterialProxySemantic.Color)), ApplicationFor(values.applyUvTransform, Supports(MaterialProxySemantic.UvTransform)));

        private MaterialProxyDiagnostic GetUnsupportedSemanticWarning(MaterialProxySemanticValues values)
        {
            if (values.applyBaseColorTexture && !Supports(MaterialProxySemantic.BaseColorTexture)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the base-color texture semantic; it was ignored.");
            if (values.applyNormalTexture && !Supports(MaterialProxySemantic.NormalTexture)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the normal-texture semantic; it was ignored.");
            if (values.applyColor && !Supports(MaterialProxySemantic.Color)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the color semantic; it was ignored.");
            if (values.applyUvTransform && !Supports(MaterialProxySemantic.UvTransform)) return MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.SemanticUnsupported, "The adapter does not support the UV-transform semantic; it was ignored.");
            return default;
        }

        private static MaterialProxySemanticApplication ApplicationFor(bool requested, bool supported)
        {
            if (!requested) return MaterialProxySemanticApplication.NotRequested;
            return supported ? MaterialProxySemanticApplication.Applied : MaterialProxySemanticApplication.Ignored;
        }

        private static bool TryValidateValues(MaterialProxySemanticValues values, out MaterialProxyDiagnostic diagnostic)
        {
            if (values.applyColor && (!Finite(values.color.r) || !Finite(values.color.g) || !Finite(values.color.b) || !Finite(values.color.a) || values.color.r < 0f || values.color.r > 1f || values.color.g < 0f || values.color.g > 1f || values.color.b < 0f || values.color.b > 1f || values.color.a < 0f || values.color.a > 1f)) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.InvalidColor, "Color must be finite Linear RGBA in [0, 1]."); return false; }
            if (values.applyUvTransform && (!Finite(values.uvScale.x) || !Finite(values.uvScale.y) || !Finite(values.uvOffset.x) || !Finite(values.uvOffset.y))) { diagnostic = MaterialProxyDiagnostic.Fail(MaterialProxyDiagnosticCode.InvalidUvTransform, "UV transform values must be finite."); return false; }
            diagnostic = default;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void Apply(Material material, MaterialPropertyAssignment assignment)
        {
            switch (assignment.WriteKind)
            {
                case MaterialPropertyWriteKind.Texture: material.SetTexture(assignment.PropertyId, assignment.Texture); break;
                case MaterialPropertyWriteKind.Color: material.SetColor(assignment.PropertyId, assignment.Color); break;
                case MaterialPropertyWriteKind.TextureScale: material.SetTextureScale(assignment.PropertyId, assignment.Vector2); break;
                case MaterialPropertyWriteKind.TextureOffset: material.SetTextureOffset(assignment.PropertyId, assignment.Vector2); break;
            }
        }

        private static bool ShouldApply(MaterialPropertyValueSource source, MaterialProxySemanticValues values)
        {
            switch (source)
            {
                case MaterialPropertyValueSource.BaseColorTexture: return values.applyBaseColorTexture;
                case MaterialPropertyValueSource.NormalTexture: return values.applyNormalTexture;
                case MaterialPropertyValueSource.Color: return values.applyColor;
                case MaterialPropertyValueSource.UvScale:
                case MaterialPropertyValueSource.UvOffset: return values.applyUvTransform;
                default: return false;
            }
        }

        private static MaterialProxySemantic ToSemantic(MaterialPropertyValueSource source)
        {
            switch (source)
            {
                case MaterialPropertyValueSource.BaseColorTexture: return MaterialProxySemantic.BaseColorTexture;
                case MaterialPropertyValueSource.NormalTexture: return MaterialProxySemantic.NormalTexture;
                case MaterialPropertyValueSource.Color: return MaterialProxySemantic.Color;
                default: return MaterialProxySemantic.UvTransform;
            }
        }

        /// <summary>Adds a texture mapping and, for a base-color texture, its shared UV scale and offset mappings.</summary>
        /// <param name="destination">The template buffer to append to.</param>
        /// <param name="propertyName">The explicit shader texture property name.</param>
        /// <param name="textureSource">The semantic texture source for the mapping.</param>
        protected static void AddTextureAndTransform(List<MaterialPropertyBindingTemplate> destination, string propertyName, MaterialPropertyValueSource textureSource)
        {
            destination.Add(new MaterialPropertyBindingTemplate { propertyName = propertyName, writeKind = MaterialPropertyWriteKind.Texture, valueSource = textureSource, required = textureSource == MaterialPropertyValueSource.BaseColorTexture });
            if (textureSource == MaterialPropertyValueSource.BaseColorTexture)
            {
                destination.Add(new MaterialPropertyBindingTemplate { propertyName = propertyName, writeKind = MaterialPropertyWriteKind.TextureScale, valueSource = MaterialPropertyValueSource.UvScale, required = true });
                destination.Add(new MaterialPropertyBindingTemplate { propertyName = propertyName, writeKind = MaterialPropertyWriteKind.TextureOffset, valueSource = MaterialPropertyValueSource.UvOffset, required = true });
            }
        }
    }

}
