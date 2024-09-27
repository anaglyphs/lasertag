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

using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.BuildingBlocks.Editor
{
    internal static class Styles
    {
        public static class Colors
        {
            public static readonly Color AccentColor = HexToColor("#a29de5");
            public static readonly Color DragColor = new Color(1.0f, 1.0f, 1.0f, Constants.DragOpacity);
            public static readonly Color DisabledColor = new Color(Constants.DisabledTint, Constants.DisabledTint, Constants.DisabledTint, 1.0f);
        }

        public static class Constants
        {
            public const float ThumbnailRatio = 1.8f;
            public const int IdealThumbnailWidth = 280;
            public const float DisabledTint = 0.6f;

#if OVR_BB_DRAGANDDROP
            public const float DragOpacity = 0.5f;
#endif // OVR_BB_DRAGANDDROP
        }

        public static class Contents
        {
            public static readonly TextureContent AddIcon =
                TextureContent.CreateContent("ovr_icon_addblock.png", Utils.BuildingBlocksIcons, "Add Block to current scene");

            public static readonly TextureContent CancelIcon =
                TextureContent.CreateContent("ovr_icon_cancel.png", Utils.BuildingBlocksIcons, "Cancel");

            public static readonly TextureContent ConfirmIcon =
                TextureContent.CreateContent("ovr_icon_confirm.png", Utils.BuildingBlocksIcons, "Confirm");

            public static readonly TextureContent DownloadIcon =
                TextureContent.CreateContent("ovr_icon_download.png", Utils.BuildingBlocksIcons, "Download Block to your project");

            public static readonly TextureContent DownloadPackageDependenciesIcon =
                TextureContent.CreateContent("ovr_icon_download.png", Utils.BuildingBlocksIcons, "This Block requires packages to be installed");

            public static readonly TextureContent SelectIcon =
                TextureContent.CreateContent("ovr_icon_link.png", Utils.BuildingBlocksIcons, "Select Block in current scene");

            public static readonly TextureContent ErrorIcon =
                TextureContent.CreateContent("ovr_error_greybg.png", Utils.BuildingBlocksIcons);

            public static readonly TextureContent InfoIcon =
                TextureContent.CreateContent("ovr_info_greybg.png", Utils.BuildingBlocksIcons);

            public static readonly TextureContent SuccessIcon =
                TextureContent.CreateContent("ovr_success_greybg.png", Utils.BuildingBlocksIcons);

            public static readonly TextureContent HeaderIcon =
                TextureContent.CreateContent("ovr_icon_bbw.png", Utils.BuildingBlocksIcons);

            public static readonly TextureContent TagBackground =
                TextureContent.CreateContent("ovr_bg_radius4.png", Utils.BuildingBlocksIcons);
        }

        public class GUIStylesContainer
        {
            public readonly GUIStyle NoMargin = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            public readonly GUIStyle ErrorHelpBox = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                fontSize = 12,
                margin = new RectOffset(Margin, Margin, Margin, Margin),
                padding = new RectOffset(Margin, Margin, Margin, Margin),
                normal = { textColor = ErrorColor }
            };

            public readonly GUIStyle GridItemStyle = new GUIStyle()
            {
                margin = new RectOffset(Margin, Margin, Margin, Margin),
                padding = new RectOffset(Border, Border, Border, Border),
                stretchWidth = false,
                stretchHeight = false,
                normal =
                {
                    background = CharcoalGray.ToTexture()
                }
            };

            public readonly GUIStyle GridItemStyleWithHover = new GUIStyle()
            {
                margin = new RectOffset(Margin, Margin, 0, Margin),
                padding = new RectOffset(Border, Border, Border, Border),
                stretchWidth = false,
                stretchHeight = false,
                normal =
                {
                    background = CharcoalGray.ToTexture()
                },
                hover =
                {
                    background = Colors.AccentColor.ToTexture()
                }
            };

            public readonly GUIStyle GridItemDisabledStyle = new GUIStyle()
            {
                margin = new RectOffset(Margin, Margin, 0, Margin),
                padding = new RectOffset(Border, Border, Border, Border),
                stretchWidth = false,
                stretchHeight = false,
                normal =
                {
                    background = CharcoalGray.ToTexture()
                }
            };

            public readonly GUIStyle ThumbnailAreaStyle = new GUIStyle()
            {
                stretchHeight = false
            };

            public readonly GUIStyle SeparatorAreaStyle = new GUIStyle()
            {
                fixedHeight = Border,
                stretchHeight = false,
                normal =
                {
                    background = DarkGray.ToTexture()
                }
            };

            public readonly GUIStyle DescriptionAreaStyle = new GUIStyle()
            {
                stretchHeight = false,
                padding = new RectOffset(Padding, Padding, Padding, Padding),
                margin = new RectOffset(0, 0, 0, Border),
                fixedHeight = ItemHeight,
                normal =
                {
                    background = DarkGray.ToTexture()
                }
            };

            public readonly GUIStyle DescriptionAreaHoverStyle = new GUIStyle()
            {
                stretchHeight = false,
                fixedHeight = ItemHeight,
                padding = new RectOffset(Padding, Padding, Padding, Padding),
                margin = new RectOffset(0, 0, 0, Border),

                normal =
                {
                    background = DarkGrayHover.ToTexture()
                }
            };

            public readonly GUIStyle EmptyAreaStyle = new GUIStyle()
            {
                stretchHeight = true,
                fixedWidth = 0,
                fixedHeight = ItemHeight,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            public readonly GUIStyle IconStyle = new GUIStyle(EditorStyles.label)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                fixedWidth = SmallIconSize,
                stretchWidth = false
            };

            public readonly GUIStyle LargeButton = new GUIStyle(EditorStyles.miniButton)
            {
                clipping = TextClipping.Overflow,
                fixedHeight = ItemHeight - Padding * 2,
                fixedWidth = ItemHeight - Padding * 2,
                margin = new RectOffset(MiniPadding, MiniPadding, MiniPadding, MiniPadding),
                padding = new RectOffset(Padding, Padding, Padding, Padding)
            };

            public readonly GUIStyle LabelledButton = new GUIStyle(EditorStyles.miniButton)
            {
                clipping = TextClipping.Overflow,
                fixedHeight = 32 - Padding * 2,
                fixedWidth = 128 - Padding * 2,
                margin = new RectOffset(MiniPadding, MiniPadding, MiniPadding, MiniPadding),
                padding = new RectOffset(Margin, Margin, Padding, Padding),
                alignment = TextAnchor.MiddleLeft

            };

            public readonly GUIStyle LabelledButtonIcon = new GUIStyle()
            {
                margin = new RectOffset(MiniPadding, MiniPadding, MiniPadding, MiniPadding),
                padding = new RectOffset(Margin, Margin, Padding, Padding),
                alignment = TextAnchor.MiddleRight
            };

            public readonly GUIStyle LargeButtonArea = new GUIStyle()
            {
                stretchHeight = true,
                fixedWidth = 0,
                fixedHeight = ItemHeight,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(Padding, Padding + 1, Padding, Padding + 1)
            };

            public readonly GUIStyle LabelStyle = new GUIStyle(EditorStyles.boldLabel);

            public readonly GUIStyle LabelStyleWrapped = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
                richText = true
            };

            public readonly GUIStyle LabelHoverStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white }
            };

            public readonly GUIStyle SubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Italic,
                normal =
                {
                    textColor = Color.gray
                }
            };

            public readonly GUIStyle InfoStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal =
                {
                    textColor = Color.gray
                }
            };

            public readonly GUIStyle FoldoutBoldLabel = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold
            };

            public readonly GUIStyle PillBox = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = Contents.TagBackground.GUIContent.image as Texture2D },
                fixedWidth = Margin,
                fixedHeight = LargeMargin + Margin,
                stretchHeight = true,
                margin = new RectOffset(0, Margin, Margin, Margin),
                padding = new RectOffset(0, Padding, 0, 0),
                border = new RectOffset(4, 4, 4, 4)
            };

            public class ColorStates
            {
                public Color Normal;
                public Color Hover;
                public Color Active;

                public Color GetColor(bool active, bool hover)
                {
                    return hover ? Hover : active ? Active : Normal;
                }
            }

            public readonly GUIStyle TagIcon = new GUIStyle(EditorStyles.miniLabel)
            {
                padding = new RectOffset(Padding, Padding, MiniPadding, MiniPadding),

                alignment = TextAnchor.MiddleCenter,
                fixedWidth = 22,
                fixedHeight = 18
            };

            public readonly ColorStates TagBackgroundColors = new ColorStates()
            {
                Normal = CharcoalGray,
                Hover = DarkGray,
                Active = DarkGrayActive
            };

            public readonly GUIStyle TagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                margin = new RectOffset(MiniPadding, MiniPadding, MiniPadding, MiniPadding),
                padding = new RectOffset(Margin, Margin, MiniPadding, MiniPadding),

                wordWrap = false,
                stretchWidth = false,
                fixedHeight = 18,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = OffWhite,
                    background = Contents.TagBackground.GUIContent.image as Texture2D
                },
                hover =
                {
                    textColor = Color.white,
                    background = Contents.TagBackground.GUIContent.image as Texture2D
                },
                border = new RectOffset(6, 6, 6, 6)
            };

            public readonly GUIStyle TagStyleWithIcon = new GUIStyle(EditorStyles.miniLabel)
            {
                margin = new RectOffset(MiniPadding, MiniPadding, MiniPadding, MiniPadding),
                wordWrap = false,
                stretchWidth = false,
                fixedHeight = 18,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = OffWhite,
                    background = Contents.TagBackground.GUIContent.image as Texture2D
                },
                hover =
                {
                    textColor = Color.white,
                    background = Contents.TagBackground.GUIContent.image as Texture2D
                },
                border = new RectOffset(6, 6, 6, 6),
                padding = new RectOffset(18 + Padding, Padding, MiniPadding, MiniPadding)

            };

            public readonly ColorStates TagOverlayBackgroundColors = new ColorStates()
            {
                Normal = CharcoalGraySemiTransparent,
                Hover = DarkGraySemiTransparent,
                Active = CharcoalGraySemiTransparent
            };

            public readonly GUIStyle Toolbar = new GUIStyle()
            {
                margin = new RectOffset(0, 0, Margin, Margin),
                padding = new RectOffset(Margin + Border, Margin + Border, Padding, Padding),
                stretchWidth = true,
                stretchHeight = false,
                normal =
                {
                    background = DarkGray.ToTexture()
                }
            };

            public readonly GUIStyle FilterByTagGroup = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                stretchWidth = false,
                stretchHeight = false
            };
        }

        private static GUIStylesContainer _guiStyles;
        public static GUIStylesContainer GUIStyles => _guiStyles ??= new GUIStylesContainer();
    }
}
