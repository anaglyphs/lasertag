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


namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class Toggle : ButtonWithIcon
    {
        private bool _state;

        public bool State
        {
            get => _state;
            set
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
                OnStateChanged();
            }
        }

        public Action<bool> StateChanged { get; set; }

        private void OnStateChanged()
        {
            StateChanged?.Invoke(State);
            RefreshStyle();
        }

        protected override void UpdateBackground()
        {
            if (_backgroundStyle != null && _backgroundStyle.enabled)
            {
                _background.Show();
                _background.Color = Hover ? _backgroundStyle.colorHover : State ? _backgroundStyle.colorHover : _backgroundStyle.color;
            }
            else
            {
                _background.Hide();
            }
        }

        protected override void UpdateIcon()
        {
            if (_iconStyle != null && _iconStyle.enabled)
            {
                _icon.Show();
                _icon.Color = Hover ? _iconStyle.colorHover : State ? _iconStyle.color : _iconStyle.colorOff;
            }
            else
            {
                _icon.Hide();
            }
        }
    }
}

