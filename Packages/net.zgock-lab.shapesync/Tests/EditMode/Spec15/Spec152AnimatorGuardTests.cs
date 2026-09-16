// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.EditMode
{
    /// <summary>
    /// Spec15-2 §4 guard acceptance A01-A03 (generic Animator part). Ported from the research
    /// AnimatorProbe (Docs/codex/Research/Spec15Warnings/AnimatorProbe.cs): the guarded side keeps the
    /// probe case grid but restores through the real DynamicBoneBlender Capture/Restore via reflection,
    /// and the unguarded side uses the pre-guard unconditional-setter replica only. The real-Humanoid
    /// and Director parts of A03 stay in Phase 09 per Spec15-2 §15.2.
    /// </summary>
    public sealed class Spec152AnimatorGuardTests
    {
        // Spec15-2 §15.2 A01: floats compare within ±0.0001; int/bool/state identity compare exact.
        const float FloatTolerance = 0.0001f;

        // Warning text exactly as observed by the research probe callback (one per unguarded write).
        const string CurveWarning = "Parameter 'Curve' is controlled by a curve.";

        Scene testScene;
        GameObject probeObject;
        Animator animator;
        DynamicBoneBlender ddb;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        MethodInfo captureMethod;
        MethodInfo restoreMethod;

        sealed class Observed
        {
            public int stateHash;
            public float normalizedTime;
            public bool inTransition;
            public float overlayWeight;
            public float plain;
            public int integer;
            public bool flag;
            public float curve;
        }

        [SetUp]
        public void SetUp()
        {
            // Phase04-1 §4: Preview Scene fixture. The Test Framework's unsaved bootstrap scene makes
            // Additive NewScene throw (QA Q-004). Move the GameObject into the preview immediately and
            // do not activate the preview. The runner bootstrap scene is not saved, closed, or replaced.
            try
            {
                testScene = EditorSceneManager.NewPreviewScene();
                probeObject = new GameObject("Spec152AnimatorGuardProbe");
                SceneManager.MoveGameObjectToScene(probeObject, testScene);
                animator = probeObject.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                ddb = probeObject.AddComponent<DynamicBoneBlender>();
                captureMethod = typeof(DynamicBoneBlender).GetMethod(
                    "CaptureAnimatorParameters", BindingFlags.NonPublic | BindingFlags.Instance);
                restoreMethod = typeof(DynamicBoneBlender).GetMethod(
                    "RestoreAnimatorParameters", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(captureMethod, Is.Not.Null, "DynamicBoneBlender.CaptureAnimatorParameters via reflection");
                Assert.That(restoreMethod, Is.Not.Null, "DynamicBoneBlender.RestoreAnimatorParameters via reflection");
            }
            catch
            {
                CleanupFixture();
                throw;
            }
        }

        [TearDown]
        public void TearDown()
        {
            CleanupFixture();
        }

        void CleanupFixture()
        {
            try
            {
                if (probeObject != null) UnityEngine.Object.DestroyImmediate(probeObject);
            }
            finally
            {
                probeObject = null;
                animator = null;
                ddb = null;
                try
                {
                    for (int i = owned.Count - 1; i >= 0; --i)
                        if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
                }
                finally
                {
                    owned.Clear();
                    try
                    {
                        if (testScene.IsValid()) EditorSceneManager.ClosePreviewScene(testScene);
                    }
                    finally
                    {
                        testScene = default;
                    }
                }
            }
        }

        // ---- A01 ----

        [Test]
        public void Spec152_A01_CurveGuard_LeavesPlainParameters()
        {
            // §15.2 A01 / §15.5: curve expected value 0.75 for the Base-only WithCurve condition;
            // guarded restore is the real DynamicBoneBlender Capture/Restore via reflection.
            animator.runtimeAnimatorController = BuildController(true);
            LogWarningCounter counter = new LogWarningCounter();
            try
            {
                Observed observed = RunCondition(0f, true, "WithCurve", true);

                Assert.That(counter.Count, Is.EqualTo(0), "guarded restore must emit no matching warning output");
                Assert.That(observed.plain, Is.EqualTo(0.42f).Within(FloatTolerance), "plain float retained");
                Assert.That(observed.integer, Is.EqualTo(7), "int retained");
                Assert.That(observed.flag, Is.True, "bool retained");
                Assert.That(observed.curve, Is.EqualTo(0.75f).Within(FloatTolerance), "curve keeps its evaluated value");
            }
            finally { Application.logMessageReceived -= counter.Handler; }
        }

        [Test]
        public void Spec152_A01_CurveGuard_ControlNoCurveSetterRestores()
        {
            // §15.2 A01 / §15.5: the curve-less control controller proves the setter restore path runs.
            // Inputs are set through plain setters (nothing is curve-controlled here), Rebind resets the
            // parameters to defaults, and the real restore writes the captured values back.
            animator.runtimeAnimatorController = BuildController(false);
            LogWarningCounter counter = new LogWarningCounter();
            try
            {
                animator.SetFloat("Plain", .42f);
                animator.SetInteger("Integer", 7);
                animator.SetBool("Flag", true);
                animator.SetFloat("Curve", .75f);

                Array snapshots = (Array)captureMethod.Invoke(ddb, new object[] { animator });
                Assert.That(snapshots, Is.Not.Null, "capture returned snapshots");
                Assert.That(snapshots.Length, Is.EqualTo(4), "control Controller has 4 parameters");

                animator.Rebind();
                Assert.That(animator.GetFloat("Plain"), Is.EqualTo(0f).Within(FloatTolerance), "Rebind resets plain float");
                Assert.That(animator.GetInteger("Integer"), Is.EqualTo(0), "Rebind resets int");
                Assert.That(animator.GetBool("Flag"), Is.False, "Rebind resets bool");
                Assert.That(animator.GetFloat("Curve"), Is.EqualTo(0f).Within(FloatTolerance), "Rebind resets curve");

                restoreMethod.Invoke(ddb, new object[] { animator, snapshots });

                Assert.That(counter.Count, Is.EqualTo(0), "control has no curve-controlled parameters");
                Assert.That(animator.GetFloat("Plain"), Is.EqualTo(0.42f).Within(FloatTolerance), "setter restore ran");
                Assert.That(animator.GetInteger("Integer"), Is.EqualTo(7), "setter restore ran");
                Assert.That(animator.GetBool("Flag"), Is.True, "setter restore ran");
                Assert.That(animator.GetFloat("Curve"), Is.EqualTo(0.75f).Within(FloatTolerance), "setter restore ran");
            }
            finally { Application.logMessageReceived -= counter.Handler; }
        }

        // ---- A02: static phases × overlay weight × preserve × guard (16 conditions) ----

        [TestCase(0f, false, "NoCurve")]
        [TestCase(0f, true, "NoCurve")]
        [TestCase(0f, false, "WithCurve")]
        [TestCase(0f, true, "WithCurve")]
        [TestCase(1f, false, "NoCurve")]
        [TestCase(1f, true, "NoCurve")]
        [TestCase(1f, false, "WithCurve")]
        [TestCase(1f, true, "WithCurve")]
        public void Spec152_A02_CurveGuard_BothPreserveSettings(float layerWeight, bool preserve, string phase)
        {
            RunGuardedAgainstReplica(layerWeight, preserve, phase);
        }

        // ---- A03: transition phases × overlay weight × preserve × guard, generic part (16 conditions) ----

        [TestCase(0f, false, "TransitionToCurve")]
        [TestCase(0f, true, "TransitionToCurve")]
        [TestCase(0f, false, "TransitionFromCurve")]
        [TestCase(0f, true, "TransitionFromCurve")]
        [TestCase(1f, false, "TransitionToCurve")]
        [TestCase(1f, true, "TransitionToCurve")]
        [TestCase(1f, false, "TransitionFromCurve")]
        [TestCase(1f, true, "TransitionFromCurve")]
        public void Spec152_A03_CurveGuard_TransitionsAndLayers(float layerWeight, bool preserve, string phase)
        {
            RunGuardedAgainstReplica(layerWeight, preserve, phase);
        }

        void RunGuardedAgainstReplica(float layerWeight, bool preserve, string phase)
        {
            animator.runtimeAnimatorController = BuildController(true);

            // Phase04-1 §6: unguarded must emit exactly 1 warning, guarded 0.
            // LogAssert is not used because the expected literal (Parameter 'Curve') does not match the
            // observed output text (Parameter 'Hash -1772588365'); the count check via
            // Application.logMessageReceived verifies both sides instead. LogAssert's general capability
            // is not disputed (it has a Regex overload) - this is a string-representation difference.
            LogWarningCounter unguardedCounter = new LogWarningCounter();
            Observed unguarded;
            try
            {
                unguarded = RunCondition(layerWeight, preserve, phase, false);
            }
            finally { Application.logMessageReceived -= unguardedCounter.Handler; }

            LogWarningCounter counter = new LogWarningCounter();
            Observed guarded;
            try
            {
                guarded = RunCondition(layerWeight, preserve, phase, true);
            }
            finally { Application.logMessageReceived -= counter.Handler; }

            Assert.That(unguardedCounter.Count, Is.EqualTo(1), "unguarded side must emit exactly 1 matching warning");
            Assert.That(counter.Count, Is.EqualTo(0), "guarded side must emit no matching warning output");

            // Phase04-1 §5: fixed-value asserts on both sides so the same mistake on both sides
            // cannot pass by comparison alone.
            Assert.That(unguarded.plain, Is.EqualTo(0.42f).Within(FloatTolerance), "unguarded plain float");
            Assert.That(unguarded.integer, Is.EqualTo(7), "unguarded integer");
            Assert.That(unguarded.flag, Is.True, "unguarded bool");
            Assert.That(guarded.plain, Is.EqualTo(0.42f).Within(FloatTolerance), "guarded plain float");
            Assert.That(guarded.integer, Is.EqualTo(7), "guarded integer");
            Assert.That(guarded.flag, Is.True, "guarded bool");

            Assert.That(guarded.stateHash, Is.EqualTo(unguarded.stateHash), "state hash");
            Assert.That(guarded.normalizedTime, Is.EqualTo(unguarded.normalizedTime).Within(FloatTolerance), "normalizedTime");
            Assert.That(guarded.inTransition, Is.EqualTo(unguarded.inTransition), "transition flag");
            Assert.That(guarded.overlayWeight, Is.EqualTo(unguarded.overlayWeight).Within(FloatTolerance), "layer weight");
            Assert.That(guarded.plain, Is.EqualTo(unguarded.plain).Within(FloatTolerance), "plain float");
            Assert.That(guarded.integer, Is.EqualTo(unguarded.integer), "integer");
            Assert.That(guarded.flag, Is.EqualTo(unguarded.flag), "bool");
            Assert.That(guarded.curve, Is.EqualTo(unguarded.curve).Within(FloatTolerance), "curve vs control side evaluation");
        }

        /// <summary>
        /// Probe case-iteration body (AnimatorProbe.cs:44-78). The guarded side routes capture/restore
        /// through the real DynamicBoneBlender methods via reflection; the unguarded side uses the
        /// pre-guard unconditional-setter replica. JSON rows and Snapshot anonymous objects are not copied.
        /// </summary>
        Observed RunCondition(float layerWeight, bool preserve, string phase, bool guarded)
        {
            animator.Rebind();
            animator.SetLayerWeight(1, layerWeight);
            bool transition = phase.StartsWith("Transition");
            string start = phase == "WithCurve" || phase == "TransitionFromCurve" ? "WithCurve" : "NoCurve";
            animator.Play(start, 0, .3f);
            animator.Update(0f);
            if (transition)
            {
                animator.CrossFade(start == "NoCurve" ? "WithCurve" : "NoCurve", .5f, 0);
                animator.Update(.1f);
            }
            animator.SetFloat("Plain", .42f);
            animator.SetInteger("Integer", 7);
            animator.SetBool("Flag", true);

            Array snapshots = (Array)captureMethod.Invoke(ddb, new object[] { animator });
            AnimatorStateInfo savedState = animator.GetCurrentAnimatorStateInfo(0);

            animator.Rebind();
            if (guarded)
            {
                restoreMethod.Invoke(ddb, new object[] { animator, snapshots });
            }
            else
            {
                LegacyRestoreReplica(animator, snapshots);
            }
            if (preserve) animator.Play(savedState.fullPathHash, 0, savedState.normalizedTime);
            if (preserve) animator.SetLayerWeight(1, layerWeight);
            animator.Update(0f);

            return Observe();
        }

        static void LegacyRestoreReplica(Animator animator, Array snapshots)
        {
            // Pre-Spec15-2 §4 restore replica: unconditional writes by snapshot type, no guard.
            Type snapshotType = snapshots.GetType().GetElementType();
            FieldInfo nameHashField = snapshotType.GetField("nameHash");
            FieldInfo typeField = snapshotType.GetField("type");
            FieldInfo floatField = snapshotType.GetField("floatValue");
            FieldInfo intField = snapshotType.GetField("intValue");
            FieldInfo boolField = snapshotType.GetField("boolValue");
            foreach (object snapshot in snapshots)
            {
                int nameHash = (int)nameHashField.GetValue(snapshot);
                switch ((AnimatorControllerParameterType)typeField.GetValue(snapshot))
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(nameHash, (float)floatField.GetValue(snapshot));
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(nameHash, (int)intField.GetValue(snapshot));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(nameHash, (bool)boolField.GetValue(snapshot));
                        break;
                }
            }
        }

        Observed Observe()
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            return new Observed
            {
                stateHash = info.fullPathHash,
                normalizedTime = info.normalizedTime,
                inTransition = animator.IsInTransition(0),
                overlayWeight = animator.GetLayerWeight(1),
                plain = animator.GetFloat("Plain"),
                integer = animator.GetInteger("Integer"),
                flag = animator.GetBool("Flag"),
                curve = animator.GetFloat("Curve"),
            };
        }

        // Probe Controller construction (AnimatorProbe.cs:23-43); only fixture variable names changed.
        // withCurves=false is the A01 control Controller without Animator curves (§15.5).
        AnimatorController BuildController(bool withCurves)
        {
            var controller = new AnimatorController();
            owned.Add(controller);
            controller.AddParameter("Curve", AnimatorControllerParameterType.Float);
            controller.AddParameter("Plain", AnimatorControllerParameterType.Float);
            controller.AddParameter("Integer", AnimatorControllerParameterType.Int);
            controller.AddParameter("Flag", AnimatorControllerParameterType.Bool);
            controller.AddLayer("Base");
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            owned.Add(sm);
            AnimationClip curved = new AnimationClip { name = "Curved" };
            owned.Add(curved);
            AnimationClip plainClip = new AnimationClip { name = "Plain" };
            owned.Add(plainClip);
            if (withCurves)
            {
                curved.SetCurve("", typeof(Animator), "Curve", AnimationCurve.Constant(0, 1, .75f));
                plainClip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0, 1, 0));
            }
            AnimatorState s0 = sm.AddState("NoCurve");
            owned.Add(s0);
            s0.motion = plainClip;
            AnimatorState s1 = sm.AddState("WithCurve");
            owned.Add(s1);
            s1.motion = curved;
            sm.defaultState = s0;
            controller.AddLayer("Overlay");
            AnimatorStateMachine overlay = controller.layers[1].stateMachine;
            owned.Add(overlay);
            AnimationClip overlayClip = new AnimationClip { name = "OverlayCurve" };
            owned.Add(overlayClip);
            if (withCurves)
            {
                overlayClip.SetCurve("", typeof(Animator), "Curve", AnimationCurve.Constant(0, 1, .25f));
            }
            AnimatorState overlayState = overlay.AddState("OverlayCurve");
            owned.Add(overlayState);
            overlayState.motion = overlayClip;
            overlay.defaultState = overlayState;
            return controller;
        }

        sealed class LogWarningCounter
        {
            public int Count;
            public Application.LogCallback Handler;

            public LogWarningCounter()
            {
                Handler = (message, stackTrace, type) =>
                {
                    if (type == LogType.Warning && message.Contains("controlled by a curve")) Count++;
                };
                Application.logMessageReceived += Handler;
            }
        }
    }
}
#endif
