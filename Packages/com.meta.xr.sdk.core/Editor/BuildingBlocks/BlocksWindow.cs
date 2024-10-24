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

#if UNITY_2021_2_OR_NEWER
#define OVR_BB_DRAGANDDROP
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Meta.XR.Editor.StatusMenu;
using Meta.XR.Editor.Tags;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Contents;
using static Meta.XR.Editor.UserInterface.Utils;
using Object = UnityEngine.Object;

namespace Meta.XR.BuildingBlocks.Editor
{
    public class BuildingBlocksWindow : EditorWindow
    {
        private const string MenuPath = "Meta/Tools/Building Blocks";

        private const int MenuPriority = 2;
        private const string WindowName = Utils.BlocksPublicName;

#if OVR_BB_DRAGANDDROP
        private const string DragAndDropLabel = "Dragging Block";
        private const string DragAndDropBlockDataLabel = "block";
        private const string DragAndDropBlockThumbnailLabel = "blockThumbnail";
#endif // OVR_BB_DRAGANDDROP

        private static readonly GUIContent Description =
            new GUIContent("<b>Building Blocks</b> helps you get up and running faster thanks to a library of XR capabilities" +
                           " that you can simply drag and drop into your project." +
                           $"\n• Drag and drop any <b>Building Block</b> into your scene." +
                           $"\n• You can drag and drop a <b>Building Block</b> directly into an existing <b>{nameof(GameObject)}</b> when relevant." +
                           $"\n• You can use multiple blocks to enable more XR capabilities.");

        private Vector2 _scrollPosition;

        private AnimatedContent _outline = null;
        private AnimatedContent _tutorial = null;
        private static readonly OVRProjectSetupSettingBool _tutorialCompleted =
            new OVRProjectSetupUserSettingBool("BuildingBlocksTutorialCompleted", false);
        private static bool _shouldShowTutorial;
        private bool _isHoveringHotControl;

        private static readonly HashSet<Tag> TagSearch = new();
        private static string _filterSearch = "";
        private const string FilterSearchControlName = "FilterSearchControl";
        private static bool _requestFilterSearchFocus = true;

        private readonly Repainter _repainter = new();
        private readonly Dimensions _dimensions = new();

        private const string SortTypeAlphabetically = "Alphabetically";
        private const string SortTypeMostUsed = "My recently used";
        private const string SortTypeMostPopular = "Most popular";
        private string[] _sortTypes = { SortTypeMostPopular, SortTypeAlphabetically, SortTypeMostUsed };
        private int _selectedSortTypeIndex;

        private class Repainter
        {
            private bool NeedsRepaint { get; set; }
            private Vector2 MousePosition { get; set; }

            public void Assess(EditorWindow window)
            {
                if (Event.current.type != EventType.Layout)
                {
                    return;
                }

                var fullRect = new Rect(0, 0, window.position.width, window.position.height);
                var isMoving = Event.current.mousePosition != MousePosition;
                MousePosition = Event.current.mousePosition;
                var isMovingOver = fullRect.Contains(Event.current.mousePosition);
                if (isMoving && isMovingOver)
                {
                    NeedsRepaint = true;
                }

                if (NeedsRepaint)
                {
                    window.Repaint();
                    NeedsRepaint = false;
                }
            }

            public void RequestRepaint()
            {
                NeedsRepaint = true;
            }
        }

        private class Dimensions
        {
            public int WindowWidth { get; private set; }
            public int WindowHeight { get; private set; }
            public int ExpectedThumbnailWidth { get; private set; }
            public int ExpectedThumbnailHeight { get; private set; }
            public int NumberOfColumns { get; private set; }

            private int _previousThumbnailWidth;
            private int _previousThumbnailHeight;

