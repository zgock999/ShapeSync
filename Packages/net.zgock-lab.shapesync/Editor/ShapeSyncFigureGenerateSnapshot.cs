// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Validated output folders for one Figure Generate transaction.</summary>
    internal sealed class ShapeSyncFigureGenerateOutputPaths
    {
        private ShapeSyncFigureGenerateOutputPaths(string rootPath, string registriesPath, string bindingsPath, string materialsPath, string texturesPath)
        { RootPath = rootPath; RegistriesPath = registriesPath; BindingsPath = bindingsPath; MaterialsPath = materialsPath; TexturesPath = texturesPath; }

        internal string RootPath { get; }
        internal string RegistriesPath { get; }
        internal string BindingsPath { get; }
        internal string MaterialsPath { get; }
        internal string TexturesPath { get; }

        internal static bool TryCreate(string rootPath, string registriesPath, string bindingsPath, string materialsPath, string texturesPath, out ShapeSyncFigureGenerateOutputPaths paths, out StackMachineDiagnostic diagnostic)
        {
            paths = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(rootPath) || (rootPath != "Assets" && !rootPath.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(rootPath))
                return Fail("GenerateRootInvalid", "Figure Generate requires an existing output root folder below Assets.", rootPath, out diagnostic);
            if (!TryResolve(rootPath, registriesPath, "registries", out string registries, out diagnostic)
                || !TryResolve(rootPath, bindingsPath, "bindings", out string bindings, out diagnostic)
                || !TryResolve(rootPath, materialsPath, "materials", out string materials, out diagnostic)
                || !TryResolve(rootPath, texturesPath, "textures", out string textures, out diagnostic)) return false;

            var unique = new HashSet<string>(StringComparer.Ordinal) { registries };
            if (!unique.Add(bindings) || !unique.Add(materials) || !unique.Add(textures))
                return Fail("GenerateOutputPathDuplicate", "Figure Generate output paths must be distinct.", null, out diagnostic);
            paths = new ShapeSyncFigureGenerateOutputPaths(rootPath, registries, bindings, materials, textures);
            return true;
        }

        private static bool TryResolve(string rootPath, string relativePath, string subject, out string resolvedPath, out StackMachineDiagnostic diagnostic)
        {
            resolvedPath = null;
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(relativePath)) return Fail("GenerateOutputPathEmpty", "Figure Generate output paths must not be empty.", subject, out diagnostic);
            string normalized = relativePath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("Assets/", StringComparison.Ordinal)
                || normalized.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                || System.IO.Path.IsPathRooted(relativePath)) return Fail("GenerateOutputPathInvalid", "Figure Generate output paths must be relative paths below the selected output root.", subject, out diagnostic);
            resolvedPath = rootPath.TrimEnd('/') + "/" + normalized;
            return true;
        }

        private static bool Fail(string code, string message, string detail, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("figure-generate", code, message, detail: detail);
            return false;
        }
    }

    /// <summary>Immutable, Editor-only input resolved from a ShapeSync Database before Figure Generate begins.</summary>
    /// <remarks>The snapshot retains Database-owned objects only while an active Generate operation consumes it. It is never serialized into a generated Prefab.</remarks>
    internal sealed class ShapeSyncFigureGenerateSnapshot
    {
        internal sealed class Figure
        {
            internal Figure(string name, GameObject figure) { Name = name; GameObject = figure; }
            internal string Name { get; }
            internal GameObject GameObject { get; }
        }

        internal sealed class AxisFigure
        {
            internal AxisFigure(string shapeKey, GameObject figure) { ShapeKey = shapeKey; Figure = figure; }
            internal string ShapeKey { get; }
            internal GameObject Figure { get; }
        }

        internal sealed class Axis
        {
            internal Axis(string name, ShapeSyncDatabaseRegistry.FigureAxisKind kind, bool importAllMaterialsAndTextures, IReadOnlyList<AxisFigure> figures)
            { Name = name; Kind = kind; ImportAllMaterialsAndTextures = importAllMaterialsAndTextures; Figures = figures; }
            internal string Name { get; }
            internal ShapeSyncDatabaseRegistry.FigureAxisKind Kind { get; }
            internal bool ImportAllMaterialsAndTextures { get; }
            internal IReadOnlyList<AxisFigure> Figures { get; }
        }

        internal sealed class Material
        {
            internal Material(string logicalName, SkinnedMeshRenderer renderer, int materialSlot, UnityEngine.Material material, MaterialShaderAdapter adapter, IReadOnlyList<string> textureResourceNames)
            { LogicalName = logicalName; Renderer = renderer; MaterialSlot = materialSlot; MaterialAsset = material; Adapter = adapter; TextureResourceNames = textureResourceNames; }
            internal string LogicalName { get; }
            internal SkinnedMeshRenderer Renderer { get; }
            internal int MaterialSlot { get; }
            internal UnityEngine.Material MaterialAsset { get; }
            internal MaterialShaderAdapter Adapter { get; }
            internal IReadOnlyList<string> TextureResourceNames { get; }
        }

        internal sealed class TextureResource
        {
            internal TextureResource(string logicalName, Texture texture, ShapeSyncDatabaseRegistry.TextureResourceOwner owner) { LogicalName = logicalName; Texture = texture; Owner = owner; }
            internal string LogicalName { get; }
            internal Texture Texture { get; }
            internal ShapeSyncDatabaseRegistry.TextureResourceOwner Owner { get; }
        }

        internal sealed class Normal
        {
            internal Normal(string materialEntryName, string shapeKey, string textureResourceName, Texture texture)
            { MaterialEntryName = materialEntryName; ShapeKey = shapeKey; TextureResourceName = textureResourceName; Texture = texture; }
            internal string MaterialEntryName { get; }
            internal string ShapeKey { get; }
            internal string TextureResourceName { get; }
            internal Texture Texture { get; }
        }

        internal sealed class FigureNormal
        {
            internal FigureNormal(string materialEntryName) { MaterialEntryName = materialEntryName; }
            internal string MaterialEntryName { get; }
        }

        private ShapeSyncFigureGenerateSnapshot(
            string databasePath,
            Figure baseFigure,
            Animator baseAnimator,
            Avatar baseAvatar,
            IReadOnlyList<Axis> axes,
            IReadOnlyList<Material> materialEntries,
            IReadOnlyList<TextureResource> textureResources,
            IReadOnlyList<FigureNormal> figureNormalEntries,
            IReadOnlyList<Normal> normalEntries,
            int pcmSlots,
            IReadOnlyList<string> keptRawBlendShapeNames)
        {
            DatabasePath = databasePath;
            BaseFigure = baseFigure;
            BaseAnimator = baseAnimator;
            BaseAvatar = baseAvatar;
            Axes = axes;
            MaterialEntries = materialEntries;
            TextureResources = textureResources;
            FigureNormalEntries = figureNormalEntries;
            NormalEntries = normalEntries;
            PcmSlots = pcmSlots;
            KeptRawBlendShapeNames = keptRawBlendShapeNames;
        }

        internal string DatabasePath { get; }
        internal Figure BaseFigure { get; }
        internal Animator BaseAnimator { get; }
        internal Avatar BaseAvatar { get; }
        internal IReadOnlyList<Axis> Axes { get; }
        internal IReadOnlyList<Material> MaterialEntries { get; }
        internal IReadOnlyList<TextureResource> TextureResources { get; }
        internal IReadOnlyList<FigureNormal> FigureNormalEntries { get; }
        internal IReadOnlyList<Normal> NormalEntries { get; }
        internal int PcmSlots { get; }
        internal IReadOnlyList<string> KeptRawBlendShapeNames { get; }

        /// <summary>Resolves all structural Figure authoring inputs without changing the Database or source assets.</summary>
        internal static bool TryCreate(ShapeSyncDatabase database, out ShapeSyncFigureGenerateSnapshot snapshot, out StackMachineDiagnostic diagnostic)
        {
            snapshot = null;
            diagnostic = null;
            if (database == null) return Fail("DatabaseRequired", "Figure Generate requires an opened ShapeSync Database.", null, out diagnostic);

            string databasePath = AssetDatabase.GetAssetPath(database);
            if (string.IsNullOrWhiteSpace(databasePath) || !databasePath.StartsWith("Assets/", StringComparison.Ordinal))
                return Fail("DatabasePersistentRequired", "Figure Generate requires a persistent Database Prefab under Assets.", null, out diagnostic);

            ShapeSyncDatabaseRegistry registry = database.Registry;
            if (registry == null || AssetDatabase.GetAssetPath(registry) != databasePath)
                return Fail("DatabaseRegistryInvalid", "Figure Generate requires the Database-owned Registry sub-asset.", null, out diagnostic);
            if (registry.BaseFigures == null || registry.MaterialEntries == null || registry.TextureResources == null
                || registry.FigureNormalEntries == null || registry.NormalEntries == null || registry.FigureAxes == null
                || registry.KeptRawBlendShapeNames == null)
                return Fail("DatabaseRegistryCollectionsInvalid", "Figure Generate requires complete Database Registry collections.", null, out diagnostic);

            if (!TryResolveBaseFigure(database, registry, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out GameObject resolvedBaseFigure, out string registryDiagnostic))
                return Fail("BaseFigureInvalid", "Figure Generate could not resolve the Base Figure.", registryDiagnostic, out diagnostic);
            if (baseFigure == null || resolvedBaseFigure == null)
                return Fail("BaseFigureRequired", "Figure Generate requires exactly one Base Figure.", null, out diagnostic);

            if (!TryValidateFigurePayload(resolvedBaseFigure, baseFigure.Name, out diagnostic)) return false;
            Animator[] animators = resolvedBaseFigure.GetComponentsInChildren<Animator>(true);
            if (animators.Length != 1)
                return Fail("HumanoidAnimatorInvalid", "Figure Generate requires exactly one Humanoid Animator on the Base Figure.", baseFigure.Name, out diagnostic);
            Animator animator = animators[0];
            Avatar avatar = animator == null ? null : animator.avatar;
            if (animator == null || avatar == null || !avatar.isHuman || !avatar.isValid)
                return Fail("HumanoidDefinitionRequired", "Figure Generate requires a valid Humanoid Animator and Avatar on the Base Figure.", baseFigure.Name, out diagnostic);
            if (!string.Equals(AssetDatabase.GetAssetPath(avatar), databasePath, StringComparison.Ordinal))
                return Fail("HumanoidAvatarNotDatabaseOwned", "Figure Generate requires the Base Animator Avatar to be a Database-owned sub-asset.", baseFigure.Name, out diagnostic);
            if (!TryValidateHumanoidBinding(animator, resolvedBaseFigure))
                return Fail("HumanoidBindingInvalid", "Figure Generate requires the Base Animator Avatar to resolve mandatory Humanoid bones on the Base Figure hierarchy.", baseFigure.Name, out diagnostic);

            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes)
            {
                if (axis == null || !ShapeSyncDatabaseRegistry.IsValidUserName(axis.Name) || string.Equals(axis.Name, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal)
                    || BlendShapeReservedPrefixes.IsReserved(axis.Name))
                    return Fail("FigureAxisNameInvalid", "Figure Generate requires valid non-reserved Figure-axis names.", axis == null ? null : axis.Name, out diagnostic);
            }
            if (!registry.TryValidateNormalEntries(out registryDiagnostic))
                return Fail("NormalAuthoringInvalid", "Figure Generate requires valid declared Normal entries.", registryDiagnostic, out diagnostic);

            if (!TryResolveAxes(database, registry, resolvedBaseFigure, out List<Axis> axes, out registryDiagnostic))
                return Fail("FigureAxisInvalid", "Figure Generate requires complete FBM and PBM Figure bindings.", registryDiagnostic, out diagnostic);
            if (!TryValidateFigureMorphAuthoring(registry, resolvedBaseFigure, axes, out registryDiagnostic))
                return Fail("FigureMorphAuthoringInvalid", "Figure Generate requires valid PCM and Extra Morph authoring.", registryDiagnostic, out diagnostic);

            ShapeSyncDatabaseRegistry.MaterialEntry[] materialEntries = registry.MaterialEntries.ToArray();
            ShapeSyncDatabaseRegistry.TextureResourceEntry[] textureResources = registry.TextureResources.ToArray();
            if (materialEntries.Length == 0)
                return Fail("MaterialEntriesRequired", "Figure Generate requires saved Material Entries for the Base Figure.", baseFigure.Name, out diagnostic);
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in textureResources)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.LogicalName) || resource.Texture == null
                    || !string.Equals(AssetDatabase.GetAssetPath(resource.Texture), databasePath, StringComparison.Ordinal))
                    return Fail("TextureResourceInvalid", "Figure Generate requires Database-owned Texture Resources.", resource == null ? null : resource.LogicalName, out diagnostic);
            }
            var resourcesByName = new Dictionary<string, ShapeSyncDatabaseRegistry.TextureResourceEntry>(StringComparer.Ordinal);
            var resourcesByTexture = new Dictionary<Texture, ShapeSyncDatabaseRegistry.TextureResourceEntry>();
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in textureResources)
            {
                if (!resourcesByName.TryAdd(resource.LogicalName, resource))
                    return Fail("TextureResourceDuplicate", "Figure Generate requires unique Texture Resource logical names.", resource.LogicalName, out diagnostic);
                if (!resourcesByTexture.TryAdd(resource.Texture, resource))
                    return Fail("TextureResourceTextureDuplicate", "Figure Generate requires one Texture Resource per Database Texture.", resource.LogicalName, out diagnostic);
            }
            var materialNames = new HashSet<string>(StringComparer.Ordinal);
            var snapshotMaterials = new List<Material>(materialEntries.Length);
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry material in materialEntries)
            {
                if (material == null || material.Material == null || material.Adapter == null)
                    return Fail("MaterialEntryInvalid", "Figure Generate requires complete Material Entry data.", material == null ? null : material.LogicalName, out diagnostic);
                if (!ShapeSyncDatabaseRegistry.IsValidUserName(material.LogicalName) || !materialNames.Add(material.LogicalName)
                    || material.TextureResourceNames == null)
                    return Fail("MaterialEntryInvalid", "Figure Generate requires unique Material Entry names and Texture Resource lists.", material.LogicalName, out diagnostic);
                if (!string.Equals(AssetDatabase.GetAssetPath(material.Material), databasePath, StringComparison.Ordinal)
                    || !string.Equals(AssetDatabase.GetAssetPath(material.Adapter), databasePath, StringComparison.Ordinal))
                    return Fail("MaterialEntryNotDatabaseOwned", "Figure Generate requires Database-owned Material and Adapter assets.", material.LogicalName, out diagnostic);
                Transform rendererTransform = ResolveRelativePath(resolvedBaseFigure.transform, material.BaseRelativeRendererPath);
                SkinnedMeshRenderer renderer = rendererTransform == null ? null : rendererTransform.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null || material.MaterialSlot < 0 || material.MaterialSlot >= renderer.sharedMaterials.Length
                    || renderer.sharedMaterials[material.MaterialSlot] != material.Material)
                    return Fail("MaterialEntryInvalid", "Figure Generate requires a Material Entry renderer channel on the Base Figure.", material.LogicalName, out diagnostic);
                var resourceNames = new List<string>(material.TextureResourceNames.Count);
                var entryResourceNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (string resourceName in material.TextureResourceNames)
                {
                    if (string.IsNullOrWhiteSpace(resourceName) || !entryResourceNames.Add(resourceName) || !resourcesByName.TryGetValue(resourceName, out ShapeSyncDatabaseRegistry.TextureResourceEntry resource)
                        || resource == null || resource.Texture == null)
                        return Fail("MaterialTextureResourceMissing", "Material Entry references a missing Texture Resource.", material.LogicalName + ":" + resourceName, out diagnostic);
                    resourceNames.Add(resourceName);
                }
                Texture[] materialTextures = ShapeSyncEntryAssetNaming.GetTexturesMainTexFirst(material.Material).ToArray();
                if (materialTextures.Any(texture => !string.Equals(AssetDatabase.GetAssetPath(texture), databasePath, StringComparison.Ordinal)))
                    return Fail("MaterialTextureNotDatabaseOwned", "Figure Generate requires Database-owned Material property Textures.", material.LogicalName, out diagnostic);
                // An Entry owns the Base Material property resources first, followed by
                // FBM Import All and explicit Normal resources.  The latter are still
                // MaterialBinding inputs, but are not properties of the Base Material.
                if (resourceNames.Count < materialTextures.Length)
                    return Fail("MaterialTextureResourceMismatch", "Material Entry Texture Resources must include every Base Material property.", material.LogicalName, out diagnostic);
                for (int index = 0; index < materialTextures.Length; index++)
                {
                    if (!resourcesByTexture.TryGetValue(materialTextures[index], out ShapeSyncDatabaseRegistry.TextureResourceEntry expected)
                        || !string.Equals(resourceNames[index], expected.LogicalName, StringComparison.Ordinal))
                        return Fail("MaterialTextureResourceMismatch", "Material Entry Texture Resources must match Material properties in MainTex-first order.", material.LogicalName, out diagnostic);
                }
                snapshotMaterials.Add(new Material(material.LogicalName, renderer, material.MaterialSlot, material.Material, material.Adapter, Array.AsReadOnly(resourceNames.ToArray())));
            }
            var snapshotResources = textureResources.Select(resource => new TextureResource(resource.LogicalName, resource.Texture, resource.Owner)).ToArray();
            var snapshotFigureNormals = registry.FigureNormalEntries.Select(entry => new FigureNormal(entry.MaterialEntryName)).ToArray();
            var snapshotNormals = registry.NormalEntries.Select(normal => new Normal(normal.MaterialEntryName, normal.ShapeKey, normal.TextureResourceName, normal.Texture)).ToArray();
            snapshot = new ShapeSyncFigureGenerateSnapshot(
                databasePath, new Figure(baseFigure.Name, resolvedBaseFigure), animator, avatar, Array.AsReadOnly(axes.ToArray()), Array.AsReadOnly(snapshotMaterials.ToArray()), Array.AsReadOnly(snapshotResources),
                Array.AsReadOnly(snapshotFigureNormals), Array.AsReadOnly(snapshotNormals), registry.PcmSlots, Array.AsReadOnly(registry.KeptRawBlendShapeNames.OrderBy(name => name, StringComparer.Ordinal).ToArray()));
            return true;
        }

        private static bool TryResolveBaseFigure(ShapeSyncDatabase database, ShapeSyncDatabaseRegistry registry,
            out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out GameObject resolvedFigure, out string diagnostic)
        {
            baseFigure = null;
            resolvedFigure = null;
            diagnostic = null;
            if (registry.BaseFigures.Count == 0) return true;
            if (registry.BaseFigures.Count != 1) { diagnostic = "ShapeSync Database contains multiple Base Figures."; return false; }
            baseFigure = registry.BaseFigures[0];
            if (baseFigure == null || string.IsNullOrWhiteSpace(baseFigure.Name)) { diagnostic = "ShapeSync Database Base Figure registry entry is invalid."; return false; }
            Transform resolved = database.transform.Find("Intermediate/" + baseFigure.Name);
            if (resolved == null || resolved.parent != database.transform.Find("Intermediate"))
            { diagnostic = "ShapeSync Database Base Figure registry entry is invalid."; return false; }
            resolvedFigure = resolved.gameObject;
            return true;
        }

        private static bool TryResolveAxes(ShapeSyncDatabase database, ShapeSyncDatabaseRegistry registry, GameObject baseFigure,
            out List<Axis> axes, out string diagnostic)
        {
            axes = new List<Axis>(registry.FigureAxes.Count);
            diagnostic = null;
            var axisNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes)
            {
                if (axis == null || (axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && axis.Kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)
                    || !axisNames.Add(axis.Name))
                { diagnostic = "Figure axis registry entry is invalid."; return false; }
            }
            var fbmNames = new HashSet<string>(registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name), StringComparer.Ordinal);
            var allFigures = new HashSet<GameObject>();
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in registry.FigureAxes)
            {
                if (axis == null || axis.Figures == null || axis.Figures.Count == 0) { diagnostic = "Figure axis registry Figure bindings are missing."; return false; }
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var figures = new List<AxisFigure>(axis.Figures.Count);
                foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry binding in axis.Figures)
                {
                    bool isBase = binding != null && string.Equals(binding.FbmName, ShapeSyncDatabaseRegistry.BaseShapeKey, StringComparison.Ordinal);
                    string expectedName = axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm ? axis.Name
                        : isBase ? baseFigure.name + "_" + axis.Name : binding == null ? null : binding.FbmName + "_" + axis.Name;
                    Transform resolved = string.IsNullOrWhiteSpace(expectedName) ? null : database.transform.Find("Intermediate/" + expectedName);
                    StackMachineDiagnostic payloadDiagnostic = null;
                    bool payloadIsValid = resolved != null && TryValidateFigurePayload(resolved.gameObject, axis.Name + ":" + (binding == null ? null : binding.FbmName), out payloadDiagnostic);
                    if (binding == null || string.IsNullOrWhiteSpace(binding.FbmName) || (!isBase && !fbmNames.Contains(binding.FbmName))
                        || resolved == null || resolved.parent != database.transform.Find("Intermediate") || !keys.Add(binding.FbmName) || !allFigures.Add(resolved.gameObject) || !payloadIsValid)
                    { diagnostic = payloadDiagnostic?.message ?? "Figure axis registry Figure binding is invalid."; return false; }
                    figures.Add(new AxisFigure(binding.FbmName, resolved.gameObject));
                }
                if (axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && (figures.Count != 1 || !keys.SetEquals(new[] { axis.Name })))
                { diagnostic = "Figure axis registry FBM binding is incomplete."; return false; }
                if (axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && !keys.SetEquals(fbmNames.Append(ShapeSyncDatabaseRegistry.BaseShapeKey)))
                { diagnostic = "Figure axis registry PBM bindings are incomplete."; return false; }
                axes.Add(new Axis(axis.Name, axis.Kind, axis.ImportAllMaterialsAndTextures, Array.AsReadOnly(figures.ToArray())));
            }
            return true;
        }

        private static bool TryValidateFigureMorphAuthoring(ShapeSyncDatabaseRegistry registry, GameObject baseFigure, IReadOnlyList<Axis> axes, out string diagnostic)
        {
            diagnostic = null;
            if (registry.PcmSlots < 0) { diagnostic = "PCM Slots must be zero or greater."; return false; }
            var fbmAxes = axes.Where(axis => axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).ToArray();
            if (fbmAxes.Length == 0) return registry.KeptRawBlendShapeNames.Count == 0 || FailMorph("Raw BlendShape keep selection requires a finalized FBM set.", out diagnostic);
            var candidates = GetCommonRawBlendShapeNames(new[] { baseFigure }.Concat(fbmAxes.Select(axis => axis.Figures[0].Figure)));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in registry.KeptRawBlendShapeNames)
                if (string.IsNullOrWhiteSpace(name) || !candidates.Contains(name) || !seen.Add(name))
                    return FailMorph("Persisted raw BlendShape keep selection is invalid.", out diagnostic);
            return true;
        }

        private static HashSet<string> GetCommonRawBlendShapeNames(IEnumerable<GameObject> figures)
        {
            HashSet<string> common = null;
            foreach (GameObject figure in figures)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (SkinnedMeshRenderer renderer in figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (renderer.sharedMesh != null)
                        for (int index = 0; index < renderer.sharedMesh.blendShapeCount; index++)
                        {
                            string name = renderer.sharedMesh.GetBlendShapeName(index);
                            if (!string.IsNullOrWhiteSpace(name) && !BlendShapeReservedPrefixes.IsReserved(name)) names.Add(name);
                        }
                if (common == null) common = names;
                else common.IntersectWith(names);
            }
            return common ?? new HashSet<string>(StringComparer.Ordinal);
        }

        private static Transform ResolveRelativePath(Transform root, string relativePath)
        {
            if (root == null || relativePath == null) return null;
            if (relativePath.Length == 0) return root;
            Transform current = root;
            foreach (string segment in relativePath.Split('/'))
            {
                if (!int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                    || index < 0 || index >= current.childCount) return null;
                current = current.GetChild(index);
            }
            return current;
        }

        private static bool FailMorph(string message, out string diagnostic) { diagnostic = message; return false; }

        private static bool TryValidateFigurePayload(GameObject figure, string subject, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = null;
            if (figure == null)
                return Fail("FigurePayloadInvalid", "Figure Generate requires a Figure payload.", subject, out diagnostic);
            SkinnedMeshRenderer[] renderers = figure.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1)
                return Fail("FigureRendererInvalid", "Figure Generate requires exactly one merged SkinnedMeshRenderer per Figure payload.", subject, out diagnostic);
            SkinnedMeshRenderer renderer = renderers[0];
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0 || renderer.rootBone == null || renderer.bones == null || renderer.bones.Length == 0
                || mesh.bindposes == null || mesh.bindposes.Length != renderer.bones.Length
                || !renderer.rootBone.IsChildOf(figure.transform) || renderer.bones.Any(bone => bone == null || !bone.IsChildOf(figure.transform)))
                return Fail("FigureMeshBindingInvalid", "Figure Generate requires a merged Mesh and Database-local bone bindings.", subject, out diagnostic);
            return true;
        }

        private static bool TryValidateHumanoidBinding(Animator animator, GameObject figure)
        {
            if (animator == null || figure == null) return false;
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            return hips != null && head != null && hips.IsChildOf(figure.transform) && head.IsChildOf(figure.transform);
        }

        private static bool Fail(string code, string message, string detail, out StackMachineDiagnostic diagnostic)
        {
            diagnostic = StackMachineDiagnostic.CreateDomain("figure-generate", code, message, detail: detail);
            return false;
        }
    }
}
#endif
