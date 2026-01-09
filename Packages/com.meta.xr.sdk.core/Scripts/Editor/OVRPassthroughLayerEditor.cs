/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using UnityEditor;
using UnityEngine;
using ColorMapEditorType = OVRPassthroughLayer.ColorMapEditorType;

[CustomPropertyDrawer(typeof(OVRPassthroughLayer.SerializedSurfaceGeometry))]
class SerializedSurfaceGeometryPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // Find the SerializedProperties by name
        var meshFilterProperty =
            property.FindPropertyRelative(nameof(OVRPassthroughLayer.SerializedSurfaceGeometry.meshFilter));
        var updateTransformProperty =
            property.FindPropertyRelative(nameof(OVRPassthroughLayer.SerializedSurfaceGeometry.updateTransform));
        var propertyHeight = position.height / 2;

        using (new EditorGUI.PropertyScope(position, label, property))
        {
            var surfaceGeometryPropertyPosition = new Rect(position.x, position.y, position.width, propertyHeight);
            EditorGUI.PropertyField(surfaceGeometryPropertyPosition, meshFilterProperty,
                new GUIContent("Surface Geometry", "The GameObject from which to generate surface geometry."));

            var heightOffset = EditorGUI.GetPropertyHeight(updateTransformProperty) +
                               EditorGUIUtility.standardVerticalSpacing;
            var updateTransformPosition =
                new Rect(position.x, position.y + heightOffset, position.width, propertyHeight);
            EditorGUI.PropertyField(updateTransformPosition, updateTransformProperty, new GUIContent("Update Transform",
                "When enabled, updates the mesh's transform every frame. Use this if the GameObject is dynamic."));
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return base.GetPropertyHeight(property, label) * 2.2f;
    }
}

[CustomEditor(typeof(OVRPassthroughLayer))]
public class OVRPassthroughLayerEditor : Editor
{
    internal static readonly string[] ColorMapNames =
    {
        "None",
        "Color Adjustment",
        "Grayscale",
        "Grayscale to Color",
        "Color LUT",
        "Blended Color LUTs",
        "Custom"
    };

    internal static readonly string[] SelectableColorMapNames =
    {
        ColorMapNames[0],
        ColorMapNames[1],
        ColorMapNames[2],
        ColorMapNames[3],
        ColorMapNames[4],
        ColorMapNames[5]
    };

    internal static readonly ColorMapEditorType[] ColorMapTypes =
    {
        ColorMapEditorType.None,
        ColorMapEditorType.ColorAdjustment,
        ColorMapEditorType.Grayscale,
        ColorMapEditorType.GrayscaleToColor,
        ColorMapEditorType.ColorLut,
        ColorMapEditorType.InterpolatedColorLut,
        ColorMapEditorType.Custom
    };

    private SerializedProperty _projectionSurfaces;

    private SerializedProperty _propProjectionSurfaceType;
    private SerializedProperty _propOverlayType;
    private SerializedProperty _propCompositionDepth;

    private SerializedProperty _propTextureOpacity;
    private SerializedProperty _propEdgeRenderingEnabled;
    private SerializedProperty _propEdgeColor;
    private SerializedProperty _propColorMapEditorContrast;
    private SerializedProperty _propColorMapEditorBrightness;
    private SerializedProperty _propColorMapEditorPosterize;
    private SerializedProperty _propColorMapEditorGradient;
    private SerializedProperty _propColorMapEditorSaturation;
    private SerializedProperty _propPassthroughLayerResumed;

    private SerializedProperty _propColorLutSourceTexture;
    private SerializedProperty _propColorLutTargetTexture;
    private SerializedProperty _propLutWeight;
    private SerializedProperty _propFlipLutY;

