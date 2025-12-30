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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.UserInterface;
using Meta.XR.ImmersiveDebugger.Utils;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger
{
    /// <summary>
    /// Public runtime APIs for Immersive Debugger that allow developers to dynamically add inspectors
    /// for their special needs at runtime.
    ///
    /// These APIs provide a way to add inspectors for components at runtime without using DebugMember attributes.
    /// This is useful for debugging components that are created dynamically or for adding inspectors
    /// to components that you don't have source access to.
    /// </summary>
    public static class RuntimeAPIs
    {
        /// <summary>
        /// Adds a dedicated inspector for a specific component on a game object.
        /// This will find the game object from scene, then find the component instance and create InstanceHandle,
        /// register in InstanceCache, and register inspector based on the Type and InstanceHandle.
        /// It will retrieve the members of this type based on filter. If Members filter is not supplied,
        /// it will automatically register all the public members within this class.
        /// </summary>
        /// <param name="category">The category name for organizing the inspector</param>
        /// <param name="gameObjectName">The name of the GameObject to find in the scene</param>
        /// <param name="componentClassName">The class name of the component to inspect</param>
        /// <param name="members">Optional comma-separated list of member names to inspect. If empty, all public members will be included.</param>
        /// <returns>A RuntimeAPIOperationResult containing detailed operation result with message information for AI agents</returns>
        public static RuntimeAPIOperationResult AddInspector(string category, string gameObjectName, string componentClassName, string members = "")
        {
            try
            {
                // Validate input parameters
                if (string.IsNullOrEmpty(gameObjectName))
                {
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure_InvalidParameter,
                        "GameObject name cannot be null or empty",
                        "Please provide a valid GameObject name that exists in the scene");
                }

                if (string.IsNullOrEmpty(componentClassName))
                {
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure_InvalidParameter,
                        "Component class name cannot be null or empty",
                        "Please provide a valid component class name (e.g., 'Transform', 'Rigidbody')");
                }

                // Wait for DebugManager to be ready
                if (DebugManager.Instance == null)
                {
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure,
                        "DebugManager is not initialized yet",
                        "Wait for the DebugManager to initialize before calling this API, or ensure the ImmersiveDebugger is properly set up in the scene");
                }

                // Find the GameObject in the scene
                var gameObject = GameObject.Find(gameObjectName);
                if (gameObject == null)
                {
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure,
                        $"GameObject '{gameObjectName}' not found in scene",
                        $"Make sure the GameObject named '{gameObjectName}' exists in the current scene and is active");
                }

                // Get the component directly by name
                var component = gameObject.GetComponent(componentClassName);
                if (component == null)
                {
                    var availableComponents = string.Join(", ", gameObject.GetComponents<Component>().Select(c => c.GetType().Name));
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure,
                        $"Component '{componentClassName}' not found on GameObject '{gameObjectName}'",
                        $"Available components on '{gameObjectName}': {availableComponents}");
                }

                var componentType = component.GetType();
                var instanceHandle = new InstanceHandle(componentType, component);

                var inspectorCategory = new Category
                {
                    Id = string.IsNullOrEmpty(category) ? "Runtime" : category
                };

                var uiPanel = DebugManager.Instance.UiPanel;
                if (uiPanel == null)
                {
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure,
                        "UI Panel is not available",
                        "The DebugManager's UI Panel is not initialized. Ensure the ImmersiveDebugger UI is properly set up");
                }

                var inspector = uiPanel.RegisterInspector(instanceHandle, inspectorCategory);
                if (inspector == null)
                {
                    return RuntimeAPIOperationResult.CreateFailure(
                        RuntimeAPIResult.Failure,
                        "Failed to register inspector",
                        $"The UI Panel could not create an inspector for component '{componentClassName}' on GameObject '{gameObjectName}'");
                }
                var memberCount = RegisterMembersForInspector(inspector, componentType, members, instanceHandle, category);

                // Force UI to focus on this category to ensure the newly added inspector is visible
                if (uiPanel is InspectorPanel inspectorPanel)
                {
                    var categoryButton = inspectorPanel.GetCategoryButton(inspectorCategory, true);
                    inspectorPanel.SelectCategoryButton(categoryButton);
                }

                return RuntimeAPIOperationResult.CreateSuccess(
                    $"Successfully added inspector for {componentClassName} on {gameObjectName}",
                    $"Inspector registered in category '{inspectorCategory.Id}' with {memberCount} members");
            }
            catch (Exception ex)
            {
                return RuntimeAPIOperationResult.CreateFailure(
                    RuntimeAPIResult.Failure,
                    $"Error adding inspector for {componentClassName} on {gameObjectName}: {ex.Message} ",
                    $"Exception details: {ex.GetType().Name} - {ex.StackTrace}");
            }
        }
        /// <summary>
        /// Registers members for the inspector based on the provided filter
        /// </summary>
        /// <returns>The number of members successfully registered</returns>
        private static int RegisterMembersForInspector(IInspector inspector, Type componentType, string membersFilter, InstanceHandle instanceHandle, string category)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.GetField | BindingFlags.GetProperty;

            var members = componentType.GetMembers(flags);
            var memberNames = ParseMemberNames(membersFilter);
            var registeredCount = 0;

            foreach (var member in members)
            {
                // If specific members are requested, only include those
                if (memberNames.Count > 0 && !memberNames.Contains(member.Name)) continue;

                // If no specific members requested, only include public members
                if (memberNames.Count == 0 && !member.IsPublic()) continue;

                // Skip if not compatible with debug inspector
                if (!member.IsCompatibleWithDebugInspector()) continue;

                // Create a default DebugMember attribute for runtime registration
                var debugMemberAttribute = new DebugMember
                {
                    Category = category,
                    Tweakable = IsTweakableMember(member)
                };

                try
                {
                    inspector.RegisterMember(member, debugMemberAttribute);

                    // Use RuntimeInspectorManager to process the member and create UI controls
                    RuntimeInspectorManager.Instance?.ProcessMemberForInspector(componentType, instanceHandle, member, debugMemberAttribute);

                    registeredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ImmersiveDebugger] Failed to register member '{member.Name}': {ex.Message}");
                }
            }

            return registeredCount;
        }

        /// <summary>
        /// Parses the comma-separated member names string
        /// </summary>
        private static HashSet<string> ParseMemberNames(string membersFilter)
        {
            var memberNames = new HashSet<string>();

            if (string.IsNullOrEmpty(membersFilter)) return memberNames;

            var names = membersFilter.Split(',');
            foreach (var name in names)
            {
                var trimmedName = name.Trim();
                if (!string.IsNullOrEmpty(trimmedName))
                {
                    memberNames.Add(trimmedName);
                }
            }

            return memberNames;
        }

        /// <summary>
        /// Determines if a member should be tweakable (writable)
        /// </summary>
        private static bool IsTweakableMember(MemberInfo member)
        {
            return member switch
            {
                FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
                PropertyInfo property => property.CanWrite,
                _ => false
            };
        }
    }
}
