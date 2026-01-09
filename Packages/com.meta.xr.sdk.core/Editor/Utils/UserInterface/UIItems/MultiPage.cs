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
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class MultiPage : IUserInterfaceItem
    {
        public IIdentified Owner { get; }
        public Action<int> OnPageChangeBegin;
        public Action<int> OnPageChanged;
        public Vector2 ScrollPosition
        {
            get => _scrollPosition;
            set => _scrollPosition = value;
        }

        public List<Page> Pages { get; }

        private readonly GUILayoutOption[] _options;
        private Vector2 _scrollPosition;
        private int _currentPageIndex = -1;
        private readonly bool _hasDuplicatePageId;
        private readonly MultiPageOptions _multiPageOptions;

        internal static MultiPageOptions DefaultOptions = new()
        {
            Width = Styles.Constants.DefaultPageWidth,
            Height = Styles.Constants.DefaultPageHeight,
            ScrollHorizontally = true,
            ShowHorizontalScrollbar = false,
            ShowVerticalScrollbar = false,
            ShowNavigationButtons = true,
            ShowPageIndicator = true,
            PageIndicatorsFillStyle = false,
        };

        private Button _prevButton;
        private Button _nextButton;
        private readonly List<IUserInterfaceItem> _pageNavigationUIItems = new();
        private readonly List<IUserInterfaceItem> _pageIndicators = new();
        private GroupedItem _pageIndicatorsGroup;

        public Page CurrentPage
        {
            get
            {
                if (_currentPageIndex < 0 || _currentPageIndex > Pages.Count)
                {
                    return null;
                }

                return Pages[_currentPageIndex];
            }
        }

        private Button PrevButton => _prevButton ??= new(new()
        {
            Content = new GUIContent("Previous"),
            Origin = Origins.GuidedSetup,
            Id = "PrevPageButton",
            Action = OnPrevButtonPress,
            OriginData = Owner
        })
        {
            Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.Button
        };

        private Button NextButton => _nextButton ??= new(new()
        {
            Content = new GUIContent("Next"),
            Origin = Origins.GuidedSetup,
            Id = "NextPageButton",
            Action = OnNextButtonPress,
            BackgroundColor = Styles.Colors.MetaMultiplierForButton,
            OriginData = Owner
        }, GUILayout.Width(56))
        {
            Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.Button
        };

        private int PageWidth => _multiPageOptions.Width;
        private int PageHeight => _multiPageOptions.Height;

        public MultiPage(IIdentified owner, List<Page> pages, MultiPageOptions multiPageOptions,
            params GUILayoutOption[] options)
        {
            Owner = owner;
            Pages = pages;
            _multiPageOptions = multiPageOptions;
            var list = new List<GUILayoutOption> {
                GUILayout.Width(_multiPageOptions.Width),
                GUILayout.Height(_multiPageOptions.Height)
            };
            list.AddRange(options);
            _options = list.ToArray();

            // validate that pages have unique id
            _hasDuplicatePageId = pages.Select(p => p.PageId).Distinct().Count() != pages.Count;
        }

        public void Draw()
        {
            if (_hasDuplicatePageId) return;

            EditorGUILayout.BeginVertical();
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition,
                false, false,
                _multiPageOptions.ShowHorizontalScrollbar ? GUI.skin.horizontalScrollbar : GUIStyle.none,
                _multiPageOptions.ShowVerticalScrollbar ? GUI.skin.verticalScrollbar : GUIStyle.none,
                Styles.GUIStyles.NoMargin, _options);

            if (_multiPageOptions.ScrollHorizontally)
            {
                EditorGUILayout.BeginHorizontal();
            }
            else
            {
                EditorGUILayout.BeginVertical();
            }

            if (_multiPageOptions.ScrollHorizontally)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Width(PageWidth));
            }
            else
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(PageHeight));
            }

            CurrentPage?.Draw();

            if (_multiPageOptions.ScrollHorizontally)
            {
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.EndVertical();
            }

            if (_multiPageOptions.ScrollHorizontally)
            {
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(Styles.Constants.DoubleMargin);
            DrawPageNavigationUIItems();
        }

        public bool HasCompletedCurrentAction()
        {
            return CurrentPage?.HasCompletedAction() ?? true;
        }

        private void DrawPageNavigationUIItems()
        {
            UpdatePageIndicator();
            _pageIndicatorsGroup = new(_pageIndicators, XR.Editor.UserInterface.Styles.GUIStyles.PageIndicatorGroup);
            _pageIndicatorsGroup.Hide = !_multiPageOptions.ShowPageIndicator;
            PrevButton.Hide = !_multiPageOptions.ShowNavigationButtons;
            NextButton.Hide = !_multiPageOptions.ShowNavigationButtons;

            PrevButton.Disable = _currentPageIndex == 0;
            NextButton.Disable = !HasCompletedCurrentAction();

            PrevButton.Invisible = _currentPageIndex == 0;
            NextButton.Action.BackgroundColor = NextButton.Disable ? Color.white : Styles.Colors.MetaMultiplierForButton;
            NextButton.Action.Content = new GUIContent(_currentPageIndex >= Pages.Count - 1 ? "Close" : "Next");

            PrevButton.Action.ActionData = CurrentPage;
            NextButton.Action.ActionData = CurrentPage;

            _pageNavigationUIItems.Clear();
            _pageNavigationUIItems.Add(PrevButton);
            _pageNavigationUIItems.Add(new AddSpace(true));
            _pageNavigationUIItems.Add(_pageIndicatorsGroup);
            _pageNavigationUIItems.Add(new AddSpace(true));
            _pageNavigationUIItems.Add(NextButton);
            new GroupedItem(_pageNavigationUIItems).Draw();
        }

        private void UpdatePageIndicator()
        {
            if (!_multiPageOptions.ShowPageIndicator) return;

            _pageIndicators.Clear();
            for (var i = 0; i < Pages.Count; i++)
            {
                var selectedCondition = _currentPageIndex == i;
                var fillCondition = i <= _currentPageIndex;
                var status = (_multiPageOptions.PageIndicatorsFillStyle ? fillCondition : selectedCondition)
                    ? Styles.Colors.SelectedWhite
                    : Styles.Colors.Grey60;
                _pageIndicators.Add(new Icon(Styles.Contents.BulletIcon, status,
                    XR.Editor.UserInterface.Styles.GUIStyles.PageIndicatorIcon,
                    GUILayout.Width(15), GUILayout.Height(28)));
            }
        }

        private void OnNextButtonPress() => JumpToPage(_currentPageIndex + 1);
        private void OnPrevButtonPress() => JumpToPage(_currentPageIndex - 1);

        public void JumpToPage(int index)
        {
            if (CurrentPage != null)
            {
                var closeMarker = OVRTelemetry.Start(XR.Editor.UserInterface.Telemetry.MarkerId.PageClose);
                closeMarker = AddTelemetryAnnotations(closeMarker);
                closeMarker.Send();
            }

            OnPageChangeBegin?.Invoke(index);
            if (index < 0 || index >= Pages.Count)
                return;

            if (_multiPageOptions.ScrollHorizontally)
            {
                _scrollPosition.x = PageWidth * index;
            }
            else
            {
                _scrollPosition.y = PageHeight * index;
            }

            _currentPageIndex = index;
            OnPageChanged?.Invoke(index);

            if (CurrentPage != null)
            {
                var openMarker = OVRTelemetry.Start(XR.Editor.UserInterface.Telemetry.MarkerId.PageOpen);
                openMarker = AddTelemetryAnnotations(openMarker);
                openMarker.Send();
            }
        }

        private OVRTelemetryMarker AddTelemetryAnnotations(OVRTelemetryMarker marker)
        {
            marker = marker
                .AddAnnotation(XR.Editor.UserInterface.Telemetry.AnnotationType.Origin, Origins.GuidedSetup.ToString())
                .AddAnnotation(XR.Editor.UserInterface.Telemetry.AnnotationType.OriginData, Owner?.Id)
                .AddAnnotation(XR.Editor.UserInterface.Telemetry.AnnotationType.Action, Origins.GuidedSetup.ToString())
                .AddAnnotation(XR.Editor.UserInterface.Telemetry.AnnotationType.ActionData, CurrentPage?.Id)
                .AddAnnotation(XR.Editor.UserInterface.Telemetry.AnnotationType.ActionType, GetType().Name);
            return marker;
        }

        public void JumpToPage(string pageId) => JumpToPage(Pages.FindIndex(p => p.PageId == pageId));

        public bool Hide { get; set; }

        internal struct MultiPageOptions
        {
            public int Width;
            public int Height;
            public bool ScrollHorizontally;
            public bool ShowHorizontalScrollbar;
            public bool ShowVerticalScrollbar;
            public bool ShowNavigationButtons;
            public bool ShowPageIndicator;
            public bool PageIndicatorsFillStyle;
        }
    }
}
