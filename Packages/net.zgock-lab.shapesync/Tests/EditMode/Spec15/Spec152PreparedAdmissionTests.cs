// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 Phase06-7-1 CPU coverage for the prepared admission validation order and the coalesce commit
    /// (Bootups/Phase06-7-1.txt §5). The private TryEnqueuePrepared is invoked via reflection with a CPU-injected
    /// allocator, capability, compute program, kernel cache, and state flags; nothing is submitted to the GPU and no
    /// GPU probe runs. Fixture-owned textures, leases, and the manual delivery are cleaned up in nested finally
    /// blocks that also run when an assert fails.
    /// </summary>
    public sealed class Spec152PreparedAdmissionTests
    {
        private const long G = 2097152L;
        private const long C = 131072L;
        private const string NormalRecipe = "1 0 0 1 FILL $a ADD $out COPY";

        private static readonly BindingFlags Refl = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type RequestType = typeof(TextureStackMachineHost).GetNestedType("QueuedRequest", BindingFlags.NonPublic);

        private GameObject root;
        private TextureStackMachineHost host;
        private TextureHallAllocator allocator;
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private readonly List<TextureSourceLease> ownedLeases = new List<TextureSourceLease>();
        private readonly List<KeyValuePair<TextureExecutionHandle, Action<TextureExecutionHandle>>> subscriptions = new List<KeyValuePair<TextureExecutionHandle, Action<TextureExecutionHandle>>>();
        private readonly Dictionary<TextureExecutionHandle, int> callbackCounts = new Dictionary<TextureExecutionHandle, int>();
        private TextureDelivery fixtureDelivery;

        [SetUp]
        public void SetUp()
        {
            try
            {
                root = new GameObject("Spec152 Prepared Admission Host");
                host = root.AddComponent<TextureStackMachineHost>();
                allocator = new TextureHallAllocator(512);
                SetField("allocator", allocator);
                SetField("capability", new TextureGpuCapability(512, G + 4L * C, 512));
                ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                Assert.That(compute, Is.Not.Null, "the fixed Texture StackMachine compute asset must load");
                SetField("computeProgram", compute);
                object[] cacheArgs = { null };
                Assert.That((bool)Invoke("TryCacheKernels", cacheArgs), Is.True, "the kernel cache must fill from the loaded program");
                SetField("initialized", true);
                SetField("acceptingRequests", true);
            }
            catch { Cleanup(); throw; }
        }

        [TearDown]
        public void TearDown() => Cleanup();
        // CR003: Host admission priority only; executor and waiting-input validation remain separate.
        [TestCase(true, "SourceLeaseHostMismatch")]
        [TestCase(false, "SourceLeaseInvalid")]
        public void CrossReview003_InvalidLease_HostIdentityPrecedesValidity(bool foreign, string expected)
        {
            var foreignRoot = new GameObject("CR003 foreign host");
            try
            {
                TextureStackMachineHost owner = foreign ? foreignRoot.AddComponent<TextureStackMachineHost>() : host;
                var lease = new TextureSourceLease(owner, null); // CPU invalid-binding fault injection
                ownedLeases.Add(lease);
                Assert.That(lease.IsValid, Is.False);
                TextureExecutionPlan plan = Plan(NormalRecipe, NewOwnedTexture(128), 128);
                ulong version = allocator.Version;
                var handle = NewHandle();
                Assert.That(Admit(plan, Origin(1UL), handle, new TextureExecutionOptions(sourceLease: lease), out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo(expected));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(allocator.Version, Is.EqualTo(version));
                Assert.That(NotificationQueueCount(), Is.Zero);
                Assert.That(handle.IsCompleted, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(foreignRoot); }
        }

        [Test]
        public void T01_Normal()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;

            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(diagnostic, Is.Null);
            Assert.That(handleA1.Status, Is.EqualTo(TextureExecutionStatus.Queued));
            Assert.That(handleA1.IsCompleted, Is.False);
            Assert.That(callbackCounts[handleA1], Is.EqualTo(0));
            object candidate = FindRequestByHandle(handleA1);
            Assert.That(candidate, Is.Not.Null);
            AssertCandidateHoldsNothing(candidate);
            AssertPendingAccounting();
            Assert.That(allocator.Version, Is.EqualTo(versionBefore), "admission never touches the real allocator");
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore));
            AssertQueueOrder(candidate);
            AssertMapEntry(Origin(1UL), candidate);
        }

        [Test]
        public void T02_InvalidBindingKeepsOld()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);

            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            UnityEngine.Object.DestroyImmediate(ownedTextures[ownedTextures.Count - 1]);

            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;
            TextureExecutionHandle handleA2 = NewHandle();
            Assert.That(Admit(planA2, Origin(1UL), handleA2, null, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("SourceTextureRequired"));
            RejectCommon(handleA2, versionBefore, occupiedBefore, C, 0);
            AssertQueueOrder(requestA1);
            AssertMapEntry(Origin(1UL), requestA1);
        }

        [Test]
        public void T03_CapacityKeepsOld()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);

            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(512), 512);
            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;
            TextureExecutionHandle handleA2 = NewHandle();
            Assert.That(Admit(planA2, Origin(1UL), handleA2, null, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("RequestHallCapacityExceeded"));
            RejectCommon(handleA2, versionBefore, occupiedBefore, C, 0);
            AssertQueueOrder(requestA1);
            AssertMapEntry(Origin(1UL), requestA1);
        }

        [Test]
        public void T04_BudgetKeepsOld()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);

            SetField("capability", new TextureGpuCapability(512, G + C, 512));
            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(256), 256);
            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;
            TextureExecutionHandle handleA2 = NewHandle();
            Assert.That(Admit(planA2, Origin(1UL), handleA2, null, out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
            Assert.That(diagnostic.detail, Is.EqualTo("grid=2097152;live=0;pending=131072;replacementCredit=131072;candidate=524288;budget=2228224"));
            RejectCommon(handleA2, versionBefore, occupiedBefore, C, 0);
            AssertQueueOrder(requestA1);
            AssertMapEntry(Origin(1UL), requestA1);
        }

        [Test]
        public void T05_ValidReplacementOrder()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);
            Assert.That(requestA1, Is.Not.Null);

            TextureExecutionPlan planB = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleB = NewHandle();
            Assert.That(Admit(planB, Origin(2UL), handleB, null, out diagnostic), Is.True, diagnostic?.message);

            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA2 = NewHandle();
            Assert.That(Admit(planA2, Origin(1UL), handleA2, null, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(diagnostic, Is.Null);
            Assert.That(handleA2.Status, Is.EqualTo(TextureExecutionStatus.Queued));
            Assert.That(handleA2.IsCompleted, Is.False);
            AssertCandidateHoldsNothing(FindRequestByHandle(handleA2));

            object requestB = FindRequestByHandle(handleB);
            object requestA2 = FindRequestByHandle(handleA2);
            AssertQueueOrder(requestB, requestA2);
            AssertMapEntry(Origin(1UL), requestA2);
            AssertMapEntry(Origin(2UL), requestB);
            AssertPendingAccounting(2 * C);

            Assert.That(requestA1, Is.Not.Null);
            Assert.That(handleA1.IsCompleted, Is.True);
            Assert.That(handleA1.Succeeded, Is.False);
            Assert.That(handleA1.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
            Assert.That(RequestField(requestA1, "reservedDeliveryBytes"), Is.EqualTo(0L));
            Assert.That(RequestField(requestA1, "plan"), Is.Null);
            Assert.That(RequestField(requestA1, "context"), Is.Null);
            Assert.That(RequestField(requestA1, "hallPlan"), Is.Null);
            Assert.That(RequestField(requestA1, "requestedSourceLease"), Is.Null);
            Assert.That(RequestField(requestA1, "requestedOutputLease"), Is.Null);
            Assert.That(NotificationQueueCount(), Is.EqualTo(1), "the notification queue holds only A1");
            Assert.That(callbackCounts[handleA1], Is.EqualTo(0), "admission itself never flushes");
            Assert.That(callbackCounts[handleB], Is.EqualTo(0));

            Invoke("FlushPreparedNotifications", null);
            Assert.That(callbackCounts[handleA1], Is.EqualTo(1));
        }

        [Test]
        public void T06_B01ReplacementCredit()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);
            Assert.That(requestA1, Is.Not.Null);

            SetField("capability", new TextureGpuCapability(512, G + C, 512));
            AssertStaticBudget(G, 0L, 0L, C, G + C, true);

            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;
            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA2 = NewHandle();
            Assert.That(Admit(planA2, Origin(1UL), handleA2, null, out diagnostic), Is.True, diagnostic?.message);
            Assert.That(diagnostic, Is.Null);
            Assert.That(handleA2.Status, Is.EqualTo(TextureExecutionStatus.Queued));
            Assert.That(handleA2.IsCompleted, Is.False);
            AssertCandidateHoldsNothing(FindRequestByHandle(handleA2));
            Assert.That(allocator.Version, Is.EqualTo(versionBefore));
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore));
            AssertQueueOrder(FindRequestByHandle(handleA2));
            AssertMapEntry(Origin(1UL), FindRequestByHandle(handleA2));
            AssertPendingAccounting(C);

            Assert.That(handleA1.IsCompleted, Is.True);
            Assert.That(handleA1.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
            Assert.That(RequestField(requestA1, "reservedDeliveryBytes"), Is.EqualTo(0L));
        }

        [Test]
        public void T07_B02NoSubmittedCredit()
        {
            SetField("capability", new TextureGpuCapability(512, G + C, 512));

            Texture2D deliveredTexture = NewOwnedTexture(128);
            fixtureDelivery = new TextureDelivery(deliveredTexture, null);
            object submittedRequest = Activator.CreateInstance(RequestType, nonPublic: true);
            SetRequestField(submittedRequest, "origin", Origin(1UL));
            SetRequestField(submittedRequest, "handle", NewHandle());
            SetRequestField(submittedRequest, "reservedDeliveryBytes", 0L);
            SetRequestField(submittedRequest, "stale", false);
            SetRequestField(submittedRequest, "delivery", fixtureDelivery);
            SetField("submitted", submittedRequest);
            ((HashSet<TextureDelivery>)GetField("outstandingDeliveries")).Add(fixtureDelivery);

            AssertStaticBudget(G, C, 0L, C, G + C, false);
            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA2 = NewHandle();
            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;

            Assert.That(Admit(planA2, Origin(1UL), handleA2, null, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
            Assert.That(diagnostic.detail, Is.EqualTo("grid=2097152;live=131072;pending=0;replacementCredit=0;candidate=131072;budget=2228224"));
            RejectCommon(handleA2, versionBefore, occupiedBefore, 0L, 0);
            AssertQueueOrder();
            Assert.That(GetField("submitted"), Is.SameAs(submittedRequest));
            Assert.That(RequestField(submittedRequest, "delivery"), Is.SameAs(fixtureDelivery));
        }

        [Test]
        public void T08_SourceFallbackRejected()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);
            Assert.That(requestA1, Is.Not.Null);

            Texture2D leaseTexture = NewOwnedTexture(128);
            Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation hall), Is.True);
            var lease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(leaseTexture, hall) });
            ownedLeases.Add(lease);
            Assert.That(lease.CanAcquire, Is.True);
            Assert.That(UseCount(lease), Is.EqualTo(0));

            SetField("capability", new TextureGpuCapability(512, G + C, 512));
            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(256), 256);
            TextureExecutionHandle handleA2 = NewHandle();
            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;

            Assert.That(Admit(planA2, Origin(1UL), handleA2, new TextureExecutionOptions(sourceLease: lease), out diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("GpuTransientBudgetExceeded"));
            RejectCommon(handleA2, versionBefore, occupiedBefore, C, 0);
            AssertQueueOrder(requestA1);
            AssertMapEntry(Origin(1UL), requestA1);
            Assert.That(lease.CanAcquire, Is.True);
            Assert.That(lease.IsReleaseRequested, Is.False);
            Assert.That(UseCount(lease), Is.EqualTo(0));
            Assert.That(lease.CanAcquire, Is.True);
            Assert.That(lease.IsReleaseRequested, Is.False);
            Assert.That(UseCount(lease), Is.EqualTo(0));
        }

        [Test]
        public void T09_SourceFallbackAccepted()
        {
            TextureExecutionPlan planA1 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA1 = NewHandle();
            Assert.That(Admit(planA1, Origin(1UL), handleA1, null, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            object requestA1 = FindRequestByHandle(handleA1);
            Assert.That(requestA1, Is.Not.Null);

            Texture2D leaseTexture = NewOwnedTexture(128);
            Assert.That(allocator.TryReserve(128, 128, out TextureHallAllocation hall), Is.True);
            var lease = new TextureSourceLease(host, new Dictionary<string, TextureSourceLease.Binding> { ["a"] = new TextureSourceLease.Binding(leaseTexture, hall) });
            ownedLeases.Add(lease);
            Assert.That(lease.CanAcquire, Is.True);
            Assert.That(UseCount(lease), Is.EqualTo(0));

            TextureExecutionPlan planA2 = Plan(NormalRecipe, NewOwnedTexture(128), 128);
            TextureExecutionHandle handleA2 = NewHandle();
            ulong versionBefore = allocator.Version;
            int occupiedBefore = allocator.OccupiedRoomCount;

            Assert.That(Admit(planA2, Origin(1UL), handleA2, new TextureExecutionOptions(sourceLease: lease), out diagnostic), Is.True, diagnostic?.message);
            Assert.That(diagnostic, Is.Null);
            Assert.That(handleA2.Status, Is.EqualTo(TextureExecutionStatus.Queued));
            Assert.That(handleA2.IsCompleted, Is.False);
            Assert.That(callbackCounts[handleA2], Is.EqualTo(0));
            object candidate = FindRequestByHandle(handleA2);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(RequestField(candidate, "requestedSourceLease"), Is.Null);
            Assert.That(RequestField(candidate, "requestedOutputLease"), Is.Null);
            AssertCandidateHoldsNothing(candidate);
            AssertQueueOrder(candidate);
            AssertMapEntry(Origin(1UL), candidate);
            AssertPendingAccounting(C);
            Assert.That(allocator.Version, Is.EqualTo(versionBefore), "the fixture reservation is preparation, not an admission side effect");
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupiedBefore));
            Assert.That(lease.IsReleaseRequested, Is.True);
            Assert.That(UseCount(lease), Is.EqualTo(0));
            Assert.That(RequestField(requestA1, "reservedDeliveryBytes"), Is.EqualTo(0L));
            Assert.That(RequestField(requestA1, "plan"), Is.Null);
            Assert.That(RequestField(requestA1, "context"), Is.Null);
            Assert.That(RequestField(requestA1, "hallPlan"), Is.Null);
            Assert.That(RequestField(requestA1, "requestedSourceLease"), Is.Null);
            Assert.That(RequestField(requestA1, "requestedOutputLease"), Is.Null);
            Assert.That(handleA1.IsCompleted, Is.True);
            Assert.That(handleA1.Succeeded, Is.False);
            Assert.That(handleA1.Diagnostic.domainCode, Is.EqualTo("RequestCoalesced"));
            Assert.That(NotificationQueueCount(), Is.EqualTo(1));
            Assert.That(callbackCounts[handleA1], Is.EqualTo(0));
        }

        private object GetField(string name) => typeof(TextureStackMachineHost).GetField(name, Refl).GetValue(host);
        private void SetField(string name, object value) => typeof(TextureStackMachineHost).GetField(name, Refl).SetValue(host, value);
        private object Invoke(string name, object[] args) => typeof(TextureStackMachineHost).GetMethod(name, Refl).Invoke(host, args);
        private static object RequestField(object request, string name) => RequestType.GetField(name, Refl).GetValue(request);
        private static void SetRequestField(object request, string name, object value) => RequestType.GetField(name, Refl).SetValue(request, value);
        private TextureExecutionOriginKey Origin(ulong id) => new TextureExecutionOriginKey(host, id);
        private void CancelPrepared(TextureExecutionHandle handle) => Invoke("CancelPrepared", new object[] { handle });
        private int NotificationQueueCount() => ((Queue<TextureExecutionHandle>)GetField("notificationQueue")).Count;
        private static int UseCount(TextureSourceLease lease) => (int)typeof(TextureSourceLease).GetField("useCount", Refl).GetValue(lease);

        private Texture2D NewOwnedTexture(int edge)
        {
            var texture = new Texture2D(edge, edge, TextureFormat.RGBAHalf, false, true);
            ownedTextures.Add(texture);
            return texture;
        }

        private TextureExecutionHandle NewHandle()
        {
            var handle = new TextureExecutionHandle();
            callbackCounts.Add(handle, 0);
            Action<TextureExecutionHandle> callback = _ => callbackCounts[handle]++;
            subscriptions.Add(new KeyValuePair<TextureExecutionHandle, Action<TextureExecutionHandle>>(handle, callback));
            handle.Completed += callback;
            return handle;
        }

        private TextureExecutionPlan Plan(string recipe, Texture source, int edge)
        {
            var document = new MaterialRecipeDocument { wordSource = recipe, outputLogicalName = "out", outputWidth = edge, outputHeight = edge };
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "out", declaredKind = StackMachineBindingKind.Resource });
            document.bindings.Add(new StackMachineBindingDeclaration { logicalName = "a", declaredKind = StackMachineBindingKind.Resource });
            bool success = TextureExecutionPlan.TryCreate(new TextureRecipeStub(document, new[]
            {
                new TextureBindingEntry { logicalName = "out", kind = TextureBindingKind.OutputHall },
                new TextureBindingEntry { logicalName = "a", kind = TextureBindingKind.SourceTexture, sourceTexture = source },
            }), out TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic);
            Assert.That(success, Is.True, diagnostic?.message);
            return plan;
        }

        private bool Admit(TextureExecutionPlan plan, TextureExecutionOriginKey origin,
            TextureExecutionHandle handle, TextureExecutionOptions options, out StackMachineDiagnostic diagnostic)
        {
            object[] args = { plan.DispatchPlan, plan.BindingContext, origin, handle, options, null };
            bool success = (bool)Invoke("TryEnqueuePrepared", args);
            diagnostic = (StackMachineDiagnostic)args[5];
            return success;
        }

        private object FindRequestByHandle(TextureExecutionHandle handle)
        {
            foreach (object request in (IEnumerable)GetField("pending"))
                if (ReferenceEquals(RequestField(request, "handle"), handle)) return request;
            return null;
        }

        private void AssertQueueOrder(params object[] expected)
        {
            var actual = new List<object>();
            foreach (object request in (IEnumerable)GetField("pending")) actual.Add(request);
            Assert.That(actual.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++) Assert.That(actual[i], Is.SameAs(expected[i]));
            Assert.That(((IDictionary)GetField("pendingByOrigin")).Count, Is.EqualTo(expected.Length));
        }

        private void AssertMapEntry(TextureExecutionOriginKey origin, object expected)
            => Assert.That(((IDictionary)GetField("pendingByOrigin"))[origin], Is.SameAs(expected));

        private void AssertPendingAccounting(long expected = C)
        {
            long sum = 0;
            foreach (object request in (IEnumerable)GetField("pending")) sum += (long)RequestField(request, "reservedDeliveryBytes");
            Assert.That(sum, Is.EqualTo(expected));
            Assert.That(GetField("pendingDeliveryBytes"), Is.EqualTo(sum));
        }

        private static void AssertStaticBudget(long grid, long live, long pending, long candidate, long budget, bool expected)
        {
            bool actual = TextureStackMachineHost.TryValidateDeliveryReservation(grid, live, pending, candidate, budget, out _);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private void AssertCandidateHoldsNothing(object request)
        {
            Assert.That(request, Is.Not.Null);
            Assert.That(RequestField(request, "acquiredSourceLease"), Is.Null);
            Assert.That(RequestField(request, "acquiredOutputLease"), Is.Null);
            Assert.That(RequestField(request, "ownedSourceHalls"), Is.Null);
            Assert.That(((Array)RequestField(request, "temporaryPool")).Length, Is.EqualTo(0));
            Assert.That(((TextureHallAllocation)RequestField(request, "ownedOutputHall")).IsValid, Is.False);
            Assert.That(((IDictionary)RequestField(request, "sourceHalls")).Count, Is.EqualTo(0));
        }

        // RejectCommon: the §5.b assert sequence for rejected admissions.
        private void RejectCommon(TextureExecutionHandle handle, ulong version, int occupied, long pendingBytes, int notifications)
        {
            Assert.That(handle.IsCompleted, Is.False);
            Assert.That(handle.Status, Is.EqualTo(TextureExecutionStatus.Queued));
            Assert.That(callbackCounts[handle], Is.EqualTo(0));
            Assert.That(allocator.Version, Is.EqualTo(version));
            Assert.That(allocator.OccupiedRoomCount, Is.EqualTo(occupied));
            Assert.That(GetField("pendingDeliveryBytes"), Is.EqualTo(pendingBytes));
            Assert.That(NotificationQueueCount(), Is.EqualTo(notifications));
            foreach (object request in (IEnumerable)GetField("pending"))
            {
                var pendingHandle = (TextureExecutionHandle)RequestField(request, "handle");
                Assert.That(pendingHandle.IsCompleted, Is.False);
                Assert.That(callbackCounts[pendingHandle], Is.EqualTo(0));
                Assert.That(((IDictionary)GetField("pendingByOrigin"))[RequestField(request, "origin")], Is.SameAs(request));
            }
            AssertPendingAccounting(pendingBytes);
            object submitted = GetField("submitted");
            if (submitted != null) Assert.That(RequestField(submitted, "stale"), Is.EqualTo(false));
        }

        // Cleanup: the §5.d fixed teardown, run on success, assert failure, and SetUp failure.
        private void Cleanup()
        {
            try
            {
                foreach (var pair in subscriptions) pair.Key.Completed -= pair.Value;
                if (host != null)
                {
                    var handles = new List<TextureExecutionHandle>();
                    foreach (object request in (IEnumerable)GetField("pending"))
                        handles.Add((TextureExecutionHandle)RequestField(request, "handle"));
                    foreach (TextureExecutionHandle handle in handles) CancelPrepared(handle);
                }
            }
            finally
            {
                try
                {
                    if (host != null)
                    {
                        SetField("submitted", null);
                        ((HashSet<TextureDelivery>)GetField("outstandingDeliveries")).Clear();
                        // assertion/Cancel例外時も人工prepared要求をlegacy OnDestroyへ渡さない。
                        object queue = GetField("pending");
                        queue.GetType().GetMethod("Clear").Invoke(queue, null);
                        ((IDictionary)GetField("pendingByOrigin")).Clear();
                        SetField("pendingDeliveryBytes", 0L);
                        ((Queue<TextureExecutionHandle>)GetField("notificationQueue")).Clear();
                    }
                }
                finally
                {
                    try { fixtureDelivery?.Dispose(); }
                    finally
                    {
                        try { foreach (TextureSourceLease lease in ownedLeases) lease.Dispose(); }
                        finally
                        {
                            try
                            {
                                foreach (Texture2D texture in ownedTextures)
                                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                            }
                            finally
                            {
                                try { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
                                finally
                                {
                                    fixtureDelivery = null; root = null; host = null; allocator = null;
                                    subscriptions.Clear(); callbackCounts.Clear(); ownedLeases.Clear(); ownedTextures.Clear();
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
