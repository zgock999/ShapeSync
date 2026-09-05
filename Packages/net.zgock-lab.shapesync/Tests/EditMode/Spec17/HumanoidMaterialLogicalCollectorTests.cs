// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync;
using zgock.ShapeSync.Materials;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class HumanoidMaterialLogicalCollectorTests
    {
        [Test]
        public void TryCreate_CollectsFigureEntriesAcrossMultipleRenderersWithoutMutation()
        {
            var fixture = new Fixture();
            try
            {
                fixture.AddEntry(fixture.figure, "body", false);
                fixture.AddEntry(fixture.figure, "detail", true);
                var document = new ShapeSyncDocument { MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 1 1 1 1 COLOR $detail MATERIAL 0.5 0.5 0.5 1 COLOR" } };
                Material[] rootBefore = fixture.figure.GetComponent<SkinnedMeshRenderer>().sharedMaterials;
                Material[] childBefore = fixture.figure.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>().sharedMaterials;
                MaterialProxyEntry[] entriesBefore = fixture.GetEntries(fixture.figure).ToArray();
                string[] entryNamesBefore = { entriesBefore[0].entryName, entriesBefore[1].entryName };
                SkinnedMeshRenderer[] entryRenderersBefore = { entriesBefore[0].renderer, entriesBefore[1].renderer };
                MaterialShaderAdapter[] adaptersBefore = { entriesBefore[0].adapter, entriesBefore[1].adapter };

                Assert.That(HumanoidMaterialLogicalCollector.TryCreate(fixture.figure, document, out HumanoidMaterialLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Targets, Has.Count.EqualTo(1));
                Assert.That(plan.Targets[0].Entries, Has.Count.EqualTo(2));
                Assert.That(plan.Targets[0].Entries[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
                Assert.That(plan.Targets[0].Entries[1].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "detail")));
                Assert.That(fixture.figure.GetComponent<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(rootBefore));
                Assert.That(fixture.figure.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>().sharedMaterials, Is.EqualTo(childBefore));
                Assert.That(fixture.GetEntries(fixture.figure).ToArray(), Is.EqualTo(entriesBefore));
                Assert.That(fixture.GetEntries(fixture.figure)[0].entryName, Is.EqualTo(entryNamesBefore[0]));
                Assert.That(fixture.GetEntries(fixture.figure)[1].entryName, Is.EqualTo(entryNamesBefore[1]));
                Assert.That(fixture.GetEntries(fixture.figure)[0].renderer, Is.SameAs(entryRenderersBefore[0]));
                Assert.That(fixture.GetEntries(fixture.figure)[1].renderer, Is.SameAs(entryRenderersBefore[1]));
                Assert.That(fixture.GetEntries(fixture.figure)[0].adapter, Is.SameAs(adaptersBefore[0]));
                Assert.That(fixture.GetEntries(fixture.figure)[1].adapter, Is.SameAs(adaptersBefore[1]));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void TryCreate_ResolvesFigureAndOutfitTargetScopesByMaterialId()
        {
            var fixture = new Fixture();
            try
            {
                fixture.AddEntry(fixture.figure, "body", false);
                GameObject outfit = new GameObject("outfit");
                fixture.objects.Add(outfit);
                ShapeSyncOutfit outfitComponent = outfit.AddComponent<ShapeSyncOutfit>();
                typeof(ShapeSyncOutfit).GetField("registryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outfitComponent, "hat.registry");
                fixture.AddEntry(outfit, "hat", false);
                var meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                fixture.objects.Add(meshBinding);
                typeof(MeshBinding).GetField("outfits", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(meshBinding, new List<MeshOutfitBindingEntry> { new MeshOutfitBindingEntry { logicalName = "hat", outfitPrefab = outfit } });
                var document = new ShapeSyncDocument
                {
                    MeshBinding = meshBinding,
                    MaterialRecipe = new MaterialRecipeDocument { wordSource = "FIGURE $body MATERIAL 1 1 1 1 COLOR $hat.registry OUTFIT $hat MATERIAL 1 1 1 1 COLOR" }
                };

                Assert.That(HumanoidMaterialLogicalCollector.TryCreate(fixture.figure, document, out HumanoidMaterialLogicalPlan plan, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(plan.Targets, Has.Count.EqualTo(2));
                Assert.That(plan.Targets[0].RegistryId, Is.Empty);
                Assert.That(plan.Targets[1].RegistryId, Is.EqualTo("hat.registry"));
                Assert.That(plan.Targets[1].Entries[0].MaterialId, Is.EqualTo(new MaterialId("hat.registry", "hat")));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void TryCreate_RejectsMissingScopedEntryWithoutCreatingMaterial()
        {
            var fixture = new Fixture();
            try
            {
                fixture.AddEntry(fixture.figure, "body", false);
                var document = new ShapeSyncDocument { MaterialRecipe = new MaterialRecipeDocument { wordSource = "$missing MATERIAL 1 1 1 1 COLOR" } };

                Assert.That(HumanoidMaterialLogicalCollector.TryCreate(fixture.figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialBindingMissing"));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void TryCreate_RejectsExternalRendererDuplicateEntryAndDuplicateChannel()
        {
            AssertReject("MaterialProxyRendererMismatch", fixture =>
            {
                GameObject external = new GameObject("external");
                fixture.objects.Add(external);
                MaterialProxyEntry externalEntry = fixture.AddEntry(external, "external", false);
                fixture.SetEntries(fixture.figure, new List<MaterialProxyEntry> { externalEntry });
            });
            AssertReject("MaterialProxyEntryInvalid", fixture =>
            {
                MaterialProxyEntry first = fixture.AddEntry(fixture.figure, "body", false);
                MaterialProxyEntry second = fixture.AddEntry(fixture.figure, "body", true);
                fixture.SetEntries(fixture.figure, new List<MaterialProxyEntry> { first, second });
            });
            AssertReject("MaterialProxyChannelDuplicate", fixture =>
            {
                MaterialProxyEntry first = fixture.AddEntry(fixture.figure, "body", false);
                MaterialProxyEntry second = fixture.AddEntry(fixture.figure, "detail", true);
                second.renderer = first.renderer;
                second.materialChannel = first.materialChannel;
                fixture.SetEntries(fixture.figure, new List<MaterialProxyEntry> { first, second });
            });
        }

        [Test]
        public void TryCreate_RejectsMissingSourceMaterialAdapterAndTextureBinding()
        {
            AssertReject("MaterialProxySourceMaterialMissing", fixture =>
            {
                MaterialProxyEntry entry = fixture.AddEntry(fixture.figure, "body", false);
                entry.renderer.sharedMaterial = null;
            });
            AssertReject("MaterialProxyAdapterMissing", fixture =>
            {
                MaterialProxyEntry entry = fixture.AddEntry(fixture.figure, "body", false);
                entry.adapter = null;
            });
            var textureFixture = new Fixture();
            try
            {
                textureFixture.AddEntry(textureFixture.figure, "body", false);
                var document = new ShapeSyncDocument { MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE $source CANVAS . ENDTEXTURE" } };
                Assert.That(HumanoidMaterialLogicalCollector.TryCreate(textureFixture.figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("MaterialBindingRequired"));
            }
            finally { textureFixture.Dispose(); }
        }

        [Test]
        public void TryCreate_RejectsDuplicateOutfitRegistryBeforeScopeCollection()
        {
            var fixture = new Fixture();
            try
            {
                fixture.AddEntry(fixture.figure, "body", false);
                GameObject first = fixture.CreateOutfit("first", "hat.registry");
                GameObject second = fixture.CreateOutfit("second", "hat.registry");
                var meshBinding = ScriptableObject.CreateInstance<MeshBinding>();
                fixture.objects.Add(meshBinding);
                typeof(MeshBinding).GetField("outfits", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(meshBinding, new List<MeshOutfitBindingEntry>
                {
                    new MeshOutfitBindingEntry { logicalName = "first", outfitPrefab = first },
                    new MeshOutfitBindingEntry { logicalName = "second", outfitPrefab = second }
                });
                var document = new ShapeSyncDocument { MeshBinding = meshBinding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$hat.registry OUTFIT $hat MATERIAL 1 1 1 1 COLOR" } };

                Assert.That(HumanoidMaterialLogicalCollector.TryCreate(fixture.figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("DuplicateRegistryId"));
            }
            finally { fixture.Dispose(); }
        }

        private static void AssertReject(string expectedCode, System.Action<Fixture> configure)
        {
            var fixture = new Fixture();
            try
            {
                configure(fixture);
                var document = new ShapeSyncDocument { MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 1 1 1 1 COLOR" } };
                Assert.That(HumanoidMaterialLogicalCollector.TryCreate(fixture.figure, document, out _, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo(expectedCode));
            }
            finally { fixture.Dispose(); }
        }

        private sealed class Fixture : System.IDisposable
        {
            internal readonly List<Object> objects = new List<Object>();
            internal readonly GameObject figure = new GameObject("figure");
            private readonly MaterialShaderAdapter adapter;

            internal Fixture()
            {
                objects.Add(figure);
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                objects.Add(adapter);
                figure.AddComponent<MaterialProxy>();
            }

            internal MaterialProxyEntry AddEntry(GameObject root, string entryName, bool child)
            {
                GameObject owner = root;
                if (child)
                {
                    owner = new GameObject(entryName + "Renderer");
                    owner.transform.SetParent(root.transform, false);
                    objects.Add(owner);
                }
                SkinnedMeshRenderer renderer = owner.AddComponent<SkinnedMeshRenderer>();
                Material material = new Material(Shader.Find("Unlit/Color"));
                objects.Add(material);
                renderer.sharedMaterial = material;
                MaterialProxy proxy = root.GetComponent<MaterialProxy>() ?? root.AddComponent<MaterialProxy>();
                var entry = new MaterialProxyEntry { entryName = entryName, renderer = renderer, materialChannel = 0, adapter = adapter };
                var entries = new List<MaterialProxyEntry>(proxy.Entries) { entry };
                SetEntries(root, entries);
                return entry;
            }

            internal IReadOnlyList<MaterialProxyEntry> GetEntries(GameObject root) => root.GetComponent<MaterialProxy>().Entries;
            internal void SetEntries(GameObject root, List<MaterialProxyEntry> entries) => typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(root.GetComponent<MaterialProxy>() ?? root.AddComponent<MaterialProxy>(), entries);

            internal GameObject CreateOutfit(string name, string registryId)
            {
                var outfit = new GameObject(name);
                objects.Add(outfit);
                ShapeSyncOutfit component = outfit.AddComponent<ShapeSyncOutfit>();
                typeof(ShapeSyncOutfit).GetField("registryId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, registryId);
                AddEntry(outfit, "hat", false);
                return outfit;
            }

            public void Dispose()
            {
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
        }
    }
}
