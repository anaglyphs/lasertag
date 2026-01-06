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
using System.IO;
using System.Linq;
using System.Text;
using Meta.XR.Editor.EditorCoroutine;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.Tags;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Utils;
using Object = UnityEngine.Object;

namespace Meta.XR.BuildingBlocks.Editor
{
	public partial class BuildingBlocksWindow : EditorWindow
	{
		private const string WindowName = Utils.BlocksPublicName;

		private const string DragAndDropLabel = "Dragging Block";

		private const string DragAndDropBlockDataLabel =
			nameof(BuildingBlocksWindow) + nameof(DragAndDropBlockDataLabel);

		private const string DragAndDropBlockThumbnailLabel =
			nameof(BuildingBlocksWindow) + nameof(DragAndDropBlockThumbnailLabel);

		private const string DragAndDropStartMousePosition =
			nameof(BuildingBlocksWindow) + nameof(DragAndDropStartMousePosition);

		internal static readonly GUIContent Description =
			new(
				"<b>Building Blocks</b> helps you get up and running faster thanks to a library of XR capabilities" +
				" that you can simply drag and drop into your project." +
				"\n• Drag and drop any <b>Building Block</b> into your scene." +
				$"\n• You can drag and drop a <b>Building Block</b> directly into an existing <b>{nameof(GameObject)}</b> when relevant." +
				"\n• You can use multiple blocks to enable more XR capabilities." +
				"\n• Click on a <b>Building Block</b> to see more information.");

		private Vector2 _scrollPosition;

		private float _horizontalPageRatio;
		private static IEnumerable<BlockBaseData> _currentBlockList = Enumerable.Empty<BlockBaseData>();

		private AnimatedContent _outline;
		private AnimatedContent _tutorial;

		private static readonly CustomBool TutorialCompleted =
			new UserBool
			{
				Owner = Utils.ToolDescriptor,
				Uid = "TutorialCompleted",
				OldKey = "OVRProjectSetup.BuildingBlocksTutorialCompleted",
				Default = false
			};

		private static bool _shouldShowTutorial;


		private static readonly HashSet<Tag> TagSearch = new();
		private static string _filterSearch = "";
		private const string FilterSearchControlName = "FilterSearchControl";
		private static bool _requestFilterSearchFocus = true;

		private readonly Repainter _repainter = new();
		private readonly Dimensions _dimensions = new();

		private const string SortTypeAlphabetically = "Alphabetically";
		private const string SortTypeMostUsed = "My most used";
		private const string SortTypeMostPopular = "Most popular";
		private static readonly string[] SortTypes = { SortTypeMostPopular, SortTypeAlphabetically, SortTypeMostUsed };
		private static int _selectedSortTypeIndex;

		internal class Dimensions
		{
			public int WindowWidth { get; private set; }
			public int WindowHeight { get; private set; }
			public int ExpectedThumbnailWidth { get; private set; }
			public int ExpectedThumbnailHeight { get; private set; }
			public int ExpectedCollectionThumbnailWidth { get; private set; }
			public int ExpectedCollectionThumbnailHeight { get; private set; }
			public int CollectionNumberOfColumn { get; private set; }
			public int NumberOfColumns { get; private set; }

			private int _previousThumbnailWidth;
			private int _previousThumbnailHeight;

