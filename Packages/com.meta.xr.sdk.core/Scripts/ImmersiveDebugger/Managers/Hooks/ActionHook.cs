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
using Meta.XR.ImmersiveDebugger.Utils;

namespace Meta.XR.ImmersiveDebugger.Manager
{
    internal class ActionHook : Hook
    {
        internal System.Action Delegate { get; set; }

        internal ActionHook(MemberInfo memberInfo, InstanceHandle instanceHandle, DebugMember attribute) : base(memberInfo, instanceHandle, attribute)
        {
            Delegate = () => (memberInfo as MethodInfo)?.Invoke(_instance, null);
        }
    }

    /// <summary>
    /// ActionHook for nested class methods. Invokes the method on the nested object obtained through the parent member.
    /// </summary>
    internal class NestedActionHook : ActionHook
    {
        /// <summary>
        /// Creates a nested action hook that invokes a method on a nested object.
        /// </summary>
        /// <param name="parentMemberInfo">The member info for the parent field (e.g., 'data' of type NestedData)</param>
        /// <param name="nestedMethodInfo">The method info for the nested method (e.g., 'Method()' inside NestedData)</param>
        /// <param name="instanceHandle">The instance handle of the root component</param>
        /// <param name="attribute">The debug member attribute</param>
        internal NestedActionHook(MemberInfo parentMemberInfo, MethodInfo nestedMethodInfo, InstanceHandle instanceHandle, DebugMember attribute)
            : base(nestedMethodInfo, instanceHandle, attribute)
        {
            Delegate = () =>
            {
                var parentValue = parentMemberInfo.GetValue(_instance);
                if (parentValue != null)
                {
                    nestedMethodInfo.Invoke(parentValue, null);
                }
            };
        }
    }
}
