// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class AtlasSchemaTests
    {
        [Test]
        public void Validation_AllExcludedEntriesAllowNoPages()
        {
            var document = new AtlasSchemaDocument(
                AtlasSchemaVersion.Current,
                2048,
                AtlasPackingAlgorithm.FirstFitBuddyV1,
                true,
                new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(new MaterialId(string.Empty, "body"), "body-material") }),
                new[] { new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 99, -1, true, -1) });

            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [Test]
        public void Validation_EmptySchemaWithCompleteIdentityIsValid()
        {
            AtlasSchemaDocument document = CreateDocument(2048);

            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [TestCase(512)]
        [TestCase(1024)]
        [TestCase(2048)]
        [TestCase(4096)]
        public void Validation_AcceptsSupportedPageExtent(int pageExtent)
        {
            AtlasSchemaDocument document = CreateDocument(pageExtent, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 7, 1, 2, false));
            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
        }

        [Test]
        public void Validation_RejectsUnsupportedPageExtent()
        {
            AtlasSchemaDocument document = CreateDocument(8192, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 0, 0, false));
            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasPageExtentUnsupported"));
        }

        [Test]
        public void Validation_RejectsNullDocumentAndEmptySourceIdentity()
        {
            Assert.That(AtlasSchemaValidation.TryValidate(null, out StackMachineDiagnostic nullDiagnostic), Is.False);
            Assert.That(nullDiagnostic.domainCode, Is.EqualTo("AtlasSchemaRequired"));

            var id = new MaterialId(string.Empty, "body");
            var document = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, string.Empty) }), new[] { new AtlasSchemaEntry(id, 0, 0, 0, false) });
            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic emptySourceDiagnostic), Is.False);
            Assert.That(emptySourceDiagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityInvalid"));
        }

        [Test]
        public void Validation_RejectsDuplicateMaterialId()
        {
            var id = new MaterialId("outfit", "top");
            var document = new AtlasSchemaDocument(
                AtlasSchemaVersion.Current,
                2048,
                AtlasPackingAlgorithm.FirstFitBuddyV1,
                true,
                new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "top-material") }),
                new[] { new AtlasSchemaEntry(id, 0, 1, 1, false), new AtlasSchemaEntry(id, 1, 1, 1, false) });
            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasMaterialIdDuplicate"));
        }

        [Test]
        public void Validation_RejectsInvalidActiveCellLevelAndGutter()
        {
            AtlasSchemaDocument invalidLevel = CreateDocument(2048, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 4, 0, false));
            Assert.That(AtlasSchemaValidation.TryValidate(invalidLevel, out StackMachineDiagnostic levelDiagnostic), Is.False);
            Assert.That(levelDiagnostic.domainCode, Is.EqualTo("AtlasCellLevelInvalid"));

            AtlasSchemaDocument invalidGutter = CreateDocument(2048, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 0, 0, false, 2));
            Assert.That(AtlasSchemaValidation.TryValidate(invalidGutter, out StackMachineDiagnostic gutterDiagnostic), Is.False);
            Assert.That(gutterDiagnostic.domainCode, Is.EqualTo("AtlasGutterInvalid"));

            AtlasSchemaDocument negativeGutter = CreateDocument(2048, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 0, 0, false, -4));
            Assert.That(AtlasSchemaValidation.TryValidate(negativeGutter, out StackMachineDiagnostic negativeGutterDiagnostic), Is.False);
            Assert.That(negativeGutterDiagnostic.domainCode, Is.EqualTo("AtlasGutterInvalid"));
        }

        [Test]
        public void Validation_RejectsUnsupportedVersionPackingAndNonDeterminism()
        {
            AtlasSchemaDocument valid = CreateDocument(2048, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 0, 0, false));
            var unsupportedVersion = new AtlasSchemaDocument(2, valid.PageExtent, valid.PackingAlgorithm, valid.IsDeterministic, valid.ValidationIdentity, valid.Entries);
            Assert.That(AtlasSchemaValidation.TryValidate(unsupportedVersion, out StackMachineDiagnostic versionDiagnostic), Is.False);
            Assert.That(versionDiagnostic.domainCode, Is.EqualTo("AtlasSchemaVersionUnsupported"));

            var unsupportedPacking = new AtlasSchemaDocument(valid.AtlasSchemaVersion, valid.PageExtent, "other", valid.IsDeterministic, valid.ValidationIdentity, valid.Entries);
            Assert.That(AtlasSchemaValidation.TryValidate(unsupportedPacking, out StackMachineDiagnostic packingDiagnostic), Is.False);
            Assert.That(packingDiagnostic.domainCode, Is.EqualTo("AtlasPackingAlgorithmUnsupported"));

            var nonDeterministic = new AtlasSchemaDocument(valid.AtlasSchemaVersion, valid.PageExtent, valid.PackingAlgorithm, false, valid.ValidationIdentity, valid.Entries);
            Assert.That(AtlasSchemaValidation.TryValidate(nonDeterministic, out StackMachineDiagnostic determinismDiagnostic), Is.False);
            Assert.That(determinismDiagnostic.domainCode, Is.EqualTo("AtlasPackingDeterminismRequired"));
        }

        [Test]
        public void Validation_RequiresCompleteIdentityMappingAndAllowsNegativeGroupingKey()
        {
            AtlasSchemaDocument negativePage = CreateDocument(2048, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), -12, 0, 0, false));
            Assert.That(AtlasSchemaValidation.TryValidate(negativePage, out StackMachineDiagnostic negativePageDiagnostic), Is.True, negativePageDiagnostic?.message);

            var missingFigure = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity(string.Empty, "document"), System.Array.Empty<AtlasSchemaEntry>());
            Assert.That(AtlasSchemaValidation.TryValidate(missingFigure, out StackMachineDiagnostic figureDiagnostic), Is.False);
            Assert.That(figureDiagnostic.domainCode, Is.EqualTo("AtlasFigureIdentityRequired"));

            var entry = new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 0, 0, false);
            var missingSource = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document"), new[] { entry });
            Assert.That(AtlasSchemaValidation.TryValidate(missingSource, out StackMachineDiagnostic sourceDiagnostic), Is.False);
            Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityRequired"));
        }

        [Test]
        public void Validation_RequiresProvenanceForExcludedEntry()
        {
            var entry = new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 99, -1, true, -1);
            var document = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document"), new[] { entry });

            Assert.That(AtlasSchemaValidation.TryValidate(document, out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityRequired"));
        }

        [Test]
        public void ValidationIdentity_ReportsAllComparisonBoundaryBranches()
        {
            var id = new MaterialId("outfit", "top");
            var recorded = new AtlasValidationIdentity("figure:ABC", "document:123", new[] { new AtlasSourceMaterialIdentity(id, "material:XYZ") });
            var same = new AtlasValidationIdentity("figure:ABC", "document:123", new[] { new AtlasSourceMaterialIdentity(id, "material:XYZ") });
            Assert.That(recorded.TryMatchCurrent(same, out AtlasValidationIdentityDifference sameDifference), Is.True);
            Assert.That(sameDifference, Is.Null);

            var changed = new AtlasValidationIdentity("figure:abc", "document:123", new[] { new AtlasSourceMaterialIdentity(id, "material:XYZ") });
            Assert.That(recorded.TryMatchCurrent(changed, out AtlasValidationIdentityDifference figureDifference), Is.False);
            Assert.That(figureDifference.Kind, Is.EqualTo(AtlasValidationIdentityDifferenceKind.Figure));
            Assert.That(figureDifference.ExpectedIdentity, Is.EqualTo("figure:ABC"));
            Assert.That(figureDifference.ActualIdentity, Is.EqualTo("figure:abc"));

            var documentChanged = new AtlasValidationIdentity("figure:ABC", "document:456", new[] { new AtlasSourceMaterialIdentity(id, "material:XYZ") });
            Assert.That(recorded.TryMatchCurrent(documentChanged, out AtlasValidationIdentityDifference documentDifference), Is.False);
            Assert.That(documentDifference.Kind, Is.EqualTo(AtlasValidationIdentityDifferenceKind.Document));

            var sourceChanged = new AtlasValidationIdentity("figure:ABC", "document:123", new[] { new AtlasSourceMaterialIdentity(id, "material:xyz") });
            Assert.That(recorded.TryMatchCurrent(sourceChanged, out AtlasValidationIdentityDifference sourceChangedDifference), Is.False);
            Assert.That(sourceChangedDifference.Kind, Is.EqualTo(AtlasValidationIdentityDifferenceKind.SourceMaterialChanged));
            Assert.That(sourceChangedDifference.MaterialId, Is.EqualTo(id));

            var missingSource = new AtlasValidationIdentity("figure:ABC", "document:123");
            Assert.That(recorded.TryMatchCurrent(missingSource, out AtlasValidationIdentityDifference sourceDifference), Is.True);
            Assert.That(sourceDifference, Is.Null);

            Assert.That(recorded.TryMatchCurrent(null, out AtlasValidationIdentityDifference nullDifference), Is.False);
            Assert.That(nullDifference.Kind, Is.EqualTo(AtlasValidationIdentityDifferenceKind.CurrentIdentityMissing));

            var duplicateSource = new AtlasValidationIdentity("figure:ABC", "document:123", new[] { new AtlasSourceMaterialIdentity(id, "material:XYZ"), new AtlasSourceMaterialIdentity(id, "material:XYZ") });
            Assert.That(recorded.TryMatchCurrent(duplicateSource, out AtlasValidationIdentityDifference invalidSourceDifference), Is.False);
            Assert.That(invalidSourceDifference.Kind, Is.EqualTo(AtlasValidationIdentityDifferenceKind.CurrentSourceMaterialIdentityInvalid));

            var emptySource = new AtlasValidationIdentity("figure:ABC", "document:123", new[] { new AtlasSourceMaterialIdentity(id, " ") });
            Assert.That(recorded.TryMatchCurrent(emptySource, out AtlasValidationIdentityDifference emptySourceDifference), Is.False);
            Assert.That(emptySourceDifference.Kind, Is.EqualTo(AtlasValidationIdentityDifferenceKind.CurrentSourceMaterialIdentityInvalid));
        }

        [Test]
        public void Validation_RejectsIncompleteAndInvalidSourceMaterialMappings()
        {
            var id = new MaterialId(string.Empty, "body");
            var entry = new AtlasSchemaEntry(id, 0, 0, 0, false);

            var nullSource = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new AtlasSourceMaterialIdentity[] { null }), new[] { entry });
            Assert.That(AtlasSchemaValidation.TryValidate(nullSource, out StackMachineDiagnostic nullDiagnostic), Is.False);
            Assert.That(nullDiagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityMissing"));

            var duplicateSource = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "material-A"), new AtlasSourceMaterialIdentity(id, "material-B") }), new[] { entry });
            Assert.That(AtlasSchemaValidation.TryValidate(duplicateSource, out StackMachineDiagnostic duplicateDiagnostic), Is.False);
            Assert.That(duplicateDiagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityInvalid"));

            var orphanSource = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "material-A"), new AtlasSourceMaterialIdentity(new MaterialId("outfit", "top"), "material-B") }), new[] { entry });
            Assert.That(AtlasSchemaValidation.TryValidate(orphanSource, out StackMachineDiagnostic orphanDiagnostic), Is.False);
            Assert.That(orphanDiagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityOrphaned"));
        }

        [Test]
        public void Validation_RejectsMissingDocumentNullEntryAndInvalidMaterialId()
        {
            var missingDocument = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", string.Empty), System.Array.Empty<AtlasSchemaEntry>());
            Assert.That(AtlasSchemaValidation.TryValidate(missingDocument, out StackMachineDiagnostic documentDiagnostic), Is.False);
            Assert.That(documentDiagnostic.domainCode, Is.EqualTo("AtlasDocumentIdentityRequired"));

            var nullEntry = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document"), new AtlasSchemaEntry[] { null });
            Assert.That(AtlasSchemaValidation.TryValidate(nullEntry, out StackMachineDiagnostic entryDiagnostic), Is.False);
            Assert.That(entryDiagnostic.domainCode, Is.EqualTo("AtlasEntryMissing"));

            var invalidId = new AtlasSchemaEntry(new MaterialId("outfit", string.Empty), 0, 0, 0, false);
            var invalidMaterial = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 2048, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document"), new[] { invalidId });
            Assert.That(AtlasSchemaValidation.TryValidate(invalidMaterial, out StackMachineDiagnostic materialDiagnostic), Is.False);
            Assert.That(materialDiagnostic.domainCode, Is.EqualTo("AtlasMaterialIdInvalid"));
        }

        [Test]
        public void Validation_ToleratesNullSerializedCollections()
        {
            var id = new MaterialId(string.Empty, "body");
            var sourceMissing = CreateDocument(2048, new AtlasSchemaEntry(id, 0, 0, 0, false));
            AtlasValidationIdentity sourceIdentity = GetPrivateField<AtlasValidationIdentity>(sourceMissing, "validationIdentity");
            SetPrivateField(sourceIdentity, "sourceMaterials", null);
            Assert.That(AtlasSchemaValidation.TryValidate(sourceMissing, out StackMachineDiagnostic sourceDiagnostic), Is.False);
            Assert.That(sourceDiagnostic.domainCode, Is.EqualTo("AtlasSourceMaterialIdentityRequired"));

            AtlasSchemaDocument empty = CreateDocument(2048);
            SetPrivateField(empty, "entries", null);
            Assert.That(AtlasSchemaValidation.TryValidate(empty, out StackMachineDiagnostic emptyDiagnostic), Is.True, emptyDiagnostic?.message);
        }

        [Test]
        public void Carrier_StoresOnlyDeepCopiedUserInput()
        {
            AtlasSchema carrier = ScriptableObject.CreateInstance<AtlasSchema>();
            try
            {
                var input = new AtlasSchemaDocument(
                    AtlasSchemaVersion.Current,
                    1024,
                    AtlasPackingAlgorithm.FirstFitBuddyV1,
                    true,
                    new AtlasValidationIdentity("figure-A", "document-A", new[] { new AtlasSourceMaterialIdentity(new MaterialId("outfit", "top"), "material-A") }),
                    new[] { new AtlasSchemaEntry(new MaterialId("outfit", "top"), 13, 2, 1, false) });
                Assert.That(carrier.TrySetDocument(input, out StackMachineDiagnostic setDiagnostic), Is.True, setDiagnostic?.message);

                AtlasSchemaDocument first = carrier.ToDocument();
                AtlasSchemaDocument second = carrier.ToDocument();
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(first.Entries[0], Is.Not.SameAs(second.Entries[0]));
                Assert.That(first.Entries[0].MaterialId, Is.Not.SameAs(second.Entries[0].MaterialId));
                Assert.That(first.ValidationIdentity.SourceMaterials[0], Is.Not.SameAs(second.ValidationIdentity.SourceMaterials[0]));
                Assert.That(first.ValidationIdentity.SourceMaterials[0].MaterialId, Is.Not.SameAs(second.ValidationIdentity.SourceMaterials[0].MaterialId));
                Assert.That(second.Entries[0].MaterialId.ToMaterialId(), Is.EqualTo(new MaterialId("outfit", "top")));
                Assert.That(second.Entries[0].PageIndex, Is.EqualTo(13));
                Assert.That(second.ValidationIdentity.SourceMaterials[0].SourceMaterialIdentity, Is.EqualTo("material-A"));
                string serialized = JsonUtility.ToJson(first);
                Assert.That(serialized, Does.Contain("pageIndex"));
                Assert.That(serialized, Does.Not.Contain("layout"));
                Assert.That(serialized, Does.Not.Contain("rect"));
            }
            finally
            {
                Object.DestroyImmediate(carrier);
            }
        }

        [Test]
        public void Carrier_DefaultsMatchSpecAndRejectDoesNotMutateStoredDocument()
        {
            AtlasSchema carrier = ScriptableObject.CreateInstance<AtlasSchema>();
            try
            {
                Assert.That(carrier.AtlasSchemaVersion, Is.EqualTo(AtlasSchemaVersion.Current));
                Assert.That(carrier.PageExtent, Is.EqualTo(2048));
                Assert.That(carrier.PackingAlgorithm, Is.EqualTo(AtlasPackingAlgorithm.FirstFitBuddyV1));
                Assert.That(carrier.IsDeterministic, Is.True);

                AtlasSchemaDocument valid = CreateDocument(1024, new AtlasSchemaEntry(new MaterialId(string.Empty, "body"), 0, 0, 0, false));
                Assert.That(carrier.TrySetDocument(valid, out StackMachineDiagnostic setDiagnostic), Is.True, setDiagnostic?.message);
                AtlasSchemaDocument invalid = new AtlasSchemaDocument(2, valid.PageExtent, valid.PackingAlgorithm, true, valid.ValidationIdentity, valid.Entries);
                Assert.That(carrier.TrySetDocument(invalid, out StackMachineDiagnostic invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic.domainCode, Is.EqualTo("AtlasSchemaVersionUnsupported"));
                Assert.That(carrier.ToDocument().PageExtent, Is.EqualTo(1024));
            }
            finally
            {
                Object.DestroyImmediate(carrier);
            }
        }

        [Test]
        public void Carrier_CorruptSerializedFieldsProduceStructuredValidationFailure()
        {
            AtlasSchema carrier = ScriptableObject.CreateInstance<AtlasSchema>();
            try
            {
                SetPrivateField(carrier, "validationIdentity", null);
                SetPrivateField(carrier, "entries", null);
                AtlasSchemaDocument corrupted = carrier.ToDocument();

                Assert.That(AtlasSchemaValidation.TryValidate(corrupted, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasFigureIdentityRequired"));
                Assert.That(carrier.TrySetDocument(corrupted, out StackMachineDiagnostic setDiagnostic), Is.False);
                Assert.That(setDiagnostic.domainCode, Is.EqualTo("AtlasFigureIdentityRequired"));
            }
            finally
            {
                Object.DestroyImmediate(carrier);
            }
        }

        private static AtlasSchemaDocument CreateDocument(int pageExtent, params AtlasSchemaEntry[] entries)
        {
            var sources = new AtlasSourceMaterialIdentity[entries.Length];
            for (int i = 0; i < entries.Length; i++) sources[i] = new AtlasSourceMaterialIdentity(entries[i].MaterialId.ToMaterialId(), "material-" + i);
            return new AtlasSchemaDocument(AtlasSchemaVersion.Current, pageExtent, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", sources), entries);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }
    }
}
