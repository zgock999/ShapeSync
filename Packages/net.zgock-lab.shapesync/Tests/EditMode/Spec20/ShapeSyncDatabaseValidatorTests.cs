// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Editor;
using Object = UnityEngine.Object;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    public sealed class ShapeSyncDatabaseValidatorTests
    {
        [Test]
        public void Validate_NullDatabaseAndMissingRegistryAreStructured()
        {
            Assert.That(ShapeSyncDatabaseValidator.TryValidate(null, out IReadOnlyList<ShapeSyncDatabaseDiagnostic> nullDiagnostics), Is.False);
            Assert.That(nullDiagnostics, Has.Count.EqualTo(1));
            Assert.That(nullDiagnostics[0].Code, Is.EqualTo(ShapeSyncDatabaseDiagnosticCode.DatabaseRequired));
            Assert.That(nullDiagnostics[0].EntityKind, Is.EqualTo(ShapeSyncDatabaseEntityKind.Database));

            GameObject rootObject = new GameObject("Database");
            try
            {
                ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
                Assert.That(ShapeSyncDatabaseValidator.TryValidate(root, out IReadOnlyList<ShapeSyncDatabaseDiagnostic> registryDiagnostics), Is.False);
                Assert.That(registryDiagnostics, Has.Count.EqualTo(1));
                Assert.That(registryDiagnostics[0].Code, Is.EqualTo(ShapeSyncDatabaseDiagnosticCode.RegistryRequired));
                Assert.That(registryDiagnostics[0].RelationKind, Is.EqualTo(ShapeSyncDatabaseRelationKind.Registry));
                Assert.That(registryDiagnostics[0].ToStackMachineDiagnostic().domainCode, Is.EqualTo("RegistryRequired"));
            }
            finally { Object.DestroyImmediate(rootObject); }
        }

        [Test]
        public void Validate_EmptyDatabaseHasNoDeclaredRelationFalsePositive()
        {
            ShapeSyncDatabase root = new GameObject("Database").AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            try
            {
                Assert.That(ShapeSyncDatabaseValidator.TryValidate(root, out IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics), Is.True);
                Assert.That(diagnostics, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void TryRegisterBaseFigure_RejectsSecondFigureAndValidatorLocalizesPersistedCardinality()
        {
            GameObject rootObject = new GameObject("Database");
            ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            GameObject intermediate = new GameObject("Intermediate");
            intermediate.transform.SetParent(root.transform, false);
            GameObject first = new GameObject("Base");
            first.transform.SetParent(intermediate.transform, false);
            GameObject second = new GameObject("OtherBase");
            second.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(root, "Base", first, out string firstDiagnostic), Is.True, firstDiagnostic);
                Assert.That(registry.TryRegisterBaseFigure(root, "OtherBase", second, out string secondDiagnostic), Is.False);
                Assert.That(secondDiagnostic, Does.Contain("EntityCardinality"));

                FieldInfo field = typeof(ShapeSyncDatabaseRegistry).GetField("baseFigures", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                var entries = (List<ShapeSyncDatabaseRegistry.BaseFigureEntry>)field.GetValue(registry);
                entries.Add(new ShapeSyncDatabaseRegistry.BaseFigureEntry("OtherBase", second));
                IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics = ShapeSyncDatabaseValidator.Validate(root);
                ShapeSyncDatabaseDiagnostic cardinality = diagnostics.Single(item => item.Code == ShapeSyncDatabaseDiagnosticCode.EntityCardinality);
                Assert.That(cardinality.EntityKind, Is.EqualTo(ShapeSyncDatabaseEntityKind.BaseFigure));
                Assert.That(cardinality.RelationKind, Is.EqualTo(ShapeSyncDatabaseRelationKind.BaseFigure));
                Assert.That(cardinality.EntityId, Is.EqualTo("Base"));
                Assert.That(cardinality.ToStackMachineDiagnostic().domain, Is.EqualTo("database"));
                Assert.That(cardinality.ToStackMachineDiagnostic().domainCode, Is.EqualTo("EntityCardinality"));
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void Validate_ReportsDeclaredMaterialRelationWithoutMutatingDatabase()
        {
            GameObject rootObject = new GameObject("Database");
            ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            GameObject intermediate = new GameObject("Intermediate");
            intermediate.transform.SetParent(root.transform, false);
            GameObject figure = new GameObject("Base");
            figure.transform.SetParent(intermediate.transform, false);
            try
            {
                Assert.That(registry.TryRegisterBaseFigure(root, "Base", figure, out string baseDiagnostic), Is.True, baseDiagnostic);
                FieldInfo field = typeof(ShapeSyncDatabaseRegistry).GetField("materialEntries", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                var entries = (List<ShapeSyncDatabaseRegistry.MaterialEntry>)field.GetValue(registry);
                entries.Add(new ShapeSyncDatabaseRegistry.MaterialEntry("Body", null, "", 0, "Body", null, null));

                IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics = ShapeSyncDatabaseValidator.Validate(root);
                ShapeSyncDatabaseDiagnostic relation = diagnostics.Single(item => item.EntityKind == ShapeSyncDatabaseEntityKind.MaterialEntry);
                Assert.That(relation.Code, Is.EqualTo(ShapeSyncDatabaseDiagnosticCode.RelationMissing));
                Assert.That(relation.RelationKind, Is.EqualTo(ShapeSyncDatabaseRelationKind.MaterialTarget));
                Assert.That(registry.BaseFigures, Has.Count.EqualTo(1));
                Assert.That(registry.BaseFigures[0].Figure, Is.SameAs(figure));
                Assert.That(registry.MaterialEntries, Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void Validate_ReportsAxisBindingAndOutfitTargetRelations()
        {
            GameObject rootObject = new GameObject("Database");
            ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            try
            {
                AddRegistryItem(registry, "figureAxes", new ShapeSyncDatabaseRegistry.FigureAxisEntry(
                    "Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm));
                ShapeSyncDatabaseRegistry.OutfitEntry outfit = new ShapeSyncDatabaseRegistry.OutfitEntry(
                    "Coat", "Coat", ShapeSyncDatabaseRegistry.OutfitKind.Mesh);
                outfit.SetAxisFigures(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry("Unknown", null, null, null, null, null)
                });
                AddRegistryItem(registry, "outfits", outfit);

                IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics = ShapeSyncDatabaseValidator.Validate(root);
                Assert.That(diagnostics.Any(item => item.EntityKind == ShapeSyncDatabaseEntityKind.FigureAxis
                    && item.RelationKind == ShapeSyncDatabaseRelationKind.AxisFigure
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationMissing), Is.True);
                Assert.That(diagnostics.Any(item => item.EntityKind == ShapeSyncDatabaseEntityKind.Outfit
                    && item.RelationKind == ShapeSyncDatabaseRelationKind.FigureAxis
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing
                    && item.TargetId == "Unknown"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void Validate_ReportsTextureOwnerNormalAndDuplicateIdentityRelations()
        {
            GameObject rootObject = new GameObject("Database");
            ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            try
            {
                AddRegistryItem(registry, "textureResources", new ShapeSyncDatabaseRegistry.TextureResourceEntry(
                    "BodyTexture", null, ShapeSyncDatabaseRegistry.TextureResourceOwner.Outfit("MissingOutfit")));
                AddRegistryItem(registry, "normalEntries", new ShapeSyncDatabaseRegistry.NormalEntry(
                    "MissingMaterial", "MissingAxis", "MissingTexture", null));
                AddRegistryItem(registry, "materialEntries", new ShapeSyncDatabaseRegistry.MaterialEntry(
                    "Duplicate", null, string.Empty, 0, "Duplicate", null, null));
                AddRegistryItem(registry, "materialEntries", new ShapeSyncDatabaseRegistry.MaterialEntry(
                    "Duplicate", null, string.Empty, 0, "Duplicate", null, null));

                IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics = ShapeSyncDatabaseValidator.Validate(root);
                Assert.That(diagnostics.Any(item => item.EntityKind == ShapeSyncDatabaseEntityKind.TextureResource
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing
                    && item.TargetId == "MissingOutfit"), Is.True);
                Assert.That(diagnostics.Any(item => item.EntityKind == ShapeSyncDatabaseEntityKind.NormalEntry
                    && item.RelationKind == ShapeSyncDatabaseRelationKind.NormalTarget
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing), Is.True);
                Assert.That(diagnostics.Any(item => item.EntityKind == ShapeSyncDatabaseEntityKind.MaterialEntry
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.EntityDuplicate), Is.True);
                Assert.That(diagnostics, Is.Ordered.By(nameof(ShapeSyncDatabaseDiagnostic.EntityKind)));
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void Validate_UsesShapeAdmissionForMorphTagsAndPartsTargets()
        {
            GameObject rootObject = new GameObject("Database");
            ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            try
            {
                Assert.That(registry.TrySetShapeTags(new[] { "Skin" }, out string tagDiagnostic), Is.True, tagDiagnostic);
                ShapeSyncDatabaseRegistry.ShapeEntry morph = new ShapeSyncDatabaseRegistry.ShapeEntry(
                    "Morph01", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, null);
                SetPrivateField(morph, "tags", new List<string> { "Skin" });
                AddRegistryItem(registry, "shapes", morph);

                ShapeSyncDatabaseRegistry.ShapeEntry skin = new ShapeSyncDatabaseRegistry.ShapeEntry(
                    "Skin01", "Skin", ShapeSyncDatabaseRegistry.ShapeKind.Skin, 0, new[] { "UnknownTag" });
                skin.AddPart(new ShapeSyncDatabaseRegistry.ShapeEntryDefinition(ShapeSyncDatabaseRegistry.ShapeEntryKind.Mesh));
                AddRegistryItem(registry, "shapes", skin);

                IReadOnlyList<ShapeSyncDatabaseDiagnostic> diagnostics = ShapeSyncDatabaseValidator.Validate(root);
                Assert.That(diagnostics.Any(item => item.EntityId == "Morph01"
                    && item.TargetId == "Tags"
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing), Is.True);
                Assert.That(diagnostics.Any(item => item.EntityId == "Skin01"
                    && item.TargetId == "Tags"
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing), Is.True);
                Assert.That(diagnostics.Any(item => item.EntityId == "Skin01"
                    && item.TargetId == "Parts"
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationTargetMissing), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void ValidateForGeneration_MorphRequiresEveryFigureAxisAndAcceptsExplicitZeroRows()
        {
            GameObject rootObject = new GameObject("Database");
            ShapeSyncDatabase root = rootObject.AddComponent<ShapeSyncDatabase>();
            ShapeSyncDatabaseRegistry registry = ScriptableObject.CreateInstance<ShapeSyncDatabaseRegistry>();
            root.SetRegistryForAuthoring(registry);
            GameObject intermediate = new GameObject("Intermediate");
            intermediate.transform.SetParent(root.transform, false);
            GameObject figure = new GameObject("Smile");
            figure.transform.SetParent(intermediate.transform, false);
            try
            {
                AddRegistryItem(registry, "figureAxes", new ShapeSyncDatabaseRegistry.FigureAxisEntry(
                    "Smile", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm,
                    new[] { new ShapeSyncDatabaseRegistry.AxisFigureEntry("Smile", figure) }));
                ShapeSyncDatabaseRegistry.ShapeEntry morph = new ShapeSyncDatabaseRegistry.ShapeEntry(
                    "Morph01", "Morph", ShapeSyncDatabaseRegistry.ShapeKind.Morph, 0, null);
                AddRegistryItem(registry, "shapes", morph);

                IReadOnlyList<ShapeSyncDatabaseDiagnostic> missing = ShapeSyncDatabaseValidator.ValidateForGeneration(root);
                Assert.That(missing.Any(item => item.EntityId == "Morph01" && item.TargetId == "Smile"
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationMissing), Is.True);

                morph.SetMorphs(new[] { new MorphValue { Target = "Smile", Value = 0f } });
                IReadOnlyList<ShapeSyncDatabaseDiagnostic> complete = ShapeSyncDatabaseValidator.ValidateForGeneration(root);
                Assert.That(complete.Any(item => item.EntityId == "Morph01" && item.TargetId == "Smile"
                    && item.Code == ShapeSyncDatabaseDiagnosticCode.RelationMissing), Is.False);
                Assert.That(morph.Morphs.Single(value => value.Target == "Smile").Value, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(registry);
                Object.DestroyImmediate(rootObject);
            }
        }

        private static void AddRegistryItem<T>(ShapeSyncDatabaseRegistry registry, string fieldName, T value)
        {
            FieldInfo field = typeof(ShapeSyncDatabaseRegistry).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            ((List<T>)field.GetValue(registry)).Add(value);
        }

        private static void SetPrivateField<T>(T target, string fieldName, object value)
        {
            FieldInfo field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
#endif
