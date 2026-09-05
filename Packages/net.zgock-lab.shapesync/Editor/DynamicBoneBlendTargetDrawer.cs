// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using UnityEditor;
using zgock.ShapeSync;
using UnityEngine;

namespace zgock.ShapeSync.Editor
{
    /// <summary>Draws a serialized dynamic bone BlendShape target in the Inspector.</summary>
    [CustomPropertyDrawer(typeof(DynamicBoneBlendTarget))]
    public sealed class DynamicBoneBlendTargetDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        SerializedProperty differences = property.FindPropertyRelative("pbmDifferenceTargets");
        float differenceHeight = differences != null ? EditorGUI.GetPropertyHeight(differences, true) : 0f;
        return EditorGUIUtility.singleLineHeight * 6f + Spacing * 6f + differenceHeight;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        SerializedProperty blendName = property.FindPropertyRelative("blendName");
        SerializedProperty enabled = property.FindPropertyRelative("enabled");
        SerializedProperty weight = property.FindPropertyRelative("weight");
        SerializedProperty targetAvatar = property.FindPropertyRelative("targetAvatar");
        SerializedProperty targetRegistry = property.FindPropertyRelative("targetRegistry");
        SerializedProperty differences = property.FindPropertyRelative("pbmDifferenceTargets");

        line.y += EditorGUIUtility.singleLineHeight + Spacing;
        EditorGUI.PropertyField(line, blendName);
        line.y += EditorGUIUtility.singleLineHeight + Spacing;
        EditorGUI.PropertyField(line, enabled);
        line.y += EditorGUIUtility.singleLineHeight + Spacing;
        DrawWeight(line, weight);
        line.y += EditorGUIUtility.singleLineHeight + Spacing;
        EditorGUI.PropertyField(line, targetAvatar);
        line.y += EditorGUIUtility.singleLineHeight + Spacing;
        EditorGUI.PropertyField(line, targetRegistry);

        if (differences != null)
        {
            line.y += EditorGUIUtility.singleLineHeight + Spacing;
            line.height = EditorGUI.GetPropertyHeight(differences, true);
            EditorGUI.PropertyField(line, differences, new GUIContent("PBM Difference Targets"), true);
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

        private static void DrawWeight(Rect position, SerializedProperty weight)
            => DynamicBoneBlendWeightField.Draw(position, weight);
    }

    /// <summary>Shared raw DDB weight field for authoring inspectors.</summary>
    internal static class DynamicBoneBlendWeightField
    {
        private const float Spacing = 4f;

        /// <summary>Returns the symmetric raw DDB slider limit for a finite value.</summary>
        internal static float GetSliderLimit(float value) => Mathf.Max(1f, Mathf.Ceil(Mathf.Abs(value)));

        /// <summary>Draws the DDB raw-weight slider and direct numeric field in one layout row.</summary>
        public static float DrawLayout(float value, float fieldWidth = 72f)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return EditorGUILayout.FloatField(value, GUILayout.Width(fieldWidth));
            float sliderLimit = GetSliderLimit(value);
            float sliderValue = GUILayout.HorizontalSlider(value, -sliderLimit, sliderLimit, GUILayout.MinWidth(56f));
            return EditorGUILayout.FloatField(sliderValue, GUILayout.Width(fieldWidth));
        }

        /// <summary>Draws the DDB raw-weight slider and direct numeric field at an explicit rectangle.</summary>
        public static void Draw(Rect position, SerializedProperty weight)
        {
            Rect content = EditorGUI.PrefixLabel(position, new GUIContent("Weight"));
            float fieldWidth = Mathf.Min(88f, content.width * 0.35f);
            Rect sliderRect = new Rect(content.x, content.y, content.width - fieldWidth - Spacing, content.height);
            Rect fieldRect = new Rect(sliderRect.xMax + Spacing, content.y, fieldWidth, content.height);
            float value = weight.floatValue;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                EditorGUI.BeginChangeCheck();
                float nonFiniteValue = EditorGUI.FloatField(content, value);
                if (EditorGUI.EndChangeCheck()) weight.floatValue = nonFiniteValue;
                return;
            }
            EditorGUI.BeginChangeCheck();
            float sliderLimit = GetSliderLimit(value);
            float sliderValue = GUI.HorizontalSlider(sliderRect, value, -sliderLimit, sliderLimit);
            if (EditorGUI.EndChangeCheck()) weight.floatValue = sliderValue;
            EditorGUI.BeginChangeCheck();
            float typedValue = EditorGUI.FloatField(fieldRect, weight.floatValue);
            if (EditorGUI.EndChangeCheck()) weight.floatValue = typedValue;
        }
    }
}
