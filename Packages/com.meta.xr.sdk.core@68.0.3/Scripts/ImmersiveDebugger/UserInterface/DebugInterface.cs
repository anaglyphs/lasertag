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


using System;
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    public class DebugInterface : Interface
    {
        private DebugBar _bar;

        private Toggle _showAllButton;
        private Toggle _followButton;
        private Toggle _rotateButton;
        private Toggle _opacityButton;
        private Toggle _distanceButton;
        private InspectorPanel _inspectorPanel;
        private Console _console;
        private int _distanceToggleIndex;
        private readonly int _distanceOptionSize = Enum.GetValues(typeof(RuntimeSettings.DistanceOption)).Length;

        protected override bool FollowOverride
        {
            get => _followButton.State;
            set => _followButton.State = value;
        }

        protected override bool RotateOverride
        {
            get => _rotateButton.State;
            set => _rotateButton.State = value;
        }

        private bool OpacityOverride
        {
            get => _opacityButton.State;
            set
            {
                _opacityButton.State = value;

                foreach (var child in Children)
                {
                    if (child is Panel panel)
                    {
                        panel.Transparent = !OpacityOverride;
                    }
                }
            }
        }

        public override void Awake()
        {
            base.Awake();

            _inspectorPanel = Append<InspectorPanel>("inspectors");
            _inspectorPanel.LayoutStyle = Style.Load<LayoutStyle>("InspectorPanel");
            _inspectorPanel.BackgroundStyle = Style.Load<ImageStyle>("PanelBackground");
            _inspectorPanel.Title = "Inspectors";
            _inspectorPanel.Icon = Resources.Load<Texture2D>("Textures/inspectors_icon");
            _inspectorPanel.SetPanelPosition(RuntimeSettings.Instance.PanelDistance, true);

            _console = Append<Console>("console");
            _console.LayoutStyle = Style.Load<LayoutStyle>("ConsolePanel");
            _console.BackgroundStyle = Style.Load<ImageStyle>("PanelBackground");
            _console.Title = "Console";
            _console.Icon = Resources.Load<Texture2D>("Textures/console_icon");
            _console.SetPanelPosition(RuntimeSettings.Instance.PanelDistance, true);
            _distanceToggleIndex = (int)RuntimeSettings.Instance.PanelDistance;

            _bar = Append<DebugBar>("bar");
            _bar.LayoutStyle = Style.Load<LayoutStyle>("Bar");
            _bar.BackgroundStyle = Style.Load<ImageStyle>("BarBackground");
            _bar.SphericalCoordinates = new Vector3(0.7f, 0.0f, -0.5f);
            _bar.RegisterPanel(_console);
            _bar.RegisterPanel(_inspectorPanel);

            _opacityButton = _bar.RegisterControl("opacity", Resources.Load<Texture2D>("Textures/opacity_icon"),
                (() => OpacityOverride = !OpacityOverride));
            _followButton = _bar.RegisterControl("followMove", Resources.Load<Texture2D>("Textures/move_icon"),
                            (() => FollowOverride = !FollowOverride));
            _rotateButton = _bar.RegisterControl("followRotation", Resources.Load<Texture2D>("Textures/rotate_icon"),
                (() => RotateOverride = !RotateOverride));
            _distanceButton = _bar.RegisterControl("setDistance", Resources.Load<Texture2D>("Textures/shift_icon"), ToggleDistances);
            _distanceButton.State = true;

            var runtimeSettings = RuntimeSettings.Instance;

            FollowOverride = runtimeSettings.FollowOverride;
            RotateOverride = runtimeSettings.RotateOverride;
            OpacityOverride = true;

            if (runtimeSettings.ShowInspectors)
            {
                _inspectorPanel.Show();
            }

            if (runtimeSettings.ShowConsole)
            {
                _console.Show();
            }

            if (runtimeSettings.ImmersiveDebuggerDisplayAtStartup)
            {
                Show();
            }
            else
            {
                Hide();
            }

            var debugManager = DebugManager.Instance;
            if (debugManager != null)
            {
                debugManager.OnUpdateAction += UpdateVisibility;
            }
        }

        private void ToggleDistances()
        {
            _distanceToggleIndex = ++_distanceToggleIndex % _distanceOptionSize;
            _inspectorPanel.SetPanelPosition((RuntimeSettings.DistanceOption)_distanceToggleIndex);
            _console.SetPanelPosition((RuntimeSettings.DistanceOption)_distanceToggleIndex);
        }

        private void UpdateVisibility()
        {
            if (OVRInput.GetDown(RuntimeSettings.Instance.ImmersiveDebuggerToggleDisplayButton))
            {
                ToggleVisibility();
            }
        }
    }
}

