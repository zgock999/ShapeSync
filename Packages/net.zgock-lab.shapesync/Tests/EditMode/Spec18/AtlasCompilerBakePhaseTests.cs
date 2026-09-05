// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Editor.Atlas;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Focuses the Spec18.6 controller boundary that pumps Atlas only after final Material success.</summary>
    public sealed class AtlasCompilerBakePhaseTests
    {
        private const string FixtureFolder = ShapeSyncTestAssetPaths.Spec18AtlasCompilerBakePhaseRoot;

        [Test]
        public void AtlasOn_PumpsAfterMaterialSuccess_AppliesPagesAndReleasesThemWithCandidateEscrow()
        {
            CreateFixture(out GameObject figure, out ShapeSyncDocumentAsset document, out Material source, out Texture2D baseColor);
            var neutralNormal = NeutralNormal();
            source.SetTexture("_BumpMap", neutralNormal);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var schema = CreateSchema(figure, document, source);
            var backend = new SuccessfulBackend(source, adapter, baseColor);
            var executor = new SuccessfulExecutor();
            object controller = CreateController(() => backend, () => executor);
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic start), Is.True, start?.message);
                bool observedAtlas = false;
                HumanoidBuildOperationStatus status = HumanoidBuildOperationStatus.Pending;
                for (int i = 0; i < 12 && status == HumanoidBuildOperationStatus.Pending; i++)
                {
                    status = InvokePump(controller, out StackMachineDiagnostic diagnostic);
                    Assert.That(diagnostic, Is.Null, diagnostic?.message);
                    observedAtlas |= (HumanoidBuildProgressPhase)GetProperty(controller, "ProgressPhase") == HumanoidBuildProgressPhase.Atlas;
                }
                Assert.That(status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(observedAtlas, Is.True, "Atlas phase must remain caller-visible between Material success and final result success.");
                var result = (HumanoidBuildResult)GetField(controller, "result");
                Assert.That(result.Mesh.AtlasPages, Is.Not.Null);
                Assert.That(result.Mesh.AtlasPages.Pages, Has.Count.EqualTo(1));
                Assert.That(executor.StartCount, Is.EqualTo(1));
                Assert.That(executor.ReleaseCount, Is.EqualTo(0));

                ((IDisposable)controller).Dispose();
                Assert.That(executor.ReleaseCount, Is.EqualTo(1), "candidate escrow must own and release the completed Atlas page exactly once.");
            }
            finally
            {
                ((IDisposable)controller).Dispose(); backend.Dispose(); Object.DestroyImmediate(adapter); Object.DestroyImmediate(schema); Object.DestroyImmediate(neutralNormal); Object.DestroyImmediate(baseColor); CleanupFixture();
            }
        }

        [Test]
        public void AtlasOn_PageStartFailure_AbortsCandidateAndClearsSchemaEscrow()
        {
            CreateFixture(out GameObject figure, out ShapeSyncDocumentAsset document, out Material source, out Texture2D baseColor);
            var neutralNormal = NeutralNormal();
            source.SetTexture("_BumpMap", neutralNormal);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var schema = CreateSchema(figure, document, source);
            var backend = new SuccessfulBackend(source, adapter, baseColor);
            var executor = new RejectingExecutor();
            object controller = CreateController(() => backend, () => executor);
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildOperationStatus status = HumanoidBuildOperationStatus.Pending;
                for (int i = 0; i < 8 && status == HumanoidBuildOperationStatus.Pending; i++) status = InvokePump(controller, out _);
                Assert.That(status, Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(((StackMachineDiagnostic)GetProperty(controller, "Diagnostic")).domainCode, Is.EqualTo("TestAtlasPageStartRejected"));
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.Null);
                Assert.That(executor.Disposed, Is.True);
            }
            finally
            {
                ((IDisposable)controller).Dispose(); backend.Dispose(); Object.DestroyImmediate(adapter); Object.DestroyImmediate(schema); Object.DestroyImmediate(neutralNormal); Object.DestroyImmediate(baseColor); CleanupFixture();
            }
        }

        [Test]
        public void AtlasOn_AllExcludedSchema_SucceedsAsNoOpAndEmitsOneInfo()
        {
            CreateFixture(out GameObject figure, out ShapeSyncDocumentAsset document, out Material source, out Texture2D baseColor);
            var neutralNormal = NeutralNormal();
            source.SetTexture("_BumpMap", neutralNormal);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var schema = CreateSchema(figure, document, source, excluded: true);
            var backend = new SuccessfulBackend(source, adapter, baseColor);
            object controller = CreateController(() => backend, () => null);
            FieldInfo logInfo = ControllerType.GetField("LogInfo", BindingFlags.Static | BindingFlags.NonPublic);
            Action<string> originalLogInfo = (Action<string>)logInfo.GetValue(null);
            string info = null;
            try
            {
                logInfo.SetValue(null, new Action<string>(message => info = message));
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildOperationStatus status = HumanoidBuildOperationStatus.Pending;
                for (int i = 0; i < 8 && status == HumanoidBuildOperationStatus.Pending; i++) status = InvokePump(controller, out StackMachineDiagnostic diagnostic);

                Assert.That(status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(info, Does.Contain("AtlasNoTargets"));
                var result = (HumanoidBuildResult)GetField(controller, "result");
                Assert.That(result.Mesh.AtlasPages, Is.Null);
            }
            finally
            {
                logInfo.SetValue(null, originalLogInfo); ((IDisposable)controller).Dispose(); backend.Dispose(); Object.DestroyImmediate(adapter); Object.DestroyImmediate(schema); Object.DestroyImmediate(neutralNormal); Object.DestroyImmediate(baseColor); CleanupFixture();
            }
        }

        [Test]
        public void AtlasReconciliationWarning_IsForwardedWithRequiredSchemaDriftContext()
        {
            Texture2D baseColor = new Texture2D(128, 128); Texture2D normal = new Texture2D(128, 128);
            FieldInfo logWarning = ControllerType.GetField("LogWarning", BindingFlags.Static | BindingFlags.NonPublic);
            Action<string> originalLogWarning = (Action<string>)logWarning.GetValue(null);
            string warning = null;
            try
            {
                var schemaId = new MaterialId("figure", "body");
                var extraId = new MaterialId("outfit", "extra");
                var identity = new AtlasValidationIdentity("figure", "document", new[] { new AtlasSourceMaterialIdentity(schemaId, "source-body") });
                var schema = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { new AtlasSchemaEntry(schemaId, 0, 1, 1, false) });
                using (var operation = new AtlasBakerOperation(schema, identity, new[] { new AtlasBakerMaterialInput(extraId, baseColor, normal) }))
                {
                    Assert.That(operation.Pump(), Is.EqualTo(AtlasBakerOperationStatus.Succeeded), operation.Diagnostic?.message);
                    Assert.That(operation.TryTakeResult(out AtlasBakerResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    logWarning.SetValue(null, new Action<string>(message => warning = message));
                    ControllerType.GetMethod("ReportAtlasReconciliationWarnings", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { result.Reconciliation });
                }

                Assert.That(warning, Does.Contain("AtlasFinalMaterialNotInSchema"));
                Assert.That(warning, Does.Contain("owner=outfit"));
                Assert.That(warning, Does.Contain("materialId=outfit/extra"));
                Assert.That(warning, Does.Contain("schemaDocument=document;currentDocument=document"));
            }
            finally { logWarning.SetValue(null, originalLogWarning); Object.DestroyImmediate(baseColor); Object.DestroyImmediate(normal); }
        }

        [Test]
        public void CompilerWindow_AtlasPendingPhaseDisplaysBakingAtlas()
        {
            CreateFixture(out GameObject figure, out ShapeSyncDocumentAsset document, out Material source, out Texture2D baseColor);
            var neutralNormal = NeutralNormal(); source.SetTexture("_BumpMap", neutralNormal);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>(); var backend = new SuccessfulBackend(source, adapter, baseColor); var schema = CreateSchema(figure, document, source); var window = ScriptableObject.CreateInstance<HumanoidCompilerWindow>();
            FieldInfo backendFactory = typeof(HumanoidCompilerWindow).GetField("BackendFactoryForTests", BindingFlags.Static | BindingFlags.NonPublic); object previousBackend = backendFactory.GetValue(null);
            SuccessfulExecutor executor = null;
            try
            {
                backendFactory.SetValue(null, new Func<IHumanoidBuildBackend>(() => backend));
                window.GetType().GetField("figure", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, figure); window.GetType().GetField("document", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, document); window.GetType().GetField("atlasSchema", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, schema);
                Invoke(window, "BeginGenerate");
                object controller = window.GetType().GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                FieldInfo atlasExecutorFactory = controller.GetType().GetField("atlasExecutorFactory", BindingFlags.Instance | BindingFlags.NonPublic);
                executor = new SuccessfulExecutor();
                atlasExecutorFactory.SetValue(controller, new Func<IAtlasBakerPageExecutor>(() => executor));
                for (int i = 0; i < 12 && !string.Equals((string)window.GetType().GetField("progress", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window), "Baking Atlas", StringComparison.Ordinal); i++) Invoke(window, "HandleEditorUpdate");
                string observedProgress = (string)window.GetType().GetField("progress", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); string observedWarning = (string)window.GetType().GetField("warning", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                Assert.That(observedProgress, Is.EqualTo("Baking Atlas"), $"warning={observedWarning}");
            }
            finally
            {
                backendFactory.SetValue(null, previousBackend); Object.DestroyImmediate(window); backend.Dispose(); Object.DestroyImmediate(adapter); Object.DestroyImmediate(schema); Object.DestroyImmediate(neutralNormal); Object.DestroyImmediate(baseColor); CleanupFixture();
            }
        }

        [Test]
        public void AtlasOn_CancelDuringPageExecution_CancelsExecutorAndClearsCandidateEscrow()
        {
            CreateFixture(out GameObject figure, out ShapeSyncDocumentAsset document, out Material source, out Texture2D baseColor);
            var neutralNormal = NeutralNormal();
            source.SetTexture("_BumpMap", neutralNormal);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var schema = CreateSchema(figure, document, source);
            var backend = new SuccessfulBackend(source, adapter, baseColor);
            var executor = new PendingExecutor();
            object controller = CreateController(() => backend, () => executor);
            try
            {
                Assert.That(InvokeStartWithAtlas(controller, figure, document, schema, out StackMachineDiagnostic start), Is.True, start?.message);
                for (int i = 0; i < 8 && executor.StartCount == 0; i++) InvokePump(controller, out StackMachineDiagnostic diagnostic);
                Assert.That(executor.StartCount, Is.EqualTo(1));
                Assert.That((HumanoidBuildProgressPhase)GetProperty(controller, "ProgressPhase"), Is.EqualTo(HumanoidBuildProgressPhase.Atlas));

                Invoke(controller, "Cancel");

                Assert.That(executor.CancelCount, Is.EqualTo(1));
                Assert.That(executor.Disposed, Is.True);
                Assert.That(GetProperty(controller, "Candidate"), Is.Null);
                Assert.That(GetProperty(controller, "AtlasSchema"), Is.Null);
                Assert.That((HumanoidBuildOperationStatus)GetProperty(controller, "Status"), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
            }
            finally
            {
                ((IDisposable)controller).Dispose(); backend.Dispose(); Object.DestroyImmediate(adapter); Object.DestroyImmediate(schema); Object.DestroyImmediate(neutralNormal); Object.DestroyImmediate(baseColor); CleanupFixture();
            }
        }

        private static void CreateFixture(out GameObject figure, out ShapeSyncDocumentAsset document, out Material source, out Texture2D baseColor)
        {
            CleanupFixture(); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/Spec18"), "__AtlasCompilerBakePhase");
            baseColor = new Texture2D(256, 256) { name = "base" };
            source = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "source" };
            AssetDatabase.CreateAsset(source, FixtureFolder + "/source.mat");
            source.SetTexture("_BaseMap", baseColor);
            document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>(); AssetDatabase.CreateAsset(document, FixtureFolder + "/document.asset");
            var root = new GameObject("figure"); figure = PrefabUtility.SaveAsPrefabAsset(root, FixtureFolder + "/figure.prefab"); Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
        }

        private static AtlasSchema CreateSchema(GameObject figure, ShapeSyncDocumentAsset document, Material source, bool excluded = false)
        {
            var id = new MaterialId(string.Empty, "body");
            var schema = ScriptableObject.CreateInstance<AtlasSchema>();
            var identity = new AtlasValidationIdentity(AtlasEditorIdentityTokenProvider.Create(figure), AtlasEditorIdentityTokenProvider.Create(document), new[] { new AtlasSourceMaterialIdentity(id, AtlasEditorIdentityTokenProvider.Create(source)) });
            var value = new AtlasSchemaDocument(AtlasSchemaVersion.Current, 512, AtlasPackingAlgorithm.FirstFitBuddyV1, true, identity, new[] { new AtlasSchemaEntry(id, 0, 1, 1, excluded) });
            Assert.That(schema.TrySetDocument(value, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return schema;
        }

        private static void CleanupFixture() { if (AssetDatabase.IsValidFolder(FixtureFolder)) AssetDatabase.DeleteAsset(FixtureFolder); }
        private static Texture2D NeutralNormal()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { name = "DatabaseRenamedNormal" };
            var pixels = new Color[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.5f, .5f, 1f, 1f);
            texture.SetPixels(pixels); texture.Apply(false, false);
            return texture;
        }
        private static object CreateController(Func<IHumanoidBuildBackend> backend, Func<IAtlasBakerPageExecutor> executor)
            => Activator.CreateInstance(ControllerType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { backend, executor }, null);
        private static bool InvokeStartWithAtlas(object controller, GameObject figure, ShapeSyncDocumentAsset document, AtlasSchema schema, out StackMachineDiagnostic diagnostic)
        { object[] arguments = { figure, document, schema, null }; bool result = (bool)Invoke(controller, "TryStartWithAtlas", arguments); diagnostic = (StackMachineDiagnostic)arguments[3]; return result; }
        private static HumanoidBuildOperationStatus InvokePump(object controller, out StackMachineDiagnostic diagnostic)
        { object[] arguments = { null }; HumanoidBuildOperationStatus result = (HumanoidBuildOperationStatus)Invoke(controller, "Pump", arguments); diagnostic = (StackMachineDiagnostic)arguments[0]; return result; }
        private static object Invoke(object instance, string name, object[] arguments = null) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, arguments);
        private static object GetProperty(object instance, string name) => instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static object GetField(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static Type ControllerType => typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidEditorBuildController", true);

        private sealed class SuccessfulBackend : IHumanoidBuildBackend, IDisposable
        {
            private readonly Material source; private readonly MaterialShaderAdapter adapter; private readonly Texture baseColor; private bool meshPumped;
            internal SuccessfulBackend(Material source, MaterialShaderAdapter adapter, Texture baseColor) { this.source = source; this.adapter = adapter; this.baseColor = baseColor; }
            public bool TryBeginMeshPhase(HumanoidBuildSource buildSource, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload payload, out StackMachineDiagnostic diagnostic)
            {
                diagnostic = null;
                if (meshPumped) { payload = null; return HumanoidBuildPhaseStatus.Failed; }
                meshPumped = true; var mesh = new Mesh { subMeshCount = 1 }; mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up }; mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
                var root = new GameObject("candidate"); root.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                payload = new MeshBuildPayload(new InMemoryHumanoidMesh(root, mesh, null), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, Array.Empty<HumanoidBuildSourceNormal>(), Array.Empty<HumanoidBuildComputedNormal>());
                return HumanoidBuildPhaseStatus.Succeeded;
            }
            public bool TryBeginMaterialPhase(MeshBuildPayload payload, out StackMachineDiagnostic diagnostic) { diagnostic = null; return true; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload payload, out StackMachineDiagnostic diagnostic)
            {
                payload = new MaterialBuildPayload(new[] { new HumanoidMaterialSemanticPayload(new MaterialId(string.Empty, "body"), new HumanoidOwnedTexture(baseColor, _ => { }), false, default, false, Vector2.one, Vector2.zero) });
                diagnostic = null; return HumanoidBuildPhaseStatus.Succeeded;
            }
            public void Cancel() { }
            public void Dispose() { }
        }

        private sealed class SuccessfulExecutor : IAtlasBakerPageExecutor
        {
            private AtlasBakerPagePlan page; private bool started; private bool completed;
            internal int StartCount; internal int ReleaseCount;
            public bool Start(AtlasBakerPagePlan value, out StackMachineDiagnostic diagnostic) { page = value; started = true; StartCount++; diagnostic = null; return true; }
            public AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic) { diagnostic = null; if (!started) return AtlasBakerExecutionStatus.Failed; completed = true; return AtlasBakerExecutionStatus.Succeeded; }
            public bool TryTakeCompletion(out AtlasBakerPageCompletion completion)
            { if (!completed) { completion = null; return false; } completed = false; var texture = new RenderTexture(page.Extent, page.Extent, 0); texture.Create(); completion = new AtlasBakerPageCompletion(page.PageIndex, page.Semantic, texture, Release); return true; }
            public void Cancel() { }
            public void Dispose() { }
            private void Release(RenderTexture texture) { ReleaseCount++; if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); } }
        }

        private sealed class RejectingExecutor : IAtlasBakerPageExecutor
        {
            internal bool Disposed;
            public bool Start(AtlasBakerPagePlan page, out StackMachineDiagnostic diagnostic) { diagnostic = StackMachineDiagnostic.CreateDomain("atlas", "TestAtlasPageStartRejected", "Injected page start rejection."); return false; }
            public AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic) { diagnostic = null; return AtlasBakerExecutionStatus.Failed; }
            public bool TryTakeCompletion(out AtlasBakerPageCompletion completion) { completion = null; return false; }
            public void Cancel() { }
            public void Dispose() { Disposed = true; }
        }

        private sealed class PendingExecutor : IAtlasBakerPageExecutor
        {
            internal int StartCount; internal int CancelCount; internal bool Disposed;
            public bool Start(AtlasBakerPagePlan page, out StackMachineDiagnostic diagnostic) { StartCount++; diagnostic = null; return true; }
            public AtlasBakerExecutionStatus Pump(out StackMachineDiagnostic diagnostic) { diagnostic = null; return AtlasBakerExecutionStatus.Pending; }
            public bool TryTakeCompletion(out AtlasBakerPageCompletion completion) { completion = null; return false; }
            public void Cancel() { CancelCount++; }
            public void Dispose() { Disposed = true; }
        }

    }
}
