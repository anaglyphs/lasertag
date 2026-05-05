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
using System.Linq;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.Tags;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Telemetry;

namespace Meta.XR.BuildingBlocks.Editor
{
    public partial class BuildingBlocksWindow
    {
        private class SessionStateCollection : CustomSetting<CollectionTagBehavior>
        {
            private bool _hasBeenFetched;
            private CollectionTagBehavior _savedCollection;

            public override CollectionTagBehavior Value
            {
                get
                {
                    if (_hasBeenFetched)
                    {
                        return _savedCollection;
                    }

                    var collectionName = SessionState.GetString(Key, string.Empty);
                    _hasBeenFetched = true;
                    if (BlocksContentManager.RemoteCollectionTags.TryGetValue(collectionName, out var collectionTag))
                    {
                        _hasBeenFetched = true;
                        _savedCollection = collectionTag.Behavior as CollectionTagBehavior;
                    }
                    else
                    {
                        _savedCollection = null;
                    }

                    return _savedCollection;
                }

                protected set
                {
                    _hasBeenFetched = true;
                    _savedCollection = value;
                    SessionState.SetString(Key, _savedCollection?.Tag ?? string.Empty);
                }
            }

            public override bool Equals(CollectionTagBehavior lhs, CollectionTagBehavior rhs) => lhs == rhs;
        }

        internal enum Page
        {
            Collections = 0,
            Grid = 1,
            Details = 2
        }

        private Vector2 _collectionsPageScrollPosition;
        private bool _isSwitchToNextPageCompleted;

        private Page CurrentTargetPage { get; set; } = Page.Collections;

        private bool IsPageVisible(Page page)
        {
            var pagePosition = GetPageStartPosition(page);
            return _horizontalPageRatio >= (pagePosition - 0.99f)
                   && _horizontalPageRatio <= (pagePosition + 0.99f);
        }

        private static readonly SessionStateCollection SelectedCollection = new()
        {
            Owner = Utils.ToolDescriptor,
            SendTelemetry = false,
            Uid = "SelectedCollection",
        };

        private bool _disabledCollectionPage;
        private static IReadOnlyList<Tag> _collectionTags;
        private static Dictionary<Tag, int> _collectionBlockCounts;

        private static bool ValidCollections => _collectionTags != null && _collectionTags.Any() &&
                                                _collectionTags.Contains(CustomTagBehaviors
                                                    .AllBuildingBlocksCollection);

        private static readonly Setting<bool> SkipCollectionOnStart = new UserBool
        {
            Owner = Utils.ToolDescriptor,
            Uid = "SkipCollectionOnStart",
            Default = false,
            SendTelemetry = true,
            Label = "Skip Collection Page on Start",
            Tooltip =
                "Whether or not Building Blocks directly open on the grid view, skipping the Collection page by default."
        };

        private static void RefreshCollectionTags()
        {
            _collectionTags = BlocksContentManager.RemoteCollectionTags.ToList();
            RefreshCollectionBlockCounts();
            ReturnToCollections(); // Refreshes the variables to set to current page
        }

        private static void RefreshCollectionBlockCounts()
        {
            _collectionBlockCounts = new Dictionary<Tag, int>();
            if (_collectionTags == null) return;

            foreach (var tag in _collectionTags)
            {
                if (tag == CustomTagBehaviors.AllBuildingBlocksCollection) continue;

                var collectionTagBehavior = TagBehavior.Registry[tag] as CollectionTagBehavior;
                if (collectionTagBehavior != null)
                {
                    _collectionBlockCounts[tag] = BlocksContentManager.GetCollection(collectionTagBehavior).Count();
                }
            }
        }

        private Tween PageTransitionTween => Tween.Fetch(Utils.ToolDescriptor, UpdatePageTransition);

        private void UpdatePageTransition(float value)
        {
            _horizontalPageRatio = value;
            _repainter.RequestRepaint(force: true);
        }

