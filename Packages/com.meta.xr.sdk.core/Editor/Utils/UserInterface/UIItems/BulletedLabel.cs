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

using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Make a read-only label with bullet point.
    /// </summary>
    /// <remarks>
    /// Use <see cref="GuideStyles.ContentStatusType"/> to set color of the bullet based on status type.
    /// </remarks>
    internal class BulletedLabel : IUserInterfaceItem
    {
        private VisualElement _visualElement;
        private readonly string _typography;
        private VisualElement _bullet;

        public bool Hide { get; set; }
        public GUIStyle HorizontalStyle { get; set; } = GUIStyle.none;
        public Label LabelItem { get; }
        public Color Color { get; set; }

        public BulletedLabel(string label,
            UIStyles.ContentStatusType contentStatusType = UIStyles.ContentStatusType.Normal,
            params GUILayoutOption[] options) :
            this(label, UIStyles.GUIStyles.Label, contentStatusType, options)
        {
        }

        public BulletedLabel(string label, GUIStyle style,
            UIStyles.ContentStatusType contentStatusType = UIStyles.ContentStatusType.Normal,
            params GUILayoutOption[] options)
        {
            Style = new GUIStyle(style);
            LabelItem = new Label(label, Style, options);
            SetStatus(contentStatusType);
        }

        /// <summary>
        /// Constructor to use in UIToolkit based environment
        /// </summary>
        /// <param name="label">Label to show</param>
        /// <param name="typography"><see cref="Props.Typography"/> for the typographic variants</param>
        /// <param name="contentStatusType">Status type to show</param>
        public BulletedLabel(string label, string typography,
            UIStyles.ContentStatusType contentStatusType = UIStyles.ContentStatusType.Normal)
        {
            LabelItem = new Label(label);
            _typography = typography;
            SetStatus(contentStatusType);
        }

        public void Draw()
        {
            EditorGUILayout.BeginHorizontal(HorizontalStyle);

            using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, Color))
            {
                EditorGUILayout.LabelField(
                    UIStyles.Contents.DefaultIcon,
                    new GUIStyle(UIStyles.GUIStyles.IconStyle), GUILayout.Width(SmallIconSize),
                    GUILayout.Height(SmallIconSize));
            }

            LabelItem.Draw();

            EditorGUILayout.EndHorizontal();
        }

        public void SetStatus(UIStyles.ContentStatusType statusType) => Color = Utils.GetColorByStatus(statusType);
        public void SetLabel(string text) => LabelItem.LabelContent.text = text;
        public GUIStyle Style { get; }

        public float GetHeight(float contentWidth = UIStyles.Constants.DefaultWidth - LargeMargin) =>
            Style.CalcHeight(LabelItem.LabelContent, contentWidth);

        public float GetWidth() => LabelItem.GetWidth() + SmallIconSize;

        /// <summary>
        /// Creates a UIToolkit VisualElement with RLDS styling applied.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing a bullet icon and label text</returns>
        public VisualElement Get()
        {
            _visualElement = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.FlexStart,
                    alignSelf = Align.FlexStart,
                    marginTop = RLDS.Styles.Spacing.Space4XS,
                    marginBottom = RLDS.Styles.Spacing.Space4XS
                }
            };

            var bulletSize = RLDS.Styles.IconSize.Size2XS;
            _bullet = new VisualElement
            {
                style =
                {
                    width = bulletSize,
                    height = bulletSize,
                    backgroundColor = new StyleColor(Color),
                    borderBottomLeftRadius = bulletSize / 2,
                    borderBottomRightRadius = bulletSize / 2,
                    borderTopLeftRadius = bulletSize / 2,
                    borderTopRightRadius = bulletSize / 2,
                    marginRight = RLDS.Styles.Spacing.SpaceXS,
                    marginTop = RLDS.Styles.Spacing.Space3XS

                }
            };
            _visualElement.Add(_bullet);

            var label = new UnityEngine.UIElements.Label(LabelItem.LabelContent.text)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal
                }
            };

            label.AddToClassList(_typography);
            _visualElement.Add(label);

            return _visualElement;
        }
    }
}
