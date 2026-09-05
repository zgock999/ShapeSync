// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode.Spec19
{
    public sealed class HotBakeBuildDriverTests
    {
        private const string Root = ShapeSyncTestAssetPaths.Spec19HotBakeBuildDriverRoot;
        private static readonly FieldInfo VrmTransportFactory = typeof(HumanoidVrmPhysicsTransportProvider).GetField("factory", BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void ValidateInput_RejectsLiveShapeSyncFigureBeforeSceneAdmission()
        {
            var figure = new GameObject("Spec19_4_LiveFigure");
            figure.AddComponent<ShapeDirector>();
            try
            {
                Assert.That(HotBakeBuildDriver.TryValidateInput(figure, null, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeLiveShapeSyncFigureRejected"));
            }
            finally { UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void ValidateInput_RejectsSceneObjectWithoutReadingItsDocument()
        {
            var figure = new GameObject("Spec19_4_SceneObject");
            try
            {
                Assert.That(HotBakeBuildDriver.TryValidateInput(figure, null, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakePrefabRequired"));
            }
            finally { UnityEngine.Object.DestroyImmediate(figure); }
        }

        [Test]
        public void ValidateInput_AcceptsReadablePrefabAndNonMipStreamingTexture()
        {
            using (var fixture = new InputFixture(false, true))
            {
                Assert.That(HotBakeBuildDriver.TryValidateInput(fixture.Prefab, fixture.Document, out ShapeSyncDocument snapshot, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(snapshot, Is.Not.Null);
            }
        }

        [Test]
        public void ValidateInput_RejectsNonReadableSourceMesh()
        {
            using (var fixture = new InputFixture(true, false))
            {
                Assert.That(HotBakeBuildDriver.TryValidateInput(fixture.Prefab, fixture.Document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeSourceMeshNotReadable"));
            }
        }

        [Test]
        public void ValidateInput_RejectsOnlyStreamingTextureThatAlsoHasMips()
        {
            using (var fixture = new InputFixture(false, false, mipStreaming: true))
            {
                Assert.That(HotBakeBuildDriver.TryValidateInput(fixture.Prefab, fixture.Document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeStreamingMipSourceRejected"));
            }
        }

        [Test]
        public void ValidateInput_RejectsNonReadableAttachedOutfitMesh()
        {
            using (var fixture = new InputFixture(false, false))
            {
                fixture.AddAttachedOutfit(unreadableMesh: true);
                Assert.That(HotBakeBuildDriver.TryValidateInput(fixture.Prefab, fixture.Document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeSourceMeshNotReadable"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("dress"));
            }
        }

        [Test]
        public void ValidateInput_RejectsStreamingMipAttachedOutfitTexture()
        {
            using (var fixture = new InputFixture(false, false))
            {
                fixture.AddAttachedOutfit(unreadableMesh: false, streamingMipTexture: true);
                Assert.That(HotBakeBuildDriver.TryValidateInput(fixture.Prefab, fixture.Document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeStreamingMipSourceRejected"));
                Assert.That(diagnostic.bindingName, Is.EqualTo("_MainTex"));
            }
        }

        [Test]
        public void Driver_HostDestroyedBeforePump_CancelsOwnedOperation()
        {
            using (var fixture = new InputFixture(false, false))
            {
                GameObject hostObject = new GameObject("Spec19_4_DestroyedHost");
                var host = hostObject.AddComponent<TextureStackMachineHost>();
                using (var driver = new HotBakeBuildDriver(host, null))
                {
                    Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                    UnityEngine.Object.DestroyImmediate(hostObject);
                    Assert.That(driver.Pump(out _, out StackMachineDiagnostic diagnostic), Is.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeTextureHostDestroyed"));
                }
            }
        }

        [Test]
        public void Driver_MaterialHostDestroyedDuringMeshPhase_DoesNotCancel()
        {
            using (var fixture = new InputFixture(false, false))
            {
                GameObject hostObject = new GameObject("Spec19_4_UnusedMaterialHost");
                var host = hostObject.AddComponent<TextureStackMachineHost>();
                using (var driver = new HotBakeBuildDriver(null, host))
                {
                    Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                    UnityEngine.Object.DestroyImmediate(hostObject);
                    HumanoidBuildOperationStatus status = driver.Pump(out HumanoidBuildResult result, out StackMachineDiagnostic diagnostic);
                    result?.Dispose();
                    Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Cancelled));
                    Assert.That(diagnostic?.domainCode, Is.Not.EqualTo("HotBakeTextureHostDestroyed"));
                }
            }
        }

        [Test]
        public void Driver_SuccessRetainsOperationUntilProvenanceIsTakenAndDisposed()
        {
            using (var fixture = new InputFixture(false, false))
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = null;
                for (int step = 0; step < 12 && driver.Operation.Status == HumanoidBuildOperationStatus.Pending; step++)
                    driver.Pump(out result, out _);

                try
                {
                    Assert.That(driver.Operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), driver.Operation.Diagnostic?.message);
                    Assert.That(result, Is.Not.Null);
                    Assert.That(driver.TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic provenanceDiagnostic), Is.True, provenanceDiagnostic?.message);
                    provenance.Dispose();
                    Assert.That(driver.TryTakeVrmTransportProvenance(out _, out StackMachineDiagnostic repeatDiagnostic), Is.False);
                    Assert.That(repeatDiagnostic.domainCode, Is.EqualTo("VrmTransportProvenanceAlreadyTaken"));
                }
                finally { result?.Dispose(); }
                driver.Dispose();
                Assert.That(driver.Operation, Is.Null, "Driver Dispose must release its retained successful operation after provenance transfer.");
            }
        }

        [Test]
        public void Driver_CommitArtifact_RejectsMissingScopeWithoutConsumingSuccessfulResult()
        {
            using (var fixture = new InputFixture(false, false))
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = PumpToSuccess(driver);
                try
                {
                    Assert.That(driver.TryCommitArtifact(result, null, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(set, Is.Null); Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactSceneScopeRequired"));
                    Assert.That(result.Root, Is.Not.Null);
                }
                finally { result?.Dispose(); }
            }
        }

        [Test]
        public void Driver_CommitArtifact_RejectsInvalidScopeBeforeMaterializingResult()
        {
            using (var fixture = new InputFixture(false, false)) using (var driver = new HotBakeBuildDriver(null, null))
            {
                var owner = new GameObject("Spec19_7_InvalidCommitOwner");
                try
                {
                    Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                    HumanoidBuildResult result = PumpToSuccess(driver); GameObject original = result.Root;
                    try
                    {
                        using (var scope = new HotBakeArtifactSceneScope(owner, null))
                        {
                            Assert.That(driver.TryCommitArtifact(result, null, scope, out _, out StackMachineDiagnostic diagnostic), Is.False);
                            Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeHostRequired")); Assert.That(result.Root, Is.SameAs(original));
                        }
                    }
                    finally { result?.Dispose(); }
                }
                finally { UnityEngine.Object.DestroyImmediate(owner); }
            }
        }

        [Test]
        public void Driver_CommitArtifact_MaterializesTemplateIntoScopeScene()
        {
            using (var fixture = new InputFixture(false, false)) using (var driver = new HotBakeBuildDriver(null, null))
            {
                var owner = new GameObject("Spec19_7_CommitOwner"); var hostRoot = new GameObject("Spec19_7_CommitHost");
                try
                {
                    Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                    HumanoidBuildResult result = PumpToSuccess(driver);
                    try
                    {
                        using (var scope = new HotBakeArtifactSceneScope(owner, hostRoot.AddComponent<TextureStackMachineHost>()))
                        {
                            Assert.That(driver.TryCommitArtifact(result, null, scope, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                            Assert.That(set.TemplateRoot.scene, Is.EqualTo(owner.scene)); Assert.That(scope.ArtifactSet, Is.SameAs(set));
                        }
                    }
                    finally { result?.Dispose(); }
                }
                finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(hostRoot); }
            }
        }

        [Test]
        public void Driver_TransportVrmPhysics_TransfersOpaqueOwnershipToArtifactTransaction()
        {
            using (var fixture = new InputFixture(false, false))
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = PumpToSuccess(driver);
                var transporter = new RecordingTransporter(true);
                object original = VrmTransportFactory.GetValue(null);
                try
                {
                    VrmTransportFactory.SetValue(null, new Func<IHumanoidVrmPhysicsTransporter>(() => transporter));
                    Assert.That(driver.TryTransportVrmPhysics(result.Root, out IDisposable ownership, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                    Assert.That(ownership, Is.SameAs(transporter.Ownership));
                    Assert.That(transporter.Candidate, Is.SameAs(result.Root));
                    Assert.That(transporter.Figure, Is.SameAs(fixture.Prefab));
                    Assert.That(transporter.Outfits, Is.Empty);
                    Assert.That(transporter.Ownership.Disposed, Is.False, "The driver must transfer rather than dispose successful in-memory VRM ownership.");
                    ownership.Dispose();
                    Assert.That(transporter.Ownership.Disposed, Is.True);
                    Assert.That(driver.TryTakeVrmTransportProvenance(out _, out StackMachineDiagnostic repeat), Is.False);
                    Assert.That(repeat.domainCode, Is.EqualTo("VrmTransportProvenanceAlreadyTaken"));
                }
                finally
                {
                    VrmTransportFactory.SetValue(null, original);
                    result?.Dispose();
                }
            }
        }

        [Test]
        public void Driver_TransportVrmPhysics_FailureDisposesUnhandedOwnershipAndPreservesDiagnostic()
        {
            using (var fixture = new InputFixture(false, false))
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = PumpToSuccess(driver);
                var transporter = new RecordingTransporter(false);
                object original = VrmTransportFactory.GetValue(null);
                try
                {
                    VrmTransportFactory.SetValue(null, new Func<IHumanoidVrmPhysicsTransporter>(() => transporter));
                    Assert.That(driver.TryTransportVrmPhysics(result.Root, out IDisposable ownership, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(ownership, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("TestVrmTransportRejected"));
                    Assert.That(transporter.Ownership.Disposed, Is.True, "A failed transport must not leak an ownership value returned by a faulty optional adapter.");
                }
                finally
                {
                    VrmTransportFactory.SetValue(null, original);
                    result?.Dispose();
                }
            }
        }

        [Test]
        public void Driver_TransportVrmPhysics_RejectsCandidateOtherThanSuccessfulBuildRoot()
        {
            using (var fixture = new InputFixture(false, false))
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                HumanoidBuildResult result = PumpToSuccess(driver);
                var other = new GameObject("Spec19_6_WrongCandidate");
                try
                {
                    Assert.That(driver.TryTransportVrmPhysics(other, out IDisposable ownership, out StackMachineDiagnostic diagnostic), Is.False);
                    Assert.That(ownership, Is.Null);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeVrmTransportCandidateMismatch"));
                    Assert.That(driver.TryTakeVrmTransportProvenance(out HumanoidVrmTransportProvenance provenance, out StackMachineDiagnostic provenanceDiagnostic), Is.True, provenanceDiagnostic?.message);
                    provenance.Dispose();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(other);
                    result?.Dispose();
                }
            }
        }

        [Test]
        public void Driver_TransportVrmPhysics_RejectsPendingOperationAndUnavailableProvider()
        {
            using (var fixture = new InputFixture(false, false))
            using (var driver = new HotBakeBuildDriver(null, null))
            {
                Assert.That(driver.TryBegin(fixture.Prefab, fixture.Document, out StackMachineDiagnostic start), Is.True, start?.message);
                Assert.That(driver.TryTransportVrmPhysics(fixture.Prefab, out _, out StackMachineDiagnostic pending), Is.False);
                Assert.That(pending.domainCode, Is.EqualTo("HotBakeVrmTransportBuildNotSucceeded"));
                HumanoidBuildResult result = PumpToSuccess(driver);
                object original = VrmTransportFactory.GetValue(null);
                try
                {
                    VrmTransportFactory.SetValue(null, null);
                    Assert.That(driver.TryTransportVrmPhysics(result.Root, out IDisposable ownership, out StackMachineDiagnostic unavailable), Is.False);
                    Assert.That(ownership, Is.Null);
                    Assert.That(unavailable.domainCode, Is.EqualTo("HotBakeVrmTransportUnavailable"));
                }
                finally
                {
                    VrmTransportFactory.SetValue(null, original);
                    result?.Dispose();
                }
            }
        }

        private static HumanoidBuildResult PumpToSuccess(HotBakeBuildDriver driver)
        {
            HumanoidBuildResult result = null;
            for (int step = 0; step < 12 && driver.Operation.Status == HumanoidBuildOperationStatus.Pending; step++)
                driver.Pump(out result, out _);
            Assert.That(driver.Operation.Status, Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), driver.Operation.Diagnostic?.message);
            Assert.That(result, Is.Not.Null);
            return result;
        }

        private sealed class RecordingTransporter : IHumanoidVrmPhysicsTransporter
        {
            internal RecordingTransporter(bool succeeds) { Succeeds = succeeds; Ownership = new TrackingOwnership(); }
            internal bool Succeeds { get; }
            internal TrackingOwnership Ownership { get; }
            internal GameObject Candidate { get; private set; }
            internal GameObject Figure { get; private set; }
            internal System.Collections.Generic.IReadOnlyList<GameObject> Outfits { get; private set; }

            public bool TryTransport(GameObject candidateRoot, GameObject figureSourceRoot, System.Collections.Generic.IReadOnlyList<GameObject> attachedOutfitSourceRoots, out IDisposable ownership, out StackMachineDiagnostic diagnostic)
            {
                Candidate = candidateRoot;
                Figure = figureSourceRoot;
                Outfits = attachedOutfitSourceRoots;
                ownership = Ownership;
                diagnostic = Succeeds ? null : StackMachineDiagnostic.CreateDomain("test", "TestVrmTransportRejected", "Injected VRM transport failure.");
                return Succeeds;
            }
        }

        private sealed class TrackingOwnership : IDisposable
        {
            internal bool Disposed { get; private set; }
            public void Dispose() { Disposed = true; }
        }

        internal sealed class InputFixture : IDisposable
        {
            private readonly GameObject authoring;
            public InputFixture(bool unreadable, bool streamingWithoutMips, bool mipStreaming = false)
            {
                AssetDatabase.DeleteAsset(Root);
                ShapeSyncTestAssetPaths.ConsumerFolderPath("__Spec19_4_HotBakeBuildDriverTests");
                Mesh mesh = CreateMesh();
                AssetDatabase.CreateAsset(mesh, Root + "/Source.asset");
                if (unreadable) mesh.UploadMeshData(true);
                Texture2D authoredTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
                File.WriteAllBytes(Root + "/Texture.png", authoredTexture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(authoredTexture);
                AssetDatabase.ImportAsset(Root + "/Texture.png", ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Texture.png");
                importer.mipmapEnabled = mipStreaming;
                importer.streamingMipmaps = mipStreaming || streamingWithoutMips;
                importer.SaveAndReimport();
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Texture.png");
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                var material = new Material(shader) { name = "SourceMaterial" }; material.SetTexture("_BaseMap", texture);
                AssetDatabase.CreateAsset(material, Root + "/Material.mat");
                var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                AssetDatabase.CreateAsset(adapter, Root + "/Adapter.asset");
                authoring = new GameObject("Spec19_4_PrefabFigure");
                Avatar avatar = CreateAvatar(authoring); AssetDatabase.CreateAsset(avatar, Root + "/Avatar.asset");
                var animator = authoring.AddComponent<Animator>(); animator.avatar = avatar;
                var renderer = authoring.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.sharedMaterial = material;
                renderer.rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                renderer.bones = new[] { renderer.rootBone };
                ConfigureProxy(authoring.AddComponent<MaterialProxy>(), renderer, adapter);
                Prefab = PrefabUtility.SaveAsPrefabAsset(authoring, Root + "/Figure.prefab");
                var meshBinding = ScriptableObject.CreateInstance<MeshBinding>(); AssetDatabase.CreateAsset(meshBinding, Root + "/MeshBinding.asset");
                Document = ScriptableObject.CreateInstance<ShapeSyncDocumentAsset>();
                Document.MeshBinding = meshBinding;
                Document.MeshRecipe = new MeshRecipeDocument { wordSource = "MORPH_RESET" };
                Document.MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 0.2 0.3 0.4 1 COLOR" };
                AssetDatabase.CreateAsset(Document, Root + "/Document.asset");
                AssetDatabase.SaveAssets();
            }

            public GameObject Prefab { get; }
            public ShapeSyncDocumentAsset Document { get; }

            public void AddAttachedOutfit(bool unreadableMesh, bool streamingMipTexture = false)
            {
                var outfitRoot = new GameObject("Spec19_4_OutfitDress");
                try
                {
                    var outfit = outfitRoot.AddComponent<ShapeSyncOutfit>();
                    var outfitSerialized = new SerializedObject(outfit);
                    outfitSerialized.FindProperty("registryId").stringValue = "outfit.dress";
                    var rendererRoot = new GameObject("renderer"); rendererRoot.transform.SetParent(outfitRoot.transform, false);
                    var rootBone = new GameObject("rootBone"); rootBone.transform.SetParent(outfitRoot.transform, false);
                    var renderer = rendererRoot.AddComponent<SkinnedMeshRenderer>();
                    Mesh outfitMesh = CreateMesh(); AssetDatabase.CreateAsset(outfitMesh, Root + "/OutfitMesh.asset");
                    if (unreadableMesh) outfitMesh.UploadMeshData(true);
                    renderer.sharedMesh = outfitMesh; renderer.rootBone = rootBone.transform; renderer.bones = new[] { rootBone.transform };
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit"); Assert.That(shader, Is.Not.Null);
                    var material = new Material(shader);
                    if (streamingMipTexture)
                    {
                        var authoredTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
                        File.WriteAllBytes(Root + "/OutfitTexture.png", authoredTexture.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(authoredTexture);
                        AssetDatabase.ImportAsset(Root + "/OutfitTexture.png", ImportAssetOptions.ForceSynchronousImport);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/OutfitTexture.png");
                        importer.mipmapEnabled = true; importer.streamingMipmaps = true; importer.SaveAndReimport();
                        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/OutfitTexture.png"));
                    }
                    AssetDatabase.CreateAsset(material, Root + "/OutfitMaterial.mat"); renderer.sharedMaterial = material;
                    var adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>(); AssetDatabase.CreateAsset(adapter, Root + "/OutfitAdapter.asset");
                    ConfigureProxy(outfitRoot.AddComponent<MaterialProxy>(), renderer, adapter);
                    var skinning = ScriptableObject.CreateInstance<OutfitSkinningProfile>();
                    skinning.SetRendererProfiles(new System.Collections.Generic.List<OutfitSkinningRendererProfile> { new OutfitSkinningRendererProfile { rendererPath = "renderer", baseBindposes = outfitMesh.bindposes } });
                    AssetDatabase.CreateAsset(skinning, Root + "/OutfitSkinning.asset");
                    outfitSerialized.FindProperty("skinningProfile").objectReferenceValue = skinning;
                    outfitSerialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(outfitRoot, Root + "/Outfit.prefab");
                    var binding = new SerializedObject(Document.MeshBinding);
                    SerializedProperty outfits = binding.FindProperty("outfits"); outfits.arraySize = 1;
                    SerializedProperty entry = outfits.GetArrayElementAtIndex(0);
                    entry.FindPropertyRelative("logicalName").stringValue = "dress";
                    entry.FindPropertyRelative("outfitPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Outfit.prefab");
                    binding.ApplyModifiedPropertiesWithoutUndo();
                    Document.MeshRecipe = new MeshRecipeDocument { wordSource = "$dress ATTACH" };
                    EditorUtility.SetDirty(Document); AssetDatabase.SaveAssets();
                }
                finally { UnityEngine.Object.DestroyImmediate(outfitRoot); }
            }

            public void Dispose()
            {
                if (authoring != null) UnityEngine.Object.DestroyImmediate(authoring);
                AssetDatabase.DeleteAsset(Root);
            }

            private static void ConfigureProxy(MaterialProxy proxy, SkinnedMeshRenderer renderer, MaterialShaderAdapter adapter)
            {
                var serialized = new SerializedObject(proxy);
                SerializedProperty entries = serialized.FindProperty("entries"); entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("entryName").stringValue = "body";
                entry.FindPropertyRelative("renderer").objectReferenceValue = renderer;
                entry.FindPropertyRelative("materialChannel").intValue = 0;
                entry.FindPropertyRelative("adapter").objectReferenceValue = adapter;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private static Mesh CreateMesh()
            {
                var mesh = new Mesh { name = "Spec19_4_SourceMesh" };
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { Matrix4x4.identity };
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                };
                return mesh;
            }

            private static Avatar CreateAvatar(GameObject root)
            {
                var bones = new System.Collections.Generic.List<Transform>();
                Transform hips = Add(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
                Transform spine = Add(hips, "Spine", Vector3.up * .15f, bones);
                Transform chest = Add(spine, "Chest", Vector3.up * .15f, bones);
                Transform neck = Add(chest, "Neck", Vector3.up * .15f, bones); Add(neck, "Head", Vector3.up * .12f, bones);
                Transform lua = Add(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones); Transform lla = Add(lua, "LeftLowerArm", Vector3.left * .2f, bones); Add(lla, "LeftHand", Vector3.left * .18f, bones);
                Transform rua = Add(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones); Transform rla = Add(rua, "RightLowerArm", Vector3.right * .2f, bones); Add(rla, "RightHand", Vector3.right * .18f, bones);
                Transform lul = Add(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones); Transform lll = Add(lul, "LeftLowerLeg", Vector3.down * .35f, bones); Add(lll, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
                Transform rul = Add(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones); Transform rll = Add(rul, "RightLowerLeg", Vector3.down * .35f, bones); Add(rll, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
                string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
                var human = new HumanBone[names.Length];
                for (int i = 0; i < names.Length; i++) human[i] = new HumanBone { boneName = names[i], humanName = names[i], limit = new HumanLimit { useDefaultValues = true } };
                var skeleton = new System.Collections.Generic.List<SkeletonBone> { ToSkeleton(root.transform) };
                for (int i = 0; i < bones.Count; i++) skeleton.Add(ToSkeleton(bones[i]));
                return AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
            }

            private static Transform Add(Transform parent, string name, Vector3 position, System.Collections.Generic.List<Transform> all) { Transform value = new GameObject(name).transform; value.SetParent(parent, false); value.localPosition = position; all.Add(value); return value; }
            private static SkeletonBone ToSkeleton(Transform value) => new SkeletonBone { name = value.name, position = value.localPosition, rotation = value.localRotation, scale = value.localScale };
        }
    }
}
#endif