			public void Refresh(EditorWindow window)
			{
				int windowWidth = (int)window.position.width - Margin;
				int windowHeight = (int)window.position.height;

				if (Math.Abs(WindowWidth - windowWidth) <= Mathf.Epsilon
				    && Math.Abs(WindowHeight - windowHeight) <= Mathf.Epsilon)
					return;

				WindowWidth = windowWidth;
				WindowHeight = windowHeight;

				int blockWidth = Styles.Constants.IdealThumbnailWidth;
				windowWidth = Mathf.Max(Styles.Constants.IdealThumbnailWidth + Padding * 3, windowWidth);
				int scrollableAreaWidth = windowWidth - 18;
				NumberOfColumns = Mathf.FloorToInt(scrollableAreaWidth / blockWidth);
				if (NumberOfColumns < 1) NumberOfColumns = 1;
				int marginToRemove = NumberOfColumns * Margin;

				ExpectedThumbnailWidth = Mathf.FloorToInt((scrollableAreaWidth - marginToRemove) / NumberOfColumns);
				ExpectedThumbnailHeight = Mathf.FloorToInt(ExpectedThumbnailWidth / Styles.Constants.ThumbnailRatio);
				if (ExpectedThumbnailWidth != _previousThumbnailWidth ||
				    ExpectedThumbnailHeight != _previousThumbnailHeight)
				{
					_previousThumbnailWidth = ExpectedThumbnailWidth;
					_previousThumbnailHeight = ExpectedThumbnailHeight;
				}

				int collectionWidth = Styles.Constants.IdealCollectionWidth;
				int collectionAreaWidth = windowWidth - LargeMargin * 3;
				CollectionNumberOfColumn = Mathf.FloorToInt((float)collectionAreaWidth / collectionWidth);
				if (CollectionNumberOfColumn < 1) CollectionNumberOfColumn = 1;

				ExpectedCollectionThumbnailWidth =
					Mathf.FloorToInt((float)(collectionAreaWidth - Margin) / CollectionNumberOfColumn);
				ExpectedCollectionThumbnailHeight =
					Mathf.FloorToInt(ExpectedCollectionThumbnailWidth / Styles.Constants.CollectionDivRatio);
			}
		}

		internal static void ShowWindow(Origins origin, IIdentified originData, bool showDetailPane = false,
			BlockData data = null)
		{
			BuildingBlocksWindow window = GetWindow<BuildingBlocksWindow>(WindowName);
			window.minSize = new Vector2(800, 400);

			OVRTelemetry.Start(OVRTelemetryConstants.BB.MarkerId.OpenWindow)
				.AddAnnotation(OVRTelemetryConstants.BB.AnnotationType.ActionTrigger, origin.ToString())
				.AddAnnotation(OVRTelemetryConstants.BB.AnnotationType.BlocksCount, _blockList.Count)
				.Send();

			if (showDetailPane) EditorCoroutine.Start(window.ToggleDetailPane(data, origin, originData));
		}

		private void OnGUI()
		{
			// To get mouse position relative to base window.
			CurrentMousePosition = Event.current.mousePosition;

			if (HandleMouseEvents())
			{
				_repainter.RequestRepaint();
				return;
			}

			OnHeaderGUI();

			_dimensions.Refresh(this);

			ShowList(_dimensions);

			DrawDragAndDrop(_dimensions);

			_repainter.Assess(this);
		}

		private static void OnHeaderGUI()
		{
			Utils.ToolDescriptor.DrawHeaderFromWindow(Origins.Self);

			if (!OVREditorUtils.IsUnityVersionCompatible())
				using (new ColorScope(ColorScope.Scope.Background, ErrorColorSemiTransparent))
				{
					EditorGUILayout.LabelField(
						$"<b>Warning:</b> Your version of Unity is not supported. Consider upgrading to {OVREditorUtils.VersionCompatible} or higher.",
						Styles.GUIStyles.ErrorHelpBox);
				}

			Utils.ToolDescriptor.DrawDescriptionHeader(Description.text, Origins.Self);

			EditorGUILayout.Space();
		}

		public static void BuildSettingsMenu(GenericMenu menu)
		{
			if (ValidCollections) SkipCollectionOnStart.DrawForMenu(menu, Origins.HeaderIcons, Utils.ToolDescriptor);
		}

