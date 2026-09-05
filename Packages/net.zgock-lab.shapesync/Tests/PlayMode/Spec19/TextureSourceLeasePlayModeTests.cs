// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class TextureSourceLeasePlayModeTests
    {
        [UnityTest]
        public IEnumerator CompiledPlan_ExecutesWithoutRecompilingTheRecipe()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            try
            {
                Assert.That(TextureExecutionPlan.TryCreate(CreateCopyStub(red), out TextureExecutionPlan plan, out StackMachineDiagnostic compileDiagnostic), Is.True, compileDiagnostic?.message);
                Assert.That(new TextureExecutor(host).TryExecute(plan, host.CreateOrigin(), null, out TextureExecutionHandle handle, out StackMachineDiagnostic dispatchDiagnostic), Is.True, dispatchDiagnostic?.message);
                yield return Wait(handle);
                Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
                Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                using (delivery) yield return AssertPixel(delivery.Texture, new Vector4(1f, 0f, 0f, 1f));
            }
            finally
            {
                Object.Destroy(red);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RetainedSourceLease_SkipsSecondIngest_AndReferenceSwapInvalidatesIt()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D first = Solid(Color.red);
            Texture2D replacement = Solid(Color.blue);
            TextureSourceLease lease = null;
            try
            {
                var executor = new TextureExecutor(host);
                Assert.That(executor.TryExecute(CreateStub(first), host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true), out TextureExecutionHandle firstHandle, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                yield return Wait(firstHandle);
                Assert.That(firstHandle.Succeeded, Is.True, firstHandle.Diagnostic?.message);
                Assert.That(firstHandle.Result.TryTakeDelivery(out TextureDelivery firstDelivery), Is.True);
                firstDelivery.Dispose();
                Assert.That(firstHandle.Result.TryTakeSourceLease(out lease), Is.True);
                Assert.That(lease.IsValid, Is.True);
                Assert.That(host.IngestDispatchCount, Is.EqualTo(1));

                Assert.That(executor.TryExecute(CreateStub(first), host.CreateOrigin(), new TextureExecutionOptions(lease), out TextureExecutionHandle reusedHandle, out StackMachineDiagnostic reusedDiagnostic), Is.True, reusedDiagnostic?.message);
                yield return Wait(reusedHandle);
                Assert.That(reusedHandle.Succeeded, Is.True, reusedHandle.Diagnostic?.message);
                Assert.That(reusedHandle.Result.TryTakeDelivery(out TextureDelivery reusedDelivery), Is.True);
                reusedDelivery.Dispose();
                Assert.That(host.IngestDispatchCount, Is.EqualTo(1), "A valid retained source lease must not issue KIngest again.");

                Assert.That(executor.TryExecute(CreateStub(replacement), host.CreateOrigin(), new TextureExecutionOptions(lease), out TextureExecutionHandle replacedHandle, out StackMachineDiagnostic replacedDiagnostic), Is.True, replacedDiagnostic?.message);
                yield return Wait(replacedHandle);
                Assert.That(replacedHandle.Succeeded, Is.True, replacedHandle.Diagnostic?.message);
                Assert.That(replacedHandle.Result.TryTakeDelivery(out TextureDelivery replacedDelivery), Is.True);
                replacedDelivery.Dispose();
                Assert.That(lease.IsValid, Is.False, "Changing the Texture object must invalidate the stale lease.");
                Assert.That(host.IngestDispatchCount, Is.EqualTo(2));
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(0));
            }
            finally
            {
                lease?.Dispose();
                Object.Destroy(first);
                Object.Destroy(replacement);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RetainedOutputLease_BindsNextRecipeOutput_AndPublishesCumulativeResult()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            Texture2D blue = Solid(Color.blue);
            TextureOutputLease outputLease = null;
            try
            {
                var executor = new TextureExecutor(host);
                Assert.That(executor.TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(retainOutputLease: true), out TextureExecutionHandle retainedHandle, out StackMachineDiagnostic retainedDiagnostic), Is.True, retainedDiagnostic?.message);
                yield return Wait(retainedHandle);
                Assert.That(retainedHandle.Succeeded, Is.True, retainedHandle.Diagnostic?.message);
                Assert.That(retainedHandle.Result.TryTakeDelivery(out TextureDelivery noDelivery), Is.False);
                Assert.That(noDelivery, Is.Null);
                Assert.That(retainedHandle.Result.TryTakeOutputLease(out outputLease), Is.True);
                Assert.That(host.OutstandingOutputLeaseCount, Is.EqualTo(1));

                Assert.That(executor.TryExecute(CreateAddToOutputStub(blue), host.CreateOrigin(), new TextureExecutionOptions(outputLease: outputLease), out TextureExecutionHandle publishedHandle, out StackMachineDiagnostic publishedDiagnostic), Is.True, publishedDiagnostic?.message);
                yield return Wait(publishedHandle);
                Assert.That(publishedHandle.Succeeded, Is.True, publishedHandle.Diagnostic?.message);
                Assert.That(publishedHandle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                using (delivery) yield return AssertPixel(delivery.Texture, new Vector4(1f, 0f, 1f, 2f));
                Assert.That(outputLease.IsValid, Is.True);
                Assert.That(outputLease.TryDispose(out StackMachineDiagnostic releaseDiagnostic), Is.True, releaseDiagnostic?.message);
                Assert.That(host.OutstandingOutputLeaseCount, Is.EqualTo(0));
                Assert.That(outputLease.TryDispose(out StackMachineDiagnostic doubleRelease), Is.False);
                Assert.That(doubleRelease.domainCode, Is.EqualTo("OutputLeaseAlreadyReleased"));
            }
            finally
            {
                outputLease?.Dispose();
                Object.Destroy(red);
                Object.Destroy(blue);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator OutputLease_RejectsAnotherHostAndMismatchedExtent()
        {
#if UNITY_EDITOR
            GameObject firstRoot = CreateHost(out TextureStackMachineHost firstHost);
            GameObject secondRoot = CreateHost(out TextureStackMachineHost secondHost);
            Texture2D red = Solid(Color.red);
            TextureOutputLease outputLease = null;
            TextureSourceLease sourceLease = null;
            try
            {
                var firstExecutor = new TextureExecutor(firstHost);
                Assert.That(firstExecutor.TryExecute(CreateCopyStub(red), firstHost.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true, retainOutputLease: true), out TextureExecutionHandle retainedHandle, out StackMachineDiagnostic retainedDiagnostic), Is.True, retainedDiagnostic?.message);
                yield return Wait(retainedHandle);
                Assert.That(retainedHandle.Result.TryTakeSourceLease(out sourceLease), Is.True);
                Assert.That(retainedHandle.Result.TryTakeOutputLease(out outputLease), Is.True);

                Assert.That(new TextureExecutor(secondHost).TryExecute(CreateCopyStub(red), secondHost.CreateOrigin(), new TextureExecutionOptions(sourceLease: sourceLease), out _, out StackMachineDiagnostic foreignSourceHost), Is.False);
                Assert.That(foreignSourceHost.domainCode, Is.EqualTo("SourceLeaseHostMismatch"));
                Assert.That(new TextureExecutor(secondHost).TryExecute(CreateCopyStub(red), secondHost.CreateOrigin(), new TextureExecutionOptions(outputLease: outputLease), out _, out StackMachineDiagnostic foreignHost), Is.False);
                Assert.That(foreignHost.domainCode, Is.EqualTo("OutputLeaseHostMismatch"));
                Assert.That(firstExecutor.TryExecute(CreateOutputOnlyStub(256), firstHost.CreateOrigin(), new TextureExecutionOptions(outputLease: outputLease), out _, out StackMachineDiagnostic extentMismatch), Is.False);
                Assert.That(extentMismatch.domainCode, Is.EqualTo("OutputLeaseExtentMismatch"));
            }
            finally
            {
                outputLease?.Dispose();
                sourceLease?.Dispose();
                Object.Destroy(red);
                Object.Destroy(firstRoot);
                Object.Destroy(secondRoot);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CancelledRequest_KeepsCallerOwnedSourceLease()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            TextureSourceLease sourceLease = null;
            try
            {
                var executor = new TextureExecutor(host);
                Assert.That(executor.TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true), out TextureExecutionHandle initial, out StackMachineDiagnostic initialDiagnostic), Is.True, initialDiagnostic?.message);
                yield return Wait(initial);
                Assert.That(initial.Result.TryTakeDelivery(out TextureDelivery initialDelivery), Is.True);
                initialDelivery.Dispose();
                Assert.That(initial.Result.TryTakeSourceLease(out sourceLease), Is.True);

                Assert.That(executor.TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(sourceLease: sourceLease), out TextureExecutionHandle cancelled, out StackMachineDiagnostic cancelledDiagnostic), Is.True, cancelledDiagnostic?.message);
                cancelled.Dispose();
                Assert.That(cancelled.IsCompleted, Is.True);
                Assert.That(cancelled.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                Assert.That(sourceLease.IsValid, Is.True);
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
                Assert.That(sourceLease.TryDispose(out StackMachineDiagnostic releaseDiagnostic), Is.True, releaseDiagnostic?.message);
                Assert.That(sourceLease.TryDispose(out StackMachineDiagnostic doubleRelease), Is.False);
                Assert.That(doubleRelease.domainCode, Is.EqualTo("SourceLeaseAlreadyReleased"));
            }
            finally
            {
                sourceLease?.Dispose();
                Object.Destroy(red);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SubmittedCancellation_KeepsCallerOwnedSourceLeaseUntilFenceCleanup()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            TextureSourceLease sourceLease = null;
            try
            {
                var executor = new TextureExecutor(host);
                Assert.That(executor.TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true), out TextureExecutionHandle initial, out StackMachineDiagnostic initialDiagnostic), Is.True, initialDiagnostic?.message);
                yield return Wait(initial);
                Assert.That(initial.Result.TryTakeDelivery(out TextureDelivery initialDelivery), Is.True);
                initialDelivery.Dispose();
                Assert.That(initial.Result.TryTakeSourceLease(out sourceLease), Is.True);

                Assert.That(executor.TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(sourceLease: sourceLease), out TextureExecutionHandle submitted, out StackMachineDiagnostic submittedDiagnostic), Is.True, submittedDiagnostic?.message);
                yield return null;
                Assert.That(host.HasSubmittedRequest, Is.True);
                submitted.Dispose();
                Assert.That(submitted.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
                while (host.HasSubmittedRequest) yield return null;
                Assert.That(sourceLease.IsValid, Is.True);
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
            }
            finally
            {
                sourceLease?.Dispose();
                Object.Destroy(red);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RetainedLeaseConflict_ReportsLeaseCountsInHallReservationDiagnostic()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            TextureSourceLease sourceLease = null;
            try
            {
                var executor = new TextureExecutor(host);
                Assert.That(executor.TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true), out TextureExecutionHandle initial, out StackMachineDiagnostic initialDiagnostic), Is.True, initialDiagnostic?.message);
                yield return Wait(initial);
                Assert.That(initial.Result.TryTakeDelivery(out TextureDelivery initialDelivery), Is.True);
                initialDelivery.Dispose();
                Assert.That(initial.Result.TryTakeSourceLease(out sourceLease), Is.True);

                Assert.That(executor.TryExecute(CreateOutputOnlyStub(host.Capability.FixedGridEdge), host.CreateOrigin(), out _, out StackMachineDiagnostic conflict), Is.False);
                Assert.That(conflict.domainCode, Is.EqualTo("HallReservationFailed"));
                Assert.That(conflict.detail, Does.Contain("retainedSourceLeases=1"));
            }
            finally
            {
                sourceLease?.Dispose();
                Object.Destroy(red);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator UnclaimedResultDispose_ReleasesNewSourceAndOutputLeases()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            try
            {
                Assert.That(new TextureExecutor(host).TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true, retainOutputLease: true), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                yield return Wait(handle);
                Assert.That(host.OutstandingSourceLeaseCount, Is.EqualTo(1));
                Assert.That(host.OutstandingOutputLeaseCount, Is.EqualTo(1));
                handle.Dispose();
                Assert.That(host.OutstandingSourceLeaseCount, Is.Zero);
                Assert.That(host.OutstandingOutputLeaseCount, Is.Zero);
            }
            finally
            {
                Object.Destroy(red);
                Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator NormalBlender_RetainsSourceLeaseAndSkipsWeightOnlyReingest()
        {
#if UNITY_EDITOR
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene isolatedScene = SceneManager.CreateScene("NormalBlenderLeaseIsolation");
            Assert.That(SceneManager.SetActiveScene(isolatedScene), Is.True);
            GameObject hostRoot = CreateHost(out TextureStackMachineHost host);
            GameObject figure = new GameObject("NormalBlenderLeaseIntegration");
            Texture2D normal = Solid(new Color(0.5f, 0.5f, 1f, 1f));
            MeshBindingTemplate template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                Assert.That(material.shader, Is.Not.Null);
                SetPrivate(template, "normalTargetTextures", new List<NormalTargetTextureEntry> { new NormalTargetTextureEntry { targetName = string.Empty, textures = new List<NormalTextureEntry> { new NormalTextureEntry { entryName = "body", texture = normal } } } });
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                MaterialProxy proxy = figure.AddComponent<MaterialProxy>();
                SetPrivate(proxy, "entries", new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter } });
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                SetPrivate(machine, "bindingTemplate", template);
                Dictionary<string, NormalRecipeTemplate> templates = (Dictionary<string, NormalRecipeTemplate>)GetPrivate(machine, "normalTemplates");
                templates.Add("body", new NormalRecipeTemplate("body", "$base CANVAS NORMAL_BASE NORMAL_FINALIZE"));
                NormalBlender blender = figure.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "body" });
                Assert.That(blender.TryRetry(out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                yield return WaitForNormalLease(blender);
                Assert.That(host.IngestDispatchCount, Is.EqualTo(1));

                Assert.That(blender.TryRetry(out StackMachineDiagnostic retryDiagnostic), Is.True, retryDiagnostic?.message);
                yield return WaitForNormalIdle(blender);
                Assert.That(host.IngestDispatchCount, Is.EqualTo(1), "A weight-only retry must reuse NormalBlender's retained source hall.");
            }
            finally
            {
                Object.Destroy(figure);
                Object.Destroy(hostRoot);
                Object.Destroy(normal);
                Object.Destroy(template);
                Object.Destroy(adapter);
                Object.Destroy(material);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded) SceneManager.SetActiveScene(previousActiveScene);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator NormalBlender_HostDestroyReleasesRetainedLeaseBeforeForcedCleanup()
        {
#if UNITY_EDITOR
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene isolatedScene = SceneManager.CreateScene("NormalBlenderLeaseHostLifecycleIsolation");
            Assert.That(SceneManager.SetActiveScene(isolatedScene), Is.True);
            GameObject hostRoot = CreateHost(out TextureStackMachineHost host);
            GameObject figure = new GameObject("NormalBlenderLeaseHostLifecycle");
            Texture2D normal = Solid(new Color(0.5f, 0.5f, 1f, 1f));
            MeshBindingTemplate template = ScriptableObject.CreateInstance<MeshBindingTemplate>();
            MaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            try
            {
                SetPrivate(template, "normalTargetTextures", new List<NormalTargetTextureEntry> { new NormalTargetTextureEntry { targetName = string.Empty, textures = new List<NormalTextureEntry> { new NormalTextureEntry { entryName = "body", texture = normal } } } });
                SkinnedMeshRenderer renderer = figure.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMaterial = material;
                MaterialProxy proxy = figure.AddComponent<MaterialProxy>();
                SetPrivate(proxy, "entries", new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter } });
                MeshStackMachine machine = figure.AddComponent<MeshStackMachine>();
                SetPrivate(machine, "bindingTemplate", template);
                ((Dictionary<string, NormalRecipeTemplate>)GetPrivate(machine, "normalTemplates")).Add("body", new NormalRecipeTemplate("body", "$base CANVAS NORMAL_BASE NORMAL_FINALIZE"));
                NormalBlender blender = figure.AddComponent<NormalBlender>();
                SetPrivate(blender, "entries", new List<string> { "body" });

                Assert.That(blender.TryRetry(out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                yield return WaitForNormalLease(blender);
                Assert.That(GetPrivate(blender, "retainedLeaseHost"), Is.SameAs(host));

                Object.Destroy(hostRoot);
                hostRoot = null;
                yield return null;

                Assert.That(((System.Collections.IDictionary)GetPrivate(blender, "states")).Count, Is.Zero);
            }
            finally
            {
                Object.Destroy(figure);
                Object.Destroy(normal);
                Object.Destroy(template);
                Object.Destroy(adapter);
                Object.Destroy(material);
                if (hostRoot != null) Object.Destroy(hostRoot);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded) SceneManager.SetActiveScene(previousActiveScene);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator HostDestroy_InvalidatesUnreleasedLeasesAndReportsForcedRelease()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D red = Solid(Color.red);
            TextureSourceLease sourceLease = null;
            TextureOutputLease outputLease = null;
            try
            {
                Assert.That(new TextureExecutor(host).TryExecute(CreateCopyStub(red), host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true, retainOutputLease: true), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                yield return Wait(handle);
                Assert.That(handle.Result.TryTakeSourceLease(out sourceLease), Is.True);
                Assert.That(handle.Result.TryTakeOutputLease(out outputLease), Is.True);
                LogAssert.Expect(LogType.Warning, new Regex("LeaseForcedReleaseOnHostDestroy"));
                Object.Destroy(root);
                root = null;
                yield return null;
                Assert.That(sourceLease.IsValid, Is.False);
                Assert.That(outputLease.IsValid, Is.False);
            }
            finally
            {
                sourceLease?.Dispose();
                outputLease?.Dispose();
                Object.Destroy(red);
                if (root != null) Object.Destroy(root);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static GameObject CreateHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
            var root = new GameObject("TextureSourceLeasePlayModeTests");
            host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            Assert.That(host.TryAssignNormalComputeProgram(normalCompute, out StackMachineDiagnostic normalAssignment), Is.True, normalAssignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) { Object.Destroy(root); Assert.Ignore(initialize?.message); }
            return root;
        }

        private static TextureRecipeStub CreateStub(Texture source)
        {
            var document = new MaterialRecipeDocument { wordSource = "128 128 RECTSIZE $out 0 0 0 1 FILL_OUT $source 0 0 128 128 0 0 128 128 PLACE", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string name in new[] { "source", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
        }

        private static TextureRecipeStub CreateCopyStub(Texture source)
        {
            return CreateDocumentStub("$source $out COPY DROP", source);
        }

        private static TextureRecipeStub CreateAddToOutputStub(Texture source)
        {
            return CreateDocumentStub("$out $source ADD $out COPY DROP", source);
        }

        private static TextureRecipeStub CreateDocumentStub(string words, Texture source)
        {
            var document = new MaterialRecipeDocument { wordSource = words, outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string name in new[] { "source", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
        }

        private static TextureRecipeStub CreateOutputOnlyStub(int extent)
        {
            var document = new MaterialRecipeDocument { wordSource = "$out 0 0 0 1 FILL_OUT", outputLogicalName = "out", outputWidth = extent, outputHeight = extent };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            return new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[128 * 128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static IEnumerator Wait(TextureExecutionHandle handle)
        {
            while (!handle.IsCompleted) yield return null;
        }

        private static IEnumerator WaitForNormalLease(NormalBlender blender)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                object state = GetNormalState(blender);
                if (GetStateField(state, "sourceLease") is TextureSourceLease lease && lease.IsValid && GetStateField(state, "inFlight") == null) yield break;
                yield return null;
            }
            blender.TryGetEntryDiagnostic("body", out StackMachineDiagnostic diagnostic);
            Assert.Fail("NormalBlender did not complete its first retained-source execution. " + diagnostic?.domainCode + ": " + diagnostic?.message);
        }

        private static IEnumerator WaitForNormalIdle(NormalBlender blender)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                object state = GetNormalState(blender);
                if (!(bool)GetStateField(state, "pending") && GetStateField(state, "inFlight") == null) yield break;
                yield return null;
            }
            Assert.Fail("NormalBlender did not complete its retried execution.");
        }

        private static object GetNormalState(NormalBlender blender) => ((System.Collections.IDictionary)GetPrivate(blender, "states"))["body"];
        private static object GetPrivate(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static object GetStateField(object state, string name) => state.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(state);
        private static void SetPrivate(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static IEnumerator AssertPixel(Texture texture, Vector4 expected)
        {
            bool done = false;
            UnityEngine.Rendering.AsyncGPUReadbackRequest request = default;
            UnityEngine.Rendering.AsyncGPUReadback.Request(texture, 0, value => { request = value; done = true; });
            while (!done) yield return null;
            Assert.That(request.hasError, Is.False);
            Unity.Collections.NativeArray<ushort> data = request.GetData<ushort>();
            Assert.That(Half(data[0]), Is.EqualTo(expected.x).Within(0.003f));
            Assert.That(Half(data[1]), Is.EqualTo(expected.y).Within(0.003f));
            Assert.That(Half(data[2]), Is.EqualTo(expected.z).Within(0.003f));
            Assert.That(Half(data[3]), Is.EqualTo(expected.w).Within(0.003f));
        }

        private static float Half(ushort h) { int s = (h >> 15) & 1, e = (h >> 10) & 31, f = h & 1023; if (e == 0) return (s == 0 ? 1 : -1) * f / 16777216f; return (s == 0 ? 1 : -1) * (1f + f / 1024f) * Mathf.Pow(2, e - 15); }
#endif
    }
}
