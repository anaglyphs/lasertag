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

using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Indicator variant types following the RLDS design system.
    /// </summary>
    public enum IndicatorVariant
    {
        Positive,
        Negative,
        Warning,
        Disabled,
        Default,
        Privacy
    }

    /// <summary>
    /// A small circular indicator following the RLDS design system.
    /// Indicators are used to show status or state visually without text.
    /// </summary>
    internal class Indicator : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly IndicatorVariant _variant;

        /// <summary>
        /// Constructor for UIToolkit-based Indicator
        /// </summary>
        /// <param name="variant">The variant type (Positive, Negative, Warning, etc.)</param>
        public Indicator(IndicatorVariant variant = IndicatorVariant.Default)
        {
            _variant = variant;
        }

        /// <summary>
        /// Draw method for IMGUI - not implemented for Indicator
        /// </summary>
        public void Draw()
        {
            // Not implemented - Indicator is UIToolkit only
        }

        /// <summary>
        /// Creates a UIToolkit Indicator element with RLDS styling applied.
        /// </summary>
        /// <returns>A VisualElement containing the styled indicator</returns>
        public VisualElement Get()
        {
            var indicator = new VisualElement();
            indicator.AddToClassList(Props.Indicator.Base);

            // Add variant-specific class
            string variantClass = _variant switch
            {
                IndicatorVariant.Positive => Props.Indicator.Positive,
                IndicatorVariant.Negative => Props.Indicator.Negative,
                IndicatorVariant.Warning => Props.Indicator.Warning,
                IndicatorVariant.Disabled => Props.Indicator.Disabled,
                IndicatorVariant.Privacy => Props.Indicator.Privacy,
                _ => Props.Indicator.Default
            };

            indicator.AddToClassList(variantClass);

            return indicator;
        }
    }
}
