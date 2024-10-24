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

using Meta.XR.Editor.UserInterface;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;

namespace Meta.XR.Guides.Editor
{
    public static class Utils
    {
        internal static readonly TextureContent.Category GuidedAccountSetupIcons =
            new("Guide/Icons");

        internal static readonly TextureContent.Category GuidedAccountSetupTextures =
            new("Guide/Textures");

        public enum TriggerSource
        {
            Menu,
            Inspector,
            UPST
        }

        public enum GuideItemPlacementType
        {
            Horizontal,
            Vertical
        }

        internal static Color GetColorByStatus(GuideStyles.ContentStatusType type)
        {
            Color color = type switch
            {
                GuideStyles.ContentStatusType.Success => SuccessColor,
                GuideStyles.ContentStatusType.Warning => WarningColor,
                GuideStyles.ContentStatusType.Error => ErrorColor,
                _ => LightGray
            };

            return color;
        }

        internal static void OpenURL(string url, string sourceWindow = "")
        {
            Application.OpenURL(url);

            OVRTelemetry.Start(OVRTelemetryConstants.GuidedSetup.MarkerId.URLOpen)
                .AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.GSTSource, sourceWindow)
                .AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.URL, url)
                .Send();
        }
    }
}
