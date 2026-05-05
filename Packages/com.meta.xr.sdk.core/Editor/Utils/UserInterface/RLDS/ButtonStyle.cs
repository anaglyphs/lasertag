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

using UnityEngine;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    /// <summary>
    /// Represents a button style in the RLDS (Reality Labs Design System).
    /// Contains button properties from the design system and provides conversion to Unity's GUIStyle.
    /// </summary>
    public class ButtonStyle
    {
        /// <summary>
        /// Height of the button.
        /// </summary>
        public int Height { get; set; } = Styles.ButtonSize.Large.Height;

        /// <summary>
        /// Minimum width of the button.
        /// </summary>
        public int MinWidth { get; set; } = Styles.ButtonSize.Large.MinWidth;

        /// <summary>
        /// Horizontal padding of the button.
        /// </summary>
        public int HorizontalPadding { get; set; } = Styles.ButtonSize.Large.HorizontalPadding;

        /// <summary>
        /// Vertical padding of the button.
        /// </summary>
        public int VerticalPadding { get; set; } = Styles.ButtonSize.Large.VerticalPadding;

        /// <summary>
        /// Background color for normal state.
        /// </summary>
        public Color BackgroundColorNormal { get; set; } = Styles.Colors.ButtonPrimaryBackgroundDefault;

        /// <summary>
        /// Background color for hover state.
        /// </summary>
        public Color BackgroundColorHover { get; set; } = Styles.Colors.ButtonPrimaryBackgroundHover;

        /// <summary>
        /// Background color for disabled state.
        /// </summary>
        public Color BackgroundColorDisabled { get; set; } = Styles.Colors.ButtonDisableBackground;

        /// <summary>
        /// Text color for normal state.
        /// </summary>
        public Color TextColorNormal { get; set; } = Styles.Colors.TextUIPrimary;

        /// <summary>
        /// Text color for hover state.
        /// </summary>
        public Color TextColorHover { get; set; } = Styles.Colors.TextUIPrimary;

        /// <summary>
        /// Text color for disabled state.
        /// </summary>
        public Color TextColorDisabled { get; set; } = Styles.Colors.TextUIDisabled;

        /// <summary>
        /// Corner radius from the RLDS design system.
        /// Used by GUIStyle: No - GUIStyle doesn't support corner radius directly.
        /// Kept for RLDS design system consistency.
        /// </summary>
        public int CornerRadius { get; set; } = Styles.Radius.RadiusXS;

        /// <summary>
        /// Border width for the button
        /// </summary>
        public int BorderWidth { get; set; } = 0;

        /// <summary>
        /// Border color for the button
        /// </summary>
        public Color BorderColor { get; set; } = Color.white;

        /// <summary>
        /// Text style for the button.
        /// Used by GUIStyle: Yes - Font properties are applied from this TextStyle.
        /// </summary>
        public TextStyle TextStyle { get; set; }

        /// <summary>
        /// Implicit conversion to GUIStyle for simplified workflow.
        /// Allows ButtonStyle instances to be used directly in places where GUIStyles are expected.
        /// </summary>
        public static implicit operator GUIStyle(ButtonStyle buttonStyle)
        {
            return buttonStyle.ToGUIStyle();
        }

        /// <summary>
        /// Converts this ButtonStyle to a Unity GUIStyle.
        /// Applies all supported RLDS button properties to the GUIStyle.
        /// </summary>
        /// <returns>A GUIStyle configured with the supported properties from this ButtonStyle</returns>
        public GUIStyle ToGUIStyle()
        {
            var style = new GUIStyle();

            // Apply size properties
            style.fixedHeight = Height;
            style.margin = new RectOffset(0, 0, 0, 0);
            style.padding = new RectOffset(
                HorizontalPadding,
                HorizontalPadding,
                VerticalPadding,
                VerticalPadding);
            style.stretchWidth = true;
            style.stretchHeight = false;

            // Apply colors
            // Normal state
            style.normal.background = BackgroundColorNormal.ToTexture();
            style.normal.textColor = TextColorNormal;

            // Hover state
            style.hover.background = BackgroundColorHover.ToTexture();
            style.hover.textColor = TextColorHover;

            // Active state (same as hover)
            style.active.background = BackgroundColorHover.ToTexture();
            style.active.textColor = TextColorHover;

            // Apply text style properties if provided
            if (TextStyle != null)
            {
                var textStyle = TextStyle.ToGUIStyle();
                style.font = textStyle.font;
                style.fontSize = textStyle.fontSize;
                style.fontStyle = textStyle.fontStyle;
                style.richText = textStyle.richText;
            }

            // Set alignment
            style.alignment = TextAnchor.MiddleCenter;

            return style;
        }
    }
}
