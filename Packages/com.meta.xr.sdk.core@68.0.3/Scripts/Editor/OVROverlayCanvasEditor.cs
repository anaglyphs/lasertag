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
using UnityEngine.Rendering;

[CustomEditor(typeof(OVROverlayCanvas))]
public class OVROverlayCanvasEditor : Editor
{
    private static string kOverrideUiShaderName = "UI/Default Correct";

    private static string kBuiltInOpaqueShaderName = "UI/Prerendered Opaque";
    private static string kUrpOpaqueShaderName = "URP/UI/Prerendered Opaque";
    private static string kBuiltInTransparentShaderName = "UI/Prerendered";
    private static string kUrpTransparentShaderName = "URP/UI/Prerendered";


    GUIStyle mWarningBoxStyle;

    private bool mShowControlObjects;

    [System.Flags]
    private enum SceneViewVisibility
    {
        Canvas = 1,
        Imposter = 2,
        Both = 3
    }

    private static bool UseUrp()
    {
        return GraphicsSettings.currentRenderPipeline != default;
    }

    void OnEnable()
    {
        var warningBoxStyleTex = new Texture2D(1, 1);
        warningBoxStyleTex.SetPixel(0, 0, new Color(0.4f, 0.4f, 0.4f, 0.2f));
        warningBoxStyleTex.Apply();
        mWarningBoxStyle = new GUIStyle();
        mWarningBoxStyle.normal.background = warningBoxStyleTex;
        mWarningBoxStyle.padding = new RectOffset(8, 8, 2, 2);
        mWarningBoxStyle.margin = new RectOffset(4, 4, 4, 4);
    }