        private void ShowCollectionsPage(Dimensions dimensions)
        {
            if (!ValidCollections) return;

            EditorGUILayout.BeginVertical(Styles.GUIStyles.CollectionsPageStyle,
                GUILayout.Width(dimensions.WindowWidth));

            var contentWidth = dimensions.WindowWidth - LargeMargin;
            _collectionsPageScrollPosition = EditorGUILayout.BeginScrollView(_collectionsPageScrollPosition, false,
                false,
                GUIStyle.none, GUI.skin.verticalScrollbar, Styles.GUIStyles.NoMargin, GUILayout.Width(contentWidth));

            EditorGUILayout.LabelField("Collections", Styles.GUIStyles.LargeLabelStyleFullWhite);
            EditorGUILayout.LabelField(
                "Pick one of our curated <b>Collections of Building Blocks</b> to get the most useful features that will help you bring your idea to life.",
                Styles.GUIStyles.SmallLabelStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Or browse ", Styles.GUIStyles.SmallLabelStyle);
            new ActionLinkDescription
            {
                Content = new GUIContent(CustomTagBehaviors.AllBuildingBlocksCollection),
                Style = Styles.GUIStyles.SmallInlineLinkLabelStyle,
                Action = () =>
                    SwitchToPage(Page.Grid, Origins.BlockCollectionPage, Utils.ToolDescriptor),
                ActionData = CustomTagBehaviors.AllBuildingBlocksCollection,
                Origin = Origins.BlockCollectionPage,
                OriginData = Utils.ToolDescriptor
            }.Draw();
            EditorGUILayout.LabelField(".", Styles.GUIStyles.SmallLabelStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            var tags = CustomTagBehaviors.CollectionTags.ToArray();
            var numberOfColumn = dimensions.CollectionNumberOfColumn;
            var numberOfLines = (float)_collectionTags.Count / numberOfColumn;
            var tagIndex = 0;

            for (int i = 0; i < numberOfLines; i++)
            {
                EditorGUILayout.BeginVertical();
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < numberOfColumn; j++)
                {
                    var tag = _collectionTags[tagIndex++];
                    CollectionCard(tag, dimensions);

                    if (tagIndex == _collectionTags.Count)
                        break;

                    EditorGUILayout.Space(DoubleMargin, false);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(DoubleMargin);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void CollectionCard(Tag tag, Dimensions dimensions)
        {
            var id = "Collection" + tag.Name;
            var gridItemStyle = new GUIStyle(Styles.GUIStyles.CollectionCardItemStyleWithHover)
            {
                fixedWidth = dimensions.ExpectedCollectionThumbnailWidth,
                fixedHeight = dimensions.ExpectedCollectionThumbnailHeight
            };
            var collectionTagBehavior = (CollectionTagBehavior)TagBehavior.Registry[tag];
            var gridRect = EditorGUILayout.BeginVertical(gridItemStyle);

            var isHover = HoverHelper.IsHover(id + "_hover", Event.current, gridRect);
            var hoverExpectedColor = isHover ? Styles.Colors.ComplementaryColor : CharcoalGray;

            var tween = Tween.Fetch(tag, ZoomIn);
            var expectedTarget = isHover
                ? Styles.Constants.CollectionThumbnailZoomIn
                : Styles.Constants.CollectionThumbnailZoomOut;
            if (Math.Abs(tween.Target - expectedTarget) > Styles.Constants.CollectionThumbnailZoomingEpsilon)
            {
                tween.Start = Styles.Constants.CollectionThumbnailZoomOut;
                tween.Speed = Styles.Constants.CollectionThumbnailZoomingSpeed;
                tween.Epsilon = Styles.Constants.CollectionThumbnailZoomingEpsilon;
                tween.Target = expectedTarget;
                tween.Activate();
            }

            var thumbnailHeight = dimensions.ExpectedCollectionThumbnailWidth /
                                  Styles.Constants.CollectionThumbnailDivRatio;
            ShowCollectionThumbnail(collectionTagBehavior, thumbnailHeight, tween.Current);

            var descriptionRect = EditorGUILayout.BeginVertical(Styles.GUIStyles.CollectionDescriptionAreaStyle,
                GUILayout.Height(dimensions.ExpectedCollectionThumbnailHeight - thumbnailHeight));
            descriptionRect.height = dimensions.ExpectedCollectionThumbnailHeight - thumbnailHeight - 2;
            GUI.DrawTexture(descriptionRect, CharcoalGraySemiTransparent.ToTexture(), ScaleMode.ScaleAndCrop, false, 1f,
                GUI.color, Vector4.zero, Styles.Constants.LowerRoundedBorderVectors);
            GUI.DrawTexture(gridRect, hoverExpectedColor.ToTexture(), ScaleMode.ScaleAndCrop, false, 1f, GUI.color,
                Vector4.one * 1.5f, Styles.Constants.RoundedBorderVectors);

            var colorOverride = isHover ? Color.white : CollectionTagsColor;
            using (new XR.Editor.UserInterface.Utils.ColorScope(
                       XR.Editor.UserInterface.Utils.ColorScope.Scope.Content, colorOverride))
            {
                GUILayout.Label(collectionTagBehavior.Tag, Styles.GUIStyles.LargeLabelStyleFullWhite);
            }

            var showAll = tag == CustomTagBehaviors.AllBuildingBlocksCollection;
            var blockCount = showAll
                ? _blockList.Count
                : (_collectionBlockCounts != null && _collectionBlockCounts.TryGetValue(tag, out var cachedCount) ? cachedCount : 0);
            var subLabelText = showAll ? $"{blockCount} blocks" : $"{blockCount} recommended blocks";
            GUILayout.Label(subLabelText, Styles.GUIStyles.CollectionAreaStatusStyle);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(collectionTagBehavior.Description, Styles.GUIStyles.SmallLabelStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();

            if (CollectionButton(gridRect, id))
            {
                if (!showAll) SelectedCollection.SetValue(collectionTagBehavior);
                RefreshBlockList();
                SwitchToPage(Page.Grid, Origins.BlockCollectionPage, null);
            }
        }

        private void ZoomIn(float value)
        {
            _repainter.RequestRepaint(force: true);
        }

        private bool CollectionButton(Rect rect, string controlId)
        {
            var previousColor = GUI.color;
            GUI.color = Color.white;
            var id = controlId;
            var hit = HoverHelper.Button(id, rect, new GUIContent(), GUIStyle.none, out _);
            GUI.color = previousColor;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return hit;
        }

        private static void ShowCollectionThumbnail(CollectionTagBehavior behavior, float targetHeight, float zoomIn)
        {
            var thumbnailAreaStyle = new GUIStyle(Styles.GUIStyles.ThumbnailAreaStyle)
            {
                fixedHeight = targetHeight
            };
            var thumbnailArea =
                EditorGUILayout.BeginVertical(thumbnailAreaStyle, GUILayout.Height(thumbnailAreaStyle.fixedHeight));
            {
                if (Event.current.type == EventType.Repaint)
                {
                    var clipDimensions = thumbnailArea.size;
                    GUI.BeginClip(thumbnailArea, Vector2.zero, Vector2.zero, false);

                    var scaledWidth = Mathf.Round(clipDimensions.x * zoomIn);
                    var scaledHeight = Mathf.Round(clipDimensions.y * zoomIn);
                    var xMin = -(scaledWidth - clipDimensions.x) / 2.0f;
                    var yMin = -(scaledHeight - clipDimensions.y) / 2.0f;
                    thumbnailArea.yMin = yMin;
                    thumbnailArea.height = scaledHeight;
                    thumbnailArea.xMin = xMin;
                    thumbnailArea.width = scaledWidth;
                    GUI.DrawTexture(thumbnailArea, behavior.Thumbnail.Image, ScaleMode.ScaleAndCrop, false,
                        Styles.Constants.CollectionThumbnailRatio, GUI.color, Vector4.zero,
                        Styles.Constants.UpperRoundedBorderVectors);
                    GUI.EndClip();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        private static void ReturnToCollections()
        {
            if (_instance != null)
            {
                _instance.ReturnToCollections(GetOrigin(_instance.CurrentTargetPage), _selectedBlock);
            }
        }

        private void ReturnToCollections(Origins origin, IIdentified originData)
        {
            SwitchToPage(Page.Collections, origin, originData);
            _backHistory.Clear();
        }

        private void SwitchToPage(Page targetPage, Origins origin, IIdentified originData, BlockData blockData = null)
        {
            if (targetPage == CurrentTargetPage) return;
            switch (targetPage)
            {
                case Page.Details:
                    PrepareTransitToDetailPage(blockData, origin, originData);
                    break;
                case Page.Collections:
                    ClearTagSearch();
                    ResetCollectionState();
                    ResetCurrentBlockList();
                    break;
                case Page.Grid:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(targetPage), targetPage, null);
            }

            TriggerPageTransition(targetPage, instant: false);

            // Transitioning from detail page to grid
            if (CurrentTargetPage == Page.Details && targetPage == Page.Grid)
            {
                var unifiedEvent = new OVRPlugin.UnifiedEventData(FalcoEventName.PageClose)
                {
                    isEssential = OVRPlugin.Bool.False,
                    productType = OVRPlugin.ProductType.Editor
                };
                unifiedEvent.SetMetadata(AnnotationType.Origin, origin.ToString());
                unifiedEvent.SetMetadata(AnnotationType.OriginData, originData.Id);
                unifiedEvent.SetMetadata(AnnotationType.Action, Origins.BlockDetails.ToString());
                unifiedEvent.SetMetadata(AnnotationType.ActionData, _selectedBlock?.Id);
                unifiedEvent.SetMetadata(AnnotationType.ActionType, GetType().Name);
                unifiedEvent.Send();
            }

            // Transitioning from collections page to grid
            if (CurrentTargetPage == Page.Collections && targetPage == Page.Grid)
            {
                var unifiedEvent = new OVRPlugin.UnifiedEventData(FalcoEventName.PageOpen)
                {
                    isEssential = OVRPlugin.Bool.True,
                    productType = OVRPlugin.ProductType.Editor
                };
                unifiedEvent.SetMetadata(AnnotationType.Origin, origin.ToString());
                unifiedEvent.SetMetadata(AnnotationType.Action, Origins.BlockGrid.ToString());
                unifiedEvent.SetMetadata(AnnotationType.ActionData, string.Empty);
                unifiedEvent.SetMetadata(AnnotationType.ActionType, GetType().Name);
                unifiedEvent.Send();
            }
        }

        private void PrepareTransitToDetailPage(BlockData blockData, Origins origin, IIdentified originData)
        {
            if (blockData == null)
                throw new Exception($"[{nameof(BuildingBlocksWindow)}] Target block cannot be null");

            _backHistory.Push(blockData);
            _selectedBlock = blockData;
            _variantInitialized = false;

            var unifiedEvent = new OVRPlugin.UnifiedEventData(FalcoEventName.PageOpen)
            {
                isEssential = OVRPlugin.Bool.True,
                productType = OVRPlugin.ProductType.Editor
            };
            unifiedEvent.SetMetadata(AnnotationType.Origin, origin.ToString());
            unifiedEvent.SetMetadata(AnnotationType.OriginData, originData.Id);
            unifiedEvent.SetMetadata(AnnotationType.Action, Origins.BlockDetails.ToString());
            unifiedEvent.SetMetadata(AnnotationType.ActionData, _selectedBlock.Id);
            unifiedEvent.SetMetadata(AnnotationType.ActionType, GetType().Name);
            unifiedEvent.Send();
        }

        private void TriggerPageTransition(Page targetPage, bool instant)
        {
            var tween = PageTransitionTween;
            tween.Start = GetPageContentScrollPosition(CurrentTargetPage);
            CurrentTargetPage = targetPage;
            tween.Speed = 20.0f;
            tween.Epsilon = 0.001f;
            tween.Target = GetPageContentScrollPosition(CurrentTargetPage);
            if (instant)
            {
                tween.Start = tween.Target;
            }

            tween.Activate();
        }

        private static void DrawBackToCollectionIcon()
        {
            if (!ValidCollections) return;

            new ActionLinkDescription
            {
                Content = new GUIContent(Styles.Contents.CollectionIcon.Image, "Back to Collections"),
                Style = Styles.GUIStyles.LargeButton,
                Color = BrightGray,
                Action = ReturnToCollections,
                ActionData = null,
                Origin = Origins.BlockGrid,
                OriginData = Utils.ToolDescriptor
            }.Draw();
        }

        private static float GetPageContentScrollPosition(Page page)
        {
            if (!ValidCollections && page == Page.Collections)
            {
                page = Page.Grid;
            }

            var multiplier = page switch
            {
                Page.Collections => 0,
                Page.Grid => 1,
                Page.Details => 1 + DetailPaneShowAmount,
                _ => 0
            };

            if (!ValidCollections)
            {
                multiplier -= 1;
            }

            return multiplier;
        }

        private static float GetPageStartPosition(Page page)
        {
            if (!ValidCollections && page == Page.Collections)
                page = Page.Grid;

            var multiplier = page switch
            {
                Page.Collections => 0,
                Page.Grid => 1,
                Page.Details => 2,
                _ => 0
            };

            if (!ValidCollections) multiplier -= 1;

            return multiplier;
        }

        private static Origins GetOrigin(Page page) => page switch
        {
            Page.Collections => Origins.BlockCollectionPage,
            Page.Grid => Origins.BlockGrid,
            Page.Details => Origins.BlockDetails,
            _ => Origins.Unknown
        };

        private void DrawSelectedCollectionTag()
        {
            var selectedCollection = SelectedCollection.Value;
            if (selectedCollection == null) return;
            selectedCollection.Draw($"collection_{SelectedCollection}", Tag.TagListType.Filters, true, out _,
                out var collectionTagCloseClicked);
            if (collectionTagCloseClicked)
            {
                ResetCollectionState();
            }
        }

        private void ResetCollectionState()
        {
            SelectedCollection.Reset();
            RefreshBlockList();
        }

        private static void ResetCurrentBlockList()
        {
            _currentBlockList = SortBlocks(Filter(_blockList));
        }
    }
}
