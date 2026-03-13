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

using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    /// <summary>
    /// RLDS (Reality Labs Design System) styles and constants for Unity Editor UI
    /// Based on RLDS Desktop design system
    /// </summary>
    internal static class Styles
    {
        public static class Buttons
        {
            // Button styles - Large
            public static readonly ButtonStyle Primary = new ButtonStyle
            {
                Height = ButtonSize.Large.Height,
                MinWidth = ButtonSize.Large.MinWidth,
                HorizontalPadding = ButtonSize.Large.HorizontalPadding,
                VerticalPadding = ButtonSize.Large.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonPrimaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonPrimaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUIPrimary,
                TextColorHover = Colors.TextUIPrimary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body1Button
            };

            public static readonly ButtonStyle Secondary = new ButtonStyle
            {
                Height = ButtonSize.Large.Height,
                MinWidth = ButtonSize.Large.MinWidth,
                HorizontalPadding = ButtonSize.Large.HorizontalPadding,
                VerticalPadding = ButtonSize.Large.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonSecondaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonSecondaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUISecondary,
                TextColorHover = Colors.TextUISecondary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body1Button
            };

            public static readonly ButtonStyle Tertiary = new ButtonStyle
            {
                Height = ButtonSize.Large.Height,
                MinWidth = ButtonSize.Large.MinWidth,
                HorizontalPadding = ButtonSize.Large.HorizontalPadding,
                VerticalPadding = ButtonSize.Large.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonTertiaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonTertiaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUITertiary,
                TextColorHover = Colors.TextUITertiary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body1Button
            };

            public static readonly ButtonStyle OnMedia = new ButtonStyle
            {
                Height = ButtonSize.Large.Height,
                MinWidth = ButtonSize.Large.MinWidth,
                HorizontalPadding = ButtonSize.Large.HorizontalPadding,
                VerticalPadding = ButtonSize.Large.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonOnMediaBackgroundDefault,
                BackgroundColorHover = Colors.ButtonOnMediaBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUIOnMedia,
                TextColorHover = Colors.TextUIOnMedia,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body1Button,
                BorderWidth = Border.BorderWidth,
                BorderColor = Colors.BorderColor
            };

            // Button styles - Small
            public static readonly ButtonStyle PrimarySmall = new ButtonStyle
            {
                Height = ButtonSize.Small.Height,
                MinWidth = ButtonSize.Small.MinWidth,
                HorizontalPadding = ButtonSize.Small.HorizontalPadding,
                VerticalPadding = ButtonSize.Small.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonPrimaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonPrimaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUIPrimary,
                TextColorHover = Colors.TextUIPrimary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            public static readonly ButtonStyle SecondarySmall = new ButtonStyle
            {
                Height = ButtonSize.Small.Height,
                MinWidth = ButtonSize.Small.MinWidth,
                HorizontalPadding = ButtonSize.Small.HorizontalPadding,
                VerticalPadding = ButtonSize.Small.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonSecondaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonSecondaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUISecondary,
                TextColorHover = Colors.TextUISecondary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            public static readonly ButtonStyle TertiarySmall = new ButtonStyle
            {
                Height = ButtonSize.Small.Height,
                MinWidth = ButtonSize.Small.MinWidth,
                HorizontalPadding = ButtonSize.Small.HorizontalPadding,
                VerticalPadding = ButtonSize.Small.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonTertiaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonTertiaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUITertiary,
                TextColorHover = Colors.TextUITertiary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            public static readonly ButtonStyle OnMediaSmall = new ButtonStyle
            {
                Height = ButtonSize.Small.Height,
                MinWidth = ButtonSize.Small.MinWidth,
                HorizontalPadding = ButtonSize.Small.HorizontalPadding,
                VerticalPadding = ButtonSize.Small.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonOnMediaBackgroundDefault,
                BackgroundColorHover = Colors.ButtonOnMediaBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUIOnMedia,
                TextColorHover = Colors.TextUIOnMedia,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            // Button styles - XSmall
            public static readonly ButtonStyle PrimaryXSmall = new ButtonStyle
            {
                Height = ButtonSize.XSmall.Height,
                MinWidth = ButtonSize.XSmall.MinWidth,
                HorizontalPadding = ButtonSize.XSmall.HorizontalPadding,
                VerticalPadding = ButtonSize.XSmall.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonPrimaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonPrimaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUIPrimary,
                TextColorHover = Colors.TextUIPrimary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            public static readonly ButtonStyle SecondaryXSmall = new ButtonStyle
            {
                Height = ButtonSize.XSmall.Height,
                MinWidth = ButtonSize.XSmall.MinWidth,
                HorizontalPadding = ButtonSize.XSmall.HorizontalPadding,
                VerticalPadding = ButtonSize.XSmall.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonSecondaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonSecondaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUISecondary,
                TextColorHover = Colors.TextUISecondary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            public static readonly ButtonStyle TertiaryXSmall = new ButtonStyle
            {
                Height = ButtonSize.XSmall.Height,
                MinWidth = ButtonSize.XSmall.MinWidth,
                HorizontalPadding = ButtonSize.XSmall.HorizontalPadding,
                VerticalPadding = ButtonSize.XSmall.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonTertiaryBackgroundDefault,
                BackgroundColorHover = Colors.ButtonTertiaryBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUITertiary,
                TextColorHover = Colors.TextUITertiary,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };

            public static readonly ButtonStyle OnMediaXSmall = new ButtonStyle
            {
                Height = ButtonSize.XSmall.Height,
                MinWidth = ButtonSize.XSmall.MinWidth,
                HorizontalPadding = ButtonSize.XSmall.HorizontalPadding,
                VerticalPadding = ButtonSize.XSmall.VerticalPadding,
                BackgroundColorNormal = Colors.ButtonOnMediaBackgroundDefault,
                BackgroundColorHover = Colors.ButtonOnMediaBackgroundHover,
                BackgroundColorDisabled = Colors.ButtonDisableBackground,
                TextColorNormal = Colors.TextUIOnMedia,
                TextColorHover = Colors.TextUIOnMedia,
                TextColorDisabled = Colors.TextUIDisabled,
                CornerRadius = Radius.RadiusXS,
                TextStyle = Typography.Body2SmallButton
            };
        }

        public static class Colors
        {
            // RLDS Colors (Dark mode)
            public static readonly Color SurfacePrimaryBackground = HexToColor("#414141"); // desktop-surface-primary-background
            public static readonly Color SurfaceSecondaryBackground = HexToColor("#272727"); // desktop-surface-seconday-background
            public static readonly Color BorderDivider = HexToColor("#272727"); // desktop-border-divider

            // Button backgrounds (Dark mode)
            public static readonly Color ButtonPrimaryBackgroundDefault = HexToColorWithAlpha("#FFFFFF", 0.9f); // button-primary-background-default
            public static readonly Color ButtonPrimaryBackgroundHover = HexToColor("#D9D9D9"); // button-primary-background-hover
            public static readonly Color ButtonSecondaryBackgroundDefault = HexToColorWithAlpha("#FFFFFF", 0.05f); // button-secondary-background-default
            public static readonly Color ButtonSecondaryBackgroundHover = HexToColorWithAlpha("#FFFFFF", 0.1f); // button-secondary-background-hover
            public static readonly Color ButtonTertiaryBackgroundDefault = HexToColorWithAlpha("#FFFFFF", 0f); // button-tertiary-background-default
            public static readonly Color ButtonTertiaryBackgroundHover = HexToColorWithAlpha("#FFFFFF", 0.1f); // button-tertiary-background-hover
            public static readonly Color ButtonOnMediaBackgroundDefault = HexToColorWithAlpha("#FFFFFF", 0f); // button-on media-background-default
            public static readonly Color ButtonOnMediaBackgroundHover = HexToColorWithAlpha("#FFFFFF", 0.1f); // button-on media-background-hover
            public static readonly Color ButtonDeselectBackgroundDefault = HexToColorWithAlpha("#FFFFFF", 0.05f); // button-deselect-background-default
            public static readonly Color ButtonDeselectBackgroundHover = HexToColorWithAlpha("#FFFFFF", 0.1f); // button-deselect-background-hover
            public static readonly Color ButtonDisableBackground = HexToColor("#5A5A5A"); // button-disable-background
            public static readonly Color ButtonSelectBackgroundDefault = HexToColor("#FFFFFF"); // button-select-background-default
            public static readonly Color ButtonSelectBackgroundHover = HexToColor("#F2F2F2"); // button-select-background-hover

            // Border colors
            public static readonly Color BorderColor = HexToColor("#FFFFFF");
            public static readonly Color BorderColorDark = HexToColor("#303030");

            // Icon UI colors (Dark mode)
            public static readonly Color IconUIPrimary = HexToColor("#272727"); // icon-ui-primary
            public static readonly Color IconUISecondary = HexToColorWithAlpha("#FFFFFF", 0.9f); // icon-ui-secondary
            public static readonly Color IconUITertiary = HexToColorWithAlpha("#FFFFFF", 0.9f); // icon-ui-tertiary
            public static readonly Color IconUIOnMedia = HexToColor("#F2F2F2"); // icon-ui-on media
            public static readonly Color IconUIDeselected = HexToColorWithAlpha("#FFFFFF", 0.9f); // icon-ui-deselected
            public static readonly Color IconUISelected = HexToColor("#272727"); // icon-ui-selected
            public static readonly Color IconPositive = HexToColor("#2AD116"); // icon-positive
            public static readonly Color IconNegative = HexToColor("#F7818C"); // icon-negative
            public static readonly Color IconWarning = HexToColor("#FC9435"); // icon-warning
            public static readonly Color IconNotification = HexToColor("#64B5FF"); // icon-notification
            public static readonly Color IconPrivacy = HexToColor("#9C94F8"); // icon-privacy
            public static readonly Color IconPrimary = HexToColorWithAlpha("#FFFFFF", 0.9f); // icon-primary
            public static readonly Color IconSecondary = HexToColorWithAlpha("#FFFFFF", 0.7f); // icon-secondary
            public static readonly Color IconOnMedia = HexToColorWithAlpha("#FFFFFF", 0.9f); // icon-on media
            public static readonly Color IconSecondaryOnMedia = HexToColorWithAlpha("#FFFFFF", 0.7f); // icon-secondary on media
            public static readonly Color IconDisabled = HexToColorWithAlpha("#272727", 0.3f); // icon-disabled
            public static readonly Color IconPlaceholder = HexToColorWithAlpha("#FFFFFF", 0.3f); // icon-placeholder

            // Text colors (Dark mode)
            public static readonly Color TextUIPrimary = HexToColor("#272727"); // text-ui-primary
            public static readonly Color TextUISecondary = HexToColorWithAlpha("#FFFFFF", 0.9f); // text-ui-secondary
            public static readonly Color TextUITertiary = HexToColorWithAlpha("#FFFFFF", 0.9f); // text-ui-tertiary
            public static readonly Color TextUIOnMedia = HexToColor("#F2F2F2"); // text-ui-on media
            public static readonly Color TextUIDisabled = HexToColor("#8E8E8E"); // text-ui-disabled
            public static readonly Color TextUISelected = HexToColor("#272727"); // text-ui-selected
            public static readonly Color TextPrimary = HexToColorWithAlpha("#FFFFFF", 0.9f); // text-primary
            public static readonly Color TextSecondary = HexToColorWithAlpha("#FFFFFF", 0.7f); // text-secondary
            public static readonly Color TextPlaceholder = HexToColorWithAlpha("#FFFFFF", 0.3f); // text-placeholder
            public static readonly Color TextDisabled = HexToColorWithAlpha("#FFFFFF", 0.3f); // text-disabled
            public static readonly Color TextOnMediaPrimary = HexToColorWithAlpha("#FFFFFF", 0.9f); // text-on media primary
            public static readonly Color TextOnMediaSecondary = HexToColorWithAlpha("#FFFFFF", 0.7f); // text-on media secondary
            public static readonly Color TextWarning = HexToColor("#FC9435"); // text-warning
            public static readonly Color TextLink = HexToColor("#64B5FF"); // text-link
            public static readonly Color TextPositive = HexToColor("#2AD116"); // text-postive
            public static readonly Color TextNegative = HexToColor("#F7818C"); // text-negative

            // RLDS Colors (Light mode)
            public static readonly Color LightSurfacePrimaryBackground = HexToColor("#F2F2F2"); // desktop-surface-primary-background
            public static readonly Color LightSurfaceSecondaryBackground = HexToColor("#FFFFFF"); // desktop-surface-seconday-background
            public static readonly Color LightBorderDivider = HexToColor("#F2F2F2"); // desktop-border-divider

            // Button backgrounds (Light mode)
            public static readonly Color LightButtonPrimaryBackgroundDefault = HexToColor("#272727"); // button-primary-background-default
            public static readonly Color LightButtonPrimaryBackgroundHover = HexToColor("#5A5A5A"); // button-primary-background-hover
            public static readonly Color LightButtonSecondaryBackgroundDefault = HexToColorWithAlpha("#272727", 0.05f); // button-secondary-background-default
            public static readonly Color LightButtonSecondaryBackgroundHover = HexToColorWithAlpha("#272727", 0.1f); // button-secondary-background-hover
            public static readonly Color LightButtonTertiaryBackgroundDefault = HexToColorWithAlpha("#272727", 0f); // button-tertiary-background-default
            public static readonly Color LightButtonTertiaryBackgroundHover = HexToColorWithAlpha("#272727", 0.1f); // button-tertiary-background-hover
            public static readonly Color LightButtonOnMediaBackgroundDefault = HexToColorWithAlpha("#FFFFFF", 0f); // button-on media-background-default
            public static readonly Color LightButtonOnMediaBackgroundHover = HexToColorWithAlpha("#FFFFFF", 0.1f); // button-on media-background-hover
            public static readonly Color LightButtonDeselectBackgroundDefault = HexToColorWithAlpha("#272727", 0.05f); // button-deselect-background-default
            public static readonly Color LightButtonDeselectBackgroundHover = HexToColorWithAlpha("#272727", 0.1f); // button-deselect-background-hover
            public static readonly Color LightButtonDisableBackground = HexToColor("#C0C0C0"); // button-disable-background
            public static readonly Color LightButtonSelectBackgroundDefault = HexToColor("#272727"); // button-select-background-default
            public static readonly Color LightButtonSelectBackgroundHover = HexToColorWithAlpha("#000000", 0.9f); // button-select-background-hover

            // Icon UI colors (Light mode)
            public static readonly Color LightIconUIPrimary = HexToColor("#FFFFFF"); // icon-ui-primary
            public static readonly Color LightIconUISecondary = HexToColor("#272727"); // icon-ui-secondary
            public static readonly Color LightIconUITertiary = HexToColor("#272727"); // icon-ui-tertiary
            public static readonly Color LightIconUIOnMedia = HexToColor("#F2F2F2"); // icon-ui-on media
            public static readonly Color LightIconUIDeselected = HexToColor("#272727"); // icon-ui-deselected
            public static readonly Color LightIconUISelected = HexToColor("#F2F2F2"); // icon-ui-selected
            public static readonly Color LightIconPositive = HexToColor("#0B8A1B"); // icon-positive
            public static readonly Color LightIconNegative = HexToColor("#DD1535"); // icon-negative
            public static readonly Color LightIconWarning = HexToColor("#A94302"); // icon-warning
            public static readonly Color LightIconNotification = HexToColor("#0173EC"); // icon-notification
            public static readonly Color LightIconPrivacy = HexToColor("#755BE4"); // icon-privacy
            public static readonly Color LightIconPrimary = HexToColor("#272727"); // icon-primary
            public static readonly Color LightIconSecondary = HexToColorWithAlpha("#272727", 0.7f); // icon-secondary
            public static readonly Color LightIconOnMedia = HexToColor("#FFFFFF"); // icon-on media
            public static readonly Color LightIconSecondaryOnMedia = HexToColorWithAlpha("#FFFFFF", 0.7f); // icon-secondary on media
            public static readonly Color LightIconDisabled = HexToColorWithAlpha("#272727", 0.3f); // icon-disabled
            public static readonly Color LightIconPlaceholder = HexToColorWithAlpha("#272727", 0.3f); // icon-placeholder

            // Text colors (Light mode)
            public static readonly Color LightTextUIPrimary = HexToColor("#FFFFFF"); // text-ui-primary
            public static readonly Color LightTextUISecondary = HexToColor("#272727"); // text-ui-secondary
            public static readonly Color LightTextUITertiary = HexToColor("#272727"); // text-ui-tertiary
            public static readonly Color LightTextUIOnMedia = HexToColor("#F2F2F2"); // text-ui-on media
            public static readonly Color LightTextUIDisabled = HexToColor("#F2F2F2"); // text-ui-disabled
            public static readonly Color LightTextUISelected = HexToColor("#F2F2F2"); // text-ui-selected
            public static readonly Color LightTextPrimary = HexToColor("#272727"); // text-primary
            public static readonly Color LightTextSecondary = HexToColorWithAlpha("#272727", 0.7f); // text-secondary
            public static readonly Color LightTextPlaceholder = HexToColorWithAlpha("#272727", 0.3f); // text-placeholder
            public static readonly Color LightTextDisabled = HexToColorWithAlpha("#272727", 0.3f); // text-disabled
            public static readonly Color LightTextOnMediaPrimary = HexToColor("#FFFFFF"); // text-on media primary
            public static readonly Color LightTextOnMediaSecondary = HexToColorWithAlpha("#FFFFFF", 0.7f); // text-on media secondary
            public static readonly Color LightTextWarning = HexToColor("#A94302"); // text-warning
            public static readonly Color LightTextLink = HexToColor("#0173EC"); // text-link
            public static readonly Color LightTextPositive = HexToColor("#0B8A1B"); // text-postive
            public static readonly Color LightTextNegative = HexToColor("#DD1535"); // text-negative
        }

        public static class Typography
        {
            // RLDS Typography
            // Note: Font family is not implemented here as mentioned in the task
            // RLDS: Optimistic, macOS: SF Pro, Windows: Segoe UI

            // Typography ramp styles
            public static readonly TextStyle Title = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 56,
                LineHeight = 60,
                LetterSpacing = -1f,
                Italic = false,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle TitleItalic = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 56,
                LineHeight = 60,
                LetterSpacing = -1f,
                Italic = true,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading1 = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 32,
                LineHeight = 36,
                LetterSpacing = -1f,
                Italic = false,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading1Italic = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 32,
                LineHeight = 36,
                LetterSpacing = -1f,
                Italic = true,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading2 = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 24,
                LineHeight = 28,
                LetterSpacing = -0.5f,
                Italic = false,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading2Italic = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 24,
                LineHeight = 28,
                LetterSpacing = -0.5f,
                Italic = true,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading3 = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 20,
                LineHeight = 24,
                LetterSpacing = -0.5f,
                Italic = false,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading3Italic = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 20,
                LineHeight = 24,
                LetterSpacing = -0.5f,
                Italic = true,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading4 = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 16,
                LineHeight = 20,
                LetterSpacing = -0.5f,
                Italic = false,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Heading4Italic = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 16,
                LineHeight = 20,
                LetterSpacing = -0.5f,
                Italic = true,
                UseOpticalSize80 = false
            };

            public static readonly TextStyle Body1Label = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 14,
                LineHeight = 18,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true // Use optical size 80 for better readability at smaller sizes
            };

            public static readonly TextStyle Body1Text = new TextStyle
            {
                FontWeight = FontWeight.Medium,
                FontSize = 14,
                LineHeight = 18,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Body1TextItalic = new TextStyle
            {
                FontWeight = FontWeight.Medium,
                FontSize = 14,
                LineHeight = 18,
                LetterSpacing = 0f,
                Italic = true,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Body1Button = new TextStyle
            {
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                LineHeight = 18,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Body2SmallLabel = new TextStyle
            {
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                LineHeight = 16,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Body2SmallButton = new TextStyle
            {
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                LineHeight = 16,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Body2SupportingText = new TextStyle
            {
                FontWeight = FontWeight.Medium,
                FontSize = 12,
                LineHeight = 16,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Body2SupportingTextItalic = new TextStyle
            {
                FontWeight = FontWeight.Medium,
                FontSize = 12,
                LineHeight = 16,
                LetterSpacing = 0f,
                Italic = true,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle Meta = new TextStyle
            {
                FontWeight = FontWeight.Regular,
                FontSize = 11,
                LineHeight = 16,
                LetterSpacing = 0f,
                Italic = false,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle MetaItalic = new TextStyle
            {
                FontWeight = FontWeight.Regular,
                FontSize = 11,
                LineHeight = 16,
                LetterSpacing = 0f,
                Italic = true,
                UseOpticalSize80 = true
            };

            public static readonly TextStyle EyebrowLabel = new TextStyle
            {
                FontWeight = FontWeight.Regular,
                FontSize = 11,
                LineHeight = 16,
                LetterSpacing = 0.3f,
                TextTransform = TextTransform.Uppercase,
                Italic = false,
                UseOpticalSize80 = true
            };
        }

        public static class Border
        {
            public const int BorderWidth = 1;
        }
        public static class Spacing
        {
            // RLDS spacing
            public const int None = 0;  // Semantic/Ramps/spacing/none
            public const int Space4XS = 2;  // Semantic/Ramps/spacing/4XS
            public const int Space3XS = 4;  // Semantic/Ramps/spacing/3XS
            public const int Space2XS = 6;  // Semantic/Ramps/spacing/2XS
            public const int SpaceXS = 8;   // Semantic/Ramps/spacing/XS
            public const int SpaceSM = 12;  // Semantic/Ramps/spacing/SM
            public const int SpaceMD = 16;  // Semantic/Ramps/spacing/MD
            public const int SpaceLG = 20;  // Semantic/Ramps/spacing/LG
            public const int SpaceXL = 24;  // Semantic/Ramps/spacing/XL
            public const int Space2XL = 38; // Semantic/Ramps/spacing/2XL
            public const int Space3XL = 40; // Semantic/Ramps/spacing/3XL
            public const int Space4XL = 48; // Semantic/Ramps/spacing/4XL
            public const int Space5XL = 64; // Semantic/Ramps/spacing/5XL
        }

        public static class Radius
        {
            // RLDS radius
            public const int None = 0;    // Semantic/Ramps/cornerRadius/none
            public const int RadiusXS = 4;  // Semantic/Ramps/cornerRadius/XS
            public const int RadiusSM = 8;  // Semantic/Ramps/cornerRadius/SM
            public const int RadiusMD = 14; // Semantic/Ramps/cornerRadius/MD
            public const int RadiusLG = 24; // Semantic/Ramps/cornerRadius/LG
            public const int RadiusXL = 32; // Semantic/Ramps/cornerRadius/XL
            public const int RadiusFull = 1000; // Semantic/Ramps/cornerRadius/full
        }

        public static class IconSize
        {
            // RLDS icon size
            public const int Size2XS = 8;  // Semantic/Ramps/iconSize/2XS
            public const int SizeXS = 12;  // Semantic/Ramps/iconSize/XS
            public const int SizeSM = 16;  // Semantic/Ramps/iconSize/SM
            public const int SizeMD = 18;  // Semantic/Ramps/iconSize/MD
            public const int SizeLG = 24;  // Semantic/Ramps/iconSize/LG
            public const int SizeXL = 32;  // Semantic/Ramps/iconSize/XL
            public const int Size2XL = 48; // Semantic/Ramps/iconSize/2XL
            public const int Size3XL = 72; // Semantic/Ramps/iconSize/3XL
        }

        public static class Shadow
        {
            // RLDS Materiality (Dark mode)
            public static readonly string BeveledShadow = "1px 1px 1px 0px rgba(255, 255, 255, 0.10) inset, -1px -1px 1px 0px rgba(0, 0, 0, 0.10) inset";
            public static readonly string DropShadow = "0px 0px 32px 0px rgba(0, 0, 0, 0.10), 0px 16px 48px 0px rgba(0, 0, 0, 0.20), 1px 1px 1px 0px rgba(255, 255, 255, 0.10) inset, -1px -1px 1px 0px rgba(0, 0, 0, 0.10) inset";
            public static readonly string PunchedInShadow = "0px 1px 1px 0px rgba(255, 255, 255, 0.10), 0px 1px 1px 0px rgba(0, 0, 0, 0.10) inset";

            // RLDS Materiality (Light mode)
            public static readonly string LightBeveledShadow = "1px 1px 1px 0px #FFF inset, -1px -1px 1px 0px rgba(39, 39, 39, 0.05) inset";
            public static readonly string LightDropShadow = "0px 0px 32px 0px rgba(39, 39, 39, 0.05), 0px 16px 48px 0px rgba(39, 39, 39, 0.05), 1px 1px 1px 0px #FFF inset, -1px -1px 1px 0px rgba(39, 39, 39, 0.05) inset";
            public static readonly string LightPunchedInShadow = "0px 1px 1px 0px #FFF, 0px 1px 1px 0px rgba(39, 39, 39, 0.05) inset";
        }

        public static class ButtonSize
        {
            // Button sizes
            public static class Large
            {
                public const int Height = 40;
                public const int MinWidth = 120;
                public const int HorizontalPadding = 16;
                public const int VerticalPadding = 10;
            }

            public static class Small
            {
                public const int Height = 32;
                public const int MinWidth = 80;
                public const int HorizontalPadding = 12;
                public const int VerticalPadding = 6;
            }

            public static class XSmall
            {
                public const int Height = 24;
                public const int MinWidth = 60;
                public const int HorizontalPadding = 8;
                public const int VerticalPadding = 4;
            }
        }

        public static class Divs
        {
            public static readonly GUIStyle PaddingSpaceMD = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(Spacing.SpaceMD, Spacing.SpaceMD, Spacing.SpaceMD, Spacing.SpaceMD),
            };

            public static readonly GUIStyle PaddingSpaceSM = new GUIStyle()
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(Spacing.SpaceSM, Spacing.SpaceSM, Spacing.SpaceSM, Spacing.SpaceSM),
            };

            public static readonly GUIStyle VerticalTopMarginSpaceSM = new GUIStyle()
            {
                margin = new RectOffset(0, 0, Spacing.SpaceSM, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            public static readonly GUIStyle VerticalTopMarginSpace3XS = new GUIStyle()
            {
                margin = new RectOffset(0, 0, Spacing.Space3XS, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
        }
    }
}
