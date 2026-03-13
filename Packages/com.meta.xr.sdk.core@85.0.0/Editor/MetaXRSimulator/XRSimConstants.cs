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
using System.IO;
using Meta.XR.Editor.ToolingSupport;

namespace Meta.XR.Simulator.Editor
{
    internal static class XRSimConstants
    {

#if UNITY_EDITOR_OSX
        public const string AppId = "9961418137219995";
        public const string ReleaseNotesUrl = "https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-mac-arm";
        public const string UnityXRPackage = "com.unity.xr.openxr@>=1.13.0";
#else
        public const string AppId = "28549923061320041";
        public const string ReleaseNotesUrl = "https://developers.meta.com/horizon/downloads/package/meta-xr-simulator-windows";
        public const string UnityXRPackage = "com.unity.xr.openxr";
#endif
        public static string DownloadURL = $"https://www.facebook.com/horizon_devcenter_download?app_id={AppId}&sdk_version={ToolUsage.GetSdkVersion()}";

        /// <summary>
        /// Gets the MetaXR Simulator installation directory dynamically:
        /// - Windows: Uses xrsim:// protocol handler registry lookup
        /// - macOS: Checks /Applications/MetaXRSimulator.app directory
        /// Returns null if detection fails
        /// </summary>
        public static string AppDataFolderPath
        {
            get
            {
                return XRSimInstallationDetector.GetXRSimInstallationDirectory();
            }
        }
        public const string OpenXrRuntimeEnvKey = "XR_RUNTIME_JSON";
        public const string PreviousOpenXrRuntimeEnvKey = "XR_RUNTIME_JSON_PREV";
        public const string OpenXrSelectedRuntimeEnvKey = "XR_SELECTED_RUNTIME_JSON";
        public const string PreviousOpenXrSelectedRuntimeEnvKey = "XR_SELECTED_RUNTIME_JSON_PREV";
        public const string OpenXrOtherRuntimeEnvKey = "OTHER_XR_RUNTIME_JSON";
        public const string XrSimConfigEnvKey = "META_XRSIM_CONFIG_JSON";
        public const string PreviousXrSimConfigEnvKey = "META_XRSIM_CONFIG_JSON_PREV";
        public const string ProjectTelemetryId = "META_PROJECT_TELEMETRY_ID";
        public const string PublicName = "Meta XR Simulator";
        public const string MenuPath = "Meta/" + PublicName;
        public const string LegacyPackageName = "com.meta.xr.simulator"; // Old XR Simulator v1 package

        public const string OculusXRPackageName = "com.unity.xr.oculus";
        public static int? PackageVersion => 1; // Simplified fallback

        // Toolbar and UI related constants
        public const string MqdhCategoryId = "857564592791179";


        public static string MetaOpenXrSimulationJsonPath => Path.GetFullPath(Path.Join(AppDataFolderPath, "meta_openxr_simulator.json"));
        public static readonly string DownloadFolderPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

}
