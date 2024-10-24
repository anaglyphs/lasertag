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
using Meta.XR.ImmersiveDebugger.Manager;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    public class Value : Controller
    {
        protected Label _label;
        protected Background _background;
        public Background Background => _background;

        protected ImageStyle _backgroundStyle;
        public ImageStyle BackgroundStyle
        {
            set
            {
                _backgroundStyle = value;
                _background.Sprite = value.sprite;
                _background.PixelDensityMultiplier = value.pixelDensityMultiplier;
                RefreshStyle();
            }
        }

        public TextStyle TextStyle
        {
            set => _label.TextStyle = value;
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            _background = Append<Background>("background");
            _background.LayoutStyle = Style.Load<LayoutStyle>("Fill");

            _label = Append<Label>("label");
            _label.LayoutStyle = Style.Load<LayoutStyle>("Fill");
        }

        protected void RefreshStyle()
        {
            UpdateBackground();
        }

        protected virtual void UpdateBackground()
        {
            if (_backgroundStyle != null && _backgroundStyle.enabled)
            {
                _background.Show();
                _background.Color = _backgroundStyle.color;
            }
            else
            {
                _background.Hide();
            }
        }

        public string Content
        {
            get => _label.Content;
            set => _label.Content = value;
        }

    }
}

