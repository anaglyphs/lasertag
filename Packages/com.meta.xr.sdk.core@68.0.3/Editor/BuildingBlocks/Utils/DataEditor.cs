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
using System.Collections.Generic;
using Meta.XR.Editor.Tags;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Contents;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.BuildingBlocks.Editor
{
    public abstract class DataEditor<T> : UnityEditor.Editor
        where T : ScriptableObject
    {
        private bool _initialized;
        private int _sectionIndex;
        private bool _validationPassed;
        private string _validationExceptionMessage;

        protected int SectionIndex => ++_sectionIndex;
        protected T Data => (T)serializedObject.targetObject;

        protected abstract void OnGUIImplementation();
        protected abstract BlockData BlockData { get; }
        protected abstract string Instructions { get; }

        protected virtual void OnEnable()
        {
        }


        public override void OnInspectorGUI()
        {
            _sectionIndex = 0;

            using var disabledScope = new EditorGUI.DisabledScope(true);
            serializedObject.Update();

            // Thumbnail display
            DrawThumbnail(BlockData);
            DrawInstructions(Instructions);

            OnGUIImplementation();

            serializedObject.ApplyModifiedProperties();

        }

        protected void DrawHeader(string content)
        {
            EditorGUILayout.BeginHorizontal(GUIStyles.InspectorHeaderLabelBox);
            EditorGUILayout.LabelField($"{SectionIndex}. {content}", GUIStyles.InspectorHeaderLabel);
            EditorGUILayout.EndHorizontal();
        }

        protected void DrawInstructions(string content)
        {
        }

        private void DrawValidation()
        {
            var backgroundColor = _validationPassed ? SuccessColor : ErrorColor;
            using var colorScope = new ColorScope(ColorScope.Scope.Background, backgroundColor);

            var noticeRectangle = EditorGUILayout.BeginHorizontal(GUIStyles.NoticeBox);
            EditorGUILayout.LabelField(_validationPassed ? Styles.Contents.SuccessIcon : Styles.Contents.ErrorIcon, GUIStyles.NoticeIconStyle, GUILayout.Width(LargeMargin));
            var status = _validationPassed ? "Validated" : $"Validation failed : {_validationExceptionMessage}";
            EditorGUILayout.LabelField(status, GUIStyles.NoticeTextStyle);
            EditorGUILayout.EndHorizontal();

            // Draw Left Margin
            using var contentColorScope = new ColorScope(ColorScope.Scope.All, backgroundColor);
            var xMax = noticeRectangle.xMax;
            noticeRectangle.width = noticeRectangle.x;
            noticeRectangle.x = 0;
            EditorGUI.DrawRect(noticeRectangle, LightGray);

            // Draw Right Margin
            noticeRectangle.width = 8;
            noticeRectangle.x = xMax;
            EditorGUI.DrawRect(noticeRectangle, LightGray);
        }


        private void DrawThumbnail(BlockData blockData)
        {
            if (blockData == null)
            {
                return;
            }

            var currentWidth = EditorGUIUtility.currentViewWidth;
            var expectedHeight = currentWidth / Styles.Constants.ThumbnailRatio;
            expectedHeight *= 0.2f;

            // Thumbnail
            var rect = GUILayoutUtility.GetRect(currentWidth, expectedHeight - 4);
            rect.x -= 20;
            rect.width += 40;
            rect.y -= 4;
            rect.height += 4;
            GUI.DrawTexture(rect, blockData.Thumbnail, ScaleMode.ScaleAndCrop);

            // Statuses
            GUILayout.BeginArea(new Rect(Styles.GUIStyles.TagStyle.margin.left, Styles.GUIStyles.TagStyle.margin.top, currentWidth, expectedHeight));
            foreach (var tag in blockData.Tags)
            {
                if (tag.Behavior.ShowOverlay)
                {
                    DrawTag(tag, true);
                }
            }
            GUILayout.EndArea();

            // Separator
            rect = GUILayoutUtility.GetRect(currentWidth, 1);
            rect.x -= 20;
            rect.width += 40;
            GUI.DrawTexture(rect, Styles.Colors.AccentColor.ToTexture(),
                ScaleMode.ScaleAndCrop);

            GUILayout.BeginArea(new Rect(0, 0, currentWidth, expectedHeight));
            EditorGUILayout.BeginHorizontal(GUIStyles.HeaderIcons);
            EditorGUILayout.Space(0, true);
            Utils.Item.ShowHeaderIcons();
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawTag(Tag tag, bool overlay = false)
        {
            var tagBehavior = tag.Behavior;
            var style = tagBehavior.Icon != null ? Styles.GUIStyles.TagStyleWithIcon : Styles.GUIStyles.TagStyle;
            var backgroundColors = overlay ? Styles.GUIStyles.TagOverlayBackgroundColors : Styles.GUIStyles.TagBackgroundColors;

            var tagContent = new GUIContent(tag.Name);
            var tagSize = style.CalcSize(tagContent);
            var rect = GUILayoutUtility.GetRect(tagContent, style, GUILayout.MinWidth(tagSize.x + 1));
            var color = backgroundColors.GetColor(false, false);
            using (new ColorScope(ColorScope.Scope.Background, color))
            {
                using (new ColorScope(ColorScope.Scope.Content,
                           tagBehavior.Color))
                {
                    if (GUI.Button(rect, tagContent, style))
                    {
                    }

                    if (tagBehavior.Icon != null)
                    {
                        GUI.Label(rect, tagBehavior.Icon, Styles.GUIStyles.TagIcon);
                    }
                }
            }
        }

        protected void ShowBlock(BlockData data)
        {
            if (data == null) return;

            var previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // Thumbnail
            var gridStyle = new GUIStyle(Styles.GUIStyles.GridItemStyle)
            {
                margin = new RectOffset(0, 0, 0, 0)
            };
            EditorGUILayout.BeginHorizontal(gridStyle);
            EditorGUILayout.BeginHorizontal(Styles.GUIStyles.DescriptionAreaStyle);

            var expectedSize = ItemHeight;
            var rect = GUILayoutUtility.GetRect(0, expectedSize);
            rect.y -= Padding;
            rect.x -= Padding;
            rect.width = ItemHeight;
            GUI.DrawTexture(rect, data.Thumbnail, ScaleMode.ScaleAndCrop);

            EditorGUILayout.Space(ItemHeight);

            // Label
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            var labelStyle = Styles.GUIStyles.LabelStyle;
            EditorGUILayout.LabelField(data.BlockName, labelStyle);
            labelStyle = Styles.GUIStyles.SubtitleStyle;
            EditorGUILayout.EndHorizontal();
            var blocksCount = data.GetBlocks().Count;
            EditorGUILayout.LabelField(data.Description, Styles.GUIStyles.InfoStyle);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel = previousIndent;
        }

        internal static void DrawVariants(string label, IEnumerable<VariantHandle> variants, SerializedObject serializedObject)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            foreach (var variant in variants)
            {
                variant.DrawGUI(serializedObject);
            }
        }
    }
}
