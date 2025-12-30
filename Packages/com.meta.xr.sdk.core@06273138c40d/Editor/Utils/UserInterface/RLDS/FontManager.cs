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
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    /// <summary>
    /// Manages font loading and caching for the RLDS design system.
    /// </summary>
    internal static class FontManager
    {
        // Cache for loaded fonts to avoid repeated asset loading
        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        // Root path for the fonts, determined dynamically
        private static string _fontsRootPath;


        /// <summary>
        /// Gets the appropriate font based on weight and style.
        /// </summary>
        /// <param name="weight">The font weight</param>
        /// <param name="italic">Whether to use italic style</param>
        /// <param name="useOpticalSize80">Whether to use the optical size 80 variant</param>
        /// <returns>The loaded font asset, or null if not found</returns>
        public static Font GetFont(FontWeight weight, bool italic = false, bool useOpticalSize80 = false)
        {
            var weightName = GetWeightName(weight);
            var italicSuffix = italic ? "It" : "";
            var opticalSizeSuffix = useOpticalSize80 ? "80" : "";

            var fontName = $"Optimistic_{weightName}{italicSuffix}{opticalSizeSuffix}";

            // Check if the font is already cached
            if (FontCache.TryGetValue(fontName, out var cachedFont))
            {
                return cachedFont;
            }

            Font font = null;
            // Try to find the font path
            if (TryGetFontPath(fontName, out var fontPath))
            {
                // Load the font asset
                font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            }


            FontCache[fontName] = font;
            return font;
        }

        private static Font _fallbackFont;
        private static bool _fallbackFontInitialized = false;

        public static Font FallbackFont
        {
            get
            {
                if (_fallbackFontInitialized) return _fallbackFont;
                _fallbackFont = FetchFallbackFont();
                _fallbackFontInitialized = true;
                return _fallbackFont;
            }
        }

        public static Font FetchFallbackFont()
        {
            // Get all installed fonts on the system
            var installedFonts = Font.GetOSInstalledFontNames();

            var fontName = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX
                ? "SF Pro Display"
                : "Segoe UI";

            // First verify that the font actually exists on the system
            if (!System.Array.Exists(installedFonts,
                    name => name.Equals(fontName, System.StringComparison.OrdinalIgnoreCase))) return null;
            try
            {
                return Font.CreateDynamicFontFromOSFont(fontName, 14);
            }
            catch (System.Exception)
            {
                // Font exists but creation failed - continue to fallback logic
            }

            // If preferred font doesn't exist or creation failed, return null
            // Our TextStyle class will handle null fonts gracefully with its default rendering
            return null;
        }

        /// <summary>
        /// Tries to get the path to a font file.
        /// </summary>
        /// <param name="fontName">The name of the font file without extension</param>
        /// <param name="fontPath">The output path if found</param>
        /// <returns>True if the path was found, false otherwise</returns>
        private static bool TryGetFontPath(string fontName, out string fontPath)
        {
            fontPath = null;

            if (!TryGetFontsRootPath(out var rootPath)) return false;

            fontPath = Path.Combine(rootPath, $"{fontName}.ttf");
            return true;
        }

        /// <summary>
        /// Tries to get the root path for the fonts directory.
        /// </summary>
        /// <param name="rootPath">The output root path if found</param>
        /// <returns>True if the path was found, false otherwise</returns>
        private static bool TryGetFontsRootPath(out string rootPath)
        {
            rootPath = null;

            if (_fontsRootPath == null)
            {
                // Find the FontManager script
                var guids = AssetDatabase.FindAssets($"t:Script {nameof(FontManager)}");
                if (guids.Length > 0)
                {
                    var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);

                    // Navigate to the Fonts/Optimistic directory relative to the script location
                    var scriptDirectory = Path.GetDirectoryName(scriptPath);
                    _fontsRootPath = Path.Combine(scriptDirectory, "Fonts");
                }
            }

            rootPath = _fontsRootPath;
            return rootPath != null;
        }


        /// <summary>
        /// Converts a FontWeight enum value to its corresponding name in the font file.
        /// </summary>
        private static string GetWeightName(FontWeight weight)
        {
            return weight switch
            {
                FontWeight.Light => "Light",
                FontWeight.Regular => "Regular",
                FontWeight.Medium => "Medium",
                FontWeight.SemiBold => "SemiBold",
                FontWeight.Bold => "Bold",
                FontWeight.ExtraBold => "ExtraBold",
                _ => "Regular"
            };
        }
    }
}
