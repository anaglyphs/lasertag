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
using Meta.XR.ImmersiveDebugger.Gizmo;
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.UserInterface;
using Meta.XR.ImmersiveDebugger.Utils;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger
{
    internal class DebugInspectorManager
    {
        private abstract class ManagerFromInspector : IDebugManager
        {
            private readonly Dictionary<Type, List<MemberInfo>> _dictionary = new();
            private IDebugUIPanel _uiPanel;
            protected InstanceCache InstanceCache;

            public void Setup(IDebugUIPanel panel, InstanceCache cache)
            {
                _uiPanel = panel;
                InstanceCache = cache;
            }

            public void ProcessType(Type type) => throw new NotImplementedException();

            public void ProcessTypeFromInspector(Type type, InstanceHandle handle, MemberInfo memberInfo, DebugMember memberAttribute)
            {
                var uiInspector = _uiPanel.RegisterInspector(handle, memberAttribute.Category);
                var member = uiInspector.RegisterMember(memberInfo, memberAttribute);

                if (RegisterSpecialisedWidget(member, memberInfo, memberAttribute, handle))
                {
                    if (!_dictionary.TryGetValue(type, out var list))
                    {
                        list = new List<MemberInfo>();
                        _dictionary.Add(type, list);
                    }

                    if (!list.Contains(memberInfo))
                    {
                        list.Add(memberInfo);
                    }
                }
            }
            public int GetCountPerType(Type type)
            {
                _dictionary.TryGetValue(type, out var actions);
                return actions?.Count ?? 0;
            }

            protected abstract bool RegisterSpecialisedWidget(IMember member, MemberInfo memberInfo, DebugMember memberAttribute, InstanceHandle handle);
            public abstract string TelemetryAnnotation { get; }
        }

        private class TweakManagerFromInspector : ManagerFromInspector
        {
            protected override bool RegisterSpecialisedWidget(IMember member, MemberInfo memberInfo,
                DebugMember memberAttribute, InstanceHandle handle)
            {
                if (!memberAttribute.Tweakable ||
                    !(TweakUtils.IsTypeSupported((memberInfo as FieldInfo)?.FieldType) ||
                     TweakUtils.IsTypeSupported((memberInfo as PropertyInfo)?.PropertyType)))
                {
                    return false;
                }

                var tweak = member.GetTweak();
                if (!tweak?.Matches(memberInfo, handle.Instance) ?? true)
                {
                    member.RegisterTweak(TweakUtils.Create(memberInfo, memberAttribute, handle.Instance));
                }
                return true;
            }

            public override string TelemetryAnnotation => Telemetry.AnnotationType.Tweaks;
        }

        private class ActionManagerFromInspector : ManagerFromInspector
        {
            protected override bool RegisterSpecialisedWidget(IMember member, MemberInfo memberInfo,
                DebugMember memberAttribute, InstanceHandle handle)
            {
                if (memberInfo.MemberType != MemberTypes.Method)
                {
                    return false;
                }

                var method = memberInfo as MethodInfo;
                if (method == null || method.GetParameters().Length != 0 || method.ReturnType != typeof(void))
                {
                    return false;
                }

                var action = member.GetAction();
                if (!action?.Matches(memberInfo, handle.Instance) ?? true)
                {
                    member.RegisterAction(new ActionHook(memberInfo, handle.Instance, memberAttribute));
                }
                return true;

            }

            public override string TelemetryAnnotation => Telemetry.AnnotationType.Actions;
        }

        private class WatchManagerFromInspector : ManagerFromInspector
        {
            protected override bool RegisterSpecialisedWidget(IMember member, MemberInfo memberInfo,
                DebugMember memberAttribute, InstanceHandle handle)
            {
                if (!(memberInfo.MemberType == MemberTypes.Property | memberInfo.MemberType == MemberTypes.Field))
                {
                    return false;
                }

                var watch = member.GetWatch();
                if (!watch?.Matches(memberInfo, handle.Instance) ?? true)
                {
                    member.RegisterWatch(WatchUtils.Create(memberInfo, handle.Instance, memberAttribute));
                }
                return true;
            }

            public override string TelemetryAnnotation => Telemetry.AnnotationType.Watches;
        }

        private class GizmoManagerFromInspector : ManagerFromInspector
        {
            private readonly Dictionary<MemberInfo, GizmoRendererManager> _memberToGizmoRendererManagerDict = new();
            protected override bool RegisterSpecialisedWidget(IMember member, MemberInfo memberInfo,
                DebugMember memberAttribute, InstanceHandle handle)
            {
                if (memberAttribute.GizmoType == DebugGizmoType.None)
                {
                    return false;
                }

                if (!_memberToGizmoRendererManagerDict.TryGetValue(memberInfo, out var gizmoRendererManager))
                {
                    if (AddGizmo(handle.Type, memberInfo, memberAttribute, out gizmoRendererManager))
                    {
                        _memberToGizmoRendererManagerDict[memberInfo] = gizmoRendererManager;
                    }
                }

                if (gizmoRendererManager == null)
                {
                    return false;
                }

                var gizmo = member.GetGizmo();
                if (!gizmo?.Matches(memberInfo, handle.Instance) ?? true)
                {
                    member.RegisterGizmo(new GizmoHook(memberInfo, handle.Instance, memberAttribute, OnStateChanged));
                }
                return true;

                void OnStateChanged(bool state) => _memberToGizmoRendererManagerDict[memberInfo].SetState(handle.Instance, state);
            }

            private bool AddGizmo(Type type, MemberInfo member, DebugMember gizmoAttribute, out GizmoRendererManager gizmoRendererManager)
            {
                if (!GizmoTypesRegistry.IsValidDataTypeForGizmoType(member.GetDataType(), gizmoAttribute.GizmoType))
                {
                    Debug.LogWarning($"Invalid registration of gizmo {member.Name}: type not matching gizmo type");
                    gizmoRendererManager = null;
                    return false;
                }

                var gizmo = new GameObject($"{member.Name}Gizmo");
                gizmoRendererManager = gizmo.AddComponent<GizmoRendererManager>();
                gizmoRendererManager.Setup(type, member, gizmoAttribute.GizmoType, gizmoAttribute.Color, InstanceCache);
                return true;
            }

            public override string TelemetryAnnotation => Telemetry.AnnotationType.Gizmos;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() // reset static fields in case of domain reload disabled
        {
            _instance = null;
            _uiPanel = null;
        }

        private static DebugInspectorManager _instance;
        public static DebugInspectorManager Instance => _instance ??= new DebugInspectorManager();

        private readonly InstanceCache _instanceCache = new();
        private readonly List<IDebugManager> _subDebugManagers = new();
        private readonly List<DebugInspector> _inspectors = new();
        private static IDebugUIPanel _uiPanel;

        private DebugInspectorManager()
        {
            if (DebugManager.Instance == null)
            {
                DebugManager.OnReady -= OnReady; // avoid duplicated registration when domain reload disabled
                DebugManager.OnReady += OnReady;
            }
            else
            {
                OnReady(DebugManager.Instance);
            }
        }

        internal static void Destroy()
        {
            if (_instance != null)
            {
                DebugManager.OnReady -= _instance.OnReady;
            }
        }

        private void InitSubManagers()
        {
            RegisterManager<GizmoManagerFromInspector>();
            RegisterManager<WatchManagerFromInspector>();
            RegisterManager<ActionManagerFromInspector>();
            RegisterManager<TweakManagerFromInspector>();
        }

        private void RegisterManager<TManagerType>()
            where TManagerType : IDebugManager, new()
        {
            var manager = new TManagerType();
            manager.Setup(_uiPanel, _instanceCache);
            _subDebugManagers.Add(manager);
        }

        public void RegisterInspector(DebugInspector inspector)
        {
            _inspectors.Add(inspector);
            ProcessInspector(inspector);
        }

        public void UnregisterInspector(DebugInspector inspector)
        {
            UnprocessInspector(inspector);
            _inspectors.Remove(inspector);
        }

        private void OnReady(DebugManager debugManager)
        {
            var telemetryTracker = Telemetry.TelemetryTracker.Init(Telemetry.Method.DebugInspector, _subDebugManagers, _instanceCache, debugManager);
            _uiPanel = debugManager.UiPanel;

            InitSubManagers();

            foreach (var inspector in _inspectors)
            {
                ProcessInspector(inspector);
            }

            telemetryTracker.OnStart();
        }

        private void ProcessInspector(DebugInspector inspector)
        {
            if (_uiPanel == null) return;

            foreach (var entry in inspector.Registry.Handles)
            {
                if (!entry.Visible) continue;

                var handle = entry.InstanceHandle;
                _instanceCache.RegisterHandle(handle);
                foreach (var memberEntry in entry.inspectedMembers)
                {
                    if (!memberEntry.Visible) continue;

                    var memberInfo = memberEntry.MemberInfo;
                    if (memberInfo == null) continue;

                    var attribute = memberEntry.attribute;
                    if (attribute == null) continue;

                    _uiPanel.RegisterInspector(handle, attribute.Category);

                    foreach (var manager in _subDebugManagers)
                    {
                        manager.ProcessTypeFromInspector(handle.Type, handle, memberInfo, attribute);
                    }
                }
            }
        }

        private void UnprocessInspector(DebugInspector inspector)
        {
            if (_uiPanel == null) return;

            foreach (var entry in inspector.Registry.Handles)
            {
                var handle = entry.InstanceHandle;
                foreach (var memberEntry in entry.inspectedMembers)
                {
                    var attribute = memberEntry.attribute;
                    if (attribute == null) continue;

                    _uiPanel.UnregisterInspector(handle, attribute.Category, false);
                }
                _instanceCache.UnregisterHandle(handle);
            }
        }
    }
}

