// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.EditMode
{
    public sealed class MaterialStackMachineDispatchOperationTests
    {
        [Test]
        public void CompletionRoute_EmptyPayloadIsSynchronousNoOp()
        {
            var gameObject = new GameObject("material-machine");
            try
            {
                var machine = gameObject.AddComponent<MaterialStackMachine>();
                var payload = new ShapeSyncDocument { MaterialRecipe = new MaterialRecipeDocument { wordSource = "  \n" } };
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation, Is.Null);
            }
            finally { Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void CompletionRoute_RejectsNullPayloadWithoutOperation()
        {
            var gameObject = new GameObject("material-machine");
            try
            {
                var machine = gameObject.AddComponent<MaterialStackMachine>();
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(null, out MaterialStackMachineDispatchOperation operation, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(operation, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("PayloadRequired"));
            }
            finally { Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void CompletionRoute_RejectsInvalidTargetScopeWithoutOperation()
        {
            var gameObject = new GameObject("material-machine"); var binding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                var machine = gameObject.AddComponent<MaterialStackMachine>();
                var payload = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "OUTFIT" } };
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation operation, out _), Is.False);
                Assert.That(operation, Is.Null);
            }
            finally { Object.DestroyImmediate(binding); Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void CompletionRoute_ZeroTargetScopeIsSynchronousNoOp()
        {
            var gameObject = new GameObject("material-machine"); var binding = ScriptableObject.CreateInstance<MaterialBinding>();
            try
            {
                var machine = gameObject.AddComponent<MaterialStackMachine>();
                var payload = new ShapeSyncDocument { MaterialBinding = binding, MaterialRecipe = new MaterialRecipeDocument { wordSource = "FIGURE" } };
                Assert.That(machine.TryAcceptRecipePayloadWithCompletion(payload, out MaterialStackMachineDispatchOperation operation, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(operation, Is.Null);
            }
            finally { Object.DestroyImmediate(binding); Object.DestroyImmediate(gameObject); }
        }


    }
}
