// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;

namespace zgock.ShapeSync.StackMachine.Tests.Spec18
{
    /// <summary>Focuses the small Spec18.6 Window surface without re-testing controller or publish ownership.</summary>
    public sealed class AtlasCompilerWindowTests
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void ProgressPhase_MapsAtlasToBakingAtlasWithoutChangingExistingBuildLabels()
        {
            Assert.That(Format(HumanoidBuildProgressPhase.Mesh), Is.EqualTo("Building Mesh"));
            Assert.That(Format(HumanoidBuildProgressPhase.Material), Is.EqualTo("Building Material"));
            Assert.That(Format(HumanoidBuildProgressPhase.Atlas), Is.EqualTo("Baking Atlas"));
        }

        [Test]
        public void AtlasDiagnostic_IsRenderedThroughWindowFailureWarningAndDialog()
        {
            var window = ScriptableObject.CreateInstance<HumanoidCompilerWindow>();
            FieldInfo dialog = typeof(HumanoidCompilerWindow).GetField("ShowDialog", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = (System.Action<string, string, string>)dialog.GetValue(null); string title = null; string message = null;
            try
            {
                dialog.SetValue(null, new System.Action<string, string, string>((shownTitle, shownMessage, _) => { title = shownTitle; message = shownMessage; }));
                typeof(HumanoidCompilerWindow).GetMethod("ReportFailure", Flags).Invoke(window, new object[] { StackMachineDiagnostic.CreateDomain("atlas", "AtlasPageStartRejected", "Atlas page execution was rejected.") });
                Assert.That(GetField<string>(window, "progress"), Is.EqualTo("Failed")); Assert.That(GetField<string>(window, "warning"), Does.Contain("AtlasPageStartRejected")); Assert.That(message, Does.Contain("Atlas page execution was rejected.")); Assert.That(title, Is.EqualTo("Humanoid Compiler Failed"));
            }
            finally { dialog.SetValue(null, previous); Object.DestroyImmediate(window); }
        }

        private static string Format(HumanoidBuildProgressPhase phase) => (string)typeof(HumanoidCompilerWindow).GetMethod("FormatProgress", Flags).Invoke(null, new object[] { phase });
        private static T GetField<T>(object target, string field) => (T)target.GetType().GetField(field, Flags).GetValue(target);
    }
}
