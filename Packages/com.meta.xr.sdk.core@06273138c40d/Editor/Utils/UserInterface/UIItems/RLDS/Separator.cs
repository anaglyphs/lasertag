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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal class Separator : IUserInterfaceItem
    {
        private readonly bool _shadowStyle;

        public Separator(bool shadowStyle = true)
        {
            _shadowStyle = shadowStyle;
        }

        public void Draw()
        {
            var style = UserInterface.Styles.GUIStyles.SeparatorAreaStyle;
            var shadowColor = new Color(1, 1, 1, 0.1f);
            if (!_shadowStyle)
            {
                style.normal.background = shadowColor.ToTexture();
            }

            var rect = EditorGUILayout.BeginVertical(style);
            {
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndVertical();

            if (!_shadowStyle)
            {
                return;
            }

            var dropShadow = shadowColor.ToTexture();
            rect.y += 1.5f;
            GUI.DrawTexture(rect, dropShadow, ScaleMode.StretchToFill, true);
        }

        public bool Hide { get; set; }
    }
}
