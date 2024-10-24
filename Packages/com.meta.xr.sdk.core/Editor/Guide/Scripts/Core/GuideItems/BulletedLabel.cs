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
    /// Make a read-only label with bullet point.
    /// </summary>
    /// <remarks>
    /// Use <see cref="GuideStyles.ContentStatusType"/> to set color of the bullet based on status type.
    /// </remarks>
    internal class BulletedLabel : IGuideItem
    {
        public bool Hide { get; set; }
        private Label _labelItem;
        private Color _color;

        public BulletedLabel(string label, GuideStyles.ContentStatusType contentStatusType = GuideStyles.ContentStatusType.Normal, params GUILayoutOption[] options) :
            this(label, GuideStyles.GUIStyles.Label, contentStatusType, options)
        {
        }

        public BulletedLabel(string label, GUIStyle style, GuideStyles.ContentStatusType contentStatusType = GuideStyles.ContentStatusType.Normal, params GUILayoutOption[] options)
        {
            Style = new GUIStyle(style);
            _labelItem = new Label(label, Style, options);
            SetStatus(contentStatusType);
        }

        public void Draw()
        {
            EditorGUILayout.BeginHorizontal();

            using (new XR.Editor.UserInterface.Utils.ColorScope(XR.Editor.UserInterface.Utils.ColorScope.Scope.Content, _color))
            {
                EditorGUILayout.LabelField(
                    GuideStyles.Contents.DefaultIcon,
                    new GUIStyle(GuideStyles.GUIStyles.IconStyle), GUILayout.Width(SmallIconSize),
                    GUILayout.Height(SmallIconSize));
            }

            _labelItem.Draw();

            EditorGUILayout.EndHorizontal();
        }

        public void SetStatus(GuideStyles.ContentStatusType statusType) => _color = Utils.GetColorByStatus(statusType);
        public GUIStyle Style { get; }
        public float GetHeight(float contentWidth = GuideStyles.Constants.DefaultWidth - LargeMargin) => Style.CalcHeight(_labelItem.LabelContent, contentWidth);
        public float GetWidth() => _labelItem.GetWidth() + SmallIconSize;
    }
}