            public void Refresh(EditorWindow window)
            {
                var windowWidth = (int)window.position.width - Margin;
                var windowHeight = (int)window.position.height;

                if (Math.Abs(WindowWidth - windowWidth) <= Mathf.Epsilon
                    && Math.Abs(WindowHeight - windowHeight) <= Mathf.Epsilon)
                {
                    return;
                }

                WindowWidth = windowWidth;
                WindowHeight = windowHeight;

                var blockWidth = Styles.Constants.IdealThumbnailWidth;
                windowWidth = Mathf.Max(Styles.Constants.IdealThumbnailWidth + Padding * 3, windowWidth);
                var scrollableAreaWidth = windowWidth - 18;
                NumberOfColumns = Mathf.FloorToInt(scrollableAreaWidth / blockWidth);
                if (NumberOfColumns < 1) NumberOfColumns = 1;
                var marginToRemove = NumberOfColumns * Margin;

                ExpectedThumbnailWidth = (int)Mathf.FloorToInt((scrollableAreaWidth - marginToRemove) / NumberOfColumns);
                ExpectedThumbnailHeight = (int)Mathf.FloorToInt(ExpectedThumbnailWidth / Styles.Constants.ThumbnailRatio);
                if (ExpectedThumbnailWidth != _previousThumbnailWidth || ExpectedThumbnailHeight != _previousThumbnailHeight)
                {
                    _previousThumbnailWidth = ExpectedThumbnailWidth;
                    _previousThumbnailHeight = ExpectedThumbnailHeight;
                    OVREditorUtils.TweenHelper.Reset();
                }
            }
        }

        [MenuItem(MenuPath, false, MenuPriority)]
        private static void ShowWindow()
        {
            ShowWindow(Item.Origins.Menu);
        }

        internal static void ShowWindow(Item.Origins origin)
        {
            var window = GetWindow<BuildingBlocksWindow>(WindowName);
            window.minSize = new Vector2(800, 400);

            OVRTelemetry.Start(OVRTelemetryConstants.BB.MarkerId.OpenWindow)
                .AddAnnotation(OVRTelemetryConstants.BB.AnnotationType.ActionTrigger, origin.ToString())
                .Send();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
            {
                _repainter.RequestRepaint();
                return;
            }

            OnHeaderGUI();

            _isHoveringHotControl = false;

            _dimensions.Refresh(this);

            ShowList(_dimensions);

#if OVR_BB_DRAGANDDROP
            RefreshDragAndDrop(_dimensions);
#endif // OVR_BB_DRAGANDDROP

            _repainter.Assess(this);
        }

        private static void OnHeaderGUI()
        {
            Utils.Item.DrawHeader();

            if (!OVREditorUtils.IsUnityVersionCompatible())
            {
                using (new ColorScope(ColorScope.Scope.Background, ErrorColorSemiTransparent))
                {
                    EditorGUILayout.LabelField(
                        $"<b>Warning:</b> Your version of Unity is not supported. Consider upgrading to {OVREditorUtils.VersionCompatible} or higher.",
                        Styles.GUIStyles.ErrorHelpBox);
                }
            }

            EditorGUILayout.BeginHorizontal(GUIStyles.DialogBox);
            EditorGUILayout.LabelField(DialogIcon, GUIStyles.DialogIconStyle, GUILayout.Width(GUIStyles.DialogIconStyle.fixedWidth));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(Description, GUIStyles.DialogTextStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
        }


        public static void ShowSettingsMenu()
        {
            var menu = new GenericMenu();
            foreach (var tag in Tag.Registry)
            {
                if (tag.Behavior.ToggleableVisibility)
                {
                    tag.Behavior.VisibilitySetting.AppendToMenu(menu, ClearTagSearch);
                }
            }
            menu.ShowAsContext();
        }

        private static void ClearTagSearch()
        {
            TagSearch.Clear();
        }

        private static void RefreshShowTutorial()
        {
            _shouldShowTutorial = ShouldShowTutorial();
        }

        private void OnEnable()
        {
            RefreshBlockList();
#if OVR_BB_DRAGANDDROP
            DragAndDrop.AddDropHandler(SceneDropHandler);
            DragAndDrop.AddDropHandler(HierarchyDropHandler);
#endif // OVR_BB_DRAGANDDROP
            wantsMouseMove = true;
            RefreshShowTutorial();
        }

        internal static void RefreshBlockList()
        {
            _blockList = BlocksContentManager.FilterBlockWindowContent(GetList());
            _tagList = Tag.Registry.SortedTags.Where(tag => _blockList.Any((data =>
                data.Tags.Contains(tag) && data.Tags.All(otherTag => otherTag.Behavior.Visibility)))).ToList();
        }

        private void OnDisable()
        {
#if OVR_BB_DRAGANDDROP
            DragAndDrop.RemoveDropHandler(SceneDropHandler);
            DragAndDrop.RemoveDropHandler(HierarchyDropHandler);
#endif // OVR_BB_DRAGANDDROP
            _requestFilterSearchFocus = true;
        }

        private static IReadOnlyList<BlockBaseData> _blockList;
        private static IList<Tag> _tagList;

        private static IReadOnlyList<BlockBaseData> GetList() => Utils.Sort.MostPopular(BlockBaseData.Registry.Values).ToList();

        private void ShowList(Dimensions dimensions)
        {
            EditorGUILayout.BeginVertical(Styles.GUIStyles.Toolbar);
            GUI.SetNextControlName(FilterSearchControlName);
            var estimatedTextContent = new GUIContent($"Sort by: {_sortTypes[_selectedSortTypeIndex]} <buffer>");
            var spaceForSortByField = EditorStyles.label.CalcSize(estimatedTextContent).x + Padding * 2;
            _filterSearch = EditorGUILayout.TextField(_filterSearch, GUI.skin.FindStyle("SearchTextField"));
            var availableWidth = dimensions.WindowWidth - Margin * 2 - spaceForSortByField;

            EditorGUILayout.BeginHorizontal();
            ShowTagList("window", _tagList, TagSearch, Tag.TagListType.Filters, availableWidth);

            GUILayout.FlexibleSpace();
            ShowSortBy(spaceForSortByField);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (_requestFilterSearchFocus)
            {
                GUI.FocusControl(FilterSearchControlName);
                if (!String.IsNullOrEmpty(_filterSearch))
                {
                    TextEditor textEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                    textEditor.SelectAll();
                }
                _requestFilterSearchFocus = false;
            }

            ShowList(_blockList, Filter, dimensions);
        }

        public void ShowSortBy(float estimatedTotalSpace)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Width(estimatedTotalSpace));
            var labelContent = new GUIContent("Sort by");
            var labelWidth = EditorStyles.label.CalcSize(labelContent).x;
            EditorGUILayout.LabelField(labelContent, GUILayout.Width(labelWidth));
            _selectedSortTypeIndex = EditorGUILayout.Popup(_selectedSortTypeIndex, _sortTypes);
            EditorGUILayout.EndHorizontal();
        }

