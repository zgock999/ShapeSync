// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidCompilerWindowTests
    {
        private const string OutputFolder = ShapeSyncTestAssetPaths.Spec17WindowOutputRoot;
        private const string OutputPrefix = "__Spec17_6_WindowOutput";
        [Test]
        public void PublishPathValidator_ResolvesAssetsAndRejectsProjectRoot()
        {
            string expectedAssetsFolder = ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests");
            string existingAssetFolder = System.IO.Path.Combine(Application.dataPath, expectedAssetsFolder.Substring(ShapeSyncTestAssetPaths.AssetsPrefix.Length));
            Assert.That(HumanoidPublishPathValidator.TryResolveOutputFolder(existingAssetFolder, out string assetsFolder, out StackMachineDiagnostic valid), Is.True, valid?.message);
            Assert.That(assetsFolder, Is.EqualTo(expectedAssetsFolder));

            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            Assert.That(HumanoidPublishPathValidator.TryResolveOutputFolder(projectRoot, out _, out StackMachineDiagnostic outside), Is.False);
            Assert.That(outside.domainCode, Is.EqualTo("PublishOutputFolderOutsideAssets"));
        }

        [Test]
        public void PublishPathValidator_ImportsNewDialogCreatedFolderBeforeResolvingIt()
        {
            string folder = ShapeSyncTestAssetPaths.ConsumerAssetPath("zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_DialogCreatedFolder");
            string absolute = System.IO.Path.Combine(Application.dataPath, folder.Substring(ShapeSyncTestAssetPaths.AssetsPrefix.Length));
            AssetDatabase.DeleteAsset(folder);
            try
            {
                System.IO.Directory.CreateDirectory(absolute);
                Assert.That(HumanoidPublishPathValidator.TryResolveOutputFolder(absolute, out string resolved, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(resolved, Is.EqualTo(folder));
                Assert.That(AssetDatabase.IsValidFolder(folder), Is.True);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void PublishPathValidator_RequiresSafeNonemptyVrmRelativeFolder()
        {
            Assert.That(HumanoidPublishPathValidator.TryValidateVrmRelativeFolder(string.Empty, out StackMachineDiagnostic empty, requireNonEmpty: true), Is.False);
            Assert.That(empty.domainCode, Is.EqualTo("VrmPublishRelativeFolderRequired"));
            Assert.That(HumanoidPublishPathValidator.TryValidateVrmRelativeFolder("../Escape", out StackMachineDiagnostic parent), Is.False);
            Assert.That(parent.domainCode, Is.EqualTo("VrmPublishRelativeFolderInvalid"));
            Assert.That(HumanoidPublishPathValidator.TryValidateVrmRelativeFolder("VRM/Initial", out StackMachineDiagnostic valid), Is.True, valid?.message);
        }

        [Test]
        public void WindowDisable_CancelsAndDisposesItsActiveController()
        {
            GameObject figure = new GameObject("Spec17_6_WindowFigure");
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new PendingBackend();
            object controller = CreateController(() => backend);
            EditorWindow window = null;
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                FieldInfo controllerField = window.GetType().GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic);
                controllerField.SetValue(window, controller);
                window.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                Assert.That(backend.Cancelled, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                ((IDisposable)controller).Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void WindowBeforeAssemblyReload_CancelsAndDisposesItsActiveController()
        {
            GameObject figure = new GameObject("Spec17_6_WindowReloadFigure");
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new PendingBackend();
            object controller = CreateController(() => backend);
            EditorWindow window = null;
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                window.GetType().GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, controller);
                window.GetType().GetMethod("HandleBeforeAssemblyReload", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                Assert.That(backend.Cancelled, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                ((IDisposable)controller).Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void WindowFolderDialogCancel_CancelsControllerBeforeAnyPublish()
        {
            GameObject figure = new GameObject("Spec17_6_WindowCancelFigure");
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new PendingBackend();
            object controller = CreateController(() => backend);
            EditorWindow window = null;
            FieldInfo selector = null;
            Func<string, string, string, string> originalSelector = null;
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                window.GetType().GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, controller);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => string.Empty));

                window.GetType().GetMethod("BeginPublishAfterFolderSelection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                Assert.That(backend.Cancelled, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(AssetDatabase.FindAssets("Spec17_6_WindowCancelFigure"), Is.Empty);
            }
            finally
            {
                if (selector != null) selector.SetValue(null, originalSelector);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                ((IDisposable)controller).Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void WindowNonemptyOutputFolder_CancelsControllerBeforeAnyPublish()
        {
            GameObject figure = new GameObject("Spec17_6_WindowNonemptyFigure");
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new PendingBackend();
            object controller = CreateController(() => backend);
            EditorWindow window = null;
            FieldInfo selector = null;
            Func<string, string, string, string> originalSelector = null;
            string markerPath = OutputFolder + "/__Spec17_6_WindowNonemptyMarker.txt";
            try
            {
                ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17/__Spec17_6_WindowOutput");
                System.IO.File.WriteAllText(ShapeSyncTestAssetPaths.AssetFileSystemPath(markerPath), "non-empty");
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                window.GetType().GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, controller);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                string nonempty = ShapeSyncTestAssetPaths.AssetFileSystemPath(OutputFolder);
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => nonempty));
                window.GetType().GetMethod("BeginPublishAfterFolderSelection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);

                Assert.That(backend.Cancelled, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
            }
            finally
            {
                if (System.IO.File.Exists(ShapeSyncTestAssetPaths.AssetFileSystemPath(markerPath)))
                    System.IO.File.Delete(ShapeSyncTestAssetPaths.AssetFileSystemPath(markerPath));
                if (selector != null) selector.SetValue(null, originalSelector);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                ((IDisposable)controller).Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void WindowSuccessThenFolderDialogCancel_DoesNotStageOrPublishArtifacts()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            GameObject figure = new GameObject("Spec17_6_WindowSuccessCancelFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowSuccessCancel";
            var backend = new SuccessBackend();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            int selectorCalls = 0;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document);
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => { selectorCalls++; return string.Empty; }));

                Invoke(window, "BeginGenerate");
                Invoke(window, "HandleEditorUpdate");
                Invoke(window, "HandleEditorUpdate");
                Assert.That(selectorCalls, Is.Zero, "Folder dialog must not open before terminal Mesh/Material success.");
                object activeController = GetField<object>(window, "controller");

                Invoke(window, "HandleEditorUpdate");

                Assert.That(selectorCalls, Is.EqualTo(1));
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Cancelled"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
                Assert.That(AssetDatabase.IsValidFolder(OutputFolder), Is.False);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowSuccessThenNonemptyFolder_CancelsWithoutAddingArtifacts()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            ShapeSyncDocumentAsset marker = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            AssetDatabase.CreateAsset(marker, OutputFolder + "/ExistingMarker.asset");
            GameObject figure = new GameObject("Spec17_6_WindowSuccessNonemptyFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowSuccessNonempty";
            var backend = new SuccessBackend();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            int selectorCalls = 0;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document);
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => { selectorCalls++; return System.IO.Path.GetFullPath(OutputFolder); }));

                Invoke(window, "BeginGenerate");
                Invoke(window, "HandleEditorUpdate");
                Invoke(window, "HandleEditorUpdate");
                Assert.That(selectorCalls, Is.Zero, "Folder dialog must not open before terminal Mesh/Material success.");
                object activeController = GetField<object>(window, "controller");

                Invoke(window, "HandleEditorUpdate");

                Assert.That(selectorCalls, Is.EqualTo(1));
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Cancelled"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<ShapeSyncDocumentAsset>(OutputFolder + "/ExistingMarker.asset"), Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(OutputFolder + "/WindowSuccessNonempty.asset"), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/WindowSuccessNonempty.prefab"), Is.Null);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowSuccessThenOutsideAssetsFolder_ShowsDiagnosticWithoutPublishing()
        {
            GameObject figure = new GameObject("Spec17_6_WindowOutsideFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowOutside";
            var backend = new SuccessBackend();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, dialog = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            Action<string, string, string> originalDialog = null;
            string message = null;
            int selectorCalls = 0;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document);
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                dialog = window.GetType().GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                originalDialog = (Action<string, string, string>)dialog.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => { selectorCalls++; return System.IO.Directory.GetParent(Application.dataPath).FullName; }));
                dialog.SetValue(null, new Action<string, string, string>((_, shownMessage, _) => message = shownMessage));

                Invoke(window, "BeginGenerate");
                Invoke(window, "HandleEditorUpdate");
                Invoke(window, "HandleEditorUpdate");
                Assert.That(selectorCalls, Is.Zero);
                object activeController = GetField<object>(window, "controller");

                Invoke(window, "HandleEditorUpdate");

                Assert.That(selectorCalls, Is.EqualTo(1));
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed"));
                Assert.That(message, Does.Contain("PublishOutputFolderOutsideAssets"));
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
                Assert.That(AssetDatabase.FindAssets("WindowOutside", new[] { "Assets" }), Is.Empty);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (dialog != null) dialog.SetValue(null, originalDialog);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void WindowSuccess_PumpsMeshAndMaterialThenPublishesPrefab()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            string parent = ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17");
            AssetDatabase.CreateFolder(parent, "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowSuccessFigure");
            figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowLook";
            var backend = new SuccessBackend();
            EditorWindow window = null;
            FieldInfo backendFactory = null;
            FieldInfo selector = null;
#if SHAPESYNC_USE_UNIVRM
            FieldInfo providerFactory = null;
            object originalProvider = null;
            bool providerCalled = false;
#endif
            Func<IHumanoidBuildBackend> originalFactory = null;
            Func<string, string, string, string> originalSelector = null;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document);
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
#if SHAPESYNC_USE_UNIVRM
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                originalProvider = providerFactory.GetValue(null);
#endif
                originalFactory = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder)));