    public override void OnInspectorGUI()
    {
        var settingsObject = new SerializedObject(OVROverlayCanvasSettings.Instance);
        var prop = settingsObject.FindProperty("_overrideCanvasShader");
        if (prop.objectReferenceValue == null)
        {
            prop.objectReferenceValue = Shader.Find(kOverrideUiShaderName);
        }

        prop = settingsObject.FindProperty("_opaqueImposterShader");
        string opaqueShaderName = UseUrp() ? kUrpOpaqueShaderName : kBuiltInOpaqueShaderName;
        if (!(prop.objectReferenceValue is Shader os) || os == null || os.name != opaqueShaderName)
        {
            prop.objectReferenceValue = Shader.Find(opaqueShaderName);
        }
        string transparentShaderName = UseUrp() ? kUrpTransparentShaderName : kBuiltInTransparentShaderName;
        prop = settingsObject.FindProperty("_transparentImposterShader");
        if (!(prop.objectReferenceValue is Shader ts) || ts == null || ts.name != transparentShaderName)
        {
            prop.objectReferenceValue = Shader.Find(transparentShaderName);
        }

        OVROverlayCanvas canvas = target as OVROverlayCanvas;
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Layer Configuration", EditorStyles.boldLabel);

        bool layerError = false;
        var newLayer = EditorGUILayout.LayerField(new GUIContent("Canvas Layer", "The layer this canvas is drawn on"), canvas.gameObject.layer);

        if (newLayer != canvas.gameObject.layer)
        {
            SetLayerRecursive(canvas.gameObject, newLayer, canvas.gameObject.layer, false);
        }

        if (!HasConsistentLayersRecursive(canvas.gameObject, canvas.gameObject.layer))
        {
            DisplayMessage(MessageType.Warning, "Canvas Elements have inconsistent layers!");
            if (GUILayout.Button("Fix Canvas Layers"))
            {
                SetLayerRecursive(canvas.gameObject, canvas.gameObject.layer, canvas.gameObject.layer, true);
            }
        }

        if (string.IsNullOrEmpty(LayerMask.LayerToName(canvas.gameObject.layer)))
        {
            DisplayMessage(MessageType.Notice, "The current Canvas Layer is unnamed.");
            if (GUILayout.Button("Set Canvas Layer Name"))
            {
                SetLayerName(canvas.gameObject.layer, "Overlay UI");
            }
        }

        canvas.layer =
            EditorGUILayout.LayerField(new GUIContent("Overlay Layer", "The layer this overlay should be drawn on"),
                canvas.layer);

        var mainCamera = OVRManager.FindMainCamera();
        if (canvas.layer == canvas.gameObject.layer)
        {
            DisplayMessage(MessageType.Error,
                $"This GameObject's Layer is the same as Overlay Layer ('{LayerMask.LayerToName(canvas.layer)}'). "
                + "To control camera visibility, this GameObject should have a Layer that is not the Overlay Layer.");
            layerError = true;
        }
        else if (mainCamera != null)
        {
            if ((mainCamera.cullingMask & (1 << canvas.gameObject.layer)) != 0)
            {
                DisplayMessage(MessageType.Warning,
                    $"Main Camera '{mainCamera.name}' does not cull this GameObject's Layer '{LayerMask.LayerToName(canvas.gameObject.layer)}'. "
                    + "This Canvas might be rendered by both the Main Camera and the OVROverlay system.");
                layerError = true;
                if (GUILayout.Button($"Remove {LayerMask.LayerToName(canvas.gameObject.layer)} from Camera cullingMask"))
                {
                    mainCamera.cullingMask &= ~(1 << canvas.gameObject.layer);
                }
            }

            if ((mainCamera.cullingMask & (1 << canvas.layer)) == 0)
            {
                DisplayMessage(MessageType.Error,
                    $"Overlay Layer '{LayerMask.LayerToName(canvas.layer)}' is culled by your main camera. "
                    + "The Overlay Layer is expected to render in the scene, so it shouldn't be culled.");
                layerError = true;
                if (GUILayout.Button($"Add {LayerMask.LayerToName(canvas.layer)} to Camera cullingMask"))
                {
                    mainCamera.cullingMask |= 1 << canvas.layer;
                }
            }
        }
        else
        {
            DisplayMessage(MessageType.Warning,
                "No Main Camera found. Make sure your camera does not draw this GameObject's Layer ("
                + LayerMask.LayerToName(canvas.gameObject.layer) + "), or this canvas might be rendered twice.");
        }

        if (layerError)
        {
            int overlayUILayer = LayerMask.NameToLayer("Overlay UI");
            if (overlayUILayer != -1)
            {
                if (canvas.gameObject.layer != overlayUILayer && GUILayout.Button("Set Layer to Overlay UI"))
                {
                    canvas.gameObject.layer = overlayUILayer;
                    canvas.layer = LayerMask.NameToLayer("UI");
                    if (mainCamera != null)
                    {
                        mainCamera.cullingMask &= ~LayerMask.GetMask("Overlay UI");
                    }
                }
            }
            else if (GUILayout.Button("Create Overlay Canvas Layer"))
            {
                int unusedLayer = -1;
                for (int i = 0; i < 32; i++)
                {
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    {
                        unusedLayer = i;
                        break;
                    }
                }

                if (unusedLayer == -1)
                {
                    Debug.LogError("All Layers are Used!");
                }
                else
                {
                    SetLayerName(unusedLayer, "Overlay UI");
                    canvas.layer = LayerMask.NameToLayer("UI");
                    canvas.gameObject.layer = unusedLayer;
                    if (mainCamera != null)
                    {
                        mainCamera.cullingMask &= ~(1 << unusedLayer);
                    }
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Overlay Configuration", EditorStyles.boldLabel);

        canvas.opacity = (OVROverlayCanvas.DrawMode)EditorGUILayout.EnumPopup(
            new GUIContent("Opacity", "Treat this canvas as opaque, which is a big performance improvement"),
            canvas.opacity);

        if (canvas.opacity == OVROverlayCanvas.DrawMode.TransparentDefaultAlpha)
        {
            DisplayMessage(MessageType.Notice,
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
                    DisplayMessage(MessageType.Warning,
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
                DisplayMessage(MessageType.Notice,
                    "Some graphics in this canvas are using a custom UI material.\n" +
                    "Make sure that the Alpha blend mode for these shaders is set to 'One OneMinusSrcAlpha'");
            }
        }

        if (canvas.opacity == OVROverlayCanvas.DrawMode.TransparentCorrectAlpha ||
            canvas.opacity == OVROverlayCanvas.DrawMode.TransparentDefaultAlpha)
        {
            if (PlayerSettings.colorSpace == ColorSpace.Gamma)
            {
                DisplayMessage(MessageType.Warning,
                    "This project's ColorSpace is set to Gamma. Oculus recommends using Linear ColorSpace. Alpha blending will not be correct in Gamma ColorSpace.");
            }
        }

        canvas.shape = (OVROverlayCanvas.CanvasShape)
            EditorGUILayout.EnumPopup(
                new GUIContent("Canvas Shape", "The maximum width and height for this canvas texture."),
                canvas.shape);

        if (canvas.shape == OVROverlayCanvas.CanvasShape.Curved)
        {
            canvas.curveRadius = EditorGUILayout.FloatField(new GUIContent("Curve Radius",
                "The radius of the curve on which this canvas will be displayed."), canvas.curveRadius);

            var rt = canvas.GetComponent<RectTransform>();
            float scaleX = rt.rect.width * rt.lossyScale.x;
            float minRadius = scaleX / (180 * Mathf.Deg2Rad);

            canvas.curveRadius = Mathf.Max(minRadius, canvas.curveRadius);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rendering Options", EditorStyles.boldLabel);

        canvas.maxTextureSize =
            EditorGUILayout.IntField(
                new GUIContent("Max Texture Size", "The maximum width and height for this canvas texture."),
                canvas.maxTextureSize);

        canvas.expensive =
            EditorGUILayout.Toggle(
                new GUIContent("Expensive", "Improve the visual appearance at the cost of additional GPU time"),
                canvas.expensive);

        canvas.overlapMask =
            EditorGUILayout.Toggle(
                new GUIContent("Overlap Mask", "Reduce edge artifacts between foreground and layer by shrinking the mask to overlap the canvas. Requires additional GPU time."),
                canvas.overlapMask);

        canvas.renderInterval = EditorGUILayout.IntField(
            new GUIContent("Render Interval",
                "How often we should re-render this canvas to a texture. The canvas' transform can be changed every frame, regardless of Draw Rate. A value of 1 means every frame, 2 means every other, etc."),
            canvas.renderInterval);
        if (canvas.renderInterval > 1)
        {
            canvas.renderIntervalFrameOffset =
                EditorGUILayout.IntField(
                    new GUIContent("Render Interval Frame Offset",
                        "Allows you to alternate which frame each canvas will draw by specifying a frame offset."),
                    canvas.renderIntervalFrameOffset);
        }

        canvas.overlayEnabled = EditorGUILayout.Toggle("Overlay Enabled", canvas.overlayEnabled);

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
            }
            else
            {
                child.hideFlags |= HideFlags.HideInHierarchy;
            }
        }


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

    void SetLayerRecursive(GameObject gameObject, int layer, int previousLayer, bool forceUpdate)
    {
        if (gameObject.layer == previousLayer || forceUpdate)
        {
            gameObject.layer = layer;
        }

        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            var c = gameObject.transform.GetChild(i).gameObject;
            if ((c.hideFlags &= HideFlags.DontSave) != 0)
            {
                continue;
            }
            SetLayerRecursive(c, layer, previousLayer, forceUpdate);
        }
    }

    void SetLayerName(int layer, string name)
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));

        SerializedProperty it = tagManager.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.name == "layers")
            {
                it.GetArrayElementAtIndex(layer).stringValue = name;
                break;
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    private enum MessageType
    {
        Notice,
        Warning,
        Error
    }

    void DisplayMessage(MessageType messageType, string messageText)
    {
        string iconUri = "";
        string header = "";
        switch (messageType)
        {
            case MessageType.Error:
                iconUri = "console.erroricon.sml";
                header = "Error";
                break;
            case MessageType.Warning:
                iconUri = "console.warnicon.sml";
                header = "Warning";
                break;
            case MessageType.Notice:
            default:
                iconUri = "console.infoicon.sml";
                header = "Notice";
                break;
        }

        EditorGUILayout.BeginHorizontal(mWarningBoxStyle); //2-column wrapper
        EditorGUILayout.BeginVertical(GUILayout.Width(20)); //column 1: icon
        EditorGUILayout.LabelField(EditorGUIUtility.IconContent(iconUri), GUILayout.Width(20));
        EditorGUILayout.EndVertical(); //end column 1: icon
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true)); //column 2: label, message
        GUILayout.Label(header, EditorStyles.boldLabel);
        GUILayout.Label(messageText, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical(); //end column 2: label, message, objects
        EditorGUILayout.EndHorizontal(); //end 2-column wrapper
    }
}
