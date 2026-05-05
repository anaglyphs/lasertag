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
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    /// <summary>
    /// Ring size types for RLDS Spinner (animated progress ring).
    /// </summary>
    public enum RingSize
    {
        Size12 = 12,
        Size16 = 16,
        Size24 = 24,
        Size32 = 32
    }

    /// <summary>
    /// Ring color types for RLDS Spinner (animated progress ring).
    /// </summary>
    public enum RingColor
    {
        Default,
        Accent,
        Disabled,
        Dark,
        Light
    }

    /// <summary>
    /// RLDS Spinner - An animated indeterminate progress ring indicator.
    /// Indicates that the app is making progress on loading content.
    /// Based on BaseProgressRingIndeterminate from React RLDS.
    /// </summary>
    public class Spinner : IUserInterfaceItem
    {
        private readonly RingSize _size;
        private readonly RingColor _color;
        private VisualElement _ring;
        private IVisualElementScheduledItem _rotationScheduler;
        private float _currentRotation = 0f;
        private readonly string[] _styles;
        private VisualElement _container;

        // Animation constants
        private float RotationSpeed { get; set; } = 360f; // Degrees per second (one full rotation per second)

        /// <summary>
        /// Constructor for RLDS Spinner (animated progress ring)
        /// </summary>
        /// <param name="size">Size of the spinner ring (12, 16, 24, or 32 pixels)</param>
        /// <param name="color">Color variant for the spinner ring</param>
        /// <param name="rotationSpeed">Degrees per second (one full rotation per second)</param>
        public Spinner(RingSize size = RingSize.Size24, RingColor color = RingColor.Default, float rotationSpeed = 360f, params string[] styles)
        {
            _size = size;
            _color = color;
            _styles = styles;
            RotationSpeed = rotationSpeed;
        }

        /// <summary>
        /// Starts the spinner animation.
        /// This is called automatically when Get() is called.
        /// </summary>
        private void StartAnimation()
        {
            if (_ring == null || _rotationScheduler != null)
                return;

            // Use the scheduler to animate rotation
            _rotationScheduler = _ring.schedule.Execute(() =>
            {
                // Update rotation (deltaTime is in milliseconds, convert to seconds)
                _currentRotation += RotationSpeed * (1f / 60f); // Assuming ~60 FPS
                if (_currentRotation >= 360f)
                    _currentRotation -= 360f;

                // Apply rotation using the modern style.rotate property
                _ring.style.rotate = new Rotate(new Angle(_currentRotation, AngleUnit.Degree));
            }).Every(16); // ~60 FPS (16ms per frame)
        }

        /// <summary>
        /// Stops the spinner animation.
        /// Call this when the spinner is no longer visible or needed.
        /// </summary>
        public void StopAnimation()
        {
            _rotationScheduler?.Pause();
            _rotationScheduler = null;
        }

        public void Draw()
        {
        }

        public bool Hide { get; set; }

        /// <summary>
        /// Creates a UIToolkit VisualElement representing the animated spinner ring.
        /// The animation starts automatically when this method is called.
        /// </summary>
        /// <returns>A VisualElement containing the styled animated spinner</returns>
        public VisualElement Get()
        {
            if (_container != null)
            {
                return _container;
            }
            _container = new VisualElement();
            _container.AddToClassList(Props.RingSpinner.Root);
            foreach (var style in _styles)
            {
                _container.AddToClassList(style);
            }

            _ring = new VisualElement();
            _ring.AddToClassList(Props.RingSpinner.Ring);

            // Add size class
            var sizeClass = _size switch
            {
                RingSize.Size12 => Props.RingSpinner.Size12,
                RingSize.Size16 => Props.RingSpinner.Size16,
                RingSize.Size24 => Props.RingSpinner.Size24,
                RingSize.Size32 => Props.RingSpinner.Size32,
                _ => Props.RingSpinner.Size24
            };
            _ring.AddToClassList(sizeClass);

            // Add color class
            var colorClass = _color switch
            {
                RingColor.Accent => Props.RingSpinner.Accent,
                RingColor.Disabled => Props.RingSpinner.Disabled,
                RingColor.Dark => Props.RingSpinner.Dark,
                RingColor.Light => Props.RingSpinner.Light,
                _ => Props.RingSpinner.Default
            };
            _ring.AddToClassList(colorClass);

            _container.Add(_ring);

            // Start animation when the element is attached to the panel
            _container.RegisterCallback<AttachToPanelEvent>(evt => StartAnimation());

            // Stop animation when the element is detached from the panel
            _container.RegisterCallback<DetachFromPanelEvent>(evt => StopAnimation());

            return _container;
        }
    }
}
