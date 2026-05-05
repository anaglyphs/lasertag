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
using System.Linq;
using System.Reflection;
using Meta.XR.ImmersiveDebugger.Hierarchy;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.Manager
{
    internal interface IDebugManager
    {
        public void Setup(IDebugUIPanel panel, InstanceCache cache);
        public void ProcessType(Type type);

        public void ProcessTypeFromInspector(Type type, InstanceHandle handle, MemberInfo memberInfo,
            DebugMember memberAttribute);

        public void ProcessTypeFromHierarchy(Item item, MemberInfo memberInfo);

        // Telemetry
        public string TelemetryAnnotation { get; }
        public int GetCountPerType(Type type);
    }

    internal static class ManagerUtils
    {
        /// <summary>
        /// Binding flags used for discovering nested class members.
        /// </summary>
        public const BindingFlags NestedMemberBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public delegate void RegisterMember<in T>(IMember memberController, T member, DebugMember attribute, InstanceHandle instanceHandle);
        public static void RebuildInspectorForType<T>(IDebugUIPanel panel, InstanceCache cache, Type type, List<(T, DebugMember)> memberPairs, RegisterMember<T> memberRegistration) where T : MemberInfo
        {
            foreach (var (member, attribute) in memberPairs)
            {
                if (member.IsStatic())
                {
                    var instanceHandle = InstanceHandle.Static(type);
                    var inspector = panel.RegisterInspector(instanceHandle, new Category { Id = attribute.Category });
                    var memberController = inspector?.RegisterMember(member, attribute);
                    if (memberController != null)
                    {
                        memberRegistration.Invoke(memberController, member, attribute, instanceHandle);
                    }
                }
                else
                {
                    var instances = cache.GetCacheDataForClass(type);
                    foreach (var instance in instances)
                    {
                        var inspector = panel.RegisterInspector(instance, new Category { Id = attribute.Category });
                        var memberController = inspector?.RegisterMember(member, attribute);
                        if (memberController != null)
                        {
                            memberRegistration.Invoke(memberController, member, attribute, instance);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a type has members with DebugMember attributes (i.e., is a nested class with debug members).
        /// </summary>
        public static bool HasNestedDebugMembers(Type type)
        {
            if (type == null || type.IsPrimitive || type == typeof(string) || type.IsEnum)
            {
                return false;
            }

            // Check for common Unity types that shouldn't be treated as nested classes
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
                type == typeof(Quaternion) || type == typeof(Color) || type == typeof(Rect) ||
                type == typeof(Bounds) || type == typeof(Matrix4x4) || type == typeof(Texture2D))
            {
                return false;
            }

            var nestedMembers = type.GetMembers(NestedMemberBindingFlags);
            return nestedMembers.Any(m => m.GetCustomAttribute<DebugMember>() != null);
        }

        /// <summary>
        /// Gets the child member controller for a nested member, creating the foldout structure if needed.
        /// Works with both Member (runtime) and MockMember (testing) types.
        /// </summary>
        public static IMember GetOrCreateNestedMemberController(IMember parentMemberController, MemberInfo nestedMember, DebugMember nestedAttribute)
        {
            var expectedTitle = (string.IsNullOrEmpty(nestedAttribute.DisplayName) ? nestedMember.Name : nestedAttribute.DisplayName).ToDisplayText();

            // Try to work with real Member type
            if (parentMemberController is Member member)
            {
                // Setup the parent member as a foldout if not already
                if (!member.IsFoldout)
                {
                    member.SetupAsFoldout();
                }

                // Check if child member already exists by comparing transformed titles
                var existingChild = member.ChildMembers.FirstOrDefault(c => c.Title == expectedTitle);
                if (existingChild != null)
                {
                    return existingChild;
                }

                // Create new child member
                return member.RegisterChildMember(nestedMember.Name, nestedAttribute);
            }


            return null;
        }

        /// <summary>
        /// Gets all nested members with DebugMember attributes from a type.
        /// </summary>
        public static IEnumerable<(MemberInfo member, DebugMember attribute)> GetNestedDebugMembers(Type nestedType)
        {
            var nestedMembers = nestedType.GetMembers(NestedMemberBindingFlags);
            foreach (var nestedMember in nestedMembers)
            {
                var nestedAttribute = nestedMember.GetCustomAttribute<DebugMember>();
                if (nestedAttribute != null)
                {
                    yield return (nestedMember, nestedAttribute);
                }
            }
        }
    }
}
