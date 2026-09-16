// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>
    /// The prepared-path notification flush (Spec15-2 §12.5). It shares the existing notificationQueue,
    /// EnqueueNotification, and the lifecycle flags with the legacy flush, which stays wired to the old entrypoints
    /// until 06-26. Normal flushes process only the notifications present at drain start; only the ending batch
    /// (closingNotifications) drains the queue until it is empty. Same loop, no separate recursive drain.
    /// </summary>
    public sealed partial class TextureStackMachineHost
    {
        /// <summary>
        /// Runs the single §12.5 flush loop for the prepared path. A re-entrant call while draining is a no-op.
        /// Normal flushes stop at the queue count observed at drain start; when <c>closingNotifications</c> is set
        /// the batch drains until the queue is empty, then clears the flag and resumes acceptance only when
        /// <c>resumeRequested</c> was raised and the host is actually active, enabled, not destroying, and not
        /// faulted. The finally block always clears <c>drainingNotifications</c>.
        /// </summary>
        private void FlushPreparedNotifications()
        {
            if (drainingNotifications) return;
            drainingNotifications = true;
            int remaining = notificationQueue.Count;
            try
            {
                while (notificationQueue.Count > 0)
                {
                    if (!closingNotifications && remaining == 0) break;
                    TextureExecutionHandle handle = notificationQueue.Dequeue();
                    if (remaining > 0) remaining--;
                    handle.RaiseCompleted();
                }
            }
            finally
            {
                drainingNotifications = false;
                if (closingNotifications && notificationQueue.Count == 0)
                {
                    closingNotifications = false;
                    bool resume = resumeRequested && !destroying && !faulted && isActiveAndEnabled;
                    resumeRequested = false;
                    if (resume)
                    {
                        lifecycleEndingNotified = false;
                        acceptingRequests = initialized;
                    }
                }
            }
        }
    }
}
