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


using System.Reflection;

namespace Meta.XR.ImmersiveDebugger.Manager
{

    public abstract class Hook
    {
        private readonly DebugMember _attribute;
        protected readonly MemberInfo _memberInfo;
        protected readonly object _instance;

        public DebugMember Attribute => _attribute;

        protected Hook(MemberInfo memberInfo, object instance, DebugMember attribute)
        {
            _memberInfo = memberInfo;
            _instance = instance;
            _attribute = attribute;
        }

        public bool Matches(MemberInfo memberInfo, object instance)
            => _memberInfo == memberInfo && _instance == instance;
    }
}