#if SHAPESYNC_USE_UNIVRM
                providerFactory.SetValue(null, new Func<IHumanoidVrmTransportExecutor>(() => { providerCalled = true; throw new InvalidOperationException("VRM provider must not run when Transport VRM Physics is OFF."); }));
#endif

                Invoke(window, "BeginGenerate");
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Building Material"));
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Building Material"));
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Publishing Assets"), GetField<string>(window, "warning"));
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Publishing Prefab"));
                Invoke(window, "HandleEditorUpdate");

                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Completed"));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/" + OutputPrefix + ".prefab"), Is.Not.Null);
                Assert.That(GetField<string>(window, "publishedFolder"), Is.EqualTo(OutputFolder));
#if SHAPESYNC_USE_UNIVRM
                Assert.That(providerCalled, Is.False);
                Assert.That(AssetDatabase.IsValidFolder(OutputFolder + "/VRM"), Is.False);
#endif
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalFactory);
                if (selector != null) selector.SetValue(null, originalSelector);
#if SHAPESYNC_USE_UNIVRM
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
#endif
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
                AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowFailure_ShowsStructuredDiagnosticAndResidualWarning()
        {
            GameObject figure = new GameObject("Spec17_6_WindowFailureFigure");
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var backend = new PendingBackend();
            object controller = CreateController(() => backend);
            EditorWindow window = null;
            FieldInfo dialog = null;
            Action<string, string, string> originalDialog = null;
            string title = null;
            string message = null;
            try
            {
                Assert.That(InvokeStart(controller, figure, document, out StackMachineDiagnostic start), Is.True, start?.message);
                ((System.Collections.IList)controller.GetType().GetField("residualArtifactPaths", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Add(ShapeSyncTestAssetPaths.ConsumerAssetPath("Residual.asset"));
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                window.GetType().GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, controller);
                dialog = window.GetType().GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
                originalDialog = (Action<string, string, string>)dialog.GetValue(null);
                dialog.SetValue(null, new Action<string, string, string>((shownTitle, shownMessage, _) => { title = shownTitle; message = shownMessage; }));

                MethodInfo report = window.GetType().GetMethod("ReportFailure", BindingFlags.Instance | BindingFlags.NonPublic);
                report.Invoke(window, new object[] { StackMachineDiagnostic.CreateDomain("humanoid", "WindowTestFailure", "Injected failure.") });

                Assert.That(title, Is.EqualTo("Humanoid Compiler Failed"));
                Assert.That(message, Does.Contain("WindowTestFailure"));
                Assert.That(message, Does.Contain(ShapeSyncTestAssetPaths.ConsumerAssetPath("Residual.asset")));
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed"));
                Assert.That(GetField<string>(window, "warning"), Does.Contain(ShapeSyncTestAssetPaths.ConsumerAssetPath("Residual.asset")));
            }
            finally
            {
                if (dialog != null) dialog.SetValue(null, originalDialog);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                ((IDisposable)controller).Dispose();
                UnityEngine.Object.DestroyImmediate(document);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void WindowMeshPumpFailure_ShowsDiagnosticAndDoesNotBeginPublish()
        {
            GameObject figure = new GameObject("Spec17_6_WindowMeshFailureFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowMeshFailure";
            var backend = new FailingMeshBackend();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, dialog = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            Action<string, string, string> originalDialog = null;
            string message = null;
            int selectorCalls = 0;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document);
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                dialog = window.GetType().GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                originalDialog = (Action<string, string, string>)dialog.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => { selectorCalls++; return Application.dataPath; }));
                dialog.SetValue(null, new Action<string, string, string>((_, shownMessage, _) => message = shownMessage));

                Invoke(window, "BeginGenerate");
                object activeController = GetField<object>(window, "controller");
                Invoke(window, "HandleEditorUpdate");

                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed"));
                Assert.That(message, Does.Contain("WindowMeshPumpFailed"));
                Assert.That(selectorCalls, Is.Zero);
                Assert.That(backend.Cancelled, Is.True);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (dialog != null) dialog.SetValue(null, originalDialog);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure);
            }
        }

