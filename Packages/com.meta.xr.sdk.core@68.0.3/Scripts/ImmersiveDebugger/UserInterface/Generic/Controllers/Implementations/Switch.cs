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
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class Switch : ButtonWithIcon
    {
        private Texture2D _toggleIconOn;
        private Texture2D _toggleIconOff;

        public Tweak Tweak { get; set; }
        public bool State
        {
            get => Tweak != null && Math.Abs(Tweak.Tween - 1.0f) < Mathf.Epsilon;
            set
            {
                Tweak.Tween = value ? 1.0f : 0.0f;
                OnStateChanged();
            }
        }

        public Action<bool> StateChanged { get; set; }

        private void OnStateChanged()
        {
            StateChanged?.Invoke(State);
            RefreshStyle();
        }

        private void Start()
        {
            State = Tweak.Tween > 0;
            UpdateIcon();
        }

        internal void SetToggleIcons(Texture2D onState, Texture2D offState)
        {
            _toggleIconOn = onState;
            _toggleIconOff = offState;
        }

        protected override void UpdateIcon()
        {
            Icon = State ? _toggleIconOn : _toggleIconOff;
            _icon.Color = Hover ? _iconStyle.colorHover : State ? _iconStyle.color : _iconStyle.colorOff;
        }
    }
}

