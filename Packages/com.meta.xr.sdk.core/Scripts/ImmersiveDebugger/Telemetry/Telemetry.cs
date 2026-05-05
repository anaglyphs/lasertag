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
using Meta.XR.ImmersiveDebugger.Manager;
using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using Meta.XR.ImmersiveDebugger.Utils;
using static OVRTelemetry;

namespace Meta.XR.ImmersiveDebugger
{
    internal static class Telemetry
    {
        [Markers]
        internal static class MarkerId
        {
            // Back-End Managers Events
            public const int ComponentTracked = 163059554;
            public const int Run = 163061656;
            public const int FrameUpdate = 163056655;

            // User Interface Events
            public const int PanelOpen = 163057243;
            public const int PanelClose = 163059919;
            public const int PanelInteraction = 163058794;
        }

        internal static class FalcoEventName
        {
            // Back-End Managers Events
            public const string ComponentTracked = "ID_COMPONENT_TRACKED";
            public const string Run = "ID_RUN";
            public const string FrameUpdate = "ID_FRAME_UPDATE";

            // User Interface Events
            public const string PanelOpen = "ID_PANEL_OPEN";
            public const string PanelClose = "ID_PANEL_CLOSE";
            public const string PanelInteraction = "ID_PANEL_INTERACTION";
        }

        internal enum State
        {
            OnStart,
            OnFocusLost,
            OnDisable
        }

        internal enum Method
        {
            Attributes,
            DebugInspector,
            Hierarchy,
            RuntimeAPI,
        }

        internal static class AnnotationType
        {
            // Back-End Managers Events
            public const string Type = "Type";
            public const string Method = "Method";
            public const string State = "State";
            public const string Instances = "Instances";
            public const string Gizmos = "Gizmos";
            public const string Watches = "Watches";
            public const string Tweaks = "Tweaks";
            public const string Actions = "Actions";
            public const string IsCustom = "IsCustom";

            // User Interface Events
            public const string Action = "action";
            public const string ActionType = "action_type";
            public const string Origin = "origin";
            public const string OriginType = "origin_type";
            public const string Platform = "platform";
        }

        internal class TelemetryTracker
        {
            private readonly Method _method;
            private readonly InstanceCache _cache;
            private readonly IEnumerable<IDebugManager> _managers;
            private OVRPlugin.UnifiedEventData _runFalcoEvent;

            public static TelemetryTracker Init(Method method,
                IEnumerable<IDebugManager> managers,
                InstanceCache cache,
                DebugManager debugManager)
            {
                var telemetryTracker = new TelemetryTracker(method, managers, cache);
                debugManager.OnFocusLostAction += telemetryTracker.OnFocusLost;
                debugManager.OnDisableAction += telemetryTracker.OnDisable;

                return telemetryTracker;
            }

            private TelemetryTracker(
                Method method,
                IEnumerable<IDebugManager> managers,
                InstanceCache cache)
            {
                _method = method;
                _cache = cache;
                _managers = managers;

                _runFalcoEvent = new OVRPlugin.UnifiedEventData(FalcoEventName.Run)
                {
                    isEssential = OVRPlugin.Bool.True,
                    productType = OVRPlugin.ProductType.ImmersiveDebugger
                };
                _runFalcoEvent.SetMetadata(AnnotationType.Method, _method.ToString());
                _runFalcoEvent.SetMetadata(AnnotationType.State, State.OnStart.ToString());
                _runFalcoEvent.AddPlayModeOrigin();
            }

            public void OnStart()
            {
                SendStart();
                SendComponentTracked(State.OnStart);
            }

            private void OnFocusLost()
            {
                SendComponentTracked(State.OnFocusLost);
            }

            private void OnDisable()
            {
                SendComponentTracked(State.OnDisable);
            }

            private void SendStart()
            {
                _runFalcoEvent.Send();
            }