#if SHAPESYNC_USE_UNIVRM
        [Test]
        public void WindowSuccessThenInvalidVrmRelativeFolder_ShowsDiagnosticWithoutProviderOrPublish()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowInvalidVrmPathFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowInvalidVrmPath";
            var backend = new SuccessBackend(includeVrmProvenance: true);
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, dialog = null, providerFactory = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            Action<string, string, string> originalDialog = null;
            object originalProvider = null;
            string message = null;
            bool providerCalled = false;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "transportVrmPhysics", true); SetField(window, "vrmAssetRelativeFolder", "../Escape");
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                dialog = window.GetType().GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
                originalSelector = (Func<string, string, string, string>)selector.GetValue(null);
                originalDialog = (Action<string, string, string>)dialog.GetValue(null);
                originalProvider = providerFactory.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder)));
                dialog.SetValue(null, new Action<string, string, string>((_, shownMessage, _) => message = shownMessage));
                providerFactory.SetValue(null, new Func<IHumanoidVrmTransportExecutor>(() => { providerCalled = true; throw new InvalidOperationException("Provider must not run for an invalid VRM relative folder."); }));

                Invoke(window, "BeginGenerate");
                Invoke(window, "HandleEditorUpdate");
                Invoke(window, "HandleEditorUpdate");
                object activeController = GetField<object>(window, "controller");

                Invoke(window, "HandleEditorUpdate");

                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed"));
                Assert.That(message, Does.Contain("VrmPublishRelativeFolderInvalid"));
                Assert.That(providerCalled, Is.False);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(OutputFolder + "/WindowInvalidVrmPath.asset"), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/WindowInvalidVrmPath.prefab"), Is.Null);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (dialog != null) dialog.SetValue(null, originalDialog);
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowVrmOn_TransportsStagesAndFinalizesBeforeCompleted()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowVrmSuccessFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowVrmSuccess";
            var backend = new SuccessBackend(includeVrmProvenance: true);
            var executor = new RecordingVrmExecutor();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, providerFactory = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            object originalProvider = null;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "transportVrmPhysics", true); SetField(window, "vrmAssetRelativeFolder", "VRM");
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null); originalSelector = (Func<string, string, string, string>)selector.GetValue(null); originalProvider = providerFactory.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder)));
                providerFactory.SetValue(null, new Func<IHumanoidVrmTransportExecutor>(() => executor));
                executor.ProgressReader = () => GetField<string>(window, "progress");

                Invoke(window, "BeginGenerate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Publishing Assets"));
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Initializing VRM"));
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Transporting Physics"));
                Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Publishing Prefab"));
                Invoke(window, "HandleEditorUpdate");

                Assert.That(executor.Calls, Is.EqualTo(new[] { "Transport", "Stage", "Finalize" }));
                Assert.That(executor.ProgressDuringTransport, Is.EqualTo("Initializing VRM"));
                Assert.That(executor.ProgressDuringStage, Is.EqualTo("Transporting Physics"));
                Assert.That(executor.ProgressDuringFinalize, Is.EqualTo("Publishing Prefab"));
                Assert.That(executor.TransportCandidate, Is.Not.SameAs(figure));
                Assert.That(executor.TransportSource, Is.SameAs(figure));
                Assert.That(executor.StageRelativeFolder, Is.EqualTo("VRM"));
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Completed"));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/__Spec17_6_WindowOutput.prefab"), Is.Not.Null);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowVrmOn_CancelAfterIndividualStageReportsResidualWithoutTransport()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowVrmCancelFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowVrmCancel";
            var backend = new SuccessBackend(includeVrmProvenance: true);
            var executor = new RecordingVrmExecutor();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, providerFactory = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            object originalProvider = null;
            bool providerRequested = false;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "transportVrmPhysics", true); SetField(window, "vrmAssetRelativeFolder", "VRM");
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null); originalSelector = (Func<string, string, string, string>)selector.GetValue(null); originalProvider = providerFactory.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder)));
                providerFactory.SetValue(null, new Func<IHumanoidVrmTransportExecutor>(() => { providerRequested = true; return executor; }));

                Invoke(window, "BeginGenerate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Initializing VRM"));
                object activeController = GetField<object>(window, "controller");

                InvokeWithString(window, "CancelBuild", "Cancelled during VRM initialization.");

                Assert.That(providerRequested, Is.False);
                Assert.That(executor.Calls, Is.Empty);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Cancelled"));
                Assert.That(GetField<string>(window, "warning"), Does.Contain("Cancelled during VRM initialization."));
                Assert.That(GetField<string>(window, "warning"), Does.Contain("Persistent artifacts were left for manual inspection"));
                Assert.That(GetField<string>(window, "warning"), Does.Contain("__Spec17_6_WindowOutput.asset"));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/__Spec17_6_WindowOutput.prefab"), Is.Null);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WindowLifecycleAfterIndividualStage_LogsResidualWarningWithoutTransport(bool assemblyReload)
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowLifecycleFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowVrmLifecycle";
            var backend = new SuccessBackend(includeVrmProvenance: true);
            var executor = new RecordingVrmExecutor();
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, providerFactory = null, logWarning = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            object originalProvider = null;
            Action<string> originalLogWarning = null;
            bool providerRequested = false;
            string warning = null;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "transportVrmPhysics", true); SetField(window, "vrmAssetRelativeFolder", "VRM");
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                logWarning = window.GetType().GetField("LogWarning", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null); originalSelector = (Func<string, string, string, string>)selector.GetValue(null); originalProvider = providerFactory.GetValue(null); originalLogWarning = (Action<string>)logWarning.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder)));
                providerFactory.SetValue(null, new Func<IHumanoidVrmTransportExecutor>(() => { providerRequested = true; return executor; }));
                logWarning.SetValue(null, new Action<string>(message => warning = message));

                Invoke(window, "BeginGenerate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate");
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Initializing VRM"));
                object activeController = GetField<object>(window, "controller");

                Invoke(window, assemblyReload ? "HandleBeforeAssemblyReload" : "OnDisable");

                Assert.That(providerRequested, Is.False);
                Assert.That(executor.Calls, Is.Empty);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(activeController, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(GetProperty(activeController, "Candidate"), Is.Null);
                Assert.That(warning, Does.Contain("Persistent artifacts were left for manual inspection"));
                Assert.That(warning, Does.Contain("__Spec17_6_WindowOutput.asset"));
                Assert.That(warning, Does.Contain(assemblyReload ? "assembly reload" : "window was closed"));
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
                if (logWarning != null) logWarning.SetValue(null, originalLogWarning);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowVrmOn_StageFailureShowsDiagnosticAndResidualWarningWithoutPrefab()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowVrmStageFailureFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowVrmStageFailure";
            var backend = new SuccessBackend(includeVrmProvenance: true);
            var executor = new RecordingVrmExecutor { FailStage = true };
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, dialog = null, providerFactory = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            Action<string, string, string> originalDialog = null;
            object originalProvider = null;
            string message = null;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "transportVrmPhysics", true); SetField(window, "vrmAssetRelativeFolder", "VRM");
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                dialog = window.GetType().GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null); originalSelector = (Func<string, string, string, string>)selector.GetValue(null); originalDialog = (Action<string, string, string>)dialog.GetValue(null); originalProvider = providerFactory.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder)));
                dialog.SetValue(null, new Action<string, string, string>((_, shownMessage, _) => message = shownMessage));
                providerFactory.SetValue(null, new Func<IHumanoidVrmTransportExecutor>(() => executor));

                Invoke(window, "BeginGenerate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate");
                Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate"); Invoke(window, "HandleEditorUpdate");

                Assert.That(executor.Calls, Is.EqualTo(new[] { "Transport", "Stage" }));
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed"));
                Assert.That(message, Does.Contain("WindowVrmStageFailed"));
                Assert.That(message, Does.Contain(ShapeSyncTestAssetPaths.ConsumerAssetPath("Spec17_6_WindowVrmStagePartial.asset")));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/WindowVrmStageFailure.prefab"), Is.Null);
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (dialog != null) dialog.SetValue(null, originalDialog);
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }

        [Test]
        public void WindowVrmOn_ReportsMissingOptionalProviderAfterIndividualStage()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec17"), "__Spec17_6_WindowOutput");
            GameObject figure = new GameObject("Spec17_6_WindowVrmFigure"); figure.AddComponent<SkinnedMeshRenderer>();
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.name = "WindowVrm";
            var backend = new SuccessBackend(includeVrmProvenance: true);
            EditorWindow window = null;
            FieldInfo backendFactory = null, selector = null, dialog = null, providerFactory = null;
            Func<IHumanoidBuildBackend> originalBackend = null;
            Func<string, string, string, string> originalSelector = null;
            object originalProvider = null;
            Action<string, string, string> originalDialog = null;
            string message = null;
            try
            {
                window = ScriptableObject.CreateInstance(typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidCompilerWindow", true)) as EditorWindow;
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "transportVrmPhysics", true); SetField(window, "vrmAssetRelativeFolder", "VRM");
                backendFactory = window.GetType().GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
                selector = window.GetType().GetField("SelectOutputFolder", BindingFlags.Static | BindingFlags.NonPublic);
                dialog = window.GetType().GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
                providerFactory = typeof(HumanoidVrmTransportExecutorProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);
                originalBackend = (Func<IHumanoidBuildBackend>)backendFactory.GetValue(null); originalSelector = (Func<string, string, string, string>)selector.GetValue(null); originalDialog = (Action<string, string, string>)dialog.GetValue(null); originalProvider = providerFactory.GetValue(null);
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend)); selector.SetValue(null, new Func<string, string, string, string>((_, _, _) => System.IO.Path.GetFullPath(OutputFolder))); dialog.SetValue(null, new Action<string, string, string>((_, shownMessage, _) => message = shownMessage)); providerFactory.SetValue(null, null);

                Invoke(window, "BeginGenerate");
                for (int i = 0; i < 16 && GetField<string>(window, "progress") != "Failed"; i++) Invoke(window, "HandleEditorUpdate");

                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed"));
                Assert.That(GetField<string>(window, "warning"), Does.Contain("VrmTransportExecutorRequired"));
                Assert.That(GetField<string>(window, "warning"), Does.Contain("Persistent artifacts were left for manual inspection"));
            }
            finally
            {
                if (backendFactory != null) backendFactory.SetValue(null, originalBackend);
                if (selector != null) selector.SetValue(null, originalSelector);
                if (dialog != null) dialog.SetValue(null, originalDialog);
                if (providerFactory != null) providerFactory.SetValue(null, originalProvider);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                backend.Dispose(); UnityEngine.Object.DestroyImmediate(document); UnityEngine.Object.DestroyImmediate(figure); AssetDatabase.DeleteAsset(OutputFolder);
            }
        }
