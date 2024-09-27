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
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.Utils;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.Editor
{
    internal static class DebugMemberInspectorExtensions
    {
        // Draw tweak
        public static void DrawTweak(this InspectedMember member)
        {
            var attribute = member.attribute;
            if (TweakUtils.IsMemberTypeValidForTweak(member.MemberInfo))
            {
                attribute.Tweakable = EditorGUILayout.Toggle(new GUIContent("Tweakable", "Specify whether this field/property is tweakable, will show control UI in panel."),
                    attribute.Tweakable);

                if (attribute.Tweakable && !member.MemberInfo.IsTypeEqual(typeof(bool)))
                {
                    attribute.Min = EditorGUILayout.FloatField("Min", attribute.Min);
                    attribute.Max = EditorGUILayout.FloatField("Max", attribute.Max);
                }
            }
        }

        public static int DrawGizmo(this InspectedMember member, int selectedGizmoIndex)
        {
            var supportedGizmos = member.SupportedGizmos;
            if (supportedGizmos.Count == 1) return selectedGizmoIndex;

            var options = supportedGizmos.Select(g => g.ToString()).ToArray();
            selectedGizmoIndex = EditorGUILayout.Popup(
                new GUIContent("Gizmo Type", "Draw gizmo in space according to the runtime value of the field/property data"),
                selectedGizmoIndex, options);
            member.attribute.GizmoType = (DebugGizmoType)Enum.Parse(typeof(DebugGizmoType), options[selectedGizmoIndex]);
            return selectedGizmoIndex;
        }
    }
}
