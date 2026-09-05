// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using zgock.ShapeSync;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Custom Inspector for configuring an <see cref="OutfitAttacher"/>.</summary>
    [CustomEditor(typeof(OutfitAttacher))]
    public sealed class OutfitAttacherEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
        DrawDefaultInspector();

        OutfitAttacher outfitAttacher = (OutfitAttacher)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Outfit Prefab Attach", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            DrawDropArea(outfitAttacher);
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to attach an Outfit Prefab.", MessageType.Info);
        }

        DrawAttachedOutfits(outfitAttacher);
        DrawPcmDiagnostics(outfitAttacher);
    }

    private static void DrawDropArea(OutfitAttacher outfitAttacher)
    {
        Rect dropArea = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop Outfit Prefab Here", EditorStyles.helpBox);

        Event currentEvent = Event.current;
        if (!dropArea.Contains(currentEvent.mousePosition))
        {
            return;
        }

        switch (currentEvent.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = TryGetOutfitPrefab(DragAndDrop.objectReferences, out _) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                currentEvent.Use();
                break;
            case EventType.DragPerform:
                DragAndDrop.AcceptDrag();
                if (!TryGetOutfitPrefab(DragAndDrop.objectReferences, out ShapeSyncOutfit outfitPrefab))
                {
                    Debug.LogWarning("OutfitAttacher accepts only a Project Outfit Prefab with ShapeSyncOutfit.", outfitAttacher);
                }
                else
                {
                    outfitAttacher.TryAttach(outfitPrefab);
                }

                currentEvent.Use();
                break;
        }
    }

    private static bool TryGetOutfitPrefab(Object[] draggedObjects, out ShapeSyncOutfit outfitPrefab)
    {
        outfitPrefab = null;
        if (draggedObjects == null || draggedObjects.Length != 1)
        {
            return false;
        }

        GameObject prefabRoot = draggedObjects[0] as GameObject;
        if (prefabRoot == null || !PrefabUtility.IsPartOfPrefabAsset(prefabRoot))
        {
            return false;
        }

        outfitPrefab = prefabRoot.GetComponent<ShapeSyncOutfit>();
        return outfitPrefab != null;
    }

    private static void DrawAttachedOutfits(OutfitAttacher outfitAttacher)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Attached Outfits", EditorStyles.boldLabel);
        IReadOnlyList<AttachedOutfitRegistrySet> attachedOutfits = outfitAttacher.AttachedOutfits;
        if (attachedOutfits.Count == 0)
        {
            EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
            return;
        }

        for (int i = 0; i < attachedOutfits.Count; i++)
        {
            AttachedOutfitRegistrySet attachedOutfit = attachedOutfits[i];
            if (attachedOutfit == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.IsNullOrEmpty(attachedOutfit.RegistryName) ? attachedOutfit.RegistryId : attachedOutfit.RegistryName);
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Delete", GUILayout.Width(64f)))
                    {
                        outfitAttacher.Detach(attachedOutfit.RegistryId);
                    }
                }
            }
        }
    }

        private static void DrawPcmDiagnostics(OutfitAttacher outfitAttacher)
        {
        DynamicMorphAdapter adapter = outfitAttacher.DynamicBoneBlender != null ? outfitAttacher.DynamicBoneBlender.DynamicMorphAdapter : null;
        if (adapter == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Plugable PCM Diagnostics", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Slot Groups", adapter.Schema.PcmSlotCount);
            EditorGUILayout.IntField("Free Slot Groups", adapter.FreeSlotGroups);
            EditorGUILayout.IntField("Active Registrations", adapter.ActiveRegistrationCount);
            EditorGUILayout.IntField("First Slot BlendShape", adapter.Schema.FirstSlotBlendShapeIndex);
        }
        }
    }
}