#endif

        private static object CreateController(Func<IHumanoidBuildBackend> factory)
        {
            Type type = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidEditorBuildController", true);
            return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { factory }, null);
        }

        private static InMemoryHumanoidMesh CreateResolvedMesh(Mesh mesh)
        {
            mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
            };
            var root = new GameObject("Spec17_6_WindowResolvedHumanoid");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            Transform bone = new GameObject("FinalBone").transform;
            bone.SetParent(root.transform, false);
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { bone };
            renderer.rootBone = bone;
            return new InMemoryHumanoidMesh(root, mesh, null);
        }

        private static bool InvokeStart(object controller, GameObject figure, ShapeSyncDocumentAsset document, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { figure, document, null };
            bool result = (bool)controller.GetType().GetMethod("TryStart", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[2];
            return result;
        }

        private static object GetProperty(object instance, string name) => instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void SetField(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static T GetField<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        private static void InvokeWithString(object instance, string name, string value) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, new object[] { value });

        private sealed class PendingBackend : IHumanoidBuildBackend
        {
            internal bool Cancelled { get; private set; }
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic) { meshPayload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic diagnostic) { materialPayload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public void Cancel() { Cancelled = true; }
        }

        private sealed class FailingMeshBackend : IHumanoidBuildBackend
        {
            internal bool Cancelled { get; private set; }
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic)
            {
                meshPayload = null;
                diagnostic = StackMachineDiagnostic.CreateDomain("mesh", "WindowMeshPumpFailed", "Injected Mesh Pump failure.");
                return HumanoidBuildPhaseStatus.Failed;
            }
            public bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic diagnostic) { materialPayload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public void Cancel() { Cancelled = true; }
        }

        private sealed class SuccessBackend : IHumanoidBuildBackend, IDisposable
        {
            private readonly Material source = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            private readonly UrpUnlitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            private readonly bool includeVrmProvenance;
            private bool meshReturned;
            internal SuccessBackend(bool includeVrmProvenance = false) { this.includeVrmProvenance = includeVrmProvenance; }
            public bool TryBeginMeshPhase(HumanoidBuildSource sourceInput, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic)
            {
                diagnostic = null;
                if (meshReturned) { payload = null; return HumanoidBuildPhaseStatus.Failed; }
                meshReturned = true;
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                HumanoidMeshVrmTransportProvenance provenance = null;
                if (includeVrmProvenance)
                {
                    var plan = new HumanoidMeshLogicalPlan(null, default, Array.Empty<HumanoidMeshSource>(), Array.Empty<HumanoidMeshSource>(), Array.Empty<HumanoidMeshSource>(), Array.Empty<HumanoidMeshNormalSource>(), Array.Empty<HumanoidMeshNormalTextureRegistration>());
                    if (!HumanoidMeshVrmTransportProvenance.TryCreate(plan, out provenance, out StackMachineDiagnostic provenanceDiagnostic)) throw new InvalidOperationException(provenanceDiagnostic?.message);
                }
                payload = new MeshBuildPayload(CreateResolvedMesh(mesh), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, Array.Empty<HumanoidBuildSourceNormal>(), Array.Empty<HumanoidBuildComputedNormal>(), provenance);
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            public bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = new MaterialBuildPayload(Array.Empty<HumanoidMaterialSemanticPayload>()); diagnostic = null; return HumanoidBuildPhaseStatus.Succeeded; }
            public void Cancel() { }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(adapter); }
        }

