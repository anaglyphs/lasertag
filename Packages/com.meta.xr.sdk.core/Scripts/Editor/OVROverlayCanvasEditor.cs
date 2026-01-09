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

using System.Reflection;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using static OVROverlayEditorHelper;

[CustomEditor(typeof(OVROverlayCanvas))]
[CanEditMultipleObjects]
public class OVROverlayCanvasEditor : Editor
{
    private bool mShowControlObjects;

    [System.Flags]
    private enum SceneViewVisibility
    {
        Canvas = 1,
        Imposter = 2,
        Both = 3
    }

    private void LayerConfigurationGUI(IEnumerable<OVROverlayCanvas> canvases)
    {
        EditorGUILayout.LabelField("Layer Configuration", EditorStyles.boldLabel);

        var settings = OVROverlayCanvasSettings.Instance;

        if (targets.Length > 1)
        {
            using var disabled = new EditorGUI.DisabledGroupScope(true);

            foreach (var canvasLayer in canvases.Select(c => c.gameObject.layer).Distinct())
            {
                _ = EditorGUILayout.LayerField(new GUIContent("Canvas Layer", "The layer this canvas is drawn on"), canvasLayer);
            }

            foreach (var overlayLayer in canvases.Select(c => c.layer).Distinct())
            {
                _ = EditorGUILayout.LayerField(new GUIContent("Overlay Layer", "The layer this overlay should be drawn on"), overlayLayer);
            }

            CanvasRenderLayerGUI();

            return;
        }

        var canvas = target as OVROverlayCanvas;

        bool layerError = false;
        var newLayer = EditorGUILayout.LayerField(new GUIContent("Canvas Layer", "The layer this canvas is drawn on"), canvas.gameObject.layer);

        if (newLayer != canvas.gameObject.layer)
        {
            canvas.SetCanvasLayer(newLayer, false);
        }

        if (!HasConsistentLayersRecursive(canvas.gameObject, canvas.gameObject.layer))
        {
            DisplayMessage(DisplayMessageType.Warning, "Canvas Elements have inconsistent layers!");
            if (GUILayout.Button("Fix Canvas Layers"))
            {
                canvas.SetCanvasLayer(canvas.gameObject.layer, true);
                OVRPlugin.SendEvent("canvas_fix_canvas_layers_clicked");
            }
        }

        if (string.IsNullOrEmpty(LayerMask.LayerToName(canvas.gameObject.layer)))
        {
            DisplayMessage(DisplayMessageType.Notice, "The current Canvas Layer is unnamed.");
            if (GUILayout.Button("Set Canvas Layer Name"))
            {
                SetLayerName(canvas.gameObject.layer, DefaultCanvasLayerName);
                OVRPlugin.SendEvent("canvas_set_canvas_layer_name_clicked");
            }
        }

        canvas.layer = DirtyLayerField(canvas, canvas.layer, "Overlay Layer", "The layer this overlay should draw");

        CanvasRenderLayerGUI();

        var mainCamera = OVRManager.FindMainCamera();
        if (canvas.layer == canvas.gameObject.layer)
        {
            DisplayMessage(DisplayMessageType.Error,
                $"This GameObject's Layer is the same as Overlay Layer ('{LayerMask.LayerToName(canvas.layer)}'). "
                + "To control camera visibility, this GameObject should have a Layer that is not the Overlay Layer.");
            layerError = true;
        }
        else if (mainCamera != null)
        {
            if ((mainCamera.cullingMask & (1 << canvas.gameObject.layer)) != 0)
            {
                DisplayMessage(DisplayMessageType.Warning,
                    $"Main Camera '{mainCamera.name}' does not cull this GameObject's Layer '{LayerMask.LayerToName(canvas.gameObject.layer)}'. "
                    + "This Canvas might be rendered by both the Main Camera and the OVROverlay system.");
                layerError = true;
                if (GUILayout.Button($"Remove {LayerMask.LayerToName(canvas.gameObject.layer)} from Camera cullingMask"))
                {
                    mainCamera.cullingMask &= ~(1 << canvas.gameObject.layer);
                    OVRPlugin.SendEvent("canvas_remove_from_culling_mask_clicked");
                }
            }

            if ((mainCamera.cullingMask & (1 << canvas.layer)) == 0)
            {
                DisplayMessage(DisplayMessageType.Error,
                    $"Overlay Layer '{LayerMask.LayerToName(canvas.layer)}' is culled by your main camera. "
                    + "The Overlay Layer is expected to render in the scene, so it shouldn't be culled.");
                layerError = true;
                if (GUILayout.Button($"Add {LayerMask.LayerToName(canvas.layer)} to Camera cullingMask"))
                {
                    mainCamera.cullingMask |= 1 << canvas.layer;
                    OVRPlugin.SendEvent("canvas_add_to_culling_mask_clicked");
                }
            }
        }
        else
        {
            DisplayMessage(DisplayMessageType.Warning,
                "No Main Camera found. Make sure your camera does not draw this GameObject's Layer ("
                + LayerMask.LayerToName(canvas.gameObject.layer) + "), or this canvas might be rendered twice.");
        }

        if (layerError)
        {
            CanvasLayerSelectionUI(
                canvas.gameObject.layer,
                layer => canvas.gameObject.layer = layer, mask =>
            {
                canvas.layer = LayerMask.NameToLayer("UI");
                if (mainCamera != null)
                {
                    mainCamera.cullingMask &= mask;
                }
            });
        }
    }