            private void SendComponentTracked(State state)
            {
                foreach (var (type, instances) in _cache.CacheData)
                {
                    var instancesCount = instances.Count;
                    if (instancesCount <= 0) continue;

                    var unifiedEvent = new OVRPlugin.UnifiedEventData(FalcoEventName.ComponentTracked)
                    {
                        isEssential = OVRPlugin.Bool.False,
                        productType = OVRPlugin.ProductType.ImmersiveDebugger
                    };
                    unifiedEvent.SetMetadata(AnnotationType.State, state.ToString());
                    unifiedEvent.SetMetadata(AnnotationType.Method, _method.ToString());
                    unifiedEvent.SetMetadata(AnnotationType.Instances, instances.Count.ToString());

                    // When Type is from a non Meta assembly, we will only send a hash
                    if (type.IsTypeCustom())
                    {
                        unifiedEvent.SetMetadata(AnnotationType.Type, type.GetTypeHash());
                        unifiedEvent.SetMetadata(AnnotationType.IsCustom, true);
                    }
                    else
                    {
                        unifiedEvent.SetMetadata(AnnotationType.Type, type.FullName);
                        unifiedEvent.SetMetadata(AnnotationType.IsCustom, false);
                    }

                    foreach (var manager in _managers)
                    {
                        unifiedEvent.SetMetadata(manager.TelemetryAnnotation, manager.GetCountPerType(type).ToString());
                    }

                    unifiedEvent.AddPlayModeOrigin();
                    unifiedEvent.Send();
                }
            }
        }

        internal static string GetTypeHash(this Type type)
        {
            var hash = type.GetHashCode();
            var fullName = type.FullName;
            var fullNameHash = fullName?.GetHashCode() ?? 0;
            var uniqueHash = hash ^ fullNameHash;
            return uniqueHash.ToString();
        }

        private static readonly List<string> NonCustomAssemblies = new()
        {
            "Oculus.",
            "Meta."
        };

        private static bool IsTypeCustom(this Type type)
        {
            var assemblyName = type.Assembly.GetName().Name;
            foreach (var assembly in NonCustomAssemblies)
            {
                if (assemblyName.StartsWith(assembly, StringComparison.InvariantCultureIgnoreCase)
                    )
                {
                    return false;
                }
            }

            return true;
        }

        public static void OnPanelActiveStateChanged(Panel panel)
        {
            // Ignore any state change that happens before the panel is initialized
            if (!panel.Initialised) return;

            var falcoEventName = panel.isActiveAndEnabled ? FalcoEventName.PanelOpen : FalcoEventName.PanelClose;

            var unifiedEvent = new OVRPlugin.UnifiedEventData(falcoEventName)
            {
                isEssential = OVRPlugin.Bool.True,
                productType = OVRPlugin.ProductType.ImmersiveDebugger
            };
            unifiedEvent.SetMetadata(AnnotationType.Action, panel.name);
            unifiedEvent.SetMetadata(AnnotationType.ActionType, panel.GetType().Name);
            unifiedEvent.AddPlayModeOrigin();
            unifiedEvent.Send();
        }

        public static void OnButtonClicked(Button button)
        {
            var panel = FetchPanel(button);
            var unifiedEvent = new OVRPlugin.UnifiedEventData(FalcoEventName.PanelInteraction)
            {
                isEssential = OVRPlugin.Bool.False,
                productType = OVRPlugin.ProductType.ImmersiveDebugger
            };
            unifiedEvent.SetMetadata(AnnotationType.Action, button.name);
            unifiedEvent.SetMetadata(AnnotationType.ActionType, button.GetType().Name);
            unifiedEvent.SetMetadata(AnnotationType.Origin, panel?.name);
            unifiedEvent.SetMetadata(AnnotationType.OriginType, panel?.GetType().Name);
            unifiedEvent.AddPlayModeOrigin();
            unifiedEvent.Send();
        }

        private static Panel FetchPanel(Controller controller)
        {
            if (controller == null) return null;
            if (controller is Panel panel) return panel;

            return FetchPanel(controller.Owner);
        }
    }
}