    void OnEnable()
    {
        _projectionSurfaces = serializedObject.FindProperty(nameof(OVRPassthroughLayer.serializedSurfaceGeometry));

        _propProjectionSurfaceType = serializedObject.FindProperty(nameof(OVRPassthroughLayer.projectionSurfaceType));
        _propOverlayType = serializedObject.FindProperty(nameof(OVRPassthroughLayer.overlayType));
        _propCompositionDepth = serializedObject.FindProperty(nameof(OVRPassthroughLayer.compositionDepth));
        _propTextureOpacity = serializedObject.FindProperty(nameof(OVRPassthroughLayer.textureOpacity_));
        _propEdgeRenderingEnabled = serializedObject.FindProperty(nameof(OVRPassthroughLayer.edgeRenderingEnabled_));
        _propEdgeColor = serializedObject.FindProperty(nameof(OVRPassthroughLayer.edgeColor_));
        _propColorMapEditorContrast = serializedObject.FindProperty(nameof(OVRPassthroughLayer.colorMapEditorContrast));
        _propColorMapEditorBrightness =
            serializedObject.FindProperty(nameof(OVRPassthroughLayer.colorMapEditorBrightness));
        _propColorMapEditorPosterize =
            serializedObject.FindProperty(nameof(OVRPassthroughLayer.colorMapEditorPosterize));
        _propColorMapEditorSaturation =
            serializedObject.FindProperty(nameof(OVRPassthroughLayer.colorMapEditorSaturation));
        _propColorMapEditorGradient = serializedObject.FindProperty(nameof(OVRPassthroughLayer.colorMapEditorGradient));
        _propPassthroughLayerResumed = serializedObject.FindProperty(nameof(OVRPassthroughLayer.passthroughLayerResumed));

        _propColorLutSourceTexture = serializedObject.FindProperty(nameof(OVRPassthroughLayer._colorLutSourceTexture));
        _propColorLutTargetTexture = serializedObject.FindProperty(nameof(OVRPassthroughLayer._colorLutTargetTexture));
        _propLutWeight = serializedObject.FindProperty(nameof(OVRPassthroughLayer._lutWeight));
        _propFlipLutY = serializedObject.FindProperty(nameof(OVRPassthroughLayer._flipLutY));
    }

