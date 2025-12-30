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
using System.Runtime.CompilerServices;
using UnityEngine;
namespace Meta.XR.ImmersiveDebugger.Utils
{
    /// <summary>
    /// Public extension methods for MemberInfo that provide unified access to fields, properties, and methods.
    ///
    /// This utility class simplifies reflection operations by providing a consistent API for accessing
    /// member information regardless of whether the member is a field, property, or method.
    /// It's designed to work seamlessly with the Immersive Debugger's inspection and tweaking systems.
    ///
    /// These extensions are particularly useful for:
    /// - Runtime component inspection and modification
    /// - Dynamic UI generation for debugging interfaces
    /// - Type-safe reflection operations with proper error handling
    /// - Integration with AI agents and automated debugging tools
    /// </summary>
    public static class MemberInfoExtensions
    {
        /// <summary>
        /// Gets the value of a field or property from the specified instance.
        ///
        /// This method provides a unified way to retrieve values from both fields and properties,
        /// handling the underlying reflection differences automatically.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo representing the field or property to read from</param>
        /// <param name="instance">The object instance to read the value from</param>
        /// <returns>The value of the member, or null if the member cannot be read or an error occurs</returns>
        public static object GetValue(this MemberInfo memberInfo, object instance)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                return ((FieldInfo)memberInfo).GetValue(instance);
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                var property = (PropertyInfo)memberInfo;
                if (property.CanRead)
                {
                    return property.GetValue(instance);
                }
                Debug.LogWarning("Calling GetValue() from property cannot be read");
                return null;
            }
            Debug.LogWarning("Calling GetValue() from wrong member type, expect field/property");
            return null;
        }

        /// <summary>
        /// Sets the value of a field or property on the specified instance.
        ///
        /// This method provides a unified way to modify values for both fields and properties,
        /// handling the underlying reflection differences automatically. It's essential for
        /// runtime tweaking and dynamic component modification in debugging scenarios.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo representing the field or property to write to</param>
        /// <param name="instance">The object instance to modify</param>
        /// <param name="value">The new value to assign to the member</param>
        public static void SetValue(this MemberInfo memberInfo, object instance, object value)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                ((FieldInfo)memberInfo).SetValue(instance, value);
                return;
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                var property = (PropertyInfo)memberInfo;
                if (property.CanWrite)
                {
                    property.SetValue(instance, value);
                    return;
                }
                Debug.LogWarning("Calling SetValue() from property cannot be written");
                return;
            }
            Debug.LogWarning("Calling SetValue() from wrong member type, expect field/property");
        }

        /// <summary>
        /// Gets the data type of a field or property.
        ///
        /// This method provides a unified way to retrieve the type information from both
        /// fields and properties, which is essential for type-safe operations and UI generation.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo representing the field or property</param>
        /// <returns>The Type of the field or property, or null if the member is not a field or property</returns>
        public static Type GetDataType(this MemberInfo memberInfo)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                return ((FieldInfo)memberInfo).FieldType;
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                return ((PropertyInfo)memberInfo).PropertyType;
            }
            Debug.LogWarning("Calling GetDataType() from wrong member type, expect field/property");
            return null;
        }

        /// <summary>
        /// Determines whether a field, property, or method is static.
        ///
        /// This method provides a unified way to check if a member is static across different member types,
        /// which is important for determining how to access the member (instance vs static access).
        /// </summary>
        /// <param name="memberInfo">The MemberInfo to check</param>
        /// <returns>True if the member is static, false otherwise</returns>
        public static bool IsStatic(this MemberInfo memberInfo)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                return ((FieldInfo)memberInfo).IsStatic;
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                var property = (PropertyInfo)memberInfo;
                return property.CanRead && property.GetMethod.IsStatic ||
                       property.CanWrite && property.SetMethod.IsStatic;
            }
            if ((memberType & MemberTypes.Method) != 0)
            {
                var method = (MethodInfo)memberInfo;
                return method.IsStatic;
            }
            Debug.LogWarning("Calling IsStatic() from wrong member type, expect field/property");
            return false;
        }

        /// <summary>
        /// Determines whether a field, property, or method is public.
        ///
        /// This method provides a unified way to check accessibility across different member types,
        /// which is crucial for filtering members that should be exposed in debugging interfaces.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo to check</param>
        /// <returns>True if the member is public, false otherwise</returns>
        public static bool IsPublic(this MemberInfo memberInfo)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                return ((FieldInfo)memberInfo).IsPublic;
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                var property = (PropertyInfo)memberInfo;
                return property.CanRead && property.GetMethod.IsPublic ||
                       property.CanWrite && property.SetMethod.IsPublic;
            }
            if ((memberType & MemberTypes.Method) != 0)
            {
                var method = (MethodInfo)memberInfo;
                return method.IsPublic;
            }
            return false;
        }

        /// <summary>
        /// Builds a formatted signature string for display in the debug inspector UI.
        ///
        /// This method creates human-readable signatures that include access modifiers, types,
        /// and member names with HTML formatting for rich text display in debugging interfaces.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo to create a signature for</param>
        /// <returns>A formatted HTML string representing the member signature</returns>
        public static string BuildSignatureForDebugInspector(this MemberInfo memberInfo)
        {
            var memberType = memberInfo.MemberType;
            if ((memberType & MemberTypes.Field) != 0)
            {
                var fieldInfo = (FieldInfo)memberInfo;
                var prefix = fieldInfo.IsPublic ? "public" :
                    fieldInfo.IsPrivate ? "private" :
                    fieldInfo.IsFamily ? "protected" : "internal";
                return $"<i>{prefix} {fieldInfo.FieldType.Name}</i> <b>{fieldInfo.Name}</b>";
            }
            if ((memberType & MemberTypes.Method) != 0)
            {
                var methodInfo = (MethodInfo)memberInfo;
                var prefix = methodInfo.IsPublic ? "public" :
                    methodInfo.IsPrivate ? "private" :
                    methodInfo.IsFamily ? "protected" : "internal";
                return $"<i>{prefix} {methodInfo.ReturnType.Name}</i> <b>{methodInfo.Name}</b>()";
            }
            if ((memberType & MemberTypes.Property) != 0)
            {
                var propertyInfo = (PropertyInfo)memberInfo;
                var getMethodInfo = propertyInfo.GetMethod;
                var prefix = getMethodInfo.IsPublic ? "public" :
                    getMethodInfo.IsPrivate ? "private" :
                    getMethodInfo.IsFamily ? "protected" : "internal";
                return $"<i>{prefix} {propertyInfo.PropertyType.Name}</i> <b>{propertyInfo.Name}</b>";
            }

            return memberInfo.Name;
        }

        /// <summary>
        /// Determines whether a member is compatible with the Immersive Debugger's inspection system.
        ///
        /// This method applies comprehensive filtering to determine if a member should be exposed
        /// in debugging interfaces. It's a core method used throughout the Immersive Debugger
        /// to ensure only appropriate members are shown to users and AI agents.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo to check for compatibility</param>
        /// <returns>True if the member is compatible with debug inspection, false otherwise</returns>
        public static bool IsCompatibleWithDebugInspector(this MemberInfo memberInfo)
        {
            if ((memberInfo as ConstructorInfo) != null)
            {
                return false;
            }

            var memberType = memberInfo.MemberType;
            if ((memberType & (MemberTypes.Method | MemberTypes.Property | MemberTypes.Field)) == 0)
            {
                return false;
            }

            if (memberInfo.GetCustomAttribute<ObsoleteAttribute>() != null)
            {
                return false; // skip obsolete members, getValue will throw exception
            }

            if (memberInfo.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
            {
                return false;
            }

            if ((memberType & MemberTypes.Method) != 0)
            {
                var method = (MethodInfo)memberInfo;
                if (method.GetParameters().Length > 0 || method.ReturnType != typeof(void))
                {
                    return false;

                }
            }

            if (memberInfo is PropertyInfo { CanRead: false })
            {
                // There is no way to use any of our managers (Actions, Gizmos, Watch, Tweak)
                // if we cannot even read the property
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether a field or property has the exact specified type.
        ///
        /// This method provides a unified way to check if a member's type matches exactly
        /// with a given type, which is useful for type-specific filtering and operations.
        /// </summary>
        /// <param name="member">The MemberInfo to check</param>
        /// <param name="type">The Type to compare against</param>
        /// <returns>True if the member's type exactly matches the specified type, false otherwise</returns>
        public static bool IsTypeEqual(this MemberInfo member, Type type) => (member as FieldInfo)?.FieldType == type ||
                                                                  (member as PropertyInfo)?.PropertyType == type;

        /// <summary>
        /// Determines whether a field or property's base type matches the specified type.
        ///
        /// This method checks if the member's type inherits from or has the specified base type,
        /// which is useful for inheritance-based filtering and polymorphic operations.
        /// </summary>
        /// <param name="member">The MemberInfo to check</param>
        /// <param name="type">The base Type to compare against</param>
        /// <returns>True if the member's base type matches the specified type, false otherwise</returns>
        public static bool IsBaseTypeEqual(this MemberInfo member, Type type) => (member as FieldInfo)?.FieldType.BaseType == type ||
                                                                             (member as PropertyInfo)?.PropertyType.BaseType == type;

        /// <summary>
        /// Determines whether a member can be modified (is settable).
        ///
        /// This method provides a quick way to check if a member supports value modification,
        /// which is essential for determining which members can be tweaked in debugging interfaces.
        /// </summary>
        /// <param name="memberInfo">The MemberInfo to check</param>
        /// <returns>True if the member is a field or property that can potentially be changed, false otherwise</returns>
        public static bool CanBeChanged(this MemberInfo memberInfo) => (memberInfo.MemberType & (MemberTypes.Property | MemberTypes.Field)) != 0;
    }
}
