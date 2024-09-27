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
using System.Text.RegularExpressions;
using Meta.XR.Guides.Editor.Items;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Guides.Editor
{
    [InitializeOnLoad]
    internal static class GuideProcessor
    {
        internal static Dictionary<string, MethodInfo> _guideItemsMap = new();

        private const string RegexMatchPattern = @"\b(Unity|Oculus|Meta)\b";

        static GuideProcessor()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => Regex.IsMatch(a.FullName, RegexMatchPattern, RegexOptions.IgnoreCase));

            var types = assemblies.SelectMany(a => a.GetTypes());
            var methods = types
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .Where(m => m.GetCustomAttributes<GuideItemsAttribute>(false).Any());

            foreach (var methodInfo in methods)
            {
                var id = $"{methodInfo.DeclaringType}.{methodInfo.Name}";
                _guideItemsMap[id] = methodInfo;
            }
        }

        public static List<IGuideItem> GetItems(string methodId)
        {
            if (_guideItemsMap.TryGetValue(methodId, out var methodInfo))
            {
                var obj = methodInfo.IsStatic ? null : Activator.CreateInstance(methodInfo.DeclaringType);
                return (List<IGuideItem>)methodInfo.Invoke(obj, null);
            }
            return null;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    sealed class GuideItemsAttribute : Attribute
    {
    }
}
