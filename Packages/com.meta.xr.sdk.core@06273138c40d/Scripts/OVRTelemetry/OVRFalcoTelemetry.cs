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
#if UNITY_EDITOR
using Meta.XR.Editor.Callbacks;
#endif
using System.Text;
using UnityEngine;

namespace Meta.XR.Editor.FalcoOVRTelemetry
{
    internal static class OVRFalcoTelemetry
    {
        internal static bool IsActive = global::OVRTelemetry.IsActive;

        public static readonly FalcoTelemetryClient InactiveClient = new NullTelemetryClient();
        public static readonly FalcoTelemetryClient ActiveClient = new ActiveFalcoTelemetryClient();
        public static FalcoTelemetryClient Client => IsActive ? ActiveClient : InactiveClient;

        public static OVRFalcoEvent NewEvent(string eventName)
        {
            return new OVRFalcoEvent(eventName);
        }

        public static void SendEssential(this OVRFalcoEvent falcoEvent)
        {
            Client.SendEssentialEvent(falcoEvent);
        }

        public static void SendNonEssential(this OVRFalcoEvent falcoEvent)
        {
            Client.SendNonEssentialEvent(falcoEvent);
        }
    }

    internal struct OVRFalcoEvent
    {
        internal string EventName { get; }
        internal Dictionary<string, string> Metadata { get; set; }
        internal string EventType { get; set; }
        internal string EntryPoint { get; set; }
        internal string ErrorMessage { get; set; }
        internal string EventTarget { get; set; }
        internal string Result { get; set; }
        internal string ProductType { get; set; }
        internal ulong MachineOculusUserId { get; set; }

        public OVRFalcoEvent(string eventName)
        {
            EventName = eventName;
            EventType = null;
            ErrorMessage = null;
            EventTarget = null;
            Result = null;
            EntryPoint = null;
            Metadata = new Dictionary<string, string>();
            ProductType = "Unity SDK";
            MachineOculusUserId = 0;
        }

        public void SendEssential()
        {
            OVRFalcoTelemetry.SendEssential(this);
        }

        public void SendNonEssential()
        {
            OVRFalcoTelemetry.SendNonEssential(this);
        }

        public OVRFalcoEvent AddMetadata(string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Metadata[name] = value;
            }

            return this;
        }

        public OVRFalcoEvent AddMetadata(string name, bool value)
        {
            Metadata[name] = value.ToString();
            return this;
        }

        public OVRFalcoEvent AddMetadata(string name, long value)
        {
            Metadata[name] = value.ToString();
            return this;
        }

        public OVRFalcoEvent AddMetadata(string name, double value)
        {
            Metadata[name] = value.ToString();
            return this;
        }

        public OVRFalcoEvent AddMetadata(string name, IEnumerable<string> stringList)
        {
            Metadata[name] = string.Join(", ", stringList);
            return this;
        }

        public string GetMetadataJson()
        {
            if (Metadata == null || Metadata.Count == 0)
            {
                return "{}";
            }

            var sb = new StringBuilder();
            sb.Append("{");
            var first = true;
            foreach (var (key, value) in Metadata)
            {
                if (!first)
                    sb.Append(",");
                first = false;

                sb.Append($"\n  \"{key}\": \"{value}\"");
            }
            sb.Append("\n}");
            return sb.ToString();
        }

    }

    internal abstract class FalcoTelemetryClient
    {
        public abstract void SendEssentialEvent(OVRFalcoEvent falcoEvent);

        public abstract void SendNonEssentialEvent(OVRFalcoEvent falcoEvent);
    }

    internal class NullTelemetryClient : FalcoTelemetryClient
    {
        public override void SendEssentialEvent(OVRFalcoEvent falcoEvent)
        {
        }

        public override void SendNonEssentialEvent(OVRFalcoEvent falcoEvent)
        {
        }
    }

    internal class ActiveFalcoTelemetryClient : FalcoTelemetryClient
    {
        private static OVRPlugin.OptionalBool _isBatchMode => Application.isBatchMode ? OVRPlugin.OptionalBool.True : OVRPlugin.OptionalBool.False;

        private static string _applicationIdentifier;
        private static string ApplicationIdentifier => _applicationIdentifier ??= Application.identifier;

        public override void SendEssentialEvent(OVRFalcoEvent falcoEvent)
        {
            SendEvent(falcoEvent, true);
        }

        public override void SendNonEssentialEvent(OVRFalcoEvent falcoEvent)
        {
            SendEvent(falcoEvent, false);
        }

        private static void SendEvent(OVRFalcoEvent falcoEvent, bool isEssential)
        {

#if UNITY_EDITOR
            var projectGuid = InitializeOnLoad.EditorReady
                ? OVRRuntimeSettings.Instance.TelemetryProjectGuid
                : string.Empty;
#else
            var projectGuid = OVRRuntimeSettings.Instance.TelemetryProjectGuid;
#endif

            var eventData = new OVRPlugin.UnifiedEventData(falcoEvent.EventName)
            {
                isEssential = isEssential ? OVRPlugin.Bool.True : OVRPlugin.Bool.False,
                productType = falcoEvent.ProductType,
                metadata_json = falcoEvent.GetMetadataJson(),
                project_name = ApplicationIdentifier,
                entrypoint = falcoEvent.EntryPoint,
                project_guid = projectGuid,
                type = falcoEvent.EventType,
                target = falcoEvent.EventTarget,
                error_msg = falcoEvent.ErrorMessage,
                is_internal_build = OVRPlugin.OptionalBool.False,
                batch_mode = _isBatchMode,
                machine_oculus_user_id = falcoEvent.MachineOculusUserId
            };

            OVRPlugin.SendUnifiedEvent(eventData);
        }
    }
}
