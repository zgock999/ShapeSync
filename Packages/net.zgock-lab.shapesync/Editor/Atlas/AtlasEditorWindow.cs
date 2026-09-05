// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor.Atlas
{
    /// <summary>Hosts Atlas candidate input and entry editing. Dry Run and Schema persistence are supplied by later Atlas Editor steps.</summary>
    public sealed class AtlasEditorWindow : EditorWindow
    {
        internal const float DefaultWindowWidth = 800f;
        internal const float DefaultWindowHeight = 600f;
        internal const float ActionButtonHeight = 40f;
        private static readonly int[] PageExtents = { 4096, 2048, 1024, 512 };
        private static readonly string[] PageExtentLabels = { "4096", "2048", "1024", "512" };
        // GenericMenu reserves ASCII '/' as a submenu separator; use U+2044 so each occupancy stays one item.
        private static readonly string[] CellLabels = { "ignore", "1⁄1", "1⁄4", "1⁄8 Horizontal", "1⁄8 Vertical", "1⁄16", "1⁄16 Horizontal", "1⁄16 Vertical", "1⁄32 Horizontal", "1⁄32 Vertical", "1⁄64" };
        private GameObject figure;
        private ShapeSyncDocumentAsset document;
        private AtlasEditorState state;
        private string alert;
        private MessageType alertType;
        private Vector2 contentScrollPosition;

        /// <summary>Opens the Atlas Editor window.</summary>
        [MenuItem("Tools/zgock/ShapeSync/Atlas Editor")]
        public static void Open()
        {
            GetWindowWithRect<AtlasEditorWindow>(new Rect(0f, 0f, DefaultWindowWidth, DefaultWindowHeight), false, "Atlas Editor");
        }

        private void OnEnable()
        {
            state = new AtlasEditorState();
            state.SetFigure(figure);
            state.SetDocument(document);
        }

        private void OnGUI()
        {
            if (state == null) OnEnable();
            contentScrollPosition = EditorGUILayout.BeginScrollView(contentScrollPosition, GUILayout.ExpandHeight(true));
            EditorGUI.BeginChangeCheck();
            GameObject nextFigure = (GameObject)EditorGUILayout.ObjectField("Figure", figure, typeof(GameObject), true);
            ShapeSyncDocumentAsset nextDocument = (ShapeSyncDocumentAsset)EditorGUILayout.ObjectField("Document", document, typeof(ShapeSyncDocumentAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                figure = nextFigure;
                document = nextDocument;
                state.SetFigure(figure);
                state.SetDocument(document);
                alert = null;
            }

            int pageIndex = PageExtentIndex(state.PageExtent);
            int nextPageIndex = EditorGUILayout.Popup("Page Size", pageIndex, PageExtentLabels);
            if (nextPageIndex != pageIndex && !state.TrySetPageExtent(PageExtents[nextPageIndex], out StackMachineDiagnostic extentDiagnostic)) SetAlert(extentDiagnostic);

            using (new EditorGUI.DisabledScope(!state.CanListEntries))
            {
                if (GUILayout.Button("List Entries") && !state.TryListEntries(out StackMachineDiagnostic listDiagnostic)) SetAlert(listDiagnostic);
            }
            DrawEntries();
            DrawLayoutPreview();
            if (!string.IsNullOrEmpty(alert)) EditorGUILayout.HelpBox(alert, alertType);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(16f);
            using (new EditorGUI.DisabledScope(!state.CanDryRun))
            {
                if (GUILayout.Button("Dry Run", GUILayout.Height(ActionButtonHeight)))
                {
                    if (AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic dryRunDiagnostic)) SetAlert("Atlas Dry Run succeeded.", MessageType.Info);
                    else SetAlert(dryRunDiagnostic);
                }
            }
            using (new EditorGUI.DisabledScope(!state.CanGenerate))
            {
                if (GUILayout.Button("Generate Atlas", GUILayout.Height(ActionButtonHeight)))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save Atlas Schema", "AtlasSchema", "asset", "Choose the Atlas Schema asset path.");
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, path, out _, out StackMachineDiagnostic saveDiagnostic)) SetAlert("Atlas Schema saved.", MessageType.Info);
                        else SetAlert(saveDiagnostic);
                    }
                }
            }
        }

        private void DrawEntries()
        {
            if (!state.CanDryRun) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Entries", EditorStyles.boldLabel);
            for (int i = 0; i < state.Entries.Count; i++)
            {
                AtlasEditorEntryState entry = state.Entries[i];
                Texture source = null;
                if (AtlasEditorMaterialSourceResolver.TryResolve(entry.Candidate.ValidationBinding, out _, out MaterialProxySemanticValues values, out _))
                    source = AtlasEditorMaterialSourceResolver.GetDisplaySourceTexture(values);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(entry.Candidate.MaterialId.ToString() + "  " + FormatSourceTextureSize(source));
                EditorGUILayout.LabelField("Source", entry.Candidate.SourceMaterialName);
                int page = EditorGUILayout.IntField("Page", entry.PageGroupingKey);
                int selection = EditorGUILayout.Popup("Occupancy", (int)entry.CellSelection, CellLabels);
                if ((page != entry.PageGroupingKey || selection != (int)entry.CellSelection)
                    && !state.TrySetEntry(entry.Candidate.MaterialId, page, (AtlasEditorCellSelection)selection, out StackMachineDiagnostic editDiagnostic)) SetAlert(editDiagnostic);
                if (HasAspectMismatch(source, entry)) EditorGUILayout.HelpBox(FormatAspectMismatchWarning(source, entry, state.PageExtent), MessageType.Warning);
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawLayoutPreview()
        {
            AtlasLayoutResult layout = state.LayoutPreview;
            if (layout == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layout Preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Page Extent", layout.PageExtent.ToString());
            for (int i = 0; i < layout.Cells.Count; i++)
            {
                AtlasLayoutCell cell = layout.Cells[i];
                EditorGUILayout.LabelField(cell.MaterialId + "  page " + cell.PageIndex + "  (" + cell.X + ", " + cell.Y + ") " + cell.Width + " x " + cell.Height);
            }
        }

        internal static bool HasAspectMismatch(Texture source, AtlasEditorEntryState entry)
        {
            if (source == null || entry == null || entry.Excluded) return false;
            return (long)source.width * (1 << entry.CellLevelX) != (long)source.height * (1 << entry.CellLevelY);
        }

        internal static string FormatSourceTextureSize(Texture source) => source == null ? "Source texture: none" : source.width + " x " + source.height;

        internal static string FormatAspectMismatchWarning(Texture source, AtlasEditorEntryState entry, int pageExtent)
        {
            if (source == null || entry == null) return string.Empty;
            int cellWidth = pageExtent >> entry.CellLevelX;
            int cellHeight = pageExtent >> entry.CellLevelY;
            return entry.Candidate.MaterialId.ToString() + ": source " + FormatSourceTextureSize(source) + " does not match Atlas cell " + cellWidth + " x " + cellHeight + ". PLACE will resample the source into this cell.";
        }

        internal static string FormatDiagnostic(StackMachineDiagnostic diagnostic)
        {
            if (diagnostic == null) return string.Empty;
            return string.IsNullOrEmpty(diagnostic.detail) ? diagnostic.message : diagnostic.message + "\n" + diagnostic.detail;
        }

        private void SetAlert(StackMachineDiagnostic diagnostic) { alert = FormatDiagnostic(diagnostic); alertType = MessageType.Error; }
        private void SetAlert(string message, MessageType type) { alert = message; alertType = type; }

        private static int PageExtentIndex(int extent)
        {
            for (int i = 0; i < PageExtents.Length; i++) if (PageExtents[i] == extent) return i;
            return 1;
        }
    }
}
