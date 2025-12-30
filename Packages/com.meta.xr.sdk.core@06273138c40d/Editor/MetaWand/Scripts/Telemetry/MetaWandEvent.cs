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
using Meta.XR.Editor.FalcoOVRTelemetry;
using Meta.XR.MetaWand.Editor.API;
using UnityEngine;

namespace Meta.XR.MetaWand.Editor.Telemetry
{
    internal static class MetaWandEvent
    {
        private static OVRFalcoEvent CreateBaseEvent(Data eventData)
        {
            var falcoEvent = new OVRFalcoEvent(eventData.Name)
            {
                ProductType = "meta_wand",
                EventType = eventData.Type,
                EventTarget = eventData.Target,
                EntryPoint = eventData.Entrypoint,
                ErrorMessage = eventData.ErrorMessage,
                MachineOculusUserId = MetaWandAuth.Data.IsValid ? MetaWandAuth.Data.ProfileId : 0
            }.AddMetadata("app_version", Application.version)
            .AddMetadata("device_os", SystemInfo.operatingSystem)
            .AddMetadata("developer_platform", Application.platform.ToString());

            foreach (var (value, key) in eventData.Metadata ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                falcoEvent.AddMetadata(value, key);
            }

            return falcoEvent;
        }

        public record Data
        {
            public string Name;
            public bool IsEssential;
            public Dictionary<string, string> Metadata;
            public string Type;
            public string Target;
            public string Entrypoint;
            public string ErrorMessage;
        }

        public static void Send(Data eventData)
        {
            var falcoEvent = CreateBaseEvent(eventData);

            if (eventData.IsEssential)
            {
                falcoEvent.SendEssential();
            }
            else
            {
                falcoEvent.SendNonEssential();
            }
        }
    }
}
