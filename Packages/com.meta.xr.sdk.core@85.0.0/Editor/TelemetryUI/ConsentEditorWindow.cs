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
using Meta.XR.Editor.Id;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Guides.Editor;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Editor.TelemetryUI
{
    internal class ConsentEditorWindow : EditorWindow
    {
        private Vector2 _size = new(600f, 320f);
        private GroupedItem _groupedUIItems;
        private string _consentMarkdownText;

        private GUIStyle _contentContainer;
        private GUIStyle _buttonsContainer;
        private GUIStyle _headerContainer;
        private GUIStyle _titleLabel;

        private ActionLinkDescription _shareAdditional;
        private ActionLinkDescription _onlyEssential;

        public static void ShowWindow()
        {
            var consentTitle = OVRPlugin.UnifiedConsent.GetConsentTitle();
            if (string.IsNullOrEmpty(consentTitle))
            {
                return;
            }

            var consentText = OVRPlugin.UnifiedConsent.GetConsentMarkdownText();
            if (string.IsNullOrEmpty(consentText))
            {
                return;
            }

            var window = CreateInstance<ConsentEditorWindow>();
            window.titleContent = new GUIContent(consentTitle);
            window._consentMarkdownText = consentText;
            window.ShowUtility();

            // Ensure the window appears front and center
            window.CenterWindow();
        }

        private void OnEnable()
        {
            _contentContainer = new()
            {
                padding = new RectOffset(LargeMargin, LargeMargin, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            _buttonsContainer = new()
            {
                margin = new RectOffset(LargeMargin, LargeMargin, DoubleMargin, LargeMargin),
            };

            _headerContainer = new()
            {
                padding = new RectOffset(DoubleMargin + Margin, Margin, DoubleMargin, DoubleMargin),
                margin = new RectOffset(0, 0, 0, DoubleMargin),
                normal = { background = Colors.CharcoalGraySemiTransparent.ToTexture() },
                fixedHeight = Meta.XR.Editor.UserInterface.UIStyles.Constants.DefaultHeaderHeight
            };

            _titleLabel = new()
            {
                fontStyle = FontStyle.Bold,
                stretchHeight = true,
                fontSize = 12,
                fixedHeight = 52,
                normal =
                {
                    textColor = Color.white
                },
                hover =
                {
                    textColor = Color.white
                },
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                wordWrap = true
            };

            Notifications.Notification.Manager.RequestSnooze(this);
        }

        private void OnDisable()
        {
            Notifications.Notification.Manager.ReleaseSnooze(this);
        }

        private void OnGUI()
        {
            // Overall vertical wrapper
            var rect = EditorGUILayout.BeginVertical(GUIStyles.NoMargin);

            // Header
            DrawHeaderTitle();

            // Content
            DrawContent();

            // Buttons
            DrawButtons();

            EditorGUILayout.EndVertical();
            UpdateHeight(rect);
        }

        private void RecordConsent(bool consent)
        {
            OVRTelemetryConsent.SetTelemetryEnabled(consent);
            Close();
        }

        private void UpdateHeight(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            _size.y = rect.height;
            minSize = _size;
            maxSize = _size;
        }

        private void DrawHeaderTitle()
        {
            using var indentScope = new UserInterface.Utils.IndentScope(0);
            EditorGUILayout.BeginVertical(_headerContainer);
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal(UIStyles.GUIStyles.HeaderLargeHorizontal, GUILayout.ExpandHeight(false));
            EditorGUILayout.LabelField(GuideStyles.Contents.HeaderIcon, UIStyles.GUIStyles.HeaderIconStyleLarge,
                GUILayout.Width(UIStyles.GUIStyles.HeaderIconStyleLarge.fixedWidth), GUILayout.ExpandWidth(false));
            EditorGUILayout.LabelField(titleContent, _titleLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawContent()
        {
            if (_groupedUIItems == null)
            {
                var contentItems = MarkdownUtils.GetGuideItemsForMarkdownText(_consentMarkdownText).ToList();
                if (contentItems is not { Count: > 0 })
                {
                    return;
                }

                _groupedUIItems = new GroupedItem(contentItems, UserInterface.Utils.UIItemPlacementType.Vertical);
            }

            EditorGUILayout.BeginVertical(_contentContainer);
            _groupedUIItems.Draw();
            EditorGUILayout.EndVertical();
        }

        private void DrawButtons()
        {
            _onlyEssential ??= new ActionLinkDescription()
            {
                Action = () => { RecordConsent(false); },
                Content = new GUIContent("Only share essential data"),
                Style = GUIStyles.LargeButton,
                ActionData = null,
                Origin = Origins.Self,
                OriginData = null,
            };

            _shareAdditional ??= new ActionLinkDescription()
            {
                Action = () => { RecordConsent(true); },
                Content = new GUIContent("Share additional data"),
                Style = GUIStyles.LargeButton,
                ActionData = null,
                Origin = Origins.Self,
                OriginData = null,
                BackgroundColor = Utils.ButtonAcceptColor,
            };

            EditorGUILayout.BeginHorizontal(_buttonsContainer);
            _onlyEssential.Draw();
            _shareAdditional.Draw();
            EditorGUILayout.EndHorizontal();
        }
    }
}
