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
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
#if USING_XR_SDK_OCULUS
using Unity.XR.Oculus;
#endif

[CustomEditor(typeof(OVROverlay))]
public class OVROverlayEditor : Editor
{
    /// <summary>
    /// Common Video Types, to ease source and dest rect creation
    /// </summary>
    public enum StereoType
    {
        Custom = 0,
        Mono = 1,
        Stereo = 2,
        StereoLeftRight = 3,
        StereoTopBottom = 4,
    }

    public enum DisplayType
    {
        Custom = 0,
        Full = 1,
        Half = 2,
    }

    public enum OverlayLayerSharpenType
    {
        None,
        Normal,
        Quality
    }

    private OVRPlugin.LayerSuperSamplingType superSampleType = OVRPlugin.LayerSuperSamplingType.None;
    private OverlayLayerSharpenType sharpenType = OverlayLayerSharpenType.None;

    private bool sourceRectsVisible = false;
    private bool destRectsVisible = false;

    private bool _DidLookupSrcRectShader;
    private bool _DidLookupDestRectShader;
    private Shader _SrcRectShader;
    private Shader _DestRectShader;

    private Material _SrcRectMaterialLeft;
    private Material _SrcRectMaterialRight;
    private Material _DestRectMaterialLeft;
    private Material _DestRectMaterialRight;

    private TextureRect _DraggingRect;
    private Side _DraggingSide;

    enum TextureRect
    {
        None,
        SrcLeft,
        SrcRight,
        DestLeft,
        DestRight
    }

    enum Side
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private GUIContent[] selectableShapeNames;
    private OVROverlay.OverlayShape[] selectableShapeValues;

    private SerializedProperty _propCurrentOverlayType;
    private SerializedProperty _propCompositionDepth;
    private SerializedProperty _propNoDepthBufferTesting;
    private SerializedProperty _propCurrentOverlayShape;

    private SerializedProperty _propUseLegacyCubemapRotation;
    private SerializedProperty _propUseBicubicFiltering;
    private SerializedProperty _propIsExternalSurface;
    private SerializedProperty _propExternalSurfaceWidth;
    private SerializedProperty _propExternalSurfaceHeight;
    private SerializedProperty _propIsProtectedContent;
    private SerializedProperty _propIsDynamic;
    private SerializedProperty _propOverrideTextureRectMatrix;
    private SerializedProperty _propInvertTextureRects;
    private SerializedProperty _propOverridePerLayerColorScaleAndOffset;
    private SerializedProperty _propColorScale;
    private SerializedProperty _propColorOffset;
    private SerializedProperty _propPreviewInEditor;
    private SerializedProperty _propUseAutoFiltering;


    private void Awake()
    {
        List<GUIContent> selectableShapeNameList = new List<GUIContent>();
        List<OVROverlay.OverlayShape> selectableShapesValueList = new List<OVROverlay.OverlayShape>();
        foreach (OVROverlay.OverlayShape value in Enum.GetValues(typeof(OVROverlay.OverlayShape)))
        {
            if (!OVROverlay.IsPassthroughShape(value))
            {
                string name = Enum.GetName(typeof(OVROverlay.OverlayShape), value);
                selectableShapeNameList.Add(new GUIContent(name, name));
                selectableShapesValueList.Add(value);
            }
        }

        selectableShapeNames = selectableShapeNameList.ToArray();
        selectableShapeValues = selectableShapesValueList.ToArray();
    }

    private void OnEnable()
    {
        _propCurrentOverlayType = serializedObject.FindProperty(nameof(OVROverlay.currentOverlayType));
        _propCompositionDepth = serializedObject.FindProperty(nameof(OVROverlay.compositionDepth));
        _propNoDepthBufferTesting = serializedObject.FindProperty(nameof(OVROverlay.noDepthBufferTesting));
        _propCurrentOverlayShape = serializedObject.FindProperty(nameof(OVROverlay.currentOverlayShape));
        _propUseLegacyCubemapRotation = serializedObject.FindProperty(nameof(OVROverlay.useLegacyCubemapRotation));
        _propUseBicubicFiltering = serializedObject.FindProperty(nameof(OVROverlay.useBicubicFiltering));
        _propIsExternalSurface = serializedObject.FindProperty(nameof(OVROverlay.isExternalSurface));
        _propExternalSurfaceWidth = serializedObject.FindProperty(nameof(OVROverlay.externalSurfaceWidth));
        _propExternalSurfaceHeight = serializedObject.FindProperty(nameof(OVROverlay.externalSurfaceHeight));
        _propIsProtectedContent = serializedObject.FindProperty(nameof(OVROverlay.isProtectedContent));
        _propIsDynamic = serializedObject.FindProperty(nameof(OVROverlay.isDynamic));
        _propOverrideTextureRectMatrix = serializedObject.FindProperty(nameof(OVROverlay.overrideTextureRectMatrix));
        _propInvertTextureRects = serializedObject.FindProperty(nameof(OVROverlay.invertTextureRects));
        _propOverridePerLayerColorScaleAndOffset =
            serializedObject.FindProperty(nameof(OVROverlay.overridePerLayerColorScaleAndOffset));
        _propColorScale = serializedObject.FindProperty(nameof(OVROverlay.colorScale));
        _propColorOffset = serializedObject.FindProperty(nameof(OVROverlay.colorOffset));
        _propPreviewInEditor = serializedObject.FindProperty(nameof(OVROverlay._previewInEditor));
        _propUseAutoFiltering = serializedObject.FindProperty(nameof(OVROverlay.useAutomaticFiltering));

    }

