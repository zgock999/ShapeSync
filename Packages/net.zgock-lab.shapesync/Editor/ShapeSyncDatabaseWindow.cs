// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using zgock.ShapeSync.Materials;

#if UNITY_6000_2_OR_NEWER
using ShapeSyncTreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;
using ShapeSyncTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
#else
using ShapeSyncTreeView = UnityEditor.IMGUI.Controls.TreeView;
using ShapeSyncTreeViewState = UnityEditor.IMGUI.Controls.TreeViewState;
using ShapeSyncTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem;
#endif

namespace zgock.ShapeSync.Editor
{
    /// <summary>Provides the authoring-only shell for the ShapeSync Database editor.</summary>
    public sealed class ShapeSyncDatabaseWindow : EditorWindow
    {
        internal delegate bool DatabaseCreator(string folderPath, out ShapeSyncDatabase database, out string diagnostic);
        internal delegate bool DatabaseOpener(string assetPath, out ShapeSyncDatabase database, out string diagnostic);
        internal delegate bool FigureAdmitter(GameObject candidate, out ShapeSyncFigureImportAdmission admission, out string diagnostic);
        internal delegate bool FigureImporter(string databasePath, ShapeSyncFigureImportAdmission admission, string figureName, out string diagnostic);
        internal delegate bool MaterialEntrySaver(string databasePath, IReadOnlyList<ShapeSyncMaterialAdapterResolver.Admission> admissions, out string diagnostic);
        internal delegate bool MaterialEntrySaverWithTextureRename(string databasePath, IReadOnlyList<ShapeSyncMaterialAdapterResolver.Admission> admissions, bool renameTextures, out string diagnostic);
        internal delegate bool MaterialEntryRenamer(string databasePath, IReadOnlyList<ShapeSyncMaterialEntryImport.Rename> renames, bool renameTextures, out string diagnostic);
        internal delegate bool TextureResourceSaver(string databasePath, IReadOnlyList<ShapeSyncTextureResourceAuthoring.Rename> renames, IReadOnlyList<ShapeSyncTextureResourceAuthoring.Addition> additions, IReadOnlyList<ShapeSyncTextureResourceAuthoring.Removal> removals, out string diagnostic);
        internal delegate bool NormalEntrySaver(string databasePath, IReadOnlyList<string> figureNormalEntryMaterialNames, IReadOnlyList<ShapeSyncNormalEntryAuthoring.Assignment> assignments, out string diagnostic);
        internal delegate bool FbmAxisRemover(string databasePath, string axisName, out string diagnostic);
        internal delegate bool FbmAxisReplacer(string databasePath, string currentName, string replacementName, bool importMaterialsAndTextures, ShapeSyncFigureImportAdmission admission, out string diagnostic);
        internal delegate bool FbmAxisRenamer(string databasePath, string currentName, string replacementName, out string diagnostic);
        internal delegate bool PbmAxisReplacer(string databasePath, string currentName, string replacementName, IReadOnlyList<ShapeSyncAxisFigureSource> sources, out string diagnostic);
        internal delegate bool BaseFigureRenamer(string databasePath, string currentName, string replacementName, out string diagnostic);
        internal delegate bool DatabaseFigureExporter(ShapeSyncDatabase database, GameObject figure, string destinationPath, out GameObject exportedPrefab, out string diagnostic);
        internal delegate bool DatabaseOutfitExporter(ShapeSyncDatabase database, GameObject outfit, string destinationPath, out GameObject exportedPrefab, out string diagnostic);
        internal delegate bool FigureGenerator(ShapeSyncDatabase database, string rootPath, string registriesPath, string bindingsPath, string materialsPath, string texturesPath, ICollection<string> generatedPaths, out string diagnostic);
        internal delegate bool OutfitGenerator(ShapeSyncDatabase database, string rootPath, string bindingsPath, string outfitsPath, ICollection<string> generatedPaths, out string diagnostic);
        internal delegate bool ShapeGenerator(ShapeSyncDatabase database, string rootPath, IReadOnlyCollection<string> generatedPaths, out string diagnostic);

        internal const string MenuPath = "Tools/zgock/ShapeSync/ShapeSync Editor";
        internal const string WindowTitle = "ShapeSync Database";
        internal const string FbmAddButtonLabel = "Add FBM Entry";
        internal const string FbmSaveButtonLabel = "Save to Database";
        internal const string FigureExportButtonLabel = "Export";
        internal const string PbmAddButtonLabel = "Add PBM Entry";
        internal const string PbmSaveButtonLabel = "Save to Database";
        internal static string GetNameAfterPrefabAssignment(string currentName, GameObject prefab)
        {
            return string.IsNullOrWhiteSpace(currentName) && prefab != null ? prefab.name : currentName;
        }

        internal readonly struct PbmDetailLayout
        {
            internal int CentralScrollViewCount => 1;
            internal bool AddActionIsAboveCentralScroll => true;
            internal bool SaveActionIsBelowCentralScroll => true;
            internal bool ShowsPbmPrefabsHeadingAfterName => true;
            internal bool HasFigureNamedFirstPrefabRow => true;
            internal bool UsesUnlabeledWidePrefabFields => true;
            internal bool HidesBaseInternalTerm => true;
            internal string AddActionLabel => PbmAddButtonLabel;
            internal string SaveActionLabel => PbmSaveButtonLabel;
        }
        internal static PbmDetailLayout GetPbmDetailLayoutForTest() => new PbmDetailLayout();

        internal readonly struct FbmDetailLayout
        {
            internal FbmDetailLayout(bool showsAddFbmEntry)
            {
                ShowsAddFbmEntry = showsAddFbmEntry;
            }

            internal bool ShowsAddFbmEntry { get; }
            internal int FooterActionCount => 1;
            internal string FooterActionLabel => FbmSaveButtonLabel;
            internal bool ShowsEntryNameForEachNormal => true;
        }

        internal static FbmDetailLayout GetFbmDetailLayoutForTest(bool fbmAxesFinalized) => new FbmDetailLayout(true);

        internal readonly struct OutfitDetailLayout
        {
            internal bool RemoveActionIsInOutfitIdRow => true;
            internal int FooterActionCount => 1;
            internal string FooterActionLabel => "Save to Database";
            internal bool FooterSaveUsesFullWidth => true;
        }

        internal static OutfitDetailLayout GetOutfitDetailLayoutForTest() => new OutfitDetailLayout();

        internal readonly struct ShapeDetailLayout
        {
            private readonly ShapeSyncDatabaseRegistry.ShapeKind kind;
            internal ShapeDetailLayout(ShapeSyncDatabaseRegistry.ShapeKind value) { kind = value; }
            internal int FooterActionCount => 1;
            internal string FooterSaveActionLabel => "Save to Database";
            internal bool FooterSaveUsesFullWidth => true;
            internal bool SaveAppearsInContent => false;
            internal bool ShowsPriority => kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph;
            internal bool ShowsTags => kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph;
        }

        internal static ShapeDetailLayout GetShapeDetailLayoutForTest(ShapeSyncDatabaseRegistry.ShapeKind kind = ShapeSyncDatabaseRegistry.ShapeKind.Skin) => new ShapeDetailLayout(kind);
        internal static float GetMorphSliderLimitForTest(float value) => DynamicBoneBlendWeightField.GetSliderLimit(value);

        internal readonly struct ShapePartEntryLayout
        {
            internal bool TargetAndEntryShareOneRow => true;
            internal bool TextureOwnerAndTextureShareOneRow => true;
            internal bool TextureColorizeAndPickerShareOneRow => true;
            internal bool MeshEntryHidesFigureMask => true;
        }

        internal static ShapePartEntryLayout GetShapePartEntryLayoutForTest() => new ShapePartEntryLayout();

        internal readonly struct ShapeTagLayout
        {
            internal bool SelectorAndAddShareOneRow => true;
            internal bool ChipsWrapWithinDetailWidth => true;
        }

        internal static ShapeTagLayout GetShapeTagLayoutForTest() => new ShapeTagLayout();

        internal readonly struct GenerationDetailLayout
        {
            internal int PathFieldCount => 5;
            internal int FooterActionCount => 1;
            internal string FooterSaveActionLabel => "Save to Database";
            internal bool SaveAppearsInFooter => true;
            internal bool GenerateRequiresCleanDraft => true;
        }

        internal static GenerationDetailLayout GetGenerationDetailLayoutForTest() => new GenerationDetailLayout();

        internal readonly struct ShapeTagsVocabularyLayout
        {
            internal bool AddActionSharesHeaderRow => true;
            internal bool UsesStandardListEditor => true;
            internal string AddActionLabel => "Add Tag";
        }

        internal static ShapeTagsVocabularyLayout GetShapeTagsVocabularyLayoutForTest() => new ShapeTagsVocabularyLayout();

        internal readonly struct ShapePartUvLayout
        {
            internal bool ScaleUsesOneRowWithXYFields => true;
            internal bool OffsetUsesOneRowWithXYFields => true;
        }

        internal static ShapePartUvLayout GetShapePartUvLayoutForTest() => new ShapePartUvLayout();

        internal readonly struct NormalDetailLayout
        {
            internal int CentralScrollViewCount => 1;
            internal bool AddActionIsAboveCentralScroll => true;
            internal bool SaveActionIsBelowCentralScroll => true;
            internal string AddActionLabel => "Add Normal Entry";
            internal string SaveActionLabel => "Save to Database";
        }
        internal static NormalDetailLayout GetNormalDetailLayoutForTest() => new NormalDetailLayout();

        [Serializable]
        private sealed class OutfitMaterialClassificationDraft
        {
            [SerializeField] private string sourceMaterialName;
            [SerializeField] private ShapeSyncDatabaseRegistry.OutfitMaterialClassification classification;
            [SerializeField] private string entryName;
            [SerializeField] private string acceptedEntryName;
            [SerializeField] private ShapeSyncDatabaseRegistry.OutfitMaterialClassification acceptedClassification;
            internal string SourceMaterialName => sourceMaterialName;
            internal ShapeSyncDatabaseRegistry.OutfitMaterialClassification Classification { get => classification; set => classification = value; }
            internal string EntryName { get => entryName; set => entryName = value; }
            internal bool IsDirty => classification != acceptedClassification || !string.Equals(entryName, acceptedEntryName, StringComparison.Ordinal);
            internal OutfitMaterialClassificationDraft(string sourceName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification value, string logicalEntryName)
            { sourceMaterialName = sourceName; classification = acceptedClassification = value; entryName = acceptedEntryName = logicalEntryName; }
            internal void Accept() { acceptedClassification = classification; acceptedEntryName = entryName; }
        }

        [Serializable]
        private sealed class OutfitFbmSourceDraft
        {
            [SerializeField] private string shapeKey;
            [SerializeField] private GameObject sourcePrefab;
            [SerializeField] private GameObject acceptedSourcePrefab;
            internal string ShapeKey => shapeKey;
            internal GameObject SourcePrefab { get => sourcePrefab; set => sourcePrefab = value; }
            internal bool IsDirty => sourcePrefab != acceptedSourcePrefab;
            internal OutfitFbmSourceDraft(string key, GameObject value) { shapeKey = key; sourcePrefab = acceptedSourcePrefab = value; }
            internal void Accept() { acceptedSourcePrefab = sourcePrefab; }
        }

        [Serializable]
        private sealed class OutfitNormalDraft
        {
            [SerializeField] private string materialEntryName;
            [SerializeField] private string shapeKey;
            [SerializeField] private Texture texture;
            [SerializeField] private Texture acceptedTexture;
            internal string MaterialEntryName { get => materialEntryName; set => materialEntryName = value; }
            internal string ShapeKey => shapeKey;
            internal Texture Texture { get => texture; set => texture = value; }
            internal bool IsDirty => texture != acceptedTexture;
            internal OutfitNormalDraft(string material, string shape, Texture value) { materialEntryName = material; shapeKey = shape; texture = acceptedTexture = value; }
            internal void Accept() { acceptedTexture = texture; }
        }

        [Serializable]
        private sealed class OutfitTextureDraft
        {
            [SerializeField] private string key;
            [SerializeField] private Texture texture;
            [SerializeField] private Texture acceptedTexture;
            internal string Key { get => key; set => key = value; }
            internal Texture Texture { get => texture; set => texture = value; }
            internal bool IsDirty => acceptedTexture == null || texture != acceptedTexture;
            internal OutfitTextureDraft(string value, Texture source) { key = value; texture = acceptedTexture = source; }
            internal OutfitTextureDraft(string value) { key = value; }
        }

        [Serializable]
        private sealed class OutfitPbmFollowDraft
        {
            [Serializable]
            internal sealed class SourceRow
            {
                [SerializeField] private string shapeKey;
                [SerializeField] private GameObject prefab;
                internal string ShapeKey => shapeKey;
                internal GameObject Prefab { get => prefab; set => prefab = value; }
                internal SourceRow(string key, GameObject value) { shapeKey = key; prefab = value; }
            }
            [SerializeField] private string pbmAxisName;
            [SerializeField] private bool selected;
            [SerializeField] private bool acceptedSelected;
            [SerializeField] private List<SourceRow> rows = new List<SourceRow>();
            [SerializeField] private List<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry> savedFigures = new List<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry>();
            internal string PbmAxisName => pbmAxisName;
            internal bool Selected { get => selected; set => selected = value; }
            internal IReadOnlyList<SourceRow> Rows => rows;
            internal bool IsDirty => selected != acceptedSelected
                || (selected && rows.Any(row => row.Prefab != GetSavedSourcePrefab(row.ShapeKey)));
            internal OutfitPbmFollowDraft(string name, ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry saved, IEnumerable<string> shapeKeys)
            {
                pbmAxisName = name; selected = acceptedSelected = saved != null;
                savedFigures = saved?.Figures.Where(value => value != null).ToList() ?? new List<ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry>();
                foreach (string key in shapeKeys)
                {
                    // A persisted SourcePrefab is reusable only while its geometry is
                    // intact. PBM Follow sources intentionally have no Material payload,
                    // but they must retain Mesh/weight/bindpose data. Do not feed a stale
                    // Database artifact with a missing Mesh back into Save; an explicitly
                    // supplied source row is the overwrite authority.
                    GameObject savedSource = GetSavedSourcePrefab(key);
                    rows.Add(new SourceRow(key, IsReusablePbmFollowSource(savedSource) ? savedSource : null));
                }
            }
            internal GameObject GetSavedPrefab(string key) => savedFigures.FirstOrDefault(figure => figure.ShapeKey == key)?.Figure;
            internal GameObject GetSavedSourcePrefab(string key)
                => savedFigures.FirstOrDefault(figure => figure.ShapeKey == key)?.SourcePrefab;
        }

        private static bool IsReusablePbmFollowSource(GameObject sourcePrefab)
        {
            if (sourcePrefab == null || !EditorUtility.IsPersistent(sourcePrefab)) return false;
            SkinnedMeshRenderer[] renderers = sourcePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return false;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer?.sharedMesh;
                if (mesh == null)
                    return false;
            }
            return true;
        }

        [Serializable]
        private sealed class OutfitCollectionDraft
        {
            [SerializeField] private string shapeKey;
            [SerializeField] private GameObject prefab;
            [SerializeField] private GameObject acceptedPrefab;
            [SerializeField] private GameObject savedPrefab;
            internal string ShapeKey => shapeKey;
            internal GameObject Prefab { get => prefab; set => prefab = value; }
            internal GameObject SavedPrefab => savedPrefab;
            internal bool IsDirty => prefab != acceptedPrefab;
            internal OutfitCollectionDraft(string key, GameObject source, GameObject saved)
            { shapeKey = key; prefab = acceptedPrefab = source; savedPrefab = saved; }
        }
        internal const float DefaultWindowWidth = 1024f;
        internal const float DefaultWindowHeight = 768f;
        internal const float TreeViewWidth = 200f;
        // Match Figure Builder's primary Generate action so every Detail save has the
        // same visual weight and click target.
        internal const float DetailSaveButtonHeight = 40f;
        internal const string DetailTitle = "General";
        internal const string EmptyDatabaseMessage = "Select or create a ShapeSync Database.";
        internal const string FigureDetailMessage = "No Figure is selected.";
        internal const string ShapesDetailMessage = "No Shape is selected.";
        internal static readonly string[] TreeLabels = { "General", "Figure", "Materials", "Shapes", "Textures" };
        internal static Func<Rect, string, ShapeSyncDatabaseWindow> CreateWindow = (rect, title) =>
            GetWindowWithRect<ShapeSyncDatabaseWindow>(rect, false, title);
        internal static DatabaseCreator CreateDatabase = ShapeSyncDatabaseAsset.TryCreate;
        internal static DatabaseOpener OpenDatabase = ShapeSyncDatabaseAsset.TryOpen;
        internal static Func<string, bool> DeleteDatabaseAsset = AssetDatabase.DeleteAsset;
        internal static Action RefreshAssetDatabase = AssetDatabase.Refresh;
        internal static Func<string, string, string, string, string, string> SaveDatabasePanel = EditorUtility.SaveFilePanelInProject;
        internal static Func<string, string, string, string, string, string> SaveFigureExportPanel = EditorUtility.SaveFilePanelInProject;
        internal static Func<string, string, string, string, string, string> SaveOutfitExportPanel = EditorUtility.SaveFilePanelInProject;
        internal static Func<string, string, string, string> OpenDatabasePanel = EditorUtility.OpenFilePanel;
        internal static Func<string, string> ToProjectRelativePath = FileUtil.GetProjectRelativePath;
        internal static Func<Section, bool> IsDetailDirty = _ => false;
        internal static Func<Section, string> SaveDirtyDetail = _ => null;
        internal static Action<Section> IgnoreDirtyDetail = _ => { };
        internal static Func<string, string, string, string, string, int> DisplayDirtyDialog = EditorUtility.DisplayDialogComplex;
        internal static Func<string, string, string, bool> ConfirmFigureImport = EditorUtility.DisplayDialog;
        internal static Func<string, string, string, string, bool> ConfirmTextureRename = EditorUtility.DisplayDialog;
        internal static Func<string, string, string, string, bool> ConfirmIrreversibleOutfitClassification = EditorUtility.DisplayDialog;
        internal static Func<bool> IsBatchMode = () => Application.isBatchMode;
        internal static FigureAdmitter AdmitFigure = ShapeSyncFigureImport.TryAdmit;
        internal static FigureAdmitter AdmitAxisFigure = ShapeSyncFigureImport.TryAdmitAxisSource;
        internal static FigureImporter ImportFigure = ShapeSyncFigureImport.TryImport;
        internal static MaterialEntrySaver SaveMaterialEntries = ShapeSyncMaterialEntryImport.TrySave;
        internal static MaterialEntrySaverWithTextureRename SaveMaterialEntriesWithTextureRename = ShapeSyncMaterialEntryImport.TrySaveWithTextureRename;
        internal static MaterialEntryRenamer RenameMaterialEntries = ShapeSyncMaterialEntryImport.TryRename;
        internal static TextureResourceSaver SaveTextureResources = ShapeSyncTextureResourceAuthoring.TrySave;
        internal static NormalEntrySaver SaveNormalEntries = ShapeSyncNormalEntryAuthoring.TrySave;
        internal static FbmAxisRemover RemoveFbmAxis = ShapeSyncFigureAxisImport.TryRemoveFbm;
        internal static FbmAxisReplacer ReplaceFbmAxis = ShapeSyncFigureAxisImport.TryReplaceFbm;
        internal static FbmAxisRenamer RenameFbmAxis = ShapeSyncFigureAxisImport.TryRenameFbm;
        internal static FbmAxisRemover RemovePbmAxis = ShapeSyncFigureAxisImport.TryRemovePbm;
        internal static FbmAxisRenamer RenamePbmAxis = ShapeSyncFigureAxisImport.TryRenamePbm;
        internal static PbmAxisReplacer ReplacePbmAxis = ShapeSyncFigureAxisImport.TryReplacePbm;
        internal static BaseFigureRenamer RenameBaseFigure = ShapeSyncFigureImport.TryRenameBaseFigure;
        internal static DatabaseFigureExporter ExportDatabaseFigure = ShapeSyncDatabaseFigureExport.TryExport;
        internal static DatabaseOutfitExporter ExportDatabaseOutfit = ShapeSyncDatabaseOutfitExport.TryExport;
        internal static FigureGenerator GenerateFigure = ShapeSyncFigureGenerator.TryGenerate;
        internal static OutfitGenerator GenerateOutfit = ShapeSyncOutfitGenerator.TryGenerate;
        internal static ShapeGenerator GenerateShape = ShapeSyncShapeGenerator.TryGenerate;

        internal enum Section
        {
            General,
            Figure,
            Materials,
            Shapes,
            Textures,
            ExtraMorphs,
            Fbms,
            Pbms,
            Normals,
            Generation,
            Outfits,
            MeshOutfit,
            MaterialOutfit,
            Vrm,
        }

        [SerializeField] private Section selectedSection = Section.General;
        [SerializeField] private ShapeSyncDatabase database;
        [SerializeField] private string diagnostic;
        [NonSerialized] private ShapeSyncDatabaseDiagnostic[] generateDiagnostics = Array.Empty<ShapeSyncDatabaseDiagnostic>();
        [SerializeField] private string figureName;
        [SerializeField] private GameObject figurePrefab;
        [SerializeField] private GameObject databaseFigurePrefab;
        // The Figure Detail is an editable draft. These values are the last accepted state:
        // after a successful import, an Ignore, or a Database rebind.
        [SerializeField] private string acceptedFigureName;
        [SerializeField] private GameObject acceptedFigurePrefab;
        [SerializeField] private int pcmSlots;
        [SerializeField] private int acceptedPcmSlots;
        [SerializeField] private List<string> keptRawMorphs = new List<string>();
        [SerializeField] private List<string> acceptedKeptRawMorphs = new List<string>();
        [SerializeField] private List<MaterialEntryDraft> materialDrafts = new List<MaterialEntryDraft>();
        [SerializeField] private List<NormalDraft> normalDrafts = new List<NormalDraft>();
        [SerializeField] private List<Texture> acceptedNormalTextures = new List<Texture>();
        [SerializeField] private List<string> figureNormalEntryMaterialNames = new List<string>();
        [SerializeField] private List<string> acceptedFigureNormalEntryMaterialNames = new List<string>();
        [SerializeField] private bool figureNormalEntriesInitialized;
        [SerializeField] private Vector2 figureDetailScrollPosition;
        [SerializeField] private Vector2 figureNormalEntriesScrollPosition;
        [SerializeField] private List<FbmAxisDraft> fbmAxisDrafts = new List<FbmAxisDraft>();
        [SerializeField] private List<FbmAxisRedefinitionDraft> fbmAxisRedefinitionDrafts = new List<FbmAxisRedefinitionDraft>();
        [SerializeField] private List<PbmAxisDraft> pbmAxisDrafts = new List<PbmAxisDraft>();
        [SerializeField] private List<PbmAxisRedefinitionDraft> pbmAxisRedefinitionDrafts = new List<PbmAxisRedefinitionDraft>();
        [SerializeField] private List<string> acceptedMaterialDraftNames = new List<string>();
        [SerializeField] private string materialDraftDiagnostic;
        [SerializeField] private Vector2 materialsScrollPosition;
        [SerializeField] private Vector2 outfitMaterialsScrollPosition;
        [SerializeField] private Vector2 outfitDetailScrollPosition;
        [SerializeField] private Vector2 outfitFbmsScrollPosition;
        [SerializeField] private Vector2 outfitNormalsScrollPosition;
        [SerializeField] private Vector2 outfitPbmsScrollPosition;
        [SerializeField] private Vector2 outfitCollectionScrollPosition;
        [SerializeField] private Vector2 materialOutfitScrollPosition;
        [SerializeField] private Vector2 figureMaskScrollPosition;
        [SerializeField] private Vector2 shapeDetailScrollPosition;
        [SerializeField] private Vector2 shapeTagsScrollPosition;
        [SerializeField] private Vector2 shapePartsScrollPosition;
        [SerializeField] private List<TextureResourceDraft> textureDrafts = new List<TextureResourceDraft>();
        [SerializeField] private List<string> acceptedTextureDraftNames = new List<string>();
        [SerializeField] private List<string> removedTextureDraftNames = new List<string>();
        [SerializeField] private string newTextureName;
        [SerializeField] private Texture newTexture;
        [SerializeField] private Vector2 texturesScrollPosition;
        [SerializeField] private string generationRegistriesPath = ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultRegistriesPath;
        [SerializeField] private string generationBindingsPath = ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultBindingsPath;
        [SerializeField] private string generationMaterialsPath = ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultMaterialsPath;
        [SerializeField] private string generationTexturesPath = ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultTexturesPath;
        [SerializeField] private string generationOutfitsPath = ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultOutfitsPath;
        [NonSerialized] private ShapeSyncDatabase generationDraftDatabase;
        [SerializeField] private string newOutfitIdentity;
        [SerializeField] private string newOutfitName;
        [SerializeField] private string selectedOutfitIdentity;
        [SerializeField] private List<string> outfitOrderDraft = new List<string>();
        [SerializeField] private List<string> acceptedOutfitOrderDraft = new List<string>();
        [SerializeField] private string outfitNameDraft;
        [SerializeField] private string acceptedOutfitNameDraft;
        [SerializeField] private GameObject outfitSourcePrefabDraft;
        [SerializeField] private GameObject acceptedOutfitSourcePrefabDraft;
        [SerializeField] private string selectedMeshOutfitChildLabel;
        [SerializeField] private List<OutfitMaterialClassificationDraft> outfitMaterialClassificationDrafts = new List<OutfitMaterialClassificationDraft>();
        [SerializeField] private List<OutfitFbmSourceDraft> outfitFbmSourceDrafts = new List<OutfitFbmSourceDraft>();
        [SerializeField] private List<string> outfitNormalEntryMaterialNames = new List<string>();
        [SerializeField] private List<string> acceptedOutfitNormalEntryMaterialNames = new List<string>();
        [SerializeField] private List<OutfitNormalDraft> outfitNormalDrafts = new List<OutfitNormalDraft>();
        [SerializeField] private bool outfitNormalDraftsInitialized;
        [SerializeField] private List<OutfitPbmFollowDraft> outfitPbmFollowDrafts = new List<OutfitPbmFollowDraft>();
        [SerializeField] private ShapeSyncDatabaseRegistry.OutfitCollectionKind outfitCollectionKind;
        [SerializeField] private ShapeSyncDatabaseRegistry.OutfitCollectionKind acceptedOutfitCollectionKind;
        [SerializeField] private bool useProjectionForFullCollection;
        [SerializeField] private bool acceptedUseProjectionForFullCollection;
        [SerializeField] private List<OutfitCollectionDraft> outfitCollectionDrafts = new List<OutfitCollectionDraft>();
        [SerializeField] private List<OutfitTextureDraft> materialOutfitTextureDrafts = new List<OutfitTextureDraft>();
        [SerializeField] private List<OutfitTextureDraft> figureMaskDrafts = new List<OutfitTextureDraft>();
        [SerializeField] private bool materialOutfitTextureDraftsInitialized;
        [SerializeField] private bool figureMaskDraftsInitialized;
        [SerializeField] private string newMaterialOutfitTextureEntryName;
        [SerializeField] private Texture newMaterialOutfitTexture;
        [SerializeField] private string newFigureMaskMaterialEntryName;
        [SerializeField] private Texture newFigureMaskTexture;
        [SerializeField] private string newShapeId;
        [SerializeField] private string newShapeName;
        [SerializeField] private string selectedShapeId;
        [SerializeField] private ShapesDetailView shapesDetailView = ShapesDetailView.Root;
        [SerializeField] private List<string> shapeTagsDraft = new List<string>();
        [SerializeField] private string selectedShapeNameDraft;
        [SerializeField] private int selectedShapePriorityDraft;
        [SerializeField] private List<string> selectedShapeTagsDraft = new List<string>();
        [SerializeField] private List<MorphValue> selectedShapeMorphsDraft = new List<MorphValue>();
        [SerializeField] private List<ShapeSyncDatabaseRegistry.ShapeEntryDefinition> selectedShapePartsDraft = new List<ShapeSyncDatabaseRegistry.ShapeEntryDefinition>();
        [SerializeField] private string newShapeTag;
        [SerializeField] private string acceptedShapeNameDraft;
        [SerializeField] private int acceptedShapePriorityDraft;
        [SerializeField] private List<string> acceptedShapeTagsDraft = new List<string>();
        [SerializeField] private List<string> shapeOrderDraft = new List<string>();
        [SerializeField] private List<string> acceptedShapeOrderDraft = new List<string>();
        private ShapeSyncDatabase shapeTagsDraftDatabase;
        [NonSerialized] private ShapeSyncDatabaseRegistry.ShapeEntry pendingShapeDraft;
        // Navigation uses stable, local integer IDs (1: General, 2: Figure, 3: Shapes),
        // rather than Unity instance IDs.
        [SerializeField] private ShapeSyncTreeViewState treeViewState;
        private NavigationTreeView treeView;

        internal Section SelectedSection => selectedSection;
        internal ShapeSyncDatabase Database => database;
        internal string Diagnostic => diagnostic;
        internal IReadOnlyList<ShapeSyncDatabaseDiagnostic> GenerateDiagnosticsForTest => generateDiagnostics;
        internal string SelectedOutfitIdentityForTest => selectedOutfitIdentity;
        internal string OutfitNameDraftForTest => outfitNameDraft;
        internal GameObject OutfitSourcePrefabDraftForTest => outfitSourcePrefabDraft;
        internal bool IsOutfitDetailDirtyForTest => IsOutfitDetailDirty();
        internal string FigureName => figureName;
        internal GameObject FigurePrefab => figurePrefab;
        internal GameObject DatabaseFigurePrefab => databaseFigurePrefab;
        internal bool IsFigureDetailDirtyForTest => IsFigureDetailDirty();
        internal bool IsFigureExportEnabledForTest => CanExportDatabaseFigure();
        internal bool CanExportDatabaseFigureForTest(GameObject figure) => CanExportDatabaseFigure(figure);
        internal bool CanExportDatabaseOutfitForTest(GameObject outfit) => CanExportDatabaseOutfit(outfit);
        internal bool IsNormalsDetailDirtyForTest => IsNormalsDetailDirty();
        internal bool IsExtraMorphsDetailDirtyForTest => IsExtraMorphsDetailDirty();
        internal bool IsMaterialsDetailDirtyForTest => IsMaterialsDetailDirty();
        internal bool IsFigureSaveEnabledForTest => CanSaveFigure();
        internal bool IsFbmSaveEnabledForTest => database != null && database.Registry != null && IsFbmAxisDetailDirty();
        internal bool CanAddFigureNormalEntryForTest { get { EnsureFigureNormalEntryDrafts(); return databaseFigurePrefab != null && GetAvailableFigureNormalEntryMaterialNames(null).Length != 0; } }
        internal IReadOnlyList<string> FigureNormalEntryMaterialNamesForTest { get { EnsureFigureNormalEntryDrafts(); return figureNormalEntryMaterialNames.ToArray(); } }
        internal int NormalDraftCountForTest { get { EnsureFigureNormalEntryDrafts(); return normalDrafts.Count; } }
        internal bool HasFigureNormalEntryDraftForTest(int index)
        {
            EnsureFigureNormalEntryDrafts();
            return index >= 0 && index < figureNormalEntryMaterialNames.Count && FindBaseNormalDraft(figureNormalEntryMaterialNames[index]) != null;
        }
        internal bool TryAddFigureNormalEntryForTest() => TryAddFigureNormalEntry();
        internal bool TryRemoveFigureNormalEntryForTest(int index) => TryRemoveFigureNormalEntry(index);
        internal bool TrySaveNormalsForTest(out string saveDiagnostic) => TrySaveNormals(out saveDiagnostic);
        internal bool TrySaveFbmNormalsForTest(out string saveDiagnostic) => TrySaveFbmNormals(out saveDiagnostic);
        internal bool TryRemoveFbmAxisForTest(string axisName, out string removeDiagnostic) => TryRemoveFbmAxis(axisName, out removeDiagnostic);
        internal int PcmSlotsForTest => pcmSlots;
        internal IReadOnlyList<string> KeptRawMorphsForTest => keptRawMorphs;
        internal bool IsMaterialsSaveEnabledForTest => CanSaveMaterialEntries();
        internal bool TrySaveMaterialEntriesForTest(out string diagnostic) => TrySaveMaterialEntries(out diagnostic);
        internal bool TrySaveExtraMorphsForTest(out string diagnostic) => TrySaveExtraMorphs(out diagnostic);
        internal bool TryGenerateForTest(string rootPath, out string generateDiagnostic) => TryGenerate(rootPath, out generateDiagnostic);
        internal bool TryAddOutfitForTest(string identity, string name, ShapeSyncDatabaseRegistry.OutfitKind kind, out string saveDiagnostic)
            => TryAddOutfit(identity, name, kind, out saveDiagnostic);
        internal bool TryAddShapeForTest(string id, string name, ShapeSyncDatabaseRegistry.ShapeKind kind, out string saveDiagnostic)
        {
            newShapeId = id;
            newShapeName = name;
            if (!TryAddShape(kind, out saveDiagnostic)) return false;
            // Preserve the long-standing test seam contract: this helper represents a
            // complete registration. The production button remains a memory draft.
            return TrySaveSelectedShapeDraft(out saveDiagnostic);
        }
        internal bool TryBeginShapeDraftForTest(string id, string name, ShapeSyncDatabaseRegistry.ShapeKind kind, out string diagnostic)
        {
            newShapeId = id;
            newShapeName = name;
            return TryAddShape(kind, out diagnostic);
        }
        internal bool TrySelectOutfitForTest(string identity) => TrySelectOutfit(identity);
        internal bool TrySelectShapeForTest(string id) => TryNavigateToShape(id);
        internal bool TrySelectShapeTagsForTest() => TryNavigateToShapeTags();
        internal string ShapesDetailViewForTest => shapesDetailView.ToString();
        internal string SelectedShapeIdForTest => selectedShapeId;
        internal bool IsShapeTagsDetailDirtyForTest() => IsShapeTagsDetailDirty();
        internal bool IsSelectedShapeIdReadOnlyForTest => pendingShapeDraft == null;
        internal void DiscardSelectedShapeDraftForTest() => DiscardSelectedShapeDraft();
        internal void SetShapeTagsDraftForTest(IReadOnlyList<string> tags) => shapeTagsDraft = tags == null ? new List<string>() : new List<string>(tags);
        internal bool TrySaveShapeTagsForTest(out string diagnostic) => TrySaveShapeTags(out diagnostic);
        internal void SetSelectedShapeMetadataDraftForTest(string name, int priority, IReadOnlyList<string> tags)
        {
            selectedShapeNameDraft = name;
            selectedShapePriorityDraft = priority;
            selectedShapeTagsDraft = tags == null ? new List<string>() : new List<string>(tags);
        }

