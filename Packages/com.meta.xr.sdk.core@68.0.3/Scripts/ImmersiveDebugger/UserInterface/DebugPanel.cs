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
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    public class DebugPanel : OverlayCanvasPanel
    {
        private Label _title;
        private ButtonWithIcon _closeIcon;

        public Texture2D Icon { get; set; }

        public string Title
        {
            get => _title.Content;
            set => _title.Content = value;
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            _title = Append<Label>("title");
            _title.LayoutStyle = Style.Load<LayoutStyle>("PanelTitle");
            _title.TextStyle = Style.Load<TextStyle>("PanelTitle");

            _closeIcon = Append<ButtonWithIcon>("CloseButton");
            _closeIcon.LayoutStyle = Style.Load<LayoutStyle>("CloseButton");
            _closeIcon.BackgroundStyle = Style.Load<ImageStyle>("CloseButtonBackground");
            _closeIcon.Icon = Resources.Load<Texture2D>("Textures/minimize_icon");
            _closeIcon.IconStyle = Style.Load<ImageStyle>("CloseButtonIcon");
            _closeIcon.Callback = Hide;

            SetExpectedPixelsPerUnit(1000.0f, 10.0f, 2.24f);

            Hide();
        }
    }
}

