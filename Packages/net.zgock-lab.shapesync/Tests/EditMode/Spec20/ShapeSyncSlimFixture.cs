// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync.Editor;
using zgock.ShapeSync.Materials;

namespace zgock.ShapeSync.Tests.EditMode.Spec20
{
    /// <summary>
    /// Runtime-created, PlayTest-independent Fixture used by the Spec20.9 Slim Oracle and
    /// diagnostic tests. The fixture is deliberately transient and is never committed.
    /// </summary>
    internal sealed class ShapeSyncSlimFixture : IDisposable
    {
        internal const string Root = ShapeSyncTestAssetPaths.Spec20SlimFixtureRoot;
        internal string DatabasePath { get; private set; }
        internal ShapeSyncDatabase Database { get; private set; }

        internal static ShapeSyncSlimFixture Create()
        {
            var fixture = new ShapeSyncSlimFixture();
            fixture.CreateDatabase();
            return fixture;
        }

        public void Dispose()
        {
            Database = null;
            // Generation deliberately writes into the transient root so the Oracle exercises
            // the real AssetDatabase path. Remove the whole root, including generated outputs,
            // at the end of every test; no probe asset is allowed to remain in the worktree.
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private void CreateDatabase()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            ShapeSyncTestAssetPaths.EnsureConsumerTempRoot();
            AssetDatabase.CreateFolder(ShapeSyncTestAssetPaths.ConsumerTempRoot, "__Spec20_9_SlimFixture");
            DatabasePath = Root + "/SlimDatabase.prefab";
            Assert.That(ShapeSyncDatabaseAsset.TryCreateAtPath(DatabasePath, out ShapeSyncDatabase created, out string createDiagnostic), Is.True, createDiagnostic);
            Assert.That(ShapeSyncDatabaseTransaction.TryEditStructureWithAssets(DatabasePath, (contents, intermediate, transaction) =>
            {
                ShapeSyncDatabase database = contents;
                GameObject figure = CreateHumanoid("SlimFigure", intermediate, transaction, out SkinnedMeshRenderer figureRenderer, out Material figureMaterial, out MaterialShaderAdapter figureAdapter);
                Assert.That(database.Registry.TryRegisterBaseFigure(database, "SlimFigure", figure, out string baseDiagnostic), Is.True, baseDiagnostic);
                Assert.That(database.Registry.TryRegisterMaterialEntry(database, "Body", figureRenderer, 0, "Body", figureMaterial, figureAdapter, out string materialDiagnostic), Is.True, materialDiagnostic);

                GameObject tallFigure = CreateHumanoid("Tall", intermediate, transaction, out _, out _, out _);
                GameObject basePbmFigure = CreateHumanoid("SlimFigure_Wide", intermediate, transaction, out _, out _, out _);
                GameObject tallPbmFigure = CreateHumanoid("Tall_Wide", intermediate, transaction, out _, out _, out _);
                AddRegistryItem(database.Registry, "figureAxes", new ShapeSyncDatabaseRegistry.FigureAxisEntry(
                    "Tall", ShapeSyncDatabaseRegistry.FigureAxisKind.Fbm,
                    new[] { new ShapeSyncDatabaseRegistry.AxisFigureEntry("Tall", tallFigure) }));
                AddRegistryItem(database.Registry, "figureAxes", new ShapeSyncDatabaseRegistry.FigureAxisEntry(
                    "Wide", ShapeSyncDatabaseRegistry.FigureAxisKind.Pbm,
                    new[]
                    {
                        new ShapeSyncDatabaseRegistry.AxisFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbmFigure),
                        new ShapeSyncDatabaseRegistry.AxisFigureEntry("Tall", tallPbmFigure)
                    }));

                GameObject outfit = CreateHumanoid("SlimOutfit", intermediate, transaction, out SkinnedMeshRenderer outfitRenderer, out _, out _);
                // Use the Database-owned Figure material for deterministic identity matching in
                // the Outfit MaterialProxy transfer.
                outfitRenderer.sharedMaterials = new[] { figureMaterial };
                GameObject tallOutfit = CreateHumanoid("SlimOutfit_Tall", intermediate, transaction, out SkinnedMeshRenderer tallOutfitRenderer, out _, out _);
                GameObject basePbmOutfit = CreateHumanoid("SlimOutfit_Wide", intermediate, transaction, out SkinnedMeshRenderer basePbmOutfitRenderer, out _, out _);
                GameObject tallPbmOutfit = CreateHumanoid("SlimOutfit_Tall_Wide", intermediate, transaction, out SkinnedMeshRenderer tallPbmOutfitRenderer, out _, out _);
                tallOutfitRenderer.sharedMaterials = new[] { figureMaterial };
                basePbmOutfitRenderer.sharedMaterials = new[] { figureMaterial };
                tallPbmOutfitRenderer.sharedMaterials = new[] { figureMaterial };
                Assert.That(database.Registry.TryAddOutfit("SlimOutfit", "Slim Outfit", ShapeSyncDatabaseRegistry.OutfitKind.Mesh, out string outfitDiagnostic), Is.True, outfitDiagnostic);
                ShapeSyncDatabaseRegistry.OutfitEntry entry = database.Registry.Outfits.Single(value => value.Identity == "SlimOutfit");
                entry.SetAxisFigures(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, outfit, outfit, outfit, null, new[] { "Body" }),
                    new ShapeSyncDatabaseRegistry.OutfitAxisFigureEntry("Tall", tallOutfit, tallOutfit, tallOutfit, null, new[] { "Body" })
                });
                entry.SetMaterialEntries(new[] { new ShapeSyncDatabaseRegistry.OutfitMaterialEntry("Body", figureMaterial, figureAdapter) });
                entry.SetPbmFollows(new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitPbmFollowEntry("Wide", new[]
                    {
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, basePbmOutfit, basePbmOutfit),
                        new ShapeSyncDatabaseRegistry.OutfitPbmFollowFigureEntry("Tall", tallPbmOutfit, tallPbmOutfit)
                    })
                });
                entry.SetCollection(ShapeSyncDatabaseRegistry.OutfitCollectionKind.Full, false, new[]
                {
                    new ShapeSyncDatabaseRegistry.OutfitCollectionEntry(ShapeSyncDatabaseRegistry.BaseShapeKey, outfit, outfit),
                    new ShapeSyncDatabaseRegistry.OutfitCollectionEntry("Tall", tallOutfit, tallOutfit)
                });
                EditorUtility.SetDirty(database.Registry);
            }, out string editDiagnostic), Is.True, editDiagnostic);

            AssetDatabase.ImportAsset(DatabasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(ShapeSyncDatabaseAsset.TryOpen(DatabasePath, out ShapeSyncDatabase reopened, out string openDiagnostic), Is.True, openDiagnostic);
            Database = reopened;
        }

        private static GameObject CreateHumanoid(string name, Transform intermediate, ShapeSyncDatabaseTransaction.EditContext transaction,
            out SkinnedMeshRenderer renderer, out Material material, out MaterialShaderAdapter adapter)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(intermediate, false);
            Transform hips = NewBone(root.transform, "Hips");
            Transform spine = NewBone(hips, "Spine");
            Transform chest = NewBone(spine, "Chest");
            Transform neck = NewBone(chest, "Neck");
            Transform head = NewBone(neck, "Head");
            Transform leftUpperArm = NewBone(chest, "LeftUpperArm");
            Transform leftLowerArm = NewBone(leftUpperArm, "LeftLowerArm");
            NewBone(leftLowerArm, "LeftHand");
            Transform rightUpperArm = NewBone(chest, "RightUpperArm");
            Transform rightLowerArm = NewBone(rightUpperArm, "RightLowerArm");
            NewBone(rightLowerArm, "RightHand");
            Transform leftUpperLeg = NewBone(hips, "LeftUpperLeg");
            Transform leftLowerLeg = NewBone(leftUpperLeg, "LeftLowerLeg");
            NewBone(leftLowerLeg, "LeftFoot");
            Transform rightUpperLeg = NewBone(hips, "RightUpperLeg");
            Transform rightLowerLeg = NewBone(rightUpperLeg, "RightLowerLeg");
            NewBone(rightLowerLeg, "RightFoot");
            leftUpperArm.localPosition = new Vector3(-.5f, .5f, 0f);
            rightUpperArm.localPosition = new Vector3(.5f, .5f, 0f);
            leftUpperLeg.localPosition = new Vector3(-.25f, -.5f, 0f);
            rightUpperLeg.localPosition = new Vector3(.25f, -.5f, 0f);
            renderer = root.AddComponent<SkinnedMeshRenderer>();
            Mesh mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bindposes = new[] { hips.worldToLocalMatrix * root.transform.localToWorldMatrix, spine.worldToLocalMatrix * root.transform.localToWorldMatrix, head.worldToLocalMatrix * root.transform.localToWorldMatrix };
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
                new BoneWeight { boneIndex0 = 2, weight0 = 1f }
            };
            Vector3[] delta = { new Vector3(.1f, 0f, 0f), Vector3.zero, Vector3.zero };
            mesh.AddBlendShapeFrame("SlimMorph", 100f, delta, new Vector3[3], new Vector3[3]);
            transaction.AddSubAsset(mesh);
            renderer.sharedMesh = mesh;
            renderer.rootBone = hips;
            renderer.bones = new[] { hips, spine, head };

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("Slim fixture requires the URP Unlit shader.");
            material = new Material(shader) { name = name + "Material" };
            material.SetColor("_BaseColor", Color.white);
            transaction.AddSubAsset(material);
            Assert.That(ShapeSyncMaterialAdapterResolver.TryCreateFor(material, out adapter, out string adapterDiagnostic), Is.True, adapterDiagnostic);
            adapter.name = name + "Adapter";
            transaction.AddSubAsset(adapter);
            renderer.sharedMaterials = new[] { material };

            Animator animator = root.AddComponent<Animator>();
            HumanDescription description = CreateHumanDescription(root.transform);
            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("Slim fixture AvatarBuilder could not create a valid humanoid Avatar.");
            avatar.name = name + "Avatar";
            transaction.AddSubAsset(avatar);
            animator.avatar = avatar;
            ShapeSyncFigureImportRecord record = root.AddComponent<ShapeSyncFigureImportRecord>();
            Assert.That(record.TryConfigure(new[] { renderer }, out string recordDiagnostic), Is.True, recordDiagnostic);
            return root;
        }

        private static void AddRegistryItem<T>(ShapeSyncDatabaseRegistry registry, string fieldName, T value)
        {
            FieldInfo field = typeof(ShapeSyncDatabaseRegistry).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(typeof(ShapeSyncDatabaseRegistry).FullName, fieldName);
            ((List<T>)field.GetValue(registry)).Add(value);
        }

        private static Transform NewBone(Transform parent, string name)
        {
            GameObject bone = new GameObject(name);
            bone.transform.SetParent(parent, false);
            return bone.transform;
        }

        private static HumanDescription CreateHumanDescription(Transform root)
        {
            string[] names =
            {
                "Hips", "Spine", "Chest", "Neck", "Head",
                "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
                "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot"
            };
            string[] humans =
            {
                "Hips", "Spine", "Chest", "Neck", "Head",
                "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
                "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot"
            };
            var human = new HumanBone[names.Length];
            for (int index = 0; index < names.Length; index++)
                human[index] = new HumanBone { humanName = humans[index], boneName = names[index] };
            var skeleton = root.GetComponentsInChildren<Transform>(true).Select(transform => new SkeletonBone
            {
                name = transform.name,
                position = transform.localPosition,
                rotation = transform.localRotation,
                scale = transform.localScale
            }).ToArray();
            return new HumanDescription { human = human, skeleton = skeleton, upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f, feetSpacing = 0f, hasTranslationDoF = false };
        }
    }
}
#endif
