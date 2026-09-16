// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// <summary>
    /// Spec15-2 §15.2 shared fixture for the Phase06 integration preparation. CreateHost and the readback
    /// conversion are copied from the Spec14 runtime tests; the only additions are the §82 grid/allocator/
    /// capability swap (256 default, 512 only when specified), the COPY128/FILL128/Output-only inputs, the
    /// once-built Update delegate, the lease generation flows, the real 30-second GPU wait, and the Scope-based
    /// UnityTearDown cleanup. No random numbers. The single smoke, <see cref="Spec152_Fixture_CopyPixels"/>,
    /// exercises the current public path and is not a new-queue acceptance record. Shared helpers are internal
    /// static so a sibling Batch in the same PlayMode assembly can call Spec152GpuFixture.X(...).
    /// </summary>
    public sealed class Spec152GpuFixture
    {
        // §15.2 fixed inputs (design decisions, used verbatim).
        internal const string Copy128Word = "$a $out COPY DROP";
        internal const string Fill128Word = "1 0 0 1 FILL $out COPY DROP";
        internal const string OutputOnlyWord = "$out 0 0 0 1 FILL_OUT";
        internal const float Tolerance = 0.002f;
        internal const float RealGpuWaitLimitSeconds = 30f;
        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private Spec152GpuScope scope;

        [SetUp]
        public void SetUp() => scope = new Spec152GpuScope();

        [UnityTearDown]
        public IEnumerator CleanupFixture()
        {
            yield return Cleanup(scope);
            scope = null;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Spec152_Fixture_CopyPixels()
        {
#if UNITY_EDITOR
            Texture2D source = CreateCopy128Source(scope);
            CreateHost(scope, 256); // registers Root/Host/Tick/Pump and the swapped grid; Assert.Ignore on init failure
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = CreateCopy128Stub(source);
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle); // register before the assert
            Assert.That(enqueued, Is.True, diagnostic?.message);
            yield return WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            bool took = handle.Result.TryTakeDelivery(out TextureDelivery delivery);
            if (delivery != null) scope.Deliveries.Add(delivery); // register before the assert
            Assert.That(took, Is.True);
            yield return AssertAllPixels(scope, delivery.Texture, 128, 128, new Vector4(0.25f, 0.5f, 0.75f, 1f));
            delivery.Dispose();
            Assert.That(HashSetCount(host, "outstandingDeliveries"), Is.Zero, "the disposed delivery left the host's delivery sets");
            Assert.That(HashSetCount(host, "handedOffDeliveries"), Is.Zero, "the disposed delivery left the host's delivery sets");
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
#endif
            yield break;
        }

        // §82: Spec14 CreateHost compute load/assignment plus the specified post-initialization swap. The scope
        // registers Root/Host immediately; the grid/allocator/capability swap happens only after initialization and
        // with no requests. Assets are referenced Editor-only; the non-Editor path ignores and returns null.
        internal static GameObject CreateHost(Spec152GpuScope scope, int gridEdge = 256)
        {
#if UNITY_EDITOR
            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
            Assert.That(compute, Is.Not.Null);
            var root = new GameObject("Spec152GpuFixture");
            scope.Root = root;
            var host = root.AddComponent<TextureStackMachineHost>();
            scope.Host = host;
            Assert.That(host.TryAssignComputeProgram(compute, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
            if (!host.TryInitialize(out StackMachineDiagnostic initialize)) Assert.Ignore(initialize?.message); // UnityTearDown recovers; do not Destroy here
            scope.Tick = UpdateDelegate(host);
            scope.Pump = (MonoBehaviour)HostField(host, "retirementPump");
            var oldGrid = (RenderTexture)HostField(host, "grid");
            long budget = ((TextureGpuCapability)HostField(host, "capability")).GpuBudgetBytes;
            RenderTextureDescriptor descriptor = oldGrid.descriptor;
            descriptor.width = gridEdge;
            descriptor.height = gridEdge;
            var newGrid = new RenderTexture(descriptor) { name = "ShapeSync.TextureStackMachine.Grid" };
            scope.UnassignedGrids.Add(newGrid); // register the new grid right after creation
            Assert.That(newGrid.Create(), Is.True, "the swapped fixture grid must create successfully");
            scope.UnassignedGrids.Add(oldGrid); // register the old grid right before the host.grid swap
            SetHostField(host, "grid", newGrid);
            scope.UnassignedGrids.Remove(newGrid); // newGrid becomes the live host grid, no longer unassigned
            SetHostField(host, "allocator", new TextureHallAllocator(gridEdge));
            SetHostField(host, "capability", new TextureGpuCapability(SystemInfo.maxTextureSize, budget, gridEdge));
            oldGrid.Release(); // GPU requests are guaranteed zero at this point only
            UnityEngine.Object.Destroy(oldGrid);
            scope.UnassignedGrids.Remove(oldGrid);
            return root;
#else
            Assert.Ignore("Compute asset loading is Editor-only.");
            return null;
#endif
        }

        // §84 COPY128 input: register the source right after creation, then fill all pixels and return.
        internal static Texture2D CreateCopy128Source(Spec152GpuScope scope)
        {
            var source = new Texture2D(128, 128, UnityEngine.TextureFormat.RGBAHalf, false, true);
            scope.Textures.Add(source);
            var fillColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            var pixels = new Color[128 * 128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = fillColor;
            source.SetPixels(pixels);
            source.Apply(false, false);
            return source;
        }

        internal static TextureRecipeStub CreateCopy128Stub(Texture2D source)
            => new TextureRecipeStub(Document(Copy128Word, 128, "a", "out"), new[] { Source("a", source), Output() });

        internal static TextureRecipeStub CreateFill128Stub()
            => new TextureRecipeStub(Document(Fill128Word, 128, "out"), new[] { Output() });

        internal static TextureRecipeStub CreateOutputOnlyStub(int width, int height)
        {
            MaterialRecipeDocument document = Document(OutputOnlyWord, width, "out");
            document.outputHeight = height;
            return new TextureRecipeStub(document, new[] { Output() });
        }

        // §88: the Host's private Update converted once to a delegate so synchronous tests call it directly
        // without reflection in their measured region and without mixing with the auto PlayerLoop.
        internal static Action UpdateDelegate(TextureStackMachineHost host)
            => (Action)typeof(TextureStackMachineHost).GetMethod("Update", Refl).CreateDelegate(typeof(Action), host);

        // §15.3: GPU waits inside tests are capped at 30 real seconds; exceeding the cap is Assert.Fail.
        internal static IEnumerator WaitForRealGpu(Func<bool> completed)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + RealGpuWaitLimitSeconds;
            while (!completed())
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) Assert.Fail("The real GPU wait exceeded the 30-second test limit.");
                yield return null;
            }
        }

        // §87: AsyncGPUReadback.Request, hasError=false, GetData<ushort>() through the existing Half conversion;
        // every width×height×4 element within the inherited 0.002 tolerance. OutstandingReadbacks guards the pending
        // readback so the texture is not disposed/destroyed before the callback completes.
        internal static IEnumerator AssertAllPixels(Spec152GpuScope scope, Texture texture, int width, int height, Vector4 expected)
        {
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(width), "the readback texture width matches the expected width");
            Assert.That(texture.height, Is.EqualTo(height), "the readback texture height matches the expected height");
            bool done = false; AsyncGPUReadbackRequest request = default;
            scope.OutstandingReadbacks++;
            try { AsyncGPUReadback.Request(texture, 0, value => { request = value; done = true; scope.OutstandingReadbacks--; }); }
            catch { scope.OutstandingReadbacks--; throw; }
            yield return WaitForRealGpu(() => done);
            Assert.That(request.hasError, Is.False);
            NativeArray<ushort> data = request.GetData<ushort>();
            for (int i = 0; i < width * height; i++)
            {
                int start = i * 4;
                Assert.That(Half(data[start]), Is.EqualTo(expected.x).Within(Tolerance), $"pixel {i} channel 0");
                Assert.That(Half(data[start + 1]), Is.EqualTo(expected.y).Within(Tolerance), $"pixel {i} channel 1");
                Assert.That(Half(data[start + 2]), Is.EqualTo(expected.z).Within(Tolerance), $"pixel {i} channel 2");
                Assert.That(Half(data[start + 3]), Is.EqualTo(expected.w).Within(Tolerance), $"pixel {i} channel 3");
            }
        }

        // §90: complete a real-GPU COPY128 with retainSourceLease and take the Source Lease from the Result; the
        // Delivery is taken and disposed here. Handles/Deliveries/Leases register into the scope before their
        // asserts. The callback receives the live scope-owned lease; the helper does not dispose it at its end.
        internal static IEnumerator RunCopy128RetainSourceLease(Spec152GpuScope scope, Texture2D source, Func<TextureSourceLease, IEnumerator> onLease)
        {
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = CreateCopy128Stub(source);
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), new TextureExecutionOptions(retainSourceLease: true), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            yield return WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            bool tookDelivery = handle.Result.TryTakeDelivery(out TextureDelivery delivery);
            if (delivery != null) scope.Deliveries.Add(delivery);
            Assert.That(tookDelivery, Is.True);
            delivery.Dispose();
            bool tookLease = handle.Result.TryTakeSourceLease(out TextureSourceLease lease);
            if (lease != null) scope.SourceLeases.Add(lease);
            Assert.That(tookLease, Is.True);
            yield return onLease(lease);
        }

        // §90: complete a real-GPU FILL128 with retainOutputLease and hand the Output Lease to the callback.
        internal static IEnumerator RunFill128RetainOutputLease(Spec152GpuScope scope, Func<TextureOutputLease, IEnumerator> onLease)
        {
            TextureStackMachineHost host = scope.Host;
            TextureRecipeStub stub = CreateFill128Stub();
            bool enqueued = new TextureExecutor(host).TryExecute(stub, host.CreateOrigin(), new TextureExecutionOptions(retainOutputLease: true), out TextureExecutionHandle handle, out StackMachineDiagnostic diagnostic);
            if (handle != null) scope.Handles.Add(handle);
            Assert.That(enqueued, Is.True, diagnostic?.message);
            yield return WaitForRealGpu(() => handle.IsCompleted);
            Assert.That(handle.Succeeded, Is.True, handle.Diagnostic?.message);
            bool tookLease = handle.Result.TryTakeOutputLease(out TextureOutputLease lease);
            if (lease != null) scope.OutputLeases.Add(lease);
            Assert.That(tookLease, Is.True);
            yield return onLease(lease);
        }

        // T03: the single cleanup coroutine in the fixed order. No immediate Destroy via finally; UnityTearDown runs
        // after assert/Ignore/exception. Fail paths do not run the later steps, and nothing is force-destroyed.
        internal static IEnumerator Cleanup(Spec152GpuScope scope)
        {
            if (scope == null) yield break;
            TextureStackMachineHost host = scope.Host;

            // (1) reset the test contacts and accepting flag, cancel un-started requests.
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
            if (host != null) { SetHostField(host, "testFencePending", false); SetHostField(host, "testFailurePoint", TextureStackMachineHost.TestFailurePoint.None); }
#endif
            if (host != null) SetHostField(host, "acceptingRequests", false);
            foreach (TextureExecutionHandle handle in scope.Handles)
                if (!handle.IsCompleted) handle.Dispose(); // cancel un-started requests only; completed Results are handled in (4)
            if (host != null && (int)HostProperty(host, "PendingRequestCount") > 0)
                Assert.Fail("Fixture cleanup left untracked pending requests; resources preserved.");

            // (2) resolve the pump ticket through reflection (null for a normal smoke).
            object ticket = null;
            if (scope.Pump != null) ticket = scope.Pump.GetType().GetField("ticket", Refl).GetValue(scope.Pump);
#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
            if (ticket != null) ticket.GetType().GetField("testHoldPending", Refl).SetValue(ticket, false);
#endif

            // (3) one real 30-second deadline; wait until submitted is cleared, the ticket is disposed (or null), and no readback is outstanding.
            double deadline = Time.realtimeSinceStartupAsDouble + RealGpuWaitLimitSeconds;
            while (true)
            {
                bool hostDone = host == null || !(bool)HostProperty(host, "HasSubmittedRequest");
                bool ticketDone = ticket == null || (bool)ticket.GetType().GetField("disposed", Refl).GetValue(ticket);
                bool readbackDone = scope.OutstandingReadbacks == 0;
                if (hostDone && ticketDone && readbackDone) break;
                if (host != null && (bool)HostProperty(host, "HasSubmittedRequest"))
                {
                    object request = HostField(host, "submitted");
                    GraphicsFence fence = (GraphicsFence)request.GetType().GetField("fence", Refl).GetValue(request);
                    try
                    {
                        if (fence.passed) scope.Tick?.Invoke(); // already stopped accepting, so this does not submit the next head
                    }
                    catch { Assert.Fail("Fixture cleanup could not poll/finish submitted GPU work; resources preserved."); yield break; }
                }
                if (ticket != null && (bool)ticket.GetType().GetField("uncertain", Refl).GetValue(ticket))
                    Assert.Fail("Fixture cleanup found an Uncertain ticket; resources preserved.");
                if (Time.realtimeSinceStartupAsDouble > deadline) Assert.Fail("Fixture cleanup exceeded 30 seconds; GPU resources preserved.");
                yield return null;
            }

            // (4) no untracked pending; then Dispose in the fixed order (wrappers are idempotent, disposed refs stay listed).
            if (host != null && (int)HostProperty(host, "PendingRequestCount") > 0)
                Assert.Fail("Fixture cleanup left untracked pending requests; resources preserved.");
            foreach (TextureDelivery d in scope.Deliveries) d.Dispose();
            foreach (TextureSourceLease l in scope.SourceLeases) l.Dispose();
            foreach (TextureOutputLease l in scope.OutputLeases) l.Dispose();
            foreach (TextureExecutionHandle h in scope.Handles) h.Dispose();
            if (host != null)
            {
                Assert.That(HashSetCount(host, "outstandingDeliveries"), Is.Zero, "no delivery remains outstanding on the host");
                Assert.That(HashSetCount(host, "handedOffDeliveries"), Is.Zero, "no delivery remains handed off on the host");
                Assert.That(HashSetCount(host, "outstandingSourceLeases"), Is.Zero, "no source lease remains outstanding on the host");
                Assert.That(HashSetCount(host, "outstandingOutputLeases"), Is.Zero, "no output lease remains outstanding on the host");
            }

            // (5) release/destroy the tracked grids, textures, root, and a live pump whose ticket is null/disposed.
            foreach (RenderTexture grid in scope.UnassignedGrids) { grid.Release(); UnityEngine.Object.Destroy(grid); }
            foreach (Texture2D texture in scope.Textures) UnityEngine.Object.Destroy(texture);
            if (scope.Root != null) UnityEngine.Object.Destroy(scope.Root);
            if (scope.Pump != null && (ticket == null || (bool)ticket.GetType().GetField("disposed", Refl).GetValue(ticket)))
                UnityEngine.Object.Destroy(scope.Pump.gameObject);
            yield return null;
        }

        private static MaterialRecipeDocument Document(string wordSource, int outputEdge, params string[] names)
        {
            var document = new MaterialRecipeDocument { wordSource = wordSource, outputLogicalName = "out", outputWidth = outputEdge, outputHeight = outputEdge };
            foreach (string name in names) document.bindings.Add(new StackMachineBindingDeclaration { logicalName = name, declaredKind = StackMachineBindingKind.Resource });
            return document;
        }

        private static TextureBindingEntry Source(string name, Texture2D texture) => new TextureBindingEntry { logicalName = name, kind = TextureBindingKind.SourceTexture, sourceTexture = texture };
        private static TextureBindingEntry Output() => new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall };

        private static object HostField(TextureStackMachineHost host, string name) => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);
        private static object HostProperty(TextureStackMachineHost host, string name) => typeof(TextureStackMachineHost).GetProperty(name, Refl).GetValue(host);
        private static void SetHostField(TextureStackMachineHost host, string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);

        private static int HashSetCount(TextureStackMachineHost host, string name)
            => (int)HostField(host, name).GetType().GetProperty("Count").GetValue(HostField(host, name));

        private static float Half(ushort h) { int s=(h>>15)&1,e=(h>>10)&31,f=h&1023; if(e==0)return (s==0?1:-1)*f/16777216f; return (s==0?1:-1)*(1f+f/1024f)*Mathf.Pow(2,e-15); }
    }

    /// <summary>
    /// Spec15-2 §15.2 per-test ownership scope (not a product type). One scope owns one host. Every reference a
    /// test creates is registered into the matching list immediately after creation, before the next assert/yield/
    /// callback; disposed references stay in the list (each wrapper's Dispose is idempotent). Cleanup runs at
    /// UnityTearDown and never force-destroys an un-passed or Uncertain ticket.
    /// </summary>
    internal sealed class Spec152GpuScope
    {
        internal GameObject Root;
        internal TextureStackMachineHost Host;
        internal MonoBehaviour Pump; // CreateHost stores the host's private retirementPump; null is normal for the old initialization
        internal Action Tick;        // UpdateDelegate(Host), built once inside CreateHost
        internal int OutstandingReadbacks;
        internal readonly List<Texture2D> Textures = new List<Texture2D>();
        internal readonly List<TextureExecutionHandle> Handles = new List<TextureExecutionHandle>();
        internal readonly List<TextureDelivery> Deliveries = new List<TextureDelivery>();
        internal readonly List<TextureSourceLease> SourceLeases = new List<TextureSourceLease>();
        internal readonly List<TextureOutputLease> OutputLeases = new List<TextureOutputLease>();
        internal readonly List<RenderTexture> UnassignedGrids = new List<RenderTexture>();
    }
}