        private enum ShapesDetailView { Root, Tags, Shape }
        internal bool IsShapesDetailDirtyForTest() => IsShapesDetailDirty();
        internal bool TryAddShapePartDraftForTest(ShapeSyncDatabaseRegistry.ShapeEntryKind kind, out string diagnostic) => TryAddShapePart(kind, out diagnostic);
        internal bool TryNavigateToShapeForTest(string shapeId) => TryNavigateToShape(shapeId);
        internal int ShapePartDraftCountForTest => selectedShapePartsDraft.Count;
        internal ShapeSyncDatabaseRegistry.ShapeEntryDefinition GetShapePartDraftForTest(int index)
            => index >= 0 && index < selectedShapePartsDraft.Count ? selectedShapePartsDraft[index] : null;
        internal IReadOnlyList<MorphValue> ShapeMorphDraftForTest => selectedShapeMorphsDraft.ToArray();
        internal void EnsureSelectedShapeDraftForTest()
        {
            ShapeSyncDatabaseRegistry.ShapeEntry shape = GetSelectedShapeEntry();
            EnsureSelectedShapeDraft(shape);
        }
        internal bool TrySetShapeMorphDraftForTest(string target, float value)
        {
            int existing = selectedShapeMorphsDraft.FindIndex(entry => string.Equals(entry.Target, target, StringComparison.Ordinal));
            if (existing >= 0) selectedShapeMorphsDraft.RemoveAt(existing);
            selectedShapeMorphsDraft.Add(new MorphValue { Target = target, Value = value });
            return true;
        }
        internal bool TrySaveSelectedShapeDraftForTest(out string diagnostic) => TrySaveSelectedShapeDraft(out diagnostic);
        internal bool TryRemoveSelectedShapeForTest(out string diagnostic) => TryRemoveShape(out diagnostic);
        internal bool TryMoveSelectedShapeForTest(bool moveUp, out string saveDiagnostic) => TryMoveSelectedShape(moveUp, out saveDiagnostic);
        internal bool TrySaveOutfitForTest(out string saveDiagnostic) => TrySaveOutfit(out saveDiagnostic);
        internal bool TryMoveSelectedOutfitForTest(bool moveUp, out string saveDiagnostic) => TryMoveSelectedOutfit(moveUp, out saveDiagnostic);
        internal bool TryRemoveSelectedOutfitForTest(out string saveDiagnostic) => TryRemoveSelectedOutfit(out saveDiagnostic);
        internal bool TrySelectOutfitChildForTest(string identity, string childLabel) => TryNavigateToOutfitChild(identity, childLabel);
        internal bool TrySetOutfitPbmFollowDraftForTest(string pbmAxisName, bool selected, string shapeKey, GameObject prefab)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) return false;
            EnsureOutfitPbmFollowDrafts(outfit);
            OutfitPbmFollowDraft draft = outfitPbmFollowDrafts.FirstOrDefault(value => value.PbmAxisName == pbmAxisName);
            OutfitPbmFollowDraft.SourceRow row = draft?.Rows.FirstOrDefault(value => value.ShapeKey == shapeKey);
            if (draft == null || row == null) return false;
            draft.Selected = selected;
            row.Prefab = prefab;
            return true;
        }
        internal GameObject OutfitPbmFollowSourcePrefabForTest(string pbmAxisName, string shapeKey)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) return null;
            EnsureOutfitPbmFollowDrafts(outfit);
            return outfitPbmFollowDrafts.FirstOrDefault(value => value.PbmAxisName == pbmAxisName)?.Rows
                .FirstOrDefault(value => value.ShapeKey == shapeKey)?.Prefab;
        }
        internal bool TrySetOutfitCollectionDraftForTest(ShapeSyncDatabaseRegistry.OutfitCollectionKind kind, bool useProjection, string shapeKey, GameObject prefab)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) return false;
            EnsureOutfitCollectionDrafts(outfit);
            OutfitCollectionDraft draft = outfitCollectionDrafts.FirstOrDefault(value => value.ShapeKey == shapeKey);
            if (draft == null) return false;
            outfitCollectionKind = kind;
            useProjectionForFullCollection = useProjection;
            draft.Prefab = prefab;
            return true;
        }
        internal bool TryAddOutfitNormalEntryForTest()
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) return false;
            EnsureOutfitNormalDrafts(outfit);
            string materialEntry = outfit.MaterialEntries.Select(entry => entry.LogicalName).FirstOrDefault(name => !outfitNormalEntryMaterialNames.Contains(name));
            if (materialEntry == null) return false;
            outfitNormalEntryMaterialNames.Add(materialEntry);
            EnsureOutfitNormalCells(outfit, materialEntry);
            return true;
        }
        internal IReadOnlyList<string> OutfitNormalEntryMaterialNamesForTest
        {
            get
            {
                EnsureOutfitNormalDrafts(GetSelectedOutfit());
                return outfitNormalEntryMaterialNames.ToArray();
            }
        }
        internal bool TrySetOutfitNormalDraftForTest(string materialEntryName, string shapeKey, Texture texture)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null || !outfitNormalEntryMaterialNames.Contains(materialEntryName)) return false;
            GetOrCreateOutfitNormalDraft(outfit, materialEntryName, shapeKey).Texture = texture;
            return true;
        }
        internal bool TryRemoveOutfitNormalEntryForTest(string materialEntryName)
        {
            if (!outfitNormalEntryMaterialNames.Contains(materialEntryName)) return false;
            RemoveOutfitNormalEntry(materialEntryName);
            return true;
        }
        internal bool TryAddMaterialOutfitTextureDraftForTest(string entryName, Texture texture)
        {
            EnsureMaterialOutfitTextureDrafts(GetSelectedOutfit());
            if (string.IsNullOrWhiteSpace(entryName) || texture == null || materialOutfitTextureDrafts.Any(draft => draft.Key == entryName)) return false;
            materialOutfitTextureDrafts.Add(new OutfitTextureDraft(entryName) { Texture = texture });
            return true;
        }
        internal IReadOnlyList<string> MaterialOutfitTextureDraftNamesForTest
        {
            get
            {
                EnsureMaterialOutfitTextureDrafts(GetSelectedOutfit());
                return materialOutfitTextureDrafts.Where(draft => draft != null).Select(draft => draft.Key).ToArray();
            }
        }
        internal bool TryRemoveMaterialOutfitTextureDraftForTest(string entryName)
        {
            EnsureMaterialOutfitTextureDrafts(GetSelectedOutfit());
            OutfitTextureDraft draft = materialOutfitTextureDrafts.FirstOrDefault(value => value != null && value.Key == entryName);
            if (draft == null) return false;
            materialOutfitTextureDrafts.Remove(draft);
            return true;
        }
        internal bool TryRenameMaterialOutfitTextureDraftForTest(string currentName, string nextName)
        {
            EnsureMaterialOutfitTextureDrafts(GetSelectedOutfit());
            OutfitTextureDraft draft = materialOutfitTextureDrafts.FirstOrDefault(value => value != null && value.Key == currentName);
            if (draft == null) return false;
            draft.Key = nextName;
            return true;
        }
        internal bool TryAddFigureMaskDraftForTest(string figureMaterialEntryName, Texture texture)
        {
            EnsureFigureMaskDrafts(GetSelectedOutfit());
            if (string.IsNullOrWhiteSpace(figureMaterialEntryName) || texture == null || figureMaskDrafts.Any(draft => draft.Key == figureMaterialEntryName)) return false;
            figureMaskDrafts.Add(new OutfitTextureDraft(figureMaterialEntryName) { Texture = texture });
            return true;
        }
        internal IReadOnlyList<string> FigureMaskDraftNamesForTest
        {
            get
            {
                EnsureFigureMaskDrafts(GetSelectedOutfit());
                return figureMaskDrafts.Where(draft => draft != null).Select(draft => draft.Key).ToArray();
            }
        }
        internal bool TryRemoveFigureMaskDraftForTest(string figureMaterialEntryName)
        {
            EnsureFigureMaskDrafts(GetSelectedOutfit());
            OutfitTextureDraft draft = figureMaskDrafts.FirstOrDefault(value => value != null && value.Key == figureMaterialEntryName);
            if (draft == null) return false;
            figureMaskDrafts.Remove(draft);
            return true;
        }
        internal void SetOutfitNameDraftForTest(string value) { outfitNameDraft = value; }
        internal void SetOutfitSourcePrefabDraftForTest(GameObject value) { outfitSourcePrefabDraft = value; }
        internal NavigationTreeView CreateNavigationTreeViewForTest()
        {
            treeView = new NavigationTreeView(treeViewState ??= new ShapeSyncTreeViewState(), TryNavigateTreeItem, () => selectedSection, GetOutfitsForTreeView, TryNavigateToOutfit, TryNavigateToOutfitChild, GetShapesForTreeView, TryNavigateToShape, () => ShapeSyncDatabaseOptionalUiProvider.HasVrmNavigation);
            return treeView;
        }
        internal string ResolveGenerationRootForTest(string selectedFolderPath) => ResolveGenerationRoot(selectedFolderPath);
        internal IReadOnlyList<string> GenerationPathsForTest
        {
            get { EnsureGenerationDraft(); return new[] { generationRegistriesPath, generationBindingsPath, generationMaterialsPath, generationTexturesPath, generationOutfitsPath }; }
        }
        internal bool IsGenerationDetailDirtyForTest => IsGenerationDetailDirty();
        internal bool TrySaveGenerationForTest(out string diagnostic) => TrySaveGeneration(out diagnostic);
        internal void SetGenerationPathsForTest(string registries, string bindings, string materials, string textures)
        {
            EnsureGenerationDraft();
            generationRegistriesPath = registries;
            generationBindingsPath = bindings;
            generationMaterialsPath = materials;
            generationTexturesPath = textures;
        }
        internal void SetGenerationPathsForTest(string registries, string bindings, string materials, string textures, string outfits)
        {
            EnsureGenerationDraft();
            generationRegistriesPath = registries;
            generationBindingsPath = bindings;
            generationMaterialsPath = materials;
            generationTexturesPath = textures;
            generationOutfitsPath = outfits;
        }
        internal Texture GetNormalDraftTextureForTest(string materialEntryName, string shapeKey)
        {
            EnsureFigureNormalEntryDrafts();
            MaterialEntryDraft material = FindMaterialDraft(materialEntryName);
            NormalDraft draft = material == null ? null : GetOrCreateNormalDraft(material, shapeKey);
            return draft?.Texture;
        }
        internal Section SelectedSectionForTest => selectedSection;
        internal IReadOnlyList<string> MaterialDraftNamesForTest => materialDrafts.Select(draft => draft.EntryName).ToArray();
        internal bool TrySetNormalDraftForTest(string materialEntryName, string shapeKey, Texture texture)
        {
            EnsureFigureNormalEntryDrafts();
            MaterialEntryDraft material = FindMaterialDraft(materialEntryName);
            NormalDraft draft = material == null ? null : GetOrCreateNormalDraft(material, shapeKey);
            if (draft == null) return false;
            draft.Texture = texture;
            return true;
        }
        internal bool TryPickFbmNormalFromModelForTest(string materialEntryName, string fbmName)
        {
            EnsureFigureNormalEntryDrafts();
            MaterialEntryDraft material = FindMaterialDraft(materialEntryName);
            ShapeSyncDatabaseRegistry.FigureAxisEntry axis = database?.Registry?.FigureAxes.FirstOrDefault(entry =>
                entry != null
                && entry.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                && string.Equals(entry.Name, fbmName, StringComparison.Ordinal));
            NormalDraft draft = material == null || axis == null ? null : GetOrCreateNormalDraft(material, fbmName);
            if (draft == null) return false;
            Texture normal = ResolveFbmNormalFromModel(axis, material);
            if (normal == null) return false;
            draft.Texture = normal;
            return true;
        }
        internal void SetFbmAxisDraftsForTest(IReadOnlyList<string> names, IReadOnlyList<GameObject> prefabs)
        {
            SetFbmAxisDraftsForTest(names, prefabs, null);
        }
        internal void SetFbmAxisDraftsForTest(IReadOnlyList<string> names, IReadOnlyList<GameObject> prefabs, IReadOnlyList<bool> importMaterialsAndTextures)
        {
            fbmAxisDrafts.Clear();
            for (int index = 0; names != null && index < names.Count; index++) fbmAxisDrafts.Add(new FbmAxisDraft { Name = names[index], SourcePrefab = prefabs != null && index < prefabs.Count ? prefabs[index] : null, ImportMaterialsAndTextures = importMaterialsAndTextures != null && index < importMaterialsAndTextures.Count && importMaterialsAndTextures[index] });
        }
        internal bool AssignFbmAxisDraftPrefabFromUiForTest(int index, GameObject prefab)
        {
            if (index < 0 || index >= fbmAxisDrafts.Count) return false;
            fbmAxisDrafts[index].AssignSourcePrefab(prefab);
            return true;
        }
        internal IReadOnlyList<string> FbmAxisDraftNamesForTest => fbmAxisDrafts.Select(draft => draft.Name).ToArray();
        internal bool SetFbmAxisRedefinitionDraftForTest(string currentName, string nextName, GameObject sourcePrefab, bool importMaterialsAndTextures)
        {
            FbmAxisRedefinitionDraft draft = fbmAxisRedefinitionDrafts.FirstOrDefault(item => item.OriginalName == currentName);
            if (draft == null) return false;
            draft.Name = nextName;
            draft.SourcePrefab = sourcePrefab;
            draft.ImportMaterialsAndTextures = importMaterialsAndTextures;
            return true;
        }
        internal bool TryRemoveFbmAxisDraftForTest(int index) => TryRemoveFbmAxisDraft(index);
        internal int FbmAxisDraftCountForTest => fbmAxisDrafts.Count;
        internal bool TryRemovePbmAxisDraftForTest(int index) => TryRemovePbmAxisDraft(index);
        internal bool TryRemovePbmAxisForTest(string axisName, out string diagnostic) => TryRemovePbmAxis(axisName, out diagnostic);
        internal int PbmAxisDraftCountForTest => pbmAxisDrafts.Count;
        internal bool SetPbmAxisRedefinitionDraftForTest(string currentName, string nextName)
        {
            PbmAxisRedefinitionDraft draft = pbmAxisRedefinitionDrafts.FirstOrDefault(item => item.OriginalName == currentName);
            if (draft == null) return false;
            draft.Name = nextName;
            return true;
        }
        internal bool SetPbmAxisRedefinitionDraftForTest(string currentName, string nextName, GameObject basePrefab, IReadOnlyList<string> fbmNames, IReadOnlyList<GameObject> prefabs)
        {
            PbmAxisRedefinitionDraft draft = pbmAxisRedefinitionDrafts.FirstOrDefault(item => item.OriginalName == currentName);
            if (draft == null) return false;
            draft.Name = nextName;
            draft.BasePrefab = basePrefab;
            for (int index = 0; fbmNames != null && index < fbmNames.Count; index++) draft.SetSource(fbmNames[index], prefabs != null && index < prefabs.Count ? prefabs[index] : null);
            return true;
        }
        internal bool TrySaveFbmAxisDraftsForTest(out string diagnostic) => TrySaveFbmAxisDrafts(out diagnostic);
        internal void SetPbmAxisDraftForTest(string name, IReadOnlyList<string> fbmNames, IReadOnlyList<GameObject> prefabs)
            => SetPbmAxisDraftForTest(name, null, fbmNames, prefabs);

        internal void SetPbmAxisDraftForTest(string name, GameObject basePrefab, IReadOnlyList<string> fbmNames, IReadOnlyList<GameObject> prefabs)
        {
            pbmAxisDrafts.Clear(); var draft = new PbmAxisDraft { Name = name, BasePrefab = basePrefab };
            for (int index = 0; fbmNames != null && index < fbmNames.Count; index++) draft.SetSource(fbmNames[index], prefabs != null && index < prefabs.Count ? prefabs[index] : null);
            pbmAxisDrafts.Add(draft);
        }
        internal bool TrySavePbmAxisDraftsForTest(out string diagnostic) => TrySavePbmAxisDrafts(out diagnostic);
        internal IReadOnlyList<Texture> MaterialDraftPreviewsForTest => materialDrafts.Select(draft => draft.PreviewTexture).ToArray();
        internal string MaterialDraftDiagnosticForTest => materialDraftDiagnostic;
        internal IReadOnlyList<string> TextureDraftNamesForTest => textureDrafts.Select(draft => draft.Name).ToArray();
        internal IReadOnlyList<Texture> TextureDraftPreviewsForTest => textureDrafts.Select(draft => draft.Texture).ToArray();
        internal bool IsTexturesDetailDirtyForTest => IsTexturesDetailDirty();
        internal bool IsTexturesSaveEnabledForTest => CanSaveTextureDrafts();
        internal Vector2 MaterialsScrollPositionForTest { get => materialsScrollPosition; set => materialsScrollPosition = value; }
        internal Vector2 OutfitMaterialsScrollPositionForTest { get => outfitMaterialsScrollPosition; set => outfitMaterialsScrollPosition = value; }
        internal Vector2 TexturesScrollPositionForTest { get => texturesScrollPosition; set => texturesScrollPosition = value; }
        internal Vector2 FigureDetailScrollPositionForTest { get => figureDetailScrollPosition; set => figureDetailScrollPosition = value; }
        internal Vector2 OutfitDetailScrollPositionForTest { get => outfitDetailScrollPosition; set => outfitDetailScrollPosition = value; }
        internal Vector2 OutfitFbmsScrollPositionForTest { get => outfitFbmsScrollPosition; set => outfitFbmsScrollPosition = value; }
        internal Vector2 OutfitNormalsScrollPositionForTest { get => outfitNormalsScrollPosition; set => outfitNormalsScrollPosition = value; }
        internal Vector2 OutfitPbmsScrollPositionForTest { get => outfitPbmsScrollPosition; set => outfitPbmsScrollPosition = value; }
        internal Vector2 OutfitCollectionScrollPositionForTest { get => outfitCollectionScrollPosition; set => outfitCollectionScrollPosition = value; }
        internal Vector2 MaterialOutfitScrollPositionForTest { get => materialOutfitScrollPosition; set => materialOutfitScrollPosition = value; }
        internal Vector2 FigureMaskScrollPositionForTest { get => figureMaskScrollPosition; set => figureMaskScrollPosition = value; }
        internal Vector2 ShapeDetailScrollPositionForTest { get => shapeDetailScrollPosition; set => shapeDetailScrollPosition = value; }
        internal Texture ResolveMaterialEntryPreviewForTest(string entryName) => database == null || database.Registry == null ? null : ResolveMaterialEntryPreview(database.Registry.MaterialEntries.FirstOrDefault(entry => entry != null && entry.LogicalName == entryName));

        internal void SetFigureInputsForTest(string name, GameObject prefab)
        {
            figureName = name;
            figurePrefab = prefab;
            ResolveDatabaseFigurePrefab();
            diagnostic = null;
        }
        internal void SetFigureMorphDraftForTest(int slots, IReadOnlyList<string> names)
        {
            pcmSlots = slots;
            keptRawMorphs = names == null ? new List<string>() : names.OrderBy(value => value, StringComparer.Ordinal).ToList();
        }
        internal void SetSelectedSectionForTest(Section value) { selectedSection = value; }
        internal void DiscardFigureDraftForTest() { DiscardFigureDraft(); }
        internal void DiscardExtraMorphDraftForTest() { DiscardExtraMorphDraft(); }

        internal bool TrySetMaterialDraftNameForTest(int index, string name)
        {
            EnsureMaterialDrafts();
            if (index < 0 || index >= materialDrafts.Count) return false;
            materialDrafts[index].EntryName = name;
            return true;
        }
        internal bool TrySetTextureDraftNameForTest(int index, string name)
        {
            EnsureTextureDrafts();
            if (index < 0 || index >= textureDrafts.Count) return false;
            textureDrafts[index].Name = name;
            return true;
        }
        internal bool TryAddTextureDraftForTest(string name, Texture source)
        {
            newTextureName = name;
            newTexture = source;
            return TryAddTextureDraft();
        }
        internal bool TryRemoveTextureDraftForTest(int index) => TryRemoveTextureDraft(index);
        internal bool TrySaveTextureDraftsForTest(out string saveDiagnostic) => TrySaveTextureDrafts(out saveDiagnostic);
        internal bool TrySaveFigure(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database == null) { saveDiagnostic = "Select or create a ShapeSync Database."; diagnostic = saveDiagnostic; return false; }
            if (database.Registry != null
                && string.Equals(figureName, acceptedFigureName, StringComparison.Ordinal) && figurePrefab == acceptedFigurePrefab)
            {
                if (selectedSection == Section.Figure && pcmSlots != acceptedPcmSlots)
                {
                    if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Set ShapeSync PCM Slots",
                        (ShapeSyncDatabaseRegistry registry, out string detail) => registry.TrySetPcmSlots(pcmSlots, out detail), out saveDiagnostic))
                    { diagnostic = saveDiagnostic; return false; }
                    AcceptPcmSlotsDraft();
                }
                AcceptFigureDraft();
                diagnostic = null;
                return true;
            }
            if (string.IsNullOrWhiteSpace(figureName)) { saveDiagnostic = "Figure Name is required."; diagnostic = saveDiagnostic; return false; }
            if (database.Registry != null && figurePrefab == acceptedFigurePrefab && acceptedFigurePrefab == null && !string.Equals(figureName, acceptedFigureName, StringComparison.Ordinal))
            {
                string databasePath = AssetDatabase.GetAssetPath(database);
                if (!RenameBaseFigure(databasePath, acceptedFigureName, figureName, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                if (!TrySetDatabaseAtPath(databasePath, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                AcceptFigureDraft();
                diagnostic = null;
                return true;
            }
            if (!AdmitFigure(figurePrefab, out ShapeSyncFigureImportAdmission admission, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            string order = string.Join("\n", System.Linq.Enumerable.Select(admission.SourceRenderers, (renderer, index) => index + ": " + renderer.name));
            try { if (!ConfirmFigureImport("Save Figure to Database", "Prefab contains " + admission.SourceRenderers.Count + " SkinnedMesh Renderers.\n" + order + "\nThey will be merged.", "Save")) { saveDiagnostic = "Figure import was cancelled."; diagnostic = saveDiagnostic; return false; } }
            catch (Exception exception) { saveDiagnostic = "Could not confirm Figure import: " + exception.Message; diagnostic = saveDiagnostic; return false; }
            if (!ImportFigure(AssetDatabase.GetAssetPath(database), admission, figureName, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            ResolveDatabaseFigurePrefab();
            AcceptFigureDraft();
            diagnostic = null;
            return true;
        }

        private bool TrySaveExtraMorphs(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database == null || database.Registry == null) { saveDiagnostic = "Extra Morphs require an open Database."; diagnostic = saveDiagnostic; return false; }
            if (!TrySaveKeptRawMorphs(keptRawMorphs, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            AcceptExtraMorphDraft();
            diagnostic = null;
            return true;
        }

        private bool TrySaveNormals(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            EnsureFigureNormalEntryDrafts();
            if (database == null || database.Registry == null)
            {
                saveDiagnostic = "Normal Entry save requires an open Database.";
                diagnostic = saveDiagnostic;
                return false;
            }
            if (!HasBaseNormalDraftChanges()) return true;
            ShapeSyncNormalEntryAuthoring.Assignment[] assignments = ToChangedBaseNormalAssignments();
            if (!TryValidateRequiredNormalTextures(assignments, "Base", out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            string databasePath = AssetDatabase.GetAssetPath(database);
            if (!SaveNormalEntries(databasePath, figureNormalEntryMaterialNames, assignments, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            if (!TrySetDatabaseAtPath(databasePath, out saveDiagnostic)) return false;
            EnsureFigureNormalEntryDrafts();
            diagnostic = null;
            return true;
        }

        private bool TrySaveFbmNormals(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            EnsureFigureNormalEntryDrafts();
            if (database == null || database.Registry == null) { saveDiagnostic = "FBM Normal save requires an open Database."; diagnostic = saveDiagnostic; return false; }
            if (!HasFbmNormalDraftChanges()) return true;
            ShapeSyncNormalEntryAuthoring.Assignment[] assignments = ToChangedNormalAssignments()
                .Where(assignment => assignment.ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey).ToArray();
            if (!TryValidateRequiredNormalTextures(assignments, "FBM", out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            string databasePath = AssetDatabase.GetAssetPath(database);
            if (!SaveNormalEntries(databasePath, figureNormalEntryMaterialNames, assignments, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            if (!TrySetDatabaseAtPath(databasePath, out saveDiagnostic)) return false;
            EnsureFigureNormalEntryDrafts();
            diagnostic = null;
            return true;
        }
        internal bool IsSelectedOutfitMaterialEntryNameEditableForTest(string sourceMaterialName)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) return false;
            EnsureOutfitMaterialClassificationDrafts(outfit);
            return outfitMaterialClassificationDrafts.Any(draft => draft != null && draft.SourceMaterialName == sourceMaterialName)
                && CanEditOutfitMaterialEntryName(outfit);
        }
        internal static bool IsOutfitMaterialEntryNameEditableForTest(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
            => CanEditOutfitMaterialEntryName(outfit);

        private bool TrySaveKeptRawMorphs(IReadOnlyList<string> nextKeptRawMorphs, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Set ShapeSync Extra Morphs",
                (ShapeSyncDatabaseRegistry registry, out string registryDiagnostic) =>
            {
                return registry.TrySetKeptRawBlendShapeNames(database, nextKeptRawMorphs, out registryDiagnostic);
            }, out saveDiagnostic))
            {
                return false;
            }
            return true;
        }

        /// <summary>Opens the ShapeSync Database editor shell.</summary>
        /// <remarks>The window is available only when the ShapeSync Editor assembly is loaded.</remarks>
        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            OpenWindow();
        }

        internal static ShapeSyncDatabaseWindow OpenWindow()
        {
            return CreateWindow(new Rect(0f, 0f, DefaultWindowWidth, DefaultWindowHeight), WindowTitle);
        }

        /// <summary>Accepts only a Database that satisfies the Spec20.1 root and Intermediate contracts.</summary>
        internal bool TrySetDatabase(ShapeSyncDatabase candidate, out string diagnostic)
        {
            if (candidate == null)
            {
                diagnostic = "ShapeSync Database window requires a Database Prefab.";
                this.diagnostic = diagnostic;
                return false;
            }

            return TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(candidate), out diagnostic);
        }

        private bool TrySetDatabaseAtPath(string assetPath, out string diagnostic)
        {
            if (!OpenDatabase(assetPath, out ShapeSyncDatabase validated, out diagnostic))
            {
                this.diagnostic = diagnostic;
                return false;
            }

            // A successful bind owns its diagnostic state.  Keep a registry validation
            // diagnostic produced by ResetFigureDraft, but never retain one from a
            // previous failed open.
            this.diagnostic = null;
            database = validated;
            ShapeSyncDatabaseOptionalUiProvider.NotifyDatabaseChanged(this);
            ResetFigureDraft();
            ResetMaterialDraft();
            ResetTextureDraft();
            ResetOutfitDraft();
            pendingShapeDraft = null;
            selectedShapeId = null;
            shapesDetailView = ShapesDetailView.Root;
            ResetShapeOrderDraft();
            ResetFbmAxisRedefinitionDrafts();
            ResetPbmAxisRedefinitionDrafts();
            generationDraftDatabase = null;
            EnsureGenerationDraft();
            ResolveDatabaseFigurePrefab();
            // The NavigationTreeView may already exist from the previous Database
            // (or from window initialization). Rebind must invalidate its cached
            // rows so persisted Outfit entries become visible immediately after
            // Open Database.
            treeView?.Reload();
            diagnostic = null;
            return true;
        }

        /// <summary>Creates a Database through the Spec20.1 creation boundary and binds it only after validation.</summary>
        internal bool TryCreateDatabase(string folderPath, out string diagnostic)
        {
            if (!CreateDatabase(folderPath, out ShapeSyncDatabase created, out diagnostic))
            {
                this.diagnostic = diagnostic;
                return false;
            }

            if (TrySetDatabase(created, out diagnostic)) return true;

            string createdAssetPath = AssetDatabase.GetAssetPath(created);
            try
            {
                if (!string.IsNullOrEmpty(createdAssetPath) && !DeleteDatabaseAsset(createdAssetPath))
                {
                    diagnostic = "ShapeSync Database was created but could not be cleaned up after binding failed: " + diagnostic;
                    this.diagnostic = diagnostic;
                }
            }
            catch (Exception exception)
            {
                diagnostic = "ShapeSync Database was created but could not be cleaned up after binding failed: " + exception.Message;
                this.diagnostic = diagnostic;
            }

            return false;
        }

        /// <summary>Opens a selected Database Prefab after resolving the current Detail draft through the Save, Ignore, or Cancel guard.</summary>
        /// <param name="candidate">The Database Prefab asset to validate and bind.</param>
        /// <param name="diagnostic">Receives an admission, dirty-draft, or binding diagnostic.</param>
        internal bool TryOpenDatabase(UnityEngine.Object candidate, out string diagnostic)
        {
            if (candidate == null) return TrySetDatabase(null, out diagnostic);

            if (!TryPrepareDatabaseSwitch(out diagnostic)) return false;

            return TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(candidate), out diagnostic);
        }

        /// <summary>Resolves unsaved Detail state before a user-initiated Database replacement.</summary>
        /// <param name="switchDiagnostic">Receives the diagnostic when the current Detail cannot be resolved.</param>
        private bool TryPrepareDatabaseSwitch(out string switchDiagnostic)
        {
            switchDiagnostic = null;
            // The General detail is the only place where the Open/New buttons are
            // rendered. Any other section must first pass through the existing
            // Save / Ignore / Cancel dirty guard before a new Database can replace
            // the current binding.
            if (selectedSection == Section.General) return true;
            if (TryNavigateTo(Section.General)) return true;
            switchDiagnostic = diagnostic;
            return false;
        }

        private bool TryNavigateTreeItem(int itemId)
        {
            if (itemId == NavigationTreeView.ShapeTagsItemId) return TryNavigateToShapeTags();
            if (itemId == NavigationTreeView.ShapesItemId) return TryNavigateToShapesRoot();
            if (itemId == NavigationTreeView.VrmItemId) return TryNavigateTo(Section.Vrm);
            if (itemId == NavigationTreeView.OutfitsItemId) return TryNavigateTo(Section.Outfits);
            if (itemId == NavigationTreeView.MeshOutfitsItemId || itemId == NavigationTreeView.MaterialOutfitsItemId) return TryNavigateTo(Section.Outfits);
            return itemId >= 1 && itemId <= 10 && TryNavigateTo((Section)(itemId - 1));
        }

        private bool TryNavigateToOutfit(string identity)
        {
            if (IsOutfitDetailDirty() && !TryNavigateTo(Section.Outfits)) return false;
            return TrySelectOutfit(identity);
        }

        private bool TryNavigateToOutfitChild(string identity, string childLabel)
        {
            if (!TryNavigateToOutfit(identity)) return false;
            selectedMeshOutfitChildLabel = childLabel;
            return true;
        }

        private IReadOnlyList<ShapeSyncDatabaseRegistry.ShapeEntry> GetShapesForTreeView()
        {
            ShapeSyncDatabaseRegistry.ShapeEntry[] stored = database?.Registry?.Shapes?.Where(entry => entry != null).ToArray()
                ?? Array.Empty<ShapeSyncDatabaseRegistry.ShapeEntry>();
            if (pendingShapeDraft != null)
            {
                var withPending = stored.ToList();
                withPending.Add(pendingShapeDraft);
                return withPending;
            }
            if (shapeOrderDraft.Count != stored.Length) return stored;

            var byId = stored.ToDictionary(entry => entry.ShapeId, StringComparer.Ordinal);
            var ordered = new List<ShapeSyncDatabaseRegistry.ShapeEntry>(stored.Length);
            foreach (string shapeId in shapeOrderDraft)
            {
                if (!byId.TryGetValue(shapeId, out ShapeSyncDatabaseRegistry.ShapeEntry entry)) return stored;
                ordered.Add(entry);
            }
            return ordered.Count == stored.Length ? ordered : stored;
        }

        private bool TryNavigateToShape(string shapeId)
        {
            if (!TryNavigateTo(Section.Shapes)) return false;
            ShapeSyncDatabaseRegistry.ShapeEntry shape = GetShapesForTreeView().FirstOrDefault(entry => entry != null && string.Equals(entry.ShapeId, shapeId, StringComparison.Ordinal));
            if (shape == null) return false;
            selectedShapeId = shapeId;
            shapesDetailView = ShapesDetailView.Shape;
            EnsureSelectedShapeDraft(shape);
            return true;
        }

        private ShapeSyncDatabaseRegistry.ShapeEntry GetSelectedShapeEntry()
            => GetShapesForTreeView().FirstOrDefault(entry => entry != null && string.Equals(entry.ShapeId, selectedShapeId, StringComparison.Ordinal));

        private bool TryNavigateToShapesRoot()
        {
            if (!TryNavigateTo(Section.Shapes)) return false;
            selectedShapeId = null;
            shapesDetailView = ShapesDetailView.Root;
            return true;
        }

        private bool TryNavigateToShapeTags()
        {
            if (!TryNavigateTo(Section.Shapes)) return false;
            selectedShapeId = null;
            shapesDetailView = ShapesDetailView.Tags;
            return true;
        }

        internal bool TryNavigateTo(Section section)
        {
            if (section == selectedSection && !(section == Section.Shapes && IsShapesDetailDirty())) return true;
            bool isDirty;
            try { isDirty = IsDetailDirty(selectedSection) || IsShapesDetailDirty() || IsFigureDetailDirty() || ShapeSyncDatabaseOptionalUiProvider.IsFigureVrmDetailDirty(this) || IsNormalsDetailDirty() || IsExtraMorphsDetailDirty() || IsMaterialsDetailDirty() || IsTexturesDetailDirty() || IsFbmAxisDetailDirty() || IsPbmAxisDetailDirty() || IsGenerationDetailDirty() || IsOutfitDetailDirty(); }
            catch (Exception exception) { diagnostic = "Could not determine whether Detail changes are unsaved: " + exception.Message; return false; }
            if (!isDirty) { SelectSection(section); return true; }
            int choice;
            try { choice = DisplayDirtyDialog("Unsaved changes", "Save changes before changing Detail?", "Save", "Ignore", "Cancel"); }
            catch (Exception exception) { diagnostic = "Could not confirm unsaved Detail changes: " + exception.Message; return false; }
            if (choice == 0)
            {
                try
                {
                    string saveDiagnostic;
                    if (selectedSection == Section.Figure && IsFigureDetailDirty())
                    {
                        if (TrySaveFigure(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Vrm && ShapeSyncDatabaseOptionalUiProvider.IsFigureVrmDetailDirty(this))
                    {
                        saveDiagnostic = ShapeSyncDatabaseOptionalUiProvider.SaveFigureVrmDetail(this);
                    }
                    else if (selectedSection == Section.Normals && IsNormalsDetailDirty())
                    {
                        if (TrySaveNormals(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.ExtraMorphs && IsExtraMorphsDetailDirty())
                    {
                        if (TrySaveExtraMorphs(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Materials && IsMaterialsDetailDirty())
                    {
                        if (TrySaveMaterialEntries(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Textures && IsTexturesDetailDirty())
                    {
                        if (TrySaveTextureDrafts(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Fbms && IsFbmAxisDetailDirty())
                    {
                        if (TrySaveFbmAxisDrafts(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Pbms && IsPbmAxisDetailDirty())
                    {
                        if (TrySavePbmAxisDrafts(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Generation && IsGenerationDetailDirty())
                    {
                        if (TrySaveGeneration(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.MeshOutfit
                        && string.Equals(selectedMeshOutfitChildLabel, "VRM", StringComparison.Ordinal)
                        && ShapeSyncDatabaseOptionalUiProvider.IsMeshOutfitVrmDetailDirty(this, selectedOutfitIdentity))
                    {
                        saveDiagnostic = ShapeSyncDatabaseOptionalUiProvider.SaveMeshOutfitVrmDetail(this, selectedOutfitIdentity);
                    }
                    else if ((selectedSection == Section.MeshOutfit || selectedSection == Section.MaterialOutfit) && IsOutfitDetailDirty())
                    {
                        if (TrySaveOutfit(out saveDiagnostic)) saveDiagnostic = null;
                    }
                    else if (selectedSection == Section.Shapes && IsShapesDetailDirty()) TrySaveSelectedShapeDraft(out saveDiagnostic);
                    else saveDiagnostic = SaveDirtyDetail(selectedSection);
                    if (string.IsNullOrEmpty(saveDiagnostic)) { SelectSection(section); return true; }
                    diagnostic = saveDiagnostic;
                }
                catch (Exception exception) { diagnostic = "Could not save unsaved Detail changes: " + exception.Message; }
                return false;
            }

            if (choice == 1)
            {
                try
                {
                    IgnoreDirtyDetail(selectedSection);
                    if (selectedSection == Section.Shapes && IsShapesDetailDirty()) DiscardSelectedShapeDraft();
                    if (selectedSection == Section.Figure && IsFigureDetailDirty()) DiscardFigureDraft();
                    if (selectedSection == Section.Vrm && ShapeSyncDatabaseOptionalUiProvider.IsFigureVrmDetailDirty(this)) ShapeSyncDatabaseOptionalUiProvider.IgnoreFigureVrmDetail(this);
                    if (selectedSection == Section.Normals && IsNormalsDetailDirty()) DiscardNormalsDraft();
                    if (selectedSection == Section.ExtraMorphs && IsExtraMorphsDetailDirty()) DiscardExtraMorphDraft();
                    if (selectedSection == Section.Materials && IsMaterialsDetailDirty()) DiscardMaterialDraft();
                    if (selectedSection == Section.Textures && IsTexturesDetailDirty()) DiscardTextureDraft();
                    if (selectedSection == Section.Fbms && IsFbmAxisDetailDirty())
                    {
                        ResetFbmAxisDrafts();
                        DiscardFbmNormalDrafts();
                    }
                    if (selectedSection == Section.Pbms && IsPbmAxisDetailDirty()) ResetPbmAxisDrafts();
                    if (selectedSection == Section.Generation && IsGenerationDetailDirty())
                    {
                        ResetGenerationDraft();
                        ShapeSyncDatabaseOptionalUiProvider.IgnoreGenerationVrmPath(this);
                    }
                    if ((selectedSection == Section.MeshOutfit || selectedSection == Section.MaterialOutfit) && IsOutfitDetailDirty()) ResetOutfitDraft();
                    if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "VRM", StringComparison.Ordinal)
                        && ShapeSyncDatabaseOptionalUiProvider.IsMeshOutfitVrmDetailDirty(this, selectedOutfitIdentity))
                        ShapeSyncDatabaseOptionalUiProvider.IgnoreMeshOutfitVrmDetail(this, selectedOutfitIdentity);
                }
                catch (Exception exception) { diagnostic = "Could not ignore unsaved Detail changes: " + exception.Message; return false; }
                SelectSection(section);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            selectedSection = Section.General;
            titleContent = new GUIContent(WindowTitle);
            treeViewState ??= new ShapeSyncTreeViewState();
            treeView = new NavigationTreeView(treeViewState, TryNavigateTreeItem, () => selectedSection, GetOutfitsForTreeView, TryNavigateToOutfit, TryNavigateToOutfitChild, GetShapesForTreeView, TryNavigateToShape, () => ShapeSyncDatabaseOptionalUiProvider.HasVrmNavigation);
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTreeView();
                DrawDetailView();
            }
        }

        private void DrawTreeView()
        {
            treeView ??= new NavigationTreeView(treeViewState ??= new ShapeSyncTreeViewState(), TryNavigateTreeItem, () => selectedSection, GetOutfitsForTreeView, TryNavigateToOutfit, TryNavigateToOutfitChild, GetShapesForTreeView, TryNavigateToShape, () => ShapeSyncDatabaseOptionalUiProvider.HasVrmNavigation);
            treeView.OnGUI(GUILayoutUtility.GetRect(TreeViewWidth, TreeViewWidth, 0f, float.MaxValue, GUILayout.ExpandHeight(true)));
        }

        private void DrawDetailView()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                if (selectedSection == Section.General)
                {
                    DrawGeneralDetail();
                    return;
                }

                if (selectedSection == Section.Figure) { DrawFigureDetail(); return; }
                if (selectedSection == Section.Vrm)
                {
                    if (!ShapeSyncDatabaseOptionalUiProvider.TryDrawFigureVrmDetail(this))
                        EditorGUILayout.HelpBox("VRM integration is not available.", MessageType.Info);
                    return;
                }
                if (selectedSection == Section.ExtraMorphs) { DrawExtraMorphsDetail(); return; }
                if (selectedSection == Section.Materials) { DrawMaterialsDetail(); return; }
                if (selectedSection == Section.Normals) { DrawNormalsDetail(); return; }
                if (selectedSection == Section.Fbms) { DrawFigureAxisDetail(ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm); return; }
                if (selectedSection == Section.Pbms) { DrawFigureAxisDetail(ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm); return; }
                if (selectedSection == Section.Textures) { DrawTexturesDetail(); return; }
                if (selectedSection == Section.Generation) { DrawGenerationDetail(); return; }
                if (selectedSection == Section.Outfits) { DrawOutfitsDetail(); return; }
                if (selectedSection == Section.Shapes) { DrawShapesDetail(); return; }
                if (selectedSection == Section.MeshOutfit || selectedSection == Section.MaterialOutfit) { DrawOutfitDetail(); return; }
                string detailTitle = selectedSection == Section.Figure ? TreeLabels[(int)Section.Figure] : TreeLabels[(int)Section.Shapes];
                string emptyMessage = selectedSection == Section.Figure ? FigureDetailMessage : ShapesDetailMessage;
                GUILayout.Label(detailTitle, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(emptyMessage, MessageType.Info);
            }
        }

        private void DrawShapesDetail()
        {
            GUILayout.Label("Shapes", EditorStyles.boldLabel);
            if (database?.Registry == null)
            {
                EditorGUILayout.HelpBox(EmptyDatabaseMessage, MessageType.Info);
                return;
            }

            if (shapeTagsDraftDatabase != database)
            {
                shapeTagsDraft = new List<string>(database.Registry.ShapeTags);
                shapeTagsDraftDatabase = database;
            }

            if (shapesDetailView == ShapesDetailView.Shape)
            {
                DrawSelectedShapeDetail();
                if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
                return;
            }
            if (shapesDetailView == ShapesDetailView.Tags)
            {
                DrawShapeTagsEditor();
                if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
                return;
            }

            newShapeId = EditorGUILayout.TextField("Shape Id", newShapeId);
            newShapeName = EditorGUILayout.TextField("Shape Name", newShapeName);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Morph Shape Template")) TryAddShape(ShapeSyncDatabaseRegistry.ShapeKind.Morph, out _);
                if (GUILayout.Button("Create Skin Shape Template")) TryAddShape(ShapeSyncDatabaseRegistry.ShapeKind.Skin, out _);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Hair Shape Template")) TryAddShape(ShapeSyncDatabaseRegistry.ShapeKind.Hair, out _);
                if (GUILayout.Button("Create Outfit Shape Template")) TryAddShape(ShapeSyncDatabaseRegistry.ShapeKind.Outfit, out _);
            }
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void ResetShapeTagsDraft()
        {
            shapeTagsDraft = database?.Registry == null
                ? new List<string>()
                : new List<string>(database.Registry.ShapeTags);
            shapeTagsDraftDatabase = database;
        }

        private void DrawShapeTagsEditor()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel);
                if (GUILayout.Button("Add Tag", GUILayout.Width(80f))) shapeTagsDraft.Add(string.Empty);
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(shapeTagsScrollPosition, GUILayout.ExpandHeight(true)))
            {
                for (int index = 0; index < shapeTagsDraft.Count; index++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        shapeTagsDraft[index] = EditorGUILayout.TextField(shapeTagsDraft[index]);
                        if (GUILayout.Button("Remove", GUILayout.Width(70f))) { shapeTagsDraft.RemoveAt(index); break; }
                    }
                }
                shapeTagsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsShapeTagsDetailDirty()))
                if (GUILayout.Button("Save into Database", GUILayout.ExpandWidth(true), GUILayout.Height(DetailSaveButtonHeight))) TrySaveShapeTags(out _);
        }

        private void DrawSelectedShapeDetail()
        {
            ShapeSyncDatabaseRegistry.ShapeEntry shape = GetSelectedShapeEntry();
            if (shape == null) return;
            EnsureSelectedShapeDraft(shape);
            ShapeDetailLayout layout = GetShapeDetailLayoutForTest(shape.Kind);
            using (var scroll = new EditorGUILayout.ScrollViewScope(shapeDetailScrollPosition, GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(shape.Kind + " Shape", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (pendingShapeDraft == shape)
                    {
                        string nextId = EditorGUILayout.TextField("Shape Id", shape.ShapeId);
                        if (!string.Equals(nextId, shape.ShapeId, StringComparison.Ordinal))
                        {
                            if (string.IsNullOrWhiteSpace(nextId) || nextId.Any(char.IsWhiteSpace)
                                || database.Registry.Shapes.Any(entry => entry != null && entry != shape && string.Equals(entry.ShapeId, nextId, StringComparison.Ordinal)))
                                diagnostic = "Shape Id must be unique and must not be empty or contain whitespace.";
                            else
                            {
                                shape.SetShapeId(nextId);
                                selectedShapeId = nextId;
                                acceptedShapeNameDraft = "\u0001" + nextId;
                                diagnostic = null;
                                treeView?.Reload();
                            }
                        }
                    }
                    else using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Shape Id", shape.ShapeId);
                    using (new EditorGUI.DisabledScope(IsSelectedShapeContentDetailDirty() || !CanMoveShape(shape, true)))
                        if (GUILayout.Button("Move Up", GUILayout.Width(90f)) && TryMoveSelectedShape(true, out _)) GUIUtility.ExitGUI();
                    using (new EditorGUI.DisabledScope(IsSelectedShapeContentDetailDirty() || !CanMoveShape(shape, false)))
                        if (GUILayout.Button("Move Down", GUILayout.Width(100f)) && TryMoveSelectedShape(false, out _)) GUIUtility.ExitGUI();
                    if (GUILayout.Button("Remove", GUILayout.Width(75f))) TryRemoveShape(out _);
                }
                string editedShapeName = EditorGUILayout.TextField("Shape Name", selectedShapeNameDraft);
                if (!string.Equals(editedShapeName, selectedShapeNameDraft, StringComparison.Ordinal))
                {
                    selectedShapeNameDraft = editedShapeName;
                    if (pendingShapeDraft == shape)
                    {
                        shape.SetShapeName(editedShapeName);
                        treeView?.Reload();
                    }
                }
                if (!layout.ShowsPriority) selectedShapePriorityDraft = 0;
                else selectedShapePriorityDraft = EditorGUILayout.IntField("Priority", selectedShapePriorityDraft);
                if (layout.ShowsTags)
                {
                    string[] availableTags = database.Registry.ShapeTags.Where(tag => !selectedShapeTagsDraft.Contains(tag)).ToArray();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Tags", GUILayout.Width(EditorGUIUtility.labelWidth));
                        int tagIndex = Array.IndexOf(availableTags, newShapeTag);
                        if (availableTags.Length != 0)
                        {
                            newShapeTag = availableTags[Math.Max(0, tagIndex)];
                            newShapeTag = availableTags[EditorGUILayout.Popup(GUIContent.none, Math.Max(0, tagIndex), availableTags)];
                        }
                        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(newShapeTag)))
                            if (GUILayout.Button("Add Tag", GUILayout.Width(80f))) { selectedShapeTagsDraft.Add(newShapeTag); newShapeTag = null; }
                    }
                    DrawShapeTagChips();
                }
                if (shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Morphs", EditorStyles.boldLabel);
                    var values = new List<MorphValue>();
                    foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(value => value != null))
                    {
                        MorphValue draft = selectedShapeMorphsDraft.FirstOrDefault(value => string.Equals(value.Target, axis.Name, StringComparison.Ordinal));
                        float numericValue = DrawMorphWeightControl(axis.Name, draft.Value);
                        values.Add(new MorphValue { Target = axis.Name, Value = numericValue });
                    }
                    selectedShapeMorphsDraft = values;
                    if (database.Registry.FigureAxes.Count == 0)
                        EditorGUILayout.HelpBox("Register Figure FBM or PBM axes before authoring a Morph Shape.", MessageType.Info);
                }
                else DrawShapePartsDetail(shape);
                shapeDetailScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsShapesDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.ExpandWidth(true), GUILayout.Height(DetailSaveButtonHeight))) TrySaveSelectedShapeDraft(out _);
        }

        private void DrawShapeTagChips()
        {
            float availableWidth = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 24f);
            float usedWidth = 0f;
            bool rowOpen = false;
            for (int index = 0; index < selectedShapeTagsDraft.Count; index++)
            {
                string tag = selectedShapeTagsDraft[index] ?? string.Empty;
                GUIContent content = new GUIContent(tag + "[x]");
                float chipWidth = Mathf.Max(42f, EditorStyles.miniButton.CalcSize(content).x + 6f);
                if (rowOpen && usedWidth + chipWidth > availableWidth)
                {
                    EditorGUILayout.EndHorizontal();
                    rowOpen = false;
                    usedWidth = 0f;
                }
                if (!rowOpen)
                {
                    EditorGUILayout.BeginHorizontal();
                    rowOpen = true;
                }
                if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(chipWidth)))
                {
                    selectedShapeTagsDraft.RemoveAt(index);
                    break;
                }
                usedWidth += chipWidth + 2f;
            }
            if (rowOpen) EditorGUILayout.EndHorizontal();
        }

        private void DrawShapePartsDetail(ShapeSyncDatabaseRegistry.ShapeEntry shape)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parts (authoring order)", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(shapePartsScrollPosition, GUILayout.ExpandHeight(true)))
            {
                for (int index = 0; index < selectedShapePartsDraft.Count; index++)
                {
                    ShapeSyncDatabaseRegistry.ShapeEntryDefinition part = selectedShapePartsDraft[index];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField((index + 1) + ". " + part.Kind, GUILayout.Width(120f));
                        if (GUILayout.Button("Up", GUILayout.Width(45f))) { TryMoveShapePart(index, true, out _); break; }
                        if (GUILayout.Button("Down", GUILayout.Width(50f))) { TryMoveShapePart(index, false, out _); break; }
                        if (GUILayout.Button("Remove", GUILayout.Width(70f))) { TryRemoveShapePart(index, out _); break; }
                    }
                    if (part.Kind == ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh)
                    {
                        ShapeSyncDatabaseRegistry.OutfitEntry[] meshOutfits = database.Registry.Outfits.Where(outfit => outfit != null && outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh).ToArray();
                        string[] meshOutfitIds = meshOutfits.Select(outfit => outfit.Identity).ToArray();
                        string[] meshOutfitNames = meshOutfits.Select(outfit => outfit.DisplayName).ToArray();
                        int selected = Array.IndexOf(meshOutfitIds, part.OutfitIdentity);
                        EditorGUI.BeginChangeCheck();
                        int next = EditorGUILayout.Popup("Outfit Mesh", selected, meshOutfitNames);
                        if (EditorGUI.EndChangeCheck() && next >= 0) TrySetShapePartMeshOutfit(index, meshOutfitIds[next], out _);
                    }
                    else DrawShapeMaterialTarget(index, part);
                }
                shapePartsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Mesh")) TryAddShapePart(ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh, out _);
                if (GUILayout.Button("Add Texture")) TryAddShapePart(ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture, out _);
                if (GUILayout.Button("Add Color")) TryAddShapePart(ShapeSyncDatabaseRegistry.ShapeEntryKind.Color, out _);
                if (GUILayout.Button("Add UVSet")) TryAddShapePart(ShapeSyncDatabaseRegistry.ShapeEntryKind.Uvset, out _);
            }
        }

        private static float DrawMorphWeightControl(string label, float value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
                return DynamicBoneBlendWeightField.DrawLayout(value);
            }
        }

        private void DrawShapeMaterialTarget(int index, ShapeSyncDatabaseRegistry.ShapeEntryDefinition part)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry[] meshOutfits = database.Registry.Outfits.Where(outfit => outfit != null && outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh).ToArray();
            string[] ownerIds = new[] { string.Empty }.Concat(meshOutfits.Select(outfit => outfit.Identity)).ToArray();
            string[] ownerNames = new[] { "Figure" }.Concat(meshOutfits.Select(outfit => outfit.DisplayName)).ToArray();
            int ownerIndex = Array.IndexOf(ownerIds, part.RegistryId ?? string.Empty);
            int selectedOwner;
            int selectedEntry;
            string ownerId;
            string[] entryNames;
            int entryIndex;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Target", GUILayout.Width(EditorGUIUtility.labelWidth));
                selectedOwner = EditorGUILayout.Popup(Math.Max(0, ownerIndex), ownerNames);
                ownerId = ownerIds[selectedOwner];
                entryNames = string.IsNullOrEmpty(ownerId)
                    ? database.Registry.MaterialEntries.Where(entry => entry != null).Select(entry => entry.LogicalName).ToArray()
                    : meshOutfits.FirstOrDefault(outfit => outfit.Identity == ownerId)?.MaterialEntries.Where(entry => entry != null).Select(entry => entry.LogicalName).ToArray() ?? Array.Empty<string>();
                entryIndex = Array.IndexOf(entryNames, part.ProxyEntry);
                selectedEntry = EditorGUILayout.Popup(Math.Max(0, entryIndex), entryNames);
            }
            if (EditorGUI.EndChangeCheck() && entryNames.Length != 0) TrySetShapePartMaterialTarget(index, ownerId, entryNames[selectedEntry], out _);
            if (part.Kind == ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture)
            {
                ShapeSyncDatabaseRegistry.TextureResourceEntry[] resources = database.Registry.TextureResources.Where(entry => entry != null).ToArray();
                string OwnerKey(ShapeSyncDatabaseRegistry.TextureResourceEntry resource) => ((int)resource.Owner.Scope) + "|" + resource.Owner.OutfitIdentity + "|" + resource.Owner.SourceShapeKey;
                string OwnerLabel(ShapeSyncDatabaseRegistry.TextureResourceEntry resource)
                {
                    if (resource.Owner.Scope == ShapeSyncDatabaseRegistry.TextureResourceOwnerScope.Figure)
                        return string.IsNullOrEmpty(resource.Owner.SourceShapeKey) ? "Figure" : resource.Owner.SourceShapeKey;
                    return database.Registry.Outfits.FirstOrDefault(outfit => outfit != null && outfit.Identity == resource.Owner.OutfitIdentity)?.DisplayName ?? resource.Owner.OutfitIdentity;
                }
                ShapeSyncDatabaseRegistry.TextureResourceEntry current = resources.FirstOrDefault(resource => resource.LogicalName == part.TextureResourceName);
                string[] ownerKeys = resources.Select(OwnerKey).Distinct().ToArray();
                string[] ownerLabels = resources.GroupBy(OwnerKey).Select(group => OwnerLabel(group.First())).ToArray();
                int ownerSelection = current == null ? 0 : Array.IndexOf(ownerKeys, OwnerKey(current));
                int selectedTextureOwner;
                int selectedTexture;
                string[] textures;
                int textureIndex;
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Texture", GUILayout.Width(EditorGUIUtility.labelWidth));
                    selectedTextureOwner = ownerKeys.Length == 0 ? -1 : EditorGUILayout.Popup(Math.Max(0, ownerSelection), ownerLabels);
                    ShapeSyncDatabaseRegistry.TextureResourceEntry[] ownerResources = selectedTextureOwner < 0 ? Array.Empty<ShapeSyncDatabaseRegistry.TextureResourceEntry>() : resources.Where(resource => OwnerKey(resource) == ownerKeys[selectedTextureOwner]).ToArray();
                    textures = ownerResources.Select(resource => resource.LogicalName).ToArray();
                    textureIndex = Array.IndexOf(textures, part.TextureResourceName);
                    selectedTexture = textures.Length == 0 ? -1 : EditorGUILayout.Popup(Math.Max(0, textureIndex), textures);
                }
                bool colorize = part.UseColorize;
                Color color = part.Color;
                using (new EditorGUILayout.HorizontalScope())
                {
                    colorize = EditorGUILayout.ToggleLeft("Use Colorize", colorize, GUILayout.Width(EditorGUIUtility.labelWidth + 80f));
                    using (new EditorGUI.DisabledScope(!colorize))
                        color = EditorGUILayout.ColorField(GUIContent.none, color);
                }
                if (EditorGUI.EndChangeCheck() && selectedTexture >= 0 && (selectedTexture != textureIndex || colorize != part.UseColorize || color != (Color)part.Color))
                    TrySetShapePartTexture(index, textures[selectedTexture], colorize, color, out _);
            }
            if (part.Kind == ShapeSyncDatabaseRegistry.ShapeEntryKind.Color)
            {
                Color color = EditorGUILayout.ColorField("Color", part.Color);
                if (color != (Color)part.Color) TrySetShapePartColor(index, color, out _);
            }
            if (part.Kind == ShapeSyncDatabaseRegistry.ShapeEntryKind.Uvset)
            {
                Vector2 scale = new Vector2(part.ScaleX, part.ScaleY);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("UV Scale", GUILayout.Width(EditorGUIUtility.labelWidth));
                    EditorGUILayout.LabelField("X", GUILayout.Width(14f));
                    scale.x = EditorGUILayout.FloatField(scale.x);
                    EditorGUILayout.LabelField("Y", GUILayout.Width(14f));
                    scale.y = EditorGUILayout.FloatField(scale.y);
                }
                Vector2 offset = new Vector2(part.OffsetX, part.OffsetY);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("UV Offset", GUILayout.Width(EditorGUIUtility.labelWidth));
                    EditorGUILayout.LabelField("X", GUILayout.Width(14f));
                    offset.x = EditorGUILayout.FloatField(offset.x);
                    EditorGUILayout.LabelField("Y", GUILayout.Width(14f));
                    offset.y = EditorGUILayout.FloatField(offset.y);
                }
                if (scale.x != part.ScaleX || scale.y != part.ScaleY || offset.x != part.OffsetX || offset.y != part.OffsetY)
                    TrySetShapePartUv(index, scale.x, scale.y, offset.x, offset.y, out _);
            }
            EditorGUILayout.HelpBox("Target selection accepts Figure or Mesh Outfit Material Entries only.", MessageType.None);
        }

        private bool TryAddShape(ShapeSyncDatabaseRegistry.ShapeKind kind, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database?.Registry == null) { saveDiagnostic = EmptyDatabaseMessage; diagnostic = saveDiagnostic; return false; }
            if (pendingShapeDraft != null)
            { saveDiagnostic = "Finish or discard the current Shape draft before creating another Shape."; diagnostic = saveDiagnostic; return false; }
            if (string.IsNullOrWhiteSpace(newShapeId) || newShapeId.Any(char.IsWhiteSpace))
            { saveDiagnostic = "Shape Id must not be empty or contain whitespace."; diagnostic = saveDiagnostic; return false; }
            if (database.Registry.Shapes.Any(entry => entry != null && string.Equals(entry.ShapeId, newShapeId, StringComparison.Ordinal)))
            { saveDiagnostic = "Shape Id already exists: " + newShapeId; diagnostic = saveDiagnostic; return false; }
            if (!Enum.IsDefined(typeof(ShapeSyncDatabaseRegistry.ShapeKind), kind))
            { saveDiagnostic = "Shape kind is invalid."; diagnostic = saveDiagnostic; return false; }
            pendingShapeDraft = new ShapeSyncDatabaseRegistry.ShapeEntry(newShapeId, newShapeName, kind, 0, Array.Empty<string>());
            selectedShapeId = pendingShapeDraft.ShapeId;
            selectedShapeNameDraft = pendingShapeDraft.ShapeName;
            selectedShapePriorityDraft = 0;
            selectedShapeTagsDraft = new List<string>();
            selectedShapeMorphsDraft = CreateMorphDraft(pendingShapeDraft);
            selectedShapePartsDraft = new List<ShapeSyncDatabaseRegistry.ShapeEntryDefinition>();
            acceptedShapeNameDraft = "\u0001" + pendingShapeDraft.ShapeId;
            acceptedShapeTagsDraft = new List<string>();
            shapesDetailView = ShapesDetailView.Shape;
            newShapeId = null;
            newShapeName = null;
            treeView?.Reload();
            treeView?.SelectShapeId(selectedShapeId);
            diagnostic = null;
            return true;
        }

        private bool TrySaveShapeTags(out string saveDiagnostic)
        {
            return TryEditShapeRegistry((ShapeSyncDatabaseRegistry registry, out string detail) => registry.TrySetShapeTags(shapeTagsDraft, out detail), out saveDiagnostic);
        }

        private bool IsShapeTagsDetailDirty()
        {
            if (selectedSection != Section.Shapes || shapesDetailView != ShapesDetailView.Tags || database?.Registry == null) return false;
            return !shapeTagsDraft.SequenceEqual(database.Registry.ShapeTags, StringComparer.Ordinal);
        }

        private bool TrySaveShape(string id, string name, int priority, IReadOnlyList<string> tags, out string saveDiagnostic)
        {
            return TryEditShapeRegistry((ShapeSyncDatabaseRegistry registry, out string detail) => registry.TryUpdateShape(id, name, priority, tags, out detail), out saveDiagnostic);
        }

        private void EnsureSelectedShapeDraft(ShapeSyncDatabaseRegistry.ShapeEntry shape)
        {
            if (shape == null || string.Equals(acceptedShapeNameDraft, "\u0001" + shape.ShapeId, StringComparison.Ordinal)) return;
            selectedShapeNameDraft = shape.ShapeName;
            selectedShapePriorityDraft = acceptedShapePriorityDraft = shape.Priority;
            selectedShapeTagsDraft = shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? new List<string>() : new List<string>(shape.Tags);
            acceptedShapeTagsDraft = new List<string>(selectedShapeTagsDraft);
            selectedShapeMorphsDraft = CreateMorphDraft(shape);
            selectedShapePartsDraft = shape.Parts.Select(part => part.Clone()).ToList();
            acceptedShapeNameDraft = "\u0001" + shape.ShapeId;
        }

        private bool IsShapesDetailDirty()
        {
            if (selectedSection != Section.Shapes) return false;
            return IsShapeOrderDraftDirty() || IsSelectedShapeContentDetailDirty();
        }

        private bool IsSelectedShapeContentDetailDirty()
        {
            if (selectedSection != Section.Shapes || string.IsNullOrEmpty(selectedShapeId)) return false;
            ShapeSyncDatabaseRegistry.ShapeEntry shape = GetSelectedShapeEntry();
            if (shape == null) return false;
            if (pendingShapeDraft == shape) return true;
            return !string.Equals(selectedShapeNameDraft, shape.ShapeName, StringComparison.Ordinal)
                || selectedShapePriorityDraft != shape.Priority
                || (shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph && !selectedShapeTagsDraft.SequenceEqual(shape.Tags, StringComparer.Ordinal))
                || !MorphValuesEqual(selectedShapeMorphsDraft, shape.Morphs)
                || selectedShapePartsDraft.Count != shape.Parts.Count || selectedShapePartsDraft.Where((part, index) => !part.ContentEquals(shape.Parts[index])).Any();
        }

        private static bool MorphValuesEqual(IReadOnlyList<MorphValue> left, IReadOnlyList<MorphValue> right)
        {
            if (left == null || right == null) return left == right;
            var targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (MorphValue value in left) targets.Add(value.Target);
            foreach (MorphValue value in right) targets.Add(value.Target);
            foreach (string target in targets)
            {
                MorphValue leftValue = left.FirstOrDefault(value => string.Equals(value.Target, target, StringComparison.Ordinal));
                MorphValue rightValue = right.FirstOrDefault(value => string.Equals(value.Target, target, StringComparison.Ordinal));
                if (!Mathf.Approximately(leftValue.Value, rightValue.Value)) return false;
            }
            return true;
        }

        private List<MorphValue> CreateMorphDraft(ShapeSyncDatabaseRegistry.ShapeEntry shape)
        {
            if (shape == null || shape.Kind != ShapeSyncDatabaseRegistry.ShapeKind.Morph)
                return shape == null ? new List<MorphValue>() : shape.Morphs.Select(value => new MorphValue { Target = value.Target, Value = value.Value }).ToList();
            return database.Registry.FigureAxes.Where(axis => axis != null)
                .Select(axis =>
                {
                    MorphValue saved = shape.Morphs.FirstOrDefault(value => string.Equals(value.Target, axis.Name, StringComparison.Ordinal));
                    return new MorphValue { Target = axis.Name, Value = saved.Value };
                }).ToList();
        }

        private bool TrySaveSelectedShapeDraft(out string saveDiagnostic)
        {
            if (IsShapeOrderDraftDirty() && !TrySaveShapeOrderDraft(out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            if (string.IsNullOrEmpty(selectedShapeId))
            {
                saveDiagnostic = null;
                diagnostic = null;
                return true;
            }
            ShapeSyncDatabaseRegistry.ShapeEntry shape = GetSelectedShapeEntry();
            if (shape == null) { saveDiagnostic = "Select an existing Shape first."; diagnostic = saveDiagnostic; return false; }
            if (pendingShapeDraft == shape)
            {
                if (string.IsNullOrWhiteSpace(selectedShapeId) || selectedShapeId.Any(char.IsWhiteSpace)
                    || database.Registry.Shapes.Any(entry => entry != null && string.Equals(entry.ShapeId, selectedShapeId, StringComparison.Ordinal)))
                { saveDiagnostic = "Shape Id must be unique and must not be empty or contain whitespace."; diagnostic = saveDiagnostic; return false; }
                IReadOnlyList<string> pendingTags = shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? Array.Empty<string>() : selectedShapeTagsDraft;
                if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Add Shape",
                    (ShapeSyncDatabaseRegistry registry, out string detail) =>
                    {
                        if (!registry.TryAddShape(selectedShapeId, selectedShapeNameDraft, shape.Kind, selectedShapePriorityDraft, pendingTags, out detail)) return false;
                        return registry.TryUpdateShapeAndContents(selectedShapeId, selectedShapeNameDraft, selectedShapePriorityDraft, pendingTags,
                            shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? selectedShapeMorphsDraft : Array.Empty<MorphValue>(), selectedShapePartsDraft, out detail);
                    }, out saveDiagnostic))
                { diagnostic = saveDiagnostic; return false; }
                pendingShapeDraft = null;
                acceptedShapeNameDraft = null;
                ResetShapeOrderDraft();
                EnsureSelectedShapeDraft(database.Registry.Shapes.First(entry => entry != null && string.Equals(entry.ShapeId, selectedShapeId, StringComparison.Ordinal)));
                treeView?.Reload();
                treeView?.SelectShapeId(selectedShapeId);
                diagnostic = null;
                return true;
            }
            bool hasUnpersistedExplicitMorphValues = shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph
                && selectedShapeMorphsDraft.Any(value => !shape.Morphs.Any(saved => string.Equals(saved.Target, value.Target, StringComparison.Ordinal)));
            if (!IsSelectedShapeContentDetailDirty() && !hasUnpersistedExplicitMorphValues)
            {
                saveDiagnostic = null;
                diagnostic = null;
                if (!string.IsNullOrEmpty(selectedShapeId)) treeView?.SelectShapeId(selectedShapeId);
                return true;
            }
            IReadOnlyList<string> tags = shape != null && shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? Array.Empty<string>() : selectedShapeTagsDraft;
            bool saved = TryEditShapeRegistry((ShapeSyncDatabaseRegistry registry, out string detail) => registry.TryUpdateShapeAndContents(selectedShapeId, selectedShapeNameDraft, selectedShapePriorityDraft, tags, selectedShapeMorphsDraft, selectedShapePartsDraft, out detail), out saveDiagnostic);
            if (saved)
            {
                ShapeSyncDatabaseRegistry.ShapeEntry persisted = database?.Registry?.Shapes.FirstOrDefault(value => value != null && value.ShapeId == selectedShapeId);
                if (persisted != null)
                {
                    selectedShapeNameDraft = persisted.ShapeName;
                    selectedShapePriorityDraft = acceptedShapePriorityDraft = persisted.Priority;
                    selectedShapeTagsDraft = persisted.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? new List<string>() : new List<string>(persisted.Tags);
                    acceptedShapeTagsDraft = new List<string>(selectedShapeTagsDraft);
                    selectedShapeMorphsDraft = CreateMorphDraft(persisted);
                    selectedShapePartsDraft = persisted.Parts.Select(part => part.Clone()).ToList();
                    acceptedShapeNameDraft = "\u0001" + persisted.ShapeId;
                }
                treeView?.SelectShapeId(selectedShapeId);
            }
            return saved;
        }

        private void DiscardSelectedShapeDraft()
        {
            ResetShapeOrderDraft();
            if (pendingShapeDraft != null && string.Equals(pendingShapeDraft.ShapeId, selectedShapeId, StringComparison.Ordinal))
            {
                pendingShapeDraft = null;
                selectedShapeId = null;
                shapesDetailView = ShapesDetailView.Root;
                acceptedShapeNameDraft = null;
                selectedShapeNameDraft = null;
                selectedShapeTagsDraft.Clear();
                selectedShapeMorphsDraft.Clear();
                selectedShapePartsDraft.Clear();
                treeView?.SelectShapesRoot();
                return;
            }
            ShapeSyncDatabaseRegistry.ShapeEntry shape = database?.Registry?.Shapes.FirstOrDefault(value => value != null && value.ShapeId == selectedShapeId);
            if (shape == null) return;
            selectedShapeNameDraft = shape.ShapeName;
            selectedShapePriorityDraft = acceptedShapePriorityDraft = shape.Priority;
            selectedShapeTagsDraft = shape.Kind == ShapeSyncDatabaseRegistry.ShapeKind.Morph ? new List<string>() : new List<string>(shape.Tags);
            acceptedShapeTagsDraft = new List<string>(selectedShapeTagsDraft);
            selectedShapeMorphsDraft = CreateMorphDraft(shape);
            selectedShapePartsDraft = shape.Parts.Select(part => part.Clone()).ToList();
        }

        private bool IsShapeOrderDraftDirty()
            => !shapeOrderDraft.SequenceEqual(acceptedShapeOrderDraft, StringComparer.Ordinal);

        private void ResetShapeOrderDraft()
        {
            shapeOrderDraft = database?.Registry?.Shapes.Where(entry => entry != null).Select(entry => entry.ShapeId).ToList()
                ?? new List<string>();
            acceptedShapeOrderDraft = new List<string>(shapeOrderDraft);
        }

        private bool CanMoveShape(ShapeSyncDatabaseRegistry.ShapeEntry shape, bool moveUp)
        {
            if (shape == null || database?.Registry == null) return false;
            ShapeSyncDatabaseRegistry.ShapeEntry[] sameKind = GetShapesForTreeView()
                .Where(entry => entry != null && entry.Kind == shape.Kind).ToArray();
            int index = Array.IndexOf(sameKind, shape);
            return moveUp ? index > 0 : index >= 0 && index < sameKind.Length - 1;
        }

        private bool TryMoveSelectedShape(bool moveUp, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            ShapeSyncDatabaseRegistry.ShapeEntry shape = database?.Registry?.Shapes.FirstOrDefault(entry => entry != null && entry.ShapeId == selectedShapeId);
            if (shape == null) { saveDiagnostic = "Select an existing Shape first."; diagnostic = saveDiagnostic; return false; }
            if (IsSelectedShapeContentDetailDirty()) { saveDiagnostic = "Save or discard Shape Detail changes before changing TreeView order."; diagnostic = saveDiagnostic; return false; }
            int currentIndex = shapeOrderDraft.IndexOf(shape.ShapeId);
            if (currentIndex < 0)
            {
                saveDiagnostic = "Shape order draft does not contain the selected Shape.";
                diagnostic = saveDiagnostic;
                return false;
            }
            int step = moveUp ? -1 : 1;
            int targetIndex = -1;
            for (int index = currentIndex + step; index >= 0 && index < shapeOrderDraft.Count; index += step)
            {
                ShapeSyncDatabaseRegistry.ShapeEntry candidate = database.Registry.Shapes.FirstOrDefault(entry => entry != null && string.Equals(entry.ShapeId, shapeOrderDraft[index], StringComparison.Ordinal));
                if (candidate != null && candidate.Kind == shape.Kind) { targetIndex = index; break; }
            }
            if (targetIndex < 0)
            {
                saveDiagnostic = moveUp ? "Shape is already first in its TreeView group." : "Shape is already last in its TreeView group.";
                diagnostic = saveDiagnostic;
                return false;
            }
            shapeOrderDraft.RemoveAt(currentIndex);
            shapeOrderDraft.Insert(Math.Min(targetIndex, shapeOrderDraft.Count), shape.ShapeId);
            treeView?.SelectShapeId(shape.ShapeId);
            diagnostic = null;
            return true;
        }

        private bool TrySaveShapeOrderDraft(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database?.Registry == null) { saveDiagnostic = "Shape order save requires an open Database."; return false; }
            ShapeSyncDatabaseRegistry.ShapeEntry[] stored = database.Registry.Shapes.Where(entry => entry != null).ToArray();
            if (shapeOrderDraft.Count != stored.Length
                || shapeOrderDraft.Distinct(StringComparer.Ordinal).Count() != stored.Length
                || stored.Any(entry => !shapeOrderDraft.Contains(entry.ShapeId)))
            {
                saveDiagnostic = "Shape order draft does not match the current Database Shapes.";
                return false;
            }

            if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Reorder ShapeSync Shapes",
                (ShapeSyncDatabaseRegistry registry, out string detail) =>
                {
                    for (int desiredIndex = 0; desiredIndex < shapeOrderDraft.Count; desiredIndex++)
                    {
                        string desiredShapeId = shapeOrderDraft[desiredIndex];
                        int currentIndex = registry.Shapes.ToList().FindIndex(entry => entry != null && string.Equals(entry.ShapeId, desiredShapeId, StringComparison.Ordinal));
                        if (currentIndex < 0) { detail = "Shape order save could not resolve: " + desiredShapeId; return false; }
                        while (currentIndex > desiredIndex)
                        {
                            if (!registry.TryMoveShape(desiredShapeId, true, out string moveDiagnostic))
                            {
                                detail = moveDiagnostic ?? "Shape order could not be saved.";
                                return false;
                            }
                            currentIndex--;
                        }
                    }
                    detail = null;
                    return true;
                }, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }

            ResetShapeOrderDraft();
            treeView?.Reload();
            if (!string.IsNullOrEmpty(selectedShapeId)) treeView?.SelectShapeId(selectedShapeId);
            diagnostic = null;
            return true;
        }

        private bool TryRemoveShape(out string saveDiagnostic)
        {
            string removed = selectedShapeId;
            if (pendingShapeDraft != null && string.Equals(pendingShapeDraft.ShapeId, removed, StringComparison.Ordinal))
            {
                pendingShapeDraft = null;
                selectedShapeId = null;
                shapesDetailView = ShapesDetailView.Root;
                ResetShapeOrderDraft();
                treeView?.SelectShapesRoot();
                saveDiagnostic = null;
                diagnostic = null;
                return true;
            }
            bool result = TryEditShapeRegistry((ShapeSyncDatabaseRegistry registry, out string detail) => registry.TryRemoveShape(removed, out detail), out saveDiagnostic);
            if (result)
            {
                selectedShapeId = null;
                shapesDetailView = ShapesDetailView.Root;
                ResetShapeOrderDraft();
                treeView?.SelectShapesRoot();
            }
            return result;
        }

        private bool TryAddShapePart(ShapeSyncDatabaseRegistry.ShapeEntryKind kind, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (string.IsNullOrEmpty(selectedShapeId)) { saveDiagnostic = "Select a Shape first."; return false; }
            if (database?.Registry == null) { saveDiagnostic = EmptyDatabaseMessage; diagnostic = saveDiagnostic; return false; }
            if (!TryCreateShapePartDraft(kind, out ShapeSyncDatabaseRegistry.ShapeEntryDefinition draft, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            selectedShapePartsDraft.Add(draft);
            diagnostic = null;
            return true;
        }

        private bool TryCreateShapePartDraft(ShapeSyncDatabaseRegistry.ShapeEntryKind kind,
            out ShapeSyncDatabaseRegistry.ShapeEntryDefinition draft, out string saveDiagnostic)
        {
            draft = null;
            saveDiagnostic = null;
            if (!Enum.IsDefined(typeof(ShapeSyncDatabaseRegistry.ShapeEntryKind), kind))
            { saveDiagnostic = "Shape entry kind is invalid."; return false; }

            draft = new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(kind);
            if (kind == ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh)
            {
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.FirstOrDefault(entry => entry != null
                    && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && !string.IsNullOrWhiteSpace(entry.Identity));
                if (outfit == null)
                { saveDiagnostic = "Mesh entry cannot be added because no Mesh Outfit target is registered."; draft = null; return false; }
                draft.SetMeshOutfit(outfit.Identity);
                return true;
            }

            if (!TryGetFirstShapeMaterialTarget(out string registryId, out string proxyEntry))
            {
                saveDiagnostic = "Shape entry cannot be added because no Material Entry target is registered.";
                draft = null;
                return false;
            }
            draft.SetMaterialTarget(registryId, proxyEntry);
            if (kind == ShapeSyncDatabaseRegistry.ShapeEntryKind.Texture)
            {
                ShapeSyncDatabaseRegistry.TextureResourceEntry resource = database.Registry.TextureResources.FirstOrDefault(entry => entry != null
                    && !string.IsNullOrWhiteSpace(entry.LogicalName));
                if (resource == null)
                {
                    saveDiagnostic = "Texture entry cannot be added because no Database Texture resource is registered.";
                    draft = null;
                    return false;
                }
                draft.SetTexture(resource.LogicalName, false, draft.Color);
            }
            return true;
        }

        private bool TryGetFirstShapeMaterialTarget(out string registryId, out string proxyEntry)
        {
            registryId = string.Empty;
            proxyEntry = null;
            ShapeSyncDatabaseRegistry.MaterialEntry figureEntry = database.Registry.MaterialEntries.FirstOrDefault(entry => entry != null
                && !string.IsNullOrWhiteSpace(entry.LogicalName));
            if (figureEntry != null)
            {
                proxyEntry = figureEntry.LogicalName;
                return true;
            }
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database.Registry.Outfits.FirstOrDefault(entry => entry != null
                && entry.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh
                && entry.MaterialEntries.Any(material => material != null && !string.IsNullOrWhiteSpace(material.LogicalName)));
            ShapeSyncDatabaseRegistry.OutfitMaterialEntry outfitEntry = outfit?.MaterialEntries.FirstOrDefault(entry => entry != null
                && !string.IsNullOrWhiteSpace(entry.LogicalName));
            if (outfit == null || outfitEntry == null) return false;
            registryId = outfit.Identity;
            proxyEntry = outfitEntry.LogicalName;
            return true;
        }

        private bool TrySaveShapeMorphs(string shapeId, IReadOnlyList<MorphValue> values, out string saveDiagnostic)
        {
            return TryEditShapeRegistry((ShapeSyncDatabaseRegistry registry, out string detail) => registry.TrySetShapeMorphs(shapeId, values, out detail), out saveDiagnostic);
        }

        private bool TrySetShapePartMeshOutfit(int index, string outfitIdentity, out string saveDiagnostic)
        {
            saveDiagnostic = null; if (index < 0 || index >= selectedShapePartsDraft.Count) { saveDiagnostic = "Shape Part was not found."; return false; }
            selectedShapePartsDraft[index].SetMeshOutfit(outfitIdentity); return true;
        }

        private bool TrySetShapePartMaterialTarget(int index, string registryId, string materialEntryName, out string saveDiagnostic)
        {
            saveDiagnostic = null; if (index < 0 || index >= selectedShapePartsDraft.Count) { saveDiagnostic = "Shape Part was not found."; return false; }
            selectedShapePartsDraft[index].SetMaterialTarget(registryId, materialEntryName); return true;
        }

        private bool TrySetShapePartTexture(int index, string textureResourceName, bool useColorize, Color color, out string saveDiagnostic)
        {
            saveDiagnostic = null; if (index < 0 || index >= selectedShapePartsDraft.Count) { saveDiagnostic = "Shape Part was not found."; return false; }
            selectedShapePartsDraft[index].SetTexture(textureResourceName, useColorize, color); return true;
        }

        private bool TrySetShapePartColor(int index, Color color, out string saveDiagnostic)
        {
            saveDiagnostic = null; if (index < 0 || index >= selectedShapePartsDraft.Count) { saveDiagnostic = "Shape Part was not found."; return false; }
            selectedShapePartsDraft[index].SetColor(color); return true;
        }

        private bool TrySetShapePartUv(int index, float scaleX, float scaleY, float offsetX, float offsetY, out string saveDiagnostic)
        {
            saveDiagnostic = null; if (index < 0 || index >= selectedShapePartsDraft.Count) { saveDiagnostic = "Shape Part was not found."; return false; }
            selectedShapePartsDraft[index].SetUv(scaleX, scaleY, offsetX, offsetY); return true;
        }

        private bool TryRemoveShapePart(int index, out string saveDiagnostic)
        {
            saveDiagnostic = null; if (index < 0 || index >= selectedShapePartsDraft.Count) { saveDiagnostic = "Shape Part was not found."; return false; }
            selectedShapePartsDraft.RemoveAt(index); return true;
        }

        private bool TryMoveShapePart(int index, bool moveUp, out string saveDiagnostic)
        {
            saveDiagnostic = null; int target = index + (moveUp ? -1 : 1);
            if (index < 0 || index >= selectedShapePartsDraft.Count || target < 0 || target >= selectedShapePartsDraft.Count) { saveDiagnostic = moveUp ? "Shape Part is already first." : "Shape Part is already last."; return false; }
            ShapeSyncDatabaseRegistry.ShapeEntryDefinition part = selectedShapePartsDraft[index]; selectedShapePartsDraft[index] = selectedShapePartsDraft[target]; selectedShapePartsDraft[target] = part; return true;
        }

        private delegate bool ShapeRegistryEdit(ShapeSyncDatabaseRegistry registry, out string diagnostic);

        private bool TryEditShapeRegistry(ShapeRegistryEdit edit, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database?.Registry == null) { saveDiagnostic = EmptyDatabaseMessage; diagnostic = saveDiagnostic; return false; }
            if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Edit ShapeSync Registry",
                (ShapeSyncDatabaseRegistry registry, out string registryDiagnostic) =>
            {
                return edit(registry, out registryDiagnostic);
            }, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            ResetShapeTagsDraft();
            diagnostic = null;
            return true;
        }

        private void DrawFigureDetail()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(figureDetailScrollPosition, GUILayout.ExpandHeight(true)))
            {
                GUILayout.Label("Figure", EditorStyles.boldLabel);
                string editedFigureName = EditorGUILayout.TextField("Figure Name", figureName);
                if (!string.Equals(editedFigureName, figureName, StringComparison.Ordinal))
                {
                    figureName = editedFigureName;
                    ResolveDatabaseFigurePrefab();
                }
                GameObject selectedFigurePrefab = (GameObject)EditorGUILayout.ObjectField("Figure prefab", figurePrefab, typeof(GameObject), false);
                if (selectedFigurePrefab != figurePrefab) AssignFigurePrefabFromUi(selectedFigurePrefab);
                if (figurePrefab != null && AdmitFigure(figurePrefab, out ShapeSyncFigureImportAdmission admission, out _))
                {
                    EditorGUILayout.HelpBox("Prefab contains " + admission.SourceRenderers.Count + " SkinnedMesh Renderers. They will be merged.\n" + string.Join("\n", System.Linq.Enumerable.Select(admission.SourceRenderers, (renderer, index) => index + ": " + renderer.name)), MessageType.Info);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Prefab on Database", databaseFigurePrefab, typeof(GameObject), false);
                    using (new EditorGUI.DisabledScope(!CanExportDatabaseFigure()))
                        if (GUILayout.Button(FigureExportButtonLabel, GUILayout.Width(80f))) TryExportDatabaseFigureWithDialog(out _);
                }
                if (database != null && database.Registry != null)
                    pcmSlots = EditorGUILayout.IntField("PCM Slots", pcmSlots);
                if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
                figureDetailScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!CanSaveFigure())) if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveFigure(out _);
        }

        private void DrawNormalsDetail()
        {
            GUILayout.Label("Normals", EditorStyles.boldLabel);
            EnsureFigureNormalEntryDrafts();
            bool canAdd = databaseFigurePrefab != null && GetAvailableFigureNormalEntryMaterialNames(null).Length != 0;
            using (new EditorGUI.DisabledScope(!canAdd))
                if (GUILayout.Button("Add Normal Entry")) TryAddFigureNormalEntry();

            using (var scroll = new EditorGUILayout.ScrollViewScope(figureNormalEntriesScrollPosition, GUILayout.ExpandHeight(true)))
            {
                if (figureNormalEntryMaterialNames.Count == 0)
                    EditorGUILayout.HelpBox("Add a Normal Entry for a Material Entry to configure its Base Normal.", MessageType.Info);
                for (int index = 0; index < figureNormalEntryMaterialNames.Count; index++)
                {
                    string currentName = figureNormalEntryMaterialNames[index];
                    string[] choices = GetAvailableFigureNormalEntryMaterialNames(currentName);
                    int selectedIndex = Array.IndexOf(choices, currentName);
                    int nextIndex = EditorGUILayout.Popup("Material Entry", Math.Max(0, selectedIndex), choices);
                    string nextName = choices[nextIndex];
                    if (!string.Equals(currentName, nextName, StringComparison.Ordinal)) ReplaceFigureNormalEntry(index, nextName);

                    MaterialEntryDraft material = FindMaterialDraft(figureNormalEntryMaterialNames[index]);
                    if (material == null) continue;
                    NormalDraft normal = GetOrCreateNormalDraft(material, ShapeSyncDatabaseRegistry.BaseShapeKey);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        normal.Texture = (Texture)EditorGUILayout.ObjectField("Normal", normal.Texture, typeof(Texture), false);
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(130)))
                        {
                            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Normal Preview", normal.Texture, typeof(Texture), false, GUILayout.Width(130));
                            if (GUILayout.Button("Pick From Model")) normal.Texture = ResolveNormalFromMaterial(material.SourceMaterial);
                            if (GUILayout.Button("Remove")) { TryRemoveFigureNormalEntry(index); break; }
                        }
                    }
                }
                figureNormalEntriesScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!CanSaveNormals()))
                if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveNormals(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawExtraMorphsDetail()
        {
            GUILayout.Label("Extra Morphs", EditorStyles.boldLabel);
            string error = null;
            if (database == null || database.Registry == null || !database.Registry.TryGetCommonFbmRawBlendShapeNamesForOpen(database, out string[] candidates, out error)) { EditorGUILayout.HelpBox(error ?? "Register FBMs first.", MessageType.Info); return; }
            using (var scroll = new EditorGUILayout.ScrollViewScope(materialsScrollPosition))
            {
                var selected = new HashSet<string>(keptRawMorphs, StringComparer.Ordinal);
                foreach (string candidate in candidates) { if (EditorGUILayout.ToggleLeft(candidate, selected.Contains(candidate))) selected.Add(candidate); else selected.Remove(candidate); }
                keptRawMorphs = selected.OrderBy(value => value, StringComparer.Ordinal).ToList();
                materialsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsExtraMorphsDetailDirty())) if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveExtraMorphs(out _);
        }

        private void DrawMaterialsDetail()
        {
            GUILayout.Label("Materials", EditorStyles.boldLabel);
            if (database == null || database.Registry == null)
            {
                EditorGUILayout.HelpBox("Select a ShapeSync Database.", MessageType.Info);
                return;
            }
            EnsureMaterialDrafts();
            using (var scroll = new EditorGUILayout.ScrollViewScope(materialsScrollPosition))
            {
                foreach (MaterialEntryDraft draft in materialDrafts)
                {
                    draft.EntryName = EditorGUILayout.TextField("Entry Name", draft.EntryName);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField("Texture Preview", draft.PreviewTexture, typeof(Texture), false);
                        EditorGUILayout.TextField("Material Name", draft.SourceMaterial == null ? string.Empty : draft.SourceMaterial.name);
                    }
                    EditorGUILayout.Space();
                }
                materialsScrollPosition = scroll.scrollPosition;
            }
            if (materialDrafts.Count == 0) EditorGUILayout.HelpBox("A saved Base Figure with a supported Material is required.", MessageType.Info);
            if (!string.IsNullOrEmpty(materialDraftDiagnostic)) EditorGUILayout.HelpBox(materialDraftDiagnostic, MessageType.Warning);
            using (new EditorGUI.DisabledScope(!CanSaveMaterialEntries())) if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveMaterialEntries(out _);
        }

        private bool TrySaveMaterialEntries(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            EnsureMaterialDrafts();
            if (database == null || materialDrafts.Count == 0) { saveDiagnostic = "Material Entry save requires a saved Base Figure with supported Materials."; diagnostic = saveDiagnostic; return false; }
            if (database.Registry.MaterialEntries.Count != 0)
            {
                var renames = materialDrafts.Select(draft => new ShapeSyncMaterialEntryImport.Rename(draft.OriginalEntryName, draft.EntryName)).ToArray();
                if (renames.All(rename => string.Equals(rename.CurrentName, rename.NextName, StringComparison.Ordinal))) { diagnostic = null; return true; }
                bool renameTextures = false;
                if (!IsBatchMode())
                {
                    try { renameTextures = ConfirmTextureRename("Rename Textures", "Rename Textures to [FigureName]_[EntryName]?", "Yes", "No"); }
                    catch (Exception exception) { saveDiagnostic = "Could not confirm Texture rename: " + exception.Message; diagnostic = saveDiagnostic; return false; }
                }
                if (!ShapeSyncMaterialEntryImport.TryRenameDirect(database, renames, renameTextures, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                ApplyMaterialRenamesToDrafts(renames);
                AcceptMaterialDraft();
                diagnostic = null;
                return true;
            }
            var admissions = new List<ShapeSyncMaterialAdapterResolver.Admission>();
            try
            {
                foreach (MaterialEntryDraft draft in materialDrafts)
                {
                    if (!ShapeSyncMaterialAdapterResolver.TryAdmit(database, draft.EntryName, draft.Renderer, draft.MaterialSlot, draft.SourceMaterial, out ShapeSyncMaterialAdapterResolver.Admission admission, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    admissions.Add(admission);
                }
                string databasePath = AssetDatabase.GetAssetPath(database);
                bool renameTextures = false;
                if (!IsBatchMode())
                {
                    try { renameTextures = ConfirmTextureRename("Rename Textures", "Rename Textures to [FigureName]_[EntryName]?", "Yes", "No"); }
                    catch (Exception exception) { saveDiagnostic = "Could not confirm Texture rename: " + exception.Message; diagnostic = saveDiagnostic; return false; }
                }
                if (!SaveMaterialEntriesWithTextureRename(databasePath, admissions, renameTextures, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                if (!TrySetDatabaseAtPath(databasePath, out saveDiagnostic)) return false;
                diagnostic = null;
                return true;
            }
            finally
            {
                foreach (ShapeSyncMaterialAdapterResolver.Admission admission in admissions) admission.Dispose();
            }
        }

        private Texture ResolveMaterialEntryPreview(ShapeSyncDatabaseRegistry.MaterialEntry entry)
        {
            if (entry == null || entry.Material == null || entry.Adapter == null) return null;
            // Registry resource-name order is an ownership/reference detail.  The preview
            // is specifically the adapter-declared BaseColor Texture, so a resource rename
            // (or another Texture preceding it in the registry) must not change it.
            foreach (MaterialPropertyBindingTemplate binding in entry.Adapter.AssignmentTemplates)
            {
                if (binding.valueSource != MaterialPropertyValueSource.BaseColorTexture || binding.writeKind != MaterialPropertyWriteKind.Texture) continue;
                return entry.Material.HasProperty(binding.propertyName) ? entry.Material.GetTexture(binding.propertyName) : null;
            }
            return null;
        }

        private void DrawFigureAxisDetail(ShapeSyncDatabaseRegistry.FigureAxisKind kind)
        {
            string label = kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm ? "FBMs" : "PBMs";
            GUILayout.Label(label, EditorStyles.boldLabel);
            if (database == null || database.Registry == null) { EditorGUILayout.HelpBox("Select a ShapeSync Database.", MessageType.Info); return; }
            FbmDetailLayout fbmLayout = kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                ? GetFbmDetailLayoutForTest(database.Registry.FbmAxesFinalized)
                : default;
            string removeFbmName = null;
            if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
            using (var scroll = new EditorGUILayout.ScrollViewScope(materialsScrollPosition))
            {
                foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(entry => entry != null && entry.Kind == kind))
                {
                    if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                    {
                        FbmAxisRedefinitionDraft redefinition = GetOrCreateFbmAxisRedefinitionDraft(axis);
                        redefinition.Name = EditorGUILayout.TextField("FBM Name", redefinition.Name);
                        GameObject selectedFbmPrefab = (GameObject)EditorGUILayout.ObjectField("FBM Prefab", redefinition.SourcePrefab, typeof(GameObject), false);
                        if (selectedFbmPrefab != redefinition.SourcePrefab) redefinition.AssignSourcePrefab(selectedFbmPrefab);
                        EditorGUILayout.HelpBox("To rename or update this FBM, select its current source Prefab and save. PBMs are discarded and Extra Morph choices are regenerated.", MessageType.None);
                    }
                    else using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("PBM Name", axis.Name);
                    }
                    foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry figure in axis.Figures)
                    {
                        if (kind != ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                        {
                            using (new EditorGUI.DisabledScope(true))
                            {
                                EditorGUILayout.TextField("FBM Key", figure.FbmName);
                                EditorGUILayout.ObjectField("Saved Prefab", figure.Figure, typeof(GameObject), false);
                            }
                            continue;
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(true))
                            {
                                EditorGUILayout.ObjectField("Prefab on Database", figure.Figure, typeof(GameObject), false);
                            }
                            using (new EditorGUI.DisabledScope(!CanExportDatabaseFigure(figure.Figure)))
                                if (GUILayout.Button(FigureExportButtonLabel, GUILayout.Width(80f))) TryExportFbmFigureWithDialog(figure.Figure, out _);
                        }
                    }
                    if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            FbmAxisRedefinitionDraft redefinition = GetOrCreateFbmAxisRedefinitionDraft(axis);
                            redefinition.ImportMaterialsAndTextures = EditorGUILayout.ToggleLeft("Import All Materials and Textures", redefinition.ImportMaterialsAndTextures);
                            if (GUILayout.Button("Remove", GUILayout.Width(130))) removeFbmName = axis.Name;
                        }
                        DrawFbmNormalEntries(axis);
                    }
                    EditorGUILayout.Space();
                }
                materialsScrollPosition = scroll.scrollPosition;
            }
            if (!string.IsNullOrEmpty(removeFbmName))
            {
                TryRemoveFbmAxis(removeFbmName, out _);
                return;
            }
            if (!database.Registry.FigureAxes.Any(entry => entry != null && entry.Kind == kind))
                EditorGUILayout.HelpBox(kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm
                    ? "FBM registration is not authored yet."
                    : "PBM registration requires one source Figure for every registered FBM.", MessageType.Info);
            if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm && fbmLayout.ShowsAddFbmEntry)
            {
                GUILayout.Label("Register FBMs", EditorStyles.boldLabel);
                int removeFbmDraftIndex = -1;
                for (int index = 0; index < fbmAxisDrafts.Count; index++)
                {
                    FbmAxisDraft draft = fbmAxisDrafts[index];
                    draft.Name = EditorGUILayout.TextField("FBM Name", draft.Name);
                    GameObject selectedFbmPrefab = (GameObject)EditorGUILayout.ObjectField("Source Prefab", draft.SourcePrefab, typeof(GameObject), false);
                    if (selectedFbmPrefab != draft.SourcePrefab) draft.AssignSourcePrefab(selectedFbmPrefab);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        draft.ImportMaterialsAndTextures = EditorGUILayout.ToggleLeft("Import All Materials and Textures", draft.ImportMaterialsAndTextures);
                        if (GUILayout.Button("Remove", GUILayout.Width(130))) removeFbmDraftIndex = index;
                    }
                }
                if (removeFbmDraftIndex >= 0) { TryRemoveFbmAxisDraft(removeFbmDraftIndex); return; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(FbmAddButtonLabel)) fbmAxisDrafts.Add(new FbmAxisDraft());
                }
            }
            if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && database.Registry.FbmAxesFinalized)
            {
                // PBM follows the same screen contract as FBM: add at the top, one
                // central scroll area for both saved cards and drafts, and one footer.
                if (GUILayout.Button(PbmAddButtonLabel)) pbmAxisDrafts.Add(new PbmAxisDraft());
                GUILayout.Label("Register PBMs", EditorStyles.boldLabel);
                int removePbmDraftIndex = -1;
                string removeSavedPbmName = null;
                using (var pbmScroll = new EditorGUILayout.ScrollViewScope(materialsScrollPosition))
                {
                    foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(entry => entry != null && entry.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm))
                    {
                        PbmAxisRedefinitionDraft redefinition = GetOrCreatePbmAxisRedefinitionDraft(axis);
                        redefinition.Name = EditorGUILayout.TextField("PBM Name", redefinition.Name);
                        GUILayout.Label("PBM Prefabs", EditorStyles.boldLabel);
                        ShapeSyncDatabaseRegistry.AxisFigureEntry baseFigure = axis.Figures.FirstOrDefault(figure => figure != null && figure.FbmName == ShapeSyncDatabaseRegistry.BaseShapeKey);
                        redefinition.BasePrefab = DrawPbmPrefabRow(GetBaseFigureDisplayName(), redefinition.BasePrefab, baseFigure?.Figure);
                        foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry figure in axis.Figures)
                        {
                            if (figure == null || figure.FbmName == ShapeSyncDatabaseRegistry.BaseShapeKey) continue;
                            redefinition.SetSource(figure.FbmName, DrawPbmPrefabRow(figure.FbmName, redefinition.GetSource(figure.FbmName), figure.Figure));
                        }
                        if (GUILayout.Button("Remove", GUILayout.Width(130))) removeSavedPbmName = axis.Name;
                        EditorGUILayout.Space();
                    }
                    for (int index = 0; index < pbmAxisDrafts.Count; index++)
                    {
                        PbmAxisDraft draft = pbmAxisDrafts[index];
                        draft.Name = EditorGUILayout.TextField("PBM Name", draft.Name);
                        GUILayout.Label("PBM Prefabs", EditorStyles.boldLabel);
                        draft.BasePrefab = DrawPbmPrefabRow(GetBaseFigureDisplayName(), draft.BasePrefab, null);
                        foreach (string fbmName in database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name))
                        {
                            draft.SetSource(fbmName, DrawPbmPrefabRow(fbmName, draft.GetSource(fbmName), null));
                        }
                        if (GUILayout.Button("Remove", GUILayout.Width(130))) removePbmDraftIndex = index;
                    }
                    materialsScrollPosition = pbmScroll.scrollPosition;
                }
                if (!string.IsNullOrEmpty(removeSavedPbmName)) { TryRemovePbmAxis(removeSavedPbmName, out _); return; }
                if (removePbmDraftIndex >= 0) { TryRemovePbmAxisDraft(removePbmDraftIndex); return; }
            }
            // The FBM screen owns the FBM-specific Normal cells and commits them with
            // the same footer as FBM registration/redefinition.
            if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
            {
                using (new EditorGUI.DisabledScope(!IsFbmAxisDetailDirty()))
                    if (GUILayout.Button(fbmLayout.FooterActionLabel, GUILayout.Height(DetailSaveButtonHeight))) TrySaveFbmAxisDrafts(out _);
            }
            if (kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && database.Registry.FbmAxesFinalized)
            {
                using (new EditorGUI.DisabledScope(!IsPbmAxisDetailDirty()))
                    if (GUILayout.Button(PbmSaveButtonLabel, GUILayout.Height(DetailSaveButtonHeight))) TrySavePbmAxisDrafts(out _);
            }
        }

        private void DrawFbmNormalEntries(ShapeSyncDatabaseRegistry.FigureAxisEntry axis)
        {
            EnsureFigureNormalEntryDrafts();
            if (figureNormalEntryMaterialNames.Count == 0) return;
            GUILayout.Label("Normals", EditorStyles.boldLabel);
            foreach (string materialEntryName in figureNormalEntryMaterialNames)
            {
                MaterialEntryDraft material = FindMaterialDraft(materialEntryName);
                if (material == null) continue;
                NormalDraft fbmNormal = GetOrCreateNormalDraft(material, axis.Name);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextField("Entry", material.EntryName);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        fbmNormal.Texture = (Texture)EditorGUILayout.ObjectField("Normal", fbmNormal.Texture, typeof(Texture), false);
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(130)))
                        {
                            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Normal Preview", fbmNormal.Texture, typeof(Texture), false, GUILayout.Width(130));
                            if (GUILayout.Button("Pick From Model"))
                            {
                                Texture picked = ResolveFbmNormalFromModel(axis, material);
                                if (picked != null) fbmNormal.Texture = picked;
                            }
                        }
                    }
                }
            }
        }

        private void DrawOutfitsDetail()
        {
            // Outfit registration is the empty-state authoring surface reached
            // immediately after removing the selected Outfit.  A child Detail may
            // have left GUI.enabled disabled while drawing a read-only field; make
            // the registration inputs explicitly editable and restore the caller's
            // state after the block.
            bool previousGuiEnabled = GUI.enabled;
            GUI.enabled = true;
            try
            {
                GUILayout.Label("Outfits", EditorStyles.boldLabel);
                newOutfitIdentity = EditorGUILayout.TextField("Outfit Id", newOutfitIdentity);
                newOutfitName = EditorGUILayout.TextField("Outfit Name", newOutfitName);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Create Mesh Outfit")) TryAddOutfit(newOutfitIdentity, newOutfitName, ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out _);
                    if (GUILayout.Button("Create Material Outfit")) TryAddOutfit(newOutfitIdentity, newOutfitName, ShapeSyncDatabaseRegistry.OutfitKind.Material, out _);
                }
            }
            finally
            {
                GUI.enabled = previousGuiEnabled;
            }
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawOutfitDetail()
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null)
            {
                EditorGUILayout.HelpBox("Select an Outfit.", MessageType.Info);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Materials", StringComparison.Ordinal))
            {
                DrawMeshOutfitMaterialsDetail(outfit);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "FBMs", StringComparison.Ordinal))
            {
                DrawMeshOutfitFbmsDetail(outfit);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Normals", StringComparison.Ordinal))
            {
                DrawMeshOutfitNormalsDetail(outfit);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "PBMs", StringComparison.Ordinal))
            {
                DrawMeshOutfitPbmsDetail(outfit);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Collections", StringComparison.Ordinal))
            {
                DrawMeshOutfitCollectionDetail(outfit);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Figure Mask", StringComparison.Ordinal))
            {
                DrawFigureMaskDetail(outfit);
                return;
            }
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "VRM", StringComparison.Ordinal))
            {
                if (!ShapeSyncDatabaseOptionalUiProvider.TryDrawMeshOutfitVrmDetail(this, outfit.Identity))
                    EditorGUILayout.HelpBox("VRM integration is not available.", MessageType.Info);
                return;
            }
            if (selectedSection == Section.MaterialOutfit)
            {
                DrawMaterialOutfitDetail(outfit);
                return;
            }
            using (var scroll = new EditorGUILayout.ScrollViewScope(outfitDetailScrollPosition, GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Outfit Id", outfit.Identity);
                    using (new EditorGUI.DisabledScope(IsOutfitContentDetailDirty() || !CanMoveOutfit(outfit, true)))
                        if (GUILayout.Button("Move Up", GUILayout.Width(75f)) && TryMoveSelectedOutfit(true, out _)) GUIUtility.ExitGUI();
                    using (new EditorGUI.DisabledScope(IsOutfitContentDetailDirty() || !CanMoveOutfit(outfit, false)))
                        if (GUILayout.Button("Move Down", GUILayout.Width(90f)) && TryMoveSelectedOutfit(false, out _)) GUIUtility.ExitGUI();
                    if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                    {
                        TryRemoveSelectedOutfit(out _);
                        GUIUtility.ExitGUI();
                    }
                }
                string nextName = EditorGUILayout.TextField("Outfit Name", outfitNameDraft);
                if (!string.Equals(nextName, outfitNameDraft, StringComparison.Ordinal)) outfitNameDraft = nextName;
                if (selectedSection == Section.MeshOutfit)
                {
                    GameObject nextSource = (GameObject)EditorGUILayout.ObjectField("Outfit Prefab", outfitSourcePrefabDraft, typeof(GameObject), false);
                    if (nextSource != outfitSourcePrefabDraft) outfitSourcePrefabDraft = nextSource;
                    ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures.FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField("Outfit Prefab on Database", baseAxis?.OutfitPrefab, typeof(GameObject), false);
                        }
                        using (new EditorGUI.DisabledScope(!CanExportDatabaseOutfit(baseAxis?.OutfitPrefab)))
                            if (GUILayout.Button(FigureExportButtonLabel, GUILayout.Width(80f)))
                                TryExportOutfitWithDialog(baseAxis?.OutfitPrefab, "Outfit", out _);
                    }
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField("Merged Prefab on Database", baseAxis?.MergedPrefab, typeof(GameObject), false);
                        EditorGUILayout.ObjectField("PCM Projection on Database", baseAxis?.ProjectionPrefab, typeof(GameObject), false);
                    }
                    EditorGUILayout.HelpBox("All Skinned Meshes are merged when saving. Material classification, Normals, FBMs, PBMs, Collections, and Figure Mask are introduced by subsequent steps.", MessageType.Info);
                }
                else
                    EditorGUILayout.HelpBox("Material Outfit Texture Entries are authoring-only abstract Texture declarations.", MessageType.Info);
                outfitDetailScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.ExpandWidth(true), GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawMeshOutfitMaterialsDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureOutfitMaterialClassificationDrafts(outfit);
            GUILayout.Label("Mesh Outfit Materials", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(outfitMaterialsScrollPosition, GUILayout.ExpandHeight(true)))
            {
                foreach (OutfitMaterialClassificationDraft draft in outfitMaterialClassificationDrafts)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        // Entry names are authored while the classification draft is
                        // still mutable.  The classification value may already be
                        // changed to Exclude/Projection before Save; that must not
                        // make the field appear locked during the same authoring pass.
                        // Once classifications are persisted, the whole table is
                        // intentionally immutable and reclassification requires a
                        // remove/recreate flow.
                        using (new EditorGUI.DisabledScope(!CanEditOutfitMaterialEntryName(outfit)))
                            draft.EntryName = EditorGUILayout.TextField("Entry Name", draft.EntryName);
                        using (new EditorGUI.DisabledScope(outfit.MaterialClassifications.Count != 0))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("Classification", GUILayout.Width(EditorGUIUtility.labelWidth));
                                ShapeSyncDatabaseRegistry.OutfitMaterialClassification current = draft.Classification;
                                foreach (ShapeSyncDatabaseRegistry.OutfitMaterialClassification option in OutfitMaterialClassificationOptions)
                                {
                                    bool selected = GUILayout.Toggle(current == option, option.ToString(), OutfitMaterialClassificationRadioStyle);
                                    if (selected && current != option) draft.Classification = option;
                                }
                            }
                        }
                        using (new EditorGUI.DisabledScope(true))
                        {
                            Material sourceMaterial = ResolveOutfitMaterialForPreview(outfit, draft);
                            EditorGUILayout.ObjectField("Source Material", sourceMaterial, typeof(Material), false);
                            // Match Figure Material Detail: every Entry exposes its
                            // MainTex so classification can be visually verified before Save.
                            EditorGUILayout.ObjectField("Texture Preview", ResolveOutfitMaterialPreview(sourceMaterial), typeof(Texture), false);
                        }
                    }
                }
                outfitMaterialsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private static readonly ShapeSyncDatabaseRegistry.OutfitMaterialClassification[] OutfitMaterialClassificationOptions =
        {
            ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include,
            ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude,
            ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection
        };
        private static GUIStyle OutfitMaterialClassificationRadioStyle => EditorStyles.radioButton;
        internal static string[] OutfitMaterialClassificationControlLabelsForTest => OutfitMaterialClassificationOptions.Select(option => option.ToString()).ToArray();
        internal static GUIStyle OutfitMaterialClassificationControlStyleForTest => OutfitMaterialClassificationRadioStyle;

        private void DrawMeshOutfitFbmsDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureOutfitFbmSourceDrafts(outfit);
            GUILayout.Label("Mesh Outfit FBMs", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(outfitFbmsScrollPosition, GUILayout.ExpandHeight(true)))
            {
                foreach (OutfitFbmSourceDraft draft in outfitFbmSourceDrafts)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(draft.ShapeKey, EditorStyles.boldLabel);
                        draft.SourcePrefab = (GameObject)EditorGUILayout.ObjectField("FBM Prefab", draft.SourcePrefab, typeof(GameObject), false);
                        ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis = outfit.AxisFigures.FirstOrDefault(entry => entry != null && entry.ShapeKey == draft.ShapeKey);
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.ObjectField("Merged Prefab", axis?.MergedPrefab, typeof(GameObject), false);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            using (new EditorGUI.DisabledScope(true))
                                EditorGUILayout.ObjectField("Outfit Prefab", axis?.OutfitPrefab, typeof(GameObject), false);
                            using (new EditorGUI.DisabledScope(!CanExportDatabaseOutfit(axis?.OutfitPrefab)))
                                if (GUILayout.Button(FigureExportButtonLabel, GUILayout.Width(80f)))
                                    TryExportOutfitWithDialog(axis?.OutfitPrefab, "FBM Outfit", out _);
                        }
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.ObjectField("Projection Prefab", axis?.ProjectionPrefab, typeof(GameObject), false);
                    }
                }
                outfitFbmsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawMeshOutfitNormalsDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureOutfitNormalDrafts(outfit);
            GUILayout.Label("Mesh Outfit Normals", EditorStyles.boldLabel);
            string[] available = outfit.MaterialEntries.Select(entry => entry.LogicalName)
                .Where(name => !outfitNormalEntryMaterialNames.Contains(name)).ToArray();
            using (new EditorGUI.DisabledScope(available.Length == 0))
                if (GUILayout.Button("Add Normal Entry"))
                {
                    outfitNormalEntryMaterialNames.Add(available[0]);
                    EnsureOutfitNormalCells(outfit, available[0]);
                }
            using (var scroll = new EditorGUILayout.ScrollViewScope(outfitNormalsScrollPosition, GUILayout.ExpandHeight(true)))
            {
                for (int entryIndex = 0; entryIndex < outfitNormalEntryMaterialNames.Count; entryIndex++)
                {
                    string materialEntry = outfitNormalEntryMaterialNames[entryIndex];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Entry", materialEntry);
                        foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(axis => axis != null))
                        {
                            OutfitNormalDraft draft = GetOrCreateOutfitNormalDraft(outfit, materialEntry, axis.ShapeKey);
                            draft.Texture = (Texture)EditorGUILayout.ObjectField(axis.ShapeKey + " Normal", draft.Texture, typeof(Texture), false);
                        }
                        if (GUILayout.Button("Remove")) { RemoveOutfitNormalEntry(materialEntry); break; }
                    }
                }
                outfitNormalsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawMeshOutfitPbmsDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureOutfitPbmFollowDrafts(outfit);
            GUILayout.Label("Mesh Outfit PBMs", EditorStyles.boldLabel);
            using (var scroll = new EditorGUILayout.ScrollViewScope(outfitPbmsScrollPosition, GUILayout.ExpandHeight(true)))
            {
                foreach (OutfitPbmFollowDraft draft in outfitPbmFollowDrafts)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        draft.Selected = EditorGUILayout.ToggleLeft("Follow " + draft.PbmAxisName, draft.Selected);
                        if (!draft.Selected) continue;
                        foreach (OutfitPbmFollowDraft.SourceRow row in draft.Rows)
                        {
                            row.Prefab = (GameObject)EditorGUILayout.ObjectField(row.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey ? "Base Prefab" : row.ShapeKey + " Prefab", row.Prefab, typeof(GameObject), false);
                            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Prefab on Database", draft.GetSavedPrefab(row.ShapeKey), typeof(GameObject), false);
                        }
                    }
                }
                outfitPbmsScrollPosition = scroll.scrollPosition;
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawMeshOutfitCollectionDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureOutfitCollectionDrafts(outfit);
            GUILayout.Label("Collections", EditorStyles.boldLabel);
            outfitCollectionKind = (ShapeSyncDatabaseRegistry.OutfitCollectionKind)EditorGUILayout.EnumPopup("Collection", outfitCollectionKind);
            bool canUseProjection = outfitCollectionKind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full
                && outfit.AxisFigures.Any(axis => axis != null && axis.ProjectionPrefab != null);
            using (new EditorGUI.DisabledScope(!canUseProjection))
                useProjectionForFullCollection = EditorGUILayout.ToggleLeft("Use Projection for Full Collection", canUseProjection && useProjectionForFullCollection);
            if (outfitCollectionKind != ShapeSyncDatabaseRegistry.OutfitCollectionKind.None)
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(outfitCollectionScrollPosition, GUILayout.ExpandHeight(true)))
                {
                    foreach (OutfitCollectionDraft draft in outfitCollectionDrafts)
                    {
                        draft.Prefab = (GameObject)EditorGUILayout.ObjectField(draft.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey ? "Base Collection Prefab" : draft.ShapeKey + " Collection Prefab", draft.Prefab, typeof(GameObject), false);
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Prefab on Database", draft.SavedPrefab, typeof(GameObject), false);
                    }
                    outfitCollectionScrollPosition = scroll.scrollPosition;
                }
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawMaterialOutfitDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureMaterialOutfitTextureDrafts(outfit);
            GUILayout.Label("Material Outfit", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Outfit Id", outfit.Identity);
                using (new EditorGUI.DisabledScope(IsOutfitContentDetailDirty() || !CanMoveOutfit(outfit, true)))
                    if (GUILayout.Button("Move Up", GUILayout.Width(75f)) && TryMoveSelectedOutfit(true, out _)) GUIUtility.ExitGUI();
                using (new EditorGUI.DisabledScope(IsOutfitContentDetailDirty() || !CanMoveOutfit(outfit, false)))
                    if (GUILayout.Button("Move Down", GUILayout.Width(90f)) && TryMoveSelectedOutfit(false, out _)) GUIUtility.ExitGUI();
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    TryRemoveSelectedOutfit(out _);
                    GUIUtility.ExitGUI();
                }
            }
            string nextName = EditorGUILayout.TextField("Outfit Name", outfitNameDraft);
            if (!string.Equals(nextName, outfitNameDraft, StringComparison.Ordinal)) outfitNameDraft = nextName;
            using (var scroll = new EditorGUILayout.ScrollViewScope(materialOutfitScrollPosition, GUILayout.ExpandHeight(true)))
            {
                int removeIndex = -1;
                foreach (OutfitTextureDraft draft in materialOutfitTextureDrafts)
                {
                    string nextEntryName = EditorGUILayout.TextField("Entry Name", draft.Key, GUILayout.ExpandWidth(true));
                    if (!string.Equals(nextEntryName, draft.Key, StringComparison.Ordinal)) draft.Key = nextEntryName;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        draft.Texture = (Texture)EditorGUILayout.ObjectField("Texture Preview", draft.Texture, typeof(Texture), false);
                        if (GUILayout.Button("Remove", GUILayout.Width(70))) removeIndex = materialOutfitTextureDrafts.IndexOf(draft);
                    }
                }
                if (removeIndex >= 0) { materialOutfitTextureDrafts.RemoveAt(removeIndex); GUIUtility.ExitGUI(); }
                materialOutfitScrollPosition = scroll.scrollPosition;
            }
            newMaterialOutfitTextureEntryName = EditorGUILayout.TextField("Texture Entry Name", newMaterialOutfitTextureEntryName);
            newMaterialOutfitTexture = (Texture)EditorGUILayout.ObjectField("Texture Preview", newMaterialOutfitTexture, typeof(Texture), false);
            if (GUILayout.Button("Add Texture Entry") && !string.IsNullOrWhiteSpace(newMaterialOutfitTextureEntryName)
                && newMaterialOutfitTexture != null && materialOutfitTextureDrafts.All(draft => draft.Key != newMaterialOutfitTextureEntryName))
            {
                materialOutfitTextureDrafts.Add(new OutfitTextureDraft(newMaterialOutfitTextureEntryName) { Texture = newMaterialOutfitTexture });
                newMaterialOutfitTextureEntryName = null; newMaterialOutfitTexture = null;
            }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty()))
                if (GUILayout.Button("Save to Database", GUILayout.ExpandWidth(true), GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private void DrawFigureMaskDetail(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            EnsureFigureMaskDrafts(outfit);
            GUILayout.Label("Figure Mask", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Masks are registered for Spec20.8 Shape selection; they are not applied in this step.", MessageType.Info);
            using (var scroll = new EditorGUILayout.ScrollViewScope(figureMaskScrollPosition, GUILayout.ExpandHeight(true)))
            {
                int removeIndex = -1;
                foreach (OutfitTextureDraft draft in figureMaskDrafts)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField(draft.Key);
                        draft.Texture = (Texture)EditorGUILayout.ObjectField(draft.Texture, typeof(Texture), false);
                        if (GUILayout.Button("Remove", GUILayout.Width(70))) removeIndex = figureMaskDrafts.IndexOf(draft);
                    }
                }
                if (removeIndex >= 0) { figureMaskDrafts.RemoveAt(removeIndex); GUIUtility.ExitGUI(); }
                figureMaskScrollPosition = scroll.scrollPosition;
            }
            string[] available = database.Registry.MaterialEntries.Select(entry => entry.LogicalName).Where(name => figureMaskDrafts.All(draft => draft.Key != name)).ToArray();
            if (available.Length != 0)
            {
                int index = Math.Max(0, Array.IndexOf(available, newFigureMaskMaterialEntryName));
                newFigureMaskMaterialEntryName = available[EditorGUILayout.Popup("Figure Material Entry", index, available)];
            }
            newFigureMaskTexture = (Texture)EditorGUILayout.ObjectField("Mask Texture", newFigureMaskTexture, typeof(Texture), false);
            using (new EditorGUI.DisabledScope(available.Length == 0 || newFigureMaskTexture == null))
                if (GUILayout.Button("Add Figure Mask")) { figureMaskDrafts.Add(new OutfitTextureDraft(newFigureMaskMaterialEntryName) { Texture = newFigureMaskTexture }); newFigureMaskTexture = null; }
            using (new EditorGUI.DisabledScope(!IsOutfitDetailDirty())) if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveOutfit(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private bool IsFbmAxisDetailDirty() => selectedSection == Section.Fbms
            && (fbmAxisDrafts.Count != 0 || fbmAxisRedefinitionDrafts.Any(draft => draft.IsChanged) || HasFbmNormalDraftChanges());

        private void ResetFbmAxisDrafts() { fbmAxisDrafts.Clear(); }

        private bool TryRemoveFbmAxisDraft(int index)
        {
            if (index < 0 || index >= fbmAxisDrafts.Count) return false;
            fbmAxisDrafts.RemoveAt(index);
            diagnostic = null;
            return true;
        }

        private void ResetFbmAxisRedefinitionDrafts()
        {
            fbmAxisRedefinitionDrafts.Clear();
            if (database?.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm))
                fbmAxisRedefinitionDrafts.Add(new FbmAxisRedefinitionDraft(axis.Name, axis.ImportAllMaterialsAndTextures));
        }

        private FbmAxisRedefinitionDraft GetOrCreateFbmAxisRedefinitionDraft(ShapeSyncDatabaseRegistry.FigureAxisEntry axis)
        {
            FbmAxisRedefinitionDraft draft = fbmAxisRedefinitionDrafts.FirstOrDefault(item => item.OriginalName == axis.Name);
            if (draft != null) return draft;
            draft = new FbmAxisRedefinitionDraft(axis.Name, axis.ImportAllMaterialsAndTextures);
            fbmAxisRedefinitionDrafts.Add(draft);
            return draft;
        }

        private bool TryRemoveFbmAxis(string axisName, out string removeDiagnostic)
        {
            removeDiagnostic = null;
            if (database == null) { removeDiagnostic = "Select a ShapeSync Database."; diagnostic = removeDiagnostic; return false; }
            string databasePath = AssetDatabase.GetAssetPath(database);
            if (!RemoveFbmAxis(databasePath, axisName, out removeDiagnostic)) { diagnostic = removeDiagnostic; return false; }
            if (!TrySetDatabaseAtPath(databasePath, out removeDiagnostic)) { diagnostic = removeDiagnostic; return false; }
            diagnostic = null;
            return true;
        }

        private bool TrySaveFbmAxisDrafts(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database == null || database.Registry == null) { saveDiagnostic = "FBM registration requires an open Database."; return false; }
            FbmAxisRedefinitionDraft[] redefinitions = fbmAxisRedefinitionDrafts.Where(draft => draft.IsChanged).ToArray();
            bool hasRedefinition = redefinitions.Length != 0;
            // A Normal edit is authored by this Detail too. Commit it before either a
            // redefinition or a new-FBM import, because either transaction rebinds the
            // Database and therefore replaces this Detail's transient drafts.
            if (HasFbmNormalDraftChanges() && !TrySaveFbmNormals(out saveDiagnostic)) return false;
            foreach (FbmAxisRedefinitionDraft replacement in redefinitions)
            {
                if (replacement.SourcePrefab == null)
                {
                    if (!replacement.IsNameOnlyChange) { saveDiagnostic = "FBM Prefab update requires FBM Prefab: " + replacement.OriginalName; return false; }
                    if (!RenameFbmAxis(AssetDatabase.GetAssetPath(database), replacement.OriginalName, replacement.Name, out saveDiagnostic)) return false;
                    if (!TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(database), out saveDiagnostic)) return false;
                    continue;
                }
                if (!AdmitAxisFigure(replacement.SourcePrefab, out ShapeSyncFigureImportAdmission admission, out saveDiagnostic)) return false;
                if (!ReplaceFbmAxis(AssetDatabase.GetAssetPath(database), replacement.OriginalName, replacement.Name,
                    replacement.ImportMaterialsAndTextures, admission, out saveDiagnostic)) return false;
                if (!TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(database), out saveDiagnostic)) return false;
            }
            if (fbmAxisDrafts.Count == 0)
            {
                if (hasRedefinition) { diagnostic = null; return true; }
                if (HasFbmNormalDraftChanges()) return TrySaveFbmNormals(out saveDiagnostic);
                saveDiagnostic = "FBM Detail has no changes to save.";
                return false;
            }
            ShapeSyncDatabaseRegistry.FigureAxisDraft[] drafts = fbmAxisDrafts.Select(draft => new ShapeSyncDatabaseRegistry.FigureAxisDraft(draft.Name, ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm, draft.ImportMaterialsAndTextures)).ToArray();
            if (!database.Registry.TryAdmitFigureAxes(database, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes, out saveDiagnostic)) return false;
            var admissions = new List<ShapeSyncFigureImportAdmission>();
            try
            {
                for (int index = 0; index < fbmAxisDrafts.Count; index++)
                {
                    if (!AdmitAxisFigure(fbmAxisDrafts[index].SourcePrefab, out ShapeSyncFigureImportAdmission admission, out saveDiagnostic)) return false;
                    admissions.Add(admission);
                }
                ShapeSyncFigureAxisImportRequest[] requests = fbmAxisDrafts.Select((draft, index) => new ShapeSyncFigureAxisImportRequest(
                    axes[index], new[] { new ShapeSyncAxisFigureSource(draft.Name, admissions[index]) })).ToArray();
                if (!ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), requests, out saveDiagnostic)) return false;
                if (!TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(database), out saveDiagnostic)) return false;
                ResetFbmAxisDrafts();
                return true;
            }
            finally { }
        }

        private bool IsPbmAxisDetailDirty() => selectedSection == Section.Pbms && (pbmAxisDrafts.Count != 0 || pbmAxisRedefinitionDrafts.Any(draft => draft.IsChanged));

        private void ResetPbmAxisDrafts() { pbmAxisDrafts.Clear(); ResetPbmAxisRedefinitionDrafts(); }

        private void ResetPbmAxisRedefinitionDrafts()
        {
            pbmAxisRedefinitionDrafts.Clear();
            if (database?.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm))
                pbmAxisRedefinitionDrafts.Add(new PbmAxisRedefinitionDraft(axis.Name));
        }

        private PbmAxisRedefinitionDraft GetOrCreatePbmAxisRedefinitionDraft(ShapeSyncDatabaseRegistry.FigureAxisEntry axis)
        {
            PbmAxisRedefinitionDraft draft = pbmAxisRedefinitionDrafts.FirstOrDefault(item => item.OriginalName == axis.Name);
            if (draft != null) return draft;
            draft = new PbmAxisRedefinitionDraft(axis.Name);
            pbmAxisRedefinitionDrafts.Add(draft);
            return draft;
        }

        private bool TryRemovePbmAxis(string axisName, out string removeDiagnostic)
        {
            removeDiagnostic = null;
            if (database == null) { removeDiagnostic = "Select a ShapeSync Database."; diagnostic = removeDiagnostic; return false; }
            string databasePath = AssetDatabase.GetAssetPath(database);
            if (!RemovePbmAxis(databasePath, axisName, out removeDiagnostic)) { diagnostic = removeDiagnostic; return false; }
            if (!TrySetDatabaseAtPath(databasePath, out removeDiagnostic)) { diagnostic = removeDiagnostic; return false; }
            diagnostic = null;
            return true;
        }

        private bool TryRemovePbmAxisDraft(int index)
        {
            if (index < 0 || index >= pbmAxisDrafts.Count) return false;
            pbmAxisDrafts.RemoveAt(index);
            diagnostic = null;
            return true;
        }

        private bool TrySavePbmAxisDrafts(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database == null || database.Registry == null) { saveDiagnostic = "PBM registration requires an open Database."; return false; }
            foreach (PbmAxisRedefinitionDraft replacement in pbmAxisRedefinitionDrafts.Where(draft => draft.IsChanged).ToArray())
            {
                if (replacement.HasPrefabChange)
                {
                    ShapeSyncDatabaseRegistry.FigureAxisEntry currentAxis = database.Registry.FigureAxes.FirstOrDefault(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm && axis.Name == replacement.OriginalName);
                    if (currentAxis == null) { saveDiagnostic = "PBM entry was not found: " + replacement.OriginalName; return false; }
                    GameObject storedBase = currentAxis.Figures.FirstOrDefault(item => item != null && item.FbmName == ShapeSyncDatabaseRegistry.BaseShapeKey)?.Figure;
                    if (!TryAdmitPbmReplacementSource(replacement.BasePrefab, storedBase, out ShapeSyncFigureImportAdmission baseAdmission, out saveDiagnostic)) return false;
                    var replacementAdmissions = new List<ShapeSyncFigureImportAdmission> { baseAdmission };
                    try
                    {
                        var sources = new List<ShapeSyncAxisFigureSource> { new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, baseAdmission) };
                        foreach (string fbm in database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name))
                        {
                            GameObject storedSource = currentAxis.Figures.FirstOrDefault(item => item != null && item.FbmName == fbm)?.Figure;
                            if (!TryAdmitPbmReplacementSource(replacement.GetSource(fbm), storedSource, out ShapeSyncFigureImportAdmission admission, out saveDiagnostic)) return false;
                            replacementAdmissions.Add(admission);
                            sources.Add(new ShapeSyncAxisFigureSource(fbm, admission));
                        }
                        if (!ReplacePbmAxis(AssetDatabase.GetAssetPath(database), replacement.OriginalName, replacement.Name, sources, out saveDiagnostic)) return false;
                    }
                    finally { }
                }
                else if (!RenamePbmAxis(AssetDatabase.GetAssetPath(database), replacement.OriginalName, replacement.Name, out saveDiagnostic)) return false;
                if (!TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(database), out saveDiagnostic)) return false;
            }
            if (pbmAxisDrafts.Count == 0) { diagnostic = null; return true; }
            ShapeSyncDatabaseRegistry.FigureAxisDraft[] drafts = pbmAxisDrafts.Select(draft => new ShapeSyncDatabaseRegistry.FigureAxisDraft(draft.Name, ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm)).ToArray();
            if (!database.Registry.TryAdmitFigureAxes(database, drafts, out ShapeSyncDatabaseRegistry.FigureAxisAdmission[] axes, out saveDiagnostic)) return false;
            string[] fbms = database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm).Select(axis => axis.Name).ToArray();
            var admissions = new List<ShapeSyncFigureImportAdmission>();
            try
            {
                var requests = new List<ShapeSyncFigureAxisImportRequest>();
                for (int index = 0; index < pbmAxisDrafts.Count; index++)
                {
                    if (!AdmitAxisFigure(pbmAxisDrafts[index].BasePrefab, out ShapeSyncFigureImportAdmission baseAdmission, out saveDiagnostic)) return false;
                    admissions.Add(baseAdmission);
                    var sources = new List<ShapeSyncAxisFigureSource>
                    {
                        new ShapeSyncAxisFigureSource(ShapeSyncDatabaseRegistry.BaseShapeKey, baseAdmission)
                    };
                    foreach (string fbm in fbms)
                    {
                        if (!AdmitAxisFigure(pbmAxisDrafts[index].GetSource(fbm), out ShapeSyncFigureImportAdmission admission, out saveDiagnostic)) return false;
                        admissions.Add(admission);
                        sources.Add(new ShapeSyncAxisFigureSource(fbm, admission));
                    }
                    requests.Add(new ShapeSyncFigureAxisImportRequest(axes[index], sources));
                }
                if (!ShapeSyncFigureAxisImport.TryImport(AssetDatabase.GetAssetPath(database), requests, out saveDiagnostic)) return false;
                if (!TrySetDatabaseAtPath(AssetDatabase.GetAssetPath(database), out saveDiagnostic)) return false;
                ResetPbmAxisDrafts();
                return true;
            }
            finally { }
        }

        private void AssignFigurePrefabFromUi(GameObject prefab)
        {
            figurePrefab = prefab;
            figureName = GetNameAfterPrefabAssignment(figureName, figurePrefab);
            ResolveDatabaseFigurePrefab();
        }

        private string GetBaseFigureDisplayName()
        {
            return database?.Registry != null && database.Registry.TryGetSingleBaseFigureForOpen(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure, out _)
                ? baseFigure.Name
                : "Figure";
        }

        /// <summary>Draws one user-facing PBM Figure row without exposing internal Base terminology.</summary>
        private static GameObject DrawPbmPrefabRow(string figureName, GameObject sourcePrefab, GameObject databasePrefab)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(figureName, GUILayout.Width(150f));
                sourcePrefab = (GameObject)EditorGUILayout.ObjectField(GUIContent.none, sourcePrefab, typeof(GameObject), false, GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(GUIContent.none, databasePrefab, typeof(GameObject), false, GUILayout.ExpandWidth(true));
            }
            return sourcePrefab;
        }

        private static Texture ResolveNormalFromMaterial(Material material)
        {
            if (material == null) return null;
            foreach (string property in material.GetTexturePropertyNames())
            {
                if (property.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0)
                    return material.GetTexture(property);
            }
            return null;
        }

        /// <summary>Resolves an FBM Normal from that FBM's selected model, never from the Base Figure Material Entry.</summary>
        private Texture ResolveFbmNormalFromModel(ShapeSyncDatabaseRegistry.FigureAxisEntry axis, MaterialEntryDraft material)
        {
            if (database?.Registry == null || axis == null || material == null) return null;
            ShapeSyncDatabaseRegistry.MaterialEntry entry = database.Registry.MaterialEntries.FirstOrDefault(candidate =>
                candidate != null
                && (string.Equals(candidate.LogicalName, material.EntryName, StringComparison.Ordinal)
                    || string.Equals(candidate.LogicalName, material.OriginalEntryName, StringComparison.Ordinal)));
            if (entry == null) return null;

            // A selected source Prefab has not yet become a Database Figure.  Merge a
            // disposable preview through the same Figure-axis pipeline as Save, then use
            // the Material Entry's merged slot. This deliberately avoids guessing from
            // source renderer order or Base Figure hierarchy.
            GameObject selectedSource = GetOrCreateFbmAxisRedefinitionDraft(axis).SourcePrefab;
            if (selectedSource != null)
            {
                if (!AdmitAxisFigure(selectedSource, out ShapeSyncFigureImportAdmission admission, out _)
                    || !ShapeSyncFigureMeshMerger.TryMergeOwned(admission.HumanoidRoot, admission.SourceRenderers, out ShapeSyncFigureMeshMerger.Result preview, out _)) return null;
                try { return ResolveNormalFromRendererSlot(preview.Renderer, entry.MaterialSlot); }
                finally { preview.Dispose(); }
            }

            // Once saved, the axis's Database Figure is the only model source. There is
            // no Base-Figure fallback: a missing FBM correspondence remains unresolved.
            GameObject storedFigure = axis.Figures.FirstOrDefault(figure => figure != null && string.Equals(figure.FbmName, axis.Name, StringComparison.Ordinal))?.Figure;
            ShapeSyncFigureImportRecord record = storedFigure == null ? null : storedFigure.GetComponent<ShapeSyncFigureImportRecord>();
            SkinnedMeshRenderer renderer = record?.ConfirmedRendererOrder.Count == 1 ? record.ConfirmedRendererOrder[0] : null;
            return ResolveNormalFromRendererSlot(renderer, entry.MaterialSlot);
        }

        private static Texture ResolveNormalFromRendererSlot(SkinnedMeshRenderer renderer, int materialSlot)
        {
            if (renderer == null || materialSlot < 0 || materialSlot >= renderer.sharedMaterials.Length) return null;
            return ResolveNormalFromMaterial(renderer.sharedMaterials[materialSlot]);
        }

        private void EnsureMaterialDrafts()
        {
            if (materialDrafts.Count != 0 || database == null || database.Registry == null) return;
            materialDraftDiagnostic = null;
            if (database.Registry.MaterialEntries.Count != 0)
            {
                foreach (ShapeSyncDatabaseRegistry.MaterialEntry entry in database.Registry.MaterialEntries)
                {
                    if (entry == null) continue;
                    materialDrafts.Add(new MaterialEntryDraft(entry.Renderer, entry.MaterialSlot, entry.Material, ResolveMaterialEntryPreview(entry), entry.LogicalName, entry.LogicalName));
                }
                AcceptMaterialDraft();
                return;
            }
            if (!database.Registry.TryGetSingleBaseFigureForOpen(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry baseEntry, out string baseDiagnostic) || baseEntry == null) { materialDraftDiagnostic = baseDiagnostic; return; }
            int index = 0;
            foreach (SkinnedMeshRenderer renderer in baseEntry.Figure.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material source = materials[slot];
                    if (source == null) continue;
                    string name = "MaterialEntry-" + index;
                    if (!ShapeSyncMaterialAdapterResolver.TryAdmit(database, name, renderer, slot, source, out ShapeSyncMaterialAdapterResolver.Admission admission, out string admissionDiagnostic))
                    {
                        materialDraftDiagnostic ??= admissionDiagnostic;
                        continue;
                    }
                    try { materialDrafts.Add(new MaterialEntryDraft(renderer, slot, source, admission.PreviewTexture, name, null)); }
                    finally { admission.Dispose(); }
                    index++;
                }
            }
            AcceptMaterialDraft();
        }

        private bool IsMaterialsDetailDirty()
        {
            if (selectedSection != Section.Materials) return false;
            if (materialDrafts.Count != acceptedMaterialDraftNames.Count) return true;
            for (int index = 0; index < materialDrafts.Count; index++) if (!string.Equals(materialDrafts[index].EntryName, acceptedMaterialDraftNames[index], StringComparison.Ordinal)) return true;
            return false;
        }

        private bool HasNormalDraftChanges() => normalDrafts.Count != acceptedNormalTextures.Count || normalDrafts.Where((draft, index) => draft.Texture != acceptedNormalTextures[index]).Any();

        private void SelectSection(Section section)
        {
            selectedSection = section;
            if (selectedSection == Section.Materials) EnsureMaterialDrafts();
            if (selectedSection == Section.Textures) EnsureTextureDrafts();
        }

        private void AcceptMaterialDraft() { acceptedMaterialDraftNames = materialDrafts.Select(draft => draft.EntryName).ToList(); acceptedNormalTextures = normalDrafts.Select(draft => draft.Texture).ToList(); }

        private static string ResolveMaterialRename(string name, IReadOnlyList<ShapeSyncMaterialEntryImport.Rename> renames)
        {
            if (name == null || renames == null) return name;
            ShapeSyncMaterialEntryImport.Rename rename = renames.FirstOrDefault(item => string.Equals(item.CurrentName, name, StringComparison.Ordinal));
            return rename.CurrentName == null ? name : rename.NextName;
        }

        private void ApplyMaterialRenamesToDrafts(IReadOnlyList<ShapeSyncMaterialEntryImport.Rename> renames)
        {
            if (renames == null || renames.Count == 0) return;
            foreach (MaterialEntryDraft draft in materialDrafts)
            {
                string current = draft.OriginalEntryName ?? draft.EntryName;
                string next = ResolveMaterialRename(current, renames);
                draft.EntryName = next;
                draft.OriginalEntryName = next;
            }
            for (int index = 0; index < figureNormalEntryMaterialNames.Count; index++)
                figureNormalEntryMaterialNames[index] = ResolveMaterialRename(figureNormalEntryMaterialNames[index], renames);
            for (int index = 0; index < acceptedFigureNormalEntryMaterialNames.Count; index++)
                acceptedFigureNormalEntryMaterialNames[index] = ResolveMaterialRename(acceptedFigureNormalEntryMaterialNames[index], renames);
            foreach (NormalDraft draft in normalDrafts)
                draft.MaterialEntryName = ResolveMaterialRename(draft.MaterialEntryName, renames);
            if (figureNormalEntriesInitialized && database?.Registry != null)
            {
                figureNormalEntryMaterialNames = database.Registry.FigureNormalEntries
                    .Where(entry => entry != null)
                    .Select(entry => entry.MaterialEntryName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                acceptedFigureNormalEntryMaterialNames = new List<string>(figureNormalEntryMaterialNames);
            }
        }

        private void DiscardMaterialDraft()
        {
            for (int index = 0; index < materialDrafts.Count && index < acceptedMaterialDraftNames.Count; index++) materialDrafts[index].EntryName = acceptedMaterialDraftNames[index];
            DiscardNormalDrafts();
            diagnostic = null;
        }

        private void DiscardNormalDrafts()
        {
            for (int index = 0; index < normalDrafts.Count && index < acceptedNormalTextures.Count; index++) normalDrafts[index].Texture = acceptedNormalTextures[index];
        }

        private void ResetMaterialDraft()
        {
            materialDrafts.Clear();
            normalDrafts.Clear();
            acceptedNormalTextures.Clear();
            acceptedMaterialDraftNames.Clear();
            materialDraftDiagnostic = null;
        }

        private void DrawTexturesDetail()
        {
            GUILayout.Label("Textures", EditorStyles.boldLabel);
            if (database == null || database.Registry == null) { EditorGUILayout.HelpBox("Select a ShapeSync Database.", MessageType.Info); return; }
            EnsureTextureDrafts();
            using (var scroll = new EditorGUILayout.ScrollViewScope(texturesScrollPosition, GUILayout.ExpandHeight(true)))
            {
                int removeIndex = -1;
                foreach (TextureResourceDraft draft in textureDrafts)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Remove", GUILayout.Width(70))) removeIndex = textureDrafts.IndexOf(draft);
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("Preview", draft.Texture, typeof(Texture), false);
                    }
                    draft.Name = EditorGUILayout.TextField("Texture Name", draft.Name);
                    EditorGUILayout.Space();
                }
                if (removeIndex >= 0) { TryRemoveTextureDraft(removeIndex); GUIUtility.ExitGUI(); }
                texturesScrollPosition = scroll.scrollPosition;
            }
            newTextureName = EditorGUILayout.TextField("Texture Name", newTextureName);
            newTexture = (Texture)EditorGUILayout.ObjectField("Texture", newTexture, typeof(Texture), false);
            if (GUILayout.Button("Add New Texture")) TryAddTextureDraft();
            using (new EditorGUI.DisabledScope(!CanSaveTextureDrafts())) if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveTextureDrafts(out _);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private bool TryAddTextureDraft()
        {
            EnsureTextureDrafts();
            if (string.IsNullOrWhiteSpace(newTextureName) || newTexture == null)
            { diagnostic = "Add New Texture requires both a Texture Name and a Texture."; return false; }
            if (textureDrafts.Any(draft => string.Equals(draft.Name, newTextureName, StringComparison.Ordinal)))
            { diagnostic = "Texture resource names must be unique."; return false; }
            textureDrafts.Add(new TextureResourceDraft(null, newTextureName, newTexture));
            newTextureName = null;
            newTexture = null;
            diagnostic = null;
            return true;
        }

        private bool TrySaveTextureDrafts(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            EnsureTextureDrafts();
            if (database == null || database.Registry == null) { saveDiagnostic = "Select a ShapeSync Database."; diagnostic = saveDiagnostic; return false; }
            string databasePath = AssetDatabase.GetAssetPath(database);
            var renames = textureDrafts.Where(draft => draft.OriginalName != null && !string.Equals(draft.OriginalName, draft.Name, StringComparison.Ordinal))
                .Select(draft => new ShapeSyncTextureResourceAuthoring.Rename(draft.OriginalName, draft.Name)).ToArray();
            var additions = textureDrafts.Where(draft => draft.OriginalName == null)
                .Select(draft => new ShapeSyncTextureResourceAuthoring.Addition(draft.Name, draft.Texture)).ToArray();
            var removals = removedTextureDraftNames.Select(name => new ShapeSyncTextureResourceAuthoring.Removal(name)).ToArray();
            bool saved = additions.Length == 0 && removals.Length == 0
                ? ShapeSyncTextureResourceAuthoring.TryRenameDirect(database, renames, out saveDiagnostic)
                : SaveTextureResources(databasePath, renames, additions, removals, out saveDiagnostic);
            if (!saved)
            {
                // A rejected removal leaves the Database unchanged, so reinsert just the
                // removed rows.  Other failed edits deliberately remain visible for retry.
                if (removals.Length != 0) RestoreRejectedTextureRemovals();
                diagnostic = saveDiagnostic;
                return false;
            }
            if (additions.Length != 0 || removals.Length != 0)
            {
                if (!TrySetDatabaseAtPath(databasePath, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            }
            if (additions.Length == 0 && removals.Length == 0) AcceptTextureDraft();
            else EnsureTextureDrafts();
            diagnostic = null;
            return true;
        }

        private void EnsureTextureDrafts()
        {
            if (textureDrafts.Count != 0 || database == null || database.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in database.Registry.TextureResources)
                if (resource != null && !removedTextureDraftNames.Contains(resource.LogicalName)) textureDrafts.Add(new TextureResourceDraft(resource.LogicalName, resource.LogicalName, resource.Texture));
            AcceptTextureDraft();
        }

        private void RestoreRejectedTextureRemovals()
        {
            if (database == null || database.Registry == null || removedTextureDraftNames.Count == 0) return;
            var currentByOriginalName = textureDrafts
                .Where(draft => !string.IsNullOrEmpty(draft.OriginalName))
                .ToDictionary(draft => draft.OriginalName, StringComparer.Ordinal);
            var restored = new List<TextureResourceDraft>();
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in database.Registry.TextureResources)
            {
                if (resource == null) continue;
                if (currentByOriginalName.TryGetValue(resource.LogicalName, out TextureResourceDraft current)) restored.Add(current);
                else if (removedTextureDraftNames.Contains(resource.LogicalName))
                    restored.Add(new TextureResourceDraft(resource.LogicalName, resource.LogicalName, resource.Texture));
            }
            restored.AddRange(textureDrafts.Where(draft => draft.OriginalName == null));
            textureDrafts = restored;
            removedTextureDraftNames.Clear();
            if (textureDrafts.All(draft => draft.OriginalName != null && string.Equals(draft.OriginalName, draft.Name, StringComparison.Ordinal)))
                AcceptTextureDraft();
        }

        private bool IsTexturesDetailDirty()
        {
            if (selectedSection != Section.Textures) return false;
            if (textureDrafts.Count != acceptedTextureDraftNames.Count) return true;
            for (int index = 0; index < textureDrafts.Count; index++) if (!string.Equals(textureDrafts[index].Name, acceptedTextureDraftNames[index], StringComparison.Ordinal)) return true;
            return removedTextureDraftNames.Count != 0 || !string.IsNullOrWhiteSpace(newTextureName) || newTexture != null;
        }

        private ShapeSyncDatabaseRegistry.OutfitEntry GetSelectedOutfit()
        {
            return database?.Registry?.Outfits.FirstOrDefault(entry => entry != null && string.Equals(entry.Identity, selectedOutfitIdentity, StringComparison.Ordinal));
        }

        private bool IsOutfitDetailDirty()
        {
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "VRM", StringComparison.Ordinal)
                && ShapeSyncDatabaseOptionalUiProvider.IsMeshOutfitVrmDetailDirty(this, selectedOutfitIdentity)) return true;
            return (selectedSection == Section.MeshOutfit || selectedSection == Section.MaterialOutfit)
                && (IsOutfitOrderDraftDirty() || IsOutfitContentDetailDirty());
        }

        private bool IsOutfitContentDetailDirty()
        {
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Materials", StringComparison.Ordinal)
                && outfitMaterialClassificationDrafts.Any(draft => draft != null && draft.IsDirty)) return true;
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "FBMs", StringComparison.Ordinal)
                && outfitFbmSourceDrafts.Any(draft => draft != null && draft.IsDirty)) return true;
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Normals", StringComparison.Ordinal)
                && (!outfitNormalEntryMaterialNames.SequenceEqual(acceptedOutfitNormalEntryMaterialNames)
                    || outfitNormalDrafts.Any(draft => draft != null && draft.IsDirty))) return true;
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "PBMs", StringComparison.Ordinal)
                && outfitPbmFollowDrafts.Any(draft => draft != null && draft.IsDirty)) return true;
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Collections", StringComparison.Ordinal)
                && (outfitCollectionKind != acceptedOutfitCollectionKind || useProjectionForFullCollection != acceptedUseProjectionForFullCollection
                    || outfitCollectionDrafts.Any(draft => draft != null && draft.IsDirty))) return true;
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (selectedSection == Section.MeshOutfit && string.Equals(selectedMeshOutfitChildLabel, "Figure Mask", StringComparison.Ordinal))
            {
                EnsureFigureMaskDrafts(outfit);
                if (figureMaskDrafts.Any(draft => draft != null && draft.IsDirty)
                    || !figureMaskDrafts.Select(draft => draft.Key).SequenceEqual((outfit?.FigureMaskEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.FigureMaskEntry>()).Select(entry => entry.FigureMaterialEntryName))) return true;
            }
            if (selectedSection == Section.MaterialOutfit)
            {
                EnsureMaterialOutfitTextureDrafts(outfit);
                if (materialOutfitTextureDrafts.Any(draft => draft != null && draft.IsDirty)
                    || !materialOutfitTextureDrafts.Select(draft => draft.Key).SequenceEqual((outfit?.MaterialOutfitTextureEntries ?? Array.Empty<ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry>()).Select(entry => entry.EntryName))) return true;
            }
            return (selectedSection == Section.MeshOutfit || selectedSection == Section.MaterialOutfit)
                && (!string.Equals(outfitNameDraft, acceptedOutfitNameDraft, StringComparison.Ordinal)
                    || (selectedSection == Section.MeshOutfit && outfitSourcePrefabDraft != acceptedOutfitSourcePrefabDraft));
        }

        private IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitEntry> GetOutfitsForTreeView()
        {
            ShapeSyncDatabaseRegistry.OutfitEntry[] stored = database?.Registry?.Outfits
                .Where(entry => entry != null).ToArray() ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>();
            if (outfitOrderDraft.Count != stored.Length) return stored;

            var byIdentity = stored.ToDictionary(entry => entry.Identity, StringComparer.Ordinal);
            var ordered = new List<ShapeSyncDatabaseRegistry.OutfitEntry>(stored.Length);
            foreach (string identity in outfitOrderDraft)
            {
                if (!byIdentity.TryGetValue(identity, out ShapeSyncDatabaseRegistry.OutfitEntry entry)) return stored;
                ordered.Add(entry);
            }
            return ordered.Count == stored.Length ? ordered : stored;
        }

        private bool IsOutfitOrderDraftDirty()
            => !outfitOrderDraft.SequenceEqual(acceptedOutfitOrderDraft, StringComparer.Ordinal);

        private void ResetOutfitOrderDraft()
        {
            outfitOrderDraft = database?.Registry?.Outfits.Where(entry => entry != null).Select(entry => entry.Identity).ToList()
                ?? new List<string>();
            acceptedOutfitOrderDraft = new List<string>(outfitOrderDraft);
        }

        private bool TryAddOutfit(string identity, string displayName, ShapeSyncDatabaseRegistry.OutfitKind kind, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database == null) { saveDiagnostic = "Select or create a ShapeSync Database first."; diagnostic = saveDiagnostic; return false; }
            string path = AssetDatabase.GetAssetPath(database);
            bool changed = false;
            string registryDiagnostic = null;
            if (!ShapeSyncDatabaseTransaction.TryEditStructure(path, (contents, _) =>
            {
                changed = contents.Registry != null && contents.Registry.TryAddOutfit(identity, displayName, kind, out registryDiagnostic);
            }, out string transactionDiagnostic))
            {
                saveDiagnostic = registryDiagnostic ?? transactionDiagnostic;
                diagnostic = saveDiagnostic;
                return false;
            }
            if (!changed) { saveDiagnostic = registryDiagnostic ?? "Outfit could not be saved."; diagnostic = saveDiagnostic; return false; }
            if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            newOutfitIdentity = null;
            newOutfitName = null;
            treeView?.Reload();
            treeView?.SelectOutfitIdentity(identity);
            TrySelectOutfit(identity);
            diagnostic = null;
            return true;
        }

        private bool TrySelectOutfit(string identity)
        {
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = database?.Registry?.Outfits.FirstOrDefault(entry => entry != null && string.Equals(entry.Identity, identity, StringComparison.Ordinal));
            if (outfit == null) return false;
            outfitNormalEntryMaterialNames.Clear();
            acceptedOutfitNormalEntryMaterialNames.Clear();
            outfitNormalDrafts.Clear();
            outfitNormalDraftsInitialized = false;
            outfitMaterialClassificationDrafts.Clear();
            outfitFbmSourceDrafts.Clear();
            outfitPbmFollowDrafts.Clear();
            outfitCollectionDrafts.Clear();
            materialOutfitTextureDrafts.Clear();
            figureMaskDrafts.Clear();
            materialOutfitTextureDraftsInitialized = false;
            figureMaskDraftsInitialized = false;
            selectedOutfitIdentity = outfit.Identity;
            selectedMeshOutfitChildLabel = null;
            outfitNameDraft = outfit.StoredDisplayName;
            acceptedOutfitNameDraft = outfit.StoredDisplayName;
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures.FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            outfitSourcePrefabDraft = baseAxis?.SourcePrefab;
            acceptedOutfitSourcePrefabDraft = outfitSourcePrefabDraft;
            outfitCollectionKind = acceptedOutfitCollectionKind = outfit.CollectionKind;
            useProjectionForFullCollection = acceptedUseProjectionForFullCollection = outfit.UseProjectionForFullCollection;
            selectedSection = outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh ? Section.MeshOutfit : Section.MaterialOutfit;
            return true;
        }

        private bool TrySaveOutfit(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) { saveDiagnostic = "Select an existing Outfit first."; diagnostic = saveDiagnostic; return false; }
            if (IsOutfitOrderDraftDirty() && !TrySaveOutfitOrderDraft(out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            if (!IsOutfitContentDetailDirty())
            {
                treeView?.SelectOutfitIdentity(outfit.Identity);
                diagnostic = null;
                return true;
            }
            string identity = outfit.Identity;
            string path = AssetDatabase.GetAssetPath(database);
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Material)
            {
                EnsureMaterialOutfitTextureDrafts(outfit);
                if (!ShapeSyncOutfitTextureAuthoring.TrySaveMaterialOutfitTextures(path, identity,
                    materialOutfitTextureDrafts.Select(draft => new ShapeSyncOutfitTextureAuthoring.MaterialTextureInput(draft.Key, draft.Texture)).ToArray(), out saveDiagnostic))
                { diagnostic = saveDiagnostic; return false; }
                if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                TrySelectOutfit(identity);
                diagnostic = null;
                return true;
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && string.Equals(selectedMeshOutfitChildLabel, "Figure Mask", StringComparison.Ordinal))
            {
                EnsureFigureMaskDrafts(outfit);
                if (!ShapeSyncOutfitTextureAuthoring.TrySaveFigureMasks(path, identity,
                    figureMaskDrafts.Select(draft => new ShapeSyncOutfitTextureAuthoring.FigureMaskInput(draft.Key, draft.Texture)).ToArray(), out saveDiagnostic))
                { diagnostic = saveDiagnostic; return false; }
                if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                TrySelectOutfit(identity);
                selectedMeshOutfitChildLabel = "Figure Mask";
                diagnostic = null;
                return true;
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && string.Equals(selectedMeshOutfitChildLabel, "Materials", StringComparison.Ordinal))
            {
                EnsureOutfitMaterialClassificationDrafts(outfit);
                if (outfitMaterialClassificationDrafts.Any(draft => draft == null))
                {
                    saveDiagnostic = "Mesh Outfit Material classification draft is missing.";
                    diagnostic = saveDiagnostic;
                    return false;
                }
                if (outfitMaterialClassificationDrafts.Any(draft => draft.IsDirty))
                {
                    bool deletesClassifiedAssets = RequiresIrreversibleClassificationConfirmation(outfitMaterialClassificationDrafts.Select(draft => draft.Classification));
                    if (deletesClassifiedAssets && !ConfirmIrreversibleOutfitClassification(
                        "Confirm Material Classification",
                        "Saving removes the Database Material and Texture assets classified as Exclude or Projection. This operation is irreversible. Reclassifying requires removing and recreating this Outfit.",
                        "Save and Remove", "Cancel"))
                    {
                        saveDiagnostic = "Mesh Outfit Material classification Save was cancelled.";
                        diagnostic = saveDiagnostic;
                        return false;
                    }
                    if (!ShapeSyncMeshOutfitImport.TryApplyMaterialClassifications(path, identity,
                        outfitMaterialClassificationDrafts.Select(draft => new ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry(
                            draft.SourceMaterialName, draft.Classification,
                            draft.Classification == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include ? draft.EntryName : null)).ToArray(), out saveDiagnostic))
                    { diagnostic = saveDiagnostic; return false; }
                    if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    TrySelectOutfit(identity);
                    selectedMeshOutfitChildLabel = "Materials";
                    diagnostic = null;
                    return true;
                }
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && string.Equals(selectedMeshOutfitChildLabel, "FBMs", StringComparison.Ordinal))
            {
                EnsureOutfitFbmSourceDrafts(outfit);
                OutfitFbmSourceDraft[] changedFbmSources = outfitFbmSourceDrafts.Where(draft => draft != null && draft.IsDirty).ToArray();
                if (changedFbmSources.Length != 0)
                {
                    // Preflight every changed row before the first per-axis transaction.
                    // A malformed later row must not leave an earlier FBM newly saved.
                    foreach (OutfitFbmSourceDraft draft in changedFbmSources)
                    {
                        if (draft.SourcePrefab == null)
                        { saveDiagnostic = "Mesh Outfit FBM requires a Prefab: " + draft.ShapeKey; diagnostic = saveDiagnostic; return false; }
                        if (!ShapeSyncMeshOutfitImport.TryValidateAxisSource(draft.SourcePrefab, out saveDiagnostic))
                        { diagnostic = "Mesh Outfit FBM source is invalid for " + draft.ShapeKey + ": " + saveDiagnostic; return false; }
                    }
                    if (!ShapeSyncMeshOutfitImport.TryImportAxes(path, identity,
                        changedFbmSources.Select(draft => new KeyValuePair<string, GameObject>(draft.ShapeKey, draft.SourcePrefab)).ToArray(), out saveDiagnostic))
                    { diagnostic = saveDiagnostic; return false; }
                    if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    TrySelectOutfit(identity);
                    selectedMeshOutfitChildLabel = "FBMs";
                    diagnostic = null;
                    return true;
                }
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && string.Equals(selectedMeshOutfitChildLabel, "Normals", StringComparison.Ordinal))
            {
                EnsureOutfitNormalDrafts(outfit);
                if (IsOutfitDetailDirty())
                {
                    if (!ShapeSyncOutfitNormalAuthoring.TrySave(path, identity, outfitNormalEntryMaterialNames,
                        outfitNormalDrafts.Select(draft => new ShapeSyncOutfitNormalAuthoring.Assignment(draft.MaterialEntryName, draft.ShapeKey, draft.Texture)).ToArray(), out saveDiagnostic))
                    { diagnostic = saveDiagnostic; return false; }
                    if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    TrySelectOutfit(identity); selectedMeshOutfitChildLabel = "Normals"; diagnostic = null; return true;
                }
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && string.Equals(selectedMeshOutfitChildLabel, "PBMs", StringComparison.Ordinal))
            {
                EnsureOutfitPbmFollowDrafts(outfit);
                if (IsOutfitDetailDirty())
                {
                    var sources = outfitPbmFollowDrafts.Where(draft => draft.Selected)
                        .SelectMany(draft => draft.Rows.Select(row => new ShapeSyncMeshOutfitPbmFollowAuthoring.Source(draft.PbmAxisName, row.ShapeKey, row.Prefab))).ToArray();
                    if (!ShapeSyncMeshOutfitPbmFollowAuthoring.TrySave(path, identity, sources, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    TrySelectOutfit(identity); selectedMeshOutfitChildLabel = "PBMs"; diagnostic = null; return true;
                }
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && string.Equals(selectedMeshOutfitChildLabel, "Collections", StringComparison.Ordinal))
            {
                EnsureOutfitCollectionDrafts(outfit);
                if (IsOutfitDetailDirty())
                {
                    ShapeSyncMeshOutfitCollectionAuthoring.Source[] sources = outfitCollectionKind == ShapeSyncDatabaseRegistry.OutfitCollectionKind.None
                        ? Array.Empty<ShapeSyncMeshOutfitCollectionAuthoring.Source>()
                        : outfitCollectionDrafts.Select(draft => new ShapeSyncMeshOutfitCollectionAuthoring.Source(draft.ShapeKey, draft.Prefab)).ToArray();
                    if (!ShapeSyncMeshOutfitCollectionAuthoring.TrySave(path, identity, outfitCollectionKind, useProjectionForFullCollection, sources, out saveDiagnostic))
                    { diagnostic = saveDiagnostic; return false; }
                    if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
                    TrySelectOutfit(identity); selectedMeshOutfitChildLabel = "Collections"; diagnostic = null; return true;
                }
            }
            if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh && outfitSourcePrefabDraft != acceptedOutfitSourcePrefabDraft)
            {
                if (outfitSourcePrefabDraft == null)
                { saveDiagnostic = "Mesh Outfit requires an Outfit Prefab."; diagnostic = saveDiagnostic; return false; }
                if (!ShapeSyncMeshOutfitImport.TryImportBase(path, identity, outfitSourcePrefabDraft, out saveDiagnostic))
                { diagnostic = saveDiagnostic; return false; }
                if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            }
            if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Rename ShapeSync Outfit",
                (ShapeSyncDatabaseRegistry registry, out string registryDiagnostic) =>
            {
                if (registry == null) { registryDiagnostic = "ShapeSync Database Registry is missing."; return false; }
                return registry.TryRenameOutfit(identity, outfitNameDraft, out registryDiagnostic);
            }, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            outfitNameDraft = outfit.StoredDisplayName;
            acceptedOutfitNameDraft = outfit.StoredDisplayName;
            treeView?.SelectOutfitIdentity(identity);
            diagnostic = null;
            return true;
        }

        private bool TrySaveOutfitOrderDraft(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            if (database?.Registry == null) { saveDiagnostic = "Outfit order save requires an open Database."; return false; }

            ShapeSyncDatabaseRegistry.OutfitEntry[] stored = database.Registry.Outfits.Where(entry => entry != null).ToArray();
            if (outfitOrderDraft.Count != stored.Length
                || outfitOrderDraft.Distinct(StringComparer.Ordinal).Count() != stored.Length
                || stored.Any(entry => !outfitOrderDraft.Contains(entry.Identity)))
            {
                saveDiagnostic = "Outfit order draft does not match the current Database Outfits.";
                return false;
            }

            if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Reorder ShapeSync Outfits",
                (ShapeSyncDatabaseRegistry registry, out string detail) =>
                {
                    foreach (ShapeSyncDatabaseRegistry.OutfitKind kind in Enum.GetValues(typeof(ShapeSyncDatabaseRegistry.OutfitKind)))
                    {
                        string[] desired = outfitOrderDraft.Where(identity => stored.Any(entry => entry.Kind == kind && entry.Identity == identity)).ToArray();
                        string[] current = registry.Outfits.Where(entry => entry != null && entry.Kind == kind).Select(entry => entry.Identity).ToArray();
                        for (int desiredIndex = 0; desiredIndex < desired.Length; desiredIndex++)
                        {
                            int currentIndex = Array.IndexOf(current, desired[desiredIndex]);
                            if (currentIndex < 0) { detail = "Outfit order save could not resolve: " + desired[desiredIndex]; return false; }
                            while (currentIndex > desiredIndex)
                            {
                                if (!registry.TryMoveOutfit(current[currentIndex], true, out string moveDiagnostic))
                                {
                                    detail = moveDiagnostic ?? "Outfit order could not be saved.";
                                    return false;
                                }
                                (current[currentIndex - 1], current[currentIndex]) = (current[currentIndex], current[currentIndex - 1]);
                                currentIndex--;
                            }
                        }
                    }
                    detail = null;
                    return true;
                }, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            ResetOutfitOrderDraft();
            if (!string.IsNullOrEmpty(selectedOutfitIdentity)) treeView?.SelectOutfitIdentity(selectedOutfitIdentity);
            diagnostic = null;
            return true;
        }

        private bool CanMoveOutfit(ShapeSyncDatabaseRegistry.OutfitEntry outfit, bool moveUp)
        {
            if (outfit == null || database?.Registry == null) return false;
            ShapeSyncDatabaseRegistry.OutfitEntry[] sameKind = GetOutfitsForTreeView()
                .Where(entry => entry != null && entry.Kind == outfit.Kind).ToArray();
            int index = Array.IndexOf(sameKind, outfit);
            return moveUp ? index > 0 : index >= 0 && index < sameKind.Length - 1;
        }

        private bool TryMoveSelectedOutfit(bool moveUp, out string saveDiagnostic)
        {
            saveDiagnostic = null;
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) { saveDiagnostic = "Select an existing Outfit first."; diagnostic = saveDiagnostic; return false; }
            if (IsOutfitContentDetailDirty()) { saveDiagnostic = "Save or discard Outfit Detail changes before changing TreeView order."; diagnostic = saveDiagnostic; return false; }
            string identity = outfit.Identity;
            int currentIndex = outfitOrderDraft.IndexOf(identity);
            if (currentIndex < 0)
            {
                saveDiagnostic = "Outfit order draft does not contain the selected Outfit.";
                diagnostic = saveDiagnostic;
                return false;
            }
            int targetIndex = -1;
            int step = moveUp ? -1 : 1;
            for (int index = currentIndex + step; index >= 0 && index < outfitOrderDraft.Count; index += step)
            {
                ShapeSyncDatabaseRegistry.OutfitEntry candidate = database.Registry.Outfits
                    .FirstOrDefault(entry => entry != null && string.Equals(entry.Identity, outfitOrderDraft[index], StringComparison.Ordinal));
                if (candidate != null && candidate.Kind == outfit.Kind) { targetIndex = index; break; }
            }
            if (targetIndex < 0)
            {
                saveDiagnostic = moveUp ? "Outfit is already first in its TreeView group." : "Outfit is already last in its TreeView group.";
                diagnostic = saveDiagnostic;
                return false;
            }
            outfitOrderDraft.RemoveAt(currentIndex);
            outfitOrderDraft.Insert(Math.Min(targetIndex, outfitOrderDraft.Count), identity);
            treeView?.SelectOutfitIdentity(identity);
            TrySelectOutfit(identity);
            diagnostic = null;
            return true;
        }

        private bool TryRemoveSelectedOutfit(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
            if (outfit == null) { saveDiagnostic = "Select an existing Outfit first."; diagnostic = saveDiagnostic; return false; }
            string identity = outfit.Identity;
            string path = AssetDatabase.GetAssetPath(database);
            bool changed = false;
            string registryDiagnostic = null;
            HashSet<string> artifactPrefixesForCleanup = null;
            if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(path, (contents, _, transaction) =>
            {
                if (contents.Registry == null) return;
                if (!contents.Registry.Outfits.Any(entry => entry != null && entry.Identity == identity))
                {
                    registryDiagnostic = "Outfit was not found: " + identity;
                    return;
                }
                ShapeSyncDatabaseRegistry.OutfitEntry storedOutfit = contents.Registry.Outfits
                    .Single(entry => entry != null && entry.Identity == identity);
                changed = contents.Registry.TryRemoveOutfit(identity, out registryDiagnostic);
                if (!changed) return;
                // Remove the registry relation before destroying its hierarchy
                // objects. Otherwise Unity can keep serialized references to the
                // direct Intermediate children alive during Prefab save.
                artifactPrefixesForCleanup = RemoveOutfitOwnedArtifacts(contents, storedOutfit, identity, path, transaction);
            }, out string transactionDiagnostic))
            {
                saveDiagnostic = registryDiagnostic ?? transactionDiagnostic;
                diagnostic = saveDiagnostic;
                return false;
            }
            if (!changed) { saveDiagnostic = registryDiagnostic ?? "Outfit could not be removed."; diagnostic = saveDiagnostic; return false; }
            if (!TrySetDatabaseAtPath(path, out saveDiagnostic)) { diagnostic = saveDiagnostic; return false; }
            if (!RemoveNamedOutfitSubAssetsAfterPrefabEdit(path, artifactPrefixesForCleanup, identity, out saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            selectedOutfitIdentity = null;
            selectedMeshOutfitChildLabel = null;
            outfitNameDraft = null;
            acceptedOutfitNameDraft = null;
            outfitSourcePrefabDraft = null;
            acceptedOutfitSourcePrefabDraft = null;
            selectedSection = Section.Outfits;
            treeView?.SelectOutfitsRoot();
            diagnostic = null;
            return true;
        }

        private static HashSet<string> RemoveOutfitOwnedArtifacts(ShapeSyncDatabase database,
            ShapeSyncDatabaseRegistry.OutfitEntry outfit, string identity, string databaseAssetPath,
            ShapeSyncDatabaseTransaction.EditContext transaction)
        {
            if (database == null || outfit == null || transaction == null) return new HashSet<string>(StringComparer.Ordinal);
            var prefabs = new HashSet<GameObject>();
            var ownedAssets = new HashSet<UnityEngine.Object>();
            foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(entry => entry != null))
            {
                prefabs.Add(axis.SourcePrefab);
                prefabs.Add(axis.MergedPrefab);
                prefabs.Add(axis.OutfitPrefab);
                prefabs.Add(axis.ProjectionPrefab);
            }
            foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow in outfit.PbmFollows.Where(entry => entry != null))
                foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry figure in follow.Figures.Where(entry => entry != null))
                {
                    prefabs.Add(figure.SourcePrefab);
                    prefabs.Add(figure.Figure);
                }
            foreach (ShapeSyncDatabaseRegistry.OutfitCollectionEntry collection in outfit.CollectionEntries.Where(entry => entry != null))
            {
                prefabs.Add(collection.SourcePrefab);
                prefabs.Add(collection.CollectionPrefab);
            }
            string[] artifactNames = prefabs.Where(value => value != null).Select(value => value.name).Distinct().ToArray();
            // Import-time source copies can exist before Material classification is
            // authored and therefore are not reachable from an OutfitMaterialEntry
            // or a renderer that survives the remove operation.  Their ownership is
            // nevertheless explicit in the recorded axis (identity + shape key),
            // so collect the corresponding artifact-name families as well.  Do not
            // infer ownership from an arbitrary logical-name prefix.
            // Outfit identity is the deletion boundary.  The naming contract
            // guarantees that every Outfit-owned Prefab and sub-asset starts
            // with "outfitId_" and that this prefix cannot collide with Figure
            // or another Outfit payload.  Do not walk Registry relations to
            // discover deletion candidates; stale/orphaned artifacts are
            // precisely the objects that Registry traversal cannot reach.
            var artifactPrefixes = new HashSet<string>(StringComparer.Ordinal)
            {
                identity + "_"
            };
            foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(entry => entry != null))
            {
                if (!string.IsNullOrWhiteSpace(axis.ShapeKey))
                    artifactPrefixes.Add(identity + "_" + axis.ShapeKey + "_");
                AddArtifactPrefix(axis.SourcePrefab, artifactPrefixes);
                AddArtifactPrefix(axis.MergedPrefab, artifactPrefixes);
                AddArtifactPrefix(axis.OutfitPrefab, artifactPrefixes);
                AddArtifactPrefix(axis.ProjectionPrefab, artifactPrefixes);
            }
            foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry follow in outfit.PbmFollows.Where(entry => entry != null))
                foreach (ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry figure in follow.Figures.Where(entry => entry != null))
                {
                    AddArtifactPrefix(figure.SourcePrefab, artifactPrefixes);
                    AddArtifactPrefix(figure.Figure, artifactPrefixes);
                }
            foreach (ShapeSyncDatabaseRegistry.OutfitMaterialEntry materialEntry in outfit.MaterialEntries.Where(entry => entry != null))
            {
                ownedAssets.Add(materialEntry.Material);
                ownedAssets.Add(materialEntry.Adapter);
            }
            foreach (GameObject prefab in prefabs.Where(value => value != null))
            {
                ownedAssets.Add(prefab);
                foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    ownedAssets.Add(renderer.sharedMesh);
                    foreach (Material material in renderer.sharedMaterials ?? Array.Empty<Material>()) ownedAssets.Add(material);
                }
                foreach (Animator animator in prefab.GetComponentsInChildren<Animator>(true)) ownedAssets.Add(animator.avatar);
            }
            var protectedAssets = new HashSet<UnityEngine.Object>();
            foreach (ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure in database.Registry.BaseFigures.Where(entry => entry != null))
                AddProtectedPrefabReferences(baseFigure.Figure, protectedAssets);
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(entry => entry != null))
                foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry figure in axis.Figures.Where(entry => entry != null))
                    AddProtectedPrefabReferences(figure.Figure, protectedAssets);
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry materialEntry in database.Registry.MaterialEntries.Where(entry => entry != null))
            {
                protectedAssets.Add(materialEntry.Material);
                protectedAssets.Add(materialEntry.Adapter);
            }
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in database.Registry.TextureResources
                .Where(entry => entry != null
                    && !string.Equals(entry.Owner.OutfitIdentity, identity, StringComparison.Ordinal)))
                protectedAssets.Add(resource.Texture);
            foreach (Texture texture in database.Registry.RemoveTextureResourcesOwnedByOutfit(identity)) ownedAssets.Add(texture);
            UnityEngine.Object[] subAssetsToRemove = ownedAssets
                .Where(asset => asset != null && !(asset is GameObject)
                    && !protectedAssets.Contains(asset)
                    && string.Equals(AssetDatabase.GetAssetPath(asset), databaseAssetPath, StringComparison.Ordinal))
                .ToArray();
            // All Database-owned Outfit Prefabs are direct children of
            // Intermediate.  Restrict hierarchy destruction to that boundary;
            // destroying nested/persistent GameObjects by asset path while the
            // Prefab is open can leave Unity's transform graph inconsistent.
            var hierarchyObjectsToRemove = new HashSet<GameObject>(ownedAssets.OfType<GameObject>()
                .Where(asset => asset != null
                    && !protectedAssets.Contains(asset)
                    && IsDirectIntermediateChildForCleanup(database, asset)));
            Transform intermediate = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            if (intermediate != null)
            {
                Transform[] namedChildren = intermediate.Cast<Transform>()
                    .Where(value => value != null
                        && (artifactNames.Contains(value.name)
                            || artifactPrefixes.Any(prefix => value.name.StartsWith(prefix, StringComparison.Ordinal))))
                    .ToArray();
                foreach (Transform child in namedChildren)
                    hierarchyObjectsToRemove.Add(child.gameObject);
            }
            foreach (GameObject prefab in hierarchyObjectsToRemove)
            {
                // The object is part of the loaded Prefab hierarchy.  Detach it
                // through the hierarchy first; calling RemoveObjectFromAsset on
                // a hierarchy object while its renderers still reference
                // sub-assets can crash Unity during SavePrefabAsset's transform
                // rebuild.
                UnityEngine.Object.DestroyImmediate(prefab);
            }

            // Only after all hierarchy references have been removed do we detach
            // the referenced Mesh/Material/Texture sub-assets.  This ordering is
            // required by Unity's Prefab save path and avoids native hot-reload
            // crashes while preserving the deletion contract.
            foreach (UnityEngine.Object asset in subAssetsToRemove)
                transaction.RemoveSubAsset(asset);
            return artifactPrefixes;
        }

        private static bool RemoveNamedOutfitSubAssetsAfterPrefabEdit(string databaseAssetPath,
            IReadOnlyCollection<string> artifactPrefixes, string identity, out string diagnostic)
        {
            diagnostic = null;
            if (string.IsNullOrWhiteSpace(databaseAssetPath) || artifactPrefixes == null || artifactPrefixes.Count == 0) return true;
            ShapeSyncDatabase reopened = AssetDatabase.LoadAssetAtPath<ShapeSyncDatabase>(databaseAssetPath);
            if (reopened == null || reopened.Registry == null) return true;
            var protectedAssets = new HashSet<UnityEngine.Object>();
            foreach (ShapeSyncDatabaseRegistry.BaseFigureEntry baseFigure in reopened.Registry.BaseFigures.Where(entry => entry != null))
                AddProtectedPrefabReferences(baseFigure.Figure, protectedAssets);
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in reopened.Registry.FigureAxes.Where(entry => entry != null))
                foreach (ShapeSyncDatabaseRegistry.AxisFigureEntry figure in axis.Figures.Where(entry => entry != null))
                    AddProtectedPrefabReferences(figure.Figure, protectedAssets);
            foreach (ShapeSyncDatabaseRegistry.MaterialEntry materialEntry in reopened.Registry.MaterialEntries.Where(entry => entry != null))
            {
                protectedAssets.Add(materialEntry.Material);
                protectedAssets.Add(materialEntry.Adapter);
            }
            foreach (ShapeSyncDatabaseRegistry.TextureResourceEntry resource in reopened.Registry.TextureResources
                .Where(entry => entry != null && !string.Equals(entry.Owner.OutfitIdentity, identity, StringComparison.Ordinal)))
                protectedAssets.Add(resource.Texture);
            try
            {
                UnityEngine.Object[] namedSubAssets = AssetDatabase.LoadAllAssetsAtPath(databaseAssetPath)
                    .Where(asset => asset != null && !(asset is GameObject) && !(asset is Component)
                        && !protectedAssets.Contains(asset)
                        && artifactPrefixes.Any(prefix => asset.name.StartsWith(prefix, StringComparison.Ordinal)))
                    .ToArray();
                if (namedSubAssets.Length == 0) return true;
                if (!ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(databaseAssetPath, (_, _, transaction) =>
                {
                    foreach (UnityEngine.Object asset in namedSubAssets)
                        transaction.RemoveSubAsset(asset);
                }, out diagnostic)) return false;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "Mesh Outfit owned sub-assets could not be removed: " + exception.Message;
                return false;
            }
        }

        private static void AddArtifactPrefix(GameObject prefab, HashSet<string> prefixes)
        {
            if (prefab != null && prefixes != null && !string.IsNullOrWhiteSpace(prefab.name))
                prefixes.Add(prefab.name + "_");
        }

        private static bool IsDirectIntermediateChildForCleanup(ShapeSyncDatabase database, GameObject asset)
        {
            if (database == null || asset == null) return false;
            Transform intermediate = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            return intermediate != null && asset.transform.parent == intermediate;
        }

        private static void AddProtectedPrefabReferences(GameObject prefab, HashSet<UnityEngine.Object> protectedAssets)
        {
            if (prefab == null || protectedAssets == null) return;
            protectedAssets.Add(prefab);
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                protectedAssets.Add(renderer.sharedMesh);
                foreach (Material material in renderer.sharedMaterials ?? Array.Empty<Material>()) protectedAssets.Add(material);
            }
            foreach (Animator animator in prefab.GetComponentsInChildren<Animator>(true)) protectedAssets.Add(animator.avatar);
        }

        private bool CanSaveFigure() => IsFigureDetailDirty();
        private bool CanSaveNormals() => database != null && database.Registry != null && IsNormalsDetailDirty();
        private bool CanSaveMaterialEntries() => materialDrafts.Count != 0 && IsMaterialsDetailDirty();
        private bool CanSaveTextureDrafts() => IsTexturesDetailDirty();

        private void AcceptTextureDraft() { acceptedTextureDraftNames = textureDrafts.Select(draft => draft.Name).ToList(); }
        private void DiscardTextureDraft() { ResetTextureDraft(); EnsureTextureDrafts(); diagnostic = null; }
        private void ResetTextureDraft() { textureDrafts.Clear(); acceptedTextureDraftNames.Clear(); removedTextureDraftNames.Clear(); newTextureName = null; newTexture = null; }

        [Serializable]
        private sealed class TextureResourceDraft
        {
            [SerializeField] private string originalName;
            [SerializeField] private string name;
            [SerializeField] private Texture texture;
            internal string OriginalName => originalName;
            internal string Name { get => name; set => name = value; }
            internal Texture Texture => texture;
            internal TextureResourceDraft(string original, string value, Texture source) { originalName = original; name = value; texture = source; }
        }

        [Serializable]
        private sealed class MaterialEntryDraft
        {
            [SerializeField] private SkinnedMeshRenderer renderer;
            [SerializeField] private int materialSlot;
            [SerializeField] private Material sourceMaterial;
            [SerializeField] private Texture previewTexture;
            [SerializeField] private string entryName;
            [SerializeField] private string originalEntryName;
            internal SkinnedMeshRenderer Renderer => renderer;
            internal int MaterialSlot => materialSlot;
            internal Material SourceMaterial => sourceMaterial;
            internal Texture PreviewTexture => previewTexture;
            internal string EntryName { get => entryName; set => entryName = value; }
            internal string OriginalEntryName { get => originalEntryName; set => originalEntryName = value; }
            internal MaterialEntryDraft(SkinnedMeshRenderer renderer, int slot, Material material, Texture preview, string name, string originalName) { this.renderer = renderer; materialSlot = slot; sourceMaterial = material; previewTexture = preview; entryName = name; originalEntryName = originalName; }
        }

        private NormalDraft GetOrCreateNormalDraft(MaterialEntryDraft material, string shapeKey)
        {
            if (material == null || !IsFigureNormalEntryMaterial(material)) return null;
            NormalDraft existing = normalDrafts.FirstOrDefault(item => item.ShapeKey == shapeKey
                && (string.Equals(item.MaterialEntryName, material.OriginalEntryName, StringComparison.Ordinal)
                    || string.Equals(item.MaterialEntryName, material.EntryName, StringComparison.Ordinal)));
            if (existing != null) return existing;

            string materialName = material.OriginalEntryName ?? material.EntryName;
            Texture texture = database.Registry.NormalEntries.FirstOrDefault(entry => entry != null
                && (string.Equals(entry.MaterialEntryName, material.OriginalEntryName, StringComparison.Ordinal)
                    || string.Equals(entry.MaterialEntryName, material.EntryName, StringComparison.Ordinal))
                && entry.ShapeKey == shapeKey)?.Texture;
            var created = new NormalDraft(materialName, shapeKey, texture);
            normalDrafts.Add(created);
            acceptedNormalTextures.Add(texture);
            return created;
        }

        private void EnsureFigureNormalEntryDrafts()
        {
            if (figureNormalEntriesInitialized || database == null || database.Registry == null) return;
            EnsureMaterialDrafts();
            // Figure Normal Entry is the relationship root.  A Normal draft is never
            // materialized merely because a Material Entry or FBM exists.
            figureNormalEntryMaterialNames = database.Registry.FigureNormalEntries
                .Where(entry => entry != null && FindMaterialDraft(entry.MaterialEntryName) != null)
                .Select(entry => entry.MaterialEntryName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (string materialName in figureNormalEntryMaterialNames)
                GetOrCreateNormalDraft(FindMaterialDraft(materialName), ShapeSyncDatabaseRegistry.BaseShapeKey);
            acceptedFigureNormalEntryMaterialNames = new List<string>(figureNormalEntryMaterialNames);
            figureNormalEntriesInitialized = true;
        }

        private bool TryAddFigureNormalEntry()
        {
            EnsureFigureNormalEntryDrafts();
            if (databaseFigurePrefab == null) return false;
            string[] available = GetAvailableFigureNormalEntryMaterialNames(null);
            if (available.Length == 0) return false;
            figureNormalEntryMaterialNames.Add(available[0]);
            MaterialEntryDraft material = FindMaterialDraft(available[0]);
            if (material != null) GetOrCreateNormalDraft(material, ShapeSyncDatabaseRegistry.BaseShapeKey);
            return true;
        }

        private bool TryRemoveTextureDraft(int index)
        {
            EnsureTextureDrafts();
            if (index < 0 || index >= textureDrafts.Count) return false;
            if (!string.IsNullOrEmpty(textureDrafts[index].OriginalName) && !removedTextureDraftNames.Contains(textureDrafts[index].OriginalName))
                removedTextureDraftNames.Add(textureDrafts[index].OriginalName);
            textureDrafts.RemoveAt(index);
            diagnostic = null;
            return true;
        }

        /// <summary>Admits a user-selected PBM source, or explicitly reuses a stored Database Figure for an unspecified row.</summary>
        private static bool TryAdmitPbmReplacementSource(GameObject selectedSource, GameObject storedDatabaseFigure,
            out ShapeSyncFigureImportAdmission admission, out string sourceDiagnostic)
        {
            if (selectedSource != null) return AdmitAxisFigure(selectedSource, out admission, out sourceDiagnostic);
            return ShapeSyncFigureImport.TryAdmitStoredDatabaseFigure(storedDatabaseFigure, out admission, out sourceDiagnostic);
        }

        private bool TryRemoveFigureNormalEntry(int index)
        {
            EnsureFigureNormalEntryDrafts();
            if (index < 0 || index >= figureNormalEntryMaterialNames.Count) return false;
            ClearFigureNormalEntryDrafts(figureNormalEntryMaterialNames[index]);
            figureNormalEntryMaterialNames.RemoveAt(index);
            return true;
        }

        private void ReplaceFigureNormalEntry(int index, string nextName)
        {
            if (index < 0 || index >= figureNormalEntryMaterialNames.Count || string.IsNullOrWhiteSpace(nextName)) return;
            ClearFigureNormalEntryDrafts(figureNormalEntryMaterialNames[index]);
            figureNormalEntryMaterialNames[index] = nextName;
            GetOrCreateNormalDraft(FindMaterialDraft(nextName), ShapeSyncDatabaseRegistry.BaseShapeKey);
        }

        private void ClearFigureNormalEntryDrafts(string materialEntryName)
        {
            MaterialEntryDraft material = FindMaterialDraft(materialEntryName);
            if (material == null) return;
            // Existing FBM cells must be cleared together with Base when the owning
            // Figure Normal Entry relation is removed or replaced.
            IEnumerable<string> shapes = normalDrafts
                .Where(draft => IsSameMaterialEntry(draft.MaterialEntryName, material))
                .Select(draft => draft.ShapeKey)
                .Concat(database.Registry.NormalEntries
                    .Where(entry => entry != null && IsSameMaterialEntry(entry.MaterialEntryName, material))
                    .Select(entry => entry.ShapeKey))
                .Distinct(StringComparer.Ordinal);
            foreach (string shape in shapes)
            {
                NormalDraft draft = normalDrafts.FirstOrDefault(item => item.ShapeKey == shape && IsSameMaterialEntry(item.MaterialEntryName, material));
                if (draft == null)
                {
                    string storedName = material.OriginalEntryName ?? material.EntryName;
                    Texture storedTexture = database.Registry.NormalEntries.FirstOrDefault(entry => entry != null && entry.ShapeKey == shape && IsSameMaterialEntry(entry.MaterialEntryName, material))?.Texture;
                    draft = new NormalDraft(storedName, shape, storedTexture);
                    normalDrafts.Add(draft);
                    acceptedNormalTextures.Add(storedTexture);
                }
                draft.Texture = null;
            }
        }

        private string[] GetAvailableFigureNormalEntryMaterialNames(string currentName)
        {
            EnsureMaterialDrafts();
            return materialDrafts.Select(draft => draft.EntryName)
                .Where(name => string.Equals(name, currentName, StringComparison.Ordinal) || !figureNormalEntryMaterialNames.Contains(name))
                .ToArray();
        }

        private MaterialEntryDraft FindMaterialDraft(string entryName)
        {
            return materialDrafts.FirstOrDefault(draft => string.Equals(draft.EntryName, entryName, StringComparison.Ordinal)
                || string.Equals(draft.OriginalEntryName, entryName, StringComparison.Ordinal));
        }

        private bool IsFigureNormalEntryMaterial(MaterialEntryDraft material)
        {
            return material != null && figureNormalEntryMaterialNames.Any(name => IsSameMaterialEntry(name, material));
        }

        private static bool IsSameMaterialEntry(string materialEntryName, MaterialEntryDraft material)
        {
            return material != null && (string.Equals(materialEntryName, material.EntryName, StringComparison.Ordinal)
                || string.Equals(materialEntryName, material.OriginalEntryName, StringComparison.Ordinal));
        }

        private NormalDraft FindBaseNormalDraft(string materialEntryName)
        {
            return normalDrafts.FirstOrDefault(draft => draft.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey
                && (string.Equals(draft.MaterialEntryName, materialEntryName, StringComparison.Ordinal)
                    || string.Equals(FindMaterialDraft(materialEntryName)?.OriginalEntryName, draft.MaterialEntryName, StringComparison.Ordinal)));
        }

        private bool HasBaseNormalDraftChanges()
        {
            if (!figureNormalEntryMaterialNames.SequenceEqual(acceptedFigureNormalEntryMaterialNames)) return true;
            for (int index = 0; index < normalDrafts.Count; index++)
                if (normalDrafts[index].ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey
                    && (index >= acceptedNormalTextures.Count || normalDrafts[index].Texture != acceptedNormalTextures[index])) return true;
            return false;
        }

        private bool HasFbmNormalDraftChanges()
        {
            for (int index = 0; index < normalDrafts.Count; index++)
                if (normalDrafts[index].ShapeKey != ShapeSyncDatabaseRegistry.BaseShapeKey
                    && (index >= acceptedNormalTextures.Count || normalDrafts[index].Texture != acceptedNormalTextures[index])) return true;
            return false;
        }

        private void AcceptFigureNormalEntryDrafts()
        {
            acceptedFigureNormalEntryMaterialNames = new List<string>(figureNormalEntryMaterialNames);
            acceptedNormalTextures = normalDrafts.Select(draft => draft.Texture).ToList();
        }

        private void ResetFigureNormalEntryDrafts()
        {
            figureNormalEntryMaterialNames.Clear();
            acceptedFigureNormalEntryMaterialNames.Clear();
            normalDrafts.Clear();
            acceptedNormalTextures.Clear();
            figureNormalEntriesInitialized = false;
        }

        private ShapeSyncNormalEntryAuthoring.Assignment[] ToChangedNormalAssignments()
        {
            return normalDrafts.Where((draft, index) => index >= acceptedNormalTextures.Count || draft.Texture != acceptedNormalTextures[index])
                .Select(draft =>
                {
                    MaterialEntryDraft owner = materialDrafts.FirstOrDefault(material => string.Equals(material.OriginalEntryName, draft.MaterialEntryName, StringComparison.Ordinal))
                        ?? materialDrafts.FirstOrDefault(material => string.Equals(material.EntryName, draft.MaterialEntryName, StringComparison.Ordinal));
                    return new { Owner = owner, Draft = draft };
                })
                // Removing a Figure Normal Entry is an ownership-relation operation.
                // Its Base/FBM cells are removed by TrySetFigureNormalEntries, not sent
                // back as invalid matrix assignments to the Normal save boundary.
                .Where(item => IsFigureNormalEntryMaterial(item.Owner))
                .Select(item => new ShapeSyncNormalEntryAuthoring.Assignment(item.Owner.EntryName, item.Draft.ShapeKey, item.Draft.Texture,
                    item.Draft.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey
                        ? ShapeSyncDatabaseRegistry.TextureResourceOwner.FigureBase
                        : ShapeSyncDatabaseRegistry.TextureResourceOwner.FigureFbm(item.Draft.ShapeKey)))
                .ToArray();
        }

        private ShapeSyncNormalEntryAuthoring.Assignment[] ToChangedBaseNormalAssignments()
        {
            return ToChangedNormalAssignments()
                .Where(assignment => assignment.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey)
                .ToArray();
        }

        private void DrawGenerationDetail()
        {
            EnsureGenerationDraft();
            GUILayout.Label("Generation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Choose an output root when generating. The Figure Prefab is saved directly in that root; the five paths below are relative subfolders saved to the Database.", MessageType.Info);
            generationRegistriesPath = EditorGUILayout.TextField("Registries", generationRegistriesPath);
            generationBindingsPath = EditorGUILayout.TextField("Bindings", generationBindingsPath);
            generationMaterialsPath = EditorGUILayout.TextField("Materials", generationMaterialsPath);
            generationTexturesPath = EditorGUILayout.TextField("Textures", generationTexturesPath);
            generationOutfitsPath = EditorGUILayout.TextField("Outfits", generationOutfitsPath);
            ShapeSyncDatabaseOptionalUiProvider.TryDrawGenerationVrmPath(this);
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!IsGenerationDetailDirty()))
                    if (GUILayout.Button("Save to Database", GUILayout.Height(DetailSaveButtonHeight))) TrySaveGeneration(out _);
                using (new EditorGUI.DisabledScope(database == null || IsGenerationDetailDirty()))
                {
                    if (GUILayout.Button("Generate", GUILayout.Height(DetailSaveButtonHeight)))
                    {
                        string root = EditorUtility.OpenFolderPanel("Generate ShapeSync Figure", Application.dataPath, string.Empty);
                        if (!string.IsNullOrWhiteSpace(root)) TryGenerate(ResolveGenerationRoot(root), out _);
                    }
                }
            }
            if (!string.IsNullOrEmpty(diagnostic))
                EditorGUILayout.HelpBox(diagnostic, diagnostic.StartsWith("ShapeGenerateCatalogMissing", StringComparison.Ordinal) ? MessageType.Warning : MessageType.Error);
        }

        private void EnsureGenerationDraft()
        {
            if (generationDraftDatabase == database) return;
            ShapeSyncDatabaseRegistry.GenerationPathSettings settings = database?.Registry?.GenerationPaths;
            generationRegistriesPath = settings?.RegistriesPath ?? ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultRegistriesPath;
            generationBindingsPath = settings?.BindingsPath ?? ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultBindingsPath;
            generationMaterialsPath = settings?.MaterialsPath ?? ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultMaterialsPath;
            generationTexturesPath = settings?.TexturesPath ?? ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultTexturesPath;
            generationOutfitsPath = settings?.OutfitsPath ?? ShapeSyncDatabaseRegistry.GenerationPathSettings.DefaultOutfitsPath;
            generationDraftDatabase = database;
        }

        private void ResetGenerationDraft()
        {
            generationDraftDatabase = null;
            EnsureGenerationDraft();
        }

        private bool IsGenerationDetailDirty()
        {
            if (selectedSection != Section.Generation || database?.Registry == null) return false;
            EnsureGenerationDraft();
            ShapeSyncDatabaseRegistry.GenerationPathSettings saved = database.Registry.GenerationPaths;
            return !string.Equals(generationRegistriesPath, saved.RegistriesPath, StringComparison.Ordinal)
                || !string.Equals(generationBindingsPath, saved.BindingsPath, StringComparison.Ordinal)
                || !string.Equals(generationMaterialsPath, saved.MaterialsPath, StringComparison.Ordinal)
                || !string.Equals(generationTexturesPath, saved.TexturesPath, StringComparison.Ordinal)
                || !string.Equals(generationOutfitsPath, saved.OutfitsPath, StringComparison.Ordinal)
                || ShapeSyncDatabaseOptionalUiProvider.IsGenerationVrmPathDirty(this);
        }

        private bool TrySaveGeneration(out string saveDiagnostic)
        {
            saveDiagnostic = null;
            EnsureGenerationDraft();
            if (database?.Registry == null)
            { saveDiagnostic = "Generation path Save requires an opened ShapeSync Database."; diagnostic = saveDiagnostic; return false; }
            if (!ShapeSyncDatabaseRegistry.TryValidateGenerationPaths(generationRegistriesPath, generationBindingsPath,
                generationMaterialsPath, generationTexturesPath, generationOutfitsPath, out saveDiagnostic))
            { diagnostic = saveDiagnostic; return false; }
            saveDiagnostic = ShapeSyncDatabaseOptionalUiProvider.ValidateGenerationVrmPath(this);
            if (!string.IsNullOrEmpty(saveDiagnostic))
            { diagnostic = saveDiagnostic; return false; }
            if (!ShapeSyncDatabaseDirectEdit.TryEdit(database, "Set ShapeSync Generation Paths",
                (ShapeSyncDatabaseRegistry registry, out string registryDiagnostic) =>
            {
                return registry.TrySetGenerationPaths(generationRegistriesPath, generationBindingsPath,
                    generationMaterialsPath, generationTexturesPath, generationOutfitsPath, out registryDiagnostic);
            }, out saveDiagnostic))
            { diagnostic = saveDiagnostic; return false; }
            saveDiagnostic = ShapeSyncDatabaseOptionalUiProvider.SaveGenerationVrmPath(this);
            if (!string.IsNullOrEmpty(saveDiagnostic))
            {
                diagnostic = saveDiagnostic;
                return false;
            }
            ResetGenerationDraft();
            diagnostic = null;
            return true;
        }

        private bool TryGenerate(string rootPath, out string generateDiagnostic)
        {
            // A successful Generate owns the current diagnostic state.  Clear any
            // warning left by a previous run before starting the new admission and
            // pipeline, so an emptied output folder cannot keep displaying stale
            // catalog-cleanup messaging after a successful re-Generate.
            diagnostic = null;
            generateDiagnostic = null;
            generateDiagnostics = Array.Empty<ShapeSyncDatabaseDiagnostic>();
            EnsureGenerationDraft();
            if (!ShapeSyncDatabaseValidator.TryValidateForGeneration(database, out IReadOnlyList<ShapeSyncDatabaseDiagnostic> validationDiagnostics))
            {
                generateDiagnostics = validationDiagnostics == null
                    ? Array.Empty<ShapeSyncDatabaseDiagnostic>()
                    : validationDiagnostics.ToArray();
                generateDiagnostic = FormatGenerateDiagnostics(generateDiagnostics);
                diagnostic = generateDiagnostic;
                return false;
            }
            ShapeSyncDatabaseRegistry.GenerationPathSettings settings = database?.Registry?.GenerationPaths;
            if (settings == null || !ShapeSyncDatabaseRegistry.TryValidateGenerationPaths(settings.RegistriesPath, settings.BindingsPath,
                settings.MaterialsPath, settings.TexturesPath, settings.OutfitsPath, out generateDiagnostic))
            {
                diagnostic = generateDiagnostic ?? "GenerationPathInvalid: Database Generation Path settings are unavailable.";
                return false;
            }
            var generatedPaths = new HashSet<string>(StringComparer.Ordinal);
            if (!ShapeSyncGenerateOutputSnapshot.TryCreate(rootPath, out ShapeSyncGenerateOutputSnapshot outputSnapshot, out string snapshotDiagnostic))
            {
                generateDiagnostic = snapshotDiagnostic;
                diagnostic = generateDiagnostic;
                return false;
            }
            string catalogWarning = null;
            try
            {
                // The catalog is the ownership index for the complete Figure /
                // Outfit / Shape output tree.  Validate it before any layer stages
                // so an invalid index cannot be discovered only after Figure or
                // Outfit assets have already been staged.
                if (!ShapeSyncGenerateCatalog.TryRead(rootPath, out _, out _, out string catalogDiagnostic))
                {
                    generateDiagnostic = catalogDiagnostic;
                    diagnostic = generateDiagnostic;
                    return false;
                }
                catalogWarning = ShapeSyncGenerateCatalog.NormalizeDiagnostic(rootPath, generatedPaths, catalogDiagnostic);
                if (!GenerateFigure(database, rootPath, settings.RegistriesPath, settings.BindingsPath, settings.MaterialsPath, settings.TexturesPath, generatedPaths, out generateDiagnostic))
                {
                    generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, generateDiagnostic);
                    diagnostic = generateDiagnostic;
                    return false;
                }
                if (!GenerateOutfit(database, rootPath, settings.BindingsPath, settings.OutfitsPath, generatedPaths, out generateDiagnostic))
                {
                    generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, generateDiagnostic);
                    diagnostic = generateDiagnostic;
                    return false;
                }
                if (!ShapeSyncDatabaseOptionalRegistryProvider.TryGenerateVrm(database, rootPath, generatedPaths, out generateDiagnostic))
                {
                    generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, generateDiagnostic);
                    diagnostic = generateDiagnostic;
                    return false;
                }
                if (!GenerateShape(database, rootPath, generatedPaths, out generateDiagnostic))
                {
                    generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, generateDiagnostic);
                    diagnostic = generateDiagnostic;
                    return false;
                }
                if (!ShapeSyncDatabaseOptionalRegistryProvider.TryFinalizeVrm(database, rootPath, generatedPaths, out generateDiagnostic))
                {
                    generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, generateDiagnostic);
                    diagnostic = generateDiagnostic;
                    return false;
                }
                if (!outputSnapshot.TryCommit(out string commitDiagnostic))
                {
                    generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, commitDiagnostic);
                    diagnostic = generateDiagnostic;
                    return false;
                }
                if (string.IsNullOrEmpty(generateDiagnostic)) generateDiagnostic = catalogWarning;
                diagnostic = generateDiagnostic;
                return true;
            }
            catch (Exception exception)
            {
                generateDiagnostic = RollbackGenerate(outputSnapshot, generatedPaths, "GenerateUnexpected: " + exception.Message);
                diagnostic = generateDiagnostic;
                return false;
            }
            finally
            {
                outputSnapshot.Dispose();
            }
        }

        private static string RollbackGenerate(ShapeSyncGenerateOutputSnapshot outputSnapshot,
            ICollection<string> generatedPaths, string failureDiagnostic)
        {
            generatedPaths?.Clear();
            if (outputSnapshot == null) return failureDiagnostic;
            if (outputSnapshot.TryRestore(out string rollbackDiagnostic)) return failureDiagnostic;
            return string.IsNullOrEmpty(failureDiagnostic)
                ? rollbackDiagnostic
                : failureDiagnostic + "\n" + rollbackDiagnostic;
        }

        private static string FormatGenerateDiagnostics(IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0) return null;
            return string.Join("\n", diagnostics.Select(item => item.ToString()));
        }

        private static string ResolveGenerationRoot(string selectedFolderPath)
        {
            // OpenFolderPanel permits folder creation. Synchronize it before converting the
            // absolute path, otherwise a just-created folder has no AssetDatabase path yet.
            RefreshAssetDatabase();
            return ToProjectRelativePath(selectedFolderPath);
        }

        private ShapeSyncNormalEntryAuthoring.Assignment[] ToBaseNormalAssignments()
        {
            return figureNormalEntryMaterialNames
                .Select(materialEntryName => new ShapeSyncNormalEntryAuthoring.Assignment(materialEntryName,
                    ShapeSyncDatabaseRegistry.BaseShapeKey, FindBaseNormalDraft(materialEntryName)?.Texture))
                .ToArray();
        }

        private static bool TryValidateRequiredNormalTextures(IEnumerable<ShapeSyncNormalEntryAuthoring.Assignment> assignments, string detailName, out string validationDiagnostic)
        {
            validationDiagnostic = null;
            foreach (ShapeSyncNormalEntryAuthoring.Assignment assignment in assignments)
            {
                if (assignment.Source != null) continue;
                validationDiagnostic = detailName + " Normal cannot be None. Remove the Figure Normal Entry to remove this Normal configuration.";
                return false;
            }
            return true;
        }

        [Serializable]
        private sealed class NormalDraft
        {
            [SerializeField] private string materialEntryName;
            [SerializeField] private string shapeKey;
            [SerializeField] private Texture texture;
            internal string MaterialEntryName { get => materialEntryName; set => materialEntryName = value; }
            internal string ShapeKey => shapeKey;
            internal Texture Texture { get => texture; set => texture = value; }
            internal NormalDraft(string material, string shape, Texture value) { materialEntryName = material; shapeKey = shape; texture = value; }
        }

        [Serializable]
        private sealed class FbmAxisDraft
        {
            [SerializeField] private string name;
            [SerializeField] private GameObject sourcePrefab;
            [SerializeField] private bool importMaterialsAndTextures;
            internal string Name { get => name; set => name = value; }
            internal GameObject SourcePrefab { get => sourcePrefab; set => sourcePrefab = value; }
            internal bool ImportMaterialsAndTextures { get => importMaterialsAndTextures; set => importMaterialsAndTextures = value; }
            internal void AssignSourcePrefab(GameObject value)
            {
                sourcePrefab = value;
                name = GetNameAfterPrefabAssignment(name, value);
            }
        }

        [Serializable]
        private sealed class FbmAxisRedefinitionDraft
        {
            [SerializeField] private string originalName;
            [SerializeField] private string name;
            [SerializeField] private GameObject sourcePrefab;
            [SerializeField] private bool importMaterialsAndTextures;
            [SerializeField] private bool originalImportMaterialsAndTextures;
            internal FbmAxisRedefinitionDraft(string value, bool importAll)
            { originalName = value; name = value; importMaterialsAndTextures = originalImportMaterialsAndTextures = importAll; }
            internal string OriginalName => originalName;
            internal string Name { get => name; set => name = value; }
            internal GameObject SourcePrefab { get => sourcePrefab; set => sourcePrefab = value; }
            internal bool ImportMaterialsAndTextures { get => importMaterialsAndTextures; set => importMaterialsAndTextures = value; }
            internal void AssignSourcePrefab(GameObject value)
            {
                sourcePrefab = value;
                name = GetNameAfterPrefabAssignment(name, value);
            }
            internal bool IsChanged => sourcePrefab != null || !string.Equals(name, originalName, StringComparison.Ordinal) || importMaterialsAndTextures != originalImportMaterialsAndTextures;
            internal bool IsNameOnlyChange => sourcePrefab == null && !string.Equals(name, originalName, StringComparison.Ordinal) && importMaterialsAndTextures == originalImportMaterialsAndTextures;
        }

        [Serializable]
        private sealed class PbmAxisDraft
        {
            [Serializable]
            internal sealed class SourceRow { [SerializeField] internal string fbmName; [SerializeField] internal GameObject prefab; }
            [SerializeField] private string name;
            [SerializeField] private GameObject basePrefab;
            [SerializeField] private List<SourceRow> sources = new List<SourceRow>();
            internal string Name { get => name; set => name = value; }
            internal GameObject BasePrefab { get => basePrefab; set => basePrefab = value; }
            internal GameObject GetSource(string fbmName) => sources.FirstOrDefault(row => row.fbmName == fbmName)?.prefab;
            internal void SetSource(string fbmName, GameObject prefab)
            {
                SourceRow row = sources.FirstOrDefault(item => item.fbmName == fbmName);
                if (row == null) { row = new SourceRow { fbmName = fbmName }; sources.Add(row); }
                row.prefab = prefab;
            }
        }

        [Serializable]
        private sealed class PbmAxisRedefinitionDraft
        {
            [SerializeField] private string originalName;
            [SerializeField] private string name;
            [SerializeField] private GameObject basePrefab;
            [SerializeField] private List<PbmAxisDraft.SourceRow> sources = new List<PbmAxisDraft.SourceRow>();
            internal PbmAxisRedefinitionDraft(string value) { originalName = value; name = value; }
            internal string OriginalName => originalName;
            internal string Name { get => name; set => name = value; }
            internal GameObject BasePrefab { get => basePrefab; set => basePrefab = value; }
            internal GameObject GetSource(string fbmName) => sources.FirstOrDefault(row => row.fbmName == fbmName)?.prefab;
            internal void SetSource(string fbmName, GameObject prefab)
            {
                PbmAxisDraft.SourceRow row = sources.FirstOrDefault(item => item.fbmName == fbmName);
                if (row == null) { row = new PbmAxisDraft.SourceRow { fbmName = fbmName }; sources.Add(row); }
                row.prefab = prefab;
            }
            internal bool HasPrefabChange => basePrefab != null || sources.Any(row => row.prefab != null);
            internal bool IsChanged => !string.Equals(originalName, name, StringComparison.Ordinal) || HasPrefabChange;
        }

        private bool IsFigureDetailDirty()
        {
            return selectedSection == Section.Figure &&
                (!string.Equals(figureName, acceptedFigureName, StringComparison.Ordinal) || figurePrefab != acceptedFigurePrefab || pcmSlots != acceptedPcmSlots);
        }

        private bool IsNormalsDetailDirty() => selectedSection == Section.Normals && HasBaseNormalDraftChanges();

        private bool IsExtraMorphsDetailDirty() => selectedSection == Section.ExtraMorphs
            && !keptRawMorphs.SequenceEqual(acceptedKeptRawMorphs);

        private void AcceptFigureDraft()
        {
            acceptedFigureName = figureName;
            acceptedFigurePrefab = figurePrefab;
        }

        private void AcceptExtraMorphDraft()
        {
            acceptedKeptRawMorphs = new List<string>(keptRawMorphs);
        }

        private void AcceptPcmSlotsDraft() { acceptedPcmSlots = pcmSlots; }

        private void DiscardFigureDraft()
        {
            figureName = acceptedFigureName;
            figurePrefab = acceptedFigurePrefab;
            pcmSlots = acceptedPcmSlots;
            ResolveDatabaseFigurePrefab();
            diagnostic = null;
        }

        private void DiscardNormalsDraft()
        {
            DiscardNormalDrafts();
            ResetFigureNormalEntryDrafts();
            EnsureFigureNormalEntryDrafts();
            diagnostic = null;
        }

        private void DiscardFbmNormalDrafts()
        {
            DiscardNormalDrafts();
            diagnostic = null;
        }
        internal void AssignFigurePrefabFromUiForTest(GameObject prefab) => AssignFigurePrefabFromUi(prefab);

        private void DiscardExtraMorphDraft()
        {
            keptRawMorphs = new List<string>(acceptedKeptRawMorphs);
            diagnostic = null;
        }

        private void ResetFigureDraft()
        {
            figureName = null;
            figurePrefab = null;
            databaseFigurePrefab = null;
            if (database != null && database.Registry != null)
            {
                if (database.Registry.TryGetSingleBaseFigureForOpen(database, out ShapeSyncDatabaseRegistry.BaseFigureEntry entry, out string registryDiagnostic) && entry != null)
                {
                    figureName = entry.Name;
                    databaseFigurePrefab = entry.Figure;
                }
                else if (!string.IsNullOrEmpty(registryDiagnostic))
                {
                    diagnostic = registryDiagnostic;
                }
            }
            pcmSlots = database != null && database.Registry != null ? database.Registry.PcmSlots : 10;
            keptRawMorphs = database != null && database.Registry != null ? database.Registry.KeptRawBlendShapeNames.ToList() : new List<string>();
            ResetFigureNormalEntryDrafts();
            AcceptFigureDraft();
            AcceptPcmSlotsDraft();
            AcceptExtraMorphDraft();
        }

        private void ResetOutfitDraft()
        {
            ResetOutfitOrderDraft();
            if (GetSelectedOutfit() != null)
            {
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = GetSelectedOutfit();
                outfitNameDraft = outfit.StoredDisplayName;
                acceptedOutfitNameDraft = outfit.StoredDisplayName;
                ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures.FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
                outfitSourcePrefabDraft = baseAxis?.SourcePrefab;
                acceptedOutfitSourcePrefabDraft = outfitSourcePrefabDraft;
                outfitMaterialClassificationDrafts.Clear();
                outfitFbmSourceDrafts.Clear();
                outfitNormalEntryMaterialNames.Clear();
                acceptedOutfitNormalEntryMaterialNames.Clear();
                outfitNormalDrafts.Clear();
                outfitNormalDraftsInitialized = false;
                outfitPbmFollowDrafts.Clear();
                outfitCollectionDrafts.Clear();
                materialOutfitTextureDrafts.Clear();
                figureMaskDrafts.Clear();
                materialOutfitTextureDraftsInitialized = false;
                figureMaskDraftsInitialized = false;
                outfitCollectionKind = acceptedOutfitCollectionKind = outfit.CollectionKind;
                useProjectionForFullCollection = acceptedUseProjectionForFullCollection = outfit.UseProjectionForFullCollection;
                treeView?.Reload();
                return;
            }
            selectedOutfitIdentity = null;
            outfitNameDraft = null;
            acceptedOutfitNameDraft = null;
            outfitSourcePrefabDraft = null;
            acceptedOutfitSourcePrefabDraft = null;
            outfitMaterialClassificationDrafts.Clear();
            outfitFbmSourceDrafts.Clear();
            outfitNormalEntryMaterialNames.Clear();
            acceptedOutfitNormalEntryMaterialNames.Clear();
            outfitNormalDrafts.Clear();
            outfitNormalDraftsInitialized = false;
            outfitPbmFollowDrafts.Clear();
            outfitCollectionDrafts.Clear();
            materialOutfitTextureDrafts.Clear();
            figureMaskDrafts.Clear();
            materialOutfitTextureDraftsInitialized = false;
            figureMaskDraftsInitialized = false;
            outfitCollectionKind = acceptedOutfitCollectionKind = ShapeSyncDatabaseRegistry.OutfitCollectionKind.None;
            useProjectionForFullCollection = acceptedUseProjectionForFullCollection = false;
            treeView?.Reload();
        }

        private void EnsureMaterialOutfitTextureDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (materialOutfitTextureDraftsInitialized || outfit == null || database?.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.MaterialOutfitTextureEntry entry in outfit.MaterialOutfitTextureEntries.Where(entry => entry != null))
            {
                Texture texture = database.Registry.TextureResources.FirstOrDefault(resource => resource != null && resource.LogicalName == entry.TextureResourceName)?.Texture;
                materialOutfitTextureDrafts.Add(new OutfitTextureDraft(entry.EntryName, texture));
            }
            materialOutfitTextureDraftsInitialized = true;
        }

        private void EnsureFigureMaskDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (figureMaskDraftsInitialized || outfit == null || database?.Registry == null) return;
            foreach (ShapeSyncDatabaseRegistry.FigureMaskEntry entry in outfit.FigureMaskEntries.Where(entry => entry != null))
            {
                Texture texture = database.Registry.TextureResources.FirstOrDefault(resource => resource != null && resource.LogicalName == entry.TextureResourceName)?.Texture;
                figureMaskDrafts.Add(new OutfitTextureDraft(entry.FigureMaterialEntryName, texture));
            }
            figureMaskDraftsInitialized = true;
        }

        private void EnsureOutfitNormalDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (outfitNormalDraftsInitialized || outfit == null) return;
            outfitNormalEntryMaterialNames = outfit.NormalDeclarations.Where(entry => entry != null)
                .Select(entry => entry.MaterialEntryName).ToList();
            acceptedOutfitNormalEntryMaterialNames = new List<string>(outfitNormalEntryMaterialNames);
            foreach (string materialEntryName in outfitNormalEntryMaterialNames) EnsureOutfitNormalCells(outfit, materialEntryName);
            outfitNormalDraftsInitialized = true;
        }

        private void EnsureOutfitPbmFollowDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (outfit == null || database?.Registry == null) return;
            if (outfitPbmFollowDrafts.Count != 0)
            {
                // Domain reloads can preserve serialized draft rows created before a
                // Database Source lost its Mesh. Clear only that persisted artifact; an
                // explicitly selected replacement remains authoritative even when it is
                // later rejected by the normal source admission.
                foreach (OutfitPbmFollowDraft draft in outfitPbmFollowDrafts)
                    foreach (OutfitPbmFollowDraft.SourceRow row in draft.Rows)
                        if (row.Prefab != null && row.Prefab == draft.GetSavedSourcePrefab(row.ShapeKey)
                            && !IsReusablePbmFollowSource(row.Prefab))
                            row.Prefab = null;
                return;
            }
            string[] shapeKeys = database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                .Select(axis => axis.Name).Prepend(ShapeSyncDatabaseRegistry.BaseShapeKey).ToArray();
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm))
            {
                ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry saved = outfit.PbmFollows.FirstOrDefault(entry => entry != null && entry.PbmAxisName == axis.Name);
                outfitPbmFollowDrafts.Add(new OutfitPbmFollowDraft(axis.Name, saved, shapeKeys));
            }
        }

        private void EnsureOutfitCollectionDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (outfit == null || outfitCollectionDrafts.Count != 0 || database?.Registry == null) return;
            foreach (string shapeKey in database.Registry.FigureAxes.Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm)
                .Select(axis => axis.Name).Prepend(ShapeSyncDatabaseRegistry.BaseShapeKey))
            {
                ShapeSyncDatabaseRegistry.OutfitCollectionEntry saved = outfit.CollectionEntries.FirstOrDefault(entry => entry != null && entry.ShapeKey == shapeKey);
                GameObject source = saved?.SourcePrefab;
                outfitCollectionDrafts.Add(new OutfitCollectionDraft(shapeKey, source, saved?.CollectionPrefab));
            }
            outfitCollectionKind = acceptedOutfitCollectionKind = outfit.CollectionKind;
            useProjectionForFullCollection = acceptedUseProjectionForFullCollection = outfit.UseProjectionForFullCollection;
        }

        private void EnsureOutfitNormalCells(ShapeSyncDatabaseRegistry.OutfitEntry outfit, string materialEntryName)
        {
            foreach (ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry axis in outfit.AxisFigures.Where(axis => axis != null))
                GetOrCreateOutfitNormalDraft(outfit, materialEntryName, axis.ShapeKey);
        }

        private OutfitNormalDraft GetOrCreateOutfitNormalDraft(ShapeSyncDatabaseRegistry.OutfitEntry outfit, string materialEntryName, string shapeKey)
        {
            OutfitNormalDraft existing = outfitNormalDrafts.FirstOrDefault(draft => draft.MaterialEntryName == materialEntryName && draft.ShapeKey == shapeKey);
            if (existing != null) return existing;
            Texture stored = outfit.NormalEntries.FirstOrDefault(entry => entry != null && entry.MaterialEntryName == materialEntryName && entry.ShapeKey == shapeKey)?.Texture;
            existing = new OutfitNormalDraft(materialEntryName, shapeKey, stored);
            outfitNormalDrafts.Add(existing);
            return existing;
        }

        private void RemoveOutfitNormalEntry(string materialEntryName)
        {
            outfitNormalEntryMaterialNames.Remove(materialEntryName);
            outfitNormalDrafts.RemoveAll(draft => draft.MaterialEntryName == materialEntryName);
            diagnostic = null;
        }

        private void EnsureOutfitMaterialClassificationDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (outfit == null || outfitMaterialClassificationDrafts.Count != 0) return;
            // Classification Save intentionally removes the source Material payload.  Re-opened
            // Detail must therefore render the persisted classification records, rather than
            // attempting to reconstruct them from the removed source assets.
            if (outfit.MaterialClassifications.Count != 0)
            {
                foreach (ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry saved in outfit.MaterialClassifications.Where(entry => entry != null))
                    outfitMaterialClassificationDrafts.Add(new OutfitMaterialClassificationDraft(saved.SourceMaterialName, saved.Classification, saved.EntryName));
                return;
            }
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit.AxisFigures.FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            SkinnedMeshRenderer renderer = baseAxis?.SourcePrefab == null ? null : baseAxis.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null) return;
            for (int materialIndex = 0; materialIndex < renderer.sharedMaterials.Length; materialIndex++)
            {
                Material material = renderer.sharedMaterials[materialIndex];
                if (material == null || materialIndex >= baseAxis.SourceMaterialNames.Count) continue;
                string sourceMaterialName = baseAxis.SourceMaterialNames[materialIndex];
                ShapeSyncDatabaseRegistry.OutfitMaterialClassificationEntry saved = outfit.MaterialClassifications.FirstOrDefault(entry => entry != null && entry.SourceMaterialName == sourceMaterialName);
                outfitMaterialClassificationDrafts.Add(new OutfitMaterialClassificationDraft(sourceMaterialName,
                    saved?.Classification ?? ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include,
                    saved?.EntryName ?? sourceMaterialName));
            }
        }

        private static bool CanEditOutfitMaterialEntryName(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            return outfit != null && outfit.MaterialClassifications != null && outfit.MaterialClassifications.Count == 0;
        }

        private static Material FindOutfitSourceMaterial(ShapeSyncDatabaseRegistry.OutfitEntry outfit, string sourceMaterialName)
        {
            ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry baseAxis = outfit?.AxisFigures
                .FirstOrDefault(entry => entry != null && entry.ShapeKey == ShapeSyncDatabaseRegistry.BaseShapeKey);
            SkinnedMeshRenderer renderer = baseAxis?.SourcePrefab == null ? null : baseAxis.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null || baseAxis.SourceMaterialNames == null) return null;
            int materialIndex = baseAxis.SourceMaterialNames.ToList().IndexOf(sourceMaterialName);
            return materialIndex >= 0 && materialIndex < renderer.sharedMaterials.Length ? renderer.sharedMaterials[materialIndex] : null;
        }

        private static Material ResolveOutfitMaterialForPreview(ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            OutfitMaterialClassificationDraft draft)
        {
            if (draft == null) return null;
            Material sourceMaterial = FindOutfitSourceMaterial(outfit, draft.SourceMaterialName);
            if (sourceMaterial != null) return sourceMaterial;
            // Classification Save removes the source Material payload.  Included
            // rows remain previewable through their persisted logical Material Entry;
            // Exclude/Projection rows intentionally have no Material Entry.
            if (draft.Classification != ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Include
                || string.IsNullOrWhiteSpace(draft.EntryName)) return null;
            return outfit?.MaterialEntries?.FirstOrDefault(entry => entry != null && entry.LogicalName == draft.EntryName)?.Material;
        }

        internal static Material ResolveOutfitMaterialForPreviewForTest(ShapeSyncDatabaseRegistry.OutfitEntry outfit,
            string sourceMaterialName, ShapeSyncDatabaseRegistry.OutfitMaterialClassification classification, string entryName)
        {
            return ResolveOutfitMaterialForPreview(outfit,
                new OutfitMaterialClassificationDraft(sourceMaterialName, classification, entryName));
        }

        private static Texture ResolveOutfitMaterialPreview(Material material)
        {
            return material == null ? null : material.mainTexture;
        }

        internal static Texture ResolveOutfitMaterialPreviewForTest(Material material) => ResolveOutfitMaterialPreview(material);

        internal static bool RequiresIrreversibleClassificationConfirmation(IEnumerable<ShapeSyncDatabaseRegistry.OutfitMaterialClassification> classifications)
        {
            return classifications != null && classifications.Any(value => value == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Exclude
                || value == ShapeSyncDatabaseRegistry.OutfitMaterialClassification.Projection);
        }

        private void EnsureOutfitFbmSourceDrafts(ShapeSyncDatabaseRegistry.OutfitEntry outfit)
        {
            if (outfit == null || outfitFbmSourceDrafts.Count != 0) return;
            foreach (ShapeSyncDatabaseRegistry.FigureAxisEntry axis in database.Registry.FigureAxes
                .Where(axis => axis != null && axis.Kind == ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm))
            {
                ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry saved = outfit.AxisFigures.FirstOrDefault(entry => entry != null && entry.ShapeKey == axis.Name);
                outfitFbmSourceDrafts.Add(new OutfitFbmSourceDraft(axis.Name, saved?.SourcePrefab));
            }
        }

        private void ResolveDatabaseFigurePrefab()
        {
            databaseFigurePrefab = database == null || string.IsNullOrWhiteSpace(figureName)
                ? null
                : database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName + "/" + figureName)?.gameObject;
        }

        private bool CanExportDatabaseFigure() => CanExportDatabaseFigure(databaseFigurePrefab);

        private bool CanExportDatabaseFigure(GameObject figure) => database != null && figure != null;

        private bool CanExportDatabaseOutfit(GameObject outfit)
        {
            if (database == null || outfit == null) return false;
            Transform intermediate = database.transform.Find(ShapeSyncDatabaseAsset.IntermediateContainerName);
            return intermediate != null
                && outfit.transform.parent == intermediate
                && string.Equals(AssetDatabase.GetAssetPath(outfit), AssetDatabase.GetAssetPath(database), StringComparison.Ordinal)
                && !PrefabUtility.IsPartOfPrefabInstance(outfit);
        }

        private void DrawGeneralDetail()
        {
            GUILayout.Label(DetailTitle, EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Database", database, typeof(ShapeSyncDatabase), false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Database")) TryCreateDatabaseWithDialog(out _);
                if (GUILayout.Button("Open Database")) TryOpenDatabaseWithDialog(out _);
            }

            EditorGUILayout.HelpBox(EmptyDatabaseMessage, MessageType.Info);
            if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, MessageType.Error);
        }

        private static string GetSelectedFolderPath()
        {
            return GetSelectedFolderPath(Selection.activeObject);
        }

        internal static string GetSelectedFolderPath(UnityEngine.Object selectedObject)
        {
            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
            if (AssetDatabase.IsValidFolder(selectedPath)) return selectedPath;

            if (!string.IsNullOrEmpty(selectedPath))
            {
                string directory = System.IO.Path.GetDirectoryName(selectedPath)?.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(directory)) return directory;
            }

            return "Assets";
        }

        /// <summary>Opens the Figure-export save dialog and exports the currently displayed Database Figure.</summary>
        internal bool TryExportDatabaseFigureWithDialog(out string exportDiagnostic)
        {
            return TryExportDatabaseFigureWithDialog(databaseFigurePrefab, "Figure", out exportDiagnostic);
        }

        /// <summary>Opens the FBM-export save dialog and exports one Database-owned FBM Figure.</summary>
        internal bool TryExportFbmFigureWithDialog(GameObject fbmFigure, out string exportDiagnostic)
        {
            return TryExportDatabaseFigureWithDialog(fbmFigure, "FBM", out exportDiagnostic);
        }

        /// <summary>Opens the independent Outfit-export save dialog for a Database-owned Outfit Prefab.</summary>
        internal bool TryExportOutfitWithDialog(GameObject outfit, string outfitKind, out string exportDiagnostic)
        {
            exportDiagnostic = null;
            if (!CanExportDatabaseOutfit(outfit))
            {
                exportDiagnostic = outfitKind + " Export requires an Outfit Prefab on Database.";
                diagnostic = exportDiagnostic;
                return false;
            }

            string destinationPath;
            try
            {
                destinationPath = SaveOutfitExportPanel(
                    "Export " + outfitKind + " Prefab", outfit.name, "prefab",
                    "Choose a folder and name for the exported Outfit Prefab.", GetSelectedFolderPath());
            }
            catch (Exception exception)
            {
                exportDiagnostic = "Outfit Export dialog failed: " + exception.Message;
                diagnostic = exportDiagnostic;
                return false;
            }

            if (string.IsNullOrEmpty(destinationPath)) return false;
            try { RefreshAssetDatabase(); }
            catch (Exception exception)
            {
                exportDiagnostic = "Outfit Export could not refresh the Asset Database: " + exception.Message;
                diagnostic = exportDiagnostic;
                return false;
            }

            if (!ExportDatabaseOutfit(database, outfit, destinationPath, out GameObject exportedPrefab, out exportDiagnostic))
            {
                diagnostic = exportDiagnostic;
                return false;
            }

            Selection.activeObject = exportedPrefab;
            diagnostic = null;
            return true;
        }

        private bool TryExportDatabaseFigureWithDialog(GameObject figure, string figureKind, out string exportDiagnostic)
        {
            exportDiagnostic = null;
            if (!CanExportDatabaseFigure(figure))
            {
                exportDiagnostic = figureKind + " Export requires a Prefab on Database.";
                diagnostic = exportDiagnostic;
                return false;
            }

            string destinationPath;
            try
            {
                destinationPath = SaveFigureExportPanel(
                    "Export " + figureKind + " Prefab",
                    figure.name,
                    "prefab",
                    "Choose a folder and name for the exported Figure Prefab.",
                    GetSelectedFolderPath());
            }
            catch (Exception exception)
            {
                exportDiagnostic = "Figure Export dialog failed: " + exception.Message;
                diagnostic = exportDiagnostic;
                return false;
            }

            // A dialog cancel is a UI no-op.  In particular, do not invoke the service,
            // alter the current selection, or touch Detail drafts/Dirty state.
            if (string.IsNullOrEmpty(destinationPath)) return false;

            // SaveFilePanelInProject permits creating a folder.  Synchronize it before
            // the export service validates the selected Assets-relative destination.
            try { RefreshAssetDatabase(); }
            catch (Exception exception)
            {
                exportDiagnostic = figureKind + " Export could not refresh the Asset Database: " + exception.Message;
                diagnostic = exportDiagnostic;
                return false;
            }

            if (!ExportDatabaseFigure(database, figure, destinationPath, out GameObject exportedPrefab, out exportDiagnostic))
            {
                diagnostic = exportDiagnostic;
                return false;
            }

            Selection.activeObject = exportedPrefab;
            diagnostic = null;
            return true;
        }

        internal bool TryCreateDatabaseWithDialog(out string createDiagnostic)
        {
            string path;
            try { path = SaveDatabasePanel("Create ShapeSync Database", "ShapeSyncDatabase", "prefab", "Choose a folder and initial Database name.", GetSelectedFolderPath()); }
            catch (Exception exception)
            {
                createDiagnostic = "Could not open ShapeSync Database save dialog: " + exception.Message;
                diagnostic = createDiagnostic;
                return false;
            }
            if (string.IsNullOrEmpty(path)) { createDiagnostic = null; return false; }
            try { RefreshAssetDatabase(); }
            catch (Exception exception)
            {
                createDiagnostic = "Could not synchronize the selected Database folder: " + exception.Message;
                diagnostic = createDiagnostic;
                return false;
            }
            if (!TryPrepareDatabaseSwitch(out createDiagnostic)) return false;
            if (!ShapeSyncDatabaseAsset.TryCreateAtPath(path, out ShapeSyncDatabase created, out createDiagnostic)) { diagnostic = createDiagnostic; return false; }
            if (TrySetDatabase(created, out createDiagnostic)) return true;
            try { if (!DeleteDatabaseAsset(path)) createDiagnostic = "ShapeSync Database was created but could not be cleaned up after binding failed: " + createDiagnostic; }
            catch (Exception exception) { createDiagnostic = "ShapeSync Database was created but could not be cleaned up after binding failed: " + exception.Message; }
            diagnostic = createDiagnostic;
            return false;
        }

        internal bool TryOpenDatabaseWithDialog(out string openDiagnostic)
        {
            string selectedPath;
            try { selectedPath = OpenDatabasePanel("Open ShapeSync Database", Application.dataPath, "prefab"); }
            catch (Exception exception)
            {
                openDiagnostic = "Could not open ShapeSync Database file dialog: " + exception.Message;
                diagnostic = openDiagnostic;
                return false;
            }

            if (string.IsNullOrEmpty(selectedPath)) { openDiagnostic = null; return false; }
            string assetPath;
            try { assetPath = ToProjectRelativePath(selectedPath); }
            catch (Exception exception)
            {
                openDiagnostic = "Could not resolve the selected Database path: " + exception.Message;
                diagnostic = openDiagnostic;
                return false;
            }

            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                openDiagnostic = "ShapeSync Database must be selected from this project's Assets folder.";
                diagnostic = openDiagnostic;
                return false;
            }

            if (!TryPrepareDatabaseSwitch(out openDiagnostic)) return false;
            return TrySetDatabaseAtPath(assetPath, out openDiagnostic);
        }

        internal sealed class NavigationTreeView : ShapeSyncTreeView
        {
            private static readonly int[] FigureChildItemIds = { 3, 9, 7, 8, 6 };
            private static readonly string[] MeshOutfitChildLabels = { "Materials", "Normals", "FBMs", "PBMs", "Collections", "Figure Mask" };
            internal const int OutfitsItemId = 100;
            internal const int MeshOutfitsItemId = 101;
            internal const int MaterialOutfitsItemId = 102;
            internal const int VrmItemId = 11;
            private const int FirstDynamicOutfitItemId = 1000;
            internal const int ShapesItemId = 4;
            internal const int ShapeTagsItemId = 200;
            private const int FirstDynamicShapeItemId = 2000;
            private readonly Func<int, bool> onSelected;
            private readonly Func<Section> currentSection;
            private readonly Func<IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitEntry>> outfits;
            private readonly Func<string, bool> onOutfitSelected;
            private readonly Func<string, string, bool> onOutfitChildSelected;
            private readonly Func<IReadOnlyList<ShapeSyncDatabaseRegistry.ShapeEntry>> shapes;
            private readonly Func<string, bool> onShapeSelected;
            private readonly Func<bool> optionalVrmNavigation;
            private readonly Dictionary<int, string> outfitIdentityByItemId = new Dictionary<int, string>();
            private readonly Dictionary<int, string> outfitChildLabelByItemId = new Dictionary<int, string>();
            private readonly Dictionary<int, string> shapeIdByItemId = new Dictionary<int, string>();
            private int lastAcceptedSelectionId = 1;
            internal NavigationTreeView(ShapeSyncTreeViewState state, Func<Section, bool> onSelected, Func<Section> currentSection)
                : this(state, id => id >= 1 && id <= 10 && onSelected((Section)(id - 1)), currentSection, null, null) { }
            internal NavigationTreeView(ShapeSyncTreeViewState state, Func<int, bool> onSelected, Func<Section> currentSection,
                Func<IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitEntry>> outfitProvider, Func<string, bool> outfitSelected)
                : this(state, onSelected, currentSection, outfitProvider, outfitSelected, null) { }
            internal NavigationTreeView(ShapeSyncTreeViewState state, Func<int, bool> onSelected, Func<Section> currentSection,
                Func<IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitEntry>> outfitProvider, Func<string, bool> outfitSelected,
                Func<string, string, bool> outfitChildSelected,
                Func<IReadOnlyList<ShapeSyncDatabaseRegistry.ShapeEntry>> shapeProvider = null, Func<string, bool> shapeSelected = null,
                Func<bool> optionalVrmNavigation = null) : base(state)
            {
                this.onSelected = onSelected;
                this.currentSection = currentSection;
                outfits = outfitProvider;
                onOutfitSelected = outfitSelected;
                onOutfitChildSelected = outfitChildSelected;
                shapes = shapeProvider;
                onShapeSelected = shapeSelected;
                this.optionalVrmNavigation = optionalVrmNavigation;
                Section initialSection = currentSection();
                lastAcceptedSelectionId = initialSection == Section.Vrm
                    ? VrmItemId
                    : initialSection == Section.MeshOutfit || initialSection == Section.MaterialOutfit
                    ? 1
                    : (int)initialSection + 1;
                Reload();
                SetSelection(new[] { lastAcceptedSelectionId });
            }
            internal string[] FigureChildDisplayNamesForTest => GetFigureChildItemIds().Select(GetFigureChildDisplayName).ToArray();
            internal string[] RootDisplayNamesForTest => BuildRoot().children.Select(item => item.displayName).ToArray();
            internal string[] MeshOutfitChildDisplayNamesForTest => GetMeshOutfitChildLabels();
            internal int GetOutfitChildItemIdForTest(string identity, string childLabel)
            {
                Reload();
                return outfitChildLabelByItemId
                    .Where(pair => string.Equals(pair.Value, childLabel, StringComparison.Ordinal)
                        && string.Equals(GetOutfitIdentity(pair.Key), identity, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .FirstOrDefault();
            }
            internal int GetOutfitItemIdForTest(string identity)
                => outfitIdentityByItemId.FirstOrDefault(pair => string.Equals(pair.Value, identity, StringComparison.Ordinal)
                    && !outfitChildLabelByItemId.ContainsKey(pair.Key)).Key;
            internal string[] ShapeGroupDisplayNamesForTest => Enum.GetValues(typeof(ShapeSyncDatabaseRegistry.ShapeKind)).Cast<ShapeSyncDatabaseRegistry.ShapeKind>().Select(kind => kind + " Shapes").ToArray();
            internal int GetShapeItemIdForTest(string shapeId) => shapeIdByItemId.FirstOrDefault(pair => string.Equals(pair.Value, shapeId, StringComparison.Ordinal)).Key;
            internal string[] ShapeDisplayNamesForTest(ShapeSyncDatabaseRegistry.ShapeKind kind)
                => (shapes?.Invoke() ?? Array.Empty<ShapeSyncDatabaseRegistry.ShapeEntry>()).Where(entry => entry != null && entry.Kind == kind).Select(entry => entry.DisplayName).ToArray();
            internal string[] OutfitDisplayNamesForTest(ShapeSyncDatabaseRegistry.OutfitKind kind)
                => (outfits?.Invoke() ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>())
                    .Where(entry => entry != null && entry.Kind == kind).Select(entry => entry.DisplayName).ToArray();
            internal void SelectOutfitIdentity(string identity)
            {
                Reload();
                int itemId = outfitIdentityByItemId.FirstOrDefault(pair => string.Equals(pair.Value, identity, StringComparison.Ordinal)).Key;
                SetAcceptedSelection(itemId == 0 ? OutfitsItemId : itemId);
            }
            internal void SelectOutfitsRoot()
            {
                Reload();
                SetAcceptedSelection(OutfitsItemId);
            }
            internal void SelectShapesRoot()
            {
                Reload();
                SetAcceptedSelection(ShapesItemId);
            }
            internal void SelectShapeId(string shapeId)
            {
                Reload();
                int itemId = shapeIdByItemId.FirstOrDefault(pair => string.Equals(pair.Value, shapeId, StringComparison.Ordinal)).Key;
                SetAcceptedSelection(itemId == 0 ? ShapesItemId : itemId);
            }
            private void SetAcceptedSelection(int itemId)
            {
                lastAcceptedSelectionId = itemId;
                SetSelection(new[] { itemId }, TreeViewSelectionOptions.RevealAndFrame);
            }
            /// <inheritdoc />
            protected override ShapeSyncTreeViewItem BuildRoot()
            {
                outfitIdentityByItemId.Clear();
                outfitChildLabelByItemId.Clear();
                shapeIdByItemId.Clear();
                int nextDynamicOutfitItemId = FirstDynamicOutfitItemId;
                int nextDynamicShapeItemId = FirstDynamicShapeItemId;
                ShapeSyncTreeViewItem root = new ShapeSyncTreeViewItem { id = 0, depth = -1, displayName = "Root", children = new System.Collections.Generic.List<ShapeSyncTreeViewItem>() };
                root.children.Add(new ShapeSyncTreeViewItem { id = 1, depth = 0, displayName = TreeLabels[(int)Section.General] });
                ShapeSyncTreeViewItem figure = new ShapeSyncTreeViewItem { id = 2, depth = 0, displayName = TreeLabels[(int)Section.Figure], children = new System.Collections.Generic.List<ShapeSyncTreeViewItem>() };
                foreach (int id in GetFigureChildItemIds()) figure.children.Add(new ShapeSyncTreeViewItem { id = id, depth = 1, displayName = GetFigureChildDisplayName(id) });
                root.children.Add(figure);
                ShapeSyncTreeViewItem outfitRoot = new ShapeSyncTreeViewItem { id = OutfitsItemId, depth = 0, displayName = "Outfits", children = new List<ShapeSyncTreeViewItem>() };
                ShapeSyncTreeViewItem meshOutfits = new ShapeSyncTreeViewItem { id = MeshOutfitsItemId, depth = 1, displayName = "Mesh Outfits", children = new List<ShapeSyncTreeViewItem>() };
                ShapeSyncTreeViewItem materialOutfits = new ShapeSyncTreeViewItem { id = MaterialOutfitsItemId, depth = 1, displayName = "Material Outfits", children = new List<ShapeSyncTreeViewItem>() };
                IReadOnlyList<ShapeSyncDatabaseRegistry.OutfitEntry> currentOutfits = outfits?.Invoke() ?? Array.Empty<ShapeSyncDatabaseRegistry.OutfitEntry>();
                foreach (ShapeSyncDatabaseRegistry.OutfitEntry outfit in currentOutfits.Where(entry => entry != null))
                {
                    if (outfit.Kind == ShapeSyncDatabaseRegistry.OutfitKind.Mesh)
                    {
                        int outfitItemId = nextDynamicOutfitItemId++;
                        outfitIdentityByItemId.Add(outfitItemId, outfit.Identity);
                        ShapeSyncTreeViewItem meshOutfit = new ShapeSyncTreeViewItem
                        {
                            id = outfitItemId,
                            depth = 2,
                            displayName = outfit.DisplayName,
                            children = new List<ShapeSyncTreeViewItem>()
                        };
                        string[] childLabels = GetMeshOutfitChildLabels();
                        for (int childIndex = 0; childIndex < childLabels.Length; childIndex++)
                        {
                            int childItemId = nextDynamicOutfitItemId++;
                            outfitIdentityByItemId.Add(childItemId, outfit.Identity);
                            outfitChildLabelByItemId.Add(childItemId, childLabels[childIndex]);
                            meshOutfit.children.Add(new ShapeSyncTreeViewItem
                            {
                                id = childItemId,
                                depth = 3,
                                displayName = childLabels[childIndex]
                            });
                        }
                        meshOutfits.children.Add(meshOutfit);
                    }
                    else
                    {
                        int outfitItemId = nextDynamicOutfitItemId++;
                        outfitIdentityByItemId.Add(outfitItemId, outfit.Identity);
                        materialOutfits.children.Add(new ShapeSyncTreeViewItem { id = outfitItemId, depth = 2, displayName = outfit.DisplayName });
                    }
                }
                outfitRoot.children.Add(meshOutfits);
                outfitRoot.children.Add(materialOutfits);
                root.children.Add(outfitRoot);
                ShapeSyncTreeViewItem shapeRoot = new ShapeSyncTreeViewItem { id = ShapesItemId, depth = 0, displayName = TreeLabels[(int)Section.Shapes], children = new List<ShapeSyncTreeViewItem>() };
                shapeRoot.children.Add(new ShapeSyncTreeViewItem { id = ShapeTagsItemId, depth = 1, displayName = "Tags" });
                foreach (ShapeSyncDatabaseRegistry.ShapeKind kind in Enum.GetValues(typeof(ShapeSyncDatabaseRegistry.ShapeKind)))
                {
                    ShapeSyncTreeViewItem kindRoot = new ShapeSyncTreeViewItem { id = nextDynamicShapeItemId++, depth = 1, displayName = kind + " Shapes", children = new List<ShapeSyncTreeViewItem>() };
                    foreach (ShapeSyncDatabaseRegistry.ShapeEntry shape in (shapes?.Invoke() ?? Array.Empty<ShapeSyncDatabaseRegistry.ShapeEntry>()).Where(entry => entry != null && entry.Kind == kind))
                    {
                        int shapeItemId = nextDynamicShapeItemId++;
                        shapeIdByItemId.Add(shapeItemId, shape.ShapeId);
                        kindRoot.children.Add(new ShapeSyncTreeViewItem { id = shapeItemId, depth = 2, displayName = shape.DisplayName });
                    }
                    shapeRoot.children.Add(kindRoot);
                }
                root.children.Add(shapeRoot);
                root.children.Add(new ShapeSyncTreeViewItem { id = 5, depth = 0, displayName = TreeLabels[(int)Section.Textures] });
                root.children.Add(new ShapeSyncTreeViewItem { id = 10, depth = 0, displayName = "Generation" });
                return root;
            }
            private static string GetFigureChildDisplayName(int id) => id switch
            {
                3 => TreeLabels[(int)Section.Materials],
                9 => "Normals",
                6 => "Extra Morphs",
                7 => "FBMs",
                8 => "PBMs",
                VrmItemId => "VRM",
                _ => throw new ArgumentOutOfRangeException(nameof(id))
            };
            private int[] GetFigureChildItemIds()
            {
                return optionalVrmNavigation != null && optionalVrmNavigation()
                    ? FigureChildItemIds.Concat(new[] { VrmItemId }).ToArray()
                    : FigureChildItemIds;
            }
            private string[] GetMeshOutfitChildLabels()
            {
                return optionalVrmNavigation != null && optionalVrmNavigation()
                    ? MeshOutfitChildLabels.Concat(new[] { "VRM" }).ToArray()
                    : MeshOutfitChildLabels;
            }
            private string GetOutfitIdentity(int itemId)
            {
                return outfitIdentityByItemId.TryGetValue(itemId, out string identity) ? identity : null;
            }
            /// <inheritdoc />
            protected override void SelectionChanged(System.Collections.Generic.IList<int> ids)
            {
                if (ids.Count == 0) return;
                int id = ids[0];
                if (shapeIdByItemId.TryGetValue(id, out string shapeId))
                {
                    if (onShapeSelected == null || !onShapeSelected(shapeId)) SetSelection(new[] { lastAcceptedSelectionId }, TreeViewSelectionOptions.RevealAndFrame);
                    else SetAcceptedSelection(id);
                    return;
                }
                if (id == ShapeTagsItemId)
                {
                    if (!onSelected(id)) SetSelection(new[] { lastAcceptedSelectionId }, TreeViewSelectionOptions.RevealAndFrame);
                    else SetAcceptedSelection(id);
                    return;
                }
                string identity = GetOutfitIdentity(id);
                if (identity != null)
                {
                    bool accepted = outfitChildLabelByItemId.TryGetValue(id, out string childLabel)
                        ? (onOutfitChildSelected != null ? onOutfitChildSelected(identity, childLabel) : onOutfitSelected != null && onOutfitSelected(identity))
                        : onOutfitSelected != null && onOutfitSelected(identity);
                    if (!accepted) SetSelection(new[] { lastAcceptedSelectionId }, TreeViewSelectionOptions.RevealAndFrame);
                    else SetAcceptedSelection(id);
                    return;
                }
                if (!onSelected(id)) SetSelection(new[] { lastAcceptedSelectionId }, TreeViewSelectionOptions.RevealAndFrame);
                else SetAcceptedSelection(id);
            }
            internal void ApplySelectionChangeForTest(System.Collections.Generic.IList<int> ids) { SetSelection(ids, TreeViewSelectionOptions.RevealAndFrame); SelectionChanged(ids); }
            internal System.Collections.Generic.IList<int> SelectedItemIdsForTest => GetSelection();
        }
    }
}
