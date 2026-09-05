// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using Activator = System.Activator;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    public sealed class AtlasCompilerInputTests
    {
        [Test]
        public void OptionalSchemaSnapshot_RejectsInvalidAssetAndDeepCopiesValidDocument()
        {
            MethodInfo snapshot = ControllerType.GetMethod("TryCreateAtlasSchemaSnapshot", BindingFlags.Static | BindingFlags.NonPublic);
            var invalid = ScriptableObject.CreateInstance<AtlasSchema>();
            var valid = ScriptableObject.CreateInstance<AtlasSchema>();
            try
            {
                object[] invalidArguments = { invalid, null, null };
                Assert.That((bool)snapshot.Invoke(null, invalidArguments), Is.False);
                Assert.That(((StackMachineDiagnostic)invalidArguments[2]).domain, Is.EqualTo("atlas"));

                var id = new MaterialId("", "body");
                var document = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source:body") }), new[] { new AtlasSchemaEntry(id, 0, 2, 2, false, 0) });
                Assert.That(valid.TrySetDocument(document, out StackMachineDiagnostic setDiagnostic), Is.True, setDiagnostic?.message);
                object[] validArguments = { valid, null, null };
                Assert.That((bool)snapshot.Invoke(null, validArguments), Is.True, validArguments[2] as StackMachineDiagnostic == null ? string.Empty : ((StackMachineDiagnostic)validArguments[2]).message);
                var copied = (AtlasSchemaDocument)validArguments[1];
                Assert.That(copied, Is.Not.SameAs(document));
                Assert.That(copied.Entries[0], Is.Not.SameAs(valid.Entries[0]));
            }
            finally { Object.DestroyImmediate(invalid); Object.DestroyImmediate(valid); }
        }

        [Test]
        public void CompilerWindow_ExposesOptionalAtlasSchemaSerializedInput()
        {
            var window = ScriptableObject.CreateInstance<HumanoidCompilerWindow>();
            try
            {
                var serialized = new SerializedObject(window);
                Assert.That(serialized.FindProperty("atlasSchema"), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void AtlasOn_StartStoresDetachedSnapshotAndCancelClearsIt()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                AtlasSchemaDocument snapshot = (AtlasSchemaDocument)GetProperty(controller, "AtlasSchema");
                Assert.That(snapshot, Is.Not.Null);
                Assert.That(snapshot, Is.Not.SameAs(schema.ToDocument()));
                Invoke(controller, "Cancel");
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.Null);
                Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.Null);
                Assert.That(GetProperty(controller, "SourceDocument"), Is.Null);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_RejectsNullOrInvalidSchemaWithoutStartingTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var invalid = ScriptableObject.CreateInstance<AtlasSchema>();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, null, out StackMachineDiagnostic nullDiagnostic), Is.False);
                Assert.That(nullDiagnostic.domainCode, Is.EqualTo("AtlasSchemaRequired"));
                AssertTransactionNotStarted(controller);

                Assert.That(InvokeStartWithAtlas(controller, figure, document, invalid, out StackMachineDiagnostic invalidDiagnostic), Is.False);
                Assert.That(invalidDiagnostic.domain, Is.EqualTo("atlas"));
                AssertTransactionNotStarted(controller);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(invalid); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_BackendRejectClearsSnapshotAndLeavesNoTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            object controller = CreateController(() => new RejectingBackend());
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("TestAtlasBackendRejected"));
                AssertTransactionNotStarted(controller);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_BackendCreationFailureClearsSnapshotAndLeavesNoTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            object controller = CreateController(() => null);
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("EditorBuildBackendRequired"));
                AssertTransactionNotStarted(controller);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_DisposeClearsLiveSnapshotAndTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                ((System.IDisposable)controller).Dispose();
                AssertTransactionNotStarted(controller);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_DisposedControllerRejectsLifetimeBeforeSchemaValidation()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                ((System.IDisposable)controller).Dispose();
                Assert.That(InvokeStartWithAtlas(controller, figure, document, null, out StackMachineDiagnostic nullDiagnostic), Is.False);
                Assert.That(nullDiagnostic.domainCode, Is.EqualTo("EditorBuildControllerDisposed"));
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic schemaDiagnostic), Is.False);
                Assert.That(schemaDiagnostic.domainCode, Is.EqualTo("EditorBuildControllerDisposed"));
                AssertTransactionNotStarted(controller);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_BusyStartRejectsWithoutReplacingActiveTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var firstSchema = CreateValidSchema();
            var secondSchema = CreateValidSchema();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, firstSchema, out StackMachineDiagnostic firstDiagnostic), Is.True, firstDiagnostic?.message);
                object firstSnapshot = GetProperty(controller, "AtlasSchema");
                object firstFigure = GetProperty(controller, "FigureSourceRoot");
                object firstDocument = GetProperty(controller, "SourceDocument");

                Assert.That(InvokeStartWithAtlas(controller, figure, document, secondSchema, out StackMachineDiagnostic busyDiagnostic), Is.False);
                Assert.That(busyDiagnostic.domainCode, Is.EqualTo("EditorBuildControllerBusy"));
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.SameAs(firstSnapshot));
                Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.SameAs(firstFigure));
                Assert.That(GetProperty(controller, "SourceDocument"), Is.SameAs(firstDocument));
                Assert.That((bool)GetProperty(controller, "IsActive"), Is.True);
            }
            finally
            {
                ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(secondSchema); Object.DestroyImmediate(firstSchema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void AtlasOn_BusyNullSchemaRejectsWithoutReplacingActiveTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            object controller = CreateController(() => new PendingBackend());
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                object firstSnapshot = GetProperty(controller, "AtlasSchema");
                object firstFigure = GetProperty(controller, "FigureSourceRoot");
                object firstDocument = GetProperty(controller, "SourceDocument");

                Assert.That(InvokeStartWithAtlas(controller, figure, document, null, out StackMachineDiagnostic busyDiagnostic), Is.False);
                Assert.That(busyDiagnostic.domainCode, Is.EqualTo("EditorBuildControllerBusy"));
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.SameAs(firstSnapshot));
                Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.SameAs(firstFigure));
                Assert.That(GetProperty(controller, "SourceDocument"), Is.SameAs(firstDocument));
                Assert.That((bool)GetProperty(controller, "IsActive"), Is.True);
            }
            finally { ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure); }
        }

        [Test]
        public void AtlasOn_FailedStartAllowsSameControllerRetryWithNewTransaction()
        {
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var firstSchema = CreateValidSchema();
            var secondSchema = CreateValidSchema();
            bool failFirstBackend = true;
            object controller = CreateController(() =>
            {
                if (failFirstBackend) { failFirstBackend = false; return null; }
                return new PendingBackend();
            });
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, firstSchema, out StackMachineDiagnostic failureDiagnostic), Is.False);
                Assert.That(failureDiagnostic.domainCode, Is.EqualTo("EditorBuildBackendRequired"));
                AssertTransactionNotStarted(controller);

                Assert.That(InvokeStartWithAtlas(controller, figure, document, secondSchema, out StackMachineDiagnostic retryDiagnostic), Is.True, retryDiagnostic?.message);
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.Not.Null);
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.Not.SameAs(firstSchema.ToDocument()));
                Assert.That((bool)GetProperty(controller, "IsActive"), Is.True);
                Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.SameAs(figure));
                Assert.That(GetProperty(controller, "SourceDocument"), Is.Not.Null);
            }
            finally
            {
                ((System.IDisposable)controller).Dispose(); Object.DestroyImmediate(secondSchema); Object.DestroyImmediate(firstSchema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void CompilerWindow_GenerateSelectsAtlasOnAndOffControllerBoundaries()
        {
            var window = ScriptableObject.CreateInstance<HumanoidCompilerWindow>();
            var figure = new GameObject("Spec18_6_AtlasInputFigure");
            var document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
            var schema = CreateValidSchema();
            FieldInfo backendFactory = typeof(HumanoidCompilerWindow).GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic);
            System.Func<IHumanoidBuildBackend> previousFactory = (System.Func<IHumanoidBuildBackend>)backendFactory.GetValue(null);
            try
            {
                backendFactory.SetValue(null, new System.Func<IHumanoidBuildBackend>(() => new PendingBackend()));
                SetField(window, "figure", figure); SetField(window, "document", document); SetField(window, "atlasSchema", null);
                Invoke(window, "BeginGenerate");
                Assert.That(GetProperty(GetField(window, "controller"), "AtlasSchema"), Is.Null);
                Invoke(GetField(window, "controller"), "Cancel");

                SetField(window, "atlasSchema", schema);
                Invoke(window, "BeginGenerate");
                Assert.That(GetProperty(GetField(window, "controller"), "AtlasSchema"), Is.Not.Null);
            }
            finally
            {
                backendFactory.SetValue(null, previousFactory);
                Object.DestroyImmediate(window); Object.DestroyImmediate(schema); Object.DestroyImmediate(document); Object.DestroyImmediate(figure);
            }
        }

        private static AtlasSchema CreateValidSchema()
        {
            var schema = ScriptableObject.CreateInstance<AtlasSchema>();
            var id = new MaterialId("", "body");
            var document = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(id, "source:body") }), new[] { new AtlasSchemaEntry(id, 0, 2, 2, false, 0) });
            Assert.That(schema.TrySetDocument(document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return schema;
        }

        private static object CreateController(System.Func<IHumanoidBuildBackend> factory) => Activator.CreateInstance(ControllerType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { factory }, null);
        private static bool InvokeStartWithAtlas(object controller, GameObject figure, ShapeSyncDocumentAsset document, AtlasSchema schema, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { figure, document, schema, null };
            bool started = (bool)Invoke(controller, "TryStartWithAtlas", arguments);
            diagnostic = (StackMachineDiagnostic)arguments[3];
            return started;
        }
        private static void AssertTransactionNotStarted(object controller)
        {
            Assert.That(GetProperty(controller, "AtlasSchema"), Is.Null);
            Assert.That(GetProperty(controller, "FigureSourceRoot"), Is.Null);
            Assert.That(GetProperty(controller, "SourceDocument"), Is.Null);
            Assert.That(GetProperty(controller, "Candidate"), Is.Null);
            Assert.That(GetProperty(controller, "IsActive"), Is.False);
        }
        private static object Invoke(object instance, string name, object[] arguments = null) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments);
        private static object GetProperty(object instance, string name) => instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static object GetField(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void SetField(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);

        private sealed class PendingBackend : IHumanoidBuildBackend
        {
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Pending; }
            public void Cancel() { }
        }

        private sealed class RejectingBackend : IHumanoidBuildBackend
        {
            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "TestAtlasBackendRejected", "Injected Atlas input backend rejection.");
                return false;
            }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Failed; }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return false; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic) { payload = null; diagnostic = null; return HumanoidBuildPhaseStatus.Failed; }
            public void Cancel() { }
        }

        private static System.Type ControllerType => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidEditorBuildController", true);
    }
}
