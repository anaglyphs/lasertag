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
using Meta.XR.Guides.Editor.Items;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.Guides.Editor
{
    internal class GuideWindow : EditorWindow
    {
        public string Id => GetHashCode().ToString();

        private string _title = "Meta XR Guide";
        private string _description = "Description placeholder.";
        private bool _dontShowToggleState;
        private bool _isVisible;

        public Action OnWindowFocus;
        public Action OnWindowLostFocus;
        public Action OnWindowDraw;
        public Action OnWindowDestroy;

        private string DontShowAgainPrefKey => GetHashCode() + "ShowAgain";

        [SerializeField] private GuideOptions _guideOptions;
        private string _itemPopulatorId;

        private List<IGuideItem> Items { get; set; }

        [Serializable]
        public struct GuideOptions
        {
            public bool ShowCloseButton;
            public bool ShowDontShowAgainOption;
            public int MinWindowWidth;
            public int MaxWindowWidth;
            public int MinWindowHeight;
            public int MaxWindowHeight;

            public GuideOptions(GuideOptions options)
            {
                ShowCloseButton = options.ShowCloseButton;
                ShowDontShowAgainOption = options.ShowDontShowAgainOption;
                MinWindowWidth = options.MinWindowWidth;
                MaxWindowWidth = options.MaxWindowWidth;
                MinWindowHeight = options.MinWindowHeight;
                MaxWindowHeight = options.MaxWindowHeight;
            }
        }

        public static readonly GuideOptions DefaultOptions = new()
        {
            ShowCloseButton = true,
            ShowDontShowAgainOption = true,
            MinWindowWidth = GuideStyles.Constants.DefaultWidth,
            MaxWindowWidth = GuideStyles.Constants.DefaultWidth,
            MinWindowHeight = GuideStyles.Constants.DefaultHeight,
            MaxWindowHeight = GuideStyles.Constants.DefaultHeight
        };

        public void Setup(string title, string description, string itemPopulatorId, GuideOptions guideOptions)
        {
            _guideOptions = guideOptions;
            _itemPopulatorId = itemPopulatorId;
            SetupWindow(title, description);
            _dontShowToggleState = DontShowAgainPref();
        }

        public virtual void SetupWindow(
            string windowTitle,
            string description,
            int minSizeW = GuideStyles.Constants.DefaultWidth,
            int maxSizeW = GuideStyles.Constants.DefaultWidth,
            int minSizeH = GuideStyles.Constants.DefaultHeight,
            int maxSizeH = GuideStyles.Constants.DefaultHeight)
        {
            _title = windowTitle;
            _description = description;
            name = windowTitle;
            titleContent = new GUIContent(windowTitle);
            minSize = new Vector2(minSizeW, minSizeH);
            maxSize = new Vector2(maxSizeW, maxSizeH);
        }

        internal new void Show(bool ignoreDontShowAgainFlag = false)
        {
            if (Application.isBatchMode) return;
            if ((ignoreDontShowAgainFlag || !_guideOptions.ShowDontShowAgainOption || !DontShowAgainPref()) && !_isVisible)
            {
                base.Show();
            }
        }

        private bool DontShowAgainPref() => EditorPrefs.GetBool(DontShowAgainPrefKey, false);

        internal void OnGUI()
        {
            Items ??= GuideProcessor.GetItems(_itemPopulatorId);
            if (Items == null) return;

            DrawHeader();

            XRGuideBeginVertical();
            BeforeItemDraw();

            foreach (var item in Items)
            {
                if (item == null || item.Hide) continue;
                item.Draw();
            }

            AfterItemDraw();
            XRGuideEndVertical();

            DrawFooters();
            OnWindowDraw?.Invoke();
        }

        private void OnBecameVisible()
        {
            Guide.GuideWindowIntances.TryAdd(GetHashCode(), this);
            _isVisible = true;
        }

        private void OnBecameInvisible()
        {
            EditorPrefs.SetBool(DontShowAgainPrefKey, _guideOptions.ShowDontShowAgainOption && _dontShowToggleState);
            _isVisible = false;
        }

        private void OnFocus() => OnWindowFocus?.Invoke();
        private void OnLostFocus() => OnWindowLostFocus?.Invoke();
        private void OnDestroy()
        {
            OnWindowDestroy?.Invoke();
            Guide.GuideWindowIntances.Remove(GetHashCode());
        }

        public override int GetHashCode() => Guide.GetGuideHash(_title, _description);

        private void DrawHeader()
        {
            var titleGuiContent = new GUIContent(_title);
            var expectedHeight = 84;
            var rect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, expectedHeight);
            GUI.DrawTexture(rect, GuideStyles.Contents.BannerImage.GUIContent.image, ScaleMode.ScaleAndCrop);

            GUILayout.BeginArea(new Rect(Margin, LargeMargin, EditorGUIUtility.currentViewWidth, expectedHeight));

            EditorGUILayout.BeginHorizontal(GuideStyles.GUIStyles.Header);
            using (new ColorScope(ColorScope.Scope.Content, OffWhite))
            {
                EditorGUILayout.LabelField(GuideStyles.Contents.HeaderIcon, GuideStyles.GUIStyles.HeaderIconStyle, GUILayout.Width(32.0f),
                    GUILayout.ExpandWidth(false));
            }

            EditorGUILayout.LabelField(titleGuiContent, GuideStyles.GUIStyles.HeaderBoldLabel);
            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_description, GuideStyles.GUIStyles.SubtitleLabel, GUILayout.Width(position.width - LargeMargin));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooters()
        {
            if (!_guideOptions.ShowCloseButton && !_guideOptions.ShowDontShowAgainOption) return;

            var systemDefaultPaddingLeft = 3f;
            var rect = new Rect(
                LargeMargin + systemDefaultPaddingLeft,
                position.height - LargeMargin * 2,
                EditorGUIUtility.currentViewWidth - LargeMargin * 2 - systemDefaultPaddingLeft * 2,
                LargeMargin * 2);

            XRGuideBeginVertical();
            GUILayout.BeginArea(rect);

            if (_guideOptions.ShowCloseButton)
            {
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
            }

            if (_guideOptions.ShowDontShowAgainOption)
            {
                EditorGUILayout.Space(4f);
                _dontShowToggleState = EditorGUILayout.ToggleLeft("Don't show this again.", _dontShowToggleState);
            }

            GUILayout.EndArea();
            XRGuideEndVertical();
        }

        /// <summary>
        /// Adds custom GUI elements before drawing <see cref="IGuideItem"/>(s).
        /// </summary>
        protected virtual void BeforeItemDraw()
        {
        }

        /// <summary>
        /// Adds custom GUI elements after drawing <see cref="IGuideItem"/>(s).
        /// </summary>
        protected virtual void AfterItemDraw()
        {
        }

        internal Rect XRGuideBeginVertical() => EditorGUILayout.BeginVertical(GuideStyles.GUIStyles.ContentMargin);
        internal void XRGuideEndVertical() => EditorGUILayout.EndVertical();
    }
}
