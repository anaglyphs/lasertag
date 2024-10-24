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

using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.StatusMenu;
using Meta.XR.Editor.Tags;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.BuildingBlocks.Editor
{
    [CustomEditor(typeof(BuildingBlock))]
    public class BuildingBlockEditor : UnityEditor.Editor
    {
        private BuildingBlock _block;
        private BlockData _blockData;

        private bool _foldoutInstruction = true;

        public override void OnInspectorGUI()
        {
            _block = target as BuildingBlock;
            _blockData = _block.GetBlockData();

            if (_blockData == null)
            {
                return;
            }

            ShowThumbnail();
            ShowBlock(_blockData, _block, false, false, true);
            ShowTagList(_blockData.Tags, Tag.TagListType.Filters);
            ShowAdditionals();

            EditorGUILayout.Space();
            ShowBlockDataList("Dependencies", "No dependency blocks are required.", _blockData.GetAllDependencies().ToList());

            EditorGUILayout.Space();
            ShowBlockDataList("Used by", "No other blocks depend on this one.", _blockData.GetUsingBlockDatasInScene());

            EditorGUILayout.Space();
            ShowInstructions();

        }

        protected virtual void ShowAdditionals()
        {
            // A placeholder for adding more details. E.g., Info box from GuidedSetup.
            // Override this function to implement your additional details.
        }

        private void ShowVersionInfo()
        {
            EditorGUILayout.LabelField("Version", EditorStyles.boldLabel);

            var blockVersion = _block ? _block.Version : 0;
            var currentVersionStr = $"Current version: {blockVersion}.";
            if (_blockData.IsUpdateAvailableForBlock(_block))
            {
                EditorGUILayout.LabelField($"{currentVersionStr} Newest version: {_blockData.Version}.",
                    Styles.GUIStyles.InfoStyle);

                if (!GUILayout.Button($"Update to latest version ({_blockData.Version})"))
                {
                    return;
                }

                if (EditorUtility.DisplayDialog("Confirmation",
                        "Any changes done to this block will be lost. Do you want to proceed?", "Yes", "No"))
                {
#pragma warning disable CS4014
                    _blockData.UpdateBlockToLatestVersion(_block);
#pragma warning restore CS4014
                }
            }
            else
            {
                EditorGUILayout.LabelField($"{currentVersionStr} Block is up to date", Styles.GUIStyles.InfoStyle);
            }
        }

        private void ShowInstructions()
        {
            if (string.IsNullOrEmpty(_blockData.UsageInstructions)) return;

            EditorGUILayout.Space();
            _foldoutInstruction =
                EditorGUILayout.Foldout(_foldoutInstruction, "Block instructions", Styles.GUIStyles.FoldoutBoldLabel);
            if (_foldoutInstruction)
            {
                EditorGUILayout.LabelField(_blockData.UsageInstructions, EditorStyles.helpBox);
            }
        }

        private void ShowThumbnail()
        {
            var currentWidth = EditorGUIUtility.currentViewWidth;
            var expectedHeight = currentWidth / Styles.Constants.ThumbnailRatio;
            expectedHeight *= 0.5f;

            // Thumbnail
            var rect = GUILayoutUtility.GetRect(currentWidth, expectedHeight);
            rect.x -= 20;
            rect.width += 40;
            rect.y -= 4;
            GUI.DrawTexture(rect, _blockData.Thumbnail, ScaleMode.ScaleAndCrop);

            GUILayout.BeginArea(new Rect(Styles.GUIStyles.TagStyle.margin.left,
                Styles.GUIStyles.TagStyle.margin.top, currentWidth, expectedHeight));
            ShowTagList(_blockData.Tags, Tag.TagListType.Overlays);
            GUILayout.EndArea();

            // Separator
            rect = GUILayoutUtility.GetRect(currentWidth, 1);
            rect.x -= 20;
            rect.width += 40;
            rect.y -= 4;
            GUI.DrawTexture(rect, Styles.Colors.AccentColor.ToTexture(),
                ScaleMode.ScaleAndCrop);
        }

        private void ShowTagList(IEnumerable<Tag> tagArray, Tag.TagListType listType)
        {
            EditorGUILayout.BeginHorizontal();
            foreach (var tag in tagArray)
            {
                ShowTag(tag, listType);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void ShowTag(Tag tag, Tag.TagListType listType)
        {
            var tagBehavior = tag.Behavior;
            if (!tagBehavior.Show)
            {
                return;
            }

            switch (listType)
            {
                case Tag.TagListType.Filters when !tagBehavior.CanFilterBy:
                case Tag.TagListType.Overlays when !tagBehavior.ShowOverlay:
                    return;
            }

            var style = tagBehavior.Icon != null ? Styles.GUIStyles.TagStyleWithIcon : Styles.GUIStyles.TagStyle;
            var backgroundColors = listType == Tag.TagListType.Overlays ? Styles.GUIStyles.TagOverlayBackgroundColors : Styles.GUIStyles.TagBackgroundColors;

            var tagContent = new GUIContent(tag.Name);
            var tagSize = style.CalcSize(tagContent);
            var rect = GUILayoutUtility.GetRect(tagContent, style, GUILayout.MinWidth(tagSize.x + 1));
            var color = backgroundColors.GetColor(false, false);
            using (new ColorScope(ColorScope.Scope.Background, color))
            {
                using (new ColorScope(ColorScope.Scope.Content, tagBehavior.Color))

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

        private static bool ShowLargeButton(GUIContent icon)
        {
            var previousColor = GUI.color;
            GUI.color = Color.white;
            var hit = GUILayout.Button(icon, Styles.GUIStyles.LargeButton);
            GUI.color = previousColor;
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            return hit;
        }

        private void ShowBlockDataList(string listName, string noneNotice, IReadOnlyCollection<BlockData> list)
        {
            EditorGUILayout.LabelField(listName, EditorStyles.boldLabel);

            if (list.Count == 0)
            {
                EditorGUILayout.LabelField(noneNotice, Styles.GUIStyles.InfoStyle);
                return;
            }

            foreach (var dependency in list)
            {
                ShowBlock(dependency, null, true, true, false);
            }
        }

        private async void ShowBlock(BlockData data, BuildingBlock block, bool asGridItem,
            bool showAction, bool showBuildingBlock)
        {
            var previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            data = data ? data : block.GetBlockData();
            block = block ? block : data.GetBlock();

            // Thumbnail
            if (asGridItem)
            {
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

                EditorGUILayout.Space(ItemHeight - Padding - SmallIconSize * 0.5f - 2);
                EditorGUILayout.LabelField(block != null ? Styles.Contents.SuccessIcon : Styles.Contents.ErrorIcon, Styles.GUIStyles.IconStyle,
                    GUILayout.Width(SmallIconSize), GUILayout.Height(ItemHeight - Padding * 2));
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginHorizontal();
            }

            // Label
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            var labelStyle = Styles.GUIStyles.LabelStyle;
            EditorGUILayout.LabelField(data.BlockName, labelStyle);
            labelStyle = Styles.GUIStyles.SubtitleStyle;
            EditorGUILayout.EndHorizontal();
            if (asGridItem)
            {
                var blocksCount = data.GetBlocks().Count;
                var label = blocksCount > 0
                    ? $"{blocksCount} {OVREditorUtils.ChoosePlural(blocksCount, "Block", "Blocks")} installed"
                    : "Not Installed";
                EditorGUILayout.LabelField(label, Styles.GUIStyles.InfoStyle);
            }
            else
            {
                EditorGUILayout.LabelField(data.Description, Styles.GUIStyles.InfoStyle);
            }
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            if (showAction)
            {
                if (block != null)
                {
                    if (ShowLargeButton(Utils.GotoIcon))
                    {
                        data.SelectBlocksInScene();
                    }
                }
                else
                {
                    if (ShowLargeButton(Utils.AddIcon))
                    {
                        await data.AddToProject();
                    }
                }
            }

            if (showBuildingBlock && ShowLargeButton(Utils.StatusIcon))
            {
                BuildingBlocksWindow.ShowWindow(Item.Origins.Component);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndHorizontal();

            // Only for dependency block(s)
            if (!showBuildingBlock)
            {
                AddBlockHighlightListeners(block);
            }

            EditorGUI.indentLevel = previousIndent;
        }

        private static void AddBlockHighlightListeners(BuildingBlock buildingBlock)
        {
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            var currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 ||
                !rect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            buildingBlock.HighlightBlockInScene();
            if (currentEvent.clickCount == 2)
                buildingBlock.SelectBlockInScene();

            currentEvent.Use();
        }
    }
}
