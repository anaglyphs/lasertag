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
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.RLDS.Styles.Spacing;

namespace Meta.XR.MetaWand.Editor
{
    internal static class Styles
    {
        private static GUIStylesContainer _guiStyles;
        public static GUIStylesContainer GUIStyles => _guiStyles ??= new GUIStylesContainer();

        public static class Constants
        {
            public const int MinWidth = 416;
            public const int MaxWidth = 1024;
            public const int MinHeight = 820;
            public const int MaxHeight = 1024;
            public const int HeaderIconSize = XR.Editor.UserInterface.RLDS.Styles.IconSize.SizeXL;
            public const int ButtonHeight = 24;
            public const int ButtonWidth = 256;
            public const int ButtonWidthSmall = 64;
            public const int UserIconSize = 24;
            public const int ChatMessageLeftMargin = 32;
            public const int NewPromptHeaderContainerHeight = 185;
            public const int MetaIconSize = 48;

            public const int AuthWindowMinWidth = 800;
            public const int AuthWindowMaxWidth = 500;

            public const int GenContainerHeight = 128;
            public const int SmallGridItemSize = 82;
            public const int LargeGridItemSize = 184;
            public const int MinItemPerRow = 2;
            public const int MaxItemPerRow = 3;
        }

        public static class Contents
        {
            private static readonly TextureContent.Category Icons = new("MetaWand/Icons");
            private static readonly TextureContent.Category Textures = new("MetaWand/Textures");

            public static readonly TextureContent MetaWandIcon = TextureContent.CreateContent("metawand.png", Icons);

            public static readonly TextureContent MetaWandMiniIcon =
                TextureContent.CreateContent("metawand_mini.png", Icons);

            public static readonly TextureContent AssetLibraryIcon =
                TextureContent.CreateContent("asset_library.png", Icons);

            public static readonly TextureContent CubeFilledMiniIcon =
                TextureContent.CreateContent("cube_filled_mini.png", Icons);

            public static readonly TextureContent CubeFilledIcon =
                TextureContent.CreateContent("cube_filled.png", Icons);

            public static readonly TextureContent AssetLibraryBanner =
                TextureContent.CreateContent("mal_banner.png", Textures);

            public static readonly TextureContent AssetLibraryBannerMini =
                TextureContent.CreateContent("mal_banner_mini.png", Textures);
        }

        public static class Colors
        {
            public static readonly Color GrayBackground = XR.Editor.UserInterface.Utils.HexToColor("#585858");
            public static readonly Color DarkBackground = XR.Editor.UserInterface.Utils.HexToColor("#383838");
            public static readonly Color DarkerBackground = XR.Editor.UserInterface.Utils.HexToColor("#303030");
            public static readonly Color BorderColor = XR.Editor.UserInterface.Utils.HexToColor("#0D0D0D");
            public static readonly Color RowHover = XR.Editor.UserInterface.Utils.HexToColorWithAlpha("#2F60C1", 0.85f);

            public static readonly Color SemiTransparentBlack =
                XR.Editor.UserInterface.Utils.HexToColorWithAlpha("#000000", 0.5f);

            public static Color TransparentBlack(float alpha)
            {
                return XR.Editor.UserInterface.Utils.HexToColorWithAlpha("#000000", alpha);
            }
        }

        public class GUIStylesContainer
        {
            public readonly GUIStyle Body1 =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Body2SmallLabel.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextPrimary
                    },
                    wordWrap = false,
                    overflow = new RectOffset(0, 100, 0, 0)
                };

