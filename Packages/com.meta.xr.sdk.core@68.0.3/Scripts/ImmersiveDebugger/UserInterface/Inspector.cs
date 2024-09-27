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
using System.Collections.Generic;
using System.Reflection;

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    internal class Inspector : Controller, IInspector
    {
        private Label _title;
        private Flex _flex;
        private Background _background;
        private readonly Dictionary<MemberInfo, Member> _registry = new Dictionary<MemberInfo, Member>();

        public ImageStyle BackgroundStyle
        {
            set
            {
                _background.Sprite = value.sprite;
                _background.Color = value.color;
                _background.PixelDensityMultiplier = value.pixelDensityMultiplier;
            }
        }

        public string Title
        {
            get => _title.Content;
            set => _title.Content = value;
        }

        protected override void Setup(Controller owner)
        {
            base.Setup(owner);

            // Background
            _background = Append<Background>("background");
            _background.LayoutStyle = Style.Load<LayoutStyle>("Fill");
            BackgroundStyle = Style.Load<ImageStyle>("InspectorBackground");

            // Flex
            _flex = Append<Flex>("list");
            _flex.LayoutStyle = Style.Load<LayoutStyle>("InspectorFlex");

            // Label
            _title = _flex.Append<Label>("title");
            _title.LayoutStyle = Style.Load<LayoutStyle>("InspectorTitle");
            _title.TextStyle = Style.Load<TextStyle>("InspectorTitle");
        }

        public IMember RegisterMember(MemberInfo memberInfo, DebugMember attribute)
        {
            if (!_registry.TryGetValue(memberInfo, out var member))
            {
                member = _flex.Append<Member>(memberInfo.Name);
                member.LayoutStyle = Style.Load<LayoutStyle>("Member");
                member.Title = $"{memberInfo.Name}";
                member.PillColor = attribute.Color;
                _registry.Add(memberInfo, member);
            }

            return member;
        }

        public IMember GetMember(MemberInfo memberInfo)
        {
            _registry.TryGetValue(memberInfo, out var member);
            return member;
        }
    }
}