        private IEnumerable<BlockBaseData> Filter(IEnumerable<BlockBaseData> blocks) => blocks.Where(Match);

        private bool Match(BlockBaseData block)
        {
            if (block.Hidden) return false;

            if (TagSearch.Any(tag => !block.Tags.Contains(tag))) return false;

            var containsSearch = string.IsNullOrEmpty(_filterSearch)
                           || block.blockName.Contains(_filterSearch, StringComparison.InvariantCultureIgnoreCase)
                           || block.Description.Value.Contains(_filterSearch, StringComparison.InvariantCultureIgnoreCase)
                           || block.Tags.Any(tag => tag.Name.Contains(_filterSearch, StringComparison.InvariantCultureIgnoreCase));
            return containsSearch;
        }

        private bool IsVisibleInScrollView(int lineIndex, Dimensions dimensions)
        {
            var blockHeight = dimensions.ExpectedThumbnailHeight + +Styles.GUIStyles.DescriptionAreaStyle.fixedHeight +
                              3 + XR.Editor.UserInterface.Styles.Constants.Margin;
            var minLineIndex = (_scrollPosition.y / blockHeight) - 1;
            var maximumNumberOfLinesShown = dimensions.WindowHeight / blockHeight;
            var maxLineIndex = minLineIndex + maximumNumberOfLinesShown + 1;
            return minLineIndex <= lineIndex && lineIndex <= maxLineIndex;
        }

