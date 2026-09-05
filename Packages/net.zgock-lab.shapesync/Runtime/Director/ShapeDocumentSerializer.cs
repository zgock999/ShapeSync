// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;

namespace zgock.ShapeSync
{
    /// <summary>Standard Figure component that serializes runtime Shapes into a <see cref="ShapeDocument"/> carrier.</summary>
    public class ShapeDocumentSerializer : ShapeSerializer
    {
        [System.NonSerialized] private string meshRecoverySource = string.Empty;
        [System.NonSerialized] private string materialRecoverySource = string.Empty;
        [System.NonSerialized] private StackMachine.MeshBinding meshBinding;
        [System.NonSerialized] private StackMachine.MaterialBinding materialBinding;

        /// <summary>Gets the detached recovery Mesh recipe source most recently supplied by ShapeDirector.</summary>
        protected string MeshRecoverySource => meshRecoverySource;
        /// <summary>Gets the detached recovery Material recipe source most recently supplied by ShapeDirector.</summary>
        protected string MaterialRecoverySource => materialRecoverySource;

        /// <inheritdoc />
        public override bool TrySerialize(string fileName, List<ShapeSyncShape> runtimeShapes)
        {
            if (string.IsNullOrWhiteSpace(fileName) || runtimeShapes == null) return false;
#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<ShapeDocument>(fileName) != null) return false;
            var morphs = new List<SerializedMorphShape>(); var skins = new List<SerializedSkinShape>(); var hairs = new List<SerializedHairShape>(); var outfits = new List<SerializedOutfitShape>();
            for (int i = 0; i < runtimeShapes.Count; i++)
            {
                ShapeSyncShape shape = runtimeShapes[i];
                if (shape is MorphShape morph) morphs.Add(Copy(morph, i));
                else if (shape is SkinShape skin) skins.Add(Copy<SerializedSkinShape>(skin, i));
                else if (shape is HairShape hair) hairs.Add(Copy<SerializedHairShape>(hair, i));
                else if (shape is OutfitShape outfit) outfits.Add(Copy<SerializedOutfitShape>(outfit, i));
                else return false;
            }
            var savedDocument = ScriptableObject.CreateInstance<ShapeDocument>();
            savedDocument.MeshRecipe = new StackMachine.MeshRecipeDocument { wordSource = meshRecoverySource };
            savedDocument.MeshBinding = meshBinding;
            savedDocument.MaterialRecipe = new StackMachine.MaterialRecipeDocument { wordSource = materialRecoverySource };
            savedDocument.MaterialBinding = materialBinding;
            savedDocument.ReplaceShapes(morphs, skins, hairs, outfits);
            UnityEditor.AssetDatabase.CreateAsset(savedDocument, fileName);
            UnityEditor.EditorUtility.SetDirty(savedDocument);
            UnityEditor.AssetDatabase.SaveAssets();
            return true;
#else
            return false;
#endif
        }

        internal void ConfigureRecoveryRecipeSources(
            string meshSource,
            StackMachine.MeshBinding meshBinding,
            string materialSource,
            StackMachine.MaterialBinding materialBinding)
        {
            meshRecoverySource = meshSource ?? string.Empty;
            materialRecoverySource = materialSource ?? string.Empty;
            this.meshBinding = meshBinding;
            this.materialBinding = materialBinding;
        }

        private static SerializedMorphShape Copy(MorphShape source, int position)
        {
            var result = new SerializedMorphShape { ListPosition = position, ShapeId = source.ShapeId, Priority = source.Priority };
            for (int i = 0; i < source.Tags.Count; i++) result.Tags.Add(source.Tags[i]);
            for (int i = 0; i < source.Morphs.Count; i++) result.Morphs.Add(source.Morphs[i]);
            return result;
        }

        private static T Copy<T>(PartsShape source, int position) where T : SerializedPartsShape, new()
        {
            var result = new T { ListPosition = position, ShapeId = source.ShapeId, Priority = source.Priority };
            for (int i = 0; i < source.Tags.Count; i++) result.Tags.Add(source.Tags[i]);
            for (int i = 0; i < source.Parts.Count; i++) result.Parts.Add(source.Parts[i] == null ? null : source.Parts[i].Clone());
            return result;
        }
    }
}
