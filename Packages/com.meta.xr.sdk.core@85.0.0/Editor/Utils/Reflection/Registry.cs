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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meta.XR.Editor.Reflection
{
    /// <summary>
    /// Registry for managing reflection attribute lookups using assembly-capture approach.
    /// Provides efficient, targeted reflection scanning by leveraging Handle's captured creating assembly
    /// instead of relying on problematic AppDomain.CurrentDomain.GetAssemblies() enumeration.
    /// </summary>
    /// <remarks>
    /// This registry solves the critical issue where AppDomain.CurrentDomain.GetAssemblies() may not
    /// return all loaded assemblies, especially in Unity Editor scenarios. Instead, it uses the
    /// assembly captured at Handle construction time to perform targeted reflection scanning.
    ///
    /// Key improvements over the legacy approach:
    /// - Assembly-specific scanning rather than broad AppDomain enumeration
    /// - Per-assembly caching for improved performance on repeated access
    /// - Direct assembly targeting eliminates dependency on unreliable AppDomain state
    /// - Supports cross-assembly reflection via InternalsVisibleToAttribute
    /// </remarks>
    internal static class Registry
    {
        /// <summary>
        /// Binding flags used for comprehensive member discovery including both static and instance members
        /// </summary>
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Cache for per-assembly reflection data to avoid repeated scanning
        /// </summary>
        private static readonly Dictionary<Assembly, List<(MemberInfo, ReflectionAttribute)>> _assemblyCache
            = new Dictionary<Assembly, List<(MemberInfo, ReflectionAttribute)>>();

        /// <summary>
        /// Cached list of assembly names allowed via InternalsVisibleToAttribute
        /// </summary>
        private static List<string> _allowedAssemblyNames;

        /// <summary>
        /// Resets all cached data. Typically called during testing setup to ensure clean state.
        /// </summary>
        public static void Reset()
        {
            _assemblyCache.Clear();
            _allowedAssemblyNames = null;
        }

        /// <summary>
        /// Gets the list of assembly names that are allowed to access this assembly's internals
        /// via InternalsVisibleToAttribute declarations.
        /// </summary>
        /// <returns>List of allowed assembly names (simple names, not full names)</returns>
        private static List<string> GetAllowedAssemblyNames()
        {
            if (_allowedAssemblyNames == null)
            {
                var executingAssembly = Assembly.GetExecutingAssembly();
                _allowedAssemblyNames = executingAssembly
                    .GetCustomAttributes<InternalsVisibleToAttribute>()
                    .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
                    .ToList();
            }
            return _allowedAssemblyNames;
        }

        /// <summary>
        /// Determines whether the specified assembly is allowed to access internal reflection members.
        /// </summary>
        /// <param name="assembly">The assembly to check</param>
        /// <returns>True if the assembly is allowed, false otherwise</returns>
        /// <remarks>
        /// An assembly is considered allowed if:
        /// 1. It is the executing assembly itself, or
        /// 2. It is listed in the InternalsVisibleToAttribute declarations
        /// </remarks>
        private static bool IsAssemblyAllowed(Assembly assembly)
        {
            var allowedNames = GetAllowedAssemblyNames();
            var assemblyName = assembly.GetName().Name;

            // Always allow the executing assembly (this Registry's assembly)
            if (assembly == Assembly.GetExecutingAssembly())
                return true;

            return allowedNames.Contains(assemblyName);
        }

        /// <summary>
        /// Scans the specified assembly for members decorated with ReflectionAttribute.
        /// Results are cached for performance on subsequent calls.
        /// </summary>
        /// <param name="assembly">The assembly to scan</param>
        /// <returns>List of tuples containing MemberInfo and associated ReflectionAttribute</returns>
        /// <remarks>
        /// This method performs targeted scanning of a specific assembly rather than
        /// broad AppDomain enumeration. It looks for:
        /// 1. Types decorated with [Reflection] attribute
        /// 2. Members within those types that also have [Reflection] attributes
        ///
        /// Results are cached per assembly to avoid repeated reflection operations.
        /// </remarks>
        private static List<(MemberInfo, ReflectionAttribute)> ScanAssemblyForReflectionMembers(Assembly assembly)
        {
            if (_assemblyCache.TryGetValue(assembly, out var cached))
            {
                return cached;
            }

            var members = new List<(MemberInfo, ReflectionAttribute)>();

            try
            {
                // Find all types decorated with ReflectionAttribute
                var types = assembly.GetTypes()
                    .Where(type => type.GetCustomAttribute<ReflectionAttribute>() != null);

                foreach (var type in types)
                {
                    // Find all members within the type that have ReflectionAttribute
                    var typeMembers = type.GetMembers(Flags)
                        .Select(member => (member, member.GetCustomAttribute<ReflectionAttribute>(true)))
                        .Where(tuple => tuple.Item2 != null);

                    members.AddRange(typeMembers);
                }
            }
            catch (System.Exception ex)
            {
                _ = ex; // Suppress unused variable warning when OVRPLUGIN_TESTING is not defined
            }

            // Cache the results for future lookups
            _assemblyCache[assembly] = members;

            return members;
        }

        /// <summary>
        /// Attempts to load an assembly by name, with error handling and logging.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly to load</param>
        /// <returns>The loaded Assembly, or null if loading failed</returns>
        private static Assembly TryLoadAssembly(string assemblyName)
        {
            try
            {
                return Assembly.Load(assemblyName);
            }
            catch (System.Exception ex)
            {
                _ = ex; // Suppress unused variable warning when OVRPLUGIN_TESTING is not defined
                return null;
            }
        }

        /// <summary>
        /// Extension method to retrieve a Handle instance from a MemberInfo that represents a field or property.
        /// Handles both static and instance members appropriately.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo representing a field or property</param>
        /// <returns>The Handle instance stored in the field/property, or null if not found</returns>
        /// <remarks>
        /// This method is used to extract Handle instances from fields or properties
        /// that have been decorated with ReflectionAttribute. It's part of the registry's
        /// process of matching handles with their corresponding reflection attributes.
        ///
        /// For static members, it retrieves the value directly.
        /// For instance members, it returns null since instance values require an object instance.
        /// </remarks>
        public static Handle GetHandle(this MemberInfo memberInfo)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                var fieldInfo = (FieldInfo)memberInfo;
                // Only try to get values from static fields
                if (fieldInfo.IsStatic)
                {
                    return fieldInfo.GetValue(null) as Handle;
                }
                // For non-static fields, we can't get the value without an instance
                return null;
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                var propertyInfo = (PropertyInfo)memberInfo;
                var getMethod = propertyInfo.GetGetMethod(true);
                // Only try to get values from static properties
                if (getMethod != null && getMethod.IsStatic)
                {
                    return propertyInfo.GetValue(null) as Handle;
                }
                // For non-static properties, we can't get the value without an instance
                return null;
            }

            return null;
        }

        /// <summary>
        /// Finds the ReflectionAttribute associated with the specified Handle using the Handle's
        /// captured creating assembly for targeted reflection scanning.
        /// </summary>
        /// <param name="handle">The Handle instance to find the attribute for</param>
        /// <returns>The associated ReflectionAttribute, or null if not found</returns>
        /// <remarks>
        /// This is the core method that leverages the new assembly-capture approach:
        ///
        /// 1. Uses the Handle's CreatingAssembly (captured at construction time)
        /// 2. Performs targeted scanning of that specific assembly
        /// 3. Matches the Handle instance against cached reflection data
        /// 4. Falls back to other allowed assemblies if needed
        ///
        /// This approach eliminates the need for problematic AppDomain.CurrentDomain.GetAssemblies()
        /// enumeration and provides reliable, efficient reflection attribute lookup.
        /// </remarks>
        public static ReflectionAttribute FindAttribute(Handle handle)
        {
            // Use the assembly that created the Handle (captured at construction time)
            var creatingAssembly = handle.CreatingAssembly;

            // Verify the creating assembly is allowed to access internal reflection members
            if (!IsAssemblyAllowed(creatingAssembly))
            {
                return null;
            }

            // Scan the creating assembly for reflection members (uses cache if available)
            var members = ScanAssemblyForReflectionMembers(creatingAssembly);

            // Find the member with a Handle that matches our target handle
            foreach (var (member, attribute) in members)
            {
                var memberHandle = member.GetHandle();
                if (memberHandle != null && memberHandle.Equals(handle))
                {
                    return attribute;
                }
            }

            // Fallback: try other allowed assemblies in case of cross-assembly scenarios
            var allowedAssemblyNames = GetAllowedAssemblyNames();
            foreach (var allowedName in allowedAssemblyNames)
            {
                var assembly = TryLoadAssembly(allowedName);
                if (assembly != null && assembly != creatingAssembly)
                {
                    var otherMembers = ScanAssemblyForReflectionMembers(assembly);
                    foreach (var (member, attribute) in otherMembers)
                    {
                        var memberHandle = member.GetHandle();
                        if (memberHandle != null && memberHandle.Equals(handle))
                        {
                            return attribute;
                        }
                    }
                }
            }

            return null;
        }
    }
}
