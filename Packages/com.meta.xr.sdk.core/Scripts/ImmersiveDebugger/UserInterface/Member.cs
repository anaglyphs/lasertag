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


using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using System;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    internal class Member : Controller, IMember
    {
        private Label _title;
        private Flex _flex;
        private Flex _valueFlex;

        private Values _values;
        private ButtonForAction _action;
        private Slider _slider;
        private Switch _switch;
        private ToggleForGizmo _gizmo;
        private Background _pill;

        public string Title
        {
            get => _title.Content;
            set => _title.Content = value.ToDisplayText();
        }

        public Color PillColor
        {
            set => _pill.Color = value;
        }

        public ImageStyle PillStyle
        {
            set
            {
                _pill.Sprite = value.sprite;
                _pill.Color = value.color;
                _pill.PixelDensityMultiplier = value.pixelDensityMultiplier;
            }
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            // Flex
            _flex = Append<Flex>("list");
            _flex.LayoutStyle = Style.Load<LayoutStyle>("MemberFlex");

            // Pill
            _pill = _flex.Append<Background>("pill");
            _pill.LayoutStyle = Style.Load<LayoutStyle>("PillVertical");
            PillStyle = Style.Load<ImageStyle>("PillInfo");

            // Label
            _title = _flex.Append<Label>("title");
            _title.LayoutStyle = Style.Load<LayoutStyle>("MemberTitle");
            _title.TextStyle = Style.Load<TextStyle>("MemberTitle");

            // Flex
            _valueFlex = Append<Flex>("values");
            _valueFlex.LayoutStyle = Style.Load<LayoutStyle>("MemberValueFlex");
        }

        public ActionHook GetAction()
        {
            return _action != null ? _action.Action : null;
        }

        public void RegisterAction(ActionHook action)
        {
            if (_action == null)
            {
                _action = _valueFlex.Append<ButtonForAction>("action");
                _action.LayoutStyle = Style.Load<LayoutStyle>("MemberAction");
                _action.TextStyle = Style.Load<TextStyle>("MemberValue");
                _action.BackgroundStyle = Style.Load<ImageStyle>("MemberActionBackground");
                _action.Label = Title;

                _flex.Hide();
            }

            _action.Action = action;
        }

        public GizmoHook GetGizmo()
        {
            return _gizmo != null ? _gizmo.Hook : null;
        }

        public void RegisterGizmo(GizmoHook gizmo)
        {
            if (_gizmo == null)
            {
                _gizmo = _valueFlex.Append<ToggleForGizmo>("gizmo");
                _gizmo.LayoutStyle = Style.Load<LayoutStyle>("MemberButton");
                _gizmo.Icon = Resources.Load<Texture2D>("Textures/eye_icon");
                _gizmo.IconStyle = Style.Load<ImageStyle>("MiniButtonIcon");
            }

            _gizmo.Hook = gizmo;
        }

        public Watch GetWatch()
        {
            return _values != null ? _values.Watch : null;
        }

        public void RegisterWatch(Watch watch)
        {
            if (_values == null)
            {
                _values = _valueFlex.Append<Values>("watch");
            }

            _values.Setup(watch);
        }

        public Tweak GetTweak()
        {
            return _slider != null ? _slider.Tweak : null;
        }

        public void RegisterTweak(Tweak tweak)
        {
            switch (tweak)
            {
                case Tweak<float>:
                case Tweak<int>:
                    AddSlider(tweak);
                    break;
                case Tweak<bool>:
                    AddToggle(tweak);
                    break;
            }
        }

        private void AddToggle(Tweak tweak)
        {
            if (_switch == null)
            {
                _switch = _valueFlex.Prepend<Switch>("switch");
                _switch.LayoutStyle = Style.Load<LayoutStyle>("MemberButtonToggle");
                _switch.SetToggleIcons(Resources.Load<Texture2D>("Textures/toggle_on"), Resources.Load<Texture2D>("Textures/toggle_off"));
                _switch.IconStyle = Style.Load<ImageStyle>("ToggleButtonIcon");
                _switch.Callback = () => _switch.State = !_switch.State;
            }

            _switch.Tweak = tweak;
        }

        private void AddSlider(Tweak tweak)
        {
            if (_slider == null)
            {
                _slider = _valueFlex.Append<Slider>("slider");
                _slider.LayoutStyle = Style.Load<LayoutStyle>("MemberSlider");
                _slider.EmptyBackgroundStyle = Style.Load<ImageStyle>("MemberValueBackground");
                _slider.FillBackgroundStyle = Style.Load<ImageStyle>("MemberActionBackground");
            }

            _slider.Tweak = tweak;
        }
    }
}

