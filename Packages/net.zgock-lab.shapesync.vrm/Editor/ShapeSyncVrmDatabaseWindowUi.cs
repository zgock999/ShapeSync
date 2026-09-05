// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Editor;

namespace zgock.ShapeSync.VrmIntegration.Editor
{
    /// <summary>
    /// Supplies the optional Figure and Mesh Outfit VRM Details to the Core
    /// Database window. Drafts stay in this integration so Core remains
    /// independent of UniVRM and the VRM registry schema.
    /// </summary>
    [InitializeOnLoad]
    internal static class ShapeSyncVrmDatabaseWindowUi
    {
        private const string VrmLabel = "VRM";
        private const string BaseLabel = "Base";
        private static readonly Dictionary<ShapeSyncDatabaseWindow, WindowState> States =
            new Dictionary<ShapeSyncDatabaseWindow, WindowState>();

        static ShapeSyncVrmDatabaseWindowUi()
        {
            ShapeSyncDatabaseOptionalUiProvider.RegisterVrmUi(
                () => true,
                NotifyDatabaseChanged,
                DrawFigureDetail,
                IsFigureDetailDirty,
                SaveFigureDetail,
                IgnoreFigureDetail,
                DrawMeshOutfitDetail,
                IsMeshOutfitDetailDirty,
                SaveMeshOutfitDetail,
                IgnoreMeshOutfitDetail);
            ShapeSyncDatabaseOptionalUiProvider.RegisterVrmGenerationUi(
                DrawGenerationVrmPath,
                IsGenerationVrmPathDirty,
                ValidateGenerationVrmPath,
                SaveGenerationVrmPath,
                IgnoreGenerationVrmPath);
        }

        private static WindowState GetState(ShapeSyncDatabaseWindow window)
        {
            if (window == null) return null;
            if (!States.TryGetValue(window, out WindowState state))
            {
                state = new WindowState();
                States.Add(window, state);
                state.Hydrate(window);
            }
            return state;
        }

        private static void NotifyDatabaseChanged(ShapeSyncDatabaseWindow window)
        {
            if (window == null) return;
            WindowState state = new WindowState();
            States[window] = state;
            state.Hydrate(window);
        }

        private static void DrawFigureDetail(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            if (state == null || window.Database == null)
            {
                EditorGUILayout.HelpBox("Select or create a ShapeSync Database.", MessageType.Info);
                return;
            }
            state.EnsureHydrated(window);
            GUILayout.Label("VRM", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(state.FigureScroll, GUILayout.ExpandHeight(true)))
            {
                GUILayout.Label("Expression Reference VRM", EditorStyles.boldLabel);
                foreach (ExpressionDraft row in state.ExpressionRows)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(row.ShapeKey == BaseLabel ? state.FigureName : row.ShapeKey, EditorStyles.boldLabel);
                        row.InputPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab input", row.InputPrefab, typeof(GameObject), false);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.ObjectField("Prefab in Database", row.DatabasePrefab, typeof(GameObject), false);
                            using (new EditorGUI.DisabledScope(!row.CanClear))
                                if (GUILayout.Button("Remove", GUILayout.Width(70f))) row.Clear();
                        }
                    }
                }

