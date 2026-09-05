// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Read-only terminal outcome for one Figure or Outfit Material target.</summary>
    public sealed class MaterialTargetCompletion
    {
        internal MaterialTargetCompletion(string registryId, MaterialStackMachineResult result) { RegistryId = registryId; Result = result; }
        /// <summary>Gets the empty Figure identity or the Outfit registry identity.</summary>
        public string RegistryId { get; }
        /// <summary>Gets the target terminal result.</summary>
        public MaterialStackMachineResult Result { get; }
    }

    /// <summary>Observes a fixed set of Material target operations without owning them.</summary>
    public sealed class MaterialStackMachineDispatchOperation
    {
        private readonly int expectedCount;
        private readonly MaterialTargetCompletion[] completions;
        private readonly IReadOnlyList<MaterialTargetCompletion> readOnlyCompletions;
        private readonly bool[] terminal;
        private int terminalCount;
        private bool completed;

        internal MaterialStackMachineDispatchOperation(int expectedCount)
        {
            this.expectedCount = expectedCount;
            completions = new MaterialTargetCompletion[expectedCount];
            readOnlyCompletions = Array.AsReadOnly(completions);
            terminal = new bool[expectedCount];
        }

        /// <summary>Gets whether every fixed target has reached a terminal result.</summary>
        public bool IsCompleted => completed;
        /// <summary>Gets terminal results in dispatch order.</summary>
        public IReadOnlyList<MaterialTargetCompletion> TargetCompletions => readOnlyCompletions;
        /// <summary>Raised once after every fixed target reaches a terminal result.</summary>
        public event Action<MaterialStackMachineDispatchOperation> Completed;

        internal void Observe(int targetIndex, string registryId, MaterialStackMachineOperation operation)
        {
            if (operation == null) { Record(targetIndex, registryId, new MaterialStackMachineResult(MaterialStackMachineResultCode.Applied, null)); return; }
            operation.Completed += result => Record(targetIndex, registryId, result);
            if (operation.IsCompleted) Record(targetIndex, registryId, operation.Result);
        }

        internal void Reject(int targetIndex, string registryId, StackMachineDiagnostic diagnostic) => Record(targetIndex, registryId, new MaterialStackMachineResult(MaterialStackMachineResultCode.Rejected, diagnostic));

        internal void Observe(int targetIndex, string registryId, MaterialStackMachineDispatchOperation operation)
        {
            if (operation == null) { Record(targetIndex, registryId, new MaterialStackMachineResult(MaterialStackMachineResultCode.Applied, null)); return; }
            operation.Completed += value => Record(targetIndex, registryId, value.TargetCompletions[0].Result);
            if (operation.IsCompleted) Record(targetIndex, registryId, operation.TargetCompletions[0].Result);
        }

        internal void Seal()
        {
            if (expectedCount == 0) return;
            TryComplete();
        }

        private void Record(int targetIndex, string registryId, MaterialStackMachineResult result)
        {
            if (completed || targetIndex < 0 || targetIndex >= expectedCount || terminal[targetIndex]) return;
            terminal[targetIndex] = true;
            completions[targetIndex] = new MaterialTargetCompletion(registryId, result);
            terminalCount++;
            TryComplete();
        }

        private void TryComplete()
        {
            if (completed || terminalCount != expectedCount) return;
            completed = true;
            Completed?.Invoke(this);
        }
    }
}