#if SHAPESYNC_USE_UNIVRM
        private sealed class RecordingVrmExecutor : IHumanoidVrmTransportExecutor
        {
            private sealed class Result : IDisposable { public void Dispose() { } }
            internal readonly System.Collections.Generic.List<string> Calls = new System.Collections.Generic.List<string>();
            internal bool FailStage;
            internal GameObject TransportCandidate { get; private set; }
            internal GameObject TransportSource { get; private set; }
            internal string StageRelativeFolder { get; private set; }
            internal Func<string> ProgressReader { get; set; }
            internal string ProgressDuringTransport { get; private set; }
            internal string ProgressDuringStage { get; private set; }
            internal string ProgressDuringFinalize { get; private set; }

            public bool TryTransport(GameObject candidate, GameObject figureSourceRoot, ShapeSyncDocument document, HumanoidVrmTransportProvenance provenance, out IDisposable result, out StackMachineDiagnostic diagnostic)
            {
                Calls.Add("Transport"); ProgressDuringTransport = ProgressReader?.Invoke(); TransportCandidate = candidate; TransportSource = figureSourceRoot; result = new Result(); diagnostic = null; return true;
            }

            public bool TryStageAssets(IDisposable transportResult, string outputFolder, string relativeFolder, string documentName, out System.Collections.Generic.IReadOnlyList<string> assetPaths, out StackMachineDiagnostic diagnostic)
            {
                Calls.Add("Stage"); ProgressDuringStage = ProgressReader?.Invoke(); StageRelativeFolder = relativeFolder;
                if (FailStage)
                {
                    assetPaths = new[] { ShapeSyncTestAssetPaths.ConsumerAssetPath("Spec17_6_WindowVrmStagePartial.asset") };
                    diagnostic = StackMachineDiagnostic.CreateDomain("vrm", "WindowVrmStageFailed", "Injected VRM stage failure.");
                    return false;
                }
                assetPaths = Array.Empty<string>(); diagnostic = null; return true;
            }

            public bool TryFinalizeAssets(IDisposable transportResult, GameObject publishedPrefabRoot, out StackMachineDiagnostic diagnostic)
            {
                Calls.Add("Finalize"); ProgressDuringFinalize = ProgressReader?.Invoke(); diagnostic = null; return true;
            }

            public void ReleaseAssetOwnership(IDisposable transportResult) { }
        }
#endif
    }
}
