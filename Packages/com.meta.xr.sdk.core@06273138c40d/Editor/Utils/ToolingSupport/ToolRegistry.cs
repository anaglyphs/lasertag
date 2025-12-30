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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Meta.XR.Editor.ToolingSupport
{
    internal static class ToolRegistry
    {
        private static readonly List<ToolDescriptor> _registry = new();
        private static readonly List<ToolDescriptor> ToInitialize = new();
        private static bool _initializing = false;

        public static IEnumerable<ToolDescriptor> Registry => _registry;

        public static void Register(ToolDescriptor descriptor)
        {
            _registry.Add(descriptor);

            // Register a delayed callback to initialize descriptors
            if (!ToInitialize.Any() && !_initializing)
            {
                EditorApplication.update += InitializeDescriptorsAsync;
            }

            ToInitialize.Add(descriptor);
        }

        private static async void InitializeDescriptorsAsync()
        {
            // Remove the callback immediately to prevent multiple calls
            EditorApplication.update -= InitializeDescriptorsAsync;
            _initializing = true;

            // First, wait for all ramp-up checks to complete
            var rampUpTasks = ToInitialize
                .Where(d => d.EnableRampUp)
                .Select(d => d.CheckRampUpAsync())
                .ToArray();

            if (rampUpTasks.Any())
            {
                await Task.WhenAll(rampUpTasks);
            }

            // For tools without ramp-up, just mark them as checked
            foreach (var descriptor in ToInitialize.Where(d => !d.EnableRampUp))
            {
                await descriptor.CheckRampUpAsync();
            }

            // Now initialize all tools (only ramped-up ones will register menus)
            foreach (var descriptor in ToInitialize)
            {
                descriptor.Initialize();
            }

            ToInitialize.Clear();
            _initializing = false;
        }
    }
}
