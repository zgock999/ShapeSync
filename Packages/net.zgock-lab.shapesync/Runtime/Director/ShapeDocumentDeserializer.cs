// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEngine;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Standard Figure component that deserializes runtime Shapes from a <see cref="ShapeDocument"/> carrier.</summary>
    public class ShapeDocumentDeserializer : ShapeDeserializer, IShapeDocumentSourceDeserializer
    {
        private ShapeDocument lastLoadedDocument;

        /// <summary>Gets the successfully decoded ShapeDocument for the current deserialize operation.</summary>
        /// <remarks>This is a transient standard-format handoff to the co-located ShapeDirector. It does not retain runtime Shapes or own document execution.</remarks>
        internal ShapeDocument LastLoadedDocument => lastLoadedDocument;

        /// <inheritdoc />
        public override bool TryDeserialize(string fileName, out List<ShapeSyncShape> runtimeShapes)
        {
#if UNITY_EDITOR
            runtimeShapes = new List<ShapeSyncShape>();
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            ShapeDocument document = UnityEditor.AssetDatabase.LoadAssetAtPath<ShapeDocument>(fileName);
            return TryDeserialize(document, out runtimeShapes, out _, out _);
#else
            runtimeShapes = new List<ShapeSyncShape>();
            return false;
#endif
        }

        /// <inheritdoc />
        public bool TryDeserialize(ShapeDocument source, out List<ShapeSyncShape> runtimeShapes, out ShapeSyncDocument payload, out StackMachineDiagnostic diagnostic)
        {
            runtimeShapes = new List<ShapeSyncShape>();
            payload = null;
            diagnostic = null;
            lastLoadedDocument = null;
            if (source == null)
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "ShapeDocumentRequired", "ShapeDocumentDeserializer requires an in-memory ShapeDocument source.");
                return false;
            }
            var records = new SortedDictionary<int, ShapeSyncShape>();
            if (!Add(source.MorphShapes, records, CreateMorph) || !Add(source.SkinShapes, records, CreateSkin) || !Add(source.HairShapes, records, CreateHair) || !Add(source.OutfitShapes, records, CreateOutfit))
            {
                diagnostic = StackMachineDiagnostic.CreateDomain("director", "ShapeDocumentDecodeFailed", "ShapeDocumentDeserializer could not decode all serialized Shape records.");
                return false;
            }
            foreach (KeyValuePair<int, ShapeSyncShape> pair in records) runtimeShapes.Add(pair.Value);
            if (!source.TryGetSnapshot(out payload, out diagnostic))
            {
                runtimeShapes.Clear();
                return false;
            }
            lastLoadedDocument = source;
            return true;
        }

        private static bool Add<T>(IReadOnlyList<T> source, SortedDictionary<int, ShapeSyncShape> destination, System.Func<T, ShapeSyncShape> create) where T : class
        {
            for (int i = 0; i < source.Count; i++)
            {
                T record = source[i];
                if (record == null) return false;
                ShapeSyncShape shape = create(record);
                if (shape == null || !destination.TryAdd(GetPosition(record), shape)) return false;
            }
            return true;
        }

        private static int GetPosition<T>(T record) where T : class => record is SerializedMorphShape morph ? morph.ListPosition : ((SerializedPartsShape)(object)record).ListPosition;
        private static ShapeSyncShape CreateMorph(SerializedMorphShape source) => new MorphShape(source.ShapeId, source.Priority, source.Tags, source.Morphs);
        private static ShapeSyncShape CreateSkin(SerializedSkinShape source) => new SkinShape(source.ShapeId, source.Priority, source.Tags, source.Parts);
        private static ShapeSyncShape CreateHair(SerializedHairShape source) => new HairShape(source.ShapeId, source.Priority, source.Tags, source.Parts);
        private static ShapeSyncShape CreateOutfit(SerializedOutfitShape source) => new OutfitShape(source.ShapeId, source.Priority, source.Tags, source.Parts);
    }
}
