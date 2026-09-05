// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidCompilerContractTests
    {
        [Test]
        public void VrmTransportSourceResolver_PreservesLowerOrderAndRejectsMissingOrDuplicateBinding()
        {
            var dress = new GameObject("HumanoidCompilerContractTests.Dress");
            var jacket = new GameObject("HumanoidCompilerContractTests.Jacket");
            var binding = ScriptableObject.CreateInstance<MeshBinding>();
            try
            {
                typeof(MeshBinding).GetField("outfits", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MeshOutfitBindingEntry>
                {
                    new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = dress },
                    new MeshOutfitBindingEntry { logicalName = "jacket", outfitPrefab = jacket }
                });
                var document = new ShapeSyncDocument { MeshBinding = binding };
                Assert.That(HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(new ShapeSyncDocument(), System.Array.Empty<string>(), out IReadOnlyList<GameObject> baseRoots, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(baseRoots, Is.Empty, "Base-only VRM transport must not require a MeshBinding.");
                Assert.That(HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(document, new[] { "jacket", "dress" }, out IReadOnlyList<GameObject> roots, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(roots, Is.EqualTo(new[] { jacket, dress }));
                Assert.That(HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(document, new[] { "missing" }, out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmTransportOutfitSourceMissing"));

                typeof(MeshBinding).GetField("outfits", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MeshOutfitBindingEntry>
                {
                    new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = dress },
                    new MeshOutfitBindingEntry { logicalName = "unused", outfitPrefab = jacket },
                    new MeshOutfitBindingEntry { logicalName = "unused", outfitPrefab = dress }
                });
                Assert.That(HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(document, new[] { "dress" }, out roots, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(roots, Is.EqualTo(new[] { dress }));

                typeof(MeshBinding).GetField("outfits", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(binding, new List<MeshOutfitBindingEntry>
                {
                    new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = dress },
                    new MeshOutfitBindingEntry { logicalName = "dress", outfitPrefab = jacket }
                });
                Assert.That(HumanoidVrmTransportSourceResolver.TryResolveAttachedOutfitSourceRoots(document, new[] { "dress" }, out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmTransportOutfitLogicalNameDuplicate"));
            }
            finally { Object.DestroyImmediate(binding); Object.DestroyImmediate(dress); Object.DestroyImmediate(jacket); }
        }

        [Test]
        public void OwnedTexture_DetachTransfersTheSingleReleaseResponsibility()
        {
            var texture = new Texture2D(1, 1);
            int releases = 0;
            var owned = new HumanoidOwnedTexture(texture, _ => releases++);

            HumanoidOwnedTexture transferred = owned.Detach();
            owned.Dispose();
            Assert.That(releases, Is.EqualTo(0));

            transferred.Dispose();
            transferred.Dispose();
            Assert.That(releases, Is.EqualTo(1));
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void MeshPayload_DisposeReleasesOnlyOwnedComputedNormalAndFinalMesh()
        {
            var mesh = new Mesh { name = "HumanoidCompilerContractTests.Mesh" };
            var computedTexture = new Texture2D(1, 1);
            var sourceTexture = new Texture2D(1, 1);
            int releases = 0;
            var output = new InMemoryHumanoidMesh(mesh);
            var payload = new MeshBuildPayload(
                output,
                null,
                new[] { new HumanoidBuildSourceNormal(new MaterialId(string.Empty, "face"), sourceTexture) },
                new[] { new HumanoidBuildComputedNormal(new MaterialId(string.Empty, "face"), new HumanoidOwnedTexture(computedTexture, _ => releases++)) });

            payload.Dispose();

            Assert.That(releases, Is.EqualTo(1));
            Assert.That(sourceTexture, Is.Not.Null);
            Object.DestroyImmediate(sourceTexture);
            Object.DestroyImmediate(computedTexture);
        }

        [Test]
        public void VrmTransportProvenance_OperationSingleTakeAndTerminalCleanup()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var outfitRoot = new GameObject("HumanoidCompilerContractTests.Outfit");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var sourceMaterial = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                HumanoidMeshVrmTransportProvenance coreProvenance = CreateVrmTransportProvenance(outfitRoot);
                var backend = CreateSuccessfulBackend(sourceMaterial, adapter, coreProvenance);
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.TryTakeVrmTransportProvenance(out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmTransportBuildNotSucceeded"));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out HumanoidBuildResult result, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), diagnostic?.message);
                try
                {
                    Assert.That(operation.TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(provenance.AttachedOutfitLogicalNames, Is.EqualTo(new[] { "dress" }));
                    Assert.That(operation.TryTakeVrmTransportProvenance(out _, out diagnostic), Is.False);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("VrmTransportProvenanceAlreadyTaken"));
                    operation.Dispose();
                    Assert.That(provenance.AttachedOutfitLogicalNames, Is.EqualTo(new[] { "dress" }));
                    provenance.Dispose();
                    Assert.That(provenance.AttachedOutfitLogicalNames, Is.Empty);
                }
                finally { result.Dispose(); }

                HumanoidMeshVrmTransportProvenance unhandedCoreProvenance = CreateVrmTransportProvenance(outfitRoot);
                backend = CreateSuccessfulBackend(sourceMaterial, adapter, unhandedCoreProvenance);
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out operation, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out _), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out _), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out result, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), diagnostic?.message);
                result.Dispose();
                operation.Dispose();
                Assert.That(unhandedCoreProvenance.AttachedOutfitLogicalNames, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(adapter);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(outfitRoot);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryCompile_PumpsMeshThenMaterialAndHandsOffOnlyTheSucceededResult()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var finalMesh = new Mesh { name = "HumanoidCompilerContractTests.Final" };
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var sourceMaterial = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var backend = new FakeBackend
            {
                MeshPayload = new MeshBuildPayload(
                    new InMemoryHumanoidMesh(finalMesh),
                    new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, sourceMaterial, adapter) },
                    null,
                    null),
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MaterialStatus = HumanoidBuildPhaseStatus.Succeeded,
                MaterialPayload = new MaterialBuildPayload(null)
            };
            try
            {
                var compiler = new HumanoidCompiler();
                Assert.That(compiler.TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(backend.BeginMeshCalls, Is.EqualTo(1));

                Assert.That(operation.Pump(out HumanoidBuildResult pendingResult, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(pendingResult, Is.Null);
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(0));
                Assert.That(operation.Pump(out pendingResult, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(pendingResult, Is.Null);
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(1));

                Assert.That(operation.Pump(out HumanoidBuildResult result, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Mesh.Mesh, Is.Not.Null);
                Assert.That(operation.Pump(out HumanoidBuildResult secondResult, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Assert.That(secondResult, Is.Null);
                operation.Dispose();
                Assert.That(result.Mesh.Mesh, Is.Not.Null);
                result.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(sourceMaterial); Object.DestroyImmediate(root); }
        }

        [Test]
        public void TryCompile_RejectsInvalidInputAndCancelCallsBackendOnce()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var compiler = new HumanoidCompiler();
            try
            {
                Assert.That(compiler.TryCompile(default, new FakeBackend(), out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("FigureRequired"));
                Assert.That(compiler.TryCompile(new HumanoidBuildSource(root, null), new FakeBackend(), out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("DocumentRequired"));
                Assert.That(compiler.TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), null, out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("BackendRequired"));

                var backend = new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Pending };
                Assert.That(compiler.TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out diagnostic), Is.True, diagnostic?.message);
                operation.Cancel();
                operation.Cancel();
                Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TryCompile_BackendMeshBeginRejectDoesNotCreateOperation()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var backend = new FakeBackend { BeginMeshAccepted = false, BeginMeshDiagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "MeshBeginRejected", "Fixture rejected Mesh begin.") };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(operation, Is.Null);
                Assert.That(backend.BeginMeshCalls, Is.EqualTo(1));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MeshBeginRejected"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TryCompile_BackendMeshBeginWithoutDiagnosticUsesCompilerFallback()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var backend = new FakeBackend { BeginMeshAccepted = false };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(operation, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MeshBeginFailed"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_AppliesSourceThenComputedNormalAndTransfersTextureOwnershipToResult()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var sourceNormal = new Texture2D(1, 1);
            var computedNormal = new Texture2D(1, 1);
            var mainTexture = new Texture2D(1, 1);
            var finalMesh = new Mesh { name = "HumanoidCompilerContractTests.Final" };
            int releases = 0;
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(
                    new InMemoryHumanoidMesh(finalMesh),
                    new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) },
                    new[] { new HumanoidBuildSourceNormal(id, sourceNormal) },
                    new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(computedNormal, _ => releases++)) }),
                MaterialStatus = HumanoidBuildPhaseStatus.Succeeded,
                MaterialPayload = new MaterialBuildPayload(new[] { new HumanoidMaterialSemanticPayload(id, new HumanoidOwnedTexture(mainTexture, _ => releases++), true, new Color(.25f, .5f, .75f, 1f), false, default, default) })
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out HumanoidBuildResult result, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                Material clone = result.Mesh.Materials[0];
                Assert.That(clone.GetTexture("_BaseMap"), Is.SameAs(mainTexture));
                Assert.That(clone.GetTexture("_BumpMap"), Is.SameAs(computedNormal));
                Assert.That(source.GetTexture("_BumpMap"), Is.Not.SameAs(computedNormal));
                Assert.That(releases, Is.EqualTo(0));
                result.Dispose();
                Assert.That(releases, Is.EqualTo(2));
                operation.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(sourceNormal);
                Object.DestroyImmediate(computedNormal); Object.DestroyImmediate(mainTexture); Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Pump_RejectsUnknownMaterialPayloadAndDisposesItsOwnedTexture()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var texture = new Texture2D(1, 1);
            var finalMesh = new Mesh { name = "HumanoidCompilerContractTests.Final" };
            int releases = 0;
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(finalMesh), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null, null),
                MaterialStatus = HumanoidBuildPhaseStatus.Succeeded,
                MaterialPayload = new MaterialBuildPayload(new[] { new HumanoidMaterialSemanticPayload(new MaterialId(string.Empty, "unknown"), new HumanoidOwnedTexture(texture, _ => releases++), false, default, false, default, default) })
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialPayloadInvalid"));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(texture); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_MaterialBeginRejectDisposesMeshEscrowAndComputedNormal()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var computed = new Texture2D(1, 1);
            int releases = 0;
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(computed, _ => releases++)) }),
                BeginMaterialAccepted = false,
                BeginMaterialDiagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "MaterialBeginRejected", "Fixture rejected Material begin.")
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialBeginRejected"));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(computed); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_MaterialBeginWithoutDiagnosticUsesCompilerFallback()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null, null),
                BeginMaterialAccepted = false
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialBeginFailed"));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsMaterialCloneBeforeMaterialBegin()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(unlitShader, Is.Not.Null);
            var source = new Material(unlitShader);
            var incompatibleAdapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, incompatibleAdapter) }, null, null)
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialCloneRejected"));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(0));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(incompatibleAdapter); Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsMissingOrDuplicateMaterialSlotBeforeMaterialBegin()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                var missingMesh = new Mesh { subMeshCount = 2 };
                var missingBackend = new FakeBackend
                {
                    MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                    MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(missingMesh), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null, null)
                };
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), missingBackend, out HumanoidBuildOperation missingOperation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(missingOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(missingOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialSlotMissing"));
                Assert.That(missingBackend.BeginMaterialCalls, Is.EqualTo(0));
                missingOperation.Dispose();

                var duplicateBackend = new FakeBackend
                {
                    MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                    MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[]
                    {
                        new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter),
                        new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "face"), 0, source, adapter)
                    }, null, null)
                };
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), duplicateBackend, out HumanoidBuildOperation duplicateOperation, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(duplicateOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(duplicateOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialSlotDuplicate"));
                Assert.That(duplicateBackend.BeginMaterialCalls, Is.EqualTo(0));
                duplicateOperation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsInvalidSourceNormalBeforeMaterialBegin()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var normal = new Texture2D(1, 1);
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(
                    new InMemoryHumanoidMesh(new Mesh()),
                    new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) },
                    new[] { new HumanoidBuildSourceNormal(new MaterialId(string.Empty, "unknown"), normal) },
                    null)
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceNormalInvalid"));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(0));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(normal); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsDuplicateSourceNormalBeforeMaterialBegin()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var normal = new Texture2D(1, 1);
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, new[] { new HumanoidBuildSourceNormal(id, normal), new HumanoidBuildSourceNormal(id, normal) }, null)
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceNormalInvalid"));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(0));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(normal); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsSourceNormalWhenCandidateAdapterApplyFails()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var normal = new Texture2D(1, 1);
            var id = new MaterialId(string.Empty, "body");
            typeof(MaterialShaderAdapter).GetField("assignmentTemplates", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter, new List<MaterialPropertyBindingTemplate>
            {
                new MaterialPropertyBindingTemplate { propertyName = "_Spec17MissingNormal", writeKind = MaterialPropertyWriteKind.Texture, valueSource = MaterialPropertyValueSource.NormalTexture, required = true }
            });
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, new[] { new HumanoidBuildSourceNormal(id, normal) }, null)
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("SourceNormalRejected"));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(0));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(normal); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_ProvidesFullyPreparedCandidatesToMaterialBegin()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var sourceNormal = new Texture2D(1, 1);
            var body = new MaterialId(string.Empty, "body");
            var face = new MaterialId(string.Empty, "face");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(
                    new InMemoryHumanoidMesh(new Mesh { subMeshCount = 2 }),
                    new[]
                    {
                        new HumanoidBuildMaterialSlot(body, 0, source, adapter),
                        new HumanoidBuildMaterialSlot(face, 1, source, adapter)
                    },
                    new[] { new HumanoidBuildSourceNormal(body, sourceNormal) },
                    null),
                MaterialStatus = HumanoidBuildPhaseStatus.Succeeded,
                MaterialPayload = new MaterialBuildPayload(null),
                OnBeginMaterial = payload =>
                {
                    Assert.That(payload.Mesh.Materials.Count, Is.EqualTo(2));
                    Assert.That(payload.Mesh.Materials[0], Is.Not.SameAs(source));
                    Assert.That(payload.Mesh.Materials[1], Is.Not.SameAs(source));
                    Assert.That(payload.Mesh.Materials[0].GetTexture("_BumpMap"), Is.SameAs(sourceNormal));
                    Assert.That(payload.Mesh.Materials[1].GetTexture("_BumpMap"), Is.Null);
                    Assert.That(source.GetTexture("_BumpMap"), Is.Null);
                }
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(0));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(1));
                Assert.That(operation.Pump(out HumanoidBuildResult result, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded));
                result.Dispose();
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(sourceNormal); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_BackendCancelledTerminalPathsDisposeEscrowOnce()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var meshCancelled = new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Cancelled };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), meshCancelled, out HumanoidBuildOperation meshOperation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(meshOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(meshOperation.Pump(out HumanoidBuildResult cancelledResult, out StackMachineDiagnostic cancelledDiagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(cancelledResult, Is.Null);
                Assert.That(cancelledDiagnostic, Is.Null);
                meshOperation.Cancel();
                meshOperation.Dispose();
                Assert.That(meshCancelled.CancelCalls, Is.EqualTo(1));

                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                var source = new Material(shader);
                var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                var computed = new Texture2D(1, 1);
                int releases = 0;
                var materialCancelled = new FakeBackend
                {
                    MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                    MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null, new[] { new HumanoidBuildComputedNormal(new MaterialId(string.Empty, "body"), new HumanoidOwnedTexture(computed, _ => releases++)) }),
                    MaterialStatus = HumanoidBuildPhaseStatus.Cancelled
                };
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), materialCancelled, out HumanoidBuildOperation materialOperation, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(materialOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(materialOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(materialOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                materialOperation.Cancel();
                materialOperation.Dispose();
                Assert.That(materialCancelled.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(computed);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_MeshFailurePreservesDiagnosticAndCancelsBackend()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            var backend = new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Failed, MeshDiagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "MeshPumpRejected", "Fixture rejected Mesh pump.") };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MeshPumpRejected"));
                StackMachineDiagnostic failure = diagnostic;
                Assert.That(operation.Pump(out HumanoidBuildResult failedResult, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(failedResult, Is.Null);
                Assert.That(diagnostic, Is.SameAs(failure));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_MaterialFailurePreservesDiagnosticAndDisposesEscrow()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var computed = new Texture2D(1, 1);
            int releases = 0;
            Mesh finalMesh = null;
            Material candidateClone = null;
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(computed, _ => releases++)) }),
                MaterialStatus = HumanoidBuildPhaseStatus.Failed,
                MaterialDiagnostic = StackMachineDiagnostic.CreateDomain("humanoid", "MaterialPumpRejected", "Fixture rejected Material pump."),
                OnBeginMaterial = payload =>
                {
                    finalMesh = payload.Mesh.Mesh;
                    candidateClone = payload.Mesh.Materials[0];
                }
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialPumpRejected"));
                Assert.That(operation.Diagnostic, Is.SameAs(diagnostic));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                Assert.That(finalMesh == null, Is.True);
                Assert.That(candidateClone == null, Is.True);
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(computed); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Cancel_AfterMeshSuccessDisposesComputedNormalAndCancelsBackendOnce()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var computed = new Texture2D(1, 1);
            int releases = 0;
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(computed, _ => releases++)) }),
                MaterialStatus = HumanoidBuildPhaseStatus.Pending
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                operation.Cancel();
                Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(computed); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Cancel_AfterMaterialBeginDisposesAllCompilerEscrowOnce()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var computed = new Texture2D(1, 1);
            int releases = 0;
            Mesh finalMesh = null;
            Material candidateClone = null;
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(computed, _ => releases++)) }),
                MaterialStatus = HumanoidBuildPhaseStatus.Pending,
                OnBeginMaterial = payload =>
                {
                    finalMesh = payload.Mesh.Mesh;
                    candidateClone = payload.Mesh.Materials[0];
                }
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(backend.BeginMaterialCalls, Is.EqualTo(1));
                operation.Cancel();
                operation.Cancel();
                Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                Assert.That(finalMesh == null, Is.True);
                Assert.That(candidateClone == null, Is.True);
                operation.Dispose();
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(computed); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Dispose_AfterMaterialBeginCancelsAndDisposesAllCompilerEscrowOnce()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var computed = new Texture2D(1, 1);
            int releases = 0;
            Mesh finalMesh = null;
            Material candidateClone = null;
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(computed, _ => releases++)) }),
                MaterialStatus = HumanoidBuildPhaseStatus.Pending,
                OnBeginMaterial = payload =>
                {
                    finalMesh = payload.Mesh.Mesh;
                    candidateClone = payload.Mesh.Materials[0];
                }
            };
            try
            {
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                operation.Dispose();
                operation.Dispose();
                Assert.That(operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                Assert.That(backend.CancelCalls, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                Assert.That(finalMesh == null, Is.True);
                Assert.That(candidateClone == null, Is.True);
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(computed); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsSucceededNullPhasePayloads()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            try
            {
                var meshMissing = new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Succeeded };
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), meshMissing, out HumanoidBuildOperation meshOperation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(meshOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MeshPayloadMissing"));
                Assert.That(meshMissing.CancelCalls, Is.EqualTo(1));

                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                var source = new Material(shader);
                var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                var materialMissing = new FakeBackend
                {
                    MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                    MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, source, adapter) }, null, null),
                    MaterialStatus = HumanoidBuildPhaseStatus.Succeeded
                };
                Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), materialMissing, out HumanoidBuildOperation materialOperation, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(materialOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(materialOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
                Assert.That(materialOperation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialPayloadMissing"));
                Assert.That(materialMissing.CancelCalls, Is.EqualTo(1));
                Object.DestroyImmediate(adapter); Object.DestroyImmediate(source);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsMissingFinalMeshInvalidSlotAndMaterialIdSourceMismatch()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var sourceA = new Material(shader);
            var sourceB = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            try
            {
                AssertCandidateReject(root, new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Succeeded, MeshPayload = new MeshBuildPayload(null, null, null, null) }, "MeshRequired");
                AssertCandidateReject(root, new FakeBackend
                {
                    MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                    MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), -1, sourceA, adapter) }, null, null)
                }, "MaterialSlotInvalid");
                AssertCandidateReject(root, new FakeBackend
                {
                    MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                    MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh { subMeshCount = 2 }), new[]
                    {
                        new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 0, sourceA, adapter),
                        new HumanoidBuildMaterialSlot(new MaterialId(string.Empty, "body"), 1, sourceB, adapter)
                    }, null, null)
                }, "MaterialIdSourceMismatch");
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(sourceA); Object.DestroyImmediate(sourceB); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsDuplicateMaterialPayloadAndAdapterApplyFailure()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader, Is.Not.Null);
            var source = new Material(shader);
            var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
            var id = new MaterialId(string.Empty, "body");
            var first = new Texture2D(1, 1);
            var duplicate = new Texture2D(1, 1);
            int releases = 0;
            try
            {
                AssertMaterialReject(root, source, adapter, new MaterialBuildPayload(new[]
                {
                    new HumanoidMaterialSemanticPayload(id, new HumanoidOwnedTexture(first, _ => releases++), false, default, false, default, default),
                    new HumanoidMaterialSemanticPayload(id, new HumanoidOwnedTexture(duplicate, _ => releases++), false, default, false, default, default)
                }), "MaterialPayloadInvalid");
                Assert.That(releases, Is.EqualTo(2));
                AssertMaterialReject(root, source, adapter, new MaterialBuildPayload(new[]
                {
                    new HumanoidMaterialSemanticPayload(id, null, true, new Color(2f, 0f, 0f, 1f), false, default, default)
                }), "MaterialApplyRejected");
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(duplicate); Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        [Test]
        public void Pump_RejectsInvalidDuplicateUnknownAndAdapterRejectedComputedNormals()
        {
            var root = new GameObject("HumanoidCompilerContractTests.Root");
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(litShader, Is.Not.Null); Assert.That(unlitShader, Is.Not.Null);
            var source = new Material(litShader);
            var adapter = ScriptableObject.CreateInstance<UrpLitMaterialShaderAdapter>();
            var id = new MaterialId(string.Empty, "body");
            try
            {
                AssertComputedNormalReject(root, source, adapter, new[] { new HumanoidBuildComputedNormal(default, null) }, "ComputedNormalInvalid", null);
                var unknown = new Texture2D(1, 1);
                int unknownReleases = 0;
                AssertComputedNormalReject(root, source, adapter, new[] { new HumanoidBuildComputedNormal(new MaterialId(string.Empty, "unknown"), new HumanoidOwnedTexture(unknown, _ => unknownReleases++)) }, "ComputedNormalInvalid", null);
                Assert.That(unknownReleases, Is.EqualTo(1));
                var first = new Texture2D(1, 1); var duplicate = new Texture2D(1, 1);
                int duplicateReleases = 0;
                AssertComputedNormalReject(root, source, adapter, new[]
                {
                    new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(first, _ => duplicateReleases++)),
                    new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(duplicate, _ => duplicateReleases++))
                }, "ComputedNormalInvalid", null);
                Assert.That(duplicateReleases, Is.EqualTo(2));
                var rejected = new Texture2D(1, 1);
                int rejectedReleases = 0;
                AssertComputedNormalReject(root, source, adapter, new[] { new HumanoidBuildComputedNormal(id, new HumanoidOwnedTexture(rejected, _ => rejectedReleases++)) }, "ComputedNormalRejected", payload => payload.Mesh.Materials[0].shader = unlitShader);
                Assert.That(rejectedReleases, Is.EqualTo(1));
                Object.DestroyImmediate(unknown); Object.DestroyImmediate(first); Object.DestroyImmediate(duplicate); Object.DestroyImmediate(rejected);
            }
            finally { Object.DestroyImmediate(adapter); Object.DestroyImmediate(source); Object.DestroyImmediate(root); }
        }

        private static void AssertCandidateReject(GameObject root, FakeBackend backend, string code)
        {
            Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
            Assert.That(diagnostic.domainCode, Is.EqualTo(code));
            Assert.That(backend.CancelCalls, Is.EqualTo(1));
            operation.Dispose();
        }

        private static void AssertMaterialReject(GameObject root, Material source, MaterialShaderAdapter adapter, MaterialBuildPayload payload, string code)
        {
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Succeeded, MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, null), MaterialStatus = HumanoidBuildPhaseStatus.Succeeded, MaterialPayload = payload };
            Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
            Assert.That(diagnostic.domainCode, Is.EqualTo(code));
            Assert.That(backend.CancelCalls, Is.EqualTo(1));
            operation.Dispose();
        }

        private static void AssertComputedNormalReject(GameObject root, Material source, MaterialShaderAdapter adapter, HumanoidBuildComputedNormal[] normals, string code, System.Action<MeshBuildPayload> onBeginMaterial)
        {
            var id = new MaterialId(string.Empty, "body");
            var backend = new FakeBackend { MeshStatus = HumanoidBuildPhaseStatus.Succeeded, MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(new Mesh()), new[] { new HumanoidBuildMaterialSlot(id, 0, source, adapter) }, null, normals), MaterialStatus = HumanoidBuildPhaseStatus.Succeeded, MaterialPayload = new MaterialBuildPayload(null), OnBeginMaterial = onBeginMaterial };
            Assert.That(new HumanoidCompiler().TryCompile(new HumanoidBuildSource(root, new ShapeSyncDocument()), backend, out HumanoidBuildOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Pending));
            Assert.That(operation.Pump(out _, out diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Failed));
            Assert.That(diagnostic.domainCode, Is.EqualTo(code));
            Assert.That(backend.CancelCalls, Is.EqualTo(1));
            operation.Dispose();
        }

        [Test]
        public void InMemoryHumanoidMesh_RejectsMaterialAssignmentAfterDispose()
        {
            var mesh = new Mesh();
            var output = new InMemoryHumanoidMesh(mesh);
            output.Dispose();
            Assert.That(output.TrySetMaterials(new Material[0], out StackMachineDiagnostic diagnostic), Is.False);
            Assert.That(diagnostic.domainCode, Is.EqualTo("InMemoryMeshDisposed"));
        }

        private sealed class FakeBackend : IHumanoidBuildBackend
        {
            internal HumanoidBuildPhaseStatus MeshStatus = HumanoidBuildPhaseStatus.Pending;
            internal MeshBuildPayload MeshPayload;
            internal StackMachineDiagnostic MeshDiagnostic;
            internal HumanoidBuildPhaseStatus MaterialStatus = HumanoidBuildPhaseStatus.Pending;
            internal MaterialBuildPayload MaterialPayload;
            internal StackMachineDiagnostic MaterialDiagnostic;
            internal int BeginMeshCalls;
            internal bool BeginMeshAccepted = true;
            internal StackMachineDiagnostic BeginMeshDiagnostic;
            internal int BeginMaterialCalls;
            internal bool BeginMaterialAccepted = true;
            internal StackMachineDiagnostic BeginMaterialDiagnostic;
            internal System.Action<MeshBuildPayload> OnBeginMaterial;
            internal int CancelCalls;

            public bool TryBeginMeshPhase(HumanoidBuildSource source, out StackMachineDiagnostic diagnostic) { BeginMeshCalls++; diagnostic = BeginMeshDiagnostic; return BeginMeshAccepted; }
            public HumanoidBuildPhaseStatus PumpMeshPhase(out MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic) { meshPayload = MeshPayload; diagnostic = MeshDiagnostic; return MeshStatus; }
            public bool TryBeginMaterialPhase(MeshBuildPayload meshPayload, out StackMachineDiagnostic diagnostic) { BeginMaterialCalls++; OnBeginMaterial?.Invoke(meshPayload); diagnostic = BeginMaterialDiagnostic; return BeginMaterialAccepted; }
            public HumanoidBuildPhaseStatus PumpMaterialPhase(out MaterialBuildPayload materialPayload, out StackMachineDiagnostic diagnostic) { materialPayload = MaterialPayload; diagnostic = MaterialDiagnostic; return MaterialStatus; }
            public void Cancel() { CancelCalls++; }
        }

        private static FakeBackend CreateSuccessfulBackend(Material sourceMaterial, MaterialShaderAdapter adapter, HumanoidMeshVrmTransportProvenance provenance)
        {
            var output = new Mesh { subMeshCount = 1 };
            var id = new MaterialId(string.Empty, "body");
            return new FakeBackend
            {
                MeshStatus = HumanoidBuildPhaseStatus.Succeeded,
                MeshPayload = new MeshBuildPayload(new InMemoryHumanoidMesh(output), new[] { new HumanoidBuildMaterialSlot(id, 0, sourceMaterial, adapter) }, null, null, provenance),
                MaterialStatus = HumanoidBuildPhaseStatus.Succeeded,
                MaterialPayload = new MaterialBuildPayload(null)
            };
        }

        private static HumanoidMeshVrmTransportProvenance CreateVrmTransportProvenance(GameObject outfitRoot)
        {
            ConstructorInfo constructor = typeof(HumanoidMeshVrmTransportProvenance).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(IReadOnlyList<HumanoidMeshSource>) }, null);
            Assert.That(constructor, Is.Not.Null);
            var sources = new List<HumanoidMeshSource> { new HumanoidMeshSource("dress", "outfit.dress", outfitRoot, null, null, null) };
            return (HumanoidMeshVrmTransportProvenance)constructor.Invoke(new object[] { sources });
        }
    }
}
