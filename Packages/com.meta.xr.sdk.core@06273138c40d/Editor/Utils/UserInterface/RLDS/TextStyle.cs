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
    /// Font weight values from the RLDS design system.
    /// </summary>
    public enum FontWeight
    {
        Light = 300,
        Regular = 400,
        Medium = 500,
        SemiBold = 600,
        Bold = 700,
        ExtraBold = 800
    }

    /// <summary>
    /// Text transformation options from the RLDS design system.
    /// </summary>
    public enum TextTransform
    {
        None,
        Uppercase,
        Lowercase,
        Capitalize
    }

    /// <summary>
    /// Represents a text style in the RLDS (Reality Labs Design System).
    /// Contains typography properties from the design system and provides conversion to Unity's GUIStyle.
    /// </summary>
    public class TextStyle
    {
        /// <summary>
        /// Font weight from the RLDS design system.
        /// Used by GUIStyle: Yes - With FontManager, the actual font asset with the correct weight is loaded.
        /// </summary>
        public FontWeight FontWeight { get; set; }

        /// <summary>
        /// Whether to use italic style.
        /// Used by GUIStyle: Yes - With FontManager, the actual italic font asset is loaded.
        /// </summary>
        public bool Italic { get; set; }

        /// <summary>
        /// Whether to use the optical size 80 variant of the font.
        /// Used by GUIStyle: Yes - With FontManager, the appropriate optical size variant is loaded.
        /// </summary>
        public bool UseOpticalSize80 { get; set; }

        /// <summary>
        /// Font size in pixels.
        /// Used by GUIStyle: Yes - Directly mapped to GUIStyle.fontSize.
        /// </summary>
        public int FontSize { get; set; }

        /// <summary>
        /// Line height in pixels from the RLDS design system.
        /// Used by GUIStyle: No - GUIStyle doesn't support custom line height.
        /// Kept for RLDS design system consistency and potential future implementation.
        /// </summary>
        public int LineHeight { get; set; }

        /// <summary>
        /// Letter spacing (tracking) from the RLDS design system.
        /// Used by GUIStyle: No - GUIStyle doesn't support letter spacing.
        /// Kept for RLDS design system consistency and potential future implementation.
        /// </summary>
        public float LetterSpacing { get; set; }

        /// <summary>
        /// Text transformation (uppercase, lowercase, etc.) from the RLDS design system.
        /// Used by GUIStyle: No - GUIStyle doesn't support text transformation.
        /// Kept for RLDS design system consistency and potential future implementation.
        /// </summary>
        public TextTransform TextTransform { get; set; } = TextTransform.None;

        /// <summary>
        /// Implicit conversion to GUIStyle for simplified workflow.
        /// Allows TextStyle instances to be used directly in places where GUIStyles are expected.
        /// </summary>
        public static implicit operator GUIStyle(TextStyle textStyle)
        {
            return textStyle.ToGUIStyle();
        }

        /// <summary>
        /// Converts this TextStyle to a Unity GUIStyle.
        /// With FontManager, all RLDS typography properties related to font weight, style, and optical size
        /// are properly applied by loading the appropriate font asset.
        /// </summary>
        /// <returns>A GUIStyle configured with the supported properties from this TextStyle</returns>
        public GUIStyle ToGUIStyle()
        {
            var style = new GUIStyle()
            {
                fontSize = FontSize,
                richText = true
            };

            // Load the appropriate font based on weight, style, and optical size
            var font = FontManager.GetFont(FontWeight, Italic, UseOpticalSize80);
            if (font != null)
            {
                style.font = font;
                // When using a custom font, we don't need to set fontStyle as the font itself has the correct weight and style
                style.fontStyle = FontStyle.Normal;
            }
            else
            {
                font = FontManager.FallbackFont;
                if (font != null)
                {
                    style.font = font;
                }

                // Fall back to Unity's built-in font styles if the font couldn't be loaded
                style.fontStyle = Italic ? FontStyle.Italic :
                                 (FontWeight == FontWeight.Bold || FontWeight == FontWeight.ExtraBold) ?
                                 FontStyle.Bold : FontStyle.Normal;
            }

            // Apply text transformation if supported
            if (TextTransform != TextTransform.None && style.richText)
            {
                // We can simulate text transformation using rich text tags if richText is enabled
                switch (TextTransform)
                {
                    case TextTransform.Uppercase:
                        style.normal.textColor = style.normal.textColor; // This is just to ensure the style is properly initialized
                        // The actual transformation will be applied when the text is displayed
                        break;
                    case TextTransform.Lowercase:
                        style.normal.textColor = style.normal.textColor;
                        break;
                    case TextTransform.Capitalize:
                        style.normal.textColor = style.normal.textColor;
                        break;
                }
            }

            return style;
        }

        /// <summary>
        /// Applies text transformation to the given text based on the TextTransform property.
        /// This should be used when displaying text with this style.
        /// </summary>
        /// <param name="text">The text to transform</param>
        /// <returns>The transformed text</returns>
        public string ApplyTextTransform(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            switch (TextTransform)
            {
                case TextTransform.Uppercase:
                    return text.ToUpper();
                case TextTransform.Lowercase:
                    return text.ToLower();
                case TextTransform.Capitalize:
                    return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
                default:
                    return text;
            }
        }
    }
}
