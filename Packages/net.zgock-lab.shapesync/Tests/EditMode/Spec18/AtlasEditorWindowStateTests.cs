// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor.Atlas;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasEditorWindowStateTests
    {
        private const string AssetFolder = ShapeSyncTestAssetPaths.Spec18AtlasEditorWindowStateRoot;

        [TearDown]
        public void TearDown() { if (AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.DeleteAsset(AssetFolder); }

        [Test]
        public void Window_UsesFigureBuilderSizedViewportAndEqualActionButtonHeight()
        {
            Assert.That(AtlasEditorWindow.DefaultWindowWidth, Is.EqualTo(800f));
            Assert.That(AtlasEditorWindow.DefaultWindowHeight, Is.EqualTo(600f));
            Assert.That(AtlasEditorWindow.ActionButtonHeight, Is.EqualTo(40f));
        }

        [Test]
        public void State_TransitionsInputsListEditsAndVerification()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            ShapeSyncDocumentAsset replacement = PersistentDocument();
            var state = new AtlasEditorState();
            Assert.That(state.CanListEntries, Is.False);
            Assert.That(state.CanDryRun, Is.False);
            Assert.That(state.CanGenerate, Is.False);

            state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.CanListEntries, Is.True);
            Assert.That(state.TryListEntries(out StackMachineDiagnostic listed), Is.True, listed?.message);
            Assert.That(state.CanDryRun, Is.True);
            Assert.That(state.Entries.Count, Is.EqualTo(1));
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(entry.PageGroupingKey, Is.EqualTo(0));
            Assert.That(entry.Excluded, Is.True);
            Assert.That(state.TryMarkDryRunSucceeded(out StackMachineDiagnostic verified), Is.True, verified?.message);
            Assert.That(state.CanGenerate, Is.True);

            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, -7, AtlasEditorCellSelection.Quarter, out StackMachineDiagnostic edited), Is.True, edited?.message);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.Entries[0].CellLevelX, Is.EqualTo(1));
            Assert.That(state.Entries[0].CellLevelY, Is.EqualTo(1));
            Assert.That(state.TryMarkDryRunSucceeded(out _), Is.True);
            Assert.That(state.TrySetPageExtent(1024, out StackMachineDiagnostic extent), Is.True, extent?.message);
            Assert.That(state.PageExtent, Is.EqualTo(1024));
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.CanDryRun, Is.True);

            state.SetDocument(replacement);
            Assert.That(state.CanListEntries, Is.True);
            Assert.That(state.CanDryRun, Is.False);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.Entries, Is.Empty);
        }

        [Test]
        public void State_AllExcludedCanVerifyAndRejectsInvalidEdits()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            Assert.That(state.TryMarkDryRunSucceeded(out _), Is.True);
            Assert.That(state.CanGenerate, Is.True);
            MaterialId missing = new MaterialId(string.Empty, "missing");
            Assert.That(state.TrySetEntry(missing, 0, AtlasEditorCellSelection.Whole, out StackMachineDiagnostic missingEntry), Is.False);
            Assert.That(missingEntry.domainCode, Is.EqualTo("AtlasEditorEntryMissing"));
            Assert.That(state.TrySetEntry(state.Entries[0].Candidate.MaterialId, 0, (AtlasEditorCellSelection)99, out StackMachineDiagnostic invalidSelection), Is.False);
            Assert.That(invalidSelection.domainCode, Is.EqualTo("AtlasEditorCellSelectionInvalid"));
            Assert.That(state.TrySetPageExtent(2049, out StackMachineDiagnostic invalidExtent), Is.False);
            Assert.That(invalidExtent.domainCode, Is.EqualTo("AtlasEditorPageExtentInvalid"));
            Assert.That(state.CanGenerate, Is.True);
        }

        [Test]
        public void State_GatesUnlistedActionsAndClearsWhenFigureIsRemoved()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            GameObject replacement = PersistentFigure("Replacement", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState();

            Assert.That(state.TryListEntries(out StackMachineDiagnostic missingInput), Is.False);
            Assert.That(missingInput.domainCode, Is.EqualTo("AtlasEditorInputRequired"));
            Assert.That(state.TryMarkDryRunSucceeded(out StackMachineDiagnostic unlisted), Is.False);
            Assert.That(unlisted.domainCode, Is.EqualTo("AtlasEditorEntriesRequired"));

            state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            Assert.That(state.TryMarkDryRunSucceeded(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, entry.PageGroupingKey, entry.CellSelection, out _), Is.True);
            Assert.That(state.TrySetPageExtent(state.PageExtent, out _), Is.True);
            state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.CanGenerate, Is.True, "Equivalent source and edit operations preserve verification.");

            state.SetFigure(replacement);
            Assert.That(state.CanListEntries, Is.True);
            Assert.That(state.CanDryRun, Is.False);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.Entries, Is.Empty);

            state.SetFigure(null);
            Assert.That(state.CanListEntries, Is.False);
            Assert.That(state.CanDryRun, Is.False);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.Entries, Is.Empty);
            Assert.That(state.TryListEntries(out missingInput), Is.False);
            Assert.That(missingInput.domainCode, Is.EqualTo("AtlasEditorInputRequired"));
        }

        [Test]
        public void State_MapsEveryCellSelectionAndFailedDryRunKeepsListedState()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            Assert.That(state.TryMarkDryRunSucceeded(out _), Is.True);
            state.MarkDryRunFailed();
            Assert.That(state.CanDryRun, Is.True);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.Entries, Has.Count.EqualTo(1));

            AtlasEditorEntryState entry = state.Entries[0];
            AtlasEditorCellSelection[] selections =
            {
                AtlasEditorCellSelection.Ignore, AtlasEditorCellSelection.Whole, AtlasEditorCellSelection.Quarter,
                AtlasEditorCellSelection.EighthHorizontal, AtlasEditorCellSelection.EighthVertical, AtlasEditorCellSelection.Sixteenth, AtlasEditorCellSelection.SixteenthHorizontal, AtlasEditorCellSelection.SixteenthVertical,
                AtlasEditorCellSelection.ThirtySecondHorizontal, AtlasEditorCellSelection.ThirtySecondVertical, AtlasEditorCellSelection.SixtyFourth
            };
            int[] levelsX = { -1, 0, 1, 1, 2, 2, 1, 3, 2, 3, 3 };
            int[] levelsY = { -1, 0, 1, 2, 1, 2, 3, 1, 3, 2, 3 };
            for (int i = 0; i < selections.Length; i++)
            {
                Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, selections[i], out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(entry.CellLevelX, Is.EqualTo(levelsX[i]), selections[i].ToString());
                Assert.That(entry.CellLevelY, Is.EqualTo(levelsY[i]), selections[i].ToString());
                Assert.That(entry.Excluded, Is.EqualTo(selections[i] == AtlasEditorCellSelection.Ignore));
            }
        }

        [Test]
        public void DryRun_ValidatesLayoutAndOnlyThenEnablesGeneration()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out AtlasLayoutResult excludedLayout, out StackMachineDiagnostic excludedDiagnostic), Is.True, excludedDiagnostic?.message);
            Assert.That(excludedLayout.Cells, Is.Empty);
            Assert.That(state.CanGenerate, Is.True);

            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, -4, AtlasEditorCellSelection.Whole, out _), Is.True);
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out AtlasLayoutResult layout, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(layout.Cells, Has.Count.EqualTo(1));
            Assert.That(layout.Cells[0].PageIndex, Is.EqualTo(0));
            Assert.That(state.LayoutPreview, Is.SameAs(layout));
            Assert.That(state.CanGenerate, Is.True);

            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, -4, AtlasEditorCellSelection.Quarter, out _), Is.True);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out AtlasLayoutResult changedLayout, out StackMachineDiagnostic changedDiagnostic), Is.True, changedDiagnostic?.message);
            Assert.That(state.LayoutPreview, Is.SameAs(changedLayout));
            Assert.That(state.TrySetPageExtent(1024, out _), Is.True);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);

            entry.Candidate.ValidationBinding.renderer.sharedMesh.uv = new[] { new Vector2(-1f, 0f), Vector2.right, Vector2.up };
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out AtlasLayoutResult rejectedLayout, out StackMachineDiagnostic rejected), Is.False);
            Assert.That(rejected.domainCode, Is.EqualTo("AtlasUv0OutOfRange"));
            Assert.That(rejectedLayout, Is.Null);
            Assert.That(state.CanDryRun, Is.True);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);
        }

        [Test]
        public void DryRun_RejectsMissingSemanticPayloadAndKeepsEntriesRetryable()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);
            MaterialProxyEntry binding = entry.Candidate.ValidationBinding;
            MaterialProxySemanticValues values = binding.configuredValues; values.normalTexture = null; binding.configuredValues = values;

            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasNormalRequired"));
            Assert.That(state.CanDryRun, Is.True);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.Entries, Has.Count.EqualTo(1));
            Assert.That(state.LayoutPreview, Is.Null);
        }

        [Test]
        public void DryRun_RejectsOverflowAndKeepsEntriesRetryable()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            MaterialProxy proxy = figure.GetComponent<MaterialProxy>();
            var bindings = (List<MaterialProxyEntry>)proxy.GetType().GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(proxy);
            MaterialProxyEntry first = bindings[0];
            bindings.Add(new MaterialProxyEntry { entryName = "other", renderer = first.renderer, materialChannel = first.materialChannel, adapter = first.adapter, configuredValues = first.configuredValues });
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            Assert.That(state.TrySetEntry(state.Entries[0].Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);
            Assert.That(state.TrySetEntry(state.Entries[1].Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);

            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasPageOverflow"));
            Assert.That(state.CanDryRun, Is.True);
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);
        }

        [Test]
        public void DryRun_RejectsSharedVertexWithEntryContext()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 3, AtlasEditorCellSelection.Whole, out _), Is.True);
            Mesh mesh = entry.Candidate.ValidationBinding.renderer.sharedMesh; mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 1);

            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasSharedVertex"));
            Assert.That(diagnostic.detail, Does.Contain("owner=;materialId=body;submesh=0;pageIndex=0;cause="));
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);
        }

        [Test]
        public void DryRun_RejectsSourceTransformAndClearsVerification()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);
            Material material = entry.Candidate.ValidationBinding.renderer.sharedMaterial; material.SetTextureOffset("_BaseMap", Vector2.right);

            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasMainTextureTilingUnsupported"));
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);
        }

        [Test]
        public void DryRun_RejectsNonOwnedUv0TextureAndClearsVerification()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);
            Material material = entry.Candidate.ValidationBinding.renderer.sharedMaterial;
            material.SetTexture("_DetailAlbedoMap", entry.Candidate.ValidationBinding.configuredValues.baseColorTexture);
            material.EnableKeyword("_DETAIL_MULX2"); material.SetFloat("_DetailUV", 0f);

            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasNonOwnedUv0Texture"));
            Assert.That(diagnostic.detail, Does.Contain("owner=;materialId=body;submesh=0;pageIndex=0;cause=property=_DetailAlbedoMap"));
            Assert.That(state.CanGenerate, Is.False);
            Assert.That(state.LayoutPreview, Is.Null);
        }

        [Test]
        public void DryRun_ResolvesCurrentMaterialWhenDatabaseFigureConfiguredValuesAreEmpty()
        {
            GameObject figure = PersistentFigure("DatabaseFigure", "body", true);
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            MaterialProxyEntry binding = entry.Candidate.ValidationBinding;
            Texture expectedBaseColor = binding.renderer.sharedMaterial.GetTexture("_BaseMap");
            Texture expectedNormal = binding.renderer.sharedMaterial.GetTexture("_BumpMap");
            Assert.That(binding.configuredValues.baseColorTexture, Is.Null);
            Assert.That(binding.configuredValues.normalTexture, Is.Null);
            Assert.That(AtlasEditorMaterialSourceResolver.TryResolve(binding, out Material material, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic sourceDiagnostic), Is.True, sourceDiagnostic.message);
            Assert.That(material, Is.SameAs(binding.renderer.sharedMaterial));
            Assert.That(values.baseColorTexture, Is.SameAs(expectedBaseColor));
            Assert.That(values.normalTexture, Is.SameAs(expectedNormal));
            Assert.That(AtlasEditorMaterialSourceResolver.GetDisplaySourceTexture(values), Is.SameAs(expectedBaseColor));

            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(binding.configuredValues.baseColorTexture, Is.Null, "Dry Run must not write resolved values back into configuredValues.");
            Assert.That(binding.configuredValues.normalTexture, Is.Null, "Dry Run must not write resolved values back into configuredValues.");
        }

        [Test]
        public void DryRun_MergesPartialConfiguredSemanticOverridesWithoutDroppingCurrentTextures()
        {
            GameObject figure = PersistentFigure("PartialOverrideFigure", "body", partialConfiguredValues: true);
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            MaterialProxyEntry binding = entry.Candidate.ValidationBinding;
            Texture expectedBaseColor = binding.renderer.sharedMaterial.GetTexture("_BaseMap");
            Texture expectedNormal = binding.renderer.sharedMaterial.GetTexture("_BumpMap");
            Assert.That(binding.configuredValues.applyColor, Is.True);
            Assert.That(binding.configuredValues.applyBaseColorTexture, Is.False);
            Assert.That(binding.configuredValues.applyNormalTexture, Is.False);
            Assert.That(AtlasEditorMaterialSourceResolver.TryResolve(binding, out _, out MaterialProxySemanticValues values, out MaterialProxyDiagnostic sourceDiagnostic), Is.True, sourceDiagnostic.message);
            Assert.That(values.baseColorTexture, Is.SameAs(expectedBaseColor));
            Assert.That(values.normalTexture, Is.SameAs(expectedNormal));
            Assert.That(values.applyColor, Is.True);
            Assert.That(values.color, Is.EqualTo(Color.magenta));

            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.Whole, out _), Is.True);
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [Test]
        public void SchemaWriter_RequiresVerifiedStateAndPersistsInputOnlySchema()
        {
            GameObject figure = PersistentFigure("Figure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            string output = Path("AtlasSchema", ".asset");
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, output, out _, out StackMachineDiagnostic unverified), Is.False);
            Assert.That(unverified.domainCode, Is.EqualTo("AtlasEditorVerificationRequired"));
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, -2, AtlasEditorCellSelection.Quarter, out _), Is.True);
            Assert.That(AtlasEditorValidationService.TryDryRun(state, out _, out StackMachineDiagnostic dryRun), Is.True, dryRun?.message);
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, output, out AtlasSchema schema, out StackMachineDiagnostic saved), Is.True, saved?.message);
            Assert.That(AssetDatabase.LoadAssetAtPath<AtlasSchema>(output), Is.SameAs(schema));
            AtlasSchemaDocument savedDocument = schema.ToDocument();
            Assert.That(savedDocument.Entries, Has.Count.EqualTo(1));
            Assert.That(savedDocument.Entries[0].PageIndex, Is.EqualTo(-2));
            Assert.That(savedDocument.Entries[0].CellLevelX, Is.EqualTo(1));
            Assert.That(savedDocument.ValidationIdentity.FigureIdentity, Is.EqualTo(state.Snapshot.FigureIdentity));
            AtlasLayoutResult preview = state.LayoutPreview;
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, output, out _, out StackMachineDiagnostic existing), Is.False);
            Assert.That(existing.domainCode, Is.EqualTo("AtlasEditorAssetPathInvalid"));
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, ShapeSyncTestAssetPaths.InvalidAssetPath("AtlasSchema.txt"), out _, out StackMachineDiagnostic extension), Is.False);
            Assert.That(extension.domainCode, Is.EqualTo("AtlasEditorAssetPathInvalid"));
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, "Temp/AtlasSchema.asset", out _, out StackMachineDiagnostic outside), Is.False);
            Assert.That(outside.domainCode, Is.EqualTo("AtlasEditorAssetPathInvalid"));
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, "C:/Temp/AtlasSchema.asset", out _, out StackMachineDiagnostic rooted), Is.False);
            Assert.That(rooted.domainCode, Is.EqualTo("AtlasEditorAssetPathInvalid"));
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, ShapeSyncTestAssetPaths.TraversalAssetPath("Temp/AtlasSchema.asset"), out _, out StackMachineDiagnostic traversal), Is.False);
            Assert.That(traversal.domainCode, Is.EqualTo("AtlasEditorAssetPathInvalid"));
            Assert.That(AtlasEditorSchemaWriter.TryCreateSchemaAsset(state, ShapeSyncTestAssetPaths.InvalidAssetPath("__MissingAtlasSchemaFolder/AtlasSchema.asset"), out _, out StackMachineDiagnostic missingFolder), Is.False);
            Assert.That(missingFolder.domainCode, Is.EqualTo("AtlasEditorAssetPathInvalid"));
            Assert.That(AssetDatabase.LoadAssetAtPath<AtlasSchema>(output), Is.SameAs(schema));
            Assert.That(state.CanGenerate, Is.True);
            Assert.That(state.LayoutPreview, Is.SameAs(preview));
        }

        [Test]
        public void WindowUiHelpers_PreserveDiagnosticDetailAndWarnOnlyForIncludedAspectMismatch()
        {
            Assert.That(AtlasEditorWindow.FormatDiagnostic(StackMachineDiagnostic.CreateDomain("atlas", "code", "message", detail: "owner=body;pageIndex=0")), Is.EqualTo("message\nowner=body;pageIndex=0"));
            GameObject figure = PersistentFigure("AspectFigure", "body");
            ShapeSyncDocumentAsset document = PersistentDocument();
            var state = new AtlasEditorState(); state.SetFigure(figure); state.SetDocument(document);
            Assert.That(state.TryListEntries(out _), Is.True);
            AtlasEditorEntryState entry = state.Entries[0];
            Texture source = entry.Candidate.ValidationBinding.configuredValues.baseColorTexture;
            Assert.That(AtlasEditorWindow.FormatSourceTextureSize(source), Is.EqualTo(source.width + " x " + source.height));
            Assert.That(AtlasEditorWindow.HasAspectMismatch(source, entry), Is.False);
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.EighthHorizontal, out _), Is.True);
            Assert.That(AtlasEditorWindow.HasAspectMismatch(source, entry), Is.True);
            string warning = AtlasEditorWindow.FormatAspectMismatchWarning(source, entry, state.PageExtent);
            Assert.That(warning, Does.Contain(entry.Candidate.MaterialId.ToString()));
            Assert.That(warning, Does.Contain(source.width + " x " + source.height));
            Assert.That(warning, Does.Contain("Atlas cell " + (state.PageExtent >> entry.CellLevelX) + " x " + (state.PageExtent >> entry.CellLevelY)));
            Assert.That(state.TrySetEntry(entry.Candidate.MaterialId, 0, AtlasEditorCellSelection.Ignore, out _), Is.True);
            Assert.That(AtlasEditorWindow.HasAspectMismatch(source, entry), Is.False);
        }

        private static GameObject PersistentFigure(string name, string entryName, bool emptyConfiguredValues = false, bool partialConfiguredValues = false)
        {
            EnsureFolder();
            GameObject root = new GameObject(name); GameObject child = new GameObject("Renderer"); child.transform.SetParent(root.transform);
            SkinnedMeshRenderer renderer = child.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { vertices = new[] { new Vector3(0f, 0f), new Vector3(1f, 0f), new Vector3(0f, 1f) }, uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) }, triangles = new[] { 0, 1, 2 } };
            AssetDatabase.CreateAsset(mesh, Path(name + "Mesh", ".asset")); renderer.sharedMesh = mesh;
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, Path(name + "Material", ".mat")); renderer.sharedMaterial = material;
            Texture2D baseColor = new Texture2D(128, 128); AssetDatabase.CreateAsset(baseColor, Path(name + "BaseColor", ".asset")); material.SetTexture("_BaseMap", baseColor);
            Texture2D normal = new Texture2D(128, 128); AssetDatabase.CreateAsset(normal, Path(name + "Normal", ".asset")); material.SetTexture("_BumpMap", normal);
            UrpLitMaterialShaderAdapter adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); AssetDatabase.CreateAsset(adapter, Path(name + "Adapter", ".asset"));
            MaterialProxy proxy = root.AddComponent<MaterialProxy>();
            MaterialProxySemanticValues configuredValues = emptyConfiguredValues
                ? default
                : partialConfiguredValues
                    ? new MaterialProxySemanticValues { applyColor = true, color = Color.magenta }
                    : new MaterialProxySemanticValues { applyBaseColorTexture = true, baseColorTexture = baseColor, applyNormalTexture = true, normalTexture = normal };
            Set(proxy, "entries", new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = entryName, renderer = renderer, materialChannel = 0, adapter = adapter, configuredValues = configuredValues } });
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Path(name, ".prefab")); Object.DestroyImmediate(root); return prefab;
        }

        private static ShapeSyncDocumentAsset PersistentDocument()
        {
            EnsureFolder();
            MeshBinding binding = ScriptableObject.CreateInstance<MeshBinding>(); AssetDatabase.CreateAsset(binding, Path("Binding", ".asset"));
            ShapeSyncDocumentAsset document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); document.MeshBinding = binding; document.MeshRecipe = new MeshRecipeDocument { wordSource = "DETACH_ALL" }; AssetDatabase.CreateAsset(document, Path("Document", ".asset")); return document;
        }

        private static string Path(string name, string extension) => AssetFolder + "/" + name + "_" + Guid.NewGuid().ToString("N") + extension;
        private static void EnsureFolder() { if (!AssetDatabase.IsValidFolder(AssetFolder)) { ShapeSyncTestAssetPaths.EnsureConsumerTempRoot(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec18AtlasEditorWindowStateTests"); } }
        private static void Set(object value, string field, object fieldValue) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, fieldValue);
    }
}
