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

using Meta.XR.MetaWand.Editor.API;
using UnityEngine;

namespace Meta.XR.MetaWand.Editor.Telemetry
{
    internal static class MetaWandEvent
    {
        public static bool SendMetaWandEvent(this OVRPlugin.UnifiedEventData eventData)
        {
            eventData.productType = "meta_wand";
            eventData.machine_oculus_user_id = MetaWandAuth.Data.IsValid ? MetaWandAuth.Data.ProfileId : 0;
            eventData.SetMetadata("app_version", Application.version);
            eventData.SetMetadata("device_os", SystemInfo.operatingSystem);
            eventData.SetMetadata("developer_platform", Application.platform.ToString());
            return eventData.Send();
        }
    }
}
