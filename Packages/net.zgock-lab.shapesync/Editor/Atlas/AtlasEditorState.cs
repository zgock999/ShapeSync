// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Editor.Atlas
{
    /// <summary>One selectable Atlas cell occupancy exposed by the Atlas Editor.</summary>
    public enum AtlasEditorCellSelection
    {
        /// <summary>Excludes the candidate from Atlas generation.</summary>
        Ignore,
        /// <summary>Uses one whole page.</summary>
        Whole,
        /// <summary>Uses one quarter page.</summary>
        Quarter,
        /// <summary>Uses a horizontal one-eighth page cell.</summary>
        EighthHorizontal,
        /// <summary>Uses a vertical one-eighth page cell.</summary>
        EighthVertical,
        /// <summary>Uses one sixteenth page.</summary>
        Sixteenth,
        /// <summary>Uses a horizontal 4:1 one-sixteenth page cell.</summary>
        SixteenthHorizontal,
        /// <summary>Uses a vertical 1:4 one-sixteenth page cell.</summary>
        SixteenthVertical,
        /// <summary>Uses a horizontal one-thirty-second page cell.</summary>
        ThirtySecondHorizontal,
        /// <summary>Uses a vertical one-thirty-second page cell.</summary>
        ThirtySecondVertical,
        /// <summary>Uses one sixty-fourth page.</summary>
        SixtyFourth
    }

    /// <summary>One editable Atlas setting paired with its immutable candidate snapshot value.</summary>
    public sealed class AtlasEditorEntryState
    {
        internal AtlasEditorEntryState(AtlasEditorCandidate candidate)
        {
            Candidate = candidate;
            PageGroupingKey = 0;
            CellSelection = AtlasEditorCellSelection.Ignore;
        }

        /// <summary>Gets the click-time candidate value.</summary>
        public AtlasEditorCandidate Candidate { get; }
        /// <summary>Gets the user-entered page grouping key.</summary>
        public int PageGroupingKey { get; internal set; }
        /// <summary>Gets the user-selected cell occupancy.</summary>
        public AtlasEditorCellSelection CellSelection { get; internal set; }
        /// <summary>Gets whether this candidate is excluded from Atlas generation.</summary>
        public bool Excluded => CellSelection == AtlasEditorCellSelection.Ignore;

        /// <summary>Gets the schema cell level X for an included entry.</summary>
        public int CellLevelX => Levels(CellSelection).x;
        /// <summary>Gets the schema cell level Y for an included entry.</summary>
        public int CellLevelY => Levels(CellSelection).y;

        private static Vector2Int Levels(AtlasEditorCellSelection selection)
        {
            switch (selection)
            {
                case AtlasEditorCellSelection.Whole: return new Vector2Int(0, 0);
                case AtlasEditorCellSelection.Quarter: return new Vector2Int(1, 1);
                case AtlasEditorCellSelection.EighthHorizontal: return new Vector2Int(1, 2);
                case AtlasEditorCellSelection.EighthVertical: return new Vector2Int(2, 1);
                case AtlasEditorCellSelection.Sixteenth: return new Vector2Int(2, 2);
                case AtlasEditorCellSelection.SixteenthHorizontal: return new Vector2Int(1, 3);
                case AtlasEditorCellSelection.SixteenthVertical: return new Vector2Int(3, 1);
                case AtlasEditorCellSelection.ThirtySecondHorizontal: return new Vector2Int(2, 3);
                case AtlasEditorCellSelection.ThirtySecondVertical: return new Vector2Int(3, 2);
                case AtlasEditorCellSelection.SixtyFourth: return new Vector2Int(3, 3);
                default: return new Vector2Int(-1, -1);
            }
        }
    }

    /// <summary>Owns Atlas Editor input, candidate editing, and Dry Run gating state without performing validation or asset writes.</summary>
    public sealed class AtlasEditorState
    {
        private static readonly int[] SupportedPageExtents = { 4096, 2048, 1024, 512 };
        private readonly List<AtlasEditorEntryState> entries = new List<AtlasEditorEntryState>();
        private GameObject figure;
        private IShapeSyncDocument document;
        private AtlasEditorCandidateSnapshot snapshot;
        private AtlasLayoutResult layoutPreview;
        private bool isVerified;

        /// <summary>Gets the selected Figure.</summary>
        public GameObject Figure => figure;
        /// <summary>Gets the selected Document.</summary>
        public IShapeSyncDocument Document => document;
        /// <summary>Gets the current page extent. Defaults to 2048.</summary>
        public int PageExtent { get; private set; } = 2048;
        /// <summary>Gets the listed candidate snapshot, or <see langword="null"/> before listing.</summary>
        public AtlasEditorCandidateSnapshot Snapshot => snapshot;
        /// <summary>Gets the non-serialized layout produced by the last successful Dry Run.</summary>
        public AtlasLayoutResult LayoutPreview => layoutPreview;
        /// <summary>Gets editable entries in snapshot ordinal order.</summary>
        public IReadOnlyList<AtlasEditorEntryState> Entries => entries.AsReadOnly();
        /// <summary>Gets whether both source inputs are populated.</summary>
        public bool CanListEntries => figure != null && document != null;
        /// <summary>Gets whether a listed snapshot is available for Dry Run.</summary>
        public bool CanDryRun => snapshot != null;
        /// <summary>Gets whether a successful Dry Run still verifies the current inputs and edits.</summary>
        public bool CanGenerate => isVerified;

        /// <summary>Sets the Figure and discards an existing list only when the source changes.</summary>
        public void SetFigure(GameObject value)
        {
            // UnityEngine.Object == treats a destroyed object as null. Source removal must
            // invalidate the click-time snapshot even when the previously selected Figure has
            // become Unity's "fake null" value, so use CLR reference identity here.
            if (ReferenceEquals(figure, value)) return;
            figure = value;
            ClearListedState();
        }

        /// <summary>Sets the Document and discards an existing list only when the source changes.</summary>
        public void SetDocument(IShapeSyncDocument value)
        {
            if (ReferenceEquals(document, value)) return;
            document = value;
            ClearListedState();
        }

        /// <summary>Lists current candidates and resets all entry settings to page zero and ignore.</summary>
        public bool TryListEntries(out StackMachineDiagnostic diagnostic)
        {
            if (!CanListEntries)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorInputRequired", "Atlas Editor requires both Figure and Document before listing entries.");
                return false;
            }
            if (!AtlasEditorCandidateCollector.TryCollect(figure, document, out AtlasEditorCandidateSnapshot collected, out diagnostic)) return false;
            snapshot = collected;
            entries.Clear();
            for (int i = 0; i < snapshot.Entries.Count; i++) entries.Add(new AtlasEditorEntryState(snapshot.Entries[i]));
            isVerified = false;
            return true;
        }

        /// <summary>Updates one entry setting and invalidates a prior Dry Run when it changes.</summary>
        public bool TrySetEntry(MaterialId materialId, int pageGroupingKey, AtlasEditorCellSelection cellSelection, out StackMachineDiagnostic diagnostic)
        {
            if (!Enum.IsDefined(typeof(AtlasEditorCellSelection), cellSelection))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorCellSelectionInvalid", "Atlas Editor received an unsupported cell selection.", detail: cellSelection.ToString());
                return false;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                AtlasEditorEntryState entry = entries[i];
                if (!entry.Candidate.MaterialId.Equals(materialId)) continue;
                if (entry.PageGroupingKey != pageGroupingKey || entry.CellSelection != cellSelection)
                {
                    entry.PageGroupingKey = pageGroupingKey;
                    entry.CellSelection = cellSelection;
                    layoutPreview = null;
                    isVerified = false;
                }
                diagnostic = null;
                return true;
            }
            diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorEntryMissing", "Atlas Editor cannot edit an entry outside the listed snapshot.", detail: materialId.ToString());
            return false;
        }

        /// <summary>Sets the common page extent and invalidates a prior Dry Run when it changes.</summary>
        public bool TrySetPageExtent(int value, out StackMachineDiagnostic diagnostic)
        {
            bool supported = false;
            for (int i = 0; i < SupportedPageExtents.Length; i++) if (SupportedPageExtents[i] == value) { supported = true; break; }
            if (!supported)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorPageExtentInvalid", "Atlas Editor page extent must be 4096, 2048, 1024, or 512.", detail: value.ToString());
                return false;
            }
            if (PageExtent != value) { PageExtent = value; layoutPreview = null; isVerified = false; }
            diagnostic = null;
            return true;
        }

        /// <summary>Marks current state as verified after a successful Step 3 Dry Run.</summary>
        internal bool TryMarkDryRunSucceeded(out StackMachineDiagnostic diagnostic)
        {
            if (!CanDryRun)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "AtlasEditorEntriesRequired", "Atlas Editor requires listed entries before Dry Run can succeed.");
                return false;
            }
            isVerified = true;
            diagnostic = null;
            return true;
        }

        /// <summary>Stores the non-serialized layout preview produced by the successful Dry Run.</summary>
        internal void SetLayoutPreview(AtlasLayoutResult layout) { layoutPreview = layout; }

        /// <summary>Clears verified state after a failed Step 3 Dry Run.</summary>
        internal void MarkDryRunFailed() { layoutPreview = null; isVerified = false; }

        private void ClearListedState()
        {
            snapshot = null;
            layoutPreview = null;
            entries.Clear();
            isVerified = false;
        }
    }
}
