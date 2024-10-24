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


using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    public class DebugBar : OverlayCanvasPanel
    {
        private List<DebugPanel> _panels = new List<DebugPanel>();
        private Dictionary<DebugPanel, Toggle> _panelToggles = new Dictionary<DebugPanel, Toggle>();
        private Flex _buttonsAnchor;
        private Flex _miniButtonsAnchor;
        private Label _time;

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            // List of Panel Buttons
            _buttonsAnchor = Append<Flex>("buttons");
            _buttonsAnchor.LayoutStyle = Style.Load<LayoutStyle>("Buttons");

            // Anchor for Left Side
            var leftButtons = Append<Controller>("leftbuttons");
            leftButtons.LayoutStyle = Style.Load<LayoutStyle>("FillWithMargin");

            // Time Watch
            _time = leftButtons.Append<Label>("time");
            _time.LayoutStyle = Style.Load<LayoutStyle>("BarTime");
            _time.TextStyle = Style.Load<TextStyle>("BarTime");

            // Mini Buttons
            _miniButtonsAnchor = leftButtons.Append<Flex>("miniButtons");
            _miniButtonsAnchor.LayoutStyle = Style.Load<LayoutStyle>("MiniButtons");

            SetExpectedPixelsPerUnit(1000.0f, 10.0f, 2.24f);
        }

        public void RegisterPanel(DebugPanel panel)
        {
            if (panel == null) return;

            panel.OnVisibilityChangedEvent += OnPanelVisibilityChanged;
            _panels.Add(panel);

            var toggle = _buttonsAnchor.Append<Toggle>("PanelButton");
            toggle.Icon = panel.Icon;
            toggle.LayoutStyle = Style.Load<LayoutStyle>("PanelButton");
            toggle.BackgroundStyle = Style.Load<ImageStyle>("PanelButtonBackground");
            toggle.IconStyle = Style.Load<ImageStyle>("PanelButtonIcon");
            toggle.Callback = panel.ToggleVisibility;
            _panelToggles.Add(panel, toggle);
        }

        public Toggle RegisterControl(string buttonName, Texture2D icon, Action callback)
        {
            if (buttonName == null) throw new ArgumentNullException(nameof(buttonName));
            if (icon == null) throw new ArgumentNullException(nameof(icon));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var toggle = _miniButtonsAnchor.Append<Toggle>(buttonName);
            toggle.LayoutStyle = Style.Load<LayoutStyle>("MiniButton");
            toggle.Icon = icon;
            toggle.IconStyle = Style.Load<ImageStyle>("MiniButtonIcon");
            toggle.Callback = callback;
            return toggle;
        }

        private void OnPanelVisibilityChanged(Controller controller)
        {
            if (controller is DebugPanel panel && _panelToggles.TryGetValue(panel, out var toggle))
            {
                toggle.State = panel.Visibility;
            }
        }

        private void Update()
        {
            var elapsedTime = Time.realtimeSinceStartup;
            var minutes = (int)(elapsedTime / 60);
            var seconds = (int)(elapsedTime % 60);
            var formattedTime = $"{minutes:00}:{seconds:00}";
            _time.Content = formattedTime;
        }
    }
}

