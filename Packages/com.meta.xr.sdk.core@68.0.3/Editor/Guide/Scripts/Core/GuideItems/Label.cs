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
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Guides.Editor.Items
{
    /// <summary>
    /// Make a read-only label.
    /// </summary>
    internal class Label : IGuideItem
    {
        public bool Hide { get; set; }
        public GUIContent LabelContent { get; }
        private GUIStyle _guiStyle;
        private readonly GUILayoutOption[] _options;

        public Label(string label, params GUILayoutOption[] options) : this(label, GuideStyles.GUIStyles.Label, options)
        {
        }

        public Label(string label, GUIStyle style, params GUILayoutOption[] options)
        {
            LabelContent = new GUIContent(label);
            _guiStyle = new GUIStyle(style);
            _options = options;
        }

        public void Draw()
        {
            EditorGUILayout.LabelField(LabelContent.text, _guiStyle, _options);
        }

        public float GetHeight(float contentWidth = GuideStyles.Constants.DefaultWidth - LargeMargin) => _guiStyle.CalcHeight(LabelContent, contentWidth);
        public float GetWidth() => _guiStyle.CalcSize(LabelContent).x;
    }
}
