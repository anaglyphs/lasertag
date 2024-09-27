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

namespace Meta.XR.Guides.Editor.Items
{
    internal class LinkLabel : IGuideItem
    {
        public bool Hide { get; set; }
        public GUIContent Label { get; }
        private readonly Action _linkClickCallback;
        private readonly GUILayoutOption[] _options;
        private GUIStyle _guiStyle;

        public LinkLabel(string label, Action linkClickCallback, params GUILayoutOption[] options) :
            this(label, GuideStyles.GUIStyles.LinkLabelStyle, linkClickCallback, options)
        {
        }

        public LinkLabel(string label, GUIStyle style, Action linkClickCallback, params GUILayoutOption[] options)
        {
            Label = new GUIContent(label);
            _linkClickCallback = linkClickCallback;
            _options = options;
            _guiStyle = new GUIStyle(style);
            _guiStyle.padding.top = 0;
        }

        public void Draw()
        {
            Rect position = GUILayoutUtility.GetRect(Label, _guiStyle, _options);

            Handles.color = EditorStyles.linkLabel.normal.textColor;
            Handles.DrawLine(new Vector3(position.xMin + (float)EditorStyles.linkLabel.padding.left, position.yMax), new Vector3(position.xMax - (float)EditorStyles.linkLabel.padding.right, position.yMax));
            Handles.color = Color.white;

            if (GUI.Button(position, Label, _guiStyle))
            {
                _linkClickCallback?.Invoke();
            }
            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
        }
    }
}
