// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if SHAPESYNC_USE_UNIVRM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.StackMachine;
using zgock.ShapeSync.StackMachine.Humanoid;
using zgock.ShapeSync.VrmIntegration;
using zgock.ShapeSync.VrmIntegration.Editor;

namespace zgock.ShapeSync.Tests.EditMode.VrmIntegration
{
    public sealed class Spec17VrmIntegrationRuntimeContractTests
    {
        [Test]
        public void HumanoidVrmExecutorProvider_IsRegisteredByOptionalEditorAssembly()
        {
            Assert.That(HumanoidVrmTransportExecutorProvider.IsAvailable, Is.True);
            Assert.That(HumanoidVrmTransportExecutorProvider.TryCreate(out var executor), Is.True);
            Assert.That(executor, Is.TypeOf<HumanoidVrmTransportExecutor>());
        }

        [Test]
        public void CandidateNormalize_RemovesShapeSyncRuntimeComponentAndClonedVrmInstance()
        {
            var candidate = new GameObject("Spec17_PureVrmCleanup"); candidate.SetActive(false);
            try
            {
                Vrm10Instance instance = candidate.AddComponent<Vrm10Instance>();
                candidate.AddComponent<ShapeDirector>();
                Assert.That(HumanoidPureHumanoidComponentStripper.TryNormalize(candidate, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(candidate.GetComponent<Vrm10Instance>(), Is.Null);
                Assert.That(candidate.GetComponent<ShapeDirector>(), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void VrmAssetStager_PersistsExpressionsAndVrmUnderValidatedRelativeFolder()
        {
            const string outputFolder = ShapeSyncTestAssetPaths.Spec17VrmStageRoot;
            var candidate = new GameObject("Spec17_VrmStageCandidate");
            var instance = candidate.AddComponent<Vrm10Instance>();
            var vrm = ScriptableObject.CreateInstance<VRM10Object>(); vrm.name = "InMemoryVrm";
            var expression = ScriptableObject.CreateInstance<VRM10Expression>(); expression.name = "happy";
            VrmTransportPhysicsResult result = CreateResult(instance, vrm, new[] { expression });
            try
            {
                AssetDatabase.DeleteAsset(outputFolder);
                ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/VrmIntegration");
                AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/VrmIntegration"), "__Spec17_6_VrmStage");
                Assert.That(HumanoidVrmAssetStager.TryResolveAssetFolder(outputFolder, ShapeSyncTestAssetPaths.InvalidAssetPath("Forbidden"), out _, out StackMachineDiagnostic invalid), Is.False);
                Assert.That(invalid.domainCode, Is.EqualTo("VrmPublishRelativeFolderInvalid"));
                Assert.That(HumanoidVrmAssetStager.TryStage(outputFolder, "VRM/Initial", "Look", result, out HumanoidVrmAssetStage stage, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(stage.AssetFolder, Is.EqualTo(outputFolder + "/VRM/Initial"));
                Assert.That(stage.IsComplete, Is.True);
                Assert.That(stage.AssetPaths, Is.EqualTo(new[] { outputFolder + "/VRM/Initial/Initial_happy.asset", outputFolder + "/VRM/Initial/Initial_vrm.asset" }));
                Assert.That(AssetDatabase.LoadAssetAtPath<VRM10Expression>(stage.AssetPaths[0]), Is.SameAs(expression));
                Assert.That(AssetDatabase.LoadAssetAtPath<VRM10Object>(stage.AssetPaths[1]), Is.SameAs(vrm));
                var prefabSource = new GameObject("Spec17_VrmStagePrefab");
                prefabSource.AddComponent<Vrm10Instance>();
                string prefabPath = outputFolder + "/Look.prefab";
                PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
                UnityEngine.Object.DestroyImmediate(prefabSource);
                GameObject publishedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var executor = new HumanoidVrmTransportExecutor();
                var sceneOnlyRoot = new GameObject("Spec17_VrmStageSceneOnly");
                Assert.That(executor.TryFinalizeAssets(result, sceneOnlyRoot, out StackMachineDiagnostic sceneDiagnostic), Is.False);
                Assert.That(sceneDiagnostic.domainCode, Is.EqualTo("VrmPublishPrefabRequired"));
                UnityEngine.Object.DestroyImmediate(sceneOnlyRoot);
                Assert.That(executor.TryFinalizeAssets(result, publishedPrefab, out StackMachineDiagnostic finalizeDiagnostic), Is.True, finalizeDiagnostic?.message);
                AssertPrefabAssetIdentity(vrm.Prefab, publishedPrefab, "VRM Prefab reference");
                AssertPrefabAssetIdentity(expression.Prefab, publishedPrefab, "Expression Prefab reference");
                Assert.That(publishedPrefab.GetComponent<Vrm10Instance>().Vrm, Is.SameAs(vrm));
                GameObject reloadedPrefab = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Assert.That(reloadedPrefab.GetComponent<Vrm10Instance>().Vrm, Is.SameAs(vrm));
                    VRM10Object reloadedVrm = AssetDatabase.LoadAssetAtPath<VRM10Object>(stage.AssetPaths[1]);
                    VRM10Expression reloadedExpression = AssetDatabase.LoadAssetAtPath<VRM10Expression>(stage.AssetPaths[0]);
                    AssertPrefabAssetIdentity(reloadedVrm.Prefab, publishedPrefab, "Reloaded VRM Prefab reference");
                    AssertPrefabAssetIdentity(reloadedExpression.Prefab, publishedPrefab, "Reloaded Expression Prefab reference");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(reloadedPrefab);
                }
                Assert.That(result.Vrm, Is.Null);
                result = null;
            }
            finally
            {
                result?.Dispose();
                AssetDatabase.DeleteAsset(outputFolder);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void HumanoidVrmExecutor_PartialStageFailureReleasesPersistentResultOwnership()
        {
            const string outputFolder = ShapeSyncTestAssetPaths.Spec17VrmPartialRoot;
            var candidate = new GameObject("Spec17_VrmPartialCandidate"); var instance = candidate.AddComponent<Vrm10Instance>();
            var vrm = ScriptableObject.CreateInstance<VRM10Object>(); var first = ScriptableObject.CreateInstance<VRM10Expression>(); first.name = "first"; var second = ScriptableObject.CreateInstance<VRM10Expression>(); second.name = "second";
            VrmTransportPhysicsResult result = CreateResult(instance, vrm, new[] { first, second });
            try
            {
                AssetDatabase.DeleteAsset(outputFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/VrmIntegration"), "__Spec17_6_VrmPartial");
                var occupied = ScriptableObject.CreateInstance<VRM10Expression>(); AssetDatabase.CreateAsset(occupied, outputFolder + "/__Spec17_6_VrmPartial_second.asset");
                Assert.That(new HumanoidVrmTransportExecutor().TryStageAssets(result, outputFolder, string.Empty, "Look", out IReadOnlyList<string> paths, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPublishAssetPathOccupied"));
                Assert.That(paths, Is.EqualTo(new[] { outputFolder + "/__Spec17_6_VrmPartial_first.asset" }));
                Assert.That(AssetDatabase.LoadAssetAtPath<VRM10Expression>(paths[0]), Is.SameAs(first));
                Assert.That(result.Vrm, Is.Null);
                Assert.That(second == null, Is.True);
                Assert.That(vrm == null, Is.True);
                result = null;
            }
            finally { result?.Dispose(); AssetDatabase.DeleteAsset(outputFolder); UnityEngine.Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void HumanoidVrmExecutor_MissingPublishedInstanceReleasesPersistentResultOwnership()
        {
            const string outputFolder = ShapeSyncTestAssetPaths.Spec17VrmMissingInstanceRoot;
            var candidate = new GameObject("Spec17_VrmMissingInstanceCandidate"); var instance = candidate.AddComponent<Vrm10Instance>(); var vrm = ScriptableObject.CreateInstance<VRM10Object>();
            VrmTransportPhysicsResult result = CreateResult(instance, vrm, Array.Empty<VRM10Expression>());
            try
            {
                AssetDatabase.DeleteAsset(outputFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/VrmIntegration"), "__Spec17_6_VrmMissingInstance");
                var executor = new HumanoidVrmTransportExecutor(); Assert.That(executor.TryStageAssets(result, outputFolder, string.Empty, "Look", out _, out _), Is.True);
                var source = new GameObject("Spec17_VrmMissingInstancePrefab"); PrefabUtility.SaveAsPrefabAsset(source, outputFolder + "/Look.prefab"); UnityEngine.Object.DestroyImmediate(source);
                Assert.That(executor.TryFinalizeAssets(result, AssetDatabase.LoadAssetAtPath<GameObject>(outputFolder + "/Look.prefab"), out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPublishInstanceRequired")); Assert.That(result.Vrm, Is.Null);
                result = null;
            }
            finally { result?.Dispose(); AssetDatabase.DeleteAsset(outputFolder); UnityEngine.Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void HumanoidVrmExecutor_SaveExceptionReleasesPersistentResultOwnership()
        {
            const string outputFolder = ShapeSyncTestAssetPaths.Spec17VrmSaveExceptionRoot;
            var candidate = new GameObject("Spec17_VrmSaveExceptionCandidate"); var instance = candidate.AddComponent<Vrm10Instance>(); var vrm = ScriptableObject.CreateInstance<VRM10Object>();
            VrmTransportPhysicsResult result = CreateResult(instance, vrm, Array.Empty<VRM10Expression>()); Action previousSaveAssets = GetExecutorSaveAssets();
            try
            {
                AssetDatabase.DeleteAsset(outputFolder); AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerFolderPath("zgock/ShapeSync/Tests/EditMode/VrmIntegration"), "__Spec17_6_VrmSaveException");
                var executor = new HumanoidVrmTransportExecutor(); Assert.That(executor.TryStageAssets(result, outputFolder, string.Empty, "Look", out _, out _), Is.True);
                var source = new GameObject("Spec17_VrmSaveExceptionPrefab"); source.AddComponent<Vrm10Instance>(); PrefabUtility.SaveAsPrefabAsset(source, outputFolder + "/Look.prefab"); UnityEngine.Object.DestroyImmediate(source);
                SetExecutorSaveAssets(() => throw new InvalidOperationException("Injected save failure."));
                Assert.That(executor.TryFinalizeAssets(result, AssetDatabase.LoadAssetAtPath<GameObject>(outputFolder + "/Look.prefab"), out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPublishFinalizeFailed")); Assert.That(result.Vrm, Is.Null);
                result = null;
            }
            finally { SetExecutorSaveAssets(previousSaveAssets); result?.Dispose(); AssetDatabase.DeleteAsset(outputFolder); UnityEngine.Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void Request_SnapshotsAttachedOutfitSourceRoles()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            var outfit = new GameObject("Spec17_Outfit");
            var replacement = new GameObject("Spec17_Replacement");
            try
            {
                var callerList = new List<GameObject> { outfit, replacement };
                var request = new VrmTransportPhysicsRequest(candidate, figure, callerList);
                callerList.Clear();

                Assert.That(request.AttachedOutfitSourceRoots, Has.Count.EqualTo(2));
                Assert.That(request.AttachedOutfitSourceRoots[0], Is.SameAs(outfit));
                Assert.That(request.AttachedOutfitSourceRoots[1], Is.SameAs(replacement));
                Assert.That(request.AttachedOutfitSourceRoots, Is.Not.TypeOf<GameObject[]>());
                var immutableView = request.AttachedOutfitSourceRoots as IList<GameObject>;
                Assert.That(immutableView, Is.Not.Null);
                Assert.That(() => immutableView[0] = candidate, Throws.TypeOf<NotSupportedException>());

                var nullListRequest = new VrmTransportPhysicsRequest(candidate, figure, null);
                var emptyListRequest = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(nullListRequest.AttachedOutfitSourceRoots, Is.Empty);
                Assert.That(emptyListRequest.AttachedOutfitSourceRoots, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
                UnityEngine.Object.DestroyImmediate(outfit);
                UnityEngine.Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void Validator_RejectsMissingCandidateAndFigureSourceRoles()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            try
            {
                Assert.That(TryValidate(new VrmTransportPhysicsRequest(null, figure, Array.Empty<GameObject>()), out StackMachineDiagnostic missingCandidate), Is.False);
                Assert.That(missingCandidate.domainCode, Is.EqualTo("VrmCandidateRequired"));

                Assert.That(TryValidate(new VrmTransportPhysicsRequest(candidate, null, Array.Empty<GameObject>()), out StackMachineDiagnostic missingFigure), Is.False);
                Assert.That(missingFigure.domainCode, Is.EqualTo("VrmFigureSourceRequired"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Validator_RejectsCandidateAsFigureSourceBeforeCandidateMutation()
        {
            var candidate = new GameObject("Spec17_Candidate");
            try
            {
                var request = new VrmTransportPhysicsRequest(candidate, candidate, Array.Empty<GameObject>());
                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domain, Is.EqualTo("vrm"));
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateMustBeDetached"));
                Assert.That(candidate.GetComponent<MonoBehaviour>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void Validator_RejectsCandidateAndSourceHierarchyOverlapBeforeCandidateMutation()
        {
            var figure = new GameObject("Spec17_Figure");
            var candidate = new GameObject("Spec17_Candidate");
            var outfit = new GameObject("Spec17_Outfit");
            try
            {
                candidate.transform.SetParent(figure.transform);
                var childRequest = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(TryValidate(childRequest, out StackMachineDiagnostic childDiagnostic), Is.False);
                Assert.That(childDiagnostic.domainCode, Is.EqualTo("VrmCandidateMustBeDetached"));
                Assert.That(candidate.GetComponent<MonoBehaviour>(), Is.Null);

                candidate.transform.SetParent(null);
                figure.transform.SetParent(candidate.transform);
                var parentRequest = new VrmTransportPhysicsRequest(candidate, figure, new[] { outfit });
                Assert.That(TryValidate(parentRequest, out StackMachineDiagnostic parentDiagnostic), Is.False);
                Assert.That(parentDiagnostic.domainCode, Is.EqualTo("VrmCandidateMustBeDetached"));
                Assert.That(candidate.GetComponent<MonoBehaviour>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
                UnityEngine.Object.DestroyImmediate(outfit);
            }
        }

        [Test]
        public void Validator_RejectsCandidateAndOutfitHierarchyOverlapBeforeCandidateMutation()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            var outfit = new GameObject("Spec17_Outfit");
            try
            {
                outfit.transform.SetParent(candidate.transform);
                var request = new VrmTransportPhysicsRequest(candidate, figure, new[] { outfit });

                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateMustBeDetached"));
                Assert.That(candidate.GetComponent<MonoBehaviour>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
                UnityEngine.Object.DestroyImmediate(outfit);
            }
        }

        [Test]
        public void Validator_RejectsDuplicateFigureAndOutfitSourceRoles()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            try
            {
                var request = new VrmTransportPhysicsRequest(candidate, figure, new[] { figure });
                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmSourceRoleDuplicate"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Validator_RejectsDuplicateOutfitSourceRoles()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            var outfit = new GameObject("Spec17_Outfit");
            try
            {
                var request = new VrmTransportPhysicsRequest(candidate, figure, new[] { outfit, outfit });
                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmSourceRoleDuplicate"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
                UnityEngine.Object.DestroyImmediate(outfit);
            }
        }

        [Test]
        public void Validator_RejectsNullOutfitSourceRole()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            try
            {
                var request = new VrmTransportPhysicsRequest(candidate, figure, new GameObject[] { null });
                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmOutfitSourceRequired"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Validator_AcceptsOneDetachedCandidateHumanoidAnimator()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            Avatar avatar = null;
            try
            {
                avatar = CreateTestHumanoidAvatar(candidate);
                candidate.AddComponent<Animator>().avatar = avatar;
                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());

                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Validator_RejectsZeroAndMultipleCandidateAnimators()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            var secondAnimatorRoot = new GameObject("Spec17_SecondAnimator");
            try
            {
                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(TryValidate(request, out StackMachineDiagnostic noAnimator), Is.False);
                Assert.That(noAnimator.domainCode, Is.EqualTo("VrmCandidateAnimatorAmbiguous"));

                candidate.AddComponent<Animator>();
                secondAnimatorRoot.transform.SetParent(candidate.transform);
                secondAnimatorRoot.AddComponent<Animator>();
                Assert.That(TryValidate(request, out StackMachineDiagnostic multipleAnimators), Is.False);
                Assert.That(multipleAnimators.domainCode, Is.EqualTo("VrmCandidateAnimatorAmbiguous"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
                UnityEngine.Object.DestroyImmediate(secondAnimatorRoot);
            }
        }

        [Test]
        public void Validator_RejectsCandidateWhoseOnlyAnimatorIsNotOnTheCandidateRoot()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            var animatorRoot = new GameObject("Spec17_ChildAnimator");
            Avatar avatar = null;
            try
            {
                avatar = CreateTestHumanoidAvatar(candidate);
                animatorRoot.transform.SetParent(candidate.transform);
                animatorRoot.AddComponent<Animator>().avatar = avatar;

                Assert.That(TryValidate(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateAnimatorAmbiguous"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
                UnityEngine.Object.DestroyImmediate(animatorRoot);
            }
        }

        [Test]
        public void Validator_RejectsMissingAndNonHumanoidAvatar()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            Avatar genericAvatar = null;
            try
            {
                Animator animator = candidate.AddComponent<Animator>();
                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(TryValidate(request, out StackMachineDiagnostic missingAvatar), Is.False);
                Assert.That(missingAvatar.domainCode, Is.EqualTo("VrmCandidateHumanoidRequired"));

                genericAvatar = AvatarBuilder.BuildGenericAvatar(candidate, string.Empty);
                Assert.That(genericAvatar, Is.Not.Null);
                animator.avatar = genericAvatar;
                Assert.That(TryValidate(request, out StackMachineDiagnostic nonHumanoid), Is.False);
                Assert.That(nonHumanoid.domainCode, Is.EqualTo("VrmCandidateHumanoidRequired"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(genericAvatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Validator_RejectsInvalidHumanoidAvatar()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            Avatar invalidAvatar = null;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("Required human bone 'Hips' not found"));
                invalidAvatar = AvatarBuilder.BuildHumanAvatar(candidate, new HumanDescription());
                Assert.That(invalidAvatar, Is.Not.Null);
                Assert.That(invalidAvatar.isValid, Is.False);
                candidate.AddComponent<Animator>().avatar = invalidAvatar;

                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(TryValidate(request, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateHumanoidRequired"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalidAvatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_IsTheOnlyPublicServiceOperation()
        {
            MethodInfo[] methods = typeof(VrmIntegrationService).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.That(methods, Has.Length.EqualTo(1));
            Assert.That(methods[0].Name, Is.EqualTo("TransportPhysics"));
        }

        [Test]
        public void RuntimeServiceAssembly_DoesNotReferenceUnityEditor()
        {
            AssemblyName[] references = typeof(VrmIntegrationService).Assembly.GetReferencedAssemblies();
            for (int i = 0; i < references.Length; i++)
            {
                Assert.That(references[i].Name, Is.Not.EqualTo("UnityEditor"));
            }
        }

        [Test]
        public void Result_ReleaseAssetOwnershipClearsOnlyTheResultView()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var instance = candidate.AddComponent<UniVRM10.Vrm10Instance>();
            var vrm = ScriptableObject.CreateInstance<UniVRM10.VRM10Object>();
            var expression = ScriptableObject.CreateInstance<UniVRM10.VRM10Expression>();
            try
            {
                var result = CreateResult(instance, vrm, new[] { expression });
                Assert.That(result.Expressions, Is.Not.TypeOf<UniVRM10.VRM10Expression[]>());

                result.ReleaseAssetOwnership();

                Assert.That(result.Vrm, Is.Null);
                Assert.That(result.Expressions, Is.Empty);
                Assert.That(instance, Is.Not.Null);
                Assert.That(vrm, Is.Not.Null);
                Assert.That(expression, Is.Not.Null);
                result.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vrm);
                UnityEngine.Object.DestroyImmediate(expression);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void Result_DisposeDestroysOnlyUnpublishedVrmArtifacts()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var instance = candidate.AddComponent<UniVRM10.Vrm10Instance>();
            var vrm = ScriptableObject.CreateInstance<UniVRM10.VRM10Object>();
            var expression = ScriptableObject.CreateInstance<UniVRM10.VRM10Expression>();
            try
            {
                var result = CreateResult(instance, vrm, new[] { expression });
                result.Dispose();

                Assert.That(result.Vrm, Is.Null);
                Assert.That(result.Expressions, Is.Empty);
                Assert.That(vrm == null, Is.True);
                Assert.That(expression == null, Is.True);
                Assert.That(instance, Is.Not.Null);
                Assert.That(candidate.GetComponent<UniVRM10.Vrm10Instance>(), Is.SameAs(instance));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vrm);
                UnityEngine.Object.DestroyImmediate(expression);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_InitializesCandidateAndBuildsOnlyFinalMeshExpressions()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object sourceVrm = null;
            VRM10Expression sourceOnly = null;
            VrmTransportPhysicsResult result = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", new[] { "VRM_happy", "VRM_Custom", "VRM_Another" }, out avatar, out mesh);
                figure = CreateFigureSource(out sourceVrm, out sourceOnly);
                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());

                Assert.That(VrmIntegrationService.TransportPhysics(request, out result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Instance, Is.SameAs(candidate.GetComponent<Vrm10Instance>()));
                Assert.That(result.Instance.Vrm, Is.SameAs(result.Vrm));
                Assert.That(candidate.GetComponent<UniHumanoid.Humanoid>(), Is.Not.Null);
                Assert.That(result.Expressions, Has.Count.EqualTo(StandardExpressionCount() + 2));
                Assert.That(FindExpression(result.Expressions, "SourceOnly"), Is.Null);
                Assert.That(sourceVrm.Expression.CustomClips, Has.Member(sourceOnly));

                foreach (ExpressionPreset preset in Enum.GetValues(typeof(ExpressionPreset)))
                {
                    if (preset == ExpressionPreset.custom) continue;
                    VRM10Expression standard = FindExpression(result.Expressions, preset.ToString());
                    Assert.That(standard, Is.Not.Null, preset.ToString());
                    Assert.That(GetExpressionClip(result.Vrm, preset), Is.SameAs(standard), preset.ToString());
                }

                VRM10Expression happy = FindExpression(result.Expressions, "happy");
                Assert.That(happy, Is.Not.Null);
                Assert.That(happy.MorphTargetBindings, Has.Length.EqualTo(1));
                Assert.That(happy.MorphTargetBindings[0].RelativePath, Is.EqualTo(string.Empty));
                Assert.That(happy.MorphTargetBindings[0].Index, Is.EqualTo(mesh.GetBlendShapeIndex("VRM_happy")));
                Assert.That(happy.MorphTargetBindings[0].Weight, Is.EqualTo(1f));

                VRM10Expression missing = FindExpression(result.Expressions, "sad");
                Assert.That(missing, Is.Not.Null);
                Assert.That(missing.MorphTargetBindings, Is.Empty);

                VRM10Expression custom = FindExpression(result.Expressions, "Custom");
                Assert.That(custom, Is.Not.Null);
                Assert.That(custom.MorphTargetBindings, Has.Length.EqualTo(1));
                Assert.That(custom.MorphTargetBindings[0].Index, Is.EqualTo(mesh.GetBlendShapeIndex("VRM_Custom")));
                Assert.That(custom.MorphTargetBindings[0].Weight, Is.EqualTo(1f));
                Assert.That(result.Vrm.Expression.CustomClips, Has.Member(custom));

                VRM10Expression another = FindExpression(result.Expressions, "Another");
                Assert.That(another, Is.Not.Null);
                Assert.That(another.MorphTargetBindings, Has.Length.EqualTo(1));
                Assert.That(another.MorphTargetBindings[0].Index, Is.EqualTo(mesh.GetBlendShapeIndex("VRM_Another")));
                Assert.That(another.MorphTargetBindings[0].Weight, Is.EqualTo(1f));
                Assert.That(result.Vrm.Expression.CustomClips, Has.Member(another));

                VRM10Object createdVrm = result.Vrm;
                result.Dispose();
                result = null;
                Assert.That(createdVrm == null, Is.True);
                Assert.That(happy == null, Is.True);
            }
            finally
            {
                result?.Dispose();
                UnityEngine.Object.DestroyImmediate(sourceOnly);
                UnityEngine.Object.DestroyImmediate(sourceVrm);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_RejectsCandidateWithoutExactlyOneMergedRenderer()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            VRM10Object sourceVrm = null;
            VRM10Expression sourceOnly = null;
            try
            {
                candidate = new GameObject("Spec17_Candidate");
                avatar = CreateTestHumanoidAvatar(candidate);
                candidate.AddComponent<Animator>().avatar = avatar;
                figure = CreateFigureSource(out sourceVrm, out sourceOnly);

                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(VrmIntegrationService.TransportPhysics(request, out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateRendererAmbiguous"));
                Assert.That(candidate.GetComponent<Vrm10Instance>(), Is.Null);
                Assert.That(sourceVrm.Expression.CustomClips, Has.Member(sourceOnly));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceOnly);
                UnityEngine.Object.DestroyImmediate(sourceVrm);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_ReplacesClonedCandidateVrmInstanceBeforeCreatingArtifacts()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object sourceVrm = null;
            VRM10Expression sourceOnly = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                Vrm10Instance existing = candidate.AddComponent<Vrm10Instance>();
                figure = CreateFigureSource(out sourceVrm, out sourceOnly);

                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(VrmIntegrationService.TransportPhysics(request, out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Instance, Is.Not.SameAs(existing));
                Assert.That(candidate.GetComponentsInChildren<Vrm10Instance>(true), Has.Length.EqualTo(1));
                Assert.That(candidate.GetComponent<Vrm10Instance>(), Is.SameAs(result.Instance));
                Assert.That(sourceVrm.Expression.CustomClips, Has.Member(sourceOnly));
                result.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceOnly);
                UnityEngine.Object.DestroyImmediate(sourceVrm);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_RejectsMultipleOrMeshlessCandidateRenderer()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object sourceVrm = null;
            VRM10Expression sourceOnly = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                new GameObject("Spec17_SecondRenderer").AddComponent<SkinnedMeshRenderer>().transform.SetParent(candidate.transform);
                candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true)[1].sharedMesh = mesh;
                figure = CreateFigureSource(out sourceVrm, out sourceOnly);
                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());

                Assert.That(VrmIntegrationService.TransportPhysics(request, out VrmTransportPhysicsResult multipleResult, out StackMachineDiagnostic multipleDiagnostic), Is.False);
                Assert.That(multipleResult, Is.Null);
                Assert.That(multipleDiagnostic.domainCode, Is.EqualTo("VrmCandidateRendererAmbiguous"));

                UnityEngine.Object.DestroyImmediate(candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true)[1].gameObject);
                candidate.GetComponent<SkinnedMeshRenderer>().sharedMesh = null;
                Assert.That(VrmIntegrationService.TransportPhysics(request, out VrmTransportPhysicsResult meshlessResult, out StackMachineDiagnostic meshlessDiagnostic), Is.False);
                Assert.That(meshlessResult, Is.Null);
                Assert.That(meshlessDiagnostic.domainCode, Is.EqualTo("VrmCandidateRendererAmbiguous"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceOnly);
                UnityEngine.Object.DestroyImmediate(sourceVrm);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_BindsChildRendererWithCandidateRelativePath()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object sourceVrm = null;
            VRM10Expression sourceOnly = null;
            VrmTransportPhysicsResult result = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", new[] { "VRM_happy" }, out avatar, out mesh);
                UnityEngine.Object.DestroyImmediate(candidate.GetComponent<SkinnedMeshRenderer>());
                var child = new GameObject("MergedRenderer");
                child.transform.SetParent(candidate.transform);
                child.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                figure = CreateFigureSource(out sourceVrm, out sourceOnly);

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                VRM10Expression happy = FindExpression(result.Expressions, "happy");
                Assert.That(happy.MorphTargetBindings, Has.Length.EqualTo(1));
                Assert.That(happy.MorphTargetBindings[0].RelativePath, Is.EqualTo("MergedRenderer"));
            }
            finally
            {
                result?.Dispose();
                UnityEngine.Object.DestroyImmediate(sourceOnly);
                UnityEngine.Object.DestroyImmediate(sourceVrm);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_RejectsCandidateWhoseResolvedHumanoidMappingHasRequiredBoneErrors()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object sourceVrm = null;
            VRM10Expression sourceOnly = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                Transform hips = candidate.transform.Find("Hips");
                Assert.That(hips, Is.Not.Null);
                UnityEngine.Object.DestroyImmediate(hips.gameObject);
                figure = CreateFigureSource(out sourceVrm, out sourceOnly);

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateHumanoidInvalid"));
                Assert.That(candidate.GetComponent<Vrm10Instance>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceOnly);
                UnityEngine.Object.DestroyImmediate(sourceVrm);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void Initialize_RejectsCandidateWhenRootHumanoidCannotAssignAnimatorBones()
        {
            var candidate = new GameObject("Spec17_Candidate");
            var figure = new GameObject("Spec17_Figure");
            try
            {
                var request = new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>());
                Assert.That(TryInitialize(request, out Vrm10Instance instance, out VRM10Object vrm, out VRM10Expression[] expressions, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmCandidateHumanoidMapFailed"));
                Assert.That(instance, Is.Null);
                Assert.That(vrm, Is.Null);
                Assert.That(expressions, Is.Empty);
                Assert.That(candidate.GetComponent<UniHumanoid.Humanoid>(), Is.Not.Null);
                Assert.That(candidate.GetComponent<Vrm10Instance>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(figure);
            }
        }

        [Test]
        public void TransportPhysics_TransfersFigureThenAttachedOutfitPhysicsWithoutMutatingSources()
        {
            GameObject candidate = null;
            GameObject figure = null;
            GameObject outfit = null;
            GameObject nonVrmOutfit = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            VRM10Object outfitVrm = null;
            VRM10Expression outfitExpression = null;
            VrmTransportPhysicsResult result = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                AddChild(candidate, "OutfitBone");
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                outfit = CreateFigureSource(out outfitVrm, out outfitExpression);
                nonVrmOutfit = new GameObject("Spec17_NonVrmOutfit");
                Vrm10Instance figureInstance = figure.GetComponent<Vrm10Instance>();
                Vrm10Instance outfitInstance = outfit.GetComponent<Vrm10Instance>();
                AddPhysicsSpring(figureInstance, "Hips", "FigureSpring");
                AddPhysicsSpring(outfitInstance, "OutfitJunkBone", "OutfitJunkSpring");
                ShapeSyncOutfitSpringBoneData outfitData = AddOutfitDataSpring(outfit, "OutfitBone", "OutfitSpring");

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, new[] { outfit, nonVrmOutfit }), out result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Instance.SpringBone.Springs, Has.Count.EqualTo(2));
                Assert.That(result.Instance.SpringBone.Springs[0].Name, Is.EqualTo("FigureSpring"));
                Assert.That(result.Instance.SpringBone.Springs[1].Name, Is.EqualTo("OutfitSpring"));
                Assert.That(result.Instance.SpringBone.Springs[0].Joints[0].transform, Is.SameAs(candidate.transform.Find("Hips")));
                Assert.That(result.Instance.SpringBone.Springs[1].Joints[0].transform, Is.SameAs(candidate.transform.Find("OutfitBone")));
                Assert.That(figureInstance.SpringBone.Springs, Has.Count.EqualTo(1));
                Assert.That(outfitInstance.SpringBone.Springs, Has.Count.EqualTo(1));
                Assert.That(outfitInstance.SpringBone.Springs[0].Name, Is.EqualTo("OutfitJunkSpring"));
                Assert.That(outfitData.Springs, Has.Count.EqualTo(1));
                Assert.That(figureInstance.SpringBone.Springs[0].Joints[0].transform, Is.SameAs(figure.transform.Find("Hips")));
                Assert.That(outfitInstance.SpringBone.Springs[0].Joints[0].transform, Is.SameAs(outfit.transform.Find("OutfitJunkBone")));
            }
            finally
            {
                result?.Dispose();
                DestroyPhysicsSource(outfitExpression, outfitVrm, outfit);
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(nonVrmOutfit);
            }
        }

        [Test]
        public void TransportPhysics_RejectsMissingOrAmbiguousFigureVrmSourceAndDisposesNewArtifacts()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object firstVrm = null;
            VRM10Expression firstExpression = null;
            VRM10Object secondVrm = null;
            VRM10Expression secondExpression = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = new GameObject("Spec17_FigureWithoutVrm");
                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult missingResult, out StackMachineDiagnostic missingDiagnostic), Is.False);
                Assert.That(missingResult, Is.Null);
                Assert.That(missingDiagnostic.domainCode, Is.EqualTo("VrmFigureSourceInstanceRequired"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);

                UnityEngine.Object.DestroyImmediate(candidate);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(mesh);
                candidate = CreateCandidate("Spec17_CandidateAmbiguous", Array.Empty<string>(), out avatar, out mesh);
                UnityEngine.Object.DestroyImmediate(figure);
                figure = CreateFigureSource(out firstVrm, out firstExpression);
                var secondRoot = new GameObject("Spec17_SecondVrm");
                secondRoot.transform.SetParent(figure.transform);
                var secondInstance = secondRoot.AddComponent<Vrm10Instance>();
                secondVrm = ScriptableObject.CreateInstance<VRM10Object>();
                secondExpression = ScriptableObject.CreateInstance<VRM10Expression>();
                secondVrm.Expression.AddClip(ExpressionPreset.custom, secondExpression);
                secondInstance.Vrm = secondVrm;

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult ambiguousResult, out StackMachineDiagnostic ambiguousDiagnostic), Is.False);
                Assert.That(ambiguousResult, Is.Null);
                Assert.That(ambiguousDiagnostic.domainCode, Is.EqualTo("VrmSourceInstanceAmbiguous"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(secondExpression);
                UnityEngine.Object.DestroyImmediate(secondVrm);
                DestroyPhysicsSource(firstExpression, firstVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_RejectsUnmappablePhysicsSourceAndDisposesNewArtifacts()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                AddPhysicsSpring(figure.GetComponent<Vrm10Instance>(), "MissingCandidateBone", "InvalidSpring");

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPhysicsTransportFailed"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone.Springs, Has.Count.EqualTo(1));
            }
            finally
            {
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_RejectsUnmappablePhysicsCenterAndDisposesNewArtifacts()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                Vrm10InstanceSpringBone.Spring spring = AddPhysicsSpring(figure.GetComponent<Vrm10Instance>(), "Hips", "InvalidCenter");
                spring.Center = AddChild(figure, "MissingCandidateCenter");

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPhysicsTransportFailed"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone.Springs, Has.Count.EqualTo(1));
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone.Springs[0].Center, Is.SameAs(figure.transform.Find("MissingCandidateCenter")));
            }
            finally
            {
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_AllowsUnavailableSpringBoneSourcesAsNoOp()
        {
            GameObject candidate = null;
            GameObject figure = null;
            GameObject outfit = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            VRM10Object outfitVrm = null;
            VRM10Expression outfitExpression = null;
            VrmTransportPhysicsResult result = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                outfit = CreateFigureSource(out outfitVrm, out outfitExpression);
                figure.GetComponent<Vrm10Instance>().SpringBone = null;
                outfit.GetComponent<Vrm10Instance>().SpringBone = null;

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, new[] { outfit }), out result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Instance.SpringBone.Springs, Is.Empty);
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone, Is.Null);
                Assert.That(outfit.GetComponent<Vrm10Instance>().SpringBone, Is.Null);
            }
            finally
            {
                result?.Dispose();
                DestroyPhysicsSource(outfitExpression, outfitVrm, outfit);
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_AllowsEmptyPhysicsListsAsNoOp()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            VrmTransportPhysicsResult result = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                Vrm10InstanceSpringBone physics = figure.GetComponent<Vrm10Instance>().SpringBone;
                physics.Springs = new List<Vrm10InstanceSpringBone.Spring>();
                physics.ColliderGroups = new List<VRM10SpringBoneColliderGroup>();

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out result, out StackMachineDiagnostic diagnostic), Is.True, diagnostic?.message);
                Assert.That(result.Instance.SpringBone.Springs, Is.Empty);
                Assert.That(result.Instance.SpringBone.ColliderGroups, Is.Empty);
                Assert.That(physics.Springs, Is.Empty);
                Assert.That(physics.ColliderGroups, Is.Empty);
            }
            finally
            {
                result?.Dispose();
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TransportPhysics_RejectsNullPhysicsLists(bool nullSprings)
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                Vrm10InstanceSpringBone physics = figure.GetComponent<Vrm10Instance>().SpringBone;
                if (nullSprings) physics.Springs = null;
                else physics.ColliderGroups = null;

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPhysicsTransportFailed"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);
            }
            finally
            {
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_TranslatesAttachmentMalformedSpringFailure()
        {
            GameObject candidate = null;
            GameObject figure = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                figure.GetComponent<Vrm10Instance>().SpringBone.Springs.Add(null);

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, Array.Empty<GameObject>()), out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPhysicsTransportFailed"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone.Springs, Has.Count.EqualTo(1));
            }
            finally
            {
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        [Test]
        public void TransportPhysics_PreservesSourcesWhenOutfitFailureFollowsFigureTransport()
        {
            GameObject candidate = null;
            GameObject figure = null;
            GameObject outfit = null;
            Avatar avatar = null;
            Mesh mesh = null;
            VRM10Object figureVrm = null;
            VRM10Expression figureExpression = null;
            VRM10Object outfitVrm = null;
            VRM10Expression outfitExpression = null;
            try
            {
                candidate = CreateCandidate("Spec17_Candidate", Array.Empty<string>(), out avatar, out mesh);
                figure = CreateFigureSource(out figureVrm, out figureExpression);
                outfit = CreateFigureSource(out outfitVrm, out outfitExpression);
                AddPhysicsSpring(figure.GetComponent<Vrm10Instance>(), "Hips", "FigureSpring");
                ShapeSyncOutfitSpringBoneData outfitData = AddOutfitDataSpring(outfit, "MissingCandidateOutfitBone", "OutfitFailure");

                Assert.That(VrmIntegrationService.TransportPhysics(new VrmTransportPhysicsRequest(candidate, figure, new[] { outfit }), out VrmTransportPhysicsResult result, out StackMachineDiagnostic diagnostic), Is.False);
                Assert.That(result, Is.Null);
                Assert.That(diagnostic.domainCode, Is.EqualTo("VrmPhysicsTransportFailed"));
                Assert.That(candidate.GetComponent<Vrm10Instance>().Vrm == null, Is.True);
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone.Springs, Has.Count.EqualTo(1));
                Assert.That(figure.GetComponent<Vrm10Instance>().SpringBone.Springs[0].Joints[0].transform, Is.SameAs(figure.transform.Find("Hips")));
                Assert.That(outfitData.Springs, Has.Count.EqualTo(1));
                Assert.That(outfitData.Springs[0].Joints[0].transform, Is.SameAs(outfit.transform.Find("MissingCandidateOutfitBone")));
            }
            finally
            {
                DestroyPhysicsSource(outfitExpression, outfitVrm, outfit);
                DestroyPhysicsSource(figureExpression, figureVrm, figure);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(avatar);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        private static bool TryValidate(VrmTransportPhysicsRequest request, out StackMachineDiagnostic diagnostic)
        {
            Type validator = typeof(VrmIntegrationService).Assembly.GetType("zgock.ShapeSync.VrmIntegration.VrmTransportPhysicsRequestValidator", true);
            MethodInfo method = validator.GetMethod("TryValidate", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { request, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[2];
            return result;
        }

        private static bool TryInitialize(
            VrmTransportPhysicsRequest request,
            out Vrm10Instance instance,
            out VRM10Object vrm,
            out VRM10Expression[] expressions,
            out StackMachineDiagnostic diagnostic)
        {
            Assembly assembly = typeof(VrmIntegrationService).Assembly;
            Type contextType = assembly.GetType("zgock.ShapeSync.VrmIntegration.VrmTransportPhysicsContext", true);
            ConstructorInfo constructor = contextType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(VrmTransportPhysicsRequest), typeof(Animator) },
                null);
            Assert.That(constructor, Is.Not.Null);
            object context = constructor.Invoke(new object[] { request, null });

            MethodInfo method = typeof(VrmIntegrationService).GetMethod("TryInitialize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { context, null, null, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            instance = (Vrm10Instance)arguments[1];
            vrm = (VRM10Object)arguments[2];
            expressions = (VRM10Expression[])arguments[3];
            diagnostic = (StackMachineDiagnostic)arguments[4];
            return result;
        }

        private static Action GetExecutorSaveAssets()
        {
            return (Action)typeof(HumanoidVrmTransportExecutor).GetField("SaveAllAssets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        }

 #if SHAPESYNC_RICH_TEST
        [UnityTest]
        public IEnumerator ActualSpec17DocumentB_TransportPhysicsDoesNotMutateCompilerCandidateGeometryOrPose()
        {
            const string figurePath = "Assets/zgock/ShapeSync/PlayTest/Common/Figure.prefab";
            const string documentPath = "Assets/zgock/ShapeSync/PlayTest/Common/ShapeDocument_B.asset";
            GameObject figurePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(figurePath);
            ShapeDocument document = AssetDatabase.LoadAssetAtPath<ShapeDocument>(documentPath);
            Assert.That(figurePrefab, Is.Not.Null, figurePath);
            Assert.That(document, Is.Not.Null, documentPath);
            Assert.That(document.TryGetSnapshot(out ShapeSyncDocument payload, out StackMachineDiagnostic snapshotDiagnostic), Is.True, snapshotDiagnostic?.message);

            GameObject figure = null;
            object controller = null;
            GameObject publishedContents = null;
            Mesh bakedBefore = null;
            Mesh bakedAfter = null;
            string stageFolder = "Assets/zgock/ShapeSync/Tests/EditMode/VrmIntegration/__Spec17_DocumentBVrmTransport_" + Guid.NewGuid().ToString("N");
            try
            {
                Assert.That(AssetDatabase.CreateFolder("Assets/zgock/ShapeSync/Tests/EditMode/VrmIntegration", stageFolder.Substring(stageFolder.LastIndexOf('/') + 1)), Is.Not.Empty);
                figure = UnityEngine.Object.Instantiate(figurePrefab);
                controller = CreateEditorBuildController();
                Assert.That(InvokeControllerStart(controller, figure, document, out StackMachineDiagnostic startDiagnostic), Is.True, startDiagnostic?.message);
                for (int i = 0; i < 240; i++)
                {
                    HumanoidBuildOperationStatus status = InvokeControllerPump(controller, out StackMachineDiagnostic pumpDiagnostic);
                    Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Failed), pumpDiagnostic?.message);
                    Assert.That(status, Is.Not.EqualTo(HumanoidBuildOperationStatus.Cancelled), pumpDiagnostic?.message);
                    if (status == HumanoidBuildOperationStatus.Succeeded) break;
                    EditorApplication.QueuePlayerLoopUpdate();
                    yield return null;
                }

                Assert.That(GetControllerStatus(controller), Is.EqualTo(HumanoidBuildOperationStatus.Succeeded), "Document B compiler build must complete before VRM transport.");
                Assert.That(InvokeControllerStage(controller, stageFolder, "DocumentB", out StackMachineDiagnostic stageDiagnostic), Is.True, stageDiagnostic?.message);
                Assert.That(InvokeControllerApplyStage(controller, out StackMachineDiagnostic applyDiagnostic), Is.True, applyDiagnostic?.message);
                GameObject candidate = GetControllerCandidate(controller);
                Assert.That(candidate, Is.Not.Null);
                SkinnedMeshRenderer[] renderers = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(renderers, Has.Length.EqualTo(1));
                Mesh meshBefore = renderers[0].sharedMesh;
                TransformPoseSnapshot[] poseBefore = CaptureLocalPoses(candidate);
                RelativeTransformPoseSnapshot[] persistentPoseBefore = CaptureRelativeLocalPoses(candidate);
                bakedBefore = new Mesh();
                renderers[0].BakeMesh(bakedBefore);

                var executor = new HumanoidVrmTransportExecutor();
                Assert.That(InvokeControllerTransport(controller, executor, out StackMachineDiagnostic transportDiagnostic), Is.True, transportDiagnostic?.message);
                Vrm10Instance candidateVrm = candidate.GetComponent<Vrm10Instance>();
                Assert.That(candidateVrm, Is.Not.Null, "VRM transport must initialize one candidate Vrm10Instance.");
                Assert.That(candidateVrm.SpringBone, Is.Not.Null, "VRM transport must initialize candidate SpringBone data.");
                Assert.That(candidateVrm.SpringBone.Springs, Is.Not.Empty, "Document B transport must retain Figure/Outfit Spring records before publish.");
                Assert.That(candidateVrm.SpringBone.ColliderGroups, Is.Not.Empty, "Document B transport must retain Figure/Outfit ColliderGroups before publish.");
                Assert.That(renderers[0].sharedMesh, Is.SameAs(meshBefore));
                AssertLocalPosesEqual(poseBefore, candidate, "VRM transport setup must not alter the compiler candidate pose.");
                Assert.That(InvokeControllerStageVrmAssets(controller, executor, stageFolder, "Vrm", "DocumentB", out StackMachineDiagnostic vrmStageDiagnostic), Is.True, vrmStageDiagnostic?.message);
                Assert.That(InvokeControllerCommit(controller, stageFolder, "DocumentB", executor, out StackMachineDiagnostic commitDiagnostic), Is.True, commitDiagnostic?.message);

                string prefabPath = GetControllerPublishedPrefabPath(controller);
                Assert.That(prefabPath, Is.Not.Empty);
                publishedContents = PrefabUtility.LoadPrefabContents(prefabPath);
                Assert.That(publishedContents, Is.Not.Null);
                SkinnedMeshRenderer[] persistedRenderers = publishedContents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(persistedRenderers, Has.Length.EqualTo(1));
                Assert.That(persistedRenderers[0].sharedMesh, Is.SameAs(meshBefore));
                Vrm10Instance persistedVrm = publishedContents.GetComponent<Vrm10Instance>();
                Assert.That(persistedVrm, Is.Not.Null, "Published Prefab must retain Vrm10Instance.");
                Assert.That(persistedVrm.SpringBone, Is.Not.Null, "Published Prefab must retain SpringBone data.");
                Assert.That(persistedVrm.SpringBone.Springs, Is.Not.Empty, "Published Prefab must retain transported Spring records.");
                Assert.That(persistedVrm.SpringBone.ColliderGroups, Is.Not.Empty, "Published Prefab must retain transported ColliderGroups.");
                AssertRelativeLocalPosesEqual(persistentPoseBefore, publishedContents, "VRM publish must preserve every pre-existing candidate Transform pose.");
                bakedAfter = new Mesh();
                persistedRenderers[0].BakeMesh(bakedAfter);
                AssertBakedVerticesEqual(bakedBefore.vertices, bakedAfter.vertices, "VRM publish must preserve the final skinned Mesh geometry.");
            }
            finally
            {
                if (bakedAfter != null) UnityEngine.Object.DestroyImmediate(bakedAfter);
                if (bakedBefore != null) UnityEngine.Object.DestroyImmediate(bakedBefore);
                if (publishedContents != null) PrefabUtility.UnloadPrefabContents(publishedContents);
                (controller as IDisposable)?.Dispose();
                if (figure != null) UnityEngine.Object.DestroyImmediate(figure);
                AssetDatabase.DeleteAsset(stageFolder);
            }
        }

#endif
        private static void SetExecutorSaveAssets(Action value)
        {
            typeof(HumanoidVrmTransportExecutor).GetField("SaveAllAssets", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
        }

        private static void AssertPrefabAssetIdentity(UnityEngine.Object reference, UnityEngine.Object expected, string label)
        {
            Assert.That(reference, Is.Not.Null, $"{label} must not be null.");
            Assert.That(expected, Is.Not.Null, $"{label} expected Prefab must not be null.");
            string expectedPath = AssetDatabase.GetAssetPath(expected);
            Assert.That(AssetDatabase.GetAssetPath(reference), Is.EqualTo(expectedPath), $"{label} must resolve to the published Prefab path.");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(expected, out string expectedGuid, out long expectedLocalFileId), Is.True, $"{label} expected Prefab identity must be readable.");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string referenceGuid, out long referenceLocalFileId), Is.True, $"{label} reference identity must be readable.");
            Assert.That(referenceGuid, Is.EqualTo(expectedGuid), $"{label} GUID must match the published Prefab.");
            Assert.That(referenceLocalFileId, Is.EqualTo(expectedLocalFileId), $"{label} local file ID must match the published Prefab.");
        }

        private static VrmTransportPhysicsResult CreateResult(UniVRM10.Vrm10Instance instance, UniVRM10.VRM10Object vrm, UniVRM10.VRM10Expression[] expressions)
        {
            ConstructorInfo constructor = typeof(VrmTransportPhysicsResult).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(UniVRM10.Vrm10Instance), typeof(UniVRM10.VRM10Object), typeof(UniVRM10.VRM10Expression[]) },
                null);
            Assert.That(constructor, Is.Not.Null);
            return (VrmTransportPhysicsResult)constructor.Invoke(new object[] { instance, vrm, expressions });
        }

        private static object CreateEditorBuildController()
        {
            Type type = typeof(HumanoidCompilerWindow).Assembly.GetType("zgock.ShapeSync.Editor.HumanoidEditorBuildController", true);
            return Activator.CreateInstance(type, true);
        }

        private static bool InvokeControllerStart(object controller, GameObject figure, ShapeDocument document, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { figure, document, null };
            bool result = (bool)controller.GetType().GetMethod("TryStart", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[2];
            return result;
        }

        private static HumanoidBuildOperationStatus InvokeControllerPump(object controller, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { null };
            HumanoidBuildOperationStatus result = (HumanoidBuildOperationStatus)controller.GetType().GetMethod("Pump", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[0];
            return result;
        }

        private static bool InvokeControllerStage(object controller, string outputFolder, string documentName, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { outputFolder, documentName, null };
            bool result = (bool)controller.GetType().GetMethod("TryStageIndividualAssets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[2];
            return result;
        }

        private static bool InvokeControllerApplyStage(object controller, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { null };
            bool result = (bool)controller.GetType().GetMethod("TryApplyStagedAssetsToCandidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[0];
            return result;
        }

        private static bool InvokeControllerStageVrmAssets(object controller, IHumanoidVrmTransportExecutor executor, string outputFolder, string relativeFolder, string documentName, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { executor, outputFolder, relativeFolder, documentName, null };
            bool result = (bool)controller.GetType().GetMethod("TryStageVrmAssets", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[4];
            return result;
        }

        private static bool InvokeControllerCommit(object controller, string outputFolder, string documentName, IHumanoidVrmTransportExecutor executor, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { outputFolder, documentName, executor, null };
            bool result = (bool)controller.GetType().GetMethod("TryCommitPrefab", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[3];
            return result;
        }

        private static bool InvokeControllerTransport(object controller, IHumanoidVrmTransportExecutor executor, out StackMachineDiagnostic diagnostic)
        {
            object[] arguments = { executor, null };
            bool result = (bool)controller.GetType().GetMethod("TryTransportVrmPhysics", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, arguments);
            diagnostic = (StackMachineDiagnostic)arguments[1];
            return result;
        }

        private static GameObject GetControllerCandidate(object controller)
        {
            return (GameObject)controller.GetType().GetProperty("Candidate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        }

        private static HumanoidBuildOperationStatus GetControllerStatus(object controller)
        {
            return (HumanoidBuildOperationStatus)controller.GetType().GetProperty("Status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        }

        private static string GetControllerPublishedPrefabPath(object controller)
        {
            return (string)controller.GetType().GetProperty("PublishedPrefabAssetPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        }

        private static TransformPoseSnapshot[] CaptureLocalPoses(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var snapshots = new TransformPoseSnapshot[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) snapshots[i] = new TransformPoseSnapshot(transforms[i]);
            return snapshots;
        }

        private static void AssertLocalPosesEqual(IReadOnlyList<TransformPoseSnapshot> expected, GameObject root, string message)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Assert.That(transforms, Has.Length.EqualTo(expected.Count), message);
            for (int i = 0; i < expected.Count; i++) expected[i].AssertMatches(transforms[i], message);
        }

        private static RelativeTransformPoseSnapshot[] CaptureRelativeLocalPoses(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var snapshots = new RelativeTransformPoseSnapshot[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) snapshots[i] = new RelativeTransformPoseSnapshot(root.transform, transforms[i]);
            return snapshots;
        }

        private static void AssertRelativeLocalPosesEqual(IReadOnlyList<RelativeTransformPoseSnapshot> expected, GameObject root, string message)
        {
            for (int i = 0; i < expected.Count; i++) expected[i].AssertMatches(root.transform, message);
        }

        private static void AssertBakedVerticesEqual(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual, string message)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), message);
            const float tolerance = 0.00001f;
            for (int i = 0; i < expected.Count; i++)
                Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThanOrEqualTo(tolerance), message + " vertex=" + i);
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var names = new Stack<string>();
            Transform current = target;
            while (current != null && current != root) { names.Push(current.name); current = current.parent; }
            Assert.That(current, Is.SameAs(root));
            return string.Join("/", names);
        }

        private readonly struct TransformPoseSnapshot
        {
            private readonly Transform transform;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;

            public TransformPoseSnapshot(Transform transform)
            {
                this.transform = transform;
                position = transform.localPosition;
                rotation = transform.localRotation;
                scale = transform.localScale;
            }

            public void AssertMatches(Transform actual, string message)
            {
                Assert.That(actual, Is.SameAs(transform), message);
                Assert.That(actual.localPosition, Is.EqualTo(position), message + " localPosition");
                Assert.That(actual.localRotation, Is.EqualTo(rotation), message + " localRotation");
                Assert.That(actual.localScale, Is.EqualTo(scale), message + " localScale");
            }
        }

        private readonly struct RelativeTransformPoseSnapshot
        {
            private readonly string path;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;

            public RelativeTransformPoseSnapshot(Transform root, Transform transform)
            {
                path = GetRelativePath(root, transform);
                position = transform.localPosition;
                rotation = transform.localRotation;
                scale = transform.localScale;
            }

            public void AssertMatches(Transform root, string message)
            {
                Transform actual = string.IsNullOrEmpty(path) ? root : root.Find(path);
                Assert.That(actual, Is.Not.Null, message + " missing path=" + path);
                Assert.That(actual.localPosition, Is.EqualTo(position), message + " localPosition path=" + path);
                Assert.That(actual.localRotation, Is.EqualTo(rotation), message + " localRotation path=" + path);
                Assert.That(actual.localScale, Is.EqualTo(scale), message + " localScale path=" + path);
            }
        }

        private static GameObject CreateCandidate(string name, string[] blendShapes, out Avatar avatar, out Mesh mesh)
        {
            var candidate = new GameObject(name);
            avatar = CreateTestHumanoidAvatar(candidate);
            candidate.AddComponent<Animator>().avatar = avatar;
            mesh = CreateExpressionMesh(blendShapes);
            candidate.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            return candidate;
        }

        private static GameObject CreateFigureSource(out VRM10Object sourceVrm, out VRM10Expression sourceOnly)
        {
            var figure = new GameObject("Spec17_Figure");
            var instance = figure.AddComponent<Vrm10Instance>();
            sourceVrm = ScriptableObject.CreateInstance<VRM10Object>();
            sourceOnly = ScriptableObject.CreateInstance<VRM10Expression>();
            sourceOnly.name = "SourceOnly";
            sourceVrm.Expression.AddClip(ExpressionPreset.custom, sourceOnly);
            instance.Vrm = sourceVrm;
            return figure;
        }

        private static Mesh CreateExpressionMesh(string[] blendShapes)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward }
            };
            for (int i = 0; i < blendShapes.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapes[i], 100f, new[] { Vector3.right * .01f, Vector3.zero, Vector3.zero }, null, null);
            }
            return mesh;
        }

        private static int StandardExpressionCount()
        {
            int count = 0;
            foreach (ExpressionPreset preset in Enum.GetValues(typeof(ExpressionPreset))) if (preset != ExpressionPreset.custom) count++;
            return count;
        }

        private static VRM10Expression FindExpression(IReadOnlyList<VRM10Expression> expressions, string name)
        {
            for (int i = 0; i < expressions.Count; i++) if (expressions[i] != null && expressions[i].name == name) return expressions[i];
            return null;
        }

        private static VRM10Expression GetExpressionClip(VRM10Object vrm, ExpressionPreset preset)
        {
            foreach (var pair in vrm.Expression.Clips) if (pair.Preset == preset) return pair.Clip;
            return null;
        }

        private static Transform AddChild(GameObject root, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(root.transform, false);
            return child;
        }

        private static Vrm10InstanceSpringBone.Spring AddPhysicsSpring(Vrm10Instance instance, string jointName, string springName)
        {
            Transform jointRoot = AddChild(instance.gameObject, jointName);
            var joint = jointRoot.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var spring = new Vrm10InstanceSpringBone.Spring(springName);
            spring.Joints.Add(joint);
            instance.SpringBone.Springs.Add(spring);
            return spring;
        }

        private static ShapeSyncOutfitSpringBoneData AddOutfitDataSpring(GameObject outfit, string jointName, string springName)
        {
            ShapeSyncOutfitSpringBoneData data = outfit.GetComponent<ShapeSyncOutfitSpringBoneData>() ?? outfit.AddComponent<ShapeSyncOutfitSpringBoneData>();
            Transform jointRoot = AddChild(outfit, jointName);
            var joint = jointRoot.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var spring = new Vrm10InstanceSpringBone.Spring(springName);
            spring.Joints.Add(joint);
            data.Springs.Add(spring);
            data.SpringColliderGroupNames.Add(new List<string>());
            return data;
        }

        private static void DestroyPhysicsSource(VRM10Expression expression, VRM10Object vrm, GameObject root)
        {
            UnityEngine.Object.DestroyImmediate(expression);
            UnityEngine.Object.DestroyImmediate(vrm);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Avatar CreateTestHumanoidAvatar(GameObject root)
        {
            var bones = new List<Transform>();
            Transform hips = AddBone(root.transform, "Hips", new Vector3(0f, 1f, 0f), bones);
            Transform spine = AddBone(hips, "Spine", Vector3.up * .15f, bones);
            Transform chest = AddBone(spine, "Chest", Vector3.up * .15f, bones);
            Transform neck = AddBone(chest, "Neck", Vector3.up * .15f, bones);
            AddBone(neck, "Head", Vector3.up * .12f, bones);
            Transform leftUpperArm = AddBone(chest, "LeftUpperArm", new Vector3(-.15f, .1f, 0f), bones);
            Transform leftLowerArm = AddBone(leftUpperArm, "LeftLowerArm", Vector3.left * .2f, bones);
            AddBone(leftLowerArm, "LeftHand", Vector3.left * .18f, bones);
            Transform rightUpperArm = AddBone(chest, "RightUpperArm", new Vector3(.15f, .1f, 0f), bones);
            Transform rightLowerArm = AddBone(rightUpperArm, "RightLowerArm", Vector3.right * .2f, bones);
            AddBone(rightLowerArm, "RightHand", Vector3.right * .18f, bones);
            Transform leftUpperLeg = AddBone(hips, "LeftUpperLeg", new Vector3(-.08f, -.35f, 0f), bones);
            Transform leftLowerLeg = AddBone(leftUpperLeg, "LeftLowerLeg", Vector3.down * .35f, bones);
            AddBone(leftLowerLeg, "LeftFoot", new Vector3(0f, -.1f, .1f), bones);
            Transform rightUpperLeg = AddBone(hips, "RightUpperLeg", new Vector3(.08f, -.35f, 0f), bones);
            Transform rightLowerLeg = AddBone(rightUpperLeg, "RightLowerLeg", Vector3.down * .35f, bones);
            AddBone(rightLowerLeg, "RightFoot", new Vector3(0f, -.1f, .1f), bones);
            string[] names = { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot" };
            var human = new HumanBone[names.Length];
            for (int i = 0; i < names.Length; i++) human[i] = new HumanBone { boneName = names[i], humanName = names[i], limit = new HumanLimit { useDefaultValues = true } };
            var skeleton = new List<SkeletonBone> { ToSkeletonBone(root.transform) };
            for (int i = 0; i < bones.Count; i++) skeleton.Add(ToSkeletonBone(bones[i]));
            return AvatarBuilder.BuildHumanAvatar(root, new HumanDescription { human = human, skeleton = skeleton.ToArray() });
        }

        private static Transform AddBone(Transform parent, string name, Vector3 position, List<Transform> bones)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = position;
            bones.Add(bone);
            return bone;
        }

        private static SkeletonBone ToSkeletonBone(Transform transform) => new SkeletonBone { name = transform.name, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale };
    }
}
#endif
