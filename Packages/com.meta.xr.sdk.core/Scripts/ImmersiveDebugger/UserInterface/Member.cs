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
using System.Collections.Generic;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    internal class Member : Controller, IMember
    {
        private Label _title;
        private TextArea _description;

        private Flex _flex;
        private Flex _valueFlex;
        private Flex _verticalFlex;
        private Flex _childrenFlex;

        private Values _values;
        private ButtonForAction _action;
        private Slider _slider;
        private Switch _switch;
        private ToggleForGizmo _gizmo;
        private Background _pill;
        private ImageStyle _pillBackgroundStyle;
        private Toggle _foldoutToggle;
        private ImageStyle _foldoutIconStyle;

        private Color _defaultPillColor;
        private Color _transparentPillColor;

        private readonly List<Member> _childMembers = new();
        private bool _isFoldout;
        private bool _pillCreated;

        public string Title
        {
            get => _title.Content;
            set => _title.Content = value.ToDisplayText();
        }

        public string Description
        {
            get => _description.Content;
            set => _description.Content = value;
        }

        public Color PillColor
        {
            set
            {
                _defaultPillColor = value;
                _transparentPillColor = value;
                _transparentPillColor.a = 0.8f;

                if (_isFoldout)
                {
                    // For foldouts, apply the color to the foldout icon instead
                    UpdateFoldoutIconColor();
                }
                else
                {
                    EnsurePillCreated();
                    _pill.Color = Transparent ? _transparentPillColor : _defaultPillColor;
                }
            }
        }

        public ImageStyle PillStyle
        {
            set
            {
                EnsurePillCreated();
                _pill.Sprite = value.sprite;
                _pill.Color = value.color;
                _pill.PixelDensityMultiplier = value.pixelDensityMultiplier;
            }
        }

        public bool IsFoldout => _isFoldout;

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            // Flex
            _flex = Append<Flex>("list");
            _flex.LayoutStyle = Style.Load<LayoutStyle>("MemberFlex");

            // Label (pill will be created lazily and prepended before title if needed)
            _title = _flex.Append<Label>("title");
            _title.LayoutStyle = Style.Load<LayoutStyle>("MemberTitle");
            _title.TextStyle = Style.Load<TextStyle>("MemberTitle");

            // Vertical flex
            _verticalFlex = Append<Flex>("vertical");
            _verticalFlex.LayoutStyle = Style.Load<LayoutStyle>("VerticalValueFlex");

            // Value Flex
            _valueFlex = _verticalFlex.Append<Flex>("values");
            _valueFlex.LayoutStyle = Style.Instantiate<LayoutStyle>("MemberValueFlex");
        }

        /// <summary>
        /// Lazily creates the pill if it hasn't been created yet.
        /// Call this before any operation that needs the pill.
        /// </summary>
        private void EnsurePillCreated()
        {
            if (_pillCreated || _isFoldout)
            {
                return;
            }

            _pillCreated = true;

            // Prepend pill before the title
            _pill = _flex.Prepend<Background>("pill");
            _pill.LayoutStyle = Style.Load<LayoutStyle>("PillVertical");
            _pillBackgroundStyle = Style.Load<ImageStyle>("PillInfo");
            PillStyle = _pillBackgroundStyle;
        }

        public void RegisterDescriptor()
        {
            _description = _verticalFlex.Append<TextArea>("description");
            _description.Label.LayoutStyle.margin = new Vector2(4, 4);
            _description.Background.LayoutStyle.margin = new Vector2(0, 0);

            _description.LayoutStyle = Style.Instantiate<LayoutStyle>("MemberDescriptor");
            _description.TextStyle = Style.Load<TextStyle>("MemberDescriptorValue");
            _description.BackgroundStyle = Style.Load<ImageStyle>("MemberDescriptionBackground");
            RefreshLayout();
        }

        protected override void OnTransparencyChanged()
        {
            base.OnTransparencyChanged();
            if (_pillCreated && _pill != null)
            {
                _pill.Color = Transparent ? _transparentPillColor : _defaultPillColor;
            }
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

                var title = string.IsNullOrEmpty(action.Attribute.DisplayName) ? $"{action.MemberInfo.Name}" : action.Attribute.DisplayName;
                _action.Label = title.ToDisplayText(Utils.MaxLetterCountForMethod);

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
                _gizmo.Icon = Resources.Load<UnityEngine.Texture2D>("Textures/eye_icon");
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

        public void RegisterEnum(TweakEnum tweak)
        {
            var dropdown = _valueFlex.Append<Dropdown>("dropdown");
            dropdown.LayoutStyle = Style.Instantiate<LayoutStyle>("DropdownMemberValue");
            dropdown.SetupMenu(tweak);
        }

        public void RegisterTexture(WatchTexture watchTexture)
        {
            var texture = _valueFlex.Append<Image>("texture");
            texture.LayoutStyle = Style.Instantiate<LayoutStyle>("TextureValue");
            texture.Setup(watchTexture);
            RefreshLayout();
        }

        public Tweak GetTweak()
        {
            if (_slider != null)
            {
                return _slider.Tweak;
            }
            if (_switch != null)
            {
                return _switch.Tweak;
            }
            return null;
        }

        public void RegisterTweak(Tweak tweak)
        {
            // Check the generic type to support both Tweak<T> and NestedTweak<T>
            var tweakType = tweak.GetType();
            if (tweakType.IsGenericType)
            {
                var genericTypeDef = tweakType.GetGenericTypeDefinition();
                var typeArg = tweakType.GetGenericArguments()[0];

                if (genericTypeDef == typeof(Tweak<>) || genericTypeDef == typeof(NestedTweak<>))
                {
                    if (typeArg == typeof(float) || typeArg == typeof(int))
                    {
                        AddSlider(tweak);
                        return;
                    }
                    if (typeArg == typeof(bool))
                    {
                        AddToggle(tweak);
                        return;
                    }
                }
            }
        }

        private void AddToggle(Tweak tweak)
        {
            if (_switch == null)
            {
                _switch = _valueFlex.Prepend<Switch>("switch");
                _switch.LayoutStyle = Style.Load<LayoutStyle>("MemberButtonToggle");
                _switch.SetToggleIcons(Resources.Load<UnityEngine.Texture2D>("Textures/toggle_on"), Resources.Load<UnityEngine.Texture2D>("Textures/toggle_off"));
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

        /// <summary>
        /// Converts this member into a foldout that can contain child members.
        /// Replaces the colored pill with a clickable foldout toggle and creates a container for child members.
        /// </summary>
        public void SetupAsFoldout()
        {
            if (_isFoldout)
            {
                return;
            }

            _isFoldout = true;

            // If the pill was already created, remove and destroy it since foldout replaces it
            if (_pillCreated && _pill != null)
            {
                _flex.Remove(_pill, true);
                _pill = null;
            }

            // Create a custom icon style with the pill color that doesn't change on state/hover
            _foldoutIconStyle = ScriptableObject.CreateInstance<ImageStyle>();
            _foldoutIconStyle.enabled = true;
            _foldoutIconStyle.color = _defaultPillColor;
            _foldoutIconStyle.colorHover = _defaultPillColor;
            _foldoutIconStyle.colorOff = _defaultPillColor;

            // Add foldout toggle before the title (Prepend puts it first in flex order)
            _foldoutToggle = _flex.Prepend<Toggle>("foldout");
            _foldoutToggle.LayoutStyle = Style.Load<LayoutStyle>("FoldoutCentered");
            _foldoutToggle.Icon = Resources.Load<Texture2D>("Textures/caret_right_icon");
            _foldoutToggle.IconStyle = _foldoutIconStyle;
            _foldoutToggle.StateChanged = OnFoldoutStateChanged;
            _foldoutToggle.Callback = _foldoutToggle.ToggleState;
            _foldoutToggle.State = false;

            // Make the title clickable by enabling raycast and adding a click overlay
            _title.Text.raycastTarget = true;
            var clickHandler = _title.GameObject.AddComponent<PointerHandler>();
            clickHandler.Controller = _foldoutToggle;

            // Create children flex container
            _childrenFlex = Append<Flex>("children");
            _childrenFlex.LayoutStyle = Style.Instantiate<LayoutStyle>("MemberChildrenFlex");

            // Start collapsed
            OnFoldoutStateChanged(false);
        }

        /// <summary>
        /// Updates the foldout icon color to match the pill color.
        /// </summary>
        private void UpdateFoldoutIconColor()
        {
            if (_foldoutIconStyle != null)
            {
                _foldoutIconStyle.color = _defaultPillColor;
                _foldoutIconStyle.colorHover = _defaultPillColor;
                _foldoutIconStyle.colorOff = _defaultPillColor;

                // Re-apply the style to trigger a refresh
                if (_foldoutToggle != null)
                {
                    _foldoutToggle.IconStyle = _foldoutIconStyle;
                }
            }
        }

        private void OnFoldoutStateChanged(bool state)
        {
            if (_foldoutToggle != null)
            {
                _foldoutToggle.Icon = Resources.Load<Texture2D>(state ? "Textures/caret_down_icon" : "Textures/caret_right_icon");
            }

            if (_childrenFlex == null)
            {
                return;
            }

            if (state)
            {
                foreach (var child in _childMembers)
                {
                    _childrenFlex.Remember(child);
                }
            }
            else
            {
                _childrenFlex.ForgetAll();
            }
        }

        /// <summary>
        /// Registers a child member inside this foldout member.
        /// </summary>
        public IMember RegisterChildMember(string name, DebugMember attribute)
        {
            if (!_isFoldout)
            {
                SetupAsFoldout();
            }

            var childMember = _childrenFlex.Append<Member>(name);
            childMember.LayoutStyle = Style.Instantiate<LayoutStyle>("Member");
            childMember.Title = string.IsNullOrEmpty(attribute.DisplayName) ? name : attribute.DisplayName;
            childMember.PillColor = attribute.Color;

            _childMembers.Add(childMember);

            // If foldout is collapsed, hide the new member
            if (_foldoutToggle != null && !_foldoutToggle.State)
            {
                _childrenFlex.Forget(childMember);
            }

            return childMember;
        }

        /// <summary>
        /// Gets the list of child members if this is a foldout.
        /// </summary>
        public IReadOnlyList<Member> ChildMembers => _childMembers;
    }
}