    public static void CanvasRenderLayerGUI()
    {
        var settings = OVROverlayCanvasSettings.Instance;
        settings.CanvasRenderLayer = DirtyLayerField(settings, settings.CanvasRenderLayer, "Global Hidden Render Layer", "The layer reserved for rendering overlays");

        if (LayerMask.LayerToName(CanvasRenderLayer) != DefaultCanvasRenderLayerName)
        {
            if (GUILayout.Button("Create new Global Overlay Render Layer"))
            {
                if (FindUnusedLayer(false) is { } newLayer)
                {
                    settings.CanvasRenderLayer = newLayer;
                    SetLayerName(settings.CanvasRenderLayer, DefaultCanvasRenderLayerName);
                }
                OVRPlugin.SendEvent("canvas_create_render_layer_clicked");
            }
        }
    }

#if UNITY_TEXTMESHPRO
    private bool _textRenderingTriggersToggled;

    private static IEnumerable<(OVROverlayCanvas, TMPro.TMP_Text)> PotentialTextUpgrades(IEnumerable<OVROverlayCanvas> canvases)
    {
        foreach (var canvas in canvases)
        {
            if (canvas.manualRedraw)
            {
                foreach (var text in canvas.GetComponentsInChildren<TMPro.TMP_Text>())
                {
                    if (text.GetComponent<OVROverlayCanvas_TMPChanged>() == null)
                    {
                        yield return (canvas, text);
                    }
                }
            }
        }
    }
#endif

