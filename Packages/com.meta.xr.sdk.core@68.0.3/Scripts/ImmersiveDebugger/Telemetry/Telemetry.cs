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
using Meta.XR.ImmersiveDebugger.Utils;
using static OVRTelemetry;

namespace Meta.XR.ImmersiveDebugger
{
    internal static class Telemetry
    {
        [Markers]
        public static class MarkerId
        {
            public const int SettingsAccessed = 163059815;
            public const int SettingsChanged = 163065143;
            public const int ComponentTracked = 163059554;
            public const int Run = 163061656;
            public const int FrameUpdate = 163056655;
        }

        public enum State
        {
            OnStart,
            OnFocusLost,
            OnDisable
        }

        public enum Method
        {
            Attributes,
            DebugInspector
        }

        public static class AnnotationType
        {
            public const string Origin = "Origin";
            public const string Type = "Type";
            public const string Value = "Value";
            public const string Method = "Method";
            public const string State = "State";
            public const string Instances = "Instances";
            public const string Gizmos = "Gizmos";
            public const string Watches = "Watches";
            public const string Tweaks = "Tweaks";
            public const string Actions = "Actions";
            public const string IsCustom = "IsCustom";
        }

        internal class TelemetryTracker
        {
            private readonly Method _method;
            private readonly InstanceCache _cache;
            private readonly IEnumerable<IDebugManager> _managers;
            private OVRTelemetryMarker _runTelemetryMarker;

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

                _runTelemetryMarker = Start(MarkerId.Run)
                    .AddAnnotation(AnnotationType.Method, _method.ToString())
                    .AddAnnotation(AnnotationType.State, State.OnStart.ToString())
                    .AddPlayModeOrigin();
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
                _runTelemetryMarker.Send();
            }

            private void SendComponentTracked(State state)
            {
                foreach (var (type, instances) in _cache.CacheData)
                {
                    var instancesCount = instances.Count;
                    if (instancesCount <= 0) continue;

                    var marker = Start(MarkerId.ComponentTracked)
                        .AddPlayModeOrigin()
                        .AddAnnotation(AnnotationType.State, state.ToString())
                        .AddAnnotation(AnnotationType.Method, _method.ToString())
                        .AddAnnotation(AnnotationType.Instances, instances.Count.ToString());

                    if (type.IsTypeCustom())
                    {
                        // When Type is from a non Meta assembly, we will only send a hash
                        marker = marker.AddAnnotation(AnnotationType.Type, type.GetTypeHash())
                            .AddAnnotation(AnnotationType.IsCustom, true);
                    }
                    else
                    {
                        marker = marker.AddAnnotation(AnnotationType.Type, type.FullName)
                            .AddAnnotation(AnnotationType.IsCustom, false);
                    }

                    foreach (var manager in _managers)
                    {
                        marker = marker.AddAnnotation(manager.TelemetryAnnotation, manager.GetCountPerType(type).ToString());
                    }

                    marker.Send();
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
                if (assemblyName.StartsWith(assembly, StringComparison.InvariantCultureIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