		internal static void OnUserSettingsGUI(Origins origin, string searchContext)
		{
			Utils.ToolDescriptor.DrawButton(null, false, true, origin);

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
			Utils.ToolDescriptor.ShowOverview.DrawForGUI(origin, Utils.ToolDescriptor);

			if (ValidCollections) SkipCollectionOnStart.DrawForGUI(origin, Utils.ToolDescriptor);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Blocks Filtering", EditorStyles.boldLabel);
			foreach (Tag tag in Tag.Registry)
				if (tag.Behavior.ToggleableVisibility)
					tag.Behavior.VisibilitySetting.DrawForGUI(origin, Utils.ToolDescriptor, ClearTagSearch);
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
			RefreshCollectionTags();

			BlocksContentManager.OnContentChanged += RefreshBlockList;
			BlocksContentManager.OnContentChanged += RefreshCollectionTags;

			wantsMouseMove = true;
			RefreshShowTutorial();

			// Init current block list to detect filter change
			ResetCurrentBlockList();

			if (!ValidCollections) return;

			if (SelectedCollection.Value != null)
				TriggerPageTransition(Page.Grid, true);
			else
				TriggerPageTransition(SkipCollectionOnStart.Value ? Page.Grid : Page.Collections, true);
		}

		private static void RefreshBlockList()
		{
			CollectionTagBehavior selectedCollection = SelectedCollection.Value;
			if (selectedCollection != null)
				_blockList = BlocksContentManager.GetCollection(selectedCollection);
			else
				_blockList = Utils.FilteredRegistry;

			_blockList = Utils.Sort.MostPopular(_blockList)
				.Where(block => !block.Hidden)
				.ToList();

			UpdateCurrentBlockList();

			_tagList = Tag.Registry.SortedTags
				.Where(tag => _blockList.Any(data =>
					data.Tags.Contains(tag)
					&& data.Tags.All(otherTag => otherTag.Behavior.Visibility))
				);
		}

		private void OnDisable()
		{
			_requestFilterSearchFocus = true;

			_horizontalPageRatio = 0.0f;

			BlocksContentManager.OnContentChanged -= RefreshCollectionTags;
			BlocksContentManager.OnContentChanged -= RefreshBlockList;

			_repainter.OnDisable();
		}

		private static IReadOnlyCollection<BlockBaseData> _blockList = Array.Empty<BlockBaseData>();
		private static IEnumerable<Tag> _tagList = Enumerable.Empty<Tag>();

