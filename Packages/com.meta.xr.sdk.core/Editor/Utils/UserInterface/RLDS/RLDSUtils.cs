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
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal static class RLDSUtils
    {
        private const string StyleSheetLightPath = "Assets/Oculus/VR/Editor/Utils/UserInterface/RLDS/StyleSheet-Light.uss";
        private const string StyleSheetDarkPath = "Assets/Oculus/VR/Editor/Utils/UserInterface/RLDS/StyleSheet-Dark.uss";

        public static StyleSheet LoadStyleSheet(bool isLightMode)
        {
            var styleSheetPath = isLightMode ? StyleSheetLightPath : StyleSheetDarkPath;
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath);
        }

        /// <summary>
        /// Gets the RLDS CSS class name for a button based on variant and size.
        /// </summary>
        /// <param name="variant">The button variant (Primary, Secondary, Tertiary, OnMedia)</param>
        /// <param name="size">The button size (Large, Small, XSmall)</param>
        /// <returns>The RLDS CSS class name using Props.Button constants</returns>
        public static string GetButtonStyleClass(Props.ButtonVariant variant, Props.ButtonSize size)
        {
            return (variant, size) switch
            {
                // Primary buttons
                (Props.ButtonVariant.Primary, Props.ButtonSize.Large) => Props.Button.Primary,
                (Props.ButtonVariant.Primary, Props.ButtonSize.Small) => Props.Button.PrimarySmall,
                (Props.ButtonVariant.Primary, Props.ButtonSize.XSmall) => Props.Button.PrimaryXSmall,

                // Secondary buttons
                (Props.ButtonVariant.Secondary, Props.ButtonSize.Large) => Props.Button.Secondary,
                (Props.ButtonVariant.Secondary, Props.ButtonSize.Small) => Props.Button.SecondarySmall,
                (Props.ButtonVariant.Secondary, Props.ButtonSize.XSmall) => Props.Button.SecondaryXSmall,

                // Tertiary buttons
                (Props.ButtonVariant.Tertiary, Props.ButtonSize.Large) => Props.Button.Tertiary,
                (Props.ButtonVariant.Tertiary, Props.ButtonSize.Small) => Props.Button.TertiarySmall,
                (Props.ButtonVariant.Tertiary, Props.ButtonSize.XSmall) => Props.Button.TertiaryXSmall,

                // OnMedia buttons
                (Props.ButtonVariant.OnMedia, Props.ButtonSize.Large) => Props.Button.OnMedia,
                (Props.ButtonVariant.OnMedia, Props.ButtonSize.Small) => Props.Button.OnMediaSmall,
                (Props.ButtonVariant.OnMedia, Props.ButtonSize.XSmall) => Props.Button.OnMediaXSmall,

                // Default fallback
                _ => Props.Button.Primary
            };
        }
    }
}
