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
using System.Reflection;
using Meta.XR.ImmersiveDebugger.Utils;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger
{
    [ExecuteInEditMode]
    public class DebugInspector : MonoBehaviour
    {
        [Serializable]
        internal class InspectionRegistry
        {
            [SerializeField] private List<InspectedHandle> handles = new();

            internal List<InspectedHandle> Handles => handles;

            public void Initialize(DebugInspector owner)
            {
                // Initialize Handles
                foreach (var handle in handles)
                {
                    handle.Initialize(owner);
                }

                // Search for new components
                var components = owner.GetComponents<Component>();
                foreach (var component in components)
                {
                    // Invalid component
                    if (component == null) continue;

                    var type = component.GetType();

                    // Reject DebugInspector
                    if (type == typeof(DebugInspector)) continue;

                    // Pre-existing component
                    if (TryGetHandle(component, out var handle)) continue;

                    // Discovering a new component
                    handle = new InspectedHandle(owner, type);
                    handles.Add(handle);
                }
            }

            private bool TryGetHandle(Component component, out InspectedHandle inspectedHandle)
            {
                inspectedHandle = null;
                foreach (var handle in handles)
                {
                    if (handle.InstanceHandle.Instance == component)
                    {
                        inspectedHandle = handle;
                        break;
                    }
                }

                return inspectedHandle != null;
            }
        }

        [SerializeField] private InspectionRegistry registry = new();

        internal InspectionRegistry Registry => registry;

        private void OnValidate()
        {
            Initialize();
        }

        internal void Initialize()
        {
            registry.Initialize(this);
        }

        private void OnEnable()
        {
            Initialize();

            if (Application.isPlaying)
            {
                DebugInspectorManager.Instance.RegisterInspector(this);
            }
        }

        private void OnDisable()
        {
            DebugInspectorManager.Instance.UnregisterInspector(this);
        }
    }
}