		private void ShowList(Dimensions dimensions)
		{
			EditorGUILayout.BeginHorizontal(Styles.GUIStyles.Toolbar);

			DrawBackToCollectionIcon();

			EditorGUILayout.BeginVertical();

			#region Search bar

			GUI.SetNextControlName(FilterSearchControlName);
			GUIContent estimatedTextContent = new($"Sort by: {SortTypes[_selectedSortTypeIndex]} <buffer>");
			float spaceForSortByField = EditorStyles.label.CalcSize(estimatedTextContent).x + Padding * 2;

			string previousFilterSearch = _filterSearch;
			_filterSearch = EditorGUILayout.TextField(_filterSearch, GUI.skin.FindStyle("SearchTextField"));
			if (_filterSearch != previousFilterSearch)
			{
				UpdateCurrentBlockList();
				ReturnToGrid();
			}

			float availableWidth = dimensions.WindowWidth - Margin * 2 - spaceForSortByField -
			                       Styles.GUIStyles.LargeButton.fixedWidth;

			#endregion // Search bar

			#region Tags and SortBy section

			EditorGUILayout.BeginHorizontal();

			CommonUIHelpers.DrawList("window", _tagList, Tag.TagListType.Filters, availableWidth, TagSearch,
				OnSelectTag, DrawSelectedCollectionTag);

			GUILayout.FlexibleSpace();
			ShowSortBy(spaceForSortByField);

			EditorGUILayout.EndHorizontal();

			#endregion // Tags and SortBy section

			EditorGUILayout.EndVertical();
			EditorGUILayout.EndHorizontal();

			if (_requestFilterSearchFocus)
			{
				GUI.FocusControl(FilterSearchControlName);
				if (!string.IsNullOrEmpty(_filterSearch))
				{
					TextEditor textEditor =
						(TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
					textEditor.SelectAll();
				}

				_requestFilterSearchFocus = false;
			}

			EditorGUILayout.BeginScrollView(new Vector2(_horizontalPageRatio * dimensions.WindowWidth, 0.0f), false,
				false, GUIStyle.none, GUIStyle.none, Styles.GUIStyles.NoMargin,
				GUILayout.Width(dimensions.WindowWidth));
			EditorGUILayout.BeginHorizontal();

			EditorGUI.BeginDisabledGroup(CurrentTargetPage == Page.Details);
			ShowCollectionsPage(dimensions);
			ShowBlocks(dimensions);
			EditorGUI.EndDisabledGroup();

			if (CurrentTargetPage == Page.Details)
			{
				Rect lastRect = GUILayoutUtility.GetLastRect();
				if (GUI.Button(lastRect, "", GUIStyle.none))
				{
					UpdateCurrentBlockList();
					ReturnToGrid();
				}
			}

			DrawBlockDetails(dimensions);

			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndScrollView();
		}

		private void ShowSortBy(float estimatedTotalSpace)
		{
			EditorGUILayout.BeginHorizontal(GUILayout.Width(estimatedTotalSpace));
			GUIContent labelContent = new("Sort by");
			float labelWidth = EditorStyles.label.CalcSize(labelContent).x;
			EditorGUILayout.LabelField(labelContent, GUILayout.Width(labelWidth));
			int previousSortIndex = _selectedSortTypeIndex;
			_selectedSortTypeIndex = EditorGUILayout.Popup(_selectedSortTypeIndex, SortTypes);
			if (previousSortIndex != _selectedSortTypeIndex)
			{
				UpdateCurrentBlockList();
				ReturnToGrid();
			}

			EditorGUILayout.EndHorizontal();
		}

		private static IEnumerable<BlockBaseData> Filter(IEnumerable<BlockBaseData> blocks)
		{
			return blocks.Where(Match);
		}

		private static bool Match(BlockBaseData block)
		{
			if (block.Hidden) return false;

			if (TagSearch.Any(tag => !block.Tags.Contains(tag))) return false;

			bool containsSearch = string.IsNullOrEmpty(_filterSearch)
			                      || block.blockName.Contains(_filterSearch,
				                      StringComparison.InvariantCultureIgnoreCase)
			                      || block.Description.Value.Contains(_filterSearch,
				                      StringComparison.InvariantCultureIgnoreCase)
			                      || block.Tags.Any(tag =>
				                      tag.Name.Contains(_filterSearch, StringComparison.InvariantCultureIgnoreCase));
			return containsSearch;
		}

		private bool IsVisibleInScrollView(int lineIndex, Dimensions dimensions)
		{
			float blockHeight = dimensions.ExpectedThumbnailHeight +
			                    +Styles.GUIStyles.DescriptionAreaStyle.fixedHeight +
			                    3 + Margin;
			float minLineIndex = _scrollPosition.y / blockHeight - 1;
			float maximumNumberOfLinesShown = dimensions.WindowHeight / blockHeight;
			float maxLineIndex = minLineIndex + maximumNumberOfLinesShown + 1;
			return minLineIndex <= lineIndex && lineIndex <= maxLineIndex;
		}

		private void ShowBlocks(Dimensions dimensions)
		{
			_scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, true, GUIStyle.none,
				GUI.skin.verticalScrollbar, Styles.GUIStyles.NoMargin, GUILayout.Width(dimensions.WindowWidth));

			int blockWidth = dimensions.ExpectedThumbnailWidth;
			float blockHeight = dimensions.ExpectedThumbnailHeight + Styles.GUIStyles.DescriptionAreaStyle.fixedHeight +
			                    3;

			int columnIndex = 0;
			int lineIndex = 0;
			bool showTutorial = _shouldShowTutorial;
			EditorGUILayout.BeginHorizontal(Styles.GUIStyles.NoMargin);
			foreach (BlockBaseData block in _currentBlockList)
			{
				Rect blockRect = new(columnIndex * (blockWidth + Margin) + Margin,
					lineIndex * (blockHeight + Margin), blockWidth, blockHeight);
				bool isVisibleInScrollView = IsVisibleInScrollView(lineIndex, dimensions);
				Show(block, blockRect, isVisibleInScrollView, dimensions.ExpectedThumbnailWidth,
					dimensions.ExpectedThumbnailHeight);

				if (showTutorial && block.IsInteractable)
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

		private static IEnumerable<BlockBaseData> SortBlocks(IEnumerable<BlockBaseData> blocks)
		{
			return SortTypes[_selectedSortTypeIndex] switch
			{
				SortTypeAlphabetically => Utils.Sort.Alphabetical(blocks),
				SortTypeMostUsed => Utils.Sort.MostUsed(blocks),
				_ => blocks
			};
		}

		private void ShowThumbnail(BlockBaseData block, int expectedThumbnailWidth, int expectedThumbnailHeight)
		{
			GUIStyle thumbnailAreaStyle = new(Styles.GUIStyles.ThumbnailAreaStyle)
			{
				fixedHeight = expectedThumbnailHeight
			};
			Rect thumbnailArea =
				EditorGUILayout.BeginVertical(thumbnailAreaStyle, GUILayout.Height(thumbnailAreaStyle.fixedHeight));
			{
				thumbnailArea.height = expectedThumbnailHeight;
				GUI.DrawTexture(thumbnailArea, block.Thumbnail, ScaleMode.ScaleAndCrop, false,
					Styles.Constants.ThumbnailSourceRatio, GUI.color, Vector4.zero,
					Styles.Constants.UpperRoundedBorderVectors);

				CommonUIHelpers.DrawList(block.Id + "_overlay", block.Tags,
					Tag.TagListType.Overlays, expectedThumbnailWidth, TagSearch, OnSelectTag);
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

		private static bool ShouldShowMissingPackageDependencies(BlockBaseData block)
		{
			return block != null && block.GetCache().HasMissingPackageDependencies;
		}

		private static void ShowBlockInstallButton(BlockBaseData block, Rect blockRect, bool canBeAdded,
			bool canBeSelected)
		{
			GUILayout.BeginArea(blockRect, Styles.GUIStyles.LargeButtonArea);
			GUILayout.FlexibleSpace();

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			BlockData blockData = block as BlockData;
			if (canBeAdded)
			{
				TextureContent addIcon = block is BlockDownloaderData
					? Styles.Contents.DownloadIcon
					: Styles.Contents.AddIcon;
				new ActionLinkDescription
				{
					Content = new GUIContent(addIcon),
					Style = Styles.GUIStyles.LargeButton,
					Action = () =>
						block.AddToProject(null, block.RequireListRefreshAfterInstall ? RefreshBlockList : null),
					ActionData = block,
					Origin = Origins.BlockGrid,
					OriginData = null
				}.Draw();
			}
			else if (ShouldShowMissingPackageDependencies(blockData))
			{
				new ActionLinkDescription
				{
					Content = new GUIContent(Styles.Contents.DownloadPackageDependenciesIcon),
					Style = Styles.GUIStyles.LargeButton,
					Action = () => MissingPackageDependenciesPopup(blockData),
					ActionData = blockData,
					Origin = Origins.BlockGrid,
					OriginData = null
				}.Draw();
			}

			if (canBeSelected)
				new ActionLinkDescription
				{
					Content = new GUIContent(Styles.Contents.SelectIcon),
					Style = Styles.GUIStyles.LargeButton,
					Action = () => blockData.SelectBlocksInScene(),
					ActionData = blockData,
					Origin = Origins.BlockGrid,
					OriginData = null
				}.Draw();

			EditorGUILayout.EndHorizontal();
			GUILayout.EndArea();
		}

		private static void MissingPackageDependenciesPopup(BlockData blockData)
		{
			StringBuilder message = new();
			message.Append(
				$"In order to install {blockData.BlockName} the following packages are required:\n\n");

			foreach (string packageId in blockData.GetMissingPackageDependencies)
				if (CustomPackageDependencyRegistry.IsPackageDepInCustomRegistry(packageId))
				{
					CustomPackageDependencyInfo packageDepInfo =
						CustomPackageDependencyRegistry.GetPackageDepInfo(packageId);
					message.Append(
						$"- {packageDepInfo.PackageDisplayName}: {packageDepInfo.InstallationInstructions}\n");
				}
				else
				{
					message.Append($"- {packageId}\n");
				}

			EditorUtility.DisplayDialog("Package Dependencies Required", message.ToString(), "Ok");
		}

		private void ShowDescription(BlockBaseData block, Rect blockRect,
			int expectedThumbnailWidth, int expectedThumbnailHeight, bool canBeAdded, bool canBeSelected)
		{
			GUIStyle descriptionStyle = Styles.GUIStyles.DescriptionAreaStyle;
			Rect descriptionArea = EditorGUILayout.BeginVertical(descriptionStyle);
			{
				bool hoverDescription = HoverHelper.IsHover(block.Id + "Description");
				Color expectedColor = hoverDescription ? DarkGrayHover : DarkGray;
				GUI.DrawTexture(descriptionArea, expectedColor.ToTexture(), ScaleMode.ScaleAndCrop, false, 1, GUI.color,
					Vector4.zero, Styles.Constants.LowerRoundedBorderVectors);
				EditorGUILayout.BeginHorizontal();
				{
					Rect descriptionRect = blockRect;
					descriptionRect.y += expectedThumbnailHeight + MiniPadding;
					descriptionRect.height = ItemHeight;
					int numberOfIcons = 0;
					if (canBeAdded) numberOfIcons++;
					if (canBeSelected) numberOfIcons++;
					float iconWidth = Styles.GUIStyles.LargeButton.fixedWidth +
					                  Styles.GUIStyles.LargeButton.margin.horizontal;
					int padding = descriptionStyle.padding.horizontal;
					GUIStyle style = new(Styles.GUIStyles.DescriptionPaddingStyle)
					{
						fixedWidth = expectedThumbnailWidth - padding - numberOfIcons * iconWidth
					};
					EditorGUILayout.BeginVertical(style);
					{
						GUIStyle labelStyle = new(hoverDescription
							? Styles.GUIStyles.BlockLabelHoverGridStyle
							: Styles.GUIStyles.BlockLabelGridStyle);
						// This logic replaces an EditorGUILayout.LabelField which enforces a height of 18f (hardcoded)
						Rect rect = EditorGUILayout.GetControlRect(false, DoubleMargin, labelStyle);
						EditorGUI.LabelField(rect, block.BlockName, labelStyle);
						CommonUIHelpers.DrawList(block.Id, block.Tags, Tag.TagListType.Filters,
							style.fixedWidth, TagSearch, OnSelectTag);
					}
					EditorGUILayout.EndVertical();
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();
		}

		private static void ShowDragAndDrop(BlockBaseData block, Rect blockRect, bool canBeAdded)
		{
			bool hoverGrid = HoverHelper.IsHover(block.Id + "Grid", Event.current, blockRect);
			if (!canBeAdded) return;

			if (!hoverGrid) return;

			switch (Event.current.type)
			{
				case EventType.Repaint:
					EditorGUIUtility.AddCursorRect(blockRect, MouseCursor.Pan);
					break;
				case EventType.MouseDown:
					SetDragAndDrop(block);
					break;
			}
		}

		private static void DrawEmptyGridItem(int expectedThumbnailWidth, int expectedThumbnailHeight)
		{
			// Early return with empty grid item
			GUIStyle emptyGridStyle = new(Styles.GUIStyles.GridItemStyleWithHover)
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
			BlocksWindowCache.BlockDataCache blockDataCache = block.GetCache();
			if (Event.current.type == EventType.Layout) blockDataCache.Reset();

			bool canBeAdded = blockDataCache.IsInteractable;
			int numberInScene = blockDataCache.NumberOfBlocksInScene;

			GUIStyle gridItemStyle = new(Styles.GUIStyles.GridItemStyleWithHover)
			{
				fixedWidth = expectedThumbnailWidth,
				fixedHeight = expectedThumbnailHeight + Styles.GUIStyles.DescriptionAreaStyle.fixedHeight + 3
			};

			Color expectedColor = canBeAdded ? Color.white : Styles.Colors.DisabledColor;

			Rect grid = EditorGUILayout.BeginVertical(gridItemStyle);
			bool isHover = HoverHelper.IsHover(block.Id + "Description", Event.current, grid);
			Color hoverExpectedColor = isHover ? Styles.Colors.AccentColor : CharcoalGray;
			GUI.DrawTexture(grid, hoverExpectedColor.ToTexture(), ScaleMode.ScaleAndCrop,
				false, 1f, GUI.color, Vector4.zero, Styles.Constants.RoundedBorderVectors);

			using ColorScope color = new(ColorScope.Scope.All, expectedColor);

			ShowThumbnail(block, expectedThumbnailWidth, expectedThumbnailHeight);

			bool canBeSelected = numberInScene > 0;
			ShowDescription(block, blockRect, expectedThumbnailWidth, expectedThumbnailHeight, canBeAdded,
				canBeSelected);

			ShowBlockInstallButton(block, blockRect, canBeAdded, canBeSelected);

			ShowDragAndDrop(block, blockRect, canBeAdded);
			EditorGUILayout.EndVertical();

			if (isHover) block.MarkAsSeen();

			if (!canBeAdded && GUI.Button(blockRect, "", GUIStyle.none))
				SwitchToPage(Page.Details, Origins.BlockGrid, block, (BlockData)block);
		}

		private void Show(BlockBaseData block, Rect blockRect, bool isVisibleInScrollView, int expectedThumbnailWidth,
			int expectedThumbnailHeight)
		{
			if (!isVisibleInScrollView)
			{
				DrawEmptyGridItem(expectedThumbnailWidth, expectedThumbnailHeight);
				return;
			}

			DrawBlockGridItem(block, blockRect, expectedThumbnailWidth, expectedThumbnailHeight);
		}

		private static bool ShouldShowTutorial()
		{
			_shouldShowTutorial = !TutorialCompleted.Value;
			return _shouldShowTutorial;
		}

		private void ShowTutorial(Rect dragArea)
		{
			if (_outline == null &&
			    TextureContent.BuildPath("bb_outline.asset", Utils.BuildingBlocksAnimations, out string outlinePath))
				_outline = AssetDatabase.LoadAssetAtPath<AnimatedContent>(outlinePath);

			if (_outline != null)
			{
				_outline.Update();
				GUI.DrawTexture(dragArea, _outline.CurrentFrame);
			}

			if (_tutorial == null && TextureContent.BuildPath("bb_tutorial.asset", Utils.BuildingBlocksAnimations,
				    out string tutorialPath))
				_tutorial = AssetDatabase.LoadAssetAtPath<AnimatedContent>(tutorialPath);

			if (_tutorial != null)
			{
				_tutorial.Update();
				GUI.DrawTexture(dragArea, _tutorial.CurrentFrame);
			}

			_repainter.RequestRepaint();
		}

		private void DrawDragAndDrop(Dimensions dimensions)
		{
			if (!DragAndDropStarted) return;

			Texture2D blockThumbnail = DragAndDrop.GetGenericData(DragAndDropBlockThumbnailLabel) as Texture2D;
			Vector2 startMousePosition =
				(Vector2)(DragAndDrop.GetGenericData(DragAndDropStartMousePosition) ?? Vector2.zero);
			float dragDistance = Vector2.Distance(CurrentMousePosition, startMousePosition);
			if (blockThumbnail && dragDistance >= BlockDragStartThreshold)
			{
				Vector2 cursorOffset = new(dimensions.ExpectedThumbnailWidth / 2.0f,
					dimensions.ExpectedThumbnailHeight / 2.0f);
				Rect cursorRect = new(Event.current.mousePosition - cursorOffset,
					new Vector2(dimensions.ExpectedThumbnailWidth, dimensions.ExpectedThumbnailHeight));
				GUI.color = new Color(1, 1, 1, Styles.Constants.DragOpacity);
				GUI.DrawTexture(cursorRect, blockThumbnail, ScaleMode.ScaleAndCrop, false,
					Styles.Constants.ThumbnailSourceRatio, GUI.color, Vector4.zero,
					Styles.Constants.RoundedBorderVectors);
				GUI.color = Color.white;
			}

			_repainter.RequestRepaint();
		}

		private bool HandleMouseEvents()
		{
			bool needsRepaint = false;

			Event currentEvent = Event.current;

			switch (currentEvent.type)
			{
				case EventType.MouseMove:
				{
					if (DragAndDropStarted) ResetDragAndDrop();

					needsRepaint = true;
				}
					break;

				case EventType.DragUpdated:
				{
					if (!DragAndDropStarted) break;

					DragAndDrop.visualMode = DragAndDropVisualMode.Move;
					needsRepaint = true;

					currentEvent.Use();
				}
					break;

				case EventType.DragPerform:
				{
					if (!DragAndDropStarted) break;

					if (CurrentTargetPage != Page.Details)
					{
						Vector2 startMousePosition =
							(Vector2)(DragAndDrop.GetGenericData(DragAndDropStartMousePosition) ?? Vector2.zero);
						float dragDistance = Vector2.Distance(CurrentMousePosition, startMousePosition);
						if (dragDistance < BlockDragStartThreshold)
						{
							_selectedBlock = DragAndDrop.GetGenericData(DragAndDropBlockDataLabel) as BlockData;
							SwitchToPage(Page.Details, Origins.BlockGrid, _selectedBlock, _selectedBlock);
						}
					}

					ResetDragAndDrop();
					needsRepaint = true;

					currentEvent.Use();
				}
					break;
			}

			return needsRepaint;
		}

		private static DragAndDropVisualMode HierarchyDropHandler(
			int dropTargetInstanceID,
			HierarchyDropFlags dropMode,
			Transform parentForDraggedObjects,
			bool perform)
		{
			GameObject hoveredObject = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;
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
			BlockBaseData block = DragAndDrop.GetGenericData(DragAndDropBlockDataLabel) as BlockBaseData;

			if (block == null) return DragAndDropVisualMode.None;

			if (!perform) return DragAndDropVisualMode.Generic;

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
			TutorialCompleted.SetValue(true);
			_shouldShowTutorial = false;

			return DragAndDropVisualMode.Generic;
		}

		private static bool DragAndDropStarted => DragAndDrop.GetGenericData(DragAndDropBlockDataLabel) != null;

		private static void SetDragAndDrop(BlockBaseData block)
		{
			if (DragAndDropStarted) return;

			ResetDragAndDrop();

			DragAndDrop.PrepareStartDrag();
			DragAndDrop.SetGenericData(DragAndDropBlockDataLabel, block);
			DragAndDrop.SetGenericData(DragAndDropBlockThumbnailLabel, block.Thumbnail);
			DragAndDrop.SetGenericData(DragAndDropStartMousePosition, CurrentMousePosition);
			DragAndDrop.AddDropHandler(SceneDropHandler);
			DragAndDrop.AddDropHandler(HierarchyDropHandler);
			DragAndDrop.StartDrag(DragAndDropLabel);
		}

		private static void ResetDragAndDrop()
		{
			DragAndDrop.SetGenericData(DragAndDropBlockDataLabel, null);
			DragAndDrop.SetGenericData(DragAndDropBlockThumbnailLabel, null);
			DragAndDrop.SetGenericData(DragAndDropStartMousePosition, null);
			DragAndDrop.RemoveDropHandler(SceneDropHandler);
			DragAndDrop.RemoveDropHandler(HierarchyDropHandler);
		}

		private void OnSelectTag(Tag tag)
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

			UpdateCurrentBlockList();
			ReturnToGrid();
		}

		private static void UpdateCurrentBlockList()
		{
			_currentBlockList = SortBlocks(Filter(_blockList));
		}
	}
}