        private void ShowList(IEnumerable<BlockBaseData> blocks, Func<IEnumerable<BlockBaseData>,
                IEnumerable<BlockBaseData>> filter, Dimensions dimensions)
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, Styles.GUIStyles.NoMargin, GUILayout.Width(dimensions.WindowWidth));

            var blockWidth = dimensions.ExpectedThumbnailWidth;
            var blockHeight = dimensions.ExpectedThumbnailHeight + Styles.GUIStyles.DescriptionAreaStyle.fixedHeight + 3;

            var columnIndex = 0;
            var lineIndex = 0;
            var showTutorial = _shouldShowTutorial;
            EditorGUILayout.BeginHorizontal(Styles.GUIStyles.NoMargin);
            var filteredBlocks = SortBlocks(filter(blocks));
            foreach (var block in filteredBlocks)
            {
                var blockRect = new Rect(columnIndex * (blockWidth + Margin) + Margin, lineIndex * (blockHeight + Margin), blockWidth, blockHeight);
                var isVisibleInScrollView = IsVisibleInScrollView(lineIndex, dimensions);
                Show(block, blockRect, isVisibleInScrollView, dimensions.ExpectedThumbnailWidth, dimensions.ExpectedThumbnailHeight);

                if (showTutorial && block.CanBeAdded)
                {
                    ShowTutorial(blockRect);
                    showTutorial = false;
                }

                columnIndex++;
                if (columnIndex >= dimensions.NumberOfColumns)
                {
                    lineIndex++;
                    columnIndex = 0;
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal(Styles.GUIStyles.NoMargin);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private IEnumerable<BlockBaseData> SortBlocks(IEnumerable<BlockBaseData> blocks)
        {
            return _sortTypes[_selectedSortTypeIndex] switch
            {
                SortTypeAlphabetically => Utils.Sort.Alphabetical(blocks),
                SortTypeMostUsed => Utils.Sort.MostUsed(blocks),
                _ => blocks
            };
        }

        private BlockBaseData GetBlockFromId(IEnumerable<BlockBaseData> blocks, string id)
        {
            return blocks.FirstOrDefault(block => block.Id == id);
        }

        private void ShowThumbnail(BlockBaseData block, float targetHeight, int expectedThumbnailWidth, int expectedThumbnailHeight)
        {
            var thumbnailAreaStyle = new GUIStyle(Styles.GUIStyles.ThumbnailAreaStyle);
            thumbnailAreaStyle.fixedHeight = targetHeight;
            var thumbnailArea = EditorGUILayout.BeginVertical(thumbnailAreaStyle, GUILayout.Height(thumbnailAreaStyle.fixedHeight));
            {
                thumbnailArea.height = expectedThumbnailHeight;
                GUI.DrawTexture(thumbnailArea, block.Thumbnail, ScaleMode.ScaleAndCrop);

                var hasAttributes = ShowTagList(block.Id + "overlay", block.Tags, TagSearch, Tag.TagListType.Overlays, expectedThumbnailWidth);
                if (!hasAttributes)
                {
                    // This space fills the area, otherwise the area will have a height of null
                    // despite the fixedHeight set
                    EditorGUILayout.Space();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(Styles.GUIStyles.SeparatorAreaStyle);
            {
                // This space fills the area, otherwise the area will have a height of null
                // despite the fixedHeight set
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndVertical();
        }

        private static bool CanBeAdded(BlockBaseData block)
        {
            if (block is InterfaceBlockData interfaceBlockData)
            {
                return interfaceBlockData.HasInstallationRoutine
                       && !interfaceBlockData.HasMissingDependencies
                       && !interfaceBlockData.IsSingletonAndAlreadyPresent
                       && !Utils.IsApplicationPlaying.Invoke();
            }

            return block.CanBeAdded;
        }

        private static bool ShouldShowMissingPackageDependencies(BlockData block)
            => block != null && block.HasMissingPackageDependencies;

        private void ShowButtons(BlockBaseData block, Rect blockRect, bool canBeAdded, bool canBeSelected)
        {
            GUILayout.BeginArea(blockRect, Styles.GUIStyles.LargeButtonArea);
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var blockData = block as BlockData;
            if (canBeAdded)
            {
                var addIcon = block is BlockDownloaderData ? Styles.Contents.DownloadIcon : Styles.Contents.AddIcon;
                if (ShowLargeButton(block.Id, addIcon))
                {
                    block.AddToProject(null, block.RequireListRefreshAfterInstall ? RefreshBlockList : null);
                }
            }
            else if (ShouldShowMissingPackageDependencies(blockData))
            {
                if (ShowLargeButton(block.Id, Styles.Contents.DownloadPackageDependenciesIcon))
                {
                    var message = new StringBuilder();
                    message.Append(
                        $"In order to install {blockData.BlockName} the following packages are required:\n\n");

                    foreach (var packageId in blockData.GetMissingPackageDependencies)
                    {
                        if (CustomPackageDependencyRegistry.IsPackageDepInCustomRegistry(packageId))
                        {
                            var packageDepInfo = CustomPackageDependencyRegistry.GetPackageDepInfo(packageId);
                            message.Append($"- {packageDepInfo.PackageDisplayName}: {packageDepInfo.InstallationInstructions}\n");
                        }
                        else
                        {
                            message.Append($"- {packageId}\n");
                        }
                    }

                    EditorUtility.DisplayDialog("Package Dependencies Required", message.ToString(), "Ok");
                }
            }

            if (canBeSelected)
            {
                if (ShowLargeButton(block.Id, Styles.Contents.SelectIcon))
                {
                    blockData.SelectBlocksInScene();
                }
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ShowDescription(BlockBaseData block, Rect blockRect, float targetHeight, int expectedThumbnailWidth, int expectedThumbnailHeight, bool canBeAdded, bool canBeSelected)
        {
            var hoverDescription = OVREditorUtils.HoverHelper.IsHover(block.Id + "Description");
            var descriptionStyle = new GUIStyle(hoverDescription ? Styles.GUIStyles.DescriptionAreaHoverStyle : Styles.GUIStyles.DescriptionAreaStyle);
            descriptionStyle.fixedHeight += expectedThumbnailHeight - targetHeight;
            var descriptionArea = EditorGUILayout.BeginVertical(descriptionStyle);
            hoverDescription = OVREditorUtils.HoverHelper.IsHover(block.Id + "Description", Event.current, descriptionArea);
            EditorGUILayout.BeginHorizontal();
            var descriptionRect = blockRect;
            descriptionRect.y += targetHeight + 2;
            descriptionRect.height -= targetHeight + Padding + 2;
            GUILayout.BeginArea(descriptionRect);
            var numberOfIcons = 0;
            if (canBeAdded) numberOfIcons++;
            if (canBeSelected) numberOfIcons++;
            var iconWidth = Styles.GUIStyles.LargeButton.fixedWidth + Styles.GUIStyles.LargeButton.margin.horizontal;
            var padding = descriptionStyle.padding.horizontal;
            var style = new GUIStyle(Styles.GUIStyles.EmptyAreaStyle);
            style.fixedWidth = expectedThumbnailWidth - padding - numberOfIcons * iconWidth;
            style.fixedHeight = descriptionStyle.fixedHeight;
            style.padding = new RectOffset(Margin, Margin, Margin, Margin);
            EditorGUILayout.BeginVertical(style);
            EditorGUILayout.BeginHorizontal();
            var labelStyle = hoverDescription ? Styles.GUIStyles.LabelHoverStyle : Styles.GUIStyles.LabelStyle;
            EditorGUILayout.LabelField(block.BlockName, labelStyle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(block.Description, Styles.GUIStyles.InfoStyle);
            ShowTagList(block.Id, block.Tags, TagSearch, Tag.TagListType.Filters, style.fixedWidth);
            EditorGUILayout.EndVertical();
            GUILayout.EndArea();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

#if OVR_BB_DRAGANDDROP
        private void ShowDragAndDrop(BlockBaseData block, Rect blockRect, bool canBeAdded)
        {
            var hoverGrid = OVREditorUtils.HoverHelper.IsHover(block.Id + "Grid", Event.current, blockRect);
            if (canBeAdded)
            {
                if (hoverGrid)
                {
                    if (Event.current.type == EventType.Repaint && !_isHoveringHotControl)
                    {
                        EditorGUIUtility.AddCursorRect(blockRect, MouseCursor.Pan);
                    }

                    if (Event.current.type == EventType.MouseDown)
                    {
                        SetDragAndDrop(block);
                    }
                }
            }
        }

#endif // OVR_BB_DRAGANDDROP
        private void DrawEmptyGridItem(int expectedThumbnailWidth, int expectedThumbnailHeight)
        {
            // Early return with empty grid item
            var emptyGridStyle = new GUIStyle(Styles.GUIStyles.GridItemStyleWithHover)
            {
                fixedWidth = expectedThumbnailWidth,
                fixedHeight = expectedThumbnailHeight + Styles.GUIStyles.DescriptionAreaStyle.fixedHeight + 3
            };
            EditorGUILayout.BeginVertical(emptyGridStyle);
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        private void DrawBlockGridItem(BlockBaseData block, Rect blockRect, int expectedThumbnailWidth,
            int expectedThumbnailHeight)
        {
            var blockData = block as BlockData;
            var canBeAdded = CanBeAdded(block);
            var numberInScene = blockData != null ? blockData.ComputeNumberOfBlocksInScene() : 0;
            var canBeSelected = numberInScene > 0;
            var isHover = OVREditorUtils.HoverHelper.IsHover(block.Id + "Description");
            var targetHeight = isHover ? expectedThumbnailHeight - Styles.GUIStyles.DescriptionAreaStyle.fixedHeight : expectedThumbnailHeight;
            targetHeight = (int)OVREditorUtils.TweenHelper.GUISmooth(block.Id, targetHeight, ifNotCompletedDelegate: _repainter.RequestRepaint);
            var gridItemStyle = new GUIStyle(canBeAdded ? Styles.GUIStyles.GridItemStyleWithHover : Styles.GUIStyles.GridItemDisabledStyle)
            {
                fixedWidth = expectedThumbnailWidth,
                fixedHeight = expectedThumbnailHeight + Styles.GUIStyles.DescriptionAreaStyle.fixedHeight + 3
            };

            var expectedColor = canBeAdded ? Color.white : Styles.Colors.DisabledColor;
            using var color = new ColorScope(ColorScope.Scope.All, expectedColor);
            EditorGUILayout.BeginVertical(gridItemStyle);
            ShowThumbnail(block, targetHeight, expectedThumbnailWidth, expectedThumbnailHeight);
            ShowDescription(block, blockRect, targetHeight, expectedThumbnailWidth, expectedThumbnailHeight, canBeAdded, canBeSelected);
            ShowButtons(block, blockRect, canBeAdded, canBeSelected);
#if OVR_BB_DRAGANDDROP
            ShowDragAndDrop(block, blockRect, canBeAdded);
#endif // OVR_BB_DRAGANDDROP
            EditorGUILayout.EndVertical();

            if (isHover)
            {
                block.MarkAsSeen();
            }
        }

        private void Show(BlockBaseData block, Rect blockRect, bool isVisibleInScrollView, int expectedThumbnailWidth, int expectedThumbnailHeight)
        {
            if (!isVisibleInScrollView)
            {
                DrawEmptyGridItem(expectedThumbnailWidth, expectedThumbnailHeight);
                return;
            }

            DrawBlockGridItem(block, blockRect, expectedThumbnailWidth, expectedThumbnailHeight);
        }

        private bool ShowTagList(string controlId, IEnumerable<Tag> tagArray, ICollection<Tag> search, Tag.TagListType listType, float availableWidth)
        {
            var style = Styles.GUIStyles.FilterByTagGroup;
            style.fixedWidth = availableWidth;

            var styleHorizontal = new GUIStyle(style);

            EditorGUILayout.BeginVertical(style);
            EditorGUILayout.BeginHorizontal(styleHorizontal);
            var any = false;
            var currentWidth = 0.0f;
            foreach (var tag in tagArray)
            {
                if (!tag.Behavior.ShouldDraw(listType)) continue;

                var addedWidth = tag.Behavior.StyleWidth + Meta.XR.Editor.UserInterface.Styles.Constants.MiniPadding;
                currentWidth += addedWidth;

                if (currentWidth > availableWidth)
                {
                    // Wrap to new line
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal(style);
                    currentWidth = addedWidth;
                }

                any |= ShowTag(controlId + "list", tag, search, listType);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            return any;
        }

        private bool ShowTag(string controlId, Tag tag, ICollection<Tag> search, Tag.TagListType listType)
        {
            var tagBehavior = tag.Behavior;
            var drawn = tag.Behavior.Draw(controlId, listType, search.Contains(tag), out var hover, out var clicked);
            if (clicked)
            {
                if (TagSearch.Contains(tag))
                {
                    TagSearch.Remove(tag);
                }
                else
                {
                    TagSearch.Clear();
                    TagSearch.Add(tag);
                }
            }
            _isHoveringHotControl |= hover;
            return drawn;
        }

        private static bool ShouldShowTutorial()
        {
            _shouldShowTutorial = !_tutorialCompleted.Value;
            return _shouldShowTutorial;
        }

        private void ShowTutorial(Rect dragArea)
        {
            if (_outline == null && TextureContent.BuildPath("bb_outline.asset", Utils.BuildingBlocksAnimations, out var outlinePath))
            {
                _outline = AssetDatabase.LoadAssetAtPath<AnimatedContent>(outlinePath);
            }

            if (_outline != null)
            {
                _outline.Update();
                GUI.DrawTexture(dragArea, _outline.CurrentFrame);
            }

            if (_tutorial == null && TextureContent.BuildPath("bb_tutorial.asset", Utils.BuildingBlocksAnimations, out var tutorialPath))

            {
                _tutorial = AssetDatabase.LoadAssetAtPath<AnimatedContent>(tutorialPath);
            }

            if (_tutorial != null)
            {
                _tutorial.Update();
                GUI.DrawTexture(dragArea, _tutorial.CurrentFrame);
            }

            _repainter.RequestRepaint();
        }

        private bool ShowLargeButton(string controlId, TextureContent icon)
        {
            var previousColor = GUI.color;
            GUI.color = Color.white;
            var id = controlId + icon.Name;
            var hit = OVREditorUtils.HoverHelper.Button(id, icon, Styles.GUIStyles.LargeButton, out var hover);
            _isHoveringHotControl |= hover;
            GUI.color = previousColor;
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            return hit;
        }

#if OVR_BB_DRAGANDDROP
        private void RefreshDragAndDrop(Dimensions dimensions)
        {
            var blockThumbnail = DragAndDrop.GetGenericData(DragAndDropBlockThumbnailLabel) as Texture2D;
            if (blockThumbnail)
            {
                var cursorOffset = new Vector2(dimensions.ExpectedThumbnailWidth / 2.0f, dimensions.ExpectedThumbnailHeight / 2.0f);
                var cursorRect = new Rect(Event.current.mousePosition - cursorOffset, new Vector2(dimensions.ExpectedThumbnailWidth, dimensions.ExpectedThumbnailHeight));
                GUI.color = new Color(1, 1, 1, Styles.Constants.DragOpacity);
                GUI.DrawTexture(cursorRect, blockThumbnail, ScaleMode.ScaleAndCrop);
                GUI.color = Color.white;

                // Enforce a repaint next frame, as we need to move this thumbnail every frame
                _repainter.RequestRepaint();
            }

            if (Event.current.type == EventType.DragExited)
            {
                ResetDragThumbnail();
            }

            if (Event.current.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            }
        }

        private static DragAndDropVisualMode HierarchyDropHandler(
            int dropTargetInstanceID,
            HierarchyDropFlags dropMode,
            Transform parentForDraggedObjects,
            bool perform)
        {
            var hoveredObject = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;
            return DropHandler(perform, hoveredObject);
        }

        private static DragAndDropVisualMode SceneDropHandler(
            Object dropUpon,
            Vector3 worldPosition,
            Vector2 viewportPosition,
            Transform parentForDraggedObjects,
            bool perform)
        {
            return DropHandler(perform, dropUpon as GameObject);
        }

        private static DragAndDropVisualMode DropHandler(bool perform, GameObject dropUpon)
        {
            var block = DragAndDrop.GetGenericData(DragAndDropBlockDataLabel) as BlockBaseData;

            if (block == null)
            {
                return DragAndDropVisualMode.None;
            }

            if (!perform)
            {
                return DragAndDropVisualMode.Generic;
            }

            if (block.OverridesInstallRoutine && Selection.objects.Contains(dropUpon))
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                block.AddToObjects(Selection.objects.OfType<GameObject>().ToList());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
            else
            {
                block.AddToProject(dropUpon);
            }

            ResetDragAndDrop();
            _tutorialCompleted.Value = true;
            _shouldShowTutorial = false;

            return DragAndDropVisualMode.Generic;
        }

        private static void SetDragAndDrop(BlockBaseData block)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragAndDropBlockDataLabel, block);
            DragAndDrop.SetGenericData(DragAndDropBlockThumbnailLabel, block.Thumbnail);
            DragAndDrop.StartDrag(DragAndDropLabel);
        }

        private static void ResetDragThumbnail()
        {
            DragAndDrop.SetGenericData(DragAndDropBlockThumbnailLabel, null);
        }

        private static void ResetDragAndDrop()
        {
            DragAndDrop.SetGenericData(DragAndDropBlockDataLabel, null);
        }
#endif // OVR_BB_DRAGANDDROP
    }
}
