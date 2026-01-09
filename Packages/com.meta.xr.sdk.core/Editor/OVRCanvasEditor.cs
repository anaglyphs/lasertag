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

using System.Linq;
using UnityEditor;
using UnityEngine;
using Meta.XR.Editor.UserInterface;
using static OVROverlayEditorHelper;

#if UNITY_TEXTMESHPRO
[CustomEditor(typeof(TMPro.TextMeshPro))]
[CanEditMultipleObjects]
public class OVRTextEditor : TMPro.EditorUtilities.TMP_EditorPanel
{
    public override void OnInspectorGUI()
    {
        var upgradeableCanvases = targets.OfType<TMPro.TextMeshPro>().ToArray();
        OVRCanvasEditor.UpgradeDialog("text", upgradeableCanvases, c =>
        {
            OVRCanvasEditor.SetTextDefaults(c);
            Undo.AddComponent<OVROverlayCanvas_TMPChanged>(c.gameObject).TargetCanvas = c;
        }, null, nameof(TMPro.TextMeshPro));
        base.OnInspectorGUI();
    }
}
#endif

[CustomEditor(typeof(Canvas))]
[CanEditMultipleObjects]
public class OVRCanvasEditor : Editor
{
    private int _presetSelection = 0;
    private GUIStyle _presetAreaStyle;

    void OnEnable()
    {
        _presetAreaStyle = new GUIStyle()
        {
            normal =
            {
                background = Styles.Colors.DarkGray.ToTexture(),
            }
        };
    }

    public override void OnInspectorGUI()
    {
        var upgradeableCanvases = targets.OfType<Canvas>().
            Where(c => GetRenderMode(c) == RenderMode.WorldSpace).
            ToArray();
        UpgradeDialog("canvas", upgradeableCanvases, c =>
        {
            if (_presetSelection == 1)
            {
                SetTextDefaults(c);
            }
        }, () =>
        {
            using var verticalScope = new EditorGUILayout.VerticalScope(_presetAreaStyle, GUILayout.Width(120));
            GUILayout.Label("Preset", Styles.GUIStyles.BoldLabel);
            _presetSelection = GUILayout.SelectionGrid(_presetSelection, new[] { " Animated UI", " Static Text" }, 1, EditorStyles.radioButton);
        },
        $"{nameof(Canvas)}/{(_presetSelection == 0 ? "UI" : "Text")}");
        base.OnInspectorGUI();
    }

    private static RenderMode GetRenderMode(Canvas canvas)
    {
        // canvas.renderMode returns ScreenSpaceOverlay when editing as a prefab,
        // even when it's actually set to WorldSpace.
        var serializedObject = new SerializedObject(canvas);
        serializedObject.Update();
        return (RenderMode)serializedObject.FindProperty("m_RenderMode").intValue;
    }

    internal static void UpgradeDialog(string noun, Component[] components, System.Action<OVROverlayCanvas> onUpgrade, System.Action onPresetArea, string telemetryParam)
    {
        if (components.Length == 0)
            return;

        if (!components.All(c => c.GetComponent<OVROverlayCanvas>() != null))
        {
            using (var verticalScope = new EditorGUILayout.VerticalScope(Styles.GUIStyles.DialogBox))
            {
                using (var horizontalScope = new EditorGUILayout.HorizontalScope())
                {
                    using (var disabledScope = new EditorGUI.DisabledGroupScope(!CanvasLayerSelected))
                    {
                        if (GUILayout.Button(
                            new GUIContent($" Upgrade {(components.Length == 1 ? "" : "all ")}to OVROverlayCanvas", Styles.Contents.MetaWhiteIcon.Image, ""),
                            GUILayout.MaxHeight(40),
                            GUILayout.ExpandWidth(true),
                            GUILayout.ExpandHeight(true)))
                        {
                            foreach (var canvas in components)
                            {
                                if (canvas.GetComponent<OVROverlayCanvas>() != null)
                                    continue;
                                var overlay = Undo.AddComponent<OVROverlayCanvas>(canvas.gameObject);
                                overlay.SetCanvasLayer(CanvasLayer, false);
                                onUpgrade?.Invoke(overlay);
                                EditorUtility.SetDirty(overlay);
                                Debug.Log($"Added {nameof(OVROverlayCanvas)} to {canvas.gameObject}", overlay);
                                OVRPlugin.SendEvent("canvas_upgrade_clicked", telemetryParam);
                            }
                        }

                        onPresetArea?.Invoke();
                    }

                    if (GUILayout.Button(Styles.Contents.InfoIcon, Styles.GUIStyles.MiniButton))
                    {
                        Application.OpenURL("https://developers.meta.com/horizon/documentation/unity/unity-ovroverlay/");
                    }
                }

                GUILayout.Label("Using OVROverlayCanvas will improve the visual clarity of this UI.", Styles.GUIStyles.DialogTextStyle);
                GUILayout.Label("It will also improve the readability of any text.", Styles.GUIStyles.DialogTextStyle);
                GUILayout.FlexibleSpace();

                CanvasLayerSelectionUI(CanvasLayer, _ => { }, _ => { });
            }
        }
        else
        {
            DisplayMessage(DisplayMessageType.Check, $"This {noun} is rendered using OVROverlayCanvas.");
        }
    }

    internal static void SetTextDefaults(OVROverlayCanvas c)
    {
        c.overlayType = OVROverlay.OverlayType.Overlay;
        c.opacity = OVROverlayCanvas.DrawMode.OpaqueWithClip;
        c.manualRedraw = true;
        c._dynamicResolution = false;
        c._enableMipmapping = true;
    }
}
