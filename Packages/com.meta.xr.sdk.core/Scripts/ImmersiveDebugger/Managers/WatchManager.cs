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


using Meta.XR.ImmersiveDebugger.UserInterface;
using Meta.XR.ImmersiveDebugger.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using Meta.XR.ImmersiveDebugger.Hierarchy;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.Manager
{
    internal class WatchManager : IDebugManager
    {
        internal readonly Dictionary<Type, List<(MemberInfo, DebugMember)>> WatchesDict = new();
        private IDebugUIPanel _uiPanel;
        private InstanceCache _instanceCache;

        public void Setup(IDebugUIPanel panel, InstanceCache cache)
        {
            _uiPanel = panel;
            _instanceCache = cache;
        }

        public void ProcessType(Type type)
        {
            WatchesDict.Remove(type);

            var membersList = new List<(MemberInfo, DebugMember)>();
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.GetField | BindingFlags.GetProperty);
            foreach (var member in members)
            {
                var watchAttribute = member.GetCustomAttribute<DebugMember>();
                if (watchAttribute != null)
                {
                    if (IsMemberValidForWatch(member))
                    {
                        membersList.Add((member, watchAttribute));
                    }
                }
            }

            membersList.AddRange(InspectedDataRegistry.GetMembersForType<MemberInfo>(type,
                (info, _) => IsMemberValidForWatch(info)));

            WatchesDict[type] = membersList;
            ManagerUtils.RebuildInspectorForType(_uiPanel, _instanceCache, type, membersList, (memberController, member, attribute, instance) =>
            {
                var memberType = member.GetDataType();

                // Check if this member's type has its own DebugMember-annotated members (nested class)
                if (ManagerUtils.HasNestedDebugMembers(memberType))
                {
                    // For nested class, register each nested member as a watch
                    RegisterNestedMembersAsWatches(memberController, member, memberType, instance);
                }
                else
                {
                    // Standard watch registration
                    var watch = memberController.GetWatch();
                    if (!watch?.Matches(member, instance) ?? true)
                    {
                        if (member.IsTypeEqual(typeof(Texture2D)))
                        {
                            memberController.RegisterTexture(WatchUtils.Create(member, instance, attribute) as WatchTexture);
                        }
                        else
                        {
                            memberController.RegisterWatch(WatchUtils.Create(member, instance, attribute));
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Registers nested class members as watches inside a foldout.
        /// Only handles watch registration - other managers handle their own hooks.
        /// </summary>
        private void RegisterNestedMembersAsWatches(IMember parentMemberController, MemberInfo parentMember, Type nestedType, InstanceHandle instance)
        {
            foreach (var (nestedMember, nestedAttribute) in ManagerUtils.GetNestedDebugMembers(nestedType))
            {
                // Only register fields/properties as watches
                if (!IsMemberValidForWatch(nestedMember))
                {
                    continue;
                }

                var childMemberController = ManagerUtils.GetOrCreateNestedMemberController(parentMemberController, nestedMember, nestedAttribute);
                if (childMemberController != null)
                {
                    // Register watch only - TweakManager handles tweaks separately
                    var nestedWatch = NestedWatchUtils.CreateNested(parentMember, nestedMember, instance, nestedAttribute);
                    childMemberController.RegisterWatch(nestedWatch);
                }
            }
        }

        internal static bool IsMemberValidForWatch(MemberInfo member)
        {
            var supported = member.MemberType is MemberTypes.Property or MemberTypes.Field;
            supported &= !member.IsBaseTypeEqual(typeof(Enum));
            supported |= member.IsTypeEqual(typeof(Texture2D));
            supported &= member is not PropertyInfo { CanRead: false };
            return supported;
        }

        public void ProcessTypeFromInspector(Type type, InstanceHandle handle, MemberInfo memberInfo, DebugMember memberAttribute)
        {
            throw new NotImplementedException();
        }

        public void ProcessTypeFromHierarchy(Item item, MemberInfo memberInfo)
        {
            throw new NotImplementedException();
        }

        public string TelemetryAnnotation => Telemetry.AnnotationType.Watches;
        public int GetCountPerType(Type type)
        {
            WatchesDict.TryGetValue(type, out var actions);
            return actions?.Count ?? 0;
        }
    }
}