    public override void OnInspectorGUI()
    {
        OVROverlay overlay = (OVROverlay)target;
        if (overlay == null)
        {
            return;
        }

        serializedObject.Update();

        bool tmpEnableDepthBufferTest = !_propNoDepthBufferTesting.boolValue;

        EditorGUILayout.LabelField("Display Order", EditorStyles.boldLabel);

        GUI.enabled = Application.isEditor && !Application.isPlaying;
        EditorGUILayout.PropertyField(_propCurrentOverlayType,
            new GUIContent("Overlay Type", "Whether this overlay should layer behind the scene or in front of it"));
        GUI.enabled = true;

        EditorGUILayout.PropertyField(_propCompositionDepth, new GUIContent("Composition Depth",
            "Depth value used to sort OVROverlays in the scene, smaller value appears in front"));
        tmpEnableDepthBufferTest = EditorGUILayout.Toggle(new GUIContent("Enable Depth Buffer Testing",
                "If true, will allow layer depth buffer compositing if the engine has \"Shared Depth Buffer\" enabled"),
            tmpEnableDepthBufferTest);

#if USING_XR_SDK_OCULUS && UNITY_2021_3_OR_NEWER && OCULUS_XR_DEPTH_SUBMISSION
        OculusSettings settings;
        if (UnityEditor.EditorBuildSettings.TryGetConfigObject<OculusSettings>("Unity.XR.Oculus.Settings",
                out settings))
        {
            bool eyebufferDepthSubmission = settings.DepthSubmission;
            if (tmpEnableDepthBufferTest && !eyebufferDepthSubmission)
            {
                EditorGUILayout.HelpBox(
                    "Enabling depth testing for this layer will result in additional GPU cost during composition if depth submission is not enabled in the Oculus XR Plugin settings. Consider disabling depth testing and using composition depth instead if only testing between layers and not eye textures.",
                    MessageType.Warning);
            }
        }
#endif

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(new GUIContent("Overlay Shape", "The shape of this overlay"),
            EditorStyles.boldLabel);
        // If the overlay shape has been set to a passthrough shape (via scripting), do not allow to change it.
        if (!OVROverlay.IsPassthroughShape(overlay.currentOverlayShape))
        {
            int currentShapeIndex = Array.IndexOf(selectableShapeValues, overlay.currentOverlayShape);
            if (currentShapeIndex == -1)
            {
                Debug.LogError("Invalid shape encountered");
                currentShapeIndex = 0;
            }

            bool modified = false;
            OVREditorUtil.SetupPopupField(target,
                new GUIContent("Overlay Shape", "The shape of this overlay"), ref currentShapeIndex,
                selectableShapeNames, ref modified);
            overlay.currentOverlayShape = selectableShapeValues[currentShapeIndex];
            if (modified && overlay.previewInEditor)
                overlay.ResetEditorPreview();
        }

        if (overlay.currentOverlayShape == OVROverlay.OverlayShape.Cubemap)
        {
            EditorGUILayout.PropertyField(_propUseLegacyCubemapRotation, new GUIContent("Use Legacy Cubemap Rotation",
                "Whether the cubemap should use the legacy rotation which was rotated 180 degrees around the Y axis comapred to Unity's definition of cubemaps. This setting will be deprecated in the near future, therefore it is recommended to fix the cubemap texture instead."));
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layer Properties", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(_propUseAutoFiltering, new GUIContent("Auto Filtering",
            "Whether this layer allows the runtime to automatically apply layer filter (Sharpen or Supersampling) to improve visual quality"));
        EditorGUILayout.PropertyField(_propUseBicubicFiltering, new GUIContent("Bicubic Filtering",
            "Whether this layer should use bicubic filtering. This can increase quality for small details on text and icons being viewed at farther distances."));

        bool hasModified = false;
        if (overlay.useEfficientSupersample)
            superSampleType = OVRPlugin.LayerSuperSamplingType.Normal;
        if (overlay.useExpensiveSuperSample)
            superSampleType = OVRPlugin.LayerSuperSamplingType.Quality;

        if (overlay.useEfficientSharpen)
            sharpenType = OverlayLayerSharpenType.Normal;
        if (overlay.useExpensiveSharpen)
            sharpenType = OverlayLayerSharpenType.Quality;