                GUILayout.Label("Physics Reference VRM", EditorStyles.boldLabel);
                state.FigurePhysics.InputPrefab = EditorGUILayout.ObjectField("Prefab input", state.FigurePhysics.InputPrefab, typeof(GameObject), false) as GameObject;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField("Prefab in Database", state.FigurePhysics.DatabasePrefab, typeof(GameObject), false);
                    using (new EditorGUI.DisabledScope(!state.FigurePhysics.CanClear))
                        if (GUILayout.Button("Remove", GUILayout.Width(70f))) state.FigurePhysics.Clear();
                }
                state.FigureScroll = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!state.IsFigureDetailDirty))
                if (GUILayout.Button("Save to Database", GUILayout.ExpandWidth(true), GUILayout.Height(ShapeSyncDatabaseWindow.DetailSaveButtonHeight)))
                    state.LastDiagnostic = SaveFigureDetail(window);
            DrawDiagnostic(state);
        }

        private static void DrawMeshOutfitDetail(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            WindowState state = GetState(window);
            if (state == null || window.Database == null)
            {
                EditorGUILayout.HelpBox("Select or create a ShapeSync Database.", MessageType.Info);
                return;
            }
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = window.Database.Registry.Outfits
                .FirstOrDefault(entry => entry != null && string.Equals(entry.Identity, outfitIdentity, StringComparison.Ordinal));
            if (outfit == null || outfit.Kind != ShapeSyncDatabaseRegistry.OutfitKind.Mesh)
            {
                EditorGUILayout.HelpBox("Select a Mesh Outfit.", MessageType.Info);
                return;
            }
            state.EnsureHydrated(window);
            OutfitDraft draft = state.GetOutfitDraft(outfitIdentity);
            GUILayout.Label("VRM", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Physics Reference VRM", EditorStyles.boldLabel);
                draft.InputPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab input", draft.InputPrefab, typeof(GameObject), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField("Prefab in Database", draft.DatabasePrefab, typeof(GameObject), false);
                    using (new EditorGUI.DisabledScope(!draft.CanClear))
                        if (GUILayout.Button("Remove", GUILayout.Width(70f))) draft.Clear();
                }
            }
            using (new EditorGUI.DisabledScope(!draft.IsDirty))
                if (GUILayout.Button("Save to Database", GUILayout.ExpandWidth(true), GUILayout.Height(ShapeSyncDatabaseWindow.DetailSaveButtonHeight)))
                    draft.LastDiagnostic = SaveMeshOutfitDetail(window, outfitIdentity);
            DrawDiagnostic(draft.LastDiagnostic);
        }

        private static bool IsFigureDetailDirty(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            return state != null && state.IsFigureDetailDirty;
        }

        private static string SaveFigureDetail(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            if (state == null) return "VRM Detail requires an opened Database.";
            return state.SaveFigure(window);
        }

        private static void IgnoreFigureDetail(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.IgnoreFigure();
        }

        private static bool IsMeshOutfitDetailDirty(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            WindowState state = GetState(window);
            return state != null && state.GetOutfitDraft(outfitIdentity).IsDirty;
        }

        private static string SaveMeshOutfitDetail(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            WindowState state = GetState(window);
            return state == null ? "VRM Detail requires an opened Database." : state.SaveOutfit(window, outfitIdentity);
        }

        private static void IgnoreMeshOutfitDetail(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            WindowState state = GetState(window);
            state?.GetOutfitDraft(outfitIdentity).Ignore();
        }

        private static void DrawGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            if (state == null || window.Database == null) return;
            state.EnsureHydrated(window);
            state.GenerationVrmPath = EditorGUILayout.TextField("VRM path", state.GenerationVrmPath);
        }

        private static bool IsGenerationVrmPathDirty(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            return state != null && state.IsGenerationVrmPathDirty;
        }

        private static string ValidateGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            return state == null ? null : state.ValidateGenerationVrmPath(window);
        }

        private static string SaveGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            return state == null ? "VRM Generation Detail requires an opened Database." : state.SaveGenerationVrmPath(window);
        }

        private static void IgnoreGenerationVrmPath(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.IgnoreGenerationVrmPath();
        }

        private static void DrawDiagnostic(WindowState state)
        {
            if (state != null) DrawDiagnostic(state.LastDiagnostic);
        }

        private static void DrawDiagnostic(string value)
        {
            if (!string.IsNullOrEmpty(value)) EditorGUILayout.HelpBox(value, MessageType.Error);
        }

        internal static string[] FigureExpressionShapeKeysForTest(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            return state?.ExpressionRows.Select(row => row.ShapeKey).ToArray() ?? Array.Empty<string>();
        }

        internal static bool SetFigureExpressionInputForTest(ShapeSyncDatabaseWindow window, string shapeKey, GameObject prefab)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            ExpressionDraft row = state?.ExpressionRows.FirstOrDefault(value => string.Equals(value.ShapeKey, shapeKey, StringComparison.Ordinal));
            if (row == null) return false;
            row.InputPrefab = prefab;
            return true;
        }

        internal static bool SetFigurePhysicsInputForTest(ShapeSyncDatabaseWindow window, GameObject prefab)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            if (state == null) return false;
            state.FigurePhysics.InputPrefab = prefab;
            return true;
        }

        internal static bool ClearFigureExpressionForTest(ShapeSyncDatabaseWindow window, string shapeKey)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            ExpressionDraft row = state?.ExpressionRows.FirstOrDefault(value =>
                string.Equals(value.ShapeKey, shapeKey, StringComparison.Ordinal));
            if (row == null || !row.CanClear) return false;
            row.Clear();
            return true;
        }

        internal static bool ClearFigurePhysicsForTest(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            if (state == null || !state.FigurePhysics.CanClear) return false;
            state.FigurePhysics.Clear();
            return true;
        }

        internal static bool IsFigureDetailDirtyForTest(ShapeSyncDatabaseWindow window)
            => IsFigureDetailDirty(window);

        internal static string SaveFigureDetailForTest(ShapeSyncDatabaseWindow window)
            => SaveFigureDetail(window);

        internal static void IgnoreFigureDetailForTest(ShapeSyncDatabaseWindow window)
            => IgnoreFigureDetail(window);

        internal static GameObject FigureExpressionDatabasePrefabForTest(ShapeSyncDatabaseWindow window, string shapeKey)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            return state?.ExpressionRows.FirstOrDefault(value => string.Equals(value.ShapeKey, shapeKey, StringComparison.Ordinal))?.DatabasePrefab;
        }

        internal static GameObject FigurePhysicsDatabasePrefabForTest(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            return state?.FigurePhysics.DatabasePrefab;
        }

        internal static bool SetMeshOutfitVrmInputForTest(ShapeSyncDatabaseWindow window, string outfitIdentity, GameObject prefab)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            if (state == null) return false;
            state.GetOutfitDraft(outfitIdentity).InputPrefab = prefab;
            return true;
        }

        internal static bool ClearMeshOutfitVrmForTest(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            OutfitDraft draft = state?.GetOutfitDraft(outfitIdentity);
            if (draft == null || !draft.CanClear) return false;
            draft.Clear();
            return true;
        }

        internal static bool IsMeshOutfitVrmDetailDirtyForTest(ShapeSyncDatabaseWindow window, string outfitIdentity)
            => IsMeshOutfitDetailDirty(window, outfitIdentity);

        internal static string SaveMeshOutfitVrmDetailForTest(ShapeSyncDatabaseWindow window, string outfitIdentity)
            => SaveMeshOutfitDetail(window, outfitIdentity);

        internal static void IgnoreMeshOutfitVrmDetailForTest(ShapeSyncDatabaseWindow window, string outfitIdentity)
            => IgnoreMeshOutfitDetail(window, outfitIdentity);

        internal static GameObject MeshOutfitVrmDatabasePrefabForTest(ShapeSyncDatabaseWindow window, string outfitIdentity)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            return state?.GetOutfitDraft(outfitIdentity).DatabasePrefab;
        }

        internal static string GenerationVrmPathForTest(ShapeSyncDatabaseWindow window)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            return state?.GenerationVrmPath;
        }

        internal static bool SetGenerationVrmPathForTest(ShapeSyncDatabaseWindow window, string path)
        {
            WindowState state = GetState(window);
            state?.EnsureHydrated(window);
            if (state == null) return false;
            state.GenerationVrmPath = path;
            return true;
        }

        internal static bool IsGenerationVrmPathDirtyForTest(ShapeSyncDatabaseWindow window)
            => IsGenerationVrmPathDirty(window);

        internal static void ForgetGenerationStateForTest(ShapeSyncDatabaseWindow window)
        {
            if (window == null) return;
            if (States.TryGetValue(window, out WindowState state))
            {
                state.IgnoreGenerationVrmPath();
            }
        }

        internal static void ForgetStateForTest(ShapeSyncDatabaseWindow window)
        {
            if (window != null) States.Remove(window);
        }

        private sealed class WindowState
        {
            private ShapeSyncDatabase hydratedDatabase;
            private string figureName;
            private bool hydrated;
            private readonly List<ExpressionDraft> expressionRows = new List<ExpressionDraft>();
            private FigurePhysicsDraft figurePhysics = new FigurePhysicsDraft();
            private readonly Dictionary<string, OutfitDraft> outfitDrafts = new Dictionary<string, OutfitDraft>(StringComparer.Ordinal);
            private string generationVrmPath = ShapeSyncVrmDatabaseRegistry.DefaultGenerationVrmPath;
            private string acceptedGenerationVrmPath = ShapeSyncVrmDatabaseRegistry.DefaultGenerationVrmPath;

            internal Vector2 FigureScroll;
            internal string LastDiagnostic;
            internal string FigureName => figureName;
            internal IReadOnlyList<ExpressionDraft> ExpressionRows => expressionRows;
            internal FigurePhysicsDraft FigurePhysics => figurePhysics;
            internal bool IsFigureDetailDirty => expressionRows.Any(row => row.IsDirty) || figurePhysics.IsDirty;
            internal string GenerationVrmPath { get => generationVrmPath; set => generationVrmPath = value; }
            internal bool IsGenerationVrmPathDirty => !string.Equals(generationVrmPath, acceptedGenerationVrmPath, StringComparison.Ordinal);

            internal void EnsureHydrated(ShapeSyncDatabaseWindow window)
            {
                if (hydratedDatabase != window.Database || !hydrated) Hydrate(window);
            }

            internal void Hydrate(ShapeSyncDatabaseWindow window)
            {
                hydratedDatabase = window?.Database;
                hydrated = true;
                LastDiagnostic = null;
                expressionRows.Clear();
                figurePhysics = new FigurePhysicsDraft();
                outfitDrafts.Clear();
                generationVrmPath = ShapeSyncVrmDatabaseRegistry.DefaultGenerationVrmPath;
                acceptedGenerationVrmPath = ShapeSyncVrmDatabaseRegistry.DefaultGenerationVrmPath;
                if (window?.Database?.Registry == null) return;

                ShapeSyncVrmDatabaseRegistry registry = TryGetRegistry(window);
                if (registry != null)
                {
                    generationVrmPath = registry.GenerationVrmPath;
                    acceptedGenerationVrmPath = generationVrmPath;
                }

                ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure = window.Database.Registry.BaseFigures.FirstOrDefault(entry => entry != null);
                figureName = baseFigure?.Name;
                if (string.IsNullOrWhiteSpace(figureName)) return;

                var shapeKeys = new List<string> { ShapeSyncDatabaseRegistry.BaseShapeKey };
                shapeKeys.AddRange(window.Database.Registry.FigureAxes
                    .Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                    .Select(axis => axis.Name));
                foreach (string shapeKey in shapeKeys.Distinct(StringComparer.Ordinal))
                {
                    ShapeSyncVrmDatabaseRegistry.FigureExpressionReference reference = registry?.FigureExpressionReferences
                        .FirstOrDefault(value => value != null && value.FigureName == figureName && value.ShapeKey == shapeKey);
                    expressionRows.Add(new ExpressionDraft(shapeKey, reference?.ReferencePrefab));
                }
                ShapeSyncVrmDatabaseRegistry.FigurePhysicsReference physics = registry?.FigurePhysicsReferences
                    .FirstOrDefault(value => value != null && value.FigureName == figureName);
                figurePhysics = new FigurePhysicsDraft(physics?.ReferencePrefab);
            }

            internal OutfitDraft GetOutfitDraft(string identity)
            {
                identity ??= string.Empty;
                if (!outfitDrafts.TryGetValue(identity, out OutfitDraft draft))
                {
                    draft = new OutfitDraft(identity);
                    outfitDrafts.Add(identity, draft);
                    ShapeSyncVrmDatabaseRegistry registry = TryGetRegistry(hydratedDatabase);
                    ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference reference = registry?.MeshOutfitPhysicsReferences
                        .FirstOrDefault(value => value != null && value.OutfitIdentity == identity);
                    draft.SetAccepted(reference?.ReferencePrefab);
                }
                return draft;
            }

            internal string SaveFigure(ShapeSyncDatabaseWindow window)
            {
                if (window?.Database == null) return "VRM Detail requires an opened Database.";
                if (string.IsNullOrWhiteSpace(figureName)) return "VRM Expression Reference requires a registered Base Figure.";
                string expressionCompletenessDiagnostic = ValidateExpressionCompleteness();
                if (!string.IsNullOrEmpty(expressionCompletenessDiagnostic)) return expressionCompletenessDiagnostic;
                string databasePath = AssetDatabase.GetAssetPath(window.Database);
                foreach (ExpressionDraft row in expressionRows.Where(value => value.IsDirty))
                {
                    if (row.InputPrefab == null)
                    {
                        if (!ShapeSyncVrmReferenceImporter.TryRemoveFigureExpressionReference(databasePath, figureName,
                            row.ShapeKey, out string removeDiagnostic)) return removeDiagnostic;
                        continue;
                    }
                    if (!ShapeSyncVrmReferenceImporter.TryImportExpressionReference(databasePath, figureName, row.ShapeKey,
                        row.InputPrefab, out string diagnostic)) return diagnostic;
                }
                if (figurePhysics.IsDirty)
                {
                    if (figurePhysics.InputPrefab == null)
                    {
                        if (!ShapeSyncVrmReferenceImporter.TryRemoveFigurePhysicsReference(databasePath, figureName,
                            out string removeDiagnostic)) return removeDiagnostic;
                    }
                    else if (!ShapeSyncVrmReferenceImporter.TryImportFigurePhysicsReference(databasePath, figureName,
                        figurePhysics.InputPrefab, out string diagnostic)) return diagnostic;
                }
                Hydrate(window);
                return null;
            }

            private string ValidateExpressionCompleteness()
            {
                bool hasAnyReference = expressionRows.Any(value => value.InputPrefab != null);
                if (!hasAnyReference || expressionRows.All(value => value.InputPrefab != null)) return null;
                string missing = string.Join(", ", expressionRows
                    .Where(value => value.InputPrefab == null)
                    .Select(value => value.ShapeKey)
                    .ToArray());
                return "VRM Expression References require Base and all registered FBM references, or no Expression references. Missing: "
                    + missing + ".";
            }

            internal string SaveOutfit(ShapeSyncDatabaseWindow window, string identity)
            {
                OutfitDraft draft = GetOutfitDraft(identity);
                if (!draft.IsDirty) return null;
                string databasePath = AssetDatabase.GetAssetPath(window.Database);
                if (draft.InputPrefab == null)
                {
                    if (!ShapeSyncVrmReferenceImporter.TryRemoveMeshOutfitPhysicsReference(databasePath, identity,
                        out string removeDiagnostic)) return removeDiagnostic;
                }
                else if (!ShapeSyncVrmReferenceImporter.TryImportMeshOutfitPhysicsReference(databasePath, identity,
                    draft.InputPrefab, out string diagnostic)) return diagnostic;
                ShapeSyncVrmDatabaseRegistry registry = TryGetRegistry(window);
                ShapeSyncVrmDatabaseRegistry.MeshOutfitPhysicsReference reference = registry?.MeshOutfitPhysicsReferences
                    .FirstOrDefault(value => value != null && value.OutfitIdentity == identity);
                draft.SetAccepted(reference?.ReferencePrefab);
                draft.LastDiagnostic = null;
                return null;
            }

            internal string SaveGenerationVrmPath(ShapeSyncDatabaseWindow window)
            {
                string validationDiagnostic = ValidateGenerationVrmPath(window);
                if (!string.IsNullOrEmpty(validationDiagnostic)) return validationDiagnostic;
                if (!IsGenerationVrmPathDirty) return null;

                string databasePath = AssetDatabase.GetAssetPath(window?.Database);
                if (string.IsNullOrWhiteSpace(databasePath)) return "VRM Generation Detail requires a persistent Database Prefab.";
                bool saved = ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databasePath, (database, _, transaction) =>
                {
                    ShapeSyncVrmDatabaseRegistry registry = ShapeSyncVrmDatabaseRegistryRegistration.EnsureRegistry(
                        database, databasePath, transaction, out string registryDiagnostic);
                    if (registry == null) throw new InvalidOperationException(registryDiagnostic);
                    if (!registry.TrySetGenerationVrmPath(generationVrmPath, out string pathDiagnostic))
                        throw new InvalidOperationException(pathDiagnostic);
                }, out string diagnostic);
                if (!saved) return diagnostic;

                Hydrate(window);
                return null;
            }

            internal string ValidateGenerationVrmPath(ShapeSyncDatabaseWindow window)
            {
                if (!IsGenerationVrmPathDirty) return null;
                if (!ShapeSyncVrmDatabaseRegistry.TryValidateGenerationVrmPath(generationVrmPath, out string validationDiagnostic))
                    return validationDiagnostic;
                if (ConflictsWithCoreGenerationPath(window?.Database?.Registry?.GenerationPaths, generationVrmPath,
                    out string duplicateDiagnostic))
                    return duplicateDiagnostic;
                return null;
            }

            private static bool ConflictsWithCoreGenerationPath(
                ShapeSyncDatabaseRegistry.GenerationPathSettings corePaths, string candidate, out string diagnostic)
            {
                diagnostic = null;
                if (corePaths == null || string.IsNullOrWhiteSpace(candidate)) return false;
                string normalizedCandidate = candidate.Replace('\\', '/').Trim('/');
                string[] coreValues =
                {
                    corePaths.RegistriesPath,
                    corePaths.BindingsPath,
                    corePaths.MaterialsPath,
                    corePaths.TexturesPath,
                    corePaths.OutfitsPath
                };
                for (int index = 0; index < coreValues.Length; index++)
                {
                    string normalizedCore = (coreValues[index] ?? string.Empty).Replace('\\', '/').Trim('/');
                    if (!string.Equals(normalizedCandidate, normalizedCore, StringComparison.Ordinal)) continue;
                    diagnostic = "VrmGenerationPathDuplicate: VRM output path must be distinct from Core output paths.";
                    return true;
                }
                return false;
            }

            internal void IgnoreGenerationVrmPath()
            {
                generationVrmPath = acceptedGenerationVrmPath;
            }

            internal void IgnoreFigure()
            {
                foreach (ExpressionDraft row in expressionRows) row.Ignore();
                figurePhysics.Ignore();
                LastDiagnostic = null;
            }

            private static ShapeSyncVrmDatabaseRegistry TryGetRegistry(ShapeSyncDatabaseWindow window)
            {
                return window == null ? null : TryGetRegistry(window.Database);
            }

            private static ShapeSyncVrmDatabaseRegistry TryGetRegistry(ShapeSyncDatabase database)
            {
                if (database == null) return null;
                return ShapeSyncVrmDatabaseRegistryRegistration.TryGetRegistry(AssetDatabase.GetAssetPath(database),
                    out ShapeSyncVrmDatabaseRegistry registry, out _) ? registry : null;
            }
        }

        private sealed class ExpressionDraft
        {
            internal readonly string ShapeKey;
            internal GameObject InputPrefab;
            internal GameObject AcceptedPrefab;
            internal GameObject DatabasePrefab;
            internal bool IsDirty => InputPrefab != AcceptedPrefab;
            internal bool CanClear => InputPrefab != null || AcceptedPrefab != null;

            internal ExpressionDraft(string shapeKey, GameObject persisted)
            {
                ShapeKey = shapeKey;
                SetAccepted(persisted);
            }

            internal void SetAccepted(GameObject prefab)
            {
                InputPrefab = AcceptedPrefab = DatabasePrefab = prefab;
            }

            internal void Ignore() => InputPrefab = AcceptedPrefab;
            internal void Clear() => InputPrefab = null;
        }

        private sealed class FigurePhysicsDraft
        {
            internal GameObject InputPrefab;
            internal GameObject AcceptedPrefab;
            internal GameObject DatabasePrefab;
            internal bool IsDirty => InputPrefab != AcceptedPrefab;
            internal bool CanClear => InputPrefab != null || AcceptedPrefab != null;

            internal FigurePhysicsDraft() { }
            internal FigurePhysicsDraft(GameObject persisted) => SetAccepted(persisted);
            internal void SetAccepted(GameObject prefab) => InputPrefab = AcceptedPrefab = DatabasePrefab = prefab;
            internal void Ignore() => InputPrefab = AcceptedPrefab;
            internal void Clear() => InputPrefab = null;
        }

        private sealed class OutfitDraft
        {
            internal readonly string Identity;
            internal GameObject InputPrefab;
            internal GameObject AcceptedPrefab;
            internal GameObject DatabasePrefab;
            internal string LastDiagnostic;
            internal bool IsDirty => InputPrefab != AcceptedPrefab;
            internal bool CanClear => InputPrefab != null || AcceptedPrefab != null;

            internal OutfitDraft(string identity) { Identity = identity; }
            internal void SetAccepted(GameObject prefab) => InputPrefab = AcceptedPrefab = DatabasePrefab = prefab;
            internal void Ignore() { InputPrefab = AcceptedPrefab; LastDiagnostic = null; }
            internal void Clear() { InputPrefab = null; LastDiagnostic = null; }
        }
    }
}
#endif
