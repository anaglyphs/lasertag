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
using Meta.XR.Guides.Editor.Items;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Guides.Editor
{
    /// <summary>
    /// Create a Meta XR Guide editor window
    /// </summary>
    internal class Guide
    {
        internal static Dictionary<int, GuideWindow> GuideWindowIntances = new();

        /// <summary>
        /// This will create a <see cref="GuideWindow"/> type of <see cref="EditorWindow"/>.
        /// </summary>
        /// <param name="title">Title of the window.</param>
        /// <param name="description">A brief description of this guide.</param>
        /// <param name="itemPopulator">A static function that will return a list of <see cref="IGuideItem"/>(s).</param>
        /// <remarks>
        /// The <paramref name="itemPopulator"/> parameter function needs to have the <see cref="GuideItemsAttribute"/> attribute
        /// in order to correctly repaint the Guide window on Unity's domain reload.
        /// <example>
        /// <code>
        /// <![CDATA[
        /// public static Foo()
        /// {
        ///     Guide.Create("Foo title", "Bar desc.", GetItems).Show();
        /// }
        ///
        /// [GuideItems]
        /// public static List<IGuideItem> GetItems()
        /// {
        ///     return new List<IGuideItem>();
        /// }
        /// ]]>
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>Returns a <see cref="GuideWindow"/>.</returns>
        public static GuideWindow Create(string title, string description, Func<List<IGuideItem>> itemPopulator) =>
            Create(title, description, itemPopulator, GuideWindow.DefaultOptions);

        /// <summary>
        /// This will create a <see cref="GuideWindow"/> type of <see cref="EditorWindow"/>.
        /// </summary>
        /// <param name="title">Title of the window.</param>
        /// <param name="description">A brief description of this guide.</param>
        /// <param name="itemPopulator">A static function that will return a list of <see cref="IGuideItem"/>(s).</param>
        /// <param name="guideOptions">Takes a <see cref="GuideWindow.GuideOptions"/>.</param>
        /// <remarks>
        /// The <paramref name="itemPopulator"/> parameter function needs to have the <see cref="GuideItemsAttribute"/> attribute
        /// in order to correctly repaint the Guide window on Unity's domain reload.
        /// <example>
        /// <code>
        /// <![CDATA[
        /// public static Foo()
        /// {
        ///     Guide.Create("Foo title", "Bar desc.", GetItems).Show();
        /// }
        ///
        /// [GuideItems]
        /// public static List<IGuideItem> GetItems()
        /// {
        ///     return new List<IGuideItem>();
        /// }
        /// ]]>
        /// </code>
        /// </example>
        /// </remarks>
        /// <returns>Returns a <see cref="GuideWindow"/>.</returns>
        public static GuideWindow Create(
            string title,
            string description,
            Func<List<IGuideItem>> itemPopulator,
            GuideWindow.GuideOptions guideOptions)
        {
            if (GuideWindowIntances.TryGetValue(GetGuideHash(title, description), out var window))
                return window;

            var id = $"{itemPopulator.Method.DeclaringType}.{itemPopulator.Method.Name}";
            window = ScriptableObject.CreateInstance<GuideWindow>();
            window.Setup(title, description, id, guideOptions);

            return window;
        }

        internal static int GetGuideHash(string title, string description) => (title + description).GetHashCode();
    }
}
