// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class MaterialAttacherRuntimeTests
    {
        [UnityTest]
        public IEnumerator GpuDeliveries_AttachByReferenceAndReleaseTheReplacedDelivery()
        {
#if UNITY_EDITOR
            GameObject hostRoot = CreateHost(out TextureStackMachineHost host);
            GameObject target = new GameObject("MaterialAttacherGpuDeliveryTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out _, out source, out adapter);
                TextureExecutionHandle first = StartFill(host, Color.red);
                while (!first.IsCompleted) yield return null;
                Assert.That(first.Succeeded, Is.True, first.Diagnostic?.message);
                Assert.That(first.Result.TryTakeDelivery(out TextureDelivery firstDelivery), Is.True);
                Texture firstTexture = firstDelivery.Texture;
                var firstValues = new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = firstTexture };
                Assert.That(attacher.TryApply("Body", firstValues, firstDelivery, out MaterialAttacherResult firstApply), Is.True, firstApply.diagnostic.message);
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(firstTexture));

                TextureExecutionHandle second = StartFill(host, Color.blue);
                while (!second.IsCompleted) yield return null;
                Assert.That(second.Succeeded, Is.True, second.Diagnostic?.message);
                Assert.That(second.Result.TryTakeDelivery(out TextureDelivery secondDelivery), Is.True);
                Texture secondTexture = secondDelivery.Texture;
                var secondValues = new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = secondTexture };
                Assert.That(attacher.TryApply("Body", secondValues, secondDelivery, out MaterialAttacherResult secondApply), Is.True, secondApply.diagnostic.message);
                Assert.That(firstDelivery.Texture, Is.Null, "Replacing an applied delivery must release the old owner slot exactly once.");
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(secondTexture));

                Material clone = renderer.sharedMaterial;
                Assert.That(attacher.TryDryRunReset("Body", out MaterialAttacherResetDryRunPlan resetPlan, out MaterialProxyDiagnostic resetDiagnostic), Is.True, resetDiagnostic.message);
                Assert.That(attacher.TryCommitReset(resetPlan, out MaterialAttacherResult resetResult), Is.True, resetResult.diagnostic.message);
                Assert.That(renderer.sharedMaterial, Is.SameAs(clone), "MATERIAL_RESET must preserve the entry-local runtime clone.");
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(source.GetTexture("_BaseMap")), "MATERIAL_RESET must copy source Material properties.");
                Assert.That(secondDelivery.Texture, Is.Null, "MATERIAL_RESET must release the delivery now owned by the Proxy.");
            }
            finally
            {
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
                Object.Destroy(hostRoot);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CancelledOrCoalescedExecutionDoesNotChangeAnAlreadyAttachedMaterial()
        {
#if UNITY_EDITOR
            GameObject hostRoot = CreateHost(out TextureStackMachineHost host);
            GameObject target = new GameObject("MaterialAttacherStaleBoundaryTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out _, out source, out adapter);
                TextureExecutionHandle committed = StartFill(host, Color.red);
                while (!committed.IsCompleted) yield return null;
                Assert.That(committed.Result.TryTakeDelivery(out TextureDelivery committedDelivery), Is.True);
                Texture committedTexture = committedDelivery.Texture;
                var values = new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = committedTexture };
                Assert.That(attacher.TryApply("Body", values, committedDelivery, out MaterialAttacherResult apply), Is.True, apply.diagnostic.message);

                TextureExecutionHandle cancelled = StartFill(host, Color.green);
                cancelled.Dispose();
                Assert.That(cancelled.IsCompleted, Is.True);
                Assert.That(cancelled.Succeeded, Is.False);
                Assert.That(cancelled.Result, Is.Null);

                TextureExecutionOriginKey coalescingOrigin = host.CreateOrigin();
                TextureExecutionHandle coalesced = StartFill(host, Color.blue, coalescingOrigin);
                TextureExecutionHandle replacement = StartFill(host, Color.white, coalescingOrigin);
                Assert.That(coalesced.IsCompleted, Is.True, "Coalescing token was not reused; pending request count=" + host.PendingRequestCount + ", origin value=" + coalescingOrigin.Value);
                Assert.That(coalesced.Succeeded, Is.False);
                while (!replacement.IsCompleted) yield return null;
                if (replacement.Result != null) replacement.Result.Dispose();
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(committedTexture));
            }
            finally
            {
                if (adapter != null) Object.Destroy(adapter);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
                Object.Destroy(hostRoot);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator MaterialStackMachine_MaterialResetRelaysTheExistingEntryTransaction()
        {
#if UNITY_EDITOR
            GameObject hostRoot = CreateHost(out TextureStackMachineHost host);
            GameObject target = new GameObject("MaterialStackMachineResetTests");
            Material source = null;
            Material secondarySource = null;
            MaterialShaderAdapter adapter = null;
            GameObject secondary = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out MaterialProxy proxy, out source, out adapter);
                secondary = new GameObject("MaterialStackMachineResetSecondEntry");
                SkinnedMeshRenderer secondaryRenderer = secondary.AddComponent<SkinnedMeshRenderer>();
                secondarySource = new Material(source);
                secondaryRenderer.sharedMaterial = secondarySource;
                AddEntry(proxy, new MaterialProxyEntry { entryName = "Face", renderer = secondaryRenderer, materialChannel = 0, adapter = adapter });
                TextureExecutionHandle deliveryHandle = StartFill(host, Color.red);
                while (!deliveryHandle.IsCompleted) yield return null;
                Assert.That(deliveryHandle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                Assert.That(attacher.TryApply("Body", new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = delivery.Texture }, delivery, out MaterialAttacherResult apply), Is.True, apply.diagnostic.message);
                Assert.That(attacher.TryApply("Face", new MaterialProxySemanticValues { applyColor = true, color = Color.green }, null, out MaterialAttacherResult faceApply), Is.True, faceApply.diagnostic.message);

                Material clone = renderer.sharedMaterial;
                Material faceClone = secondaryRenderer.sharedMaterial;
                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                Assert.That(machine.TryExecute("MATERIAL_RESET", out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!operation.IsCompleted) yield return null;

                Assert.That(operation.Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied));
                Assert.That(renderer.sharedMaterial, Is.SameAs(clone));
                Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(source.GetTexture("_BaseMap")));
                Assert.That(secondaryRenderer.sharedMaterial, Is.SameAs(faceClone), "Target-wide MATERIAL_RESET must preserve every entry clone.");
                Assert.That(secondaryRenderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(secondarySource.GetColor("_BaseColor")), "Target-wide MATERIAL_RESET must reset every Proxy entry, not only the previously addressed entry.");
                Assert.That(delivery.Texture, Is.Null);
            }
            finally
            {
                if (adapter != null) Object.Destroy(adapter);
                if (secondarySource != null) Object.Destroy(secondarySource);
                if (source != null) Object.Destroy(source);
                Object.Destroy(target);
                Object.Destroy(secondary);
                Object.Destroy(hostRoot);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static MaterialAttacher ConfigureUnlitTarget(GameObject target, out SkinnedMeshRenderer renderer, out MaterialProxy proxy, out Material source, out MaterialShaderAdapter adapter)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            renderer = target.AddComponent<SkinnedMeshRenderer>();
            source = new Material(shader);
            renderer.sharedMaterial = source;
            proxy = target.AddComponent<MaterialProxy>();
            var attacher = target.AddComponent<MaterialAttacher>();
            adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            SetEntries(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "Body", renderer = renderer, materialChannel = 0, adapter = adapter } });
            attacher.Proxy = proxy;
            return attacher;
        }

        private static TextureExecutionHandle StartFill(TextureStackMachineHost host, Color color)
            => StartFill(host, color, host.CreateOrigin());

        private static TextureExecutionHandle StartFill(TextureStackMachineHost host, Color color, TextureExecutionOriginKey origin)
        {
            var document = new MaterialRecipeDocument { wordSource = color.r + " " + color.g + " " + color.b + " " + color.a + " FILL $out COPY DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
            Assert.That(new TextureExecutor(host).TryExecute(stub, origin, out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return handle;
        }

        private static GameObject CreateHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            var root = new GameObject("MaterialAttacherGpuHost");
            host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) { Object.Destroy(root); Assert.Ignore(initialize?.message); }
            return root;
        }

        private static void SetEntries(MaterialProxy proxy, List<MaterialProxyEntry> entries)
        {
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, entries);
        }

        private static void AddEntry(MaterialProxy proxy, MaterialProxyEntry entry)
        {
            var entries = (List<MaterialProxyEntry>)typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(proxy);
            entries.Add(entry);
        }
#endif
    }
}