        OVREditorUtil.SetupEnumField(target, new GUIContent("Super Sample", "The super sample filter of this layer. This can help reduce flicker artifacts."), ref superSampleType, ref hasModified);
        OVREditorUtil.SetupEnumField(target, new GUIContent("Sharpen", "The sharpen filter of this layer. This amplifies contrast and fine details."), ref sharpenType, ref hasModified);
        if (hasModified)
        {
            overlay.useEfficientSupersample = superSampleType == OVRPlugin.LayerSuperSamplingType.Normal;
            overlay.useExpensiveSuperSample = superSampleType == OVRPlugin.LayerSuperSamplingType.Quality;

            overlay.useEfficientSharpen = sharpenType == OverlayLayerSharpenType.Normal;
            overlay.useExpensiveSharpen = sharpenType == OverlayLayerSharpenType.Quality;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);

#if UNITY_ANDROID
        bool lastIsExternalSurface = overlay.isExternalSurface;
        EditorGUILayout.PropertyField(_propIsExternalSurface,
            new GUIContent("Is External Surface",
                "On Android, retrieve an Android Surface object to render to (e.g., video playback)"));
        if (lastIsExternalSurface)
        {
            EditorGUILayout.PropertyField(_propExternalSurfaceWidth, new GUIContent("External Surface Width"));
            EditorGUILayout.PropertyField(_propExternalSurfaceHeight, new GUIContent("External Surface Height"));
            EditorGUILayout.PropertyField(_propIsProtectedContent,
                new GUIContent("Is Protected Content", "The external surface has L1 widevine protection."));
        }
        else
#endif
        {
            if (overlay.textures == null)
            {
                overlay.textures = new Texture[2];
            }

            if (overlay.textures.Length < 2)
            {
                Texture[] tmp = new Texture[2];
                for (int i = 0; i < overlay.textures.Length; i++)
                {
                    tmp[i] = overlay.textures[i];
                }

                overlay.textures = tmp;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(new GUIContent("Left Eye Texture", "Texture used for the left eye"),
                GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            var left = (Texture)EditorGUILayout.ObjectField(overlay.textures[0], typeof(Texture), true,
                GUILayout.Width(64), GUILayout.Height(64));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Changed Left Texture");
                overlay.textures[0] = left;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(new GUIContent("Right Eye Texture", "Texture used for the right eye"),
                GUILayout.Width(120));
            EditorGUI.BeginChangeCheck();
            var right = (Texture)EditorGUILayout.ObjectField(
                overlay.textures[1] != null ? overlay.textures[1] : overlay.textures[0], typeof(Texture), true,
                GUILayout.Width(64), GUILayout.Height(64));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Changed Right Texture");
            }

            EditorGUILayout.EndVertical();

            overlay.textures[1] = (right == overlay.textures[0]) ? null : right;

            if (overlay.textures[1] == null)
            {
                EditorGUILayout.LabelField("Right Eye Texture is null, so Left Eye Texture will be used for both eyes.",
                    EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(_propIsDynamic,
                new GUIContent("Dynamic Texture", "This texture will be updated dynamically at runtime (e.g., Video)"));
#if !UNITY_ANDROID
            EditorGUILayout.PropertyField(_propIsProtectedContent,
                new GUIContent("Is Protected Content", "The texture has copy protection, e.g., HDCP"));
#endif
        }

        if (overlay.currentOverlayShape == OVROverlay.OverlayShape.Cylinder ||
            overlay.currentOverlayShape == OVROverlay.OverlayShape.Equirect ||
            overlay.currentOverlayShape == OVROverlay.OverlayShape.Quad ||
            overlay.currentOverlayShape == OVROverlay.OverlayShape.Fisheye)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Texture Rects", EditorStyles.boldLabel);

            _propOverrideTextureRectMatrix.boolValue = !EditorGUILayout.Toggle(
                new GUIContent("Use Default Rects",
                    overlay.textures[1] == null
                        ? "If you need to use a single texture as a stereo image, uncheck this box"
                        : "Uncheck this box if you need to clip your textures or layer"),
                !overlay.overrideTextureRectMatrix);

            if (overlay.overrideTextureRectMatrix)
            {
                const float displaySize = 128;
                const float padding = 4;
                sourceRectsVisible = EditorGUILayout.Foldout(sourceRectsVisible,
                    new GUIContent("Source Rects",
                        "What portion of the source texture will ultimately be shown in each eye."));

                Color backgroundColor = new Color32(56, 56, 56, 255);
                Color dragLeftColor = Color.red;
                Color dragRightColor = Color.green;

                if (PlayerSettings.colorSpace == ColorSpace.Linear)
                {
                    backgroundColor = backgroundColor.gamma;
                }

                if (sourceRectsVisible)
                {
                    if (TryGetSrcRectMaterials(out var leftMat, out var rightMat))
                    {
                        Rect drawRect = EditorGUILayout.GetControlRect(GUILayout.Height(displaySize + padding * 2));
                        Vector4 srcLeft = new Vector4(Mathf.Max(0.0f, overlay.srcRectLeft.x),
                            Mathf.Max(0.0f, overlay.srcRectLeft.y),
                            Mathf.Min(1.0f - overlay.srcRectLeft.x, overlay.srcRectLeft.width),
                            Mathf.Min(1.0f - overlay.srcRectLeft.y, overlay.srcRectLeft.height));
                        Vector4 srcRight = new Vector4(Mathf.Max(0.0f, overlay.srcRectRight.x),
                            Mathf.Max(0.0f, overlay.srcRectRight.y),
                            Mathf.Min(1.0f - overlay.srcRectRight.x, overlay.srcRectRight.width),
                            Mathf.Min(1.0f - overlay.srcRectRight.y, overlay.srcRectRight.height));

                        if (overlay.invertTextureRects)
                        {
                            srcLeft.y = 1 - srcLeft.y - srcLeft.w;
                            srcRight.y = 1 - srcRight.y - srcRight.w;
                        }

                        float aspect = overlay.textures[0] != null
                            ? overlay.textures[0].width / overlay.textures[0].height
                            : 1;

                        float maxWidth = (drawRect.width - padding * 6) / 2;
                        float width = Mathf.Round(Mathf.Min(maxWidth, aspect * displaySize));
                        float height = Mathf.Round(Mathf.Min(displaySize, maxWidth / aspect));

                        // Populate our material properties
                        leftMat.SetVector("_PaddingAndSize", new Vector4(padding, padding, width, height));
                        leftMat.SetVector("_SrcRect", srcLeft);
                        leftMat.SetColor("_DragColor", dragLeftColor);
                        leftMat.SetColor("_BackgroundColor", backgroundColor);

                        rightMat.SetVector("_PaddingAndSize", new Vector4(padding, padding, width, height));
                        rightMat.SetVector("_SrcRect", srcRight);
                        rightMat.SetColor("_DragColor", dragRightColor);
                        rightMat.SetColor("_BackgroundColor", backgroundColor);

                        // center our draw rect
                        var drawRectLeft = new Rect(drawRect.center.x - width - padding * 3, drawRect.center.y - height / 2 - padding, width + padding * 2,
                            height + padding * 2);
                        var drawRectRight = new Rect(drawRect.center.x + padding, drawRect.center.y - height / 2 - padding, width + padding * 2,
                            height + padding * 2);
                        EditorGUI.DrawPreviewTexture(drawRectLeft, overlay.textures[0] ?? Texture2D.blackTexture,
                            leftMat);
                        EditorGUI.DrawPreviewTexture(drawRectRight, overlay.textures[1] ?? overlay.textures[0] ?? Texture2D.blackTexture,
                            rightMat);

                        var drawRectInsetLeft = new Rect(drawRectLeft.x + padding, drawRectLeft.y + padding, width, height);
                        var drawRectInsetRight = new Rect(drawRectRight.x + padding, drawRectRight.y + padding, width, height);

                        UpdateRectDragging(drawRectInsetLeft, drawRectInsetRight, TextureRect.SrcLeft, TextureRect.SrcRight,
                            overlay.invertTextureRects, ref overlay.srcRectLeft, ref overlay.srcRectRight);
                        CreateCursorRects(drawRectInsetLeft, overlay.srcRectLeft, overlay.invertTextureRects);
                        CreateCursorRects(drawRectInsetRight, overlay.srcRectRight, overlay.invertTextureRects);
                    }

                    var labelControlRect = EditorGUILayout.GetControlRect();
                    EditorGUI.LabelField(
                        new Rect(labelControlRect.x + padding, labelControlRect.y, labelControlRect.width / 2 - padding * 2,
                            labelControlRect.height),
                        new GUIContent("Left Source Rect",
                            "The rect in the source image that will be displayed on the left eye layer"));
                    EditorGUI.LabelField(
                        new Rect(labelControlRect.center.x + padding, labelControlRect.y,
                            labelControlRect.width / 2 - padding * 2, labelControlRect.height),
                        new GUIContent("Right Source Rect",
                            "The rect in the source image that will be displayed on the right eye layer"));

                    var rectControlRect = EditorGUILayout.GetControlRect(GUILayout.Height(34));

                    EditorGUI.BeginChangeCheck();
                    var srcRectLeft = Clamp01(EditorGUI.RectField(
                        new Rect(rectControlRect.x + padding, rectControlRect.y, rectControlRect.width / 2 - padding * 2,
                            rectControlRect.height), overlay.srcRectLeft));
                    var srcRectRight = Clamp01(EditorGUI.RectField(
                        new Rect(rectControlRect.center.x + padding, rectControlRect.y,
                            rectControlRect.width / 2 - padding * 2, rectControlRect.height), overlay.srcRectRight));

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Changed Source Rect");
                        overlay.srcRectLeft = srcRectLeft;
                        overlay.srcRectRight = srcRectRight;
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (overlay.textures[1] != null)
                    {
                        if (GUILayout.Button(new GUIContent("Reset To Default", "Reset Source Rects to default")))
                        {
                            SetRectsByVideoType(overlay, StereoType.Stereo, DisplayType.Custom);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button(new GUIContent("Monoscopic", "Display the full Texture in both eyes")))
                        {
                            SetRectsByVideoType(overlay, StereoType.Mono, DisplayType.Custom);
                        }

                        if (GUILayout.Button(new GUIContent("Stereo Left/Right",
                                "The left half of the texture is displayed in the left eye, and the right half in the right eye")))
                        {
                            SetRectsByVideoType(overlay, StereoType.StereoLeftRight, DisplayType.Custom);
                        }

                        if (GUILayout.Button(new GUIContent("Stereo Top/Bottom",
                                "The top half of the texture is displayed in the left eye, and the bottom half in the right eye")))
                        {
                            SetRectsByVideoType(overlay, StereoType.StereoTopBottom, DisplayType.Custom);
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }

                destRectsVisible = EditorGUILayout.Foldout(destRectsVisible,
                    new GUIContent("Destination Rects",
                        "What portion of the destination texture that the source will be rendered into."));
                if (destRectsVisible)
                {
                    if (TryGetDestRectMaterials(out var leftMat, out var rightMat))
                    {
                        Rect drawRect = EditorGUILayout.GetControlRect(GUILayout.Height(displaySize + padding * 2));

                        Vector4 srcLeft = new Vector4(Mathf.Max(0.0f, overlay.srcRectLeft.x),
                            Mathf.Max(0.0f, overlay.srcRectLeft.y),
                            Mathf.Min(1.0f - overlay.srcRectLeft.x, overlay.srcRectLeft.width),
                            Mathf.Min(1.0f - overlay.srcRectLeft.y, overlay.srcRectLeft.height));
                        Vector4 srcRight = new Vector4(Mathf.Max(0.0f, overlay.srcRectRight.x),
                            Mathf.Max(0.0f, overlay.srcRectRight.y),
                            Mathf.Min(1.0f - overlay.srcRectRight.x, overlay.srcRectRight.width),
                            Mathf.Min(1.0f - overlay.srcRectRight.y, overlay.srcRectRight.height));
                        Vector4 destLeft = new Vector4(Mathf.Max(0.0f, overlay.destRectLeft.x),
                            Mathf.Max(0.0f, overlay.destRectLeft.y),
                            Mathf.Min(1.0f - overlay.destRectLeft.x, overlay.destRectLeft.width),
                            Mathf.Min(1.0f - overlay.destRectLeft.y, overlay.destRectLeft.height));
                        Vector4 destRight = new Vector4(Mathf.Max(0.0f, overlay.destRectRight.x),
                            Mathf.Max(0.0f, overlay.destRectRight.y),
                            Mathf.Min(1.0f - overlay.destRectRight.x, overlay.destRectRight.width),
                            Mathf.Min(1.0f - overlay.destRectRight.y, overlay.destRectRight.height));

                        if (overlay.invertTextureRects)
                        {
                            srcLeft.y = 1 - srcLeft.y - srcLeft.w;
                            srcRight.y = 1 - srcRight.y - srcRight.w;
                            destLeft.y = 1 - destLeft.y - destLeft.w;
                            destRight.y = 1 - destRight.y - destRight.w;
                        }

                        float aspect = overlay.transform.lossyScale.x / overlay.transform.lossyScale.y;

                        float maxWidth = (drawRect.width - padding * 6) / 2;
                        float width = Mathf.Round(Mathf.Min(maxWidth, aspect * displaySize));
                        float height = Mathf.Round(Mathf.Min(displaySize, maxWidth / aspect));

                        // Populate our material properties
                        leftMat.SetVector("_PaddingAndSize", new Vector4(padding, padding, width, height));
                        leftMat.SetVector("_SrcRect", srcLeft);
                        leftMat.SetVector("_DestRect", destLeft);
                        leftMat.SetColor("_DragColor", dragLeftColor);
                        leftMat.SetColor("_BackgroundColor", backgroundColor);

                        rightMat.SetVector("_PaddingAndSize", new Vector4(padding, padding, width, height));
                        rightMat.SetVector("_SrcRect", srcRight);
                        rightMat.SetVector("_DestRect", destRight);
                        rightMat.SetColor("_DragColor", dragRightColor);
                        rightMat.SetColor("_BackgroundColor", backgroundColor);

                        var drawRectLeft = new Rect(drawRect.center.x - width - padding * 3, drawRect.center.y - height / 2 - padding, width + padding * 2,
                            height + padding * 2);
                        var drawRectRight = new Rect(drawRect.center.x + padding, drawRect.center.y - height / 2 - padding, width + padding * 2,
                            height + padding * 2);

                        // center our draw rect
                        EditorGUI.DrawPreviewTexture(drawRectLeft, overlay.textures[0] ?? Texture2D.blackTexture,
                            leftMat);
                        EditorGUI.DrawPreviewTexture(drawRectRight, overlay.textures[1] ?? overlay.textures[0] ?? Texture2D.blackTexture,
                            rightMat);

                        var drawRectInsetLeft = new Rect(drawRectLeft.x + padding, drawRectLeft.y + padding, width, height);
                        var drawRectInsetRight = new Rect(drawRectRight.x + padding, drawRectRight.y + padding, width, height);
                        UpdateRectDragging(drawRectInsetLeft, drawRectInsetRight, TextureRect.DestLeft,
                            TextureRect.DestRight, overlay.invertTextureRects, ref overlay.destRectLeft,
                            ref overlay.destRectRight);

                        CreateCursorRects(drawRectInsetLeft, overlay.destRectLeft, overlay.invertTextureRects);
                        CreateCursorRects(drawRectInsetRight, overlay.destRectRight, overlay.invertTextureRects);
                    }

                    var labelControlRect = EditorGUILayout.GetControlRect();
                    EditorGUI.LabelField(
                        new Rect(labelControlRect.x + padding, labelControlRect.y, labelControlRect.width / 2 - padding * 2,
                            labelControlRect.height),
                        new GUIContent("Left Destination Rect",
                            "The rect in the destination layer the left eye will display to"));
                    EditorGUI.LabelField(
                        new Rect(labelControlRect.center.x + padding, labelControlRect.y,
                            labelControlRect.width / 2 - padding, labelControlRect.height),
                        new GUIContent("Right Destination Rect",
                            "The rect in the destination layer the right eye will display to"));

                    var rectControlRect = EditorGUILayout.GetControlRect(GUILayout.Height(34));

                    EditorGUI.BeginChangeCheck();
                    var destRectLeft = Clamp01(EditorGUI.RectField(
                        new Rect(rectControlRect.x + padding, rectControlRect.y, rectControlRect.width / 2 - padding * 2,
                            rectControlRect.height), overlay.destRectLeft));
                    var destRectRight = Clamp01(EditorGUI.RectField(
                        new Rect(rectControlRect.center.x + padding, rectControlRect.y,
                            rectControlRect.width / 2 - padding * 2, rectControlRect.height), overlay.destRectRight));

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Changed Destination Rect");
                        overlay.destRectLeft = destRectLeft;
                        overlay.destRectRight = destRectRight;
                    }

                    if (overlay.currentOverlayShape == OVROverlay.OverlayShape.Equirect)
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button(new GUIContent("360 Video", "Display the full 360 layer")))
                        {
                            SetRectsByVideoType(overlay, StereoType.Custom, DisplayType.Full);
                        }

                        if (GUILayout.Button(new GUIContent("180 Video", "Display the front 180 layer")))
                        {
                            SetRectsByVideoType(overlay, StereoType.Custom, DisplayType.Half);
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        if (GUILayout.Button(new GUIContent("Reset To Default", "Reset Source Rects to default")))
                        {
                            SetRectsByVideoType(overlay, StereoType.Custom, DisplayType.Full);
                        }
                    }
                }

                EditorGUILayout.PropertyField(_propInvertTextureRects,
                    new GUIContent("Invert Rect Coordinates",
                        "Check this box to use the top left corner of the texture as the origin"));
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Color Scale", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_propOverridePerLayerColorScaleAndOffset,
            new GUIContent("Override Color Scale",
                "Manually set color scale and offset of this layer, regardless of what the global values are from OVRManager.SetColorScaleAndOffset()."));
        if (overlay.overridePerLayerColorScaleAndOffset)
        {
            EditorGUILayout.PropertyField(_propColorScale,
                new GUIContent("Color Scale", "Scale that the color values for this overlay will be multiplied by."));
            EditorGUILayout.PropertyField(_propColorOffset,
                new GUIContent("Color Offset", "Offset that the color values for this overlay will be added to."));
            serializedObject.ApplyModifiedProperties();
            overlay.SetPerLayerColorScaleAndOffset(_propColorScale.vector4Value, _propColorOffset.vector4Value);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_propPreviewInEditor,
            new GUIContent("Preview in Editor",
                "Creates a visual preview of the overlay for visualization purposes which will not be saved to the scene."));

        _propNoDepthBufferTesting.boolValue = !tmpEnableDepthBufferTest;
        overlay.previewInEditor = _propPreviewInEditor.boolValue;
        serializedObject.ApplyModifiedProperties();
    }

    private Rect Clamp01(Rect rect)
    {
        rect.x = Mathf.Clamp01(rect.x);
        rect.y = Mathf.Clamp01(rect.y);
        rect.width = Mathf.Clamp01(rect.width);
        rect.height = Mathf.Clamp01(rect.height);
        return rect;
    }

    private bool IsUnitRect(Rect rect)
    {
        return IsRect(rect, 0, 0, 1, 1);
    }

    private bool IsRect(Rect rect, float x, float y, float w, float h)
    {
        return rect.x == x && rect.y == y && rect.width == w && rect.height == h;
    }

    private StereoType GetStereoType(OVROverlay overlay)
    {
        if (overlay.textures[0] != null && overlay.textures[1] != null)
        {
            if (IsUnitRect(overlay.srcRectLeft) && IsUnitRect(overlay.srcRectRight))
            {
                return StereoType.Stereo;
            }
            else
            {
                return StereoType.Custom;
            }
        }
        else if (overlay.textures[0] != null)
        {
            if (IsUnitRect(overlay.srcRectLeft) && IsUnitRect(overlay.srcRectRight))
            {
                return StereoType.Mono;
            }
            else if (IsRect(overlay.srcRectLeft, 0, 0, 0.5f, 1f) && IsRect(overlay.srcRectRight, 0.5f, 0, 0.5f, 1f))
            {
                return StereoType.StereoLeftRight;
            }
            else if (overlay.invertTextureRects && IsRect(overlay.srcRectLeft, 0, 0.0f, 1f, 0.5f) &&
                     IsRect(overlay.srcRectRight, 0f, 0.5f, 1f, 0.5f))
            {
                return StereoType.StereoTopBottom;
            }
            else if (!overlay.invertTextureRects && IsRect(overlay.srcRectLeft, 0, 0.5f, 1f, 0.5f) &&
                     IsRect(overlay.srcRectRight, 0f, 0f, 1f, 0.5f))
            {
                return StereoType.StereoTopBottom;
            }
            else
            {
                return StereoType.Custom;
            }
        }
        else
        {
            return StereoType.Mono;
        }
    }

    private void SetRectsByVideoType(OVROverlay overlay, StereoType stereoType, DisplayType displayType)
    {
        Rect srcRectLeft, srcRectRight, destRectLeft, destRectRight;

        switch (displayType)
        {
            case DisplayType.Full:
                destRectLeft = destRectRight = new Rect(0, 0, 1, 1);
                break;

            case DisplayType.Half:
                destRectLeft = destRectRight = new Rect(0.25f, 0, 0.5f, 1);
                break;

            default:
                destRectLeft = overlay.destRectLeft;
                destRectRight = overlay.destRectRight;
                break;
        }

        switch (stereoType)
        {
            case StereoType.Mono:
            case StereoType.Stereo:
                srcRectLeft = srcRectRight = new Rect(0, 0, 1, 1);
                break;

            case StereoType.StereoTopBottom:
                if (overlay.invertTextureRects)
                {
                    srcRectLeft = new Rect(0, 0.0f, 1, 0.5f);
                    srcRectRight = new Rect(0, 0.5f, 1, 0.5f);
                }
                else
                {
                    srcRectLeft = new Rect(0, 0.5f, 1, 0.5f);
                    srcRectRight = new Rect(0, 0.0f, 1, 0.5f);
                }

                break;

            case StereoType.StereoLeftRight:
                srcRectLeft = new Rect(0, 0, 0.5f, 1);
                srcRectRight = new Rect(0.5f, 0, 0.5f, 1);
                break;

            default:
                srcRectLeft = overlay.srcRectLeft;
                srcRectRight = overlay.srcRectRight;
                break;
        }

        Undo.RecordObject(overlay, "Changed rect");
        overlay.SetSrcDestRects(srcRectLeft, srcRectRight, destRectLeft, destRectRight);
    }

    private void GetCursorPoints(Rect drawRect, Rect selectRect, bool invertY, out Vector2 leftPos,
        out Vector2 rightPos, out Vector2 topPos, out Vector2 bottomPos)
    {
        if (invertY)
        {
            selectRect.y = 1 - selectRect.y - selectRect.height;
        }

        leftPos = new Vector2(drawRect.x + selectRect.x * drawRect.width,
            drawRect.y + (1 - selectRect.y - selectRect.height / 2) * drawRect.height);
        rightPos = new Vector2(drawRect.x + (selectRect.x + selectRect.width) * drawRect.width,
            drawRect.y + (1 - selectRect.y - selectRect.height / 2) * drawRect.height);
        topPos = new Vector2(drawRect.x + (selectRect.x + selectRect.width / 2) * drawRect.width,
            drawRect.y + (1 - selectRect.y - selectRect.height) * drawRect.height);
        bottomPos = new Vector2(drawRect.x + (selectRect.x + selectRect.width / 2) * drawRect.width,
            drawRect.y + (1 - selectRect.y) * drawRect.height);

        if (invertY)
        {
            // swap top and bottom
            var tmp = topPos;
            topPos = bottomPos;
            bottomPos = tmp;
        }
    }

    private void CreateCursorRects(Rect drawRect, Rect selectRect, bool invertY)
    {
        Vector2 leftPos, rightPos, topPos, bottomPos;
        GetCursorPoints(drawRect, selectRect, invertY, out leftPos, out rightPos, out topPos, out bottomPos);

        EditorGUIUtility.AddCursorRect(new Rect(leftPos - 5 * Vector2.one, 10 * Vector2.one),
            MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(new Rect(rightPos - 5 * Vector2.one, 10 * Vector2.one),
            MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(new Rect(topPos - 5 * Vector2.one, 10 * Vector2.one),
            MouseCursor.ResizeVertical);
        EditorGUIUtility.AddCursorRect(new Rect(bottomPos - 5 * Vector2.one, 10 * Vector2.one),
            MouseCursor.ResizeVertical);
    }

    private bool IsOverRectControls(Rect drawRect, Vector2 mousePos, Rect selectRect, bool invertY, ref Side side)
    {
        Vector2 leftPos, rightPos, topPos, bottomPos;
        GetCursorPoints(drawRect, selectRect, invertY, out leftPos, out rightPos, out topPos, out bottomPos);

        if ((leftPos - mousePos).sqrMagnitude <= 25)
        {
            side = Side.Left;
            return true;
        }

        if ((rightPos - mousePos).sqrMagnitude <= 25)
        {
            side = Side.Right;
            return true;
        }

        if ((topPos - mousePos).sqrMagnitude <= 25)
        {
            side = Side.Top;
            return true;
        }

        if ((bottomPos - mousePos).sqrMagnitude <= 25)
        {
            side = Side.Bottom;
            return true;
        }

        return false;
    }

    private void UpdateRectDragging(Rect drawingRectLeft, Rect drawingRectRight, TextureRect rectLeftType,
        TextureRect rectRightType, bool invertY, ref Rect rectLeft, ref Rect rectRight)
    {
        if (!Event.current.isMouse || Event.current.button != 0)
        {
            return;
        }

        if (Event.current.type == EventType.MouseUp)
        {
            _DraggingRect = TextureRect.None;
            return;
        }

        Vector2 mousePos = Event.current.mousePosition;
        if (_DraggingRect == TextureRect.None && Event.current.type == EventType.MouseDown)
        {
            if (IsOverRectControls(drawingRectLeft, mousePos, rectLeft, invertY, ref _DraggingSide))
            {
                _DraggingRect = rectLeftType;
            }

            if (_DraggingRect == TextureRect.None || Event.current.shift)
            {
                if (IsOverRectControls(drawingRectRight, mousePos, rectRight, invertY, ref _DraggingSide))
                {
                    _DraggingRect = rectRightType;
                }
            }
        }

        if (_DraggingRect == rectLeftType)
        {
            SetRectSideValue(drawingRectLeft, mousePos, _DraggingSide, invertY, ref rectLeft);
        }

        if (_DraggingRect == rectRightType)
        {
            SetRectSideValue(drawingRectRight, mousePos, _DraggingSide, invertY, ref rectRight);
        }
    }

    private void SetRectSideValue(Rect drawingRect, Vector2 mousePos, Side side, bool invertY, ref Rect rect)
    {
        // quantize to 1/32
        float x = Mathf.Clamp01(Mathf.Round(((mousePos.x - drawingRect.x) / drawingRect.width) * 32) / 32.0f);
        float y = Mathf.Clamp01(Mathf.Round(((mousePos.y - drawingRect.y) / drawingRect.height) * 32) / 32.0f);
        if (!invertY)
        {
            y = 1 - y;
        }

        switch (side)
        {
            case Side.Left:
                float xMax = rect.xMax;
                rect.x = Mathf.Min(x, xMax);
                rect.width = xMax - rect.x;
                break;
            case Side.Right:
                rect.width = Mathf.Max(0, x - rect.x);
                break;
            case Side.Bottom:
                float yMax = rect.yMax;
                rect.y = Mathf.Min(y, yMax);
                rect.height = yMax - rect.y;
                break;
            case Side.Top:
                rect.height = Mathf.Max(0, y - rect.y);
                break;
        }
    }



    private bool TryGetSrcRectShader(out Shader srcRectShader)
    {
        srcRectShader = _SrcRectShader;
        if (_DidLookupSrcRectShader)
        {
            return srcRectShader != null;
        }

        _DidLookupSrcRectShader = true;
        string[] shaders = AssetDatabase.FindAssets("OVROverlaySrcRectEditor");

        if (shaders.Length > 0)
        {
            Shader shader = (Shader)AssetDatabase.LoadAssetAtPath(
                AssetDatabase.GUIDToAssetPath(shaders[0]), typeof(Shader));

            if (shader != null)
            {
                _SrcRectShader = shader;
                return true;
            }
        }

        return false;
    }

    private bool TryGetDestRectShader(out Shader destRectShader)
    {
        destRectShader = _DestRectShader;
        if (_DidLookupDestRectShader)
        {
            return destRectShader != null;
        }

        _DidLookupDestRectShader = true;
        string[] shaders = AssetDatabase.FindAssets("OVROverlayDestRectEditor");

        if (shaders.Length > 0)
        {
            Shader shader = (Shader)AssetDatabase.LoadAssetAtPath(
                AssetDatabase.GUIDToAssetPath(shaders[0]), typeof(Shader));

            if (shader != null)
            {
                _DestRectShader = shader;
                return true;
            }
        }

        return false;
    }


    private bool TryGetSrcRectMaterials(out Material srcRectMaterialLeft, out Material srcRectMaterialRight)
    {
        srcRectMaterialLeft = _SrcRectMaterialLeft;
        srcRectMaterialRight = _SrcRectMaterialRight;

        if (_SrcRectMaterialLeft != null && _SrcRectMaterialRight != null)
        {
            return true;
        }

        if (!TryGetSrcRectShader(out var shader))
        {
            return false;
        }

        srcRectMaterialLeft = _SrcRectMaterialLeft = new Material(shader);
        srcRectMaterialRight = _SrcRectMaterialRight = new Material(shader);
        return true;
    }

    private bool TryGetDestRectMaterials(out Material destRectMaterialLeft, out Material destRectMaterialRight)
    {
        destRectMaterialLeft = _DestRectMaterialLeft;
        destRectMaterialRight = _DestRectMaterialRight;

        if (_DestRectMaterialLeft != null && _DestRectMaterialRight != null)
        {
            return true;
        }

        if (!TryGetDestRectShader(out var shader))
        {
            return false;
        }

        destRectMaterialLeft = _DestRectMaterialLeft = new Material(shader);
        destRectMaterialRight = _DestRectMaterialRight = new Material(shader);
        return true;
    }
}
