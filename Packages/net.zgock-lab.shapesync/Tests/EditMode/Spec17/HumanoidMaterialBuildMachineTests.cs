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
    public sealed class HumanoidMaterialBuildMachineTests
    {
        [Test]
        public void StartPumpAndSingleTake_ProducesColorAndUvPayloadWithoutTextureBackend()
        {
            using (var fixture = new Fixture())
            using (var machine = new FakeMachine())
            {
                var document = new ShapeSyncDocument { MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL 0.2 0.3 0.4 1 COLOR 2 3 0.25 0.5 UVSET" } };
                Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                Assert.That(machine.Pump(out StackMachineDiagnostic pumpDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Succeeded), pumpDiagnostic?.message);
                Assert.That(machine.TryTake(out HumanoidMaterialBuildEscrow<string> escrow), Is.True);
                using (escrow)
                {
                    Assert.That(escrow.Payloads, Has.Count.EqualTo(1));
                    Assert.That(escrow.Payloads[0].MaterialId, Is.EqualTo(new MaterialId(string.Empty, "body")));
                    Assert.That(escrow.Payloads[0].HasMainTex, Is.False);
                    Assert.That(escrow.Payloads[0].HasColor, Is.True);
                    Assert.That(escrow.Payloads[0].Color, Is.EqualTo(new Color(0.2f, 0.3f, 0.4f, 1f)));
                    Assert.That(escrow.Payloads[0].HasUvSet, Is.True);
                    Assert.That(escrow.Payloads[0].UvScale, Is.EqualTo(new Vector2(2f, 3f)));
                    Assert.That(escrow.Payloads[0].UvOffset, Is.EqualTo(new Vector2(0.25f, 0.5f)));
                }
                Assert.That(machine.TryTake(out _), Is.False);
            }
        }

        [Test]
        public void PumpAndCancel_OwnOnlyTakenTextureCompletion()
        {
            using (var fixture = new Fixture())
            using (var machine = new FakeMachine())
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    var document = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE" } };
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic firstDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), firstDiagnostic?.message);
                    Assert.That(machine.Started, Is.EqualTo(1));
                    machine.Complete = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic secondDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Succeeded), secondDiagnostic?.message);
                    Assert.That(machine.TryTake(out HumanoidMaterialBuildEscrow<string> escrow), Is.True);
                    escrow.Dispose();
                    Assert.That(machine.Disposed, Is.EqualTo(1));

                    Assert.That(machine.Start(fixture.Root, document, out startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out firstDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), firstDiagnostic?.message);
                    machine.Cancel();
                    Assert.That(machine.Status, Is.EqualTo(HumanoidMaterialBuildStatus.Cancelled));
                    Assert.That(machine.Cancelled, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        [Test]
        public void FailAfterFirstCompletion_DisposesRetainedPayloadBeforeReportingFailure()
        {
            using (var fixture = new Fixture())
            using (var machine = new FakeMachine { FailStartAt = 2 })
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    var document = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE $other MATERIAL TEXTURE 0 1 0 1 FILL $out COPY DROP ENDTEXTURE" } };
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic firstDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), firstDiagnostic?.message);
                    machine.Complete = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic failureDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Failed));
                    Assert.That(failureDiagnostic.domainCode, Is.EqualTo("FakeStartFailed"));
                    Assert.That(machine.Disposed, Is.EqualTo(1), "The first completion is Machine-owned until a successful single-take.");
                    Assert.That(machine.TryTake(out _), Is.False);
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        [Test]
        public void CancelWithRetainedCompletion_DisposesPayloadAndCancelsActiveTexture()
        {
            using (var fixture = new Fixture())
            using (var machine = new FakeMachine())
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    var document = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE $other MATERIAL TEXTURE 0 1 0 1 FILL $out COPY DROP ENDTEXTURE" } };
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic firstDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), firstDiagnostic?.message);
                    machine.Complete = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic secondDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), secondDiagnostic?.message);
                    Assert.That(machine.Started, Is.EqualTo(2));
                    machine.Cancel();
                    Assert.That(machine.Status, Is.EqualTo(HumanoidMaterialBuildStatus.Cancelled));
                    Assert.That(machine.Disposed, Is.EqualTo(1));
                    Assert.That(machine.Cancelled, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        [Test]
        public void PumpFailureWithRetainedCompletion_DisposesPayloadAndCancelsActiveTexture()
        {
            using (var fixture = new Fixture())
            using (var machine = new FakeMachine())
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    var document = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE $other MATERIAL TEXTURE 0 1 0 1 FILL $out COPY DROP ENDTEXTURE" } };
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic firstDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), firstDiagnostic?.message);
                    machine.Complete = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic secondDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), secondDiagnostic?.message);
                    machine.PumpFails = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic failureDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Failed));
                    Assert.That(failureDiagnostic.domainCode, Is.EqualTo("FakePumpFailed"));
                    Assert.That(machine.Disposed, Is.EqualTo(1));
                    Assert.That(machine.Cancelled, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        [Test]
        public void MissingCompletionWithRetainedPayload_DisposesPayloadAndCancelsActiveTexture()
        {
            using (var fixture = new Fixture())
            using (var machine = new FakeMachine())
            {
                MaterialBinding binding = ScriptableObject.CreateInstance<MaterialBinding>();
                try
                {
                    var document = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "$body MATERIAL TEXTURE 1 0 0 1 FILL $out COPY DROP ENDTEXTURE $other MATERIAL TEXTURE 0 1 0 1 FILL $out COPY DROP ENDTEXTURE" } };
                    Assert.That(machine.Start(fixture.Root, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                    Assert.That(machine.Pump(out StackMachineDiagnostic firstDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), firstDiagnostic?.message);
                    machine.Complete = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic secondDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Pending), secondDiagnostic?.message);
                    machine.TakeFails = true;
                    Assert.That(machine.Pump(out StackMachineDiagnostic failureDiagnostic), Is.EqualTo(HumanoidMaterialBuildStatus.Failed));
                    Assert.That(failureDiagnostic.domainCode, Is.EqualTo("FakeCompletionMissing"));
                    Assert.That(machine.Disposed, Is.EqualTo(1));
                    Assert.That(machine.Cancelled, Is.EqualTo(1));
                }
                finally { Object.DestroyImmediate(binding); }
            }
        }

        private sealed class FakeMachine : HumanoidMaterialBuildMachine<string>
        {
            internal bool Complete;
            internal int Started;
            internal int Cancelled;
            internal int Disposed;
            internal int FailStartAt;
            internal bool PumpFails;
            internal bool TakeFails;
            protected override bool TryStartTexture(TextureExecutionPlan plan, out StackMachineDiagnostic diagnostic) { Started++; if (FailStartAt == Started) { diagnostic = StackMachineDiagnostic.CreateDomain("material", "FakeStartFailed", "Fake Texture start failure."); return false; } diagnostic = null; return true; }
            protected override bool TryPumpTexture(out bool pending, out StackMachineDiagnostic diagnostic) { if (PumpFails) { pending = false; diagnostic = StackMachineDiagnostic.CreateDomain("material", "FakePumpFailed", "Fake Texture pump failure."); return false; } pending = !Complete; diagnostic = null; return true; }
            protected override bool TryTakeTexture(out string completion, out StackMachineDiagnostic diagnostic) { if (TakeFails) { completion = null; diagnostic = StackMachineDiagnostic.CreateDomain("material", "FakeCompletionMissing", "Fake Texture completion missing."); return false; } completion = "completion"; diagnostic = null; return true; }
            protected override void CancelTexture() { Cancelled++; }
            protected override void DisposeTexture(string completion) { if (completion != null) Disposed++; }
        }

        private sealed class Fixture : System.IDisposable
        {
            private readonly Material material;
            private readonly Material otherMaterial;
            private readonly MaterialShaderAdapter adapter;
            internal Fixture()
            {
                Root = new GameObject("material-machine");
                var renderer = Root.AddComponent<SkinnedMeshRenderer>();
                material = new Material(Shader.Find("Unlit/Color"));
                renderer.sharedMaterial = material;
                var other = new GameObject("other");
                other.transform.SetParent(Root.transform);
                var otherRenderer = other.AddComponent<SkinnedMeshRenderer>();
                otherMaterial = new Material(Shader.Find("Unlit/Color"));
                otherRenderer.sharedMaterial = otherMaterial;
                adapter = ScriptableObject.CreateInstance<UrpUnlitMaterialShaderAdapter>();
                MaterialProxy proxy = Root.AddComponent<MaterialProxy>();
                typeof(MaterialProxy).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, new List<MaterialProxyEntry> { new MaterialProxyEntry { entryName = "body", renderer = renderer, materialChannel = 0, adapter = adapter }, new MaterialProxyEntry { entryName = "other", renderer = otherRenderer, materialChannel = 0, adapter = adapter } });
            }
            internal GameObject Root { get; }
            public void Dispose() { Object.DestroyImmediate(adapter); Object.DestroyImmediate(otherMaterial); Object.DestroyImmediate(material); Object.DestroyImmediate(Root); }
        }
    }
}
