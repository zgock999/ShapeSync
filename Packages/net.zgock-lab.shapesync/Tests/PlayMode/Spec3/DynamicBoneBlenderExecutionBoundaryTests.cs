// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace zgock.ShapeSync.Tests.PlayMode
{

    [DefaultExecutionOrder(10050)]
    public sealed class DynamicBoneBlenderPostApplyProbe : MonoBehaviour
    {
        public DynamicBoneBlenderExecutionSnapshot StartSnapshot;
        public DynamicBoneBlenderExecutionSnapshot Snapshot;

        private void Start()
        {
            StartSnapshot = DynamicBoneBlenderExecutionSnapshot.Capture(GetComponent(DynamicBoneBlenderExecutionBoundaryTests.RuntimeType("DynamicBoneBlender")));
        }

        private void LateUpdate()
        {
            Snapshot = DynamicBoneBlenderExecutionSnapshot.Capture(GetComponent(DynamicBoneBlenderExecutionBoundaryTests.RuntimeType("DynamicBoneBlender")));
        }
    }

    }

    [DefaultExecutionOrder(9999)]
    public sealed class DynamicBoneBlenderPreApplyProbe : MonoBehaviour
    {
        public DynamicBoneBlenderExecutionSnapshot Snapshot;

        private void LateUpdate()
        {
            Snapshot = DynamicBoneBlenderExecutionSnapshot.Capture(GetComponent(DynamicBoneBlenderExecutionBoundaryTests.RuntimeType("DynamicBoneBlender")));
        }
    }

    [DefaultExecutionOrder(12000)]
    public sealed class DynamicBoneBlenderPostVrmProbe : MonoBehaviour
    {
        public DynamicBoneBlenderExecutionSnapshot Snapshot;

        private void LateUpdate()
        {
            Snapshot = DynamicBoneBlenderExecutionSnapshot.Capture(GetComponent(DynamicBoneBlenderExecutionBoundaryTests.RuntimeType("DynamicBoneBlender")));
        }
    }

    public sealed class DynamicBoneBlenderExecutionSnapshot
    {
        public Matrix4x4[] skinMatrices;
        public Matrix4x4[] bindposes;
        public Matrix4x4[] boneToRendererMatrices;
        public string[] boneNames;
        public Vector3[] bakedVertices;
        public float[] blendShapeWeights;
        public string[] blendShapeNames;

        public static DynamicBoneBlenderExecutionSnapshot Capture(Component blender)
        {
            PropertyInfo property = blender != null ? blender.GetType().GetProperty("TargetSkinnedMeshRenderer") : null;
            SkinnedMeshRenderer renderer = property != null ? property.GetValue(blender, null) as SkinnedMeshRenderer : null;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            if (renderer == null || mesh == null || renderer.bones == null)
            {
                return null;
            }

            int count = Mathf.Min(renderer.bones.Length, mesh.bindposes.Length);
            DynamicBoneBlenderExecutionSnapshot result = new DynamicBoneBlenderExecutionSnapshot
            {
                skinMatrices = new Matrix4x4[count],
                bindposes = new Matrix4x4[count],
                boneToRendererMatrices = new Matrix4x4[count],
                boneNames = new string[count]
            };

            Matrix4x4 rendererInverse = renderer.transform.worldToLocalMatrix;
            for (int i = 0; i < count; i++)
            {
                Transform bone = renderer.bones[i];
                Matrix4x4 bindpose = mesh.bindposes[i];
                result.boneNames[i] = bone != null ? bone.name : "<null>";
                result.bindposes[i] = bindpose;
                result.boneToRendererMatrices[i] = bone != null
                    ? rendererInverse * bone.localToWorldMatrix
                    : Matrix4x4.zero;
                result.skinMatrices[i] = bone != null
                    ? bone.localToWorldMatrix * bindpose * rendererInverse
                    : Matrix4x4.zero;
            }

            Mesh baked = new Mesh();
            renderer.BakeMesh(baked);
            result.bakedVertices = baked.vertices;
            Object.DestroyImmediate(baked);
            result.blendShapeWeights = new float[mesh.blendShapeCount];
            result.blendShapeNames = new string[mesh.blendShapeCount];
            for (int i = 0; i < result.blendShapeWeights.Length; i++)
            {
                result.blendShapeWeights[i] = renderer.GetBlendShapeWeight(i);
                result.blendShapeNames[i] = mesh.GetBlendShapeName(i);
            }

            return result;
        }
    }

    public sealed class DynamicBoneBlenderExecutionBoundaryTests
    {
        private const string TargetBlendName = "BasicGirl";
        private const float MatrixTolerance = 0.0001f;

        [UnityTest]
        public IEnumerator DynamicBoneBlender_ProducesEquivalentGeometryWhenRawVrmRuntimeIsDisabled()
        {
            GameObject healthy = CreateEquivalentFigure("Spec3_Healthy");
            GameObject repro = CreateEquivalentFigure("Spec3_Repro");
            DisableRawVrmRuntime(healthy);
            DisableRawVrmRuntime(repro);

            Component healthyBlender = healthy.GetComponent(RuntimeType("DynamicBoneBlender"));
            Component reproBlender = repro.GetComponent(RuntimeType("DynamicBoneBlender"));
            SetWeight(healthyBlender, TargetBlendName, 1f);
            SetWeight(reproBlender, TargetBlendName, 1f);
            DynamicBoneBlenderPostVrmProbe healthyProbe = healthy.AddComponent<DynamicBoneBlenderPostVrmProbe>();
            DynamicBoneBlenderPostVrmProbe reproProbe = repro.AddComponent<DynamicBoneBlenderPostVrmProbe>();
            yield return null;
            // WaitForEndOfFrame is not evoked by Unity's batchmode test runner. A
            // second frame still observes the probes after their LateUpdate while
            // keeping the geometry oracle unchanged in both interactive and batch runs.
            yield return null;

            string result = Compare("with raw Vrm10Instance disabled", healthyProbe.Snapshot, reproProbe.Snapshot);
            Assert.That(healthyProbe.Snapshot, Is.Not.Null);
            Assert.That(reproProbe.Snapshot, Is.Not.Null);
            Assert.That(healthyProbe.Snapshot.blendShapeWeights, Has.Length.EqualTo(1));
            Assert.That(reproProbe.Snapshot.blendShapeWeights, Has.Length.EqualTo(1));
            Assert.That(healthyProbe.Snapshot.blendShapeWeights[0], Is.EqualTo(100f).Within(MatrixTolerance));
            Assert.That(reproProbe.Snapshot.blendShapeWeights[0], Is.EqualTo(100f).Within(MatrixTolerance));
            Assert.That(healthyProbe.Snapshot.bakedVertices[0].x, Is.EqualTo(1f).Within(MatrixTolerance));
            Assert.That(reproProbe.Snapshot.bakedVertices[0].x, Is.EqualTo(1f).Within(MatrixTolerance));
            Object.Destroy(healthy);
            Object.Destroy(repro);
            Assert.That(result, Is.EqualTo(string.Empty), result);
        }

        [Test]
        public void DynamicBoneBlender_HealthyAndReproPrefabsRemainEquivalentAcrossApplyAllInternalStages()
        {
            GameObject healthy = CreateEquivalentFigure("Spec3_Healthy");
            GameObject repro = CreateEquivalentFigure("Spec3_Repro");
            Component healthyBlender = healthy.GetComponent(RuntimeType("DynamicBoneBlender"));
            Component reproBlender = repro.GetComponent(RuntimeType("DynamicBoneBlender"));
            Assert.That(healthyBlender, Is.Not.Null);
            Assert.That(reproBlender, Is.Not.Null);

            ((Behaviour)healthyBlender).enabled = false;
            ((Behaviour)reproBlender).enabled = false;
            SetWeight(healthyBlender, TargetBlendName, 1f);
            SetWeight(reproBlender, TargetBlendName, 1f);
            InvokePrivate(healthyBlender, "InitializeCache");
            InvokePrivate(reproBlender, "InitializeCache");
            InvokePrivate(healthyBlender, "ApplyBodyBlendShapes");
            InvokePrivate(reproBlender, "ApplyBodyBlendShapes");

            DynamicBoneBlenderExecutionSnapshot healthyInitialized = DynamicBoneBlenderExecutionSnapshot.Capture(healthyBlender);
            DynamicBoneBlenderExecutionSnapshot reproInitialized = DynamicBoneBlenderExecutionSnapshot.Capture(reproBlender);
            InvokePrivate(healthyBlender, "UpdateBindposes");
            InvokePrivate(reproBlender, "UpdateBindposes");
            DynamicBoneBlenderExecutionSnapshot healthyBindposes = DynamicBoneBlenderExecutionSnapshot.Capture(healthyBlender);
            DynamicBoneBlenderExecutionSnapshot reproBindposes = DynamicBoneBlenderExecutionSnapshot.Capture(reproBlender);
            InvokePrivate(healthyBlender, "UpdateHumanoidAvatar");
            InvokePrivate(reproBlender, "UpdateHumanoidAvatar");
            DynamicBoneBlenderExecutionSnapshot healthyAvatar = DynamicBoneBlenderExecutionSnapshot.Capture(healthyBlender);
            DynamicBoneBlenderExecutionSnapshot reproAvatar = DynamicBoneBlenderExecutionSnapshot.Capture(reproBlender);
            InvokePrivate(healthyBlender, "ApplyBoneTransforms");
            InvokePrivate(reproBlender, "ApplyBoneTransforms");
            DynamicBoneBlenderExecutionSnapshot healthyTransforms = DynamicBoneBlenderExecutionSnapshot.Capture(healthyBlender);
            DynamicBoneBlenderExecutionSnapshot reproTransforms = DynamicBoneBlenderExecutionSnapshot.Capture(reproBlender);

            string initializedResult = Compare("after InitializeCache", healthyInitialized, reproInitialized);
            string bindposeResult = Compare("after UpdateBindposes", healthyBindposes, reproBindposes);
            string avatarResult = Compare("after UpdateHumanoidAvatar", healthyAvatar, reproAvatar);
            string transformsResult = Compare("after ApplyBoneTransforms", healthyTransforms, reproTransforms);

            Object.DestroyImmediate(healthy);
            Object.DestroyImmediate(repro);

            string results = initializedResult + "\n" + bindposeResult + "\n" + avatarResult + "\n" + transformsResult;
            Debug.Log(results);
            Assert.That(results, Is.EqualTo("\n\n\n"), results);
        }

        private static void SetWeight(Component blender, string blendName, float weight)
        {
            PropertyInfo targetsProperty = blender.GetType().GetProperty("Targets");
            IList targets = targetsProperty != null ? targetsProperty.GetValue(blender, null) as IList : null;
            Assert.That(targets, Is.Not.Null);
            for (int i = 0; i < targets.Count; i++)
            {
                object target = targets[i];
                if (target == null)
                {
                    continue;
                }

                FieldInfo name = target.GetType().GetField("blendName");
                if (name != null && (string)name.GetValue(target) == blendName)
                {
                    target.GetType().GetField("enabled").SetValue(target, true);
                    target.GetType().GetField("weight").SetValue(target, weight);
                    return;
                }
            }

            Assert.Fail($"Target '{blendName}' was not found.");
        }

        private static GameObject CreateEquivalentFigure(string name)
        {
            GameObject root = new GameObject(name);
            GameObject boneObject = new GameObject("Root");
            boneObject.transform.SetParent(root.transform, false);

            Mesh mesh = new Mesh { name = name + "_Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { boneObject.transform.worldToLocalMatrix * root.transform.localToWorldMatrix };
            Vector3[] deltas = { Vector3.right, Vector3.zero, Vector3.zero };
            mesh.AddBlendShapeFrame(TargetBlendName, 100f, deltas, deltas, deltas);

            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { boneObject.transform };
            renderer.rootBone = boneObject.transform;

            Component blender = root.AddComponent(RuntimeType("DynamicBoneBlender"));
            object target = System.Activator.CreateInstance(RuntimeType("DynamicBoneBlendTarget"));
            target.GetType().GetField("blendName").SetValue(target, TargetBlendName);
            target.GetType().GetField("enabled").SetValue(target, true);
            target.GetType().GetField("weight").SetValue(target, 0f);
            System.Type listType = typeof(List<>).MakeGenericType(RuntimeType("DynamicBoneBlendTarget"));
            IList targets = (IList)System.Activator.CreateInstance(listType);
            targets.Add(target);
            blender.GetType().GetField("targetSkinnedMeshRenderer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(blender, renderer);
            blender.GetType().GetField("targets", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(blender, targets);
            return root;
        }

        private static void InvokePrivate(Component component, string methodName)
        {
            MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Could not find DynamicBoneBlender.{methodName}.");
            method.Invoke(component, null);
        }

        private static void NormalizeRootTransform(Transform transform)
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        private static void DisableRawVrmRuntime(GameObject root)
        {
            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is Behaviour behaviour && components[i].GetType().FullName == "UniVRM10.Vrm10Instance")
                {
                    behaviour.enabled = false;
                }
            }
        }

        private static string Compare(string phase, DynamicBoneBlenderExecutionSnapshot healthy, DynamicBoneBlenderExecutionSnapshot repro)
        {
            if (healthy == null || repro == null)
            {
                return phase + ": snapshot was not captured.";
            }

            if (healthy.bindposes.Length != repro.bindposes.Length || healthy.skinMatrices.Length != repro.skinMatrices.Length)
            {
                return $"{phase}: count mismatch: healthy bindposes={healthy.bindposes.Length}, repro bindposes={repro.bindposes.Length}, healthy skin={healthy.skinMatrices.Length}, repro skin={repro.skinMatrices.Length}.";
            }

            float maxBindpose = 0f;
            float maxSkin = 0f;
            float maxBoneToRenderer = 0f;
            float maxBakedVertex = 0f;
            float maxBlendShapeWeight = 0f;
            int maxBindposeIndex = -1;
            int maxSkinIndex = -1;
            int maxBoneToRendererIndex = -1;
            int maxBakedVertexIndex = -1;
            int maxBlendShapeWeightIndex = -1;
            for (int i = 0; i < healthy.bindposes.Length; i++)
            {
                float bindposeError = MaxElementDifference(healthy.bindposes[i], repro.bindposes[i]);
                if (bindposeError > maxBindpose)
                {
                    maxBindpose = bindposeError;
                    maxBindposeIndex = i;
                }

                float skinError = MaxElementDifference(healthy.skinMatrices[i], repro.skinMatrices[i]);
                if (skinError > maxSkin)
                {
                    maxSkin = skinError;
                    maxSkinIndex = i;
                }

                float boneToRendererError = MaxElementDifference(healthy.boneToRendererMatrices[i], repro.boneToRendererMatrices[i]);
                if (boneToRendererError > maxBoneToRenderer)
                {
                    maxBoneToRenderer = boneToRendererError;
                    maxBoneToRendererIndex = i;
                }
            }

            if (healthy.bakedVertices.Length != repro.bakedVertices.Length)
            {
                return $"{phase}: baked vertex count mismatch: healthy={healthy.bakedVertices.Length}, repro={repro.bakedVertices.Length}.";
            }

            if (healthy.blendShapeWeights.Length != repro.blendShapeWeights.Length)
            {
                return $"{phase}: blendshape count mismatch: healthy={healthy.blendShapeWeights.Length}, repro={repro.blendShapeWeights.Length}.";
            }

            for (int i = 0; i < healthy.blendShapeWeights.Length; i++)
            {
                float blendShapeWeightError = Mathf.Abs(healthy.blendShapeWeights[i] - repro.blendShapeWeights[i]);
                if (blendShapeWeightError > maxBlendShapeWeight)
                {
                    maxBlendShapeWeight = blendShapeWeightError;
                    maxBlendShapeWeightIndex = i;
                }
            }

            for (int i = 0; i < healthy.bakedVertices.Length; i++)
            {
                float bakedVertexError = (healthy.bakedVertices[i] - repro.bakedVertices[i]).magnitude;
                if (bakedVertexError > maxBakedVertex)
                {
                    maxBakedVertex = bakedVertexError;
                    maxBakedVertexIndex = i;
                }
            }

            string boneName = maxBoneToRendererIndex >= 0 ? healthy.boneNames[maxBoneToRendererIndex] : "<none>";
            string reproBoneName = maxBoneToRendererIndex >= 0 ? repro.boneNames[maxBoneToRendererIndex] : "<none>";
            Matrix4x4 healthyBone = maxBoneToRendererIndex >= 0 ? healthy.boneToRendererMatrices[maxBoneToRendererIndex] : Matrix4x4.zero;
            Matrix4x4 reproBone = maxBoneToRendererIndex >= 0 ? repro.boneToRendererMatrices[maxBoneToRendererIndex] : Matrix4x4.zero;
            string skinName = maxSkinIndex >= 0 ? healthy.boneNames[maxSkinIndex] : "<none>";
            string reproSkinName = maxSkinIndex >= 0 ? repro.boneNames[maxSkinIndex] : "<none>";
            Matrix4x4 healthySkin = maxSkinIndex >= 0 ? healthy.skinMatrices[maxSkinIndex] : Matrix4x4.zero;
            Matrix4x4 reproSkin = maxSkinIndex >= 0 ? repro.skinMatrices[maxSkinIndex] : Matrix4x4.zero;
            string blendShapeName = maxBlendShapeWeightIndex >= 0 ? healthy.blendShapeNames[maxBlendShapeWeightIndex] : "<none>";
            float healthyBlendShapeWeight = maxBlendShapeWeightIndex >= 0 ? healthy.blendShapeWeights[maxBlendShapeWeightIndex] : 0f;
            float reproBlendShapeWeight = maxBlendShapeWeightIndex >= 0 ? repro.blendShapeWeights[maxBlendShapeWeightIndex] : 0f;

            return maxBindpose <= MatrixTolerance && maxBakedVertex <= MatrixTolerance
                ? string.Empty
                : $"{phase}: max baked-vertex delta={maxBakedVertex} at {maxBakedVertexIndex}; max blendshape-weight delta={maxBlendShapeWeight} at {maxBlendShapeWeightIndex} ({blendShapeName}: healthy={healthyBlendShapeWeight}, repro={reproBlendShapeWeight}); max bindpose delta={maxBindpose} at {maxBindposeIndex}; max bone-to-renderer delta={maxBoneToRenderer} at {maxBoneToRendererIndex} ({boneName} / {reproBoneName}). HealthyBone={healthyBone}; ReproBone={reproBone}; max skin-matrix delta={maxSkin} at {maxSkinIndex} ({skinName} / {reproSkinName}). Healthy={healthySkin}; Repro={reproSkin}.";
        }

        private static float MaxElementDifference(Matrix4x4 first, Matrix4x4 second)
        {
            float max = 0f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    max = Mathf.Max(max, Mathf.Abs(first[row, column] - second[row, column]));
                }
            }

            return max;
        }

        public static System.Type RuntimeType(string typeName)
        {
            System.Type type = System.Type.GetType("zgock.ShapeSync." + typeName + ", zgock.ShapeSync.Runtime")
                ?? System.Type.GetType(typeName + ", zgock.ShapeSync.Runtime");
            Assert.That(type, Is.Not.Null, $"Could not find runtime type '{typeName}'.");
            return type;
        }
}
