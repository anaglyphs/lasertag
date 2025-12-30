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
using System.Linq;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;

public static class OVROverlayEditorHelper
{
    public const string DefaultCanvasRenderLayerName = "OVROverlayCanvas Rendering";
    public static int CanvasRenderLayer
    {
        get
        {
            var settings = OVROverlayCanvasSettings.Instance;
            if (settings.CanvasRenderLayer != -1 && !string.IsNullOrEmpty(LayerMask.LayerToName(settings.CanvasRenderLayer)))
                return settings.CanvasRenderLayer;

            if (LayerMask.NameToLayer(DefaultCanvasRenderLayerName) is var layer and not -1)
                settings.CanvasRenderLayer = layer;

            return settings.CanvasRenderLayer;
        }
    }

    public const string DefaultCanvasLayerName = "Overlay UI";
    public static int CanvasLayer
    {
        get
        {
            var settings = OVROverlayCanvasSettings.Instance;
            if (settings.CanvasLayer != -1 && !string.IsNullOrEmpty(LayerMask.LayerToName(settings.CanvasLayer)))
                return settings.CanvasLayer;

            if (LayerMask.NameToLayer(DefaultCanvasLayerName) is var layer and not -1)
                settings.CanvasLayer = layer;

            return settings.CanvasLayer;
        }
    }
    public static bool CanvasLayerSelected => CanvasLayer != -1 && !string.IsNullOrEmpty(LayerMask.LayerToName(CanvasLayer));

    public static void SetLayerName(int layer, string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/TagManager.asset"));
        var it = tagManager.GetIterator();
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

    public static int DirtyLayerField(UnityEngine.Object dirtyObject, int currentLayer, string label, string tooltip)
    {
        var newLayer = EditorGUILayout.LayerField(new GUIContent(label, tooltip), currentLayer);
        if (currentLayer != newLayer)
        {
            EditorUtility.SetDirty(dirtyObject);
        }
        return newLayer;
    }

    public enum DisplayMessageType
    {
        Notice,
        Warning,
        Error,
        Check
    }

    public static void DisplayMessage(DisplayMessageType messageType, string messageText, string headerOverride = null)
    {
        var (iconUri, header) = messageType switch
        {
            DisplayMessageType.Check => ("TestPassed", ""),
            DisplayMessageType.Error => ("console.erroricon.sml", "Error"),
            DisplayMessageType.Warning => ("console.warnicon.sml", "Warning"),
            _ => ("console.infoicon.sml", "Notice"),
        };
        header = headerOverride ?? header;

        EditorGUILayout.BeginHorizontal(Styles.GUIStyles.DialogBox); //2-column wrapper
        EditorGUILayout.BeginVertical(GUILayout.Width(20)); //column 1: icon
        EditorGUILayout.LabelField(EditorGUIUtility.IconContent(iconUri), GUILayout.Width(20));
        EditorGUILayout.EndVertical(); //end column 1: icon
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true)); //column 2: label, message
        if (!string.IsNullOrEmpty(header))
        {
            GUILayout.Label(header, EditorStyles.boldLabel);
        }
        GUILayout.Label(messageText, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndVertical(); //end column 2: label, message, objects
        EditorGUILayout.EndHorizontal(); //end 2-column wrapper
    }

    public static void CanvasLayerSelectionUI(int canvasLayer, Action<int> setCanvasLayer, Action<int> setMask)
    {
        if (CanvasLayer != -1)
        {
            var layerName = LayerMask.LayerToName(CanvasLayer);
            if (canvasLayer != CanvasLayer && GUILayout.Button($"Set Layer to \"{layerName}\""))
            {
                setCanvasLayer(CanvasLayer);
                setMask(~LayerMask.GetMask(layerName));
                OVRPlugin.SendEvent("canvas_set_layer_clicked");
            }
        }
        else if (FindUnusedLayer() is not { } unusedLayer)
        {
            Debug.LogError("All Layers are Used!");
        }
        else if (GUILayout.Button($"Set Layer {unusedLayer} to \"{DefaultCanvasLayerName}\""))
        {
            SetLayerName(unusedLayer, DefaultCanvasLayerName);
            setCanvasLayer(unusedLayer);
            setMask(~(1 << unusedLayer));
            OVRPlugin.SendEvent("canvas_set_default_layer_clicked");
        }
    }

    public static int? FindUnusedLayer(bool lowest = true)
    {
        var layers = Enumerable.Range(0, 32);
        if (!lowest)
            layers = layers.Reverse();

        foreach (var i in layers)
        {
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
            {
                return i;
            }
        }
        return null;
    }
}
