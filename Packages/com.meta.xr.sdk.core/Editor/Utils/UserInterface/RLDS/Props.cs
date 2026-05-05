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

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal static class Props
    {
        /// <summary>
        /// Button variant types following the RLDS design system.
        /// </summary>
        public enum ButtonVariant
        {
            Primary,
            Secondary,
            Tertiary,
            OnMedia
        }

        /// <summary>
        /// Button size types following the RLDS design system.
        /// </summary>
        public enum ButtonSize
        {
            Large,
            Small,
            XSmall
        }

        /// <summary>
        /// Typography CSS class names for RLDS design system.
        /// </summary>
        public static class Typography
        {
            public const string Title = "rlds-title";
            public const string Heading1 = "rlds-heading1";
            public const string Heading2 = "rlds-heading2";
            public const string Heading3 = "rlds-heading3";
            public const string Heading4 = "rlds-heading4";
            public const string Body1Label = "rlds-body1-label";
            public const string Body1Text = "rlds-body1-text";
            public const string Body2SmallLabel = "rlds-body2-small-label";
            public const string Body2SupportingText = "rlds-body2-supporting-text";
            public const string Meta = "rlds-meta";
            public const string EyebrowLabel = "rlds-eyebrow-label";
        }

        /// <summary>
        /// Button CSS class names for RLDS design system.
        /// </summary>
        public static class Button
        {
            // Primary buttons
            public const string Primary = "rlds-button-primary";
            public const string PrimarySmall = "rlds-button-primary-small";
            public const string PrimaryXSmall = "rlds-button-primary-xsmall";

            // Secondary buttons
            public const string Secondary = "rlds-button-secondary";
            public const string SecondarySmall = "rlds-button-secondary-small";
            public const string SecondaryXSmall = "rlds-button-secondary-xsmall";

            // Tertiary buttons
            public const string Tertiary = "rlds-button-tertiary";
            public const string TertiarySmall = "rlds-button-tertiary-small";
            public const string TertiaryXSmall = "rlds-button-tertiary-xsmall";

            // OnMedia buttons
            public const string OnMedia = "rlds-button-onmedia";
            public const string OnMediaSmall = "rlds-button-onmedia-small";
            public const string OnMediaXSmall = "rlds-button-onmedia-xsmall";
        }

        /// <summary>
        /// TextField CSS class names for RLDS design system.
        /// </summary>
        public static class TextField
        {
            // Base and variants
            public const string Base = "rlds-textfield";
            public const string Small = "rlds-textfield-small";
            public const string Error = "rlds-textfield-error";
            public const string ReadOnly = "rlds-textfield-readonly";
            public const string TextArea = "rlds-textfield-textarea";

            // With icons
            public const string WithLeftIcon = "rlds-textfield-with-left-icon";
            public const string WithRightIcon = "rlds-textfield-with-right-icon";

            // Helper elements
            public const string HelperContainer = "rlds-textfield__helper-container";
            public const string HasError = "has-error";
            public const string ErrorMessage = "rlds-textfield__error-message";
            public const string CharacterCount = "rlds-textfield__character-count";

            // Icon containers
            public const string IconLeft = "rlds-textfield__icon-left";
            public const string IconRight = "rlds-textfield__icon-right";

            // Action buttons
            public const string ClearButton = "rlds-textfield__clear-button";
            public const string PasswordToggle = "rlds-textfield__password-toggle";
        }

        /// <summary>
        /// Toggle CSS class names for RLDS design system.
        /// </summary>
        public static class Toggle
        {
            public const string Base = "rlds-toggle";
        }

        /// <summary>
        /// Radio button CSS class names for RLDS design system.
        /// </summary>
        public static class Radio
        {
            public const string Group = "rlds-radio-group";
            public const string Item = "rlds-radio-item";
            public const string InputRow = "rlds-radio-item__input-row";
            public const string Label = "rlds-radio-item__label";
            public const string LabelDisabled = "rlds-radio-item__label--disabled";
            public const string SelectedIndicator = "rlds-radio-item__selected-indicator";
            public const string ChildrenContainer = "rlds-radio-item__children-container";
            public const string Description = "rlds-radio-item__description";
            public const string DescriptionDisabled = "rlds-radio-item__description--disabled";
        }

        /// <summary>
        /// Indicator CSS class names for RLDS design system.
        /// </summary>
        public static class Indicator
        {
            public const string Base = "rlds-indicator";
            public const string Positive = "rlds-indicator--positive";
            public const string Negative = "rlds-indicator--negative";
            public const string Warning = "rlds-indicator--warning";
            public const string Disabled = "rlds-indicator--disabled";
            public const string Default = "rlds-indicator--default";
            public const string Privacy = "rlds-indicator--privacy";
        }

        /// <summary>
        /// Badge CSS class names for RLDS design system.
        /// </summary>
        public static class Badge
        {
            public const string Base = "rlds-badge";
            public const string Label = "rlds-badge__label";
            public const string Positive = "rlds-badge--positive";
            public const string Negative = "rlds-badge--negative";
            public const string Warning = "rlds-badge--warning";
            public const string Disabled = "rlds-badge--disabled";
            public const string Default = "rlds-badge--default";
            public const string Privacy = "rlds-badge--privacy";
        }

        /// <summary>
        /// Progress bar CSS class names for RLDS design system.
        /// </summary>
        public static class ProgressBar
        {
            public const string Container = "rlds-progress-bar-container";
            public const string Bar = "rlds-progress-bar";
            public const string Error = "rlds-progress-bar--error";
        }

        /// <summary>
        /// Ring Spinner CSS class names for RLDS design system (animated progress ring).
        /// </summary>
        public static class RingSpinner
        {
            public const string Root = "rlds-ring-spinner-root";
            public const string Ring = "rlds-ring-spinner";
            public const string Size12 = "rlds-ring-spinner--size-12";
            public const string Size16 = "rlds-ring-spinner--size-16";
            public const string Size24 = "rlds-ring-spinner--size-24";
            public const string Size32 = "rlds-ring-spinner--size-32";
            public const string Default = "rlds-ring-spinner--default";
            public const string Accent = "rlds-ring-spinner--accent";
            public const string Disabled = "rlds-ring-spinner--disabled";
            public const string Dark = "rlds-ring-spinner--dark";
            public const string Light = "rlds-ring-spinner--light";
        }

        /// <summary>
        /// Surface CSS class names for RLDS design system.
        /// </summary>
        public static class Surface
        {
            public const string Primary = "rlds-surface-primary";
            public const string Secondary = "rlds-surface-secondary";
        }

        /// <summary>
        /// Divider CSS class names for RLDS design system.
        /// </summary>
        public static class Divider
        {
            public const string Base = "rlds-divider";
        }

        /// <summary>
        /// Code block CSS class names for RLDS design system.
        /// </summary>
        public static class CodeBlock
        {
            public const string Container = "rlds-code-block";
            public const string Label = "rlds-code-block__label";
            public const string Language = "rlds-code-block__language";
        }

        /// <summary>
        /// Toast CSS class names for RLDS design system.
        /// </summary>
        public static class Toast
        {
            public const string Root = "rlds-toast";
        }

        /// <summary>
        /// Icon button CSS class names for RLDS design system.
        /// </summary>
        public static class IconButton
        {
            public const string Root = "rlds-icon-button";
            public const string Absolute = "rlds-icon-button--absolute";
        }

        /// <summary>
        /// Utility CSS class names for RLDS design system.
        /// </summary>
        public static class Utilities
        {
            // Padding
            public const string PaddingMD = "rlds-padding-md";
            public static string Padding2xMD = "rlds-padding-2x-md";
            public const string PaddingSM = "rlds-padding-sm";
            public const string PaddingTopLG = "rlds-padding-top-lg";

            // Margin
            public const string MarginTopSM = "rlds-margin-top-sm";
            public const string MarginTopXS = "rlds-margin-top-xs";
            public const string MarginTop3XS = "rlds-margin-top-3xs";
            public const string MarginLG = "rlds-margin-lg";
            public const string NoMargin = "rlds-no-margin";

            // Position
            public const string AbsoluteFill = "rlds-absolute-fill";

            // Curson
            public const string CursorLink = "rlds-cursor-link";
        }

        /// <summary>
        /// Flexbox utility CSS class names for RLDS design system.
        /// </summary>
        public static class Flexbox
        {
            // Direction
            public const string Row = "rlds-flex-row";
            public const string Column = "rlds-flex-column";
            public const string RowReverse = "rlds-flex-row-reverse";
            public const string ColumnReverse = "rlds-flex-column-reverse";

            // Justify Content
            public const string JustifyStart = "rlds-justify-start";
            public const string JustifyEnd = "rlds-justify-end";
            public const string JustifyCenter = "rlds-justify-center";
            public const string JustifySpaceBetween = "rlds-justify-space-between";
            public const string JustifySpaceAround = "rlds-justify-space-around";

            // Align Items
            public const string AlignStart = "rlds-align-start";
            public const string AlignEnd = "rlds-align-end";
            public const string AlignCenter = "rlds-align-center";
            public const string AlignStretch = "rlds-align-stretch";

            // Align Self
            public const string SelfStart = "rlds-self-start";
            public const string SelfEnd = "rlds-self-end";
            public const string SelfCenter = "rlds-self-center";
            public const string SelfStretch = "rlds-self-stretch";

            // Grow & Shrink
            public const string Grow0 = "rlds-flex-grow-0";
            public const string Grow1 = "rlds-flex-grow-1";
            public const string Shrink0 = "rlds-flex-shrink-0";
            public const string Shrink1 = "rlds-flex-shrink-1";

            // Wrap
            public const string NoWrap = "rlds-flex-nowrap";
            public const string Wrap = "rlds-flex-wrap";
            public const string WrapReverse = "rlds-flex-wrap-reverse";

            // Common Patterns
            public const string RowCenter = "rlds-flex-row-center";
            public const string ColumnCenter = "rlds-flex-column-center";
            public const string RowSpaceBetween = "rlds-flex-row-space-between";
            public const string ColumnSpaceBetween = "rlds-flex-column-space-between";
            public const string RowStart = "rlds-flex-row-start";
            public const string RowEnd = "rlds-flex-row-end";
            public const string ColumnStart = "rlds-flex-column-start";
            public const string ColumnEnd = "rlds-flex-column-end";
        }
    }
}
