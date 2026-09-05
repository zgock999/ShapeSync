// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Editor UI that drives one caller-owned Pure Humanoid build and publishes only after build success.</summary>
    public sealed class HumanoidCompilerWindow : EditorWindow
    {
        private const string Title = "Humanoid Compiler";

        private enum PublishPhase { None, StageIndividualAssets, InitializeVrm, StageVrmAssets, CommitPrefab }

        internal static Func<string, string, string, string> SelectOutputFolder = (title, folder, defaultName) => EditorUtility.SaveFolderPanel(title, folder, defaultName);
        internal static Action<string, string, string> ShowDialog = (title, message, ok) => EditorUtility.DisplayDialog(title, message, ok);
        internal static Action<string> LogWarning = message => Debug.LogWarning(message);
        // Test seam only; production leaves this null so the controller creates its concrete EditMode backend.
        internal static Func<IHumanoidBuildBackend> BackendFactoryForTests;

        [SerializeField] private GameObject figure;
        [SerializeField] private ShapeSyncDocumentAsset document;
        [SerializeField] private AtlasSchema atlasSchema;
#if SHAPESYNC_USE_UNIVRM
        [SerializeField] private bool transportVrmPhysics;
        [SerializeField] private string vrmAssetRelativeFolder = "VRM";
#endif

        private HumanoidEditorBuildController controller;
        private bool updateRegistered;
        private bool outputFolderRequested;
        private PublishPhase publishPhase;
        private string publishOutputFolder;
        private string publishDocumentName;
        private IHumanoidVrmTransportExecutor vrmExecutor;
        private string progress = "Ready";
        private string publishedFolder;
        private string warning;

        /// <summary>Opens the Humanoid Compiler window.</summary>
        [MenuItem("Tools/zgock/ShapeSync/Humanoid Compiler")]
        public static void ShowWindow()
        {
            GetWindowWithRect<HumanoidCompilerWindow>(new Rect(0f, 0f, 520f, 300f), false, Title);
        }

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            StopAndDispose("Humanoid Compiler window was closed before publish completed.");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(Title, EditorStyles.boldLabel);
            EditorGUILayout.Space();
            bool running = controller != null;
            using (new EditorGUI.DisabledScope(running))
            {
                figure = (GameObject)EditorGUILayout.ObjectField("Figure", figure, typeof(GameObject), true);
                document = (ShapeSyncDocumentAsset)EditorGUILayout.ObjectField("Document", document, typeof(ShapeSyncDocumentAsset), false);
                atlasSchema = (AtlasSchema)EditorGUILayout.ObjectField("Atlas Schema (Optional)", atlasSchema, typeof(AtlasSchema), false);
#if SHAPESYNC_USE_UNIVRM
                transportVrmPhysics = EditorGUILayout.Toggle("Transport VRM Physics", transportVrmPhysics);
                if (transportVrmPhysics)
                    vrmAssetRelativeFolder = EditorGUILayout.TextField("VRM Asset Relative Folder", vrmAssetRelativeFolder);
#endif
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Progress", progress);
            if (!string.IsNullOrEmpty(publishedFolder))
                EditorGUILayout.SelectableLabel("Output: " + publishedFolder, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (!string.IsNullOrEmpty(warning)) EditorGUILayout.HelpBox(warning, MessageType.Warning);

            GUILayout.FlexibleSpace();
            if (running)
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(32f)))
                {
                    CancelBuild("Cancelled before artifact publish.");
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(figure == null || document == null))
                {
                    if (GUILayout.Button("Generate", GUILayout.Height(32f))) BeginGenerate();
                }
            }
        }

        private void BeginGenerate()
        {
            warning = null;
            publishedFolder = null;
            ResetPublishPhase();
            controller?.Dispose();
            controller = BackendFactoryForTests == null ? new HumanoidEditorBuildController() : new HumanoidEditorBuildController(BackendFactoryForTests);
            bool started = atlasSchema == null
                ? controller.TryStart(figure, document, out StackMachineDiagnostic diagnostic)
                : controller.TryStartWithAtlas(figure, document, atlasSchema, out diagnostic);
            if (!started)
            {
                ReportFailure(diagnostic);
                return;
            }
            progress = "Building Mesh";
            RegisterUpdate();
        }

        private void HandleEditorUpdate()
        {
            if (controller == null) { UnregisterUpdate(); return; }
            if (controller.IsActive)
            {
                HumanoidBuildOperationStatus status = controller.Pump(out StackMachineDiagnostic diagnostic);
                if (status == HumanoidBuildOperationStatus.Pending)
                {
                    progress = FormatProgress(controller.ProgressPhase);
                    Repaint();
                    return;
                }
                if (status != HumanoidBuildOperationStatus.Succeeded)
                {
                    ReportFailure(diagnostic ?? controller.Diagnostic);
                    return;
                }
            }

            if (controller.Status == HumanoidBuildOperationStatus.Succeeded && !outputFolderRequested)
            {
                outputFolderRequested = true;
                BeginPublishAfterFolderSelection();
                return;
            }

            if (controller.Status == HumanoidBuildOperationStatus.Succeeded && publishPhase != PublishPhase.None)
                PumpPublishPhase();
        }

        private void BeginPublishAfterFolderSelection()
        {
            string selected = SelectOutputFolder("Select Empty Pure Humanoid Output Folder", UnityEngine.Application.dataPath, document != null ? document.name : "Humanoid");
            if (string.IsNullOrEmpty(selected))
            {
                CancelBuild("Publish folder selection was cancelled. No artifacts were created.");
                return;
            }
            if (!HumanoidPublishPathValidator.TryResolveOutputFolder(selected, out string outputFolder, out StackMachineDiagnostic diagnostic))
            {
                ReportFailure(diagnostic);
                return;
            }
            if (!HumanoidIndividualAssetStager.TryValidateEmptyOutputFolder(outputFolder, out diagnostic))
            {
                if (string.Equals(diagnostic?.domainCode, "PublishOutputFolderNotEmpty", StringComparison.Ordinal))
                    CancelBuild("Selected output folder is not empty. Publish was cancelled before artifact creation.");
                else
                    ReportFailure(diagnostic);
                return;
            }

#if SHAPESYNC_USE_UNIVRM
            if (transportVrmPhysics)
            {
                if (!HumanoidPublishPathValidator.TryValidateVrmRelativeFolder(vrmAssetRelativeFolder, out diagnostic, requireNonEmpty: true))
                {
                    ReportFailure(diagnostic);
                    return;
                }
            }
#endif
            publishOutputFolder = outputFolder;
            publishDocumentName = document.name;
            publishPhase = PublishPhase.StageIndividualAssets;
            progress = "Publishing Assets";
            Repaint();
        }

        private void PumpPublishPhase()
        {
            StackMachineDiagnostic diagnostic;
            switch (publishPhase)
            {
                case PublishPhase.StageIndividualAssets:
                    if (!controller.TryStageIndividualAssets(publishOutputFolder, publishDocumentName, out diagnostic)
                        || !controller.TryApplyStagedAssetsToCandidate(out diagnostic)) { ReportFailure(diagnostic); return; }
#if SHAPESYNC_USE_UNIVRM
                    if (transportVrmPhysics)
                    {
                        publishPhase = PublishPhase.InitializeVrm;
                        progress = "Initializing VRM";
                        Repaint();
                        return;
                    }
#endif
                    publishPhase = PublishPhase.CommitPrefab;
                    progress = "Publishing Prefab";
                    Repaint();
                    return;

                case PublishPhase.InitializeVrm:
#if SHAPESYNC_USE_UNIVRM
                    if (vrmExecutor == null)
                    {
                        if (!HumanoidVrmTransportExecutorProvider.TryCreate(out vrmExecutor))
                        {
                            ReportFailure(StackMachineDiagnostic.CreateDomain("humanoid", "VrmTransportExecutorRequired", "Transport VRM Physics requires the optional UniVRM Editor integration."));
                            return;
                        }
                    }
                    if (!controller.TryTransportVrmPhysics(vrmExecutor, out diagnostic)) { ReportFailure(diagnostic); return; }
                    publishPhase = PublishPhase.StageVrmAssets;
                    progress = "Transporting Physics";
                    Repaint();
                    return;
#else
                    ReportFailure(StackMachineDiagnostic.CreateDomain("humanoid", "VrmTransportExecutorRequired", "Transport VRM Physics requires the optional UniVRM Editor integration."));
                    return;
#endif

                case PublishPhase.StageVrmAssets:
#if SHAPESYNC_USE_UNIVRM
                    if (!controller.TryStageVrmAssets(vrmExecutor, publishOutputFolder, vrmAssetRelativeFolder, publishDocumentName, out diagnostic)) { ReportFailure(diagnostic); return; }
                    publishPhase = PublishPhase.CommitPrefab;
                    progress = "Publishing Prefab";
                    Repaint();
                    return;
#else
                    ReportFailure(StackMachineDiagnostic.CreateDomain("humanoid", "VrmTransportExecutorRequired", "Transport VRM Physics requires the optional UniVRM Editor integration."));
                    return;
#endif

                case PublishPhase.CommitPrefab:
                    if (!controller.TryCommitPrefab(publishOutputFolder, publishDocumentName, vrmExecutor, out diagnostic)) { ReportFailure(diagnostic); return; }
                    publishedFolder = publishOutputFolder;
                    progress = "Completed";
                    UnregisterUpdate();
                    controller.Dispose();
                    controller = null;
                    ResetPublishPhase();
                    Repaint();
                    return;
            }
        }

        private void ReportFailure(StackMachineDiagnostic diagnostic)
        {
            string message = FormatDiagnostic(diagnostic);
            progress = "Failed";
            UnregisterUpdate();
            controller?.Dispose();
            warning = BuildResidualWarning(message);
            controller = null;
            ResetPublishPhase();
            ShowDialog(Title + " Failed", warning, "OK");
            Repaint();
        }

        private void CancelBuild(string reason)
        {
            progress = "Cancelled";
            UnregisterUpdate();
            controller?.Cancel();
            controller?.Dispose();
            warning = BuildResidualWarning(reason);
            controller = null;
            ResetPublishPhase();
            Repaint();
        }

        private void StopAndDispose(string reason)
        {
            UnregisterUpdate();
            controller?.Cancel();
            controller?.Dispose();
            if (controller != null && controller.ResidualArtifactPaths.Count > 0)
                LogWarning?.Invoke(BuildResidualWarning(reason));
            controller = null;
            ResetPublishPhase();
        }

        private void HandleBeforeAssemblyReload()
        {
            StopAndDispose("Humanoid Compiler was cancelled for assembly reload.");
        }

        private void RegisterUpdate()
        {
            if (updateRegistered) return;
            EditorApplication.update += HandleEditorUpdate;
            updateRegistered = true;
        }

        private void UnregisterUpdate()
        {
            if (!updateRegistered) return;
            EditorApplication.update -= HandleEditorUpdate;
            updateRegistered = false;
        }

        private void ResetPublishPhase()
        {
            outputFolderRequested = false;
            publishPhase = PublishPhase.None;
            publishOutputFolder = null;
            publishDocumentName = null;
            vrmExecutor = null;
        }

        private string BuildResidualWarning(string message)
        {
            if (controller == null || controller.ResidualArtifactPaths.Count == 0) return message;
            return message + "\n\nPersistent artifacts were left for manual inspection:\n" + string.Join("\n", controller.ResidualArtifactPaths);
        }

        private static string FormatDiagnostic(StackMachineDiagnostic diagnostic)
            => StackMachineDiagnostic.Format(diagnostic, "The Humanoid Compiler failed without a diagnostic.");

        /// <summary>Maps the controller's intentionally small public phase surface to the user-visible compiler progress text.</summary>
        private static string FormatProgress(HumanoidBuildProgressPhase phase)
        {
            if (phase == HumanoidBuildProgressPhase.Atlas) return "Baking Atlas";
            if (phase == HumanoidBuildProgressPhase.Material) return "Building Material";
            return "Building Mesh";
        }
    }
}
