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
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Guides.Editor.About
{
    internal static class Styles
    {
        public static class Colors
        {
        }

        public class GUIStylesContainer
        {
            public readonly GUIStyle HeaderContainer = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(LargeMargin - Margin, LargeMargin, LargeMargin, Margin),
                stretchHeight = false
            };

            public readonly GUIStyle DynamicCardTitleGroup = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, Padding)
            };

            public readonly GUIStyle DynamicCardTitle = new GUIStyle()
            {
                fontSize = 14,
                wordWrap = true,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white, }
            };

            public readonly GUIStyle DynamicCardContent = new GUIStyle()
            {
                fontSize = 12,
                wordWrap = true,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white, }
            };

            public readonly GUIStyle DynamicCardDynamicContent = new GUIStyle()
            {
                fontSize = 10,
                wordWrap = true,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white },
                alignment = TextAnchor.LowerLeft,
                fixedHeight = 16,
            };

            public readonly GUIStyle HeaderTitleContainer = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, Margin),
                fixedHeight = 32,
            };

            public readonly GUIStyle HeaderBoldLabelLarge = new(EditorStyles.boldLabel)
            {
                fixedHeight = 48,
                fontSize = 24,
                normal =
                {
                    textColor = Color.white
                },
                hover =
                {
                    textColor = Color.white
                },
                alignment = TextAnchor.UpperLeft,
                richText = true,
                padding = new RectOffset(0, 0, DoublePadding + 1, 0)
            };

            public readonly GUIStyle HeaderSubtitleContainer = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(Margin + 48, 0, 0, 0),
            };

            public readonly GUIStyle HeaderSubtitle = new GUIStyle(UIStyles.GUIStyles.Label)
            {
            };

            public readonly GUIStyle HeaderSubtitleNoWrap = new GUIStyle(UIStyles.GUIStyles.Label)
            {
                wordWrap = false
            };
        }

        public static class Contents
        {
        }

        public static class Constants
        {
            public const int Width = 810;
            public const int Height = 500;
            public const int PageHeight = Height - LargeMargin - LargeMargin - DoubleMargin - 20;
            public const int LeftPaneWidth = 270;
            public const int RightPaneWidth = Width - LeftPaneWidth;

        }

        private static GUIStylesContainer _guiStyles;
        public static GUIStylesContainer GUIStyles => _guiStyles ??= new GUIStylesContainer();
    }
}
