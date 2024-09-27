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

using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Colors;

namespace Meta.XR.Guides.Editor
{
    internal static class GuideStyles
    {
        public static class Contents
        {
            public static readonly TextureContent HeaderIcon =
                TextureContent.CreateContent("meta_icon.png", Utils.GuidedAccountSetupIcons, null);

            public static readonly TextureContent DefaultIcon =
                TextureContent.CreateContent("ovr_bullet.png", Utils.GuidedAccountSetupIcons);

            public static readonly TextureContent StatusIcon =
                TextureContent.CreateContent("ovr_status.png", Utils.GuidedAccountSetupIcons);

            public static readonly TextureContent SuccessIcon =
                TextureContent.CreateContent("ovr_success.png", Utils.GuidedAccountSetupIcons);

            public static readonly TextureContent InfoIcon =
                TextureContent.CreateContent("ovr_info.png", Utils.GuidedAccountSetupIcons);

            public static readonly TextureContent BannerImage =
                TextureContent.CreateContent("ovr_banner.png", Utils.GuidedAccountSetupTextures);
        }

        public static class Constants
        {
            public const int DefaultWidth = 520;
            public const int DefaultHeight = 480;
        }

        public class GUIStylesContainer
        {
            public readonly GUIStyle Header = new(EditorStyles.miniLabel)
            {
                fontSize = 12,
                fixedHeight = 32 + Margin * 2,
                padding = new RectOffset(Margin, Margin, Margin, Margin),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = true
            };

            public readonly GUIStyle HeaderIconStyle = new()
            {
                fixedHeight = 32.0f,
                fixedWidth = 32.0f,
                stretchWidth = false,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };

            public readonly GUIStyle HeaderBoldLabel = new(EditorStyles.boldLabel)
            {
                stretchHeight = true,
                fixedHeight = 32,
                fontSize = 16,
                normal =
                {
                    textColor = CharcoalGray
                },
                hover =
                {
                    textColor = CharcoalGray
                },
                alignment = TextAnchor.MiddleLeft,
                richText = true
            };

            public readonly GUIStyle SubtitleLabel = new(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(18, 0, 12, 0),
                wordWrap = true,
                richText = true
            };

            public readonly GUIStyle IconStyleTopPadding = new(EditorStyles.label)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 2, 0),
                fixedWidth = SmallIconSize,
                stretchWidth = false
            };

            public readonly GUIStyle IconStyle = new(EditorStyles.label)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = SmallIconSize,
                stretchWidth = false
            };

            public readonly GUIStyle ContentMargin = new()
            {
                margin = new RectOffset(LargeMargin, LargeMargin, Margin, LargeMargin)
            };

            public readonly GUIStyle LabelTopPadding = new(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false,
                richText = true,
                padding = new RectOffset(0, 0, 3, 0)
            };

            public readonly GUIStyle Label = new(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = true,
                padding = new RectOffset(0, 0, 0, 0)
            };

            public readonly GUIStyle TopMargin = new()
            {
                margin = new RectOffset(0, 0, 8, 0),
            };

            public readonly GUIStyle LinkLabelStyle = new(EditorStyles.linkLabel)
            {
                margin = new RectOffset(3, 3, 2, 2),
                fontSize = 12,
                alignment = TextAnchor.UpperLeft
            };

            public readonly GUIStyle NoMarginAndPadding = new()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleLeft,
            };
        }

        private static GUIStylesContainer _guiStyles;
        public static GUIStylesContainer GUIStyles => _guiStyles ??= new GUIStylesContainer();

        public enum ContentStatusType
        {
            Normal, Warning, Error, Success
        }
    }
}
