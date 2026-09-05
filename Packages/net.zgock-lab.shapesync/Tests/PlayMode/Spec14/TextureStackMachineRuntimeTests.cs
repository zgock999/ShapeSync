// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using Unity.Collections;
using zgock.ShapeSync.StackMachine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class TextureStackMachineRuntimeTests
    {
        [UnityTest]
        public IEnumerator Fill_CompletesAndTransfersOneExactEdgeDelivery()
        {
#if UNITY_EDITOR
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            var root = new GameObject("TextureStackMachineRuntimeTests");
            var host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) { Object.Destroy(root); Assert.Ignore(initialize?.message); }
            var document = new MaterialRecipeDocument { wordSource = "1 0 0 1 FILL $out COPY DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var executor = new TextureExecutor(host);
            Assert.That(executor.TryExecute(new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } }), host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            Assert.That(delivery.Texture.width, Is.EqualTo(128));
            Assert.That(handle.Result.TryTakeDelivery(out _), Is.False);
            delivery.Dispose();
            Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator TryExecute_PropagatesHostHallReservationDiagnostic()
        {
#if UNITY_EDITOR
            GameObject root = CreateHost(out TextureStackMachineHost host);
            try
            {
                int edge = host.Capability.FixedGridEdge;
                Assert.That(host.TryReserveHall(edge, edge, out TextureHallAllocation reservation, out StackMachineDiagnostic reserveDiagnostic), Is.True, reserveDiagnostic?.message);
                try
                {
                    Assert.That(new TextureExecutor(host).TryExecute(FillStub(Color.red), host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(handle, Is.Null);
                    Assert.That(diagnostic, Is.Not.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HallReservationFailed"));
                    Assert.That(host.PendingRequestCount, Is.Zero, "A rejected enqueue must not retain a pending request.");
                }
                finally { Assert.That(host.TryReleaseHall(reservation), Is.True); }
            }
            finally { Object.Destroy(root); }
            yield break;
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Fill_WritesExpectedGpuPixel()
        {
#if UNITY_EDITOR
            yield return AssertGpuPixel("0.125 0.25 0.75 0.5 FILL .", new[] { Output() }, new Vector4(0.125f, 0.25f, 0.75f, 0.5f));
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator IngestAndPublish_TransferExactSourceTexelThroughGpu()
        {
#if UNITY_EDITOR
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            Vector4 expected = new Vector4(0.125f, 0.375f, 0.625f, 0.875f);
            source.SetPixel(63, 79, expected);
            source.Apply(false, false);
            var root = CreateHost(out TextureStackMachineHost host);
            var document = Document("$source .", 128, "source", "out");
            var stub = new TextureRecipeStub(document, new[] { Source("source", source), Output() });
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            yield return AssertReadbackPixel(delivery.Texture, 128, 63, 79, expected, 0.002f);
            delivery.Dispose(); Object.Destroy(source); Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator NativeRectangleCanvas_DeliversRenderTexture()
        {
#if UNITY_EDITOR
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            var root = new GameObject("TextureStackMachineNonSquareTests");
            var host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) { Object.Destroy(root); Assert.Ignore(initialize?.message); }
            var source = new Texture2D(128, 256, TextureFormat.RGBAHalf, false, true);
            source.SetPixel(127, 255, Color.green);
            source.Apply(false, false);
            var document = new MaterialRecipeDocument { wordSource = "$src CANVAS $out COPY", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "src", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            var stub = new TextureRecipeStub(document, new[] { new TextureBindingEntry { logicalName = "src", kind = TextureBindingKind.SourceTexture, sourceTexture = source }, new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall } });
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            Assert.That(delivery.Texture, Is.TypeOf<RenderTexture>());
            Assert.That(delivery.Texture.width, Is.EqualTo(128));
            Assert.That(delivery.Texture.height, Is.EqualTo(256));
            yield return AssertReadbackPixel(delivery.Texture, 128, 127, 255, new Vector4(0f, 1f, 0f, 1f), 0.002f);
            delivery.Dispose();
            Object.Destroy(source);
            Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator NativeRectangleAlphaComposite_DeliversRenderTexturePixels()
        {
#if UNITY_EDITOR
            var bottom = SolidNonSquare(new Color(1f, 0f, 0f, 0.5f));
            var top = SolidNonSquare(new Color(0f, 0f, 1f, 0.25f));
            var root = CreateHost(out TextureStackMachineHost host);
            var document = Document("$bottom CANVAS $top ACOPY .", 256, "bottom", "top", "out");
            var stub = new TextureRecipeStub(document, new[] { Source("bottom", bottom), Source("top", top), Output() });
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            Assert.That(delivery.Texture, Is.TypeOf<RenderTexture>());
            Assert.That(delivery.Texture.width, Is.EqualTo(256));
            Assert.That(delivery.Texture.height, Is.EqualTo(128));
            Vector4 expected = new Vector4(0.6f, 0f, 0.4f, 0.625f);
            yield return AssertReadbackPixel(delivery.Texture, 256, 0, 0, expected, 0.002f);
            yield return AssertReadbackPixel(delivery.Texture, 256, 255, 127, expected, 0.002f);
            delivery.Dispose(); Object.Destroy(bottom); Object.Destroy(top); Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Yuv_UsesDisplacedIndependentInputHalls()
        {
#if UNITY_EDITOR
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            var root = new GameObject("TextureStackMachineYuvTests"); var host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out _), Is.True); if (!host.TryInitialize(out StackMachineDiagnostic init)) { Object.Destroy(root); Assert.Ignore(init?.message); }
            Assert.That(host.TryReserveHall(256, 256, out TextureHallAllocation blockerA, out StackMachineDiagnostic blockerDiagnostic), Is.True, blockerDiagnostic?.message);
            Assert.That(host.TryReserveHall(128, 128, out TextureHallAllocation blockerB, out blockerDiagnostic), Is.True, blockerDiagnostic?.message);
            Assert.That(blockerB.PixelX, Is.GreaterThan(0));
            var y = Solid(new Color(0.5f, 0, 0)); var u = Solid(new Color(0.75f, 0, 0)); var v = Solid(new Color(0.25f, 0, 0));
            var doc = new MaterialRecipeDocument { wordSource = "$y $u $v YUV $out COPY DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string n in new[] { "y", "u", "v", "out" }) doc.bindings.Add(new StackMachineBindingDeclaration { logicalName = n, declaredKind = StackMachineBindingKind.Resource });
            var stub = new TextureRecipeStub(doc, new[] { new TextureBindingEntry { logicalName="y", kind=TextureBindingKind.SourceTexture, sourceTexture=y }, new TextureBindingEntry { logicalName="u", kind=TextureBindingKind.SourceTexture, sourceTexture=u }, new TextureBindingEntry { logicalName="v", kind=TextureBindingKind.SourceTexture, sourceTexture=v }, new TextureBindingEntry { logicalName="out", kind=TextureBindingKind.OutputHall } });
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic d), Is.True, d?.message);
            while (!handle.IsCompleted) yield return null; Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message); Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            yield return AssertReadbackPixel(delivery.Texture, 128, 0, 0, new Vector4(0.1063f, 0.5702f, 0.9639f, 1f), 0.002f);
            delivery.Dispose(); Assert.That(host.TryReleaseHall(blockerA), Is.True); Assert.That(host.TryReleaseHall(blockerB), Is.True); Object.Destroy(y); Object.Destroy(u); Object.Destroy(v); Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Colorize_ReplacesChromaPreservesAlphaAndUsesGpu()
        {
#if UNITY_EDITOR
            var source = Solid(new Color(0.5f, 0.5f, 0.5f, 0.25f));
            yield return AssertGpuPixel("$source 0.6666667 1 0 COLORIZE .", new[] { Source("source", source), Output() }, new Vector4(0.4278f, 0.4278f, 1f, 0.25f), 0.005f);
            Object.Destroy(source);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Colorize_UnitHueRoundTripsAtFullSaturation()
        {
#if UNITY_EDITOR
            foreach (var sample in new[]
            {
                new { hue = 0f, luminance = 0.2126f, expected = new Vector4(1f, 0f, 0f, 1f) },
                new { hue = 1f / 3f, luminance = 0.7152f, expected = new Vector4(0f, 1f, 0f, 1f) },
                new { hue = 2f / 3f, luminance = 0.0722f, expected = new Vector4(0f, 0f, 1f, 1f) }
            })
            {
                Texture2D source = Solid(new Color(sample.luminance, sample.luminance, sample.luminance, 1f));
                yield return AssertGpuPixel("$source " + sample.hue.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 1 0 COLORIZE .", new[] { Source("source", source), Output() }, sample.expected, 0.005f);
                Object.Destroy(source);
            }
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [Test]
        public void AlphaWordSignatures_RemainDistinctForStraightAndPremultipliedInputs()
        {
            var registry = new StackMachineWordRegistry();
            new TextureWordSet().RegisterInto(registry);
            var document = new MaterialRecipeDocument { wordSource = "$a $b ACOPY DROP $a $b PACOPY DROP", outputLogicalName = "out", outputWidth = 128, outputHeight = 128 };
            foreach (string name in new[] { "a", "b", "out" }) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });

            Assert.That(StackMachineCompiler.TryCompile(document, registry, out StackMachinePlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(plan.Instructions[2].WordId, Is.EqualTo(TextureWordSet.AlphaOver));
            Assert.That(plan.Instructions[6].WordId, Is.EqualTo(TextureWordSet.PremultipliedAlphaOver));
        }

        [UnityTest]
        public IEnumerator AlphaCopy_UsesStraightSourceOverOnGpu()
        {
#if UNITY_EDITOR
            var bottom = Solid(new Color(1f, 0f, 0f, 0.5f));
            var top = Solid(new Color(0f, 0f, 1f, 0.25f));
            yield return AssertGpuPixel("$bottom $top ACOPY .", new[] { Source("bottom", bottom), Source("top", top), Output() }, new Vector4(0.6f, 0f, 0.4f, 0.625f));
            Object.Destroy(bottom); Object.Destroy(top);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator PremultipliedAlphaCopy_UsesPremultipliedSourceOverOnGpu()
        {
#if UNITY_EDITOR
            var bottom = Solid(new Color(0.5f, 0f, 0f, 0.5f));
            var top = Solid(new Color(0f, 0f, 0.25f, 0.25f));
            yield return AssertGpuPixel("$bottom $top PACOPY .", new[] { Source("bottom", bottom), Source("top", top), Output() }, new Vector4(0.375f, 0f, 0.25f, 0.625f));
            Object.Destroy(bottom); Object.Destroy(top);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Alpha_ReplacesSourceAlphaWithGrayRgbAverageOnGpu()
        {
#if UNITY_EDITOR
            var source = Solid(new Color(0.2f, 0.4f, 0.6f, 0.9f));
            var gray = Solid(new Color(0.3f, 0.6f, 0.9f, 0.1f));
            yield return AssertGpuPixel("$source $gray ALPHA .", new[] { Source("source", source), Source("gray", gray), Output() }, new Vector4(0.2f, 0.4f, 0.6f, 0.6f));
            Object.Destroy(source); Object.Destroy(gray);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator StackBuiltins_PreserveDuplicatedIntermediateBranchesOnGpu()
        {
#if UNITY_EDITOR
            var iris = Solid(Color.black);
            var a = Solid(Color.black);
            var b = Solid(Color.red);
            var c = Solid(Color.blue);
            yield return AssertGpuPixel("$iris $a ACOPY DUP $b ACOPY SWAP $c ACOPY ADD .", new[] { Source("iris", iris), Source("a", a), Source("b", b), Source("c", c), Output() }, new Vector4(1f, 0f, 1f, 2f));
            Object.Destroy(iris); Object.Destroy(a); Object.Destroy(b); Object.Destroy(c);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CopyAddMultiplyAndNormalWeightedBlend_ProduceExpectedGpuPixels()
        {
#if UNITY_EDITOR
            var a = Solid(new Color(0.2f, 0.4f, 0.6f, 0.8f));
            var b = Solid(new Color(0.5f, 0.25f, 0.75f, 0.5f));
            yield return AssertGpuPixel("$a $out COPY DROP", new[] { Source("a", a), Output() }, new Vector4(0.2f, 0.4f, 0.6f, 0.8f));
            yield return AssertGpuPixel("$a $b ADD $out COPY DROP", new[] { Source("a", a), Source("b", b), Output() }, new Vector4(0.7f, 0.65f, 1.35f, 1.3f));
            yield return AssertGpuPixel("1 1 1 1 FILL $a SUB .", new[] { Source("a", a), Output() }, new Vector4(0.8f, 0.6f, 0.4f, 0.2f));
            yield return AssertGpuPixel("$a $b MULTIPLY $out COPY DROP", new[] { Source("a", a), Source("b", b), Output() }, new Vector4(0.1f, 0.1f, 0.45f, 0.4f));
            var baseNormal = Solid(new Color(0.5f, 0.5f, 1f, 1f));
            var detailNormal = Solid(new Color(1f, 0.5f, 0.5f, 1f));
            yield return AssertGpuPixel("$base $detail 0.25 NORMAL_WEIGHTED_BLEND $out COPY DROP", new[] { Source("base", baseNormal), Source("detail", detailNormal), Output() }, new Vector4(0.658114f, 0.5f, 0.974342f, 1f), 0.005f);
            Object.Destroy(a); Object.Destroy(b); Object.Destroy(baseNormal); Object.Destroy(detailNormal);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator NormalVectorWords_AccumulateDeltaAndNormalizeOnlyAtFinalize()
        {
#if UNITY_EDITOR
            ComputeShader normalCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.NormalTextureStackMachineComputePath);
            Assert.That(normalCompute, Is.Not.Null);
            var root = CreateHost(out TextureStackMachineHost host);
            Assert.That(host.TryAssignNormalComputeProgram(normalCompute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            var baseNormal = Solid(new Color(0.5f, 0.5f, 1f, 1f));
            var targetNormal = Solid(new Color(1f, 0.5f, 0.5f, 1f));
            var document = Document("$base NORMAL_BASE $base $target 0.5 NORMAL_DELTA_ADD NORMAL_FINALIZE .", 128, "base", "target", "out");
            var stub = new TextureRecipeStub(document, new[] { Source("base", baseNormal), Source("target", targetNormal), Output() });
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            yield return AssertReadbackPixel(delivery.Texture, 128, 0, 0, new Vector4(0.853553f, 0.5f, 0.853553f, 1f), 0.005f);
            delivery.Dispose(); Object.Destroy(baseNormal); Object.Destroy(targetNormal); Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Resample_UsesPixelCenterBilinearSamplingAndClampOnGpu()
        {
#if UNITY_EDITOR
            var source = new Texture2D(128, 128, TextureFormat.RGBAHalf, false, true);
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) pixels[y * 128 + x] = new Color(x / 127f, y / 127f, 0.25f, 1f);
            source.SetPixels(pixels); source.Apply(false, false);
            var root = CreateHost(out TextureStackMachineHost host);
            var document = Document("$source RESAMPLE $out COPY DROP", 256, "source", "out");
            var stub = new TextureRecipeStub(document, new[] { Source("source", source), Output() });
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            yield return AssertReadbackPixel(delivery.Texture as RenderTexture, 256, 0, 0, new Vector4(0f, 0f, 0.25f, 1f), 0.002f);
            yield return AssertReadbackPixel(delivery.Texture as RenderTexture, 256, 128, 128, new Vector4(63.75f / 127f, 63.75f / 127f, 0.25f, 1f), 0.002f);
            delivery.Dispose(); Object.Destroy(source); Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SrgbSquareSource_IngestsAsLinearGpuColor()
        {
#if UNITY_EDITOR
            var source = new Texture2D(128, 128, TextureFormat.RGBA32, false, false);
            Assert.That(source.isDataSRGB, Is.True);
            source.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
            source.Apply(false, false);
            float linear = Mathf.GammaToLinearSpace(0.5f);
            yield return AssertGpuPixel("$source .", new[] { Source("source", source), Output() }, new Vector4(linear, linear, linear, 1f), 0.002f);
            Object.Destroy(source);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Queue_CoalescesPendingCancelsConsumerAndMarksSubmittedWorkStale()
        {
#if UNITY_EDITOR
            var root = CreateHost(out TextureStackMachineHost host);
            var executor = new TextureExecutor(host);
            TextureExecutionOriginKey callerOrigin = host.CreateOrigin();
            TextureExecutionOriginKey otherCallerOrigin = host.CreateOrigin();
            Assert.That(executor.TryExecute(FillStub(Color.red), callerOrigin, out TextureExecutionHandle coalesced, out StackMachineDiagnostic coalescedDiagnostic), Is.True, coalescedDiagnostic?.message);
            Assert.That(executor.TryExecute(FillStub(Color.green), callerOrigin, out TextureExecutionHandle replacement, out StackMachineDiagnostic replacementDiagnostic), Is.True, replacementDiagnostic?.message);
            Assert.That(coalesced.IsCompleted, Is.True);
            Assert.That(coalesced.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
            Assert.That(host.PendingRequestCount, Is.EqualTo(1));

            Assert.That(executor.TryExecute(FillStub(Color.blue), otherCallerOrigin, out TextureExecutionHandle cancelled, out StackMachineDiagnostic cancelledDiagnostic), Is.True, cancelledDiagnostic?.message);
            cancelled.Dispose();
            Assert.That(cancelled.IsCompleted, Is.True);
            Assert.That(cancelled.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));

            yield return null;
            Assert.That(host.HasSubmittedRequest, Is.True);
            Assert.That(executor.TryExecute(FillStub(Color.white), callerOrigin, out TextureExecutionHandle newest, out StackMachineDiagnostic newestDiagnostic), Is.True, newestDiagnostic?.message);
            while (!replacement.IsCompleted || !newest.IsCompleted) yield return null;
            Assert.That(replacement.Succeeded, Is.False);
            Assert.That(replacement.Diagnostic.domainCode, Is.EqualTo("RequestStale"));
            Assert.That(newest.Succeeded, Is.True, newest.Diagnostic?.message);
            Assert.That(newest.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            Assert.That(newest.Result.TryTakeDelivery(out _), Is.False);
            delivery.Dispose(); Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Queue_DisposingSubmittedHandleDiscardsDeliveryAfterFence()
        {
#if UNITY_EDITOR
            var root = CreateHost(out TextureStackMachineHost host);
            Assert.That(new TextureExecutor(host).TryExecute(FillStub(Color.cyan), host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            yield return null;
            Assert.That(host.HasSubmittedRequest, Is.True);
            handle.Dispose();
            Assert.That(handle.IsCompleted, Is.True);
            Assert.That(handle.Succeeded, Is.False);
            Assert.That(handle.Diagnostic.domainCode, Is.EqualTo("RequestCancelled"));
            while (host.HasSubmittedRequest) yield return null;
            Assert.That(handle.Result, Is.Null);
            Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Queue_IndependentCallerOriginsDoNotCoalesceOnOneHost()
        {
#if UNITY_EDITOR
            var root = CreateHost(out TextureStackMachineHost host);
            var executor = new TextureExecutor(host);
            TextureExecutionOriginKey firstCallerOrigin = host.CreateOrigin();
            TextureExecutionOriginKey secondCallerOrigin = host.CreateOrigin();
            Assert.That(executor.TryExecute(FillStub(Color.red), firstCallerOrigin, out TextureExecutionHandle first, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
            Assert.That(executor.TryExecute(FillStub(Color.green), secondCallerOrigin, out TextureExecutionHandle second, out StackMachineDiagnostic secondDiagnostic), Is.True, secondDiagnostic?.message);
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(host.PendingRequestCount, Is.EqualTo(2), "Independent caller tokens must retain independent pending requests.");

            while (!first.IsCompleted || !second.IsCompleted) yield return null;
            Assert.That(first.Succeeded, Is.True, first.Diagnostic?.message);
            Assert.That(second.Succeeded, Is.True, second.Diagnostic?.message);
            Assert.That(first.Result.TryTakeDelivery(out TextureDelivery firstDelivery), Is.True);
            Assert.That(second.Result.TryTakeDelivery(out TextureDelivery secondDelivery), Is.True);
            firstDelivery.Dispose();
            secondDelivery.Dispose();
            Object.Destroy(root);
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Queue_RejectsAnOriginIssuedByAnotherHost()
        {
#if UNITY_EDITOR
            var firstRoot = CreateHost(out TextureStackMachineHost firstHost);
            var secondRoot = CreateHost(out TextureStackMachineHost secondHost);
            try
            {
                Assert.That(new TextureExecutor(secondHost).TryExecute(FillStub(Color.magenta), firstHost.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(handle, Is.Null);
                Assert.That(diagnostic?.domainCode, Is.EqualTo("OriginHostMismatch"));
                Assert.That(secondHost.PendingRequestCount, Is.Zero);
            }
            finally
            {
                Object.Destroy(firstRoot);
                Object.Destroy(secondRoot);
            }
            yield break;
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            yield break;
#endif
        }

        private static IEnumerator AssertGpuPixel(string wordSource, TextureBindingEntry[] bindings, Vector4 expected, float tolerance = 0.002f)
        {
            var root = CreateHost(out TextureStackMachineHost host);
            var document = Document(wordSource, 128, BindingNames(bindings));
            var stub = new TextureRecipeStub(document, bindings);
            Assert.That(new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            while (!handle.IsCompleted) yield return null;
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            Assert.That(handle.Result.TryTakeDelivery(out TextureDelivery delivery), Is.True);
            yield return AssertReadbackPixel(delivery.Texture as RenderTexture, 128, 0, 0, expected, tolerance);
            delivery.Dispose(); Object.Destroy(root);
        }

        private static IEnumerator AssertReadbackPixel(Texture texture, int edge, int x, int y, Vector4 expected, float tolerance)
        {
            Assert.That(texture, Is.Not.Null);
            bool done = false; AsyncGPUReadbackRequest request = default;
            AsyncGPUReadback.Request(texture, 0, value => { request = value; done = true; });
            while (!done) yield return null;
            Assert.That(request.hasError, Is.False);
            NativeArray<ushort> data = request.GetData<ushort>();
            int start = (y * edge + x) * 4;
            Assert.That(Half(data[start]), Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(Half(data[start + 1]), Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(Half(data[start + 2]), Is.EqualTo(expected.z).Within(tolerance));
            Assert.That(Half(data[start + 3]), Is.EqualTo(expected.w).Within(tolerance));
        }

        private static GameObject CreateHost(out TextureStackMachineHost host)
        {
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            var root = new GameObject("TextureStackMachineGpuPixelTests"); host = root.AddComponent<TextureStackMachineHost>();
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) { Object.Destroy(root); Assert.Ignore(initialize?.message); }
            return root;
        }

        private static MaterialRecipeDocument Document(string wordSource, int outputEdge, params string[] names)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = outputEdge, outputHeight = outputEdge };
            foreach (string name in names) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            return document;
        }

        private static TextureRecipeStub FillStub(Color color)
        {
            var document = Document(color.r + " " + color.g + " " + color.b + " " + color.a + " FILL $out COPY DROP", 128, "out");
            return new TextureRecipeStub(document, new[] { Output() });
        }

        private static string[] BindingNames(TextureBindingEntry[] bindings) { var names = new string[bindings.Length]; for (int i = 0; i < bindings.Length; i++) names[i] = bindings[i].logicalName; return names; }
        private static TextureBindingEntry Source(string name, Texture2D texture) => new TextureBindingEntry { logicalName = name, kind = TextureBindingKind.SourceTexture, sourceTexture = texture };
        private static TextureBindingEntry Output() => new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall };

        private static Texture2D Solid(Color color) { var texture=new Texture2D(128,128,TextureFormat.RGBAHalf,false,true); texture.SetPixel(0,0,color); texture.Apply(false,false); return texture; }
        private static Texture2D SolidNonSquare(Color color) { var texture = new Texture2D(256, 128, TextureFormat.RGBAHalf, false, true); var pixels = new Color[256 * 128]; for (int i = 0; i < pixels.Length; i++) pixels[i] = color; texture.SetPixels(pixels); texture.Apply(false, false); return texture; }
        private static float Half(ushort h) { int s=(h>>15)&1,e=(h>>10)&31,f=h&1023; if(e==0)return (s==0?1:-1)*f/16777216f; return (s==0?1:-1)*(1f+f/1024f)*Mathf.Pow(2,e-15); }
    }
}
