// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>Phase07-1 M03 coverage for structured Material diagnostics and Figure warning provenance.</summary>
    public sealed class Spec152MaterialTests
    {
        private static readonly BindingFlags FormatterFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private Spec152GpuScope scope;
        private TextureHallAllocation externalHall;
        private float timeScaleBefore;
        private Scene previousActiveScene;
        private Scene fixtureScene;

        [SetUp]
        public void SetUp()
        {
            scope = new Spec152GpuScope();
            externalHall = default;
            fixtureScene = default;
            previousActiveScene = SceneManager.GetActiveScene();
            timeScaleBefore = Time.timeScale;
            Debug.Log("Spec152 Material timeScale before=" + timeScaleBefore.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(previousActiveScene.IsValid() && previousActiveScene.isLoaded, Is.True);
            fixtureScene = SceneManager.CreateScene("Spec152MaterialIsolation_" + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(fixtureScene), Is.True);
        }

        [UnityTearDown]
        public IEnumerator CleanupFixture()
        {
            try
            {
                if (scope != null && scope.Host != null && externalHall.IsValid)
                    scope.Host.TryReleaseHall(externalHall);
                yield return Spec152GpuFixture.Cleanup(scope);
            }
            finally
            {
                // GPU cleanupが失敗してもactiveだけは元へ戻す。失敗時のscene強制解放はしない。
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    Assert.That(SceneManager.SetActiveScene(previousActiveScene), Is.True);
            }

            Assert.That(previousActiveScene.IsValid() && previousActiveScene.isLoaded, Is.True);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(previousActiveScene));
            if (fixtureScene.IsValid() && fixtureScene.isLoaded)
            {
                TextureStaticMachineFactory.Invalidate(fixtureScene);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(fixtureScene);
                Assert.That(unload, Is.Not.Null);
                double deadline = Time.realtimeSinceStartupAsDouble + 30.0;
                while (!unload.isDone && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That(unload.isDone, Is.True, "Material fixture scene unload exceeded 30 seconds.");
                Assert.That(fixtureScene.isLoaded, Is.False);
            }
            Debug.Log("Spec152 Material timeScale after=" + Time.timeScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            scope = null;
            externalHall = default;
            fixtureScene = default;
            previousActiveScene = default;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_M01_Material_WaitsAllTextures()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Assert.That(host.gameObject.scene, Is.EqualTo(fixtureScene));
            Assert.That(TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost resolvedHost,
                out StackMachineDiagnostic resolveDiagnostic), Is.True, resolveDiagnostic?.message);
            Assert.That(resolvedHost, Is.SameAs(host));
            Assert.That(host.TryReserveHall(256, 256, out externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);

            GameObject target = new GameObject("Spec152 M01 Material Target");
            Material bodySource = null;
            Material faceSource = null;
            MaterialShaderAdapter bodyAdapter = null;
            MaterialShaderAdapter faceAdapter = null;
            TextureBindingTemplate textureTemplate = null;
            MaterialStackMachineOperation operation = null;
            try
            {
                MaterialAttacher attacher = ConfigureTwoEntryTarget(target, out SkinnedMeshRenderer bodyRenderer, out bodySource, out bodyAdapter, out SkinnedMeshRenderer faceRenderer, out faceSource, out faceAdapter);
                textureTemplate = CreateOutputTemplate();
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                machine.TextureBindingTemplate = textureTemplate;
                machine.enabled = false;

                host.testFencePending = true;
                const string texture = "TEXTURE 256 128 RECTSIZE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE";
                string recipe = "$body MATERIAL " + texture + " $face MATERIAL " + texture;
                Assert.That(machine.TryExecute(recipe, out operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation, Is.Not.Null);
                Assert.That(operation.IsCompleted, Is.False);

                FieldInfo itemsField = typeof(MaterialStackMachineOperation).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo operationTick = typeof(MaterialStackMachineOperation).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(itemsField, Is.Not.Null);
                Assert.That(operationTick, Is.Not.Null);
                IList items = itemsField.GetValue(operation) as IList;
                Assert.That(items, Is.Not.Null);
                Assert.That(items.Count, Is.EqualTo(2));
                FieldInfo textureHandleField = items[0].GetType().GetField("textureHandle", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(textureHandleField, Is.Not.Null);
                TextureExecutionHandle first = textureHandleField.GetValue(items[0]) as TextureExecutionHandle;
                TextureExecutionHandle second = textureHandleField.GetValue(items[1]) as TextureExecutionHandle;
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(first, Is.Not.SameAs(second));

                // Drive the host once while the external Hall is still held, then drive only this
                // operation. This makes the first handle explicitly WaitingForHalls and leaves the
                // FIFO successor Queued before the Hall is released.
                scope.Tick();
                operationTick.Invoke(operation, null);
                Assert.That(first.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
                Assert.That(second.Status, Is.EqualTo(TextureExecutionStatus.Queued));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(host.PendingRequestCount, Is.EqualTo(2), "both Texture blocks remain queued behind the external full-grid Hall");
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource), "no Material entry may commit while Texture work is incomplete");
                Assert.That(faceRenderer.sharedMaterial, Is.SameAs(faceSource), "no Material entry may commit while Texture work is incomplete");

                Assert.That(host.TryReleaseHall(externalHall), Is.True);
                externalHall = default;
                scope.Tick();
                Assert.That(first.Status, Is.EqualTo(TextureExecutionStatus.Submitted));
                FieldInfo submittedField = typeof(TextureStackMachineHost).GetField("submitted", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(submittedField, Is.Not.Null);
                object submittedRequest = submittedField.GetValue(host);
                Assert.That(submittedRequest, Is.Not.Null);
                FieldInfo fenceField = submittedRequest.GetType().GetField("fence", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(fenceField, Is.Not.Null);
                UnityEngine.Rendering.GraphicsFence firstFence = (UnityEngine.Rendering.GraphicsFence)fenceField.GetValue(submittedRequest);
                yield return Spec152GpuFixture.WaitForRealGpu(() => firstFence.passed);

                // Finish only the first GPU request while keeping the successor from completing. The
                // operation must still escrow both entries and keep both original Materials.
                host.testFencePending = false;
                scope.Tick();
                host.testFencePending = true;
                operationTick.Invoke(operation, null);
                Assert.That(first.IsCompleted, Is.True);
                Assert.That(first.Succeeded, Is.True, first.Diagnostic?.message);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(operation.IsCompleted, Is.False, "one completed Texture is insufficient for Material commit");
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource), "body remains unchanged while the second Texture is incomplete");
                Assert.That(faceRenderer.sharedMaterial, Is.SameAs(faceSource), "face remains unchanged while the second Texture is incomplete");

                host.testFencePending = false;
                yield return Spec152GpuFixture.WaitForRealGpu(() => second.IsCompleted);
                Assert.That(second.Succeeded, Is.True, second.Diagnostic?.message);
                operationTick.Invoke(operation, null);
                Assert.That(operation.Result, Is.Not.Null);
                Assert.That(operation.Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                Assert.That(bodyRenderer.sharedMaterial, Is.Not.SameAs(bodySource), "body commits after every Texture succeeds");
                Assert.That(faceRenderer.sharedMaterial, Is.Not.SameAs(faceSource), "face commits after every Texture succeeds");
            }
            finally
            {
                host.testFencePending = false;
                if (externalHall.IsValid) host.TryReleaseHall(externalHall);
                if (operation != null && !operation.IsCompleted) operation.Dispose();
                if (textureTemplate != null) UnityEngine.Object.DestroyImmediate(textureTemplate);
                if (bodyAdapter != null) UnityEngine.Object.DestroyImmediate(bodyAdapter);
                if (faceAdapter != null) UnityEngine.Object.DestroyImmediate(faceAdapter);
                if (bodySource != null) UnityEngine.Object.DestroyImmediate(bodySource);
                if (faceSource != null) UnityEngine.Object.DestroyImmediate(faceSource);
                UnityEngine.Object.DestroyImmediate(target);
            }
#else
            Assert.Ignore("Material GPU orchestration tests require UnityEditor-only setup.");
#endif
            yield break;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_M02_Material_DryRunRejectPreservesMaterials()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Assert.That(host.gameObject.scene, Is.EqualTo(fixtureScene));
            Assert.That(TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost resolvedHost,
                out StackMachineDiagnostic resolveDiagnostic), Is.True, resolveDiagnostic?.message);
            Assert.That(resolvedHost, Is.SameAs(host));
            Assert.That(host.TryReserveHall(256, 256, out externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);

            GameObject target = new GameObject("Spec152 M02 Material Target");
            Material bodySource = null;
            Material faceSource = null;
            MaterialShaderAdapter bodyAdapter = null;
            MaterialShaderAdapter faceAdapter = null;
            TextureBindingTemplate textureTemplate = null;
            MaterialStackMachineOperation operation = null;
            try
            {
                MaterialAttacher attacher = ConfigureTwoEntryTarget(target, out SkinnedMeshRenderer bodyRenderer, out bodySource, out bodyAdapter, out SkinnedMeshRenderer faceRenderer, out faceSource, out faceAdapter);
                textureTemplate = CreateOutputTemplate();
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                machine.TextureBindingTemplate = textureTemplate;

                host.testFencePending = true;
                const string texture = "TEXTURE 256 128 RECTSIZE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE";
                string recipe = "$body MATERIAL " + texture + " $missing MATERIAL " + texture;
                Assert.That(machine.TryExecute(recipe, out operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation, Is.Not.Null);
                FieldInfo itemsField = typeof(MaterialStackMachineOperation).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(itemsField, Is.Not.Null);
                IList items = itemsField.GetValue(operation) as IList;
                Assert.That(items, Is.Not.Null);
                Assert.That(items.Count, Is.EqualTo(2));
                FieldInfo textureHandleField = items[0].GetType().GetField("textureHandle", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(textureHandleField, Is.Not.Null);
                TextureExecutionHandle first = textureHandleField.GetValue(items[0]) as TextureExecutionHandle;
                TextureExecutionHandle second = textureHandleField.GetValue(items[1]) as TextureExecutionHandle;
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(first, Is.Not.SameAs(second));
                yield return null;
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(host.PendingRequestCount, Is.EqualTo(2), "both Texture candidates are waiting behind the external full-grid Hall");
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource), "the valid entry remains untouched while candidates wait");
                Assert.That(faceRenderer.sharedMaterial, Is.SameAs(faceSource), "the target remains untouched while candidates wait");

                host.testFencePending = false;
                Assert.That(host.TryReleaseHall(externalHall), Is.True);
                externalHall = default;
                yield return Spec152GpuFixture.WaitForRealGpu(() => operation.IsCompleted);
                Assert.That(operation.Result, Is.Not.Null);
                Assert.That(operation.Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Rejected));
                Assert.That(first.Succeeded, Is.True, first.Diagnostic?.message);
                Assert.That(second.Succeeded, Is.True, second.Diagnostic?.message);
                Assert.That(operation.Result.Diagnostic, Is.Not.Null);
                Assert.That(operation.Result.Diagnostic.domainCode, Is.EqualTo("DryRunRejected"));
                Assert.That(host.PendingRequestCount, Is.Zero, "all Texture work completed before the DryRun rejection");
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource), "a later invalid binding prevents every Material commit");
                Assert.That(faceRenderer.sharedMaterial, Is.SameAs(faceSource), "a rejected transaction leaves the target materials unchanged");
            }
            finally
            {
                host.testFencePending = false;
                if (externalHall.IsValid) host.TryReleaseHall(externalHall);
                if (operation != null && !operation.IsCompleted) operation.Dispose();
                if (textureTemplate != null) UnityEngine.Object.DestroyImmediate(textureTemplate);
                if (bodyAdapter != null) UnityEngine.Object.DestroyImmediate(bodyAdapter);
                if (faceAdapter != null) UnityEngine.Object.DestroyImmediate(faceAdapter);
                if (bodySource != null) UnityEngine.Object.DestroyImmediate(bodySource);
                if (faceSource != null) UnityEngine.Object.DestroyImmediate(faceSource);
                UnityEngine.Object.DestroyImmediate(target);
            }
#else
            Assert.Ignore("Material GPU orchestration tests require UnityEditor-only setup.");
#endif
            yield break;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_M02_Material_RejectCancelsWaiting()
        {
#if UNITY_EDITOR
            Spec152GpuFixture.CreateHost(scope, 256);
            TextureStackMachineHost host = scope.Host;
            Assert.That(host.gameObject.scene, Is.EqualTo(fixtureScene));
            Assert.That(TextureStaticMachineFactory.TryGetTSM(out TextureStackMachineHost resolvedHost,
                out StackMachineDiagnostic resolveDiagnostic), Is.True, resolveDiagnostic?.message);
            Assert.That(resolvedHost, Is.SameAs(host));
            Assert.That(host.TryReserveHall(256, 256, out externalHall, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);

            GameObject target = new GameObject("Spec152 M02 Cancel Target");
            Material bodySource = null;
            Material faceSource = null;
            MaterialShaderAdapter bodyAdapter = null;
            MaterialShaderAdapter faceAdapter = null;
            TextureBindingTemplate textureTemplate = null;
            MaterialStackMachineOperation operation = null;
            try
            {
                MaterialAttacher attacher = ConfigureTwoEntryTarget(target, out SkinnedMeshRenderer bodyRenderer, out bodySource, out bodyAdapter, out SkinnedMeshRenderer faceRenderer, out faceSource, out faceAdapter);
                textureTemplate = CreateOutputTemplate();
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                machine.TextureBindingTemplate = textureTemplate;

                host.testFencePending = true;
                const string texture = "TEXTURE 256 128 RECTSIZE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE";
                string recipe = "$body MATERIAL " + texture + " $face MATERIAL " + texture;
                Assert.That(machine.TryExecute(recipe, out operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation, Is.Not.Null);

                FieldInfo itemsField = typeof(MaterialStackMachineOperation).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(itemsField, Is.Not.Null);
                IList items = itemsField.GetValue(operation) as IList;
                Assert.That(items, Is.Not.Null);
                Assert.That(items.Count, Is.EqualTo(2));
                FieldInfo textureHandleField = items[0].GetType().GetField("textureHandle", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(textureHandleField, Is.Not.Null);
                TextureExecutionHandle first = textureHandleField.GetValue(items[0]) as TextureExecutionHandle;
                TextureExecutionHandle second = textureHandleField.GetValue(items[1]) as TextureExecutionHandle;
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(first, Is.Not.SameAs(second));

                scope.Tick();
                Assert.That(first.Status, Is.EqualTo(TextureExecutionStatus.WaitingForHalls));
                Assert.That(second.Status, Is.EqualTo(TextureExecutionStatus.Queued));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(host.PendingRequestCount, Is.EqualTo(2));
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource));
                Assert.That(faceRenderer.sharedMaterial, Is.SameAs(faceSource));

                StackMachineDiagnostic injected = StackMachineDiagnostic.CreateDomain(
                    "material", "Spec152InjectedRejection", "Designer-specified rejection seam.",
                    bindingName: "face", detail: "M02 waiting cancellation seam");
                MethodInfo reject = typeof(MaterialStackMachineOperation).GetMethod("Reject", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(reject, Is.Not.Null);
                reject.Invoke(operation, new object[] { injected });

                Assert.That(operation.IsCompleted, Is.True);
                Assert.That(operation.Result, Is.Not.Null);
                Assert.That(operation.Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Rejected));
                Assert.That(operation.Result.Diagnostic, Is.SameAs(injected));
                Assert.That(first.IsCompleted, Is.True);
                Assert.That(second.IsCompleted, Is.True);
                Assert.That(first.Succeeded, Is.False);
                Assert.That(second.Succeeded, Is.False);
                Assert.That(first.Status, Is.EqualTo(TextureExecutionStatus.Completed));
                Assert.That(second.Status, Is.EqualTo(TextureExecutionStatus.Completed));
                Assert.That(first.Diagnostic, Is.Not.Null);
                Assert.That(second.Diagnostic, Is.Not.Null);
                Assert.That(first.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(second.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
                Assert.That(bodyRenderer.sharedMaterial, Is.SameAs(bodySource));
                Assert.That(faceRenderer.sharedMaterial, Is.SameAs(faceSource));
            }
            finally
            {
                host.testFencePending = false;
                if (externalHall.IsValid) host.TryReleaseHall(externalHall);
                if (operation != null && !operation.IsCompleted) operation.Dispose();
                if (textureTemplate != null) UnityEngine.Object.DestroyImmediate(textureTemplate);
                if (bodyAdapter != null) UnityEngine.Object.DestroyImmediate(bodyAdapter);
                if (faceAdapter != null) UnityEngine.Object.DestroyImmediate(faceAdapter);
                if (bodySource != null) UnityEngine.Object.DestroyImmediate(bodySource);
                if (faceSource != null) UnityEngine.Object.DestroyImmediate(faceSource);
                UnityEngine.Object.DestroyImmediate(target);
            }
#else
            Assert.Ignore("Material GPU orchestration tests require UnityEditor-only setup.");
#endif
            yield break;
        }

        [Test]
        public void Spec152_M03_Diagnostics_PreserveDetail()
        {
            MethodInfo formatter = typeof(MaterialStackMachine).GetMethod("FormatDiagnostic", FormatterFlags);
            Assert.That(formatter, Is.Not.Null, "MaterialStackMachine.FormatDiagnostic must remain the tested private boundary.");

            StackMachineDiagnostic diagnostic = StackMachineDiagnostic.CreateDomain(
                "material", "FigureTargetRejected", "Figure target was rejected.",
                bindingName: "a", detail: "marker=15-2");
            string formatted = (string)formatter.Invoke(null, new object[] { diagnostic });
            StringAssert.Contains("bindingName=a", formatted);
            StringAssert.Contains("detail=marker=15-2", formatted);

            GameObject target = new GameObject("Spec152 M03 Figure Target");
            MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
            var capture = new CapturingLogHandler();
            ILogHandler previousHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                var payload = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument
                    {
                        wordSource = "FIGURE $a MATERIAL 0 1 0 1 COLOR"
                    }
                };

                Assert.That(machine.TryAcceptRecipePayload(payload, out _, out StackMachineDiagnostic rejection), Is.True, rejection?.message);
                Assert.That(capture.LogType, Is.EqualTo(LogType.Warning));
                StringAssert.Contains(target.name, capture.Message);
                StringAssert.Contains("instanceID=" + target.GetInstanceID(), capture.Message);
                Assert.That(capture.Context, Is.SameAs(machine), "Figure warning context must remain the target MaterialStackMachine.");
            }
            finally
            {
                Debug.unityLogger.logHandler = previousHandler;
                UnityEngine.Object.DestroyImmediate(binding);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private sealed class CapturingLogHandler : ILogHandler
        {
            internal LogType LogType { get; private set; }
            internal string Message { get; private set; }
            internal UnityEngine.Object Context { get; private set; }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                LogType = LogType.Exception;
                Message = exception == null ? string.Empty : exception.ToString();
                Context = context;
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                LogType = logType;
                Message = string.Format(format, args);
                Context = context;
            }
        }

        private static TextureBindingTemplate CreateOutputTemplate()
        {
            TextureBindingTemplate template = ScriptableObject.CreateInstance<TextureBindingTemplate>();
            template.SetBindings(new[] { new TextureTemplateEntry { word = "out", kind = TextureBindingKind.OutputHall } });
            return template;
        }

        private static MaterialAttacher ConfigureTwoEntryTarget(
            GameObject target,
            out SkinnedMeshRenderer bodyRenderer,
            out Material bodySource,
            out MaterialShaderAdapter bodyAdapter,
            out SkinnedMeshRenderer faceRenderer,
            out Material faceSource,
            out MaterialShaderAdapter faceAdapter)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);

            GameObject bodyObject = new GameObject("BodyRenderer");
            bodyObject.transform.SetParent(target.transform, false);
            bodyRenderer = bodyObject.AddComponent<SkinnedMeshRenderer>();
            bodySource = new Material(shader);
            bodyRenderer.sharedMaterial = bodySource;
            bodyAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();

            GameObject faceObject = new GameObject("FaceRenderer");
            faceObject.transform.SetParent(target.transform, false);
            faceRenderer = faceObject.AddComponent<SkinnedMeshRenderer>();
            faceSource = new Material(shader);
            faceRenderer.sharedMaterial = faceSource;
            faceAdapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();

            MaterialProxy proxy = target.AddComponent<MaterialProxy>();
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "body", renderer = bodyRenderer, materialChannel = 0, adapter = bodyAdapter },
                new MaterialProxyEntry { entryName = "face", renderer = faceRenderer, materialChannel = 0, adapter = faceAdapter }
            });
            MaterialAttacher attacher = target.AddComponent<MaterialAttacher>();
            attacher.Proxy = proxy;
            return attacher;
        }
    }
}
