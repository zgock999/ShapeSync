// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode.Spec19
{
    /// <summary>White-box ownership acceptance for the scene-scoped Hot Bake artifact value.</summary>
    public sealed class HotBakeArtifactSetTests
    {
        [Test]
        public void Create_AcquireAndRelease_TransfersResourcesAndDisposesOnlyAfterFinalReference()
        {
            BuildFixture fixture = CreateFixture();
            var optional = new CountingDisposable();
            GameObject firstSpawn = null;
            GameObject secondSpawn = null;
            try
            {
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, new IDisposable[] { optional }, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(fixture.Result.Mesh, Is.Null);
                Assert.That(set.TemplateRoot, Is.SameAs(fixture.Root), "The artifact set must retain the candidate as its spawn template.");
                Assert.That(set.ReferenceCount, Is.EqualTo(1));
                Assert.That(set.Mesh, Is.SameAs(fixture.Mesh));
                Assert.That(set.Materials[0], Is.SameAs(fixture.Material));
                Assert.That(set.GpuByteCount, Is.EqualTo(4L * 2L * 8L));
                firstSpawn = UnityEngine.Object.Instantiate(set.TemplateRoot);
                secondSpawn = UnityEngine.Object.Instantiate(set.TemplateRoot);
                Assert.That(firstSpawn, Is.Not.SameAs(secondSpawn));
                Assert.That(firstSpawn.GetComponent<SkinnedMeshRenderer>().sharedMesh, Is.SameAs(set.Mesh));
                Assert.That(secondSpawn.GetComponent<SkinnedMeshRenderer>().sharedMaterials[0], Is.SameAs(set.Materials[0]));

                Assert.That(set.TryAcquire(out HotBakeArtifactLease lease, out diagnostic), Is.True, diagnostic?.message);
                Assert.That(set.ReferenceCount, Is.EqualTo(2));
                set.Dispose();
                set.Dispose();
                Assert.That(set.ReferenceCount, Is.EqualTo(1));
                Assert.That(optional.DisposeCount, Is.EqualTo(0));
                Assert.That(set.Mesh, Is.SameAs(fixture.Mesh));
                Assert.That(set.TemplateRoot, Is.SameAs(fixture.Root));
                Assert.That(set.TryAcquire(out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactOwnerReleased"));
                Assert.That(set.ReferenceCount, Is.EqualTo(1), "Rejected re-retain must not extend the surviving lease.");

                lease.Dispose();
                lease.Dispose();
                Assert.That(set.ReferenceCount, Is.EqualTo(0));
                Assert.That(optional.DisposeCount, Is.EqualTo(1));
                Assert.That(fixture.TextureReleaseCount, Is.EqualTo(1));
                Assert.That(fixture.Mesh == null, Is.True);
                Assert.That(fixture.Material == null, Is.True);
                Assert.That(fixture.Root == null, Is.True);
                Assert.That(set.TryAcquire(out _, out diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactUnavailable"));
            }
            finally
            {
                if (secondSpawn != null) UnityEngine.Object.DestroyImmediate(secondSpawn);
                if (firstSpawn != null) UnityEngine.Object.DestroyImmediate(firstSpawn);
                fixture.Result?.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void Create_RejectsNullOptionalOwnershipWithoutConsumingBuildResult()
        {
            BuildFixture fixture = CreateFixture();
            try
            {
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, new IDisposable[] { null }, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(set, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactOptionalOwnershipNull"));
                Assert.That(fixture.Result.Mesh, Is.Not.Null);
                Assert.That(fixture.Root, Is.Not.Null);
            }
            finally
            {
                fixture.Result.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void Create_RejectsDuplicateOptionalOwnershipWithoutConsumingBuildResult()
        {
            BuildFixture fixture = CreateFixture();
            var optional = new CountingDisposable();
            try
            {
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, new IDisposable[] { optional, optional }, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(set, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactOptionalOwnershipDuplicate"));
                Assert.That(optional.DisposeCount, Is.EqualTo(0));
                Assert.That(fixture.Result.Mesh, Is.Not.Null);
                Assert.That(fixture.Root, Is.Not.Null);
            }
            finally
            {
                fixture.Result.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void Create_RejectsMissingTemplateWithoutConsumingBuildResultOrOptionalOwnership()
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            HumanoidBuildResult result = CreateBuildResult(new InMemoryHumanoidMesh(mesh));
            var optional = new CountingDisposable();
            try
            {
                Assert.That(HotBakeArtifactSet.TryCreate(result, new IDisposable[] { optional }, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(set, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeArtifactTemplateRequired"));
                Assert.That(result.Mesh, Is.Not.Null);
                Assert.That(result.Mesh.Mesh, Is.SameAs(mesh));
                Assert.That(optional.DisposeCount, Is.EqualTo(0));
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void GpuByteCount_IncludesDistinctCompletedAtlasPages()
        {
            BuildFixture fixture = CreateFixture();
            try
            {
                fixture.AtlasTexture = new RenderTexture(8, 4, 0, RenderTextureFormat.ARGBHalf);
                var completion = new AtlasBakerPageCompletion(0, default, fixture.AtlasTexture, texture =>
                {
                    fixture.AtlasTextureReleaseCount++;
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                });
                ConstructorInfo pagesConstructor = typeof(AtlasBakerCandidatePages).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(AtlasBakerPageCompletion[]) }, null);
                Assert.That(pagesConstructor, Is.Not.Null);
                MethodInfo setAtlasPages = typeof(InMemoryHumanoidMesh).GetMethod("SetAtlasPages", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(setAtlasPages, Is.Not.Null);
                InMemoryHumanoidMesh carrier = fixture.Result.Mesh;
                setAtlasPages.Invoke(carrier, new[] { pagesConstructor.Invoke(new object[] { new[] { completion } }) });

                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(set.GpuByteCount, Is.EqualTo((4L * 2L * 8L) + (8L * 4L * 8L)));
                set.Dispose();
                Assert.That(fixture.AtlasTextureReleaseCount, Is.EqualTo(1));
            }
            finally
            {
                fixture.Result?.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void SceneScope_HostOnDestroy_ImmediatelyReleasesArtifact()
        {
            BuildFixture fixture = CreateFixture();
            GameObject hostRoot = new GameObject("Spec19_7_ScopeHost");
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, host))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    MethodInfo onDestroy = typeof(TextureStackMachineHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(onDestroy, Is.Not.Null);
                    onDestroy.Invoke(host, null);
                    Assert.That(scope.ArtifactSet, Is.Null);
                    Assert.That(scope.LastDiagnostic.domainCode, Is.EqualTo("HotBakeHostDestroyed"));
                    Assert.That(set.IsAvailable, Is.False);
                    Assert.That(fixture.Root == null, Is.True);
                }
            }
            finally
            {
                if (hostRoot != null) UnityEngine.Object.DestroyImmediate(hostRoot);
                fixture.Result?.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void SceneScope_HostOnDisable_ImmediatelyReleasesArtifactWithoutWarning()
        {
            BuildFixture fixture = CreateFixture();
            GameObject hostRoot = new GameObject("Spec19_9_ScopeHostDisabled");
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, host))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    MethodInfo onDisable = typeof(TextureStackMachineHost).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(onDisable, Is.Not.Null);
                    onDisable.Invoke(host, null);
                    Assert.That(scope.ArtifactSet, Is.Null);
                    Assert.That(scope.LastDiagnostic.domainCode, Is.EqualTo("HotBakeHostDestroyed"));
                    Assert.That(set.IsAvailable, Is.False);
                    LogAssert.NoUnexpectedReceived();
                }
            }
            finally
            {
                if (hostRoot != null) UnityEngine.Object.DestroyImmediate(hostRoot);
                fixture.Result?.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void SceneScope_RejectsArtifactWithoutLiveHostWithoutConsumingIt()
        {
            BuildFixture fixture = CreateFixture();
            try
            {
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, null))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.False);
                    Assert.That(diagnostic.domainCode, Is.EqualTo("HotBakeHostRequired"));
                    Assert.That(scope.ArtifactSet, Is.Null);
                    Assert.That(set.IsAvailable, Is.True, "Configuration rejection must not consume the caller-owned artifact set.");
                }
                set.Dispose();
            }
            finally
            {
                fixture.Result?.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void SceneScope_UnregisterSpawn_DoesNotTakeSpawnOwnership()
        {
            BuildFixture fixture = CreateFixture(); GameObject hostRoot = new GameObject("Spec19_7_UnregisterHost"); GameObject spawn = null;
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, host))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    spawn = UnityEngine.Object.Instantiate(set.TemplateRoot); Assert.That(scope.TryRegisterSpawn(spawn, out diagnostic), Is.True, diagnostic?.message);
                    scope.UnregisterSpawn(spawn); Assert.That(spawn == null, Is.False); Assert.That(scope.Validate(out diagnostic), Is.True, diagnostic?.message);
                }
            }
            finally { if (spawn != null) UnityEngine.Object.DestroyImmediate(spawn); if (hostRoot != null) UnityEngine.Object.DestroyImmediate(hostRoot); fixture.Result?.Dispose(); fixture.CleanupUntransferred(); }
        }

        [Test]
        public void Spawner_CreatesRegisteredCallerOwnedInstanceWithLocalTrs()
        {
            BuildFixture fixture = CreateFixture(); GameObject hostRoot = new GameObject("Spec19_7_SpawnHost"); GameObject parent = new GameObject("Spec19_7_SpawnParent"); GameObject instance = null;
            try
            {
                var host = hostRoot.AddComponent<TextureStackMachineHost>();
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, host))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(HotBakeSpawnPrimitive.TrySpawn(scope, parent.transform, new Vector3(1, 2, 3), Quaternion.Euler(0, 45, 0), false, out instance, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(instance.transform.parent, Is.SameAs(parent.transform)); Assert.That(instance.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
                    scope.UnregisterSpawn(instance); Assert.That(instance == null, Is.False);
                }
            }
            finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(hostRoot); fixture.Result?.Dispose(); fixture.CleanupUntransferred(); }
        }

        [Test]
        public void Spawner_CreatesMultipleInstancesFromOneSharedArtifact()
        {
            BuildFixture fixture = CreateFixture(); var hostRoot = new GameObject("Spec19_7_NSpawnHost"); var parent = new GameObject("Spec19_7_NSpawnParent"); GameObject first = null; GameObject second = null;
            try
            {
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, hostRoot.AddComponent<TextureStackMachineHost>()))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(HotBakeSpawnPrimitive.TrySpawn(scope, parent.transform, Vector3.zero, Quaternion.identity, false, out first, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(HotBakeSpawnPrimitive.TrySpawn(scope, parent.transform, Vector3.right, Quaternion.identity, false, out second, out diagnostic), Is.True, diagnostic?.message);
                    Assert.That(first.GetComponent<SkinnedMeshRenderer>().sharedMesh, Is.SameAs(second.GetComponent<SkinnedMeshRenderer>().sharedMesh));
                }
            }
            finally { if (second != null) UnityEngine.Object.DestroyImmediate(second); if (first != null) UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(hostRoot); fixture.Result?.Dispose(); fixture.CleanupUntransferred(); }
        }

        [Test]
        public void SceneScope_DirectorCommit_ImmediatelyReleasesArtifact()
        {
            BuildFixture fixture = CreateFixture();
            GameObject hostRoot = new GameObject("Spec19_7_DirectorScopeHost");
            GameObject directorRoot = new GameObject("Spec19_7_Director");
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>();
                ShapeDirector director = directorRoot.AddComponent<ShapeDirector>();
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, host, director))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    MethodInfo commit = typeof(ShapeDirector).GetMethod("CommitCurrentPhysicalShapes", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(commit, Is.Not.Null);
                    commit.Invoke(director, new object[] { new List<ShapeSyncShape>() });
                    Assert.That(scope.ArtifactSet, Is.Null);
                    Assert.That(scope.LastDiagnostic.domainCode, Is.EqualTo("HotBakeArtifactDirectorInvalidated"));
                    Assert.That(set.IsAvailable, Is.False);
                }
            }
            finally
            {
                if (directorRoot != null) UnityEngine.Object.DestroyImmediate(directorRoot);
                if (hostRoot != null) UnityEngine.Object.DestroyImmediate(hostRoot);
                fixture.Result?.Dispose();
                fixture.CleanupUntransferred();
            }
        }

        [Test]
        public void SceneScope_OutfitTopologyBoundary_ImmediatelyReleasesArtifact()
        {
            BuildFixture fixture = CreateFixture(); GameObject hostRoot = new GameObject("Spec19_7_OutfitScopeHost"); GameObject outfitRoot = new GameObject("Spec19_7_OutfitAttacher");
            try
            {
                TextureStackMachineHost host = hostRoot.AddComponent<TextureStackMachineHost>(); OutfitAttacher attacher = outfitRoot.AddComponent<OutfitAttacher>();
                Assert.That(HotBakeArtifactSet.TryCreate(fixture.Result, null, out HotBakeArtifactSet set, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                using (var scope = new HotBakeArtifactSceneScope(fixture.Root, host, null, attacher))
                {
                    Assert.That(scope.TrySetArtifact(set, out diagnostic), Is.True, diagnostic?.message);
                    typeof(OutfitAttacher).GetMethod("NotifyTopologyChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(attacher, null);
                    Assert.That(scope.LastDiagnostic.domainCode, Is.EqualTo("HotBakeArtifactOutfitInvalidated")); Assert.That(set.IsAvailable, Is.False);
                }
            }
            finally { if (outfitRoot != null) UnityEngine.Object.DestroyImmediate(outfitRoot); if (hostRoot != null) UnityEngine.Object.DestroyImmediate(hostRoot); fixture.Result?.Dispose(); fixture.CleanupUntransferred(); }
        }

        private static BuildFixture CreateFixture()
        {
            var fixture = new BuildFixture();
            fixture.Root = new GameObject("Spec19_7_ArtifactCandidate");
            fixture.Mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            fixture.Material = new Material(Shader.Find("Sprites/Default"));
            fixture.Root.AddComponent<SkinnedMeshRenderer>().sharedMesh = fixture.Mesh;
            var carrier = new InMemoryHumanoidMesh(fixture.Root, fixture.Mesh, null);
            MethodInfo setMaterials = typeof(InMemoryHumanoidMesh).GetMethod("TrySetMaterials", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(setMaterials, Is.Not.Null);
            object[] arguments = { new[] { fixture.Material }, null };
            Assert.That((bool)setMaterials.Invoke(carrier, arguments), Is.True, ((StackMachineDiagnostic)arguments[1])?.message);
            fixture.Texture = new Texture2D(4, 2, TextureFormat.RGBAHalf, false, true);
            MethodInfo addTexture = typeof(InMemoryHumanoidMesh).GetMethod("AddOwnedTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(addTexture, Is.Not.Null);
            addTexture.Invoke(carrier, new object[] { new HumanoidOwnedTexture(fixture.Texture, _ => fixture.TextureReleaseCount++) });
            fixture.Result = CreateBuildResult(carrier);
            return fixture;
        }

        private static HumanoidBuildResult CreateBuildResult(InMemoryHumanoidMesh carrier)
        {
            ConstructorInfo resultConstructor = typeof(HumanoidBuildResult).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(InMemoryHumanoidMesh) }, null);
            Assert.That(resultConstructor, Is.Not.Null);
            return (HumanoidBuildResult)resultConstructor.Invoke(new object[] { carrier });
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int DisposeCount { get; private set; }
            public void Dispose() { DisposeCount++; }
        }

        private sealed class BuildFixture
        {
            public GameObject Root;
            public Mesh Mesh;
            public Material Material;
            public Texture2D Texture;
            public RenderTexture AtlasTexture;
            public HumanoidBuildResult Result;
            public int TextureReleaseCount;
            public int AtlasTextureReleaseCount;
            public void CleanupUntransferred()
            {
                if (AtlasTexture != null) UnityEngine.Object.DestroyImmediate(AtlasTexture);
                if (Texture != null) UnityEngine.Object.DestroyImmediate(Texture);
                if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
                if (Mesh != null) UnityEngine.Object.DestroyImmediate(Mesh);
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
            }
        }
    }
}
