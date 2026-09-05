// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync.Tests.PlayMode
{
    public sealed class RuntimeAnimatorResolverTests
    {
        [Test]
        public void TryResolve_SkipsDisabledNearestAnimatorAndUsesFirstEnabledAncestor()
        {
            GameObject root = new GameObject("AnimatorResolverRoot");
            GameObject figure = new GameObject("AnimatorResolverFigure");
            GameObject component = new GameObject("AnimatorResolverComponent");
            try
            {
                figure.transform.SetParent(root.transform);
                component.transform.SetParent(figure.transform);
                Animator controllerAnimator = root.AddComponent<Animator>();
                Animator figureAnimator = figure.AddComponent<Animator>();
                figureAnimator.enabled = false;

                Assert.That(ShapeSyncAnimatorResolver.TryResolve(component.transform, out Animator resolved, out StackMachineDiagnostic diagnostic), Is.True);
                Assert.That(resolved, Is.SameAs(controllerAnimator));
                Assert.That(diagnostic, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryResolve_AllDisabledAnimatorsFallsBackToNearestWithStructuredDiagnostic()
        {
            GameObject root = new GameObject("AnimatorResolverRoot");
            GameObject figure = new GameObject("AnimatorResolverFigure");
            try
            {
                figure.transform.SetParent(root.transform);
                Animator rootAnimator = root.AddComponent<Animator>();
                Animator figureAnimator = figure.AddComponent<Animator>();
                rootAnimator.enabled = false;
                figureAnimator.enabled = false;

                Assert.That(ShapeSyncAnimatorResolver.TryResolve(figure.transform, out Animator resolved, out StackMachineDiagnostic diagnostic), Is.True);
                Assert.That(resolved, Is.SameAs(figureAnimator));
                Assert.That(diagnostic, Is.Not.Null);
                Assert.That(diagnostic.domain, Is.EqualTo("humanoid"));
                Assert.That(diagnostic.domainCode, Is.EqualTo("AnimatorAllDisabledFallback"));
                Assert.That(diagnostic.detail, Is.EqualTo(figure.name));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryResolve_IncludesInactiveAncestors()
        {
            GameObject root = new GameObject("AnimatorResolverRoot");
            GameObject component = new GameObject("AnimatorResolverComponent");
            try
            {
                component.transform.SetParent(root.transform);
                Animator rootAnimator = root.AddComponent<Animator>();
                component.SetActive(false);

                Assert.That(ShapeSyncAnimatorResolver.TryResolve(component.transform, out Animator resolved, out StackMachineDiagnostic diagnostic), Is.True);
                Assert.That(resolved, Is.SameAs(rootAnimator));
                Assert.That(diagnostic, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryResolve_ReportsStructuredDiagnosticWhenNoAnimatorExists()
        {
            GameObject component = new GameObject("AnimatorResolverComponent");
            try
            {
                Assert.That(ShapeSyncAnimatorResolver.TryResolve(component.transform, out Animator resolved, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(resolved, Is.Null);
                Assert.That(diagnostic, Is.Not.Null);
                Assert.That(diagnostic.domain, Is.EqualTo("humanoid"));
                Assert.That(diagnostic.domainCode, Is.EqualTo("AnimatorRequired"));
                Assert.That(diagnostic.detail, Is.EqualTo(component.name));
            }
            finally
            {
                Object.DestroyImmediate(component);
            }
        }

        [Test]
        public void DynamicBoneBlender_ResetUsesFirstEnabledAncestorAnimator()
        {
            GameObject controller = new GameObject("AnimatorResolverController");
            GameObject figure = new GameObject("AnimatorResolverFigure");
            try
            {
                figure.transform.SetParent(controller.transform);
                Animator controllerAnimator = controller.AddComponent<Animator>();
                Animator figureAnimator = figure.AddComponent<Animator>();
                figureAnimator.enabled = false;
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();

                InvokePrivate(blender, "Reset");

                Assert.That(GetPrivateField<Animator>(blender, "targetAnimator"), Is.SameAs(controllerAnimator));
                Assert.That(blender.LastAnimatorDiagnostic, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void DynamicBoneBlender_AwakeResolvesAncestorAnimatorWhenSerializedReferenceIsMissing()
        {
            GameObject controller = new GameObject("ThirdPersonController");
            GameObject figure = new GameObject("ShapeSyncFigure");
            try
            {
                figure.transform.SetParent(controller.transform);
                Animator controllerAnimator = controller.AddComponent<Animator>();
                DynamicBoneBlender blender = figure.AddComponent<DynamicBoneBlender>();

                SetPrivateField(blender, "targetAnimator", null);
                InvokePrivate(blender, "Awake");

                Assert.That(GetPrivateField<Animator>(blender, "targetAnimator"), Is.SameAs(controllerAnimator));
                Assert.That(blender.LastAnimatorDiagnostic, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void OutfitAttacher_AwakePreservesExplicitAnimatorAndResolvesAncestorWhenUnassigned()
        {
            GameObject controller = new GameObject("AnimatorResolverController");
            GameObject figure = new GameObject("AnimatorResolverFigure");
            try
            {
                figure.transform.SetParent(controller.transform);
                Animator controllerAnimator = controller.AddComponent<Animator>();
                Animator figureAnimator = figure.AddComponent<Animator>();
                figureAnimator.enabled = false;
                OutfitAttacher attacher = figure.AddComponent<OutfitAttacher>();

                InvokePrivate(attacher, "Awake");
                Assert.That(GetPrivateField<Animator>(attacher, "figureAnimator"), Is.SameAs(controllerAnimator));

                SetPrivateField(attacher, "figureAnimator", figureAnimator);
                InvokePrivate(attacher, "Awake");
                Assert.That(GetPrivateField<Animator>(attacher, "figureAnimator"), Is.SameAs(figureAnimator));
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        private static T GetPrivateField<T>(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(instance, value);
        }

        private static void InvokePrivate(object instance, string name)
        {
            MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(instance, null);
        }

    }
}