    public override void OnInspectorGUI()
    {
        OVRPassthroughLayer layer = (OVRPassthroughLayer)target;

        serializedObject.Update();
        EditorGUILayout.PropertyField(_propProjectionSurfaceType,
            new GUIContent("Projection Surface", "The type of projection surface for this Passthrough layer"));

        if (layer.projectionSurfaceType == OVRPassthroughLayer.ProjectionSurfaceType.UserDefined)
        {
            EditorGUILayout.PropertyField(_projectionSurfaces, new GUIContent("Projection Surfaces"));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Compositing", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(_propOverlayType,
            new GUIContent("Placement", "Whether this overlay should layer behind the scene or in front of it"));
        EditorGUILayout.PropertyField(_propCompositionDepth,
            new GUIContent("Composition Depth",
                "Depth value used to sort layers in the scene, smaller value appears in front"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Style", EditorStyles.boldLabel);

        EditorGUILayout.Slider(_propTextureOpacity, 0, 1f, new GUIContent("Opacity"));

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(_propEdgeRenderingEnabled,
            new GUIContent("Edge Rendering", "Highlight salient edges in the camera images in a specific color"));
        EditorGUILayout.PropertyField(_propEdgeColor, new GUIContent("Edge Color"));

        if (serializedObject.ApplyModifiedProperties())
        {
            layer.SetStyleDirty();
        }

        layer.textureOpacity = _propTextureOpacity.floatValue;
        layer.edgeRenderingEnabled = _propEdgeRenderingEnabled.boolValue;
        layer.edgeColor = _propEdgeColor.colorValue;

        EditorGUILayout.Space();

        // Custom popup for color map type to control order, names, and visibility of types
        int colorMapTypeIndex = Array.IndexOf(ColorMapTypes, layer.colorMapEditorType);
        if (colorMapTypeIndex == -1)
        {
            Debug.LogWarning("Invalid color map type encountered");
            colorMapTypeIndex = 0;
        }

        // Dropdown list contains "Custom" only if it is currently selected.
        string[] colorMapNames = layer.colorMapEditorType == ColorMapEditorType.Custom
            ? ColorMapNames
            : SelectableColorMapNames;
        GUIContent[] colorMapLabels = new GUIContent[colorMapNames.Length];
        for (int i = 0; i < colorMapNames.Length; i++)
            colorMapLabels[i] = new GUIContent(colorMapNames[i]);
        bool modified = false;
        OVREditorUtil.SetupPopupField(target,
            new GUIContent("Color Control", "The type of color controls applied to this layer"), ref colorMapTypeIndex,
            colorMapLabels,
            ref modified);
        layer.colorMapEditorType = ColorMapTypes[colorMapTypeIndex];

        if (layer.colorMapEditorType == ColorMapEditorType.Grayscale
            || layer.colorMapEditorType == ColorMapEditorType.GrayscaleToColor
            || layer.colorMapEditorType == ColorMapEditorType.ColorAdjustment)
        {
            EditorGUILayout.PropertyField(_propColorMapEditorContrast, new GUIContent("Contrast"));
            EditorGUILayout.PropertyField(_propColorMapEditorBrightness, new GUIContent("Brightness"));
        }

        if (layer.colorMapEditorType == ColorMapEditorType.Grayscale
            || layer.colorMapEditorType == ColorMapEditorType.GrayscaleToColor)
        {
            EditorGUILayout.PropertyField(_propColorMapEditorPosterize, new GUIContent("Posterize"));
        }

        if (layer.colorMapEditorType == ColorMapEditorType.ColorAdjustment)
        {
            EditorGUILayout.PropertyField(_propColorMapEditorSaturation, new GUIContent("Saturation"));
        }

        if (layer.colorMapEditorType == ColorMapEditorType.GrayscaleToColor)
        {
            EditorGUILayout.PropertyField(_propColorMapEditorGradient, new GUIContent("Colorize"));
        }

        if (layer.colorMapEditorType == ColorMapEditorType.ColorLut
            || layer.colorMapEditorType == ColorMapEditorType.InterpolatedColorLut)
        {
            var sourceLutLabel = layer.colorMapEditorType == ColorMapEditorType.ColorLut
                ? "LUT"
                : "Source LUT";

            EditorGUILayout.PropertyField(_propColorLutSourceTexture, new GUIContent(sourceLutLabel));
            PerformLutTextureCheck((Texture2D)_propColorLutSourceTexture.objectReferenceValue);

            if (layer.colorMapEditorType == ColorMapEditorType.InterpolatedColorLut)
            {
                EditorGUILayout.PropertyField(_propColorLutTargetTexture, new GUIContent("Target LUT"));
                PerformLutTextureCheck((Texture2D)_propColorLutTargetTexture.objectReferenceValue);
            }

            var flipLutYTooltip = "Flip LUT textures along the vertical axis on load. This is needed for LUT " +
                                  "images which have color (0, 0, 0) in the top-left corner. Some color grading systems, " +
                                  "e.g. Unity post-processing, have color (0, 0, 0) in the bottom-left corner, " +
                                  "in which case flipping is not needed.";
            EditorGUILayout.PropertyField(_propFlipLutY, new GUIContent("Flip Vertically", flipLutYTooltip));

            var weightTooltip = layer.colorMapEditorType == ColorMapEditorType.ColorLut
                ? "Blend between the original colors and the specified LUT. A value of 0 leaves the colors unchanged, a value of 1 fully applies the LUT."
                : "Blend between the source and the target LUT. A value of 0 fully applies the source LUT and a value of 1 fully applies the target LUT.";
            EditorGUILayout.PropertyField(_propLutWeight, new GUIContent("Blend", weightTooltip));
        }

        EditorGUILayout.PropertyField(_propPassthroughLayerResumed, new GUIContent("On Layer Resumed"));

        serializedObject.ApplyModifiedProperties();
    }

    internal static void PerformLutTextureCheck(Texture2D texture)
    {
        if (texture != null)
        {
            if (!OVRPassthroughColorLut.IsTextureSupported(texture, out var message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Error);
            }

            CheckLutImportSettings(texture);
        }
    }

    private static void CheckLutImportSettings(Texture lut)
    {
        if (lut != null)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(lut)) as TextureImporter;

            // Fails when using an internal texture as you can't change import settings on
            // builtin resources, thus the check for null
            if (importer != null)
            {
                bool isReadable = importer.isReadable == true;
                bool isUncompressed = importer.textureCompression == TextureImporterCompression.Uncompressed;
                bool valid = isReadable && isUncompressed;

                if (!valid)
                {
                    string warningMessage = ""
                                            + (isReadable ? "" : "Texture is not readable. ")
                                            + (isUncompressed ? "" : "Texture is compressed.");
                    DrawFixMeBox(warningMessage, () => SetLutImportSettings(importer));
                }
            }
        }
    }

    private static void SetLutImportSettings(TextureImporter importer)
    {
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        AssetDatabase.Refresh();
    }

    private static void DrawFixMeBox(string text, Action action)
    {
        EditorGUILayout.HelpBox(text, MessageType.Warning);

        GUILayout.Space(-32);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Fix", GUILayout.Width(60)))
                action();

            GUILayout.Space(8);
        }

        GUILayout.Space(11);
    }
}
