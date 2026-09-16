// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// State fields and the request record shared across the <see cref="TextureStackMachineHost"/> partial files
    /// (Spec15-2 §5.2, §5.5, §13.5). The MonoBehaviour component body and its serialized fields live in the main file.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        private long pendingDeliveryBytes;
        private bool acceptingRequests;
        private bool lifecycleEndingNotified;
        private readonly Queue<TextureExecutionHandle> notificationQueue = new Queue<TextureExecutionHandle>();
        private bool drainingNotifications;
        private TextureGpuRetirementPump retirementPump;
        private bool destroying;
        private bool faulted;
        private bool closingNotifications;
        private bool resumeRequested;

#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
        // Spec15-2 §13.5: fault-injection contacts for tests only. Game code never sets these.
        internal enum TestFailurePoint
        {
            None,
            BeforeDelivery,
            BeforeExecute,
            AfterExecute,
            FencePoll,
        }

        internal int testFailReservationOrdinal = -1;
        internal TestFailurePoint testFailurePoint = TestFailurePoint.None;
        internal bool testFencePending;
        internal int testLiveProbeCount;
#endif

        private sealed class QueuedRequest
        {
            public TextureDispatchPlan plan;
            public TextureBindingContext context;
            public TextureExecutionOriginKey origin;
            public TextureExecutionHandle handle;
            public bool retainSourceLease;
            public bool retainOutputLease;
            public readonly Dictionary<string, TextureHallAllocation> sourceHalls = new Dictionary<string, TextureHallAllocation>(StringComparer.Ordinal);
            public TextureHallAllocation outputHall;
            public GraphicsFence fence;
            public TextureDelivery delivery;
            public bool stale;
            public bool cancelled;

            // Spec15-2 §5.2: prepared ownership fields. The legacy mixed-ownership fields were removed at the
            // Phase06-26 public-entry switch; one request uses only these prepared fields.
            public TextureRequestHallPlan hallPlan;
            public TextureSourceLease requestedSourceLease;
            public TextureOutputLease requestedOutputLease;
            public TextureSourceLease acquiredSourceLease;
            public TextureOutputLease acquiredOutputLease;
            public List<TextureHallAllocation> ownedSourceHalls;
            public TextureHallAllocation ownedOutputHall;
            public TextureHallAllocation[] temporaryPool = Array.Empty<TextureHallAllocation>();
            public long reservedDeliveryBytes;
            public ulong waitingAllocatorVersion;
            public bool hasWaitingAllocatorVersion;
        }
    }
}
