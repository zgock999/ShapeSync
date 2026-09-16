// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync.StackMachine
{
    /// <summary>Pure CPU demand and temporary-slot analysis for one request's hall layout (Spec15 §14–§15).</summary>
    /// <remarks>
    /// Immutable after creation. The plan records the ordered demand list (new Sources in
    /// <see cref="Sources"/> order, then new Output, then temporary slots), the per-record resolved
    /// references, and the peak temporary slot count from the two-pass lifetime simulation. It performs no
    /// allocation itself; the host simulates this demand against the live allocator or an occupancy snapshot.
    /// </remarks>
    internal sealed class TextureRequestHallPlan
    {
        /// <summary>Classifies one resolved record reference.</summary>
        internal enum ReferenceKind
        {
            /// <summary>Reference to an entry in <see cref="Sources"/>.</summary>
            Source,
            /// <summary>Reference to the request Output.</summary>
            Output,
            /// <summary>Reference to a temporary pool slot.</summary>
            TemporarySlot,
        }

        /// <summary>Resolved reference to a Source entry, the Output, or a temporary pool slot.</summary>
        internal readonly struct ResolvedReference
        {
            internal ResolvedReference(ReferenceKind kind, int index) { Kind = kind; Index = index; }

            internal ReferenceKind Kind { get; }

            /// <summary>Index into <see cref="Sources"/>, the pool slot index, or -1 for Output.</summary>
            internal int Index { get; }
        }

        /// <summary>One logical source demand in <see cref="Sources"/> order (Spec15-2 §5.1).</summary>
        internal sealed class SourceEntry
        {
            internal SourceEntry(string name, Texture texture, int width, int height)
            {
                Name = name;
                Texture = texture;
                Width = width;
                Height = height;
            }

            internal string Name { get; }

            internal Texture Texture { get; }

            internal int Width { get; }

            internal int Height { get; }
        }

        /// <summary>Gets the ordered source demands in <see cref="TextureDispatchPlan.ReadSourceNames"/> order.</summary>
        internal SourceEntry[] Sources { get; private set; }

        internal int OutputWidth { get; private set; }
        internal int OutputHeight { get; private set; }

        /// <summary>Gets the peak simultaneous temporary slot count (never the total temporary name count).</summary>
        internal int TemporarySlotCount { get; private set; }

        /// <summary>Gets the ordinal map from temporary name to pool slot index. Entries persist after slot release.</summary>
        internal IReadOnlyDictionary<string, int> TemporarySlotByName => temporarySlotByName;

        /// <summary>Gets the ordinal map from source logical name to <see cref="Sources"/> index.</summary>
        internal IReadOnlyDictionary<string, int> SourceIndexByName => sourceIndexByName;

        /// <summary>Gets the resolved destination per dispatch record, in record order.</summary>
        internal ResolvedReference[] RecordDestinations { get; private set; }

        /// <summary>Gets the resolved sources per dispatch record, parallel to record sources, in record order.</summary>
        internal ResolvedReference[][] RecordSources { get; private set; }

        private readonly Dictionary<string, int> temporarySlotByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> sourceIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Builds the immutable hall plan for one request without allocating GPU resources.</summary>
        internal static bool TryCreate(TextureDispatchPlan plan, TextureBindingContext context, out TextureRequestHallPlan hallPlan, out StackMachineDiagnostic diagnostic)
        {
            hallPlan = null;
            if (plan == null || context == null)
            {
                diagnostic = Fail("HallPlanInvalid", "Dispatch plan and binding context are required.", -1);
                return false;
            }

            var result = new TextureRequestHallPlan();
            result.OutputWidth = plan.OutputWidth;
            result.OutputHeight = plan.OutputHeight;

            // Pass 1: ordered source demands and validation.
            var sources = new List<SourceEntry>();
            for (int i = 0; i < plan.ReadSourceNames.Count; i++)
            {
                string name = plan.ReadSourceNames[i];
                if (result.sourceIndexByName.ContainsKey(name))
                {
                    diagnostic = Fail("HallPlanInvalid", "Duplicate source logical name '" + name + "'.", -1);
                    return false;
                }

                if (!context.TryGetBinding(name, out TextureBinding binding))
                {
                    diagnostic = Fail("SourceBindingRequired", "Texture source binding is missing for '" + name + "'.", -1);
                    return false;
                }

                if (binding.Kind != TextureBindingKind.SourceTexture)
                {
                    diagnostic = Fail("SourceBindingRequired", "Logical name '" + name + "' is not a SourceTexture binding.", -1);
                    return false;
                }

                if (binding.SourceTexture == null)
                {
                    diagnostic = Fail("SourceTextureRequired", "Texture source binding requires a Texture for '" + name + "'.", -1);
                    return false;
                }

                if (!TextureGpuCapabilityProbe.IsPhase0Edge(binding.SourceTexture.width) || !TextureGpuCapabilityProbe.IsPhase0Edge(binding.SourceTexture.height))
                {
                    diagnostic = Fail("SourceExtentUnsupported", "Texture source extents must each be supported power-of-two extents for '" + name + "'.", -1);
                    return false;
                }

                result.sourceIndexByName.Add(name, sources.Count);
                sources.Add(new SourceEntry(name, binding.SourceTexture, binding.SourceTexture.width, binding.SourceTexture.height));
            }

            string outputName = context.OutputLogicalName;
            if (!context.TryGetBinding(outputName, out TextureBinding outputBinding) || outputBinding.Kind != TextureBindingKind.OutputHall)
            {
                diagnostic = Fail("HallPlanInvalid", "The reserved OutputHall binding is missing.", -1);
                return false;
            }

            // Definition pass: first-appearance order defines temporary slot registration order (Spec15-2 §6.1).
            var definitionRecord = new Dictionary<string, int>(StringComparer.Ordinal);
            var temporaryOrder = new List<string>();
            for (int i = 0; i < plan.Records.Count; i++)
            {
                string output = plan.Records[i].Output;
                if (output == outputName) continue;
                if (!TexturePlanCompiler.IsTemporary(output))
                {
                    diagnostic = Fail("HallPlanInvalid", "Unknown destination name '" + output + "'.", i);
                    return false;
                }

                if (definitionRecord.ContainsKey(output))
                {
                    diagnostic = Fail("HallPlanInvalid", "Temporary '" + output + "' is defined more than once.", i);
                    return false;
                }

                definitionRecord.Add(output, i);
                temporaryOrder.Add(output);
            }

            // Reference pass with last-use tracking; each temporary registers exactly one release event (Spec15-2 §6.1).
            var lastUseRecord = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> definition in definitionRecord) lastUseRecord[definition.Key] = definition.Value;
            for (int i = 0; i < plan.Records.Count; i++)
            {
                TextureDispatchRecord record = plan.Records[i];
                bool isOutputDestination = record.Output == outputName;
                if (!isOutputDestination && !definitionRecord.ContainsKey(record.Output))
                {
                    diagnostic = Fail("HallPlanInvalid", "Unknown destination name '" + record.Output + "'.", i);
                    return false;
                }

                var countedThisRecord = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < record.Sources.Count; j++)
                {
                    string name = record.Sources[j];
                    if (TexturePlanCompiler.IsTemporary(name))
                    {
                        if (!definitionRecord.TryGetValue(name, out int definition) || definition >= i)
                        {
                            diagnostic = Fail("HallPlanInvalid", "Temporary '" + name + "' is referenced before or within its defining record.", i);
                            return false;
                        }

                        if (countedThisRecord.Add(name) && i > lastUseRecord[name]) lastUseRecord[name] = i;
                    }
                    else if (name == outputName)
                    {
                        if (isOutputDestination)
                        {
                            diagnostic = Fail("HallPlanInvalid", "A record must not read and write the Output in the same record.", i);
                            return false;
                        }
                    }
                    else if (!result.sourceIndexByName.ContainsKey(name))
                    {
                        diagnostic = Fail("HallPlanInvalid", "Unknown source reference '" + name + "'.", i);
                        return false;
                    }
                }
            }

            // Pass 2 (Spec15-2 §6.2/§6.3): freeSlots is a SortedSet taken with Min→Remove, and releaseAtRecord
            // holds each temporary's registration number in its last-use list once. Per record: assign the
            // destination, write the resolved references, and only then return that record's released slots.
            var releaseAtRecord = new List<int>[plan.Records.Count];
            for (int registration = 0; registration < temporaryOrder.Count; registration++)
            {
                int lastUse = lastUseRecord[temporaryOrder[registration]];
                List<int> list = releaseAtRecord[lastUse];
                if (list == null) releaseAtRecord[lastUse] = list = new List<int>();
                list.Add(registration);
            }

            var resolvedDestinations = new ResolvedReference[plan.Records.Count];
            var resolvedSources = new ResolvedReference[plan.Records.Count][];
            var freeSlots = new SortedSet<int>();
            int nextSlot = 0;
            for (int i = 0; i < plan.Records.Count; i++)
            {
                string destination = plan.Records[i].Output;
                if (destination == outputName)
                {
                    resolvedDestinations[i] = new ResolvedReference(ReferenceKind.Output, -1);
                }
                else
                {
                    int slot;
                    if (freeSlots.Count > 0)
                    {
                        int smallest = freeSlots.Min;
                        freeSlots.Remove(smallest);
                        slot = smallest;
                    }
                    else
                    {
                        slot = nextSlot++;
                    }

                    result.temporarySlotByName.Add(destination, slot);
                    resolvedDestinations[i] = new ResolvedReference(ReferenceKind.TemporarySlot, slot);
                }

                var references = new ResolvedReference[plan.Records[i].Sources.Count];
                for (int j = 0; j < references.Length; j++)
                {
                    string name = plan.Records[i].Sources[j];
                    if (TexturePlanCompiler.IsTemporary(name))
                    {
                        references[j] = new ResolvedReference(ReferenceKind.TemporarySlot, result.temporarySlotByName[name]);
                    }
                    else if (name == outputName)
                    {
                        references[j] = new ResolvedReference(ReferenceKind.Output, -1);
                    }
                    else
                    {
                        references[j] = new ResolvedReference(ReferenceKind.Source, result.sourceIndexByName[name]);
                    }
                }

                resolvedSources[i] = references;

                List<int> release = releaseAtRecord[i];
                if (release != null) for (int j = 0; j < release.Count; j++) freeSlots.Add(result.temporarySlotByName[temporaryOrder[release[j]]]);
            }

            result.TemporarySlotCount = nextSlot;
            result.Sources = sources.ToArray();
            result.RecordDestinations = resolvedDestinations;
            result.RecordSources = resolvedSources;
            hallPlan = result;
            diagnostic = null;
            return true;
        }

        private static StackMachineDiagnostic Fail(string code, string message, int instructionPointer)
            => StackMachineDiagnostic.CreateDomain("texture", code, message, instructionPointer: instructionPointer);
    }
}