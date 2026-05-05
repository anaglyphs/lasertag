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
    /// A progress bar component following the RLDS design system.
    /// Shows progress from 0 to 100 percent, with support for error states.
    /// </summary>
    internal class ProgressBar : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private float _progress;
        private readonly bool _error;
        private VisualElement _bar;

        /// <summary>
        /// Constructor for UIToolkit-based ProgressBar
        /// </summary>
        /// <param name="progress">Progress value from 0 to 100</param>
        /// <param name="error">Whether to show error state (red bar)</param>
        public ProgressBar(float progress, bool error = false)
        {
            _progress = UnityEngine.Mathf.Clamp(progress, 0f, 100f);
            _error = error;
        }

        /// <summary>
        /// Draw method for IMGUI - not implemented for ProgressBar
        /// </summary>
        public void Draw()
        {
            // Not implemented - ProgressBar is UIToolkit only
        }

        /// <summary>
        /// Updates the progress value dynamically.
        /// Must be called after Get() to have an effect.
        /// </summary>
        /// <param name="progress">New progress value from 0 to 100</param>
        public void SetProgress(float progress)
        {
            _progress = UnityEngine.Mathf.Clamp(progress, 0f, 100f);

            if (_bar != null)
            {
                _bar.style.width = Length.Percent(_progress);
            }
        }

        /// <summary>
        /// Gets the current progress value.
        /// </summary>
        /// <returns>Current progress from 0 to 100</returns>
        public float GetProgress()
        {
            return _progress;
        }

        /// <summary>
        /// Creates a UIToolkit ProgressBar element with RLDS styling applied.
        /// </summary>
        /// <returns>A VisualElement containing the styled progress bar</returns>
        public VisualElement Get()
        {
            var container = new VisualElement();
            container.AddToClassList(Props.ProgressBar.Container);

            _bar = new VisualElement();
            _bar.AddToClassList(Props.ProgressBar.Bar);

            // Add error class if needed
            if (_error)
            {
                _bar.AddToClassList(Props.ProgressBar.Error);
            }

            // Set width based on progress
            _bar.style.width = Length.Percent(_progress);

            container.Add(_bar);

            return container;
        }
    }
}
