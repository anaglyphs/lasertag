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
using System.Reflection;
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.Utils;

namespace Meta.XR.ImmersiveDebugger
{
    internal class RuntimeInspectorManager : DebugManagerAddon<RuntimeInspectorManager>
    {
        protected override Telemetry.Method Method => Telemetry.Method.RuntimeAPI;

        /// <summary>
        /// Processes a single member for runtime inspector registration
        /// </summary>
        /// <param name="componentType">The type of the component</param>
        /// <param name="instanceHandle">The instance handle</param>
        /// <param name="memberInfo">The member to process</param>
        /// <param name="debugMemberAttribute">The debug member attribute</param>
        public void ProcessMemberForInspector(Type componentType, InstanceHandle instanceHandle, MemberInfo memberInfo, DebugMember debugMemberAttribute)
        {
            if (_uiPanel == null) return;

            // Register the instance handle in our cache
            _instanceCache.RegisterHandle(instanceHandle);

            // Process the member with all sub-managers
            foreach (var manager in _subDebugManagers)
            {
                manager.ProcessTypeFromInspector(componentType, instanceHandle, memberInfo, debugMemberAttribute);
            }
        }
    }
}