    public override void OnInspectorGUI()
    {
        var settingsObject = new SerializedObject(OVROverlayCanvasSettings.Instance);
        var canvases = targets.Cast<OVROverlayCanvas>();

        foreach (var canvas in canvases)
        {
            if (canvas.rectTransform == null)
            {
                canvas.rectTransform = canvas.GetComponent<RectTransform>();
            }
        }

        if (targets.Length == 1)
        {
            var canvas = (OVROverlayCanvas)target;
            DisplayMessage(canvas.manualRedraw ? DisplayMessageType.Notice : DisplayMessageType.Warning,
                    canvas.manualRedraw ? "This canvas will only re-render when triggered." :
                    canvas.renderInterval <= 1 ? "This canvas will render every frame." :
                    $"This canvas will render once every {canvas.renderInterval} frames.",
                    "");
            EditorGUILayout.Space();
        }

#if UNITY_TEXTMESHPRO
        if (PotentialTextUpgrades(canvases).Any())
        {
            _textRenderingTriggersToggled = EditorGUILayout.BeginFoldoutHeaderGroup(_textRenderingTriggersToggled, "Set up text rendering triggers");
            if (_textRenderingTriggersToggled)
            {
                foreach (var (canvas, text) in PotentialTextUpgrades(canvases))
                {
                    if (GUILayout.Button($"Set up rendering triggers for {text}"))
                    {
                        Undo.AddComponent<OVROverlayCanvas_TMPChanged>(text.gameObject).TargetCanvas = canvas;
                        OVRPlugin.SendEvent("canvas_add_tmpchanged_clicked");
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
#endif

        if (Application.isPlaying)
        {
            var canvas = (OVROverlayCanvas)target;
            EditorGUILayout.LabelField("View Priority Score", canvas.GetViewPriorityScore()?.ToString() ?? "N/A");
            if (canvas.Overlay != null)
            {
                EditorGUILayout.LabelField("Layer Index", canvas.Overlay.layerIndex.ToString());
                EditorGUILayout.LabelField("Layer Id", canvas.Overlay.layerId.ToString());
            }

            EditorGUILayout.Space();
        }

        EditorGUI.BeginChangeCheck();

        LayerConfigurationGUI(canvases);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Overlay Configuration", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(OVROverlayCanvas.overlayType)));
        if (serializedObject.ApplyModifiedProperties())
        {
            foreach (var canvas in canvases)
            {
                canvas.UpdateOverlaySettings();

#if UNITY_2020_1_OR_NEWER
#pragma warning disable 0618
                // Transparent Correct Alpha is no longer necessary.
                if (canvas.opacity == OVROverlayCanvas.DrawMode.TransparentCorrectAlpha)
                {
                    canvas.opacity = OVROverlayCanvas.DrawMode.Transparent;
                }
#pragma warning restore 0618
#endif
            }
        }



        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(nameof(OVROverlayCanvas.opacity)),
            new GUIContent("Opacity", "Treat this canvas as opaque, which is a big performance improvement"));
        serializedObject.ApplyModifiedProperties();

#if !UNITY_2020_1_OR_NEWER
        if (canvases.All(c => c.overlayType is OVROverlay.OverlayType.Underlay))
        {
            if (targets.Length == 1)
            {
                var canvas = canvases.First();
                if (canvas.opacity == OVROverlayCanvas.DrawMode.TransparentDefaultAlpha)
                {
                    DisplayMessage(DisplayMessageType.Notice,
                        "Transparent Default Alpha is not recommended with overlapping semitransparent graphics.");
                }

                if (canvas.opacity == OVROverlayCanvas.DrawMode.TransparentCorrectAlpha)
                {
                    var graphics = canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>();
                    bool usingDefaultMaterial = false;
                    bool usingCustomMaterial = false;
                    foreach (var graphic in graphics)
                    {
                        if (graphic.material == null || graphic.material == graphic.defaultMaterial)
                        {
                            usingDefaultMaterial = true;
                        }
                        else if (graphic.material.shader.name != "UI/Default Correct")
                        {
                            usingCustomMaterial = true;
                        }

                        if (usingDefaultMaterial && usingCustomMaterial)
                        {
                            break;
                        }
                    }

                    if (usingDefaultMaterial)
                    {
                        var overrideDefaultCanvasProp = settingsObject.FindProperty("_overrideDefaultCanvasMaterial");
                        overrideDefaultCanvasProp.boolValue = EditorGUILayout.Toggle(new GUIContent("Override Default Canvas Material",
                                "Globally overrides the default canvas material with a version that correctly populates the alpha channel " +
                                "for overlay usage. In most cases this will not cause any noticeable changes to other Canvases, " +
                                "unless the previous alpha behavior was being relied on."),
                            overrideDefaultCanvasProp.boolValue);

                        if (!overrideDefaultCanvasProp.boolValue)
                        {
                            DisplayMessage(DisplayMessageType.Warning,
                                "Some graphics in this canvas are using the default UI material.\nWould you like to replace all of them with the corrected UI Material?");

                            if (GUILayout.Button("Replace Materials"))
                            {
                                var matList = AssetDatabase.FindAssets("t:Material UI Default Correct");
                                if (matList.Length > 0)
                                {
                                    var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matList[0]));

                                    foreach (var graphic in graphics)
                                    {
                                        if (graphic.material == null || graphic.material == graphic.defaultMaterial)
                                        {
                                            graphic.material = mat;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (usingCustomMaterial)
                    {
                        DisplayMessage(DisplayMessageType.Notice,
                            "Some graphics in this canvas are using a custom UI material.\n" +
                            "Make sure that the Alpha blend mode for these shaders is set to 'One OneMinusSrcAlpha'");
                    }
                }

                if (canvas.opacity == OVROverlayCanvas.DrawMode.TransparentCorrectAlpha ||
                    canvas.opacity == OVROverlayCanvas.DrawMode.TransparentDefaultAlpha)
                {
                    if (PlayerSettings.colorSpace == ColorSpace.Gamma)
                    {
                        DisplayMessage(DisplayMessageType.Warning,
                            "This project's ColorSpace is set to Gamma. Oculus recommends using Linear ColorSpace. Alpha blending will not be correct in Gamma ColorSpace.");
                    }
                }
            }
        }
#else
        if (canvases.Any(c => c.opacity == OVROverlayCanvas.DrawMode.Transparent))
        {
            if (PlayerSettings.colorSpace == ColorSpace.Gamma)
            {
                DisplayMessage(DisplayMessageType.Warning,
                    "This project's ColorSpace is set to Gamma. Oculus recommends using Linear ColorSpace. Alpha blending will not be correct in Gamma ColorSpace.");
            }
        }
#endif

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(nameof(OVROverlayCanvas.shape)),
            new GUIContent("Canvas Shape", "The maximum width and height for this canvas texture."));
        serializedObject.ApplyModifiedProperties();

        var shape = canvases.First().shape;
        if (shape == OVROverlayCanvas.CanvasShape.Curved && canvases.All(c => c.shape == shape))
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas.curveRadius)),
                new GUIContent("Curve Radius", "The radius of the curve on which this canvas will be displayed."));
            serializedObject.ApplyModifiedProperties();

            foreach (var canvas in canvases)
            {
                var rt = canvas.rectTransform;
                float scaleX = rt.rect.width * rt.lossyScale.x;
                float minRadius = scaleX / (180 * Mathf.Deg2Rad);

                canvas.curveRadius = Mathf.Max(minRadius, canvas.curveRadius);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rendering Options", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas.maxTextureSize)),
                new GUIContent("Max Texture Size", "The maximum width and height for this canvas texture."));

        EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas.expensive)),
                new GUIContent("Expensive", "Improve the visual appearance at the cost of additional GPU time"));

        EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas.overlapMask)),
                new GUIContent("Overlap Mask", "Reduce edge artifacts between foreground and layer by shrinking the mask to overlap the canvas. Requires additional GPU time."));

        {
            using var horizontalScope = new EditorGUILayout.HorizontalScope();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas._enableMipmapping)),
                new GUIContent("Generate Mipmaps", "When enabled, the texture will generate mipmaps after rendering."));
            if (canvases.Any(c => !c.manualRedraw && c._enableMipmapping))
            {
                DisplayMessage(DisplayMessageType.Warning, "Mipmapping increases the cost of re-rendering. Manual redraw is recommended.", "");
            }
            else if (canvases.Any(c => c.manualRedraw && !c._enableMipmapping))
            {
                DisplayMessage(DisplayMessageType.Notice, "When manual redraw is enabled, mipmapping is recommended to improve quality.", "");
            }
        }

        {
            using var horizontalScope = new EditorGUILayout.HorizontalScope();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas._dynamicResolution)),
                new GUIContent("Dynamic Resolution", "When enabled, the canvas's texture resolution will be dependent on its size in screen space."));
            if (canvases.Any(c => c.manualRedraw && c._dynamicResolution && c._redrawResolutionThreshold == int.MaxValue))
            {
                DisplayMessage(DisplayMessageType.Warning, "Enable Dynamic Resolution Redraw or disable Manual Redraw, or the canvas will not automatically redraw at higher resolutions. ", "");
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Redraw Options", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty(nameof(OVROverlayCanvas.manualRedraw)),
            new GUIContent("Manual Redraw", "When enabled, the canvas will only automatically render once."));

        using (var disabledScope = new EditorGUI.DisabledGroupScope(!canvases.Any(c => !c.manualRedraw)))
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(OVROverlayCanvas.renderInterval)),
                new GUIContent("Render Interval",
                    "How often we should re-render this canvas to a texture. The canvas' transform can be changed every frame, regardless of Draw Rate. A value of 1 means every frame, 2 means every other, etc."));

            if (targets.Length == 1)
            {
                var canvas = canvases.First();
                if (canvas.renderInterval > 1)
                {
                    canvas.renderIntervalFrameOffset =
                        EditorGUILayout.IntField(
                            new GUIContent("Render Interval Frame Offset",
                                "Allows you to alternate which frame each canvas will draw by specifying a frame offset."),
                            canvas.renderIntervalFrameOffset);
                }
            }
        }

        using (var disabledScope = new EditorGUI.DisabledGroupScope(!canvases.Any(c => c._dynamicResolution && c.manualRedraw)))
        {
            var prop = serializedObject.FindProperty(nameof(OVROverlayCanvas._redrawResolutionThreshold));
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;

            using var changeScope = new EditorGUI.ChangeCheckScope();
            var redrawEnabled = EditorGUILayout.Toggle(new GUIContent("Dynamic Resolution Redraw", "Should dynamic resolution trigger redraw?"), prop.intValue != int.MaxValue);
            using (var disabledScope2 = new EditorGUI.DisabledGroupScope(!redrawEnabled))
            {
                if (redrawEnabled)
                {
                    if (changeScope.changed && (prop.intValue == int.MaxValue || prop.hasMultipleDifferentValues))
                    {
                        prop.intValue = 32;
                    }
                    EditorGUILayout.PropertyField(prop, new GUIContent("Redraw Resolution Threshold", "How many pixels does the dynamic resolution have to change by to trigger a redraw?"));
                }
                else
                {
                    prop.intValue = int.MaxValue;
                    EditorGUILayout.TextField(new GUIContent("Redraw Resolution Threshold", "How many pixels does the dynamic resolution have to change by to trigger a redraw?"), "");
                }
            }
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(OVROverlayCanvas._overlayEnabled)));
        serializedObject.ApplyModifiedProperties();

        bool updatedMaterials = false;
        if (settingsObject.ApplyModifiedProperties())
        {
            OVROverlayCanvasSettings.CommitOverlayCanvasSettings(OVROverlayCanvasSettings.Instance);
            updatedMaterials = true;
        }

        if (EditorGUI.EndChangeCheck() || updatedMaterials)
        {
            EditorUtility.SetDirty(target);
            // Call the initialize render texture method to update the content
            typeof(OVROverlayCanvas)
                .GetMethod("InitializeRenderTexture", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Editor Debug", EditorStyles.boldLabel);
        mShowControlObjects = EditorGUILayout.Toggle("Show Hidden Objects", mShowControlObjects);

        foreach (var canvas in canvases)
        {
            for (int i = 0; i < canvas.transform.childCount; i++)
            {
                var child = canvas.transform.GetChild(i);
                if ((child.hideFlags & HideFlags.DontSave) == 0)
                {
                    continue;
                }

                if (mShowControlObjects)
                {
                    child.hideFlags &= ~HideFlags.HideInHierarchy;
                    if (GUILayout.Button($"Select {child.name}"))
                        Selection.activeObject = child;
                }
                else
                {
                    child.hideFlags |= HideFlags.HideInHierarchy;
                }
            }
        }

        var firstCanvas = canvases.First();
        if (canvases.All(c => c.layer == firstCanvas.layer && c.gameObject.layer == firstCanvas.gameObject.layer))
        {
            var canvas = firstCanvas;
            SceneViewVisibility currentVisibility = default;
            if ((Tools.visibleLayers & (1 << canvas.gameObject.layer)) != 0)
            {
                currentVisibility |= SceneViewVisibility.Canvas;
            }
            if ((Tools.visibleLayers & (1 << canvas.layer)) != 0)
            {
                currentVisibility |= SceneViewVisibility.Imposter;
            }

            SceneViewVisibility newVisibility =
                (SceneViewVisibility)EditorGUILayout.EnumPopup(new GUIContent("Scene View Display"), currentVisibility);

            if ((newVisibility & SceneViewVisibility.Canvas) == 0)
            {
                Tools.visibleLayers &= ~(1 << canvas.gameObject.layer);
            }
            else
            {
                Tools.visibleLayers |= (1 << canvas.gameObject.layer);
            }

            if ((newVisibility & SceneViewVisibility.Imposter) == 0)
            {
                Tools.visibleLayers &= ~(1 << canvas.layer);
            }
            else
            {
                Tools.visibleLayers |= (1 << canvas.layer);
            }

            if (currentVisibility != newVisibility)
            {
                EditorWindow.GetWindow<SceneView>()?.Repaint();
            }
        }

        foreach (var canvas in canvases)
        {
            using var disabled = new EditorGUI.DisabledScope(true);
            var texture = canvas.Overlay?.textures?[0];
            if (texture != null)
            {
                using var vertical = new EditorGUILayout.VerticalScope();
                _ = EditorGUILayout.ObjectField(
                    $"Overlay Texture{(targets.Length == 1 ? "" : $": {canvas.name}")}",
                    texture, typeof(Texture2D), true, GUILayout.MinHeight(70));
            }
        }
    }

    bool HasConsistentLayersRecursive(GameObject gameObject, int layer)
    {
        if (gameObject.layer != layer)
        {
            return false;
        }

        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            var c = gameObject.transform.GetChild(i).gameObject;
            if ((c.hideFlags &= HideFlags.DontSave) != 0)
            {
                continue;
            }

            if (!HasConsistentLayersRecursive(c, layer))
            {
                return false;
            }
        }

        return true;
    }
}
