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
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.StatusMenu;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.Utils;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Utils;
using static Meta.XR.Guides.Editor.About.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Contents;

namespace Meta.XR.Guides.Editor.About
{
    [InitializeOnLoad]
    internal static class LearnGuide
    {
        private static Learn _learnInstance;
        private static Learn LearnInstance => _learnInstance ??= new Learn();

        public static ToolDescriptor ToolDescriptor = new()
        {
            Order = -10,
            Icon = Meta.XR.Guides.Editor.GuideStyles.Contents.LearnIcon,
            Name = "Learn",
            MenuDescription = "Documentation & external tools",
            AddToStatusMenu = true,
            AddToMenu = false,
            OnClickDelegate = ShowGuide,
        };

        static LearnGuide()
        {
            // Static constructor for initialization if needed
        }

        public static void ShowGuide(Origins origin)
        {
            LearnInstance.ShowWindow(origin, true);
        }
    }

    [GuideItems]
    internal class Learn : GuidedSetup
    {
        private static GuideWindow _window;
        private Icon _metaIcon;

        // Reuse onboarding instance for card creation
        private static Onboarding _onboardingReference;
        private static Onboarding OnboardingReference => _onboardingReference ??= new Onboarding();

        private readonly Repainter _repainter = new();
        private TextureContent _leftPanelTexture = null;
        private TextureContent _leftPanelLastTexture = null;
        private TextureContent _leftPanelBottomTexture = null;
        private Tween _fader;

        internal override GuideWindow CreateWindow()
        {
            if (_window != null) return _window;

            const int width = Constants.Width;
            const int height = Constants.Height;
            var options = new GuideWindow.GuideOptions(GuideWindow.DefaultOptions)
            {
                ShowCloseButton = false,
                MinWindowWidth = width,
                MaxWindowWidth = width,
                MinWindowHeight = height,
                MaxWindowHeight = height,
                HeaderImage = null,
                ShowDontShowAgainOption = false,
                InvertDontShowAgain = true,
                ShowAsUtility = true,
            };
            _window = Guide.Create(LearnGuide.ToolDescriptor.Name, null, this, options);
            return _window;
        }

        [Init]
        private void InitializeWindow(GuideWindow window)
        {
            _window = window;
            _window.AddAdditionalTelemetryAnnotations = marker =>
                marker.AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.HasNewVersionAvailable,
                    About.Version < About.LatestVersion);
        }

        private void OnDraw() => _repainter.RequestRepaint();

        private void DrawHeader()
        {
        }

        private void DrawBefore()
        {
            _repainter.Assess(_window);

            if (_leftPanelTexture != null && _leftPanelTexture != _leftPanelLastTexture)
            {
                _leftPanelBottomTexture = _leftPanelLastTexture;
                _leftPanelLastTexture = _leftPanelTexture;
                ResetFader();
                _fader.Activate();
            }

            _leftPanelTexture = GuideStyles.Contents.ObResources; // Use Resources image

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            DrawLeftPanelTexture();
            EditorGUILayout.EndVertical();
        }

        private void DrawLeftPanelTexture()
        {
            var rect = GUILayoutUtility.GetRect(Constants.LeftPaneWidth, _window.minSize.y);
            if (_leftPanelBottomTexture?.Valid ?? false)
            {
                GUI.DrawTexture(rect, _leftPanelBottomTexture.Image, ScaleMode.ScaleAndCrop);
            }

            if (_leftPanelTexture?.Valid ?? false)
            {
                var color = new Color(1, 1, 1, _fader.Current);
                GUI.DrawTexture(rect, _leftPanelTexture.Image, ScaleMode.ScaleAndCrop,
                    true, 0.0f, color, Vector4.zero, Vector4.zero);
            }
        }

        private static void DrawAfter() => EditorGUILayout.EndHorizontal();

        private void ResetFader()
        {
            _fader?.Deactivate();
            _fader = Tween.Fetch(this);
            _fader.Reset();
            _fader.Start = 0;
            _fader.Target = 1f;
            _fader.Speed = 12.0f;
            _fader.Epsilon = 0.01f;
        }

        [GuideItems]
        private List<IUserInterfaceItem> GetItems()
        {
            ResetFader();
            _window.OnWindowDraw = OnDraw;
            _window.DrawBefore = DrawBefore;
            _window.DrawHeader = DrawHeader;
            _window.DrawAfter = DrawAfter;

            _metaIcon = new Icon(GuideStyles.Contents.HeaderIcon, Color.white, string.Empty,
                GUILayout.Width(UIStyles.GUIStyles.HeaderIconStyleLarge.fixedWidth),
                GUILayout.Height(UIStyles.GUIStyles.HeaderIconStyleLarge.fixedHeight));

            return BuildResourcesPageContent();
        }

        private GroupedItem Header(string title) => new(new List<IUserInterfaceItem>
        {
            _metaIcon,
            new Label(title, GUIStyles.HeaderBoldLabelLarge),
        }, GUIStyles.HeaderTitleContainer);

        private static GroupedItem HeaderSubtitle(List<IUserInterfaceItem> items) =>
            new(items, GUIStyles.HeaderSubtitleContainer);

        private static GroupedItem HeaderSubtitle(string label) =>
            HeaderSubtitle(new List<IUserInterfaceItem> { new Label(label, GUIStyles.HeaderSubtitle) });

        private List<IUserInterfaceItem> BuildResourcesPageContent()
        {
            // Use onboarding's generic configuration-based card creation
            var resourceCards = OnboardingReference.CreateAllResourceCardsList();

            // Build Learn page with custom header and all resource cards (no conditional logic)
            var items = new List<IUserInterfaceItem>
            {
                Header("Learn"),
                HeaderSubtitle("Documentation and external tools to get the most out of Meta XR SDK"),
                new AddSpace(DoubleMargin),
                new Label(OnboardingReference.ResourcesIntro),
                new AddSpace(8),
            };

            // Add all resource cards dynamically
            items.AddRange(resourceCards);

            return items;
        }
    }
}
