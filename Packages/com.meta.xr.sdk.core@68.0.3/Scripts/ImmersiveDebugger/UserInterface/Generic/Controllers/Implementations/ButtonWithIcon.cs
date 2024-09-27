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

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class ButtonWithIcon : Button
    {
        protected Icon _icon;
        protected Background _background;

        protected ImageStyle _backgroundStyle;
        public ImageStyle BackgroundStyle
        {
            set
            {
                if (_backgroundStyle == value) return;

                _backgroundStyle = value;

                _background.Sprite = _backgroundStyle.sprite;
                _background.PixelDensityMultiplier = _backgroundStyle.pixelDensityMultiplier;
                RefreshStyle();
            }
        }

        protected ImageStyle _iconStyle;
        public ImageStyle IconStyle
        {
            set
            {
                _iconStyle = value;
                RefreshStyle();
            }
        }

        public Texture2D Icon
        {
            set => _icon.Texture = value;
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            _background = Append<Background>("background");
            _background.LayoutStyle = Style.Load<LayoutStyle>("Fill");
            _icon = Append<Icon>("icon");
            _icon.LayoutStyle = Style.Load<LayoutStyle>("Fill");
        }

        protected override void OnHoverChanged()
        {
            base.OnHoverChanged();
            RefreshStyle();
        }

        protected void RefreshStyle()
        {
            UpdateBackground();
            UpdateIcon();
        }

        protected virtual void UpdateBackground()
        {
            if (_backgroundStyle != null && _backgroundStyle.enabled)
            {
                _background.Show();
                _background.Color = Hover ? _backgroundStyle.colorHover : _backgroundStyle.color;
            }
            else
            {
                _background.Hide();
            }
        }

        protected virtual void UpdateIcon()
        {
            if (_iconStyle != null && _iconStyle.enabled)
            {
                _icon.Show();
                _icon.Color = Hover ? _iconStyle.colorHover : _iconStyle.color;
            }
            else
            {
                _icon.Hide();
            }
        }
    }
}

