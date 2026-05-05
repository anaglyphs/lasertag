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

namespace Meta.XR.ImmersiveDebugger.Manager
{
    internal class ActionManager : IDebugManager
    {
        internal readonly Dictionary<Type, List<(MethodInfo, DebugMember)>> ActionsDict = new();
        private IDebugUIPanel _uiPanel;
        private InstanceCache _instanceCache;

        public void Setup(IDebugUIPanel uiPanel, InstanceCache instanceCache)
        {
            _uiPanel = uiPanel;
            _instanceCache = instanceCache;
        }

        public void ProcessType(Type type)
        {
            ActionsDict.Remove(type);

            var actionList = new List<(MethodInfo, DebugMember)>();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var actionAttribute = method.GetCustomAttribute<DebugMember>();
                if (actionAttribute != null)
                {
                    actionList.Add((method, actionAttribute));
                }
            }

            actionList.AddRange(InspectedDataRegistry.GetMembersForType<MethodInfo>(type));

            ActionsDict[type] = actionList;

            // First, register standard actions
            ManagerUtils.RebuildInspectorForType(_uiPanel, _instanceCache, type, actionList, (memberController, member, attribute, instance) =>
            {
                var action = memberController.GetAction();
                if (!action?.Matches(member, instance) ?? true)
                {
                    memberController.RegisterAction(new ActionHook(member, instance, attribute));
                }
            });

            // Then, check for fields/properties that are nested classes with action methods
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.GetField | BindingFlags.GetProperty);
            foreach (var parentMember in members)
            {
                var parentAttribute = parentMember.GetCustomAttribute<DebugMember>();
                if (parentAttribute == null || !HasNestedActionMembers(parentMember.GetDataType()))
                {
                    continue;
                }

                // Register the parent member and nested actions for each instance
                var instances = _instanceCache.GetCacheDataForClass(type);
                foreach (var instance in instances)
                {
                    var inspector = _uiPanel.RegisterInspector(instance, new Category { Id = parentAttribute.Category });
                    // Register the parent member first (it may not exist if only ActionManager is running)
                    var parentMemberController = inspector?.RegisterMember(parentMember, parentAttribute);
                    if (parentMemberController != null)
                    {
                        RegisterNestedMembersAsActions(parentMemberController, parentMember, parentMember.GetDataType(), instance);
                    }
                }
            }
        }

        /// <summary>
        /// Registers nested class methods as actions inside a foldout.
        /// </summary>
        private void RegisterNestedMembersAsActions(IMember parentMemberController, MemberInfo parentMember, Type nestedType, InstanceHandle instance)
        {
            foreach (var (nestedMember, nestedAttribute) in ManagerUtils.GetNestedDebugMembers(nestedType))
            {
                // Only register methods as actions
                if (nestedMember is not MethodInfo nestedMethod)
                {
                    continue;
                }

                var childMemberController = ManagerUtils.GetOrCreateNestedMemberController(parentMemberController, nestedMember, nestedAttribute);
                if (childMemberController != null)
                {
                    // Check if action already exists to avoid duplication
                    var existingAction = childMemberController.GetAction();
                    if (!existingAction?.Matches(nestedMethod, instance) ?? true)
                    {
                        childMemberController.RegisterAction(new NestedActionHook(parentMember, nestedMethod, instance, nestedAttribute));
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a type has nested members that are methods with DebugMember attributes.
        /// </summary>
        private static bool HasNestedActionMembers(Type type)
        {
            if (!ManagerUtils.HasNestedDebugMembers(type))
            {
                return false;
            }

            foreach (var (nestedMember, _) in ManagerUtils.GetNestedDebugMembers(type))
            {
                if (nestedMember is MethodInfo)
                {
                    return true;
                }
            }

            return false;
        }

        public void ProcessTypeFromInspector(Type type, InstanceHandle handle, MemberInfo memberInfo, DebugMember memberAttribute)
        {
            throw new NotImplementedException();
        }

        public void ProcessTypeFromHierarchy(Item item, MemberInfo memberInfo)
        {
            throw new NotImplementedException();
        }

        public string TelemetryAnnotation => Telemetry.AnnotationType.Actions;
        public int GetCountPerType(Type type)
        {
            ActionsDict.TryGetValue(type, out var actions);
            return actions?.Count ?? 0;
        }
    }
}
