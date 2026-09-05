// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class TextureRecordDispatchPlayModeTests
    {
        [UnityTest]
        public IEnumerator Host_DispatchesDirectRecordsAtNonzeroHallOrigins()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            Texture2D seed = Solid(new Color(0.2f, 0.7f, 0.4f, 1f));
            var source = new RenderTexture(256, 128, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
            TextureHallAllocation blocker = default;
            try
            {
                Assert.That(source.Create(), Is.True);
                Graphics.Blit(seed, source);
                Assert.That(host.TryReserveHall(256, 256, out blocker, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);
                var document = new MaterialRecipeDocument { wordSource = "256 128 RECTSIZE $out 0.02 0.04 0.06 1 FILL_OUT $source 16 20 32 16 160 72 32 16 PLACE", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
                foreach (string name in new[] { "source", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
                var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "source", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
                Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                while (!handle.IsCompleted) yield return null;
                Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
                Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
                using (delivery)
                {
                    yield return AssertPixel(delivery.Texture, 256, 0, 0, new Vector4(0.02f, 0.04f, 0.06f, 1f));
                    yield return AssertPixel(delivery.Texture, 256, 160, 72, new Vector4(0.2f, 0.7f, 0.4f, 1f));
                    yield return AssertPixel(delivery.Texture, 256, 159, 72, new Vector4(0.02f, 0.04f, 0.06f, 1f));
                }
            }
            finally { if (blocker.IsValid) Assert.That(host.TryReleaseHall(blocker), Is.True); Object.Destroy(seed); source.Release(); Object.Destroy(source); Object.Destroy(root); }
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

#if UNITY_EDITOR
        private static GameObject CreateHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            var root = new GameObject("TextureRecordDispatchPlayModeTests"); host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) { Object.Destroy(root); Assert.Ignore(initialize?.message); }
            return root;
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(256, 128, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[256 * 128]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels); texture.Apply(false, false); return texture;
        }

        private static IEnumerator AssertPixel(Texture texture, int width, int x, int y, Vector4 expected)
        {
            bool done = false; AsyncGPUReadbackRequest request = default;
            AsyncGPUReadback.Request(texture, 0, value => { request = value; done = true; });
            while (!done) yield return null;
            Assert.That(request.hasError, Is.False);
            NativeArray<ushort> data = request.GetData<ushort>(); int start = (y * width + x) * 4;
            Assert.That(Half(data[start]), Is.EqualTo(expected.x).Within(0.003f)); Assert.That(Half(data[start + 1]), Is.EqualTo(expected.y).Within(0.003f));
            Assert.That(Half(data[start + 2]), Is.EqualTo(expected.z).Within(0.003f)); Assert.That(Half(data[start + 3]), Is.EqualTo(expected.w).Within(0.003f));
        }

        private static float Half(ushort h) { int s = (h >> 15) & 1, e = (h >> 10) & 31, f = h & 1023; if (e == 0) return (s == 0 ? 1 : -1) * f / 16777216f; return (s == 0 ? 1 : -1) * (1f + f / 1024f) * Mathf.Pow(2, e - 15); }
#endif
    }
}
