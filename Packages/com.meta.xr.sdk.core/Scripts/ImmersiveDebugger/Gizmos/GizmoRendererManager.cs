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


using Meta.XR.ImmersiveDebugger.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meta.XR.ImmersiveDebugger.Gizmo
{
    /// <summary>
    /// Manage GizmoRenderers for a specific gizmo GameObject it belong to,
    /// which corresponds to one toggle in IDF UI panel.
    /// Handling multiple instances case that can dynamically add/disable renderer
    /// components from the gizmo GameObject if the instances count changes.
    /// </summary>
    internal class GizmoRendererManager : MonoBehaviour
    {
        private Type _classType;
        private MemberInfo _memberInfo;
        private bool _isStatic;
        private InstanceCache _instanceCache;
        private DebugGizmoType _gizmoType;
        private Color _gizmoColor;

        private List<GizmoRenderer> _renderers = new List<GizmoRenderer>();
#if UNITY_6000_5_OR_NEWER
        private HashSet<EntityId> _enabledInstances = new HashSet<EntityId>();
#else
        private HashSet<int> _enabledInstances = new HashSet<int>();
#endif

        public void Setup(Type classType, MemberInfo memberInfo, DebugGizmoType gizmoType, Color gizmoColor, InstanceCache instanceCache)
        {
            _classType = classType;
            _memberInfo = memberInfo;
            _isStatic = memberInfo.IsStatic();
            _instanceCache = instanceCache;
            _gizmoType = gizmoType;
            _gizmoColor = gizmoColor;
        }

        private void Start()
        {
            // Adding at least one renderer for common/static use case
            AddGizmoRenderer();
        }

        private void Update()
        {
            if (_isStatic && _renderers.Count != 0)
            {
                _renderers[0].UpdateDataSource(_memberInfo.GetValue(null));
#if UNITY_6000_5_OR_NEWER
                _renderers[0].enabled = _enabledInstances.Contains(EntityId.None);
#else
                _renderers[0].enabled = _enabledInstances.Contains(0);
#endif
            }
            else
            {
                var instances = _instanceCache.GetCacheDataForClass(_classType);
                if (instances.Count == 0)
                {
                    return;
                }

                while (_renderers.Count < instances.Count)
                {
                    AddGizmoRenderer();
                }
                int i = 0;
                for (; i < instances.Count; i++)
                {
                    var instance = instances[i];
                    if (instance.Valid)
                    {
                        _renderers[i].UpdateDataSource(_memberInfo.GetValue(instance.Instance));
                        _renderers[i].enabled = _enabledInstances.Contains(instance.InstanceId);
                    }
                    else
                    {
                        _renderers[i].enabled = false;
                    }
                }
                // disabling unused renderers, leveraging strict (enabled, disabled) sequence
                while (i < _renderers.Count && _renderers[i].enabled)
                {
                    _renderers[i].enabled = false;
                    i++;
                }
            }

        }

        private void AddGizmoRenderer()
        {
            var renderer = gameObject.AddComponent<GizmoRenderer>();
            renderer.SetUpGizmo(_gizmoType, _gizmoColor);
            renderer.enabled = false;
            _renderers.Add(renderer);
        }

        public bool GetState(Object instance)
        {
#if UNITY_6000_5_OR_NEWER
            var id = instance != null ? instance.GetEntityId() : EntityId.None;
#else
            var id = instance != null ? instance.GetInstanceID() : 0;
#endif
            return _enabledInstances.Contains(id);
        }

        public void SetState(Object instance, bool state)
        {
#if UNITY_6000_5_OR_NEWER
            var id = instance != null ? instance.GetEntityId() : EntityId.None;
#else
            var id = instance != null ? instance.GetInstanceID() : 0;
#endif
            if (state)
            {
                _enabledInstances.Add(id);
            }
            else
            {
                _enabledInstances.Remove(id);
            }
        }
    }
}