            public readonly GUIStyle Body2SupportingText =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Body2SupportingText.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextSecondary
                    },
                    richText = true,
                    wordWrap = true
                };

            public readonly GUIStyle Body2SupportingTextNormal =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Meta.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextSecondary
                    },
                    fontSize = 13
                };

            public readonly GUIStyle Body2TextTiny =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Meta.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextSecondary
                    },
                    fontSize = 9
                };

            public readonly GUIStyle Body2TextXS =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Meta.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextSecondary
                    },
                    fontSize = 10
                };

            public readonly GUIStyle BodyLargeCenterAlign =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Meta.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextPrimary
                    },
                    fontSize = 14
                };

            public readonly GUIStyle BodySmallCenterAlign =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Meta.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextPrimary
                    },
                    fontSize = 12
                };

            public readonly GUIStyle BodySmallURL =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Body2SupportingText.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextLink
                    }
                };

            public readonly GUIStyle ContentContainer = new()
            {
                padding = new RectOffset(SpaceXS, SpaceXS, SpaceXS, SpaceXS),
                stretchWidth = false
            };

            public readonly GUIStyle HeaderContainerNewState =
                new(XR.Editor.UserInterface.RLDS.Styles.Divs.PaddingSpaceMD)
                {
                    fixedHeight = Constants.NewPromptHeaderContainerHeight
                };

            public readonly GUIStyle HeaderDescription = new(XR.Editor.UserInterface.RLDS.Styles.Typography.Meta)
            {
                normal =
                {
                    textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextSecondary
                },
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = 14
            };

            public readonly GUIStyle HeaderIcon = new()
            {
                fixedWidth = Constants.HeaderIconSize,
                fixedHeight = Constants.HeaderIconSize
            };

            public readonly GUIStyle HeaderIconContainer = new()
            {
                fixedHeight = Constants.HeaderIconSize
            };

            public readonly GUIStyle Heading3 =
                new(XR.Editor.UserInterface.RLDS.Styles.Typography.Heading3.ToGUIStyle())
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal =
                    {
                        textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextPrimary
                    }
                };

            public readonly GUIStyle PaddingTop =
                new()
                {
                    padding = new RectOffset(0, 0, SpaceLG, 0)
                };

            public readonly GUIStyle PaddingXs = new()
            {
                padding = new RectOffset(SpaceXS, SpaceXS, SpaceXS, SpaceXS)
            };

            public readonly GUIStyle PromptAreaContainer = new()
            {
                padding = new RectOffset(SpaceLG, SpaceLG, SpaceMD, SpaceMD),
                margin = new RectOffset(SpaceMD, SpaceMD, Space4XS, SpaceMD)
            };

            public readonly GUIStyle SearchAreaBanner = new()
            {
                padding = new RectOffset(SpaceLG, SpaceLG, SpaceMD, SpaceMD),
                margin = new RectOffset(SpaceMD, SpaceMD, Space4XS, SpaceMD),
                fixedHeight = 256
            };

            public readonly GUIStyle SearchAreaBannerMini = new()
            {
                padding = new RectOffset(SpaceLG, SpaceLG, SpaceMD, SpaceMD),
                margin = new RectOffset(SpaceMD, SpaceMD, Space4XS, Space3XS),
                fixedHeight = 44
            };

            public readonly GUIStyle SidePanelBottomBackground =
                new(XR.Editor.UserInterface.RLDS.Styles.Divs.PaddingSpaceSM)
                {
                    normal =
                    {
                        background = Color.black.ToTexture()
                    }
                };

            public readonly GUIStyle SidePanelMainBackground =
                new(XR.Editor.UserInterface.RLDS.Styles.Divs.PaddingSpaceSM)
                {
                    normal =
                    {
                        background = Colors.DarkBackground.ToTexture()
                    }
                };

            public readonly GUIStyle SmallGrid = new()
            {
                padding = new RectOffset(SpaceXS, SpaceXS, SpaceXS, SpaceXS),
                fixedWidth = Constants.SmallGridItemSize,
                fixedHeight = Constants.SmallGridItemSize,
                stretchHeight = false,
                stretchWidth = false
            };

            public readonly GUIStyle TabButton = new()
            {
                padding = new RectOffset(SpaceXS, SpaceXS, Space3XS, 0),
                margin = new RectOffset(0, Space3XS, SpaceMD, Space4XS),
                fixedHeight = 26,
                fixedWidth = 72
            };

            public readonly GUIStyle TabButtonsArea = new()
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(SpaceMD, SpaceMD, SpaceMD, Space4XS)
            };

            public readonly ButtonStyle TinyButton = new()
            {
                Height = 18,
                HorizontalPadding = XR.Editor.UserInterface.RLDS.Styles.ButtonSize.XSmall.HorizontalPadding,
                VerticalPadding = XR.Editor.UserInterface.RLDS.Styles.ButtonSize.XSmall.VerticalPadding,
                BackgroundColorNormal = XR.Editor.UserInterface.Utils.HexToColor("#585858"),
                BackgroundColorHover = XR.Editor.UserInterface.Utils.HexToColor("#676767"),
                BackgroundColorDisabled = XR.Editor.UserInterface.RLDS.Styles.Colors.ButtonDisableBackground,
                TextColorNormal = XR.Editor.UserInterface.RLDS.Styles.Colors.TextUISecondary,
                TextColorHover = XR.Editor.UserInterface.RLDS.Styles.Colors.TextUISecondary,
                TextColorDisabled = XR.Editor.UserInterface.RLDS.Styles.Colors.TextUIDisabled,
                CornerRadius = XR.Editor.UserInterface.RLDS.Styles.Radius.RadiusXS,
                TextStyle = new TextStyle
                {
                    FontWeight = FontWeight.Light,
                    FontSize = 11,
                    LineHeight = 16,
                    LetterSpacing = 0f,
                    Italic = false,
                    UseOpticalSize80 = true
                }
            };

            public readonly GUIStyle Title = new(XR.Editor.UserInterface.RLDS.Styles.Typography.Body1Text)
            {
                normal =
                {
                    textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextPrimary
                },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20
            };

            public readonly GUIStyle TitleIconMini = new(XR.Editor.UserInterface.RLDS.Styles.Typography.Body1Text)
            {
                fixedWidth = 30,
                fixedHeight = 30,
                padding = new RectOffset(SpaceXS, 0, SpaceXS, 0)
            };

            public readonly GUIStyle TitleMini = new(XR.Editor.UserInterface.RLDS.Styles.Typography.Body1Text)
            {
                normal =
                {
                    textColor = XR.Editor.UserInterface.RLDS.Styles.Colors.TextPrimary
                },
                fontSize = 16,
                padding = new RectOffset(SpaceMD, 0, SpaceXS, 0)
            };

            public readonly GUIStyle UniformMargin = new()
            {
                margin = new RectOffset(Margin, Margin, Margin, Margin)
            };
        }
    }
}
