// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode
{
    /// <summary>GPU-backed PlayMode coverage for Spec16 mask-only Material canvas binding.</summary>
    public sealed class MaterialCurrentTextureRuntimeTests
    {
        [UnityTest]
        public IEnumerator MaskOnlyCurrentTexture_ExecutesGpuDispatchAndCommitsMaskedCanvas()
        {
#if UNITY_EDITOR
            GameObject hostRoot = CreateHost(out _);
            GameObject target = new GameObject("MaterialCurrentTextureGpuTests");
            Material source = null;
            MaterialShaderAdapter adapter = null;
            MaterialBinding binding = null;
            Texture2D currentTexture = null;
            Texture2D firstMaskTexture = null;
            Texture2D secondMaskTexture = null;
            try
            {
                MaterialAttacher attacher = ConfigureUnlitTarget(target, out SkinnedMeshRenderer renderer, out source, out adapter);
                currentTexture = Solid(new Color(0.8f, 0.2f, 0.1f, 1f));
                firstMaskTexture = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
                secondMaskTexture = Solid(new Color(0.5f, 0.5f, 0.5f, 1f));
                source.SetTexture("_BaseMap", currentTexture);

                binding = ScriptableObject.CreateInstance<MaterialBinding>();
                typeof(MaterialBinding).GetField("textures", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MaterialTextureBindingEntry>
                {
                    new MaterialTextureBindingEntry { logicalName = "maskA", sourceTexture = firstMaskTexture },
                    new MaterialTextureBindingEntry { logicalName = "maskB", sourceTexture = secondMaskTexture }
                });

                MaterialStackMachine machine = target.AddComponent<MaterialStackMachine>();
                machine.MaterialAttacher = attacher;
                var payload = new ShapeSyncDocument
                {
                    MaterialBinding = binding,
                    MaterialRecipe = new MaterialRecipeDocument
                    {
                        wordSource = "$body MATERIAL TEXTURE $current CANVAS $maskA $maskB MULTIPLY ALPHA . ENDTEXTURE"
                    }
                };

                Assert.That(machine.TryAcceptRecipePayload(payload, out MaterialStackMachineOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation, Is.Not.Null);
                while (!operation.IsCompleted) yield return null;

                Assert.That(operation.Result, Is.Not.Null);
                Assert.That(operation.Result.Code, Is.EqualTo(MaterialStackMachineResultCode.Applied), operation.Result.Diagnostic?.message);
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(source), "GPU delivery must commit through the Material Attacher after the Texture transaction completes.");
                RenderTexture delivered = renderer.sharedMaterial.GetTexture("_BaseMap") as RenderTexture;
                Assert.That(delivered, Is.Not.Null);
                yield return AssertReadbackPixel(delivered, new Vector4(0.8f, 0.2f, 0.1f, 0.25f), 0.01f);
                Assert.That(source.GetTexture("_BaseMap"), Is.SameAs(currentTexture), "The Proxy current-texture read path must not mutate the source Material.");
            }
            finally
            {
                if (binding != null) Object.Destroy(binding);
                if (secondMaskTexture != null) Object.Destroy(secondMaskTexture);
                if (firstMaskTexture != null) Object.Destroy(firstMaskTexture);
                if (currentTexture != null) Object.Destroy(currentTexture);
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

#if UNITY_EDITOR
        private static GameObject CreateHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            var root = new GameObject("MaterialCurrentTextureGpuHost");
            host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize))
            {
                Object.Destroy(root);
                Assert.Ignore(initialize?.message);
            }
            return root;
        }

        private static MaterialAttacher ConfigureUnlitTarget(GameObject target, out SkinnedMeshRenderer renderer, out Material source, out MaterialShaderAdapter adapter)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null, "The project must provide the Phase0 URP Unlit shader.");
            renderer = target.AddComponent<SkinnedMeshRenderer>();
            source = new Material(shader);
            renderer.sharedMaterial = source;
            MaterialProxy proxy = target.AddComponent<MaterialProxy>();
            MaterialAttacher attacher = target.AddComponent<MaterialAttacher>();
            adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, new List<MaterialProxyEntry>
            {
                new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter }
            });
            attacher.Proxy = proxy;
            return attacher;
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

        private static IEnumerator AssertReadbackPixel(RenderTexture texture, Vector4 expected, float tolerance)
        {
            Assert.That(texture, Is.Not.Null);
            bool done = false;
            AsyncGPUReadbackRequest request = default;
            AsyncGPUReadback.Request(texture, 0, value => { request = value; done = true; });
            while (!done) yield return null;
            Assert.That(request.hasError, Is.False);
            NativeArray<ushort> data = request.GetData<ushort>();
            Assert.That(Half(data[0]), Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(Half(data[1]), Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(Half(data[2]), Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(Half(data[3]), Is.EqualTo(expected.w).Within(tolerance));
        }

        private static float Half(ushort value)
        {
            int sign = (value >> 15) & 1;
            int exponent = (value >> 10) & 31;
            int fraction = value & 1023;
            if (exponent == 0) return (sign == 0 ? 1f : -1f) * fraction / 16777216f;
            return (sign == 0 ? 1f : -1f) * (1f + fraction / 1024f) * Mathf.Pow(2f, exponent - 15);
        }
#endif
    }
}
