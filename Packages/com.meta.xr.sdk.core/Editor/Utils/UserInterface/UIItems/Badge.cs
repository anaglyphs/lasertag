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
    /// Badge variant types following the RLDS design system.
    /// Uses the same variants as IndicatorVariant for consistency.
    /// </summary>
    public enum BadgeVariant
    {
        Positive,
        Negative,
        Warning,
        Disabled,
        Default,
        Privacy
    }

    /// <summary>
    /// A badge component following the RLDS design system.
    /// Badges are used to display status, categories, or tags with text labels.
    /// </summary>
    internal class Badge : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly string _label;
        private readonly BadgeVariant _variant;

        /// <summary>
        /// Constructor for UIToolkit-based Badge
        /// </summary>
        /// <param name="label">The text label to display on the badge</param>
        /// <param name="variant">The variant type (Positive, Negative, Warning, etc.)</param>
        public Badge(string label, BadgeVariant variant = BadgeVariant.Default)
        {
            _label = label;
            _variant = variant;
        }

        /// <summary>
        /// Draw method for IMGUI - not implemented for Badge
        /// </summary>
        public void Draw()
        {
            // Not implemented - Badge is UIToolkit only
        }

        /// <summary>
        /// Creates a UIToolkit Badge element with RLDS styling applied.
        /// </summary>
        /// <returns>A VisualElement containing the styled badge with label</returns>
        public VisualElement Get()
        {
            var badge = new VisualElement();
            badge.AddToClassList(Props.Badge.Base);

            // Add variant-specific class
            string variantClass = _variant switch
            {
                BadgeVariant.Positive => Props.Badge.Positive,
                BadgeVariant.Negative => Props.Badge.Negative,
                BadgeVariant.Warning => Props.Badge.Warning,
                BadgeVariant.Disabled => Props.Badge.Disabled,
                BadgeVariant.Privacy => Props.Badge.Privacy,
                _ => Props.Badge.Default
            };

            badge.AddToClassList(variantClass);

            // Add label
            var labelElement = new UnityEngine.UIElements.Label(_label);
            labelElement.AddToClassList(Props.Badge.Label);
            badge.Add(labelElement);

            return badge;
        }
    }
}
