// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode.Spec19
{
    public sealed class HotBakeAtlasRuntimeOperationTests
    {
        [Test]
        public void TryCreate_RejectsMissingHostBeforeStartingAnyAtlasWork()
        {
            AtlasSchema schema = CreateValidSchema();
            var candidate = new InMemoryHumanoidMesh(new Mesh());
            try
            {
                Assert.That(HotBakeAtlasRuntimeOperation.TryCreate(schema, candidate, null, out HotBakeAtlasRuntimeOperation operation, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(operation, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeAtlasHostRequired"));
            }
            finally { candidate.Dispose(); Object.DestroyImmediate(schema); }
        }

        [Test]
        public void TryCreate_RejectsInvalidSchemaBeforeInitializingHost()
        {
            var schema = ScriptableObject.CreateInstance<AtlasSchema>();
            var candidate = new InMemoryHumanoidMesh(new Mesh());
            var hostRoot = new GameObject("Spec19_9_InvalidAtlasHost");
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                Assert.That(HotBakeAtlasRuntimeOperation.TryCreate(schema, candidate, host, out HotBakeAtlasRuntimeOperation operation, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(operation, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("AtlasFigureIdentityRequired"));
                Assert.That(host.IsInitialized, Is.False, "Schema rejection must not allocate a Texture StackMachine grid.");
            }
            finally { candidate.Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(hostRoot); }
        }

        [Test]
        public void Dispose_AfterSuccessfulAdmissionRejectsFurtherPump()
        {
            AtlasSchema schema = CreateValidSchema();
            var candidate = new InMemoryHumanoidMesh(new Mesh());
            var hostRoot = new GameObject("Spec19_9_DisposeAtlasHost");
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                ComputeShader program = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShapeSyncTestAssetPaths.TextureStackMachineComputePath);
                Assert.That(host.TryAssignComputeProgram(program, out StackMachineDiagnostic assignment), Is.True, assignment?.message);
                Assert.That(HotBakeAtlasRuntimeOperation.TryCreate(schema, candidate, host, out HotBakeAtlasRuntimeOperation operation, out StackMachineDiagnostic start), Is.True, start?.message);
                operation.Dispose();
                Assert.That(operation.Pump(out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeAtlasOperationDisposed"));
                Assert.That(host.PendingRequestCount, Is.Zero);
                Assert.That(host.HasSubmittedRequest, Is.False);
            }
            finally { candidate.Dispose(); Object.DestroyImmediate(schema); Object.DestroyImmediate(hostRoot); }
        }

        private static AtlasSchema CreateValidSchema()
        {
            var schema = ScriptableObject.CreateInstance<AtlasSchema>();
            var document = new AtlasSchemaDocument(
                AtlasSchemaVersion.Current,
                512,
                AtlasPackingAlgorithm.FirstFitBuddyV1,
                true,
                new AtlasValidationIdentity("figure", "document"),
                Array.Empty<AtlasSchemaEntry>());
            Assert.That(schema.TrySetDocument(document, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            return schema;
        }
    }
}
