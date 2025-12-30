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
using System.Linq;
using System.Collections;
using System.Collections.Generic;

[CustomEditor(typeof(OVROverlayCanvasManager))]
public class OVROverlayCanvasManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var red = new GUIStyle(GUI.skin.label);
        red.normal.textColor = Color.red;
        var green = new GUIStyle(GUI.skin.label);
        green.normal.textColor = Color.green;

        foreach (var canvas in OVROverlayCanvasManager.Instance?.Canvases)
        {
            using var horizontalScope = new EditorGUILayout.HorizontalScope();
            using var disabledScope = new EditorGUI.DisabledScope(true);

            _ = EditorGUILayout.ObjectField(canvas, typeof(OVROverlayCanvas), true);
            EditorGUILayout.LabelField(canvas.GetViewPriorityScore()?.ToString() ?? "N/A", canvas.IsCanvasPriority ? green : red);
        }
    }
}
