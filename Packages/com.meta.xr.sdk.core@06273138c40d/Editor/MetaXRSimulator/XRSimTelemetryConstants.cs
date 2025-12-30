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

internal static class XRSimTelemetryConstants
{
    public static class MarkerId
    {
        public const int SESInteraction = 163056472;
        public const int ToggleState = 163059165;
        public const int EditorRun = 163063015;
        public const int SettingsChange = 163069416;
        public const int BinariesInstalled = 163060014;
    }

    public static class AnnotationType
    {
        public const string IsActive = "active";
        public const string Action = "action";
        public const string XRSimEnabled = "xrsimenabled";
        public const string EngineXRSimSession = "engine_xrsim_session";
        public const string ErrorMessage = "error_message";

        // Required OVRManager annotation types
        public const string ProjectName = "ProjectName";
        public const string ProjectGuid = "ProjectGuid";
        public const string Internal = "Internal";
        public const string BatchMode = "BatchMode";
        public const string ProcessorType = "ProcessorType";

        public const string Origin = "Origin";

        public const string XRSimVersion = "XRSimVersion";
        public const string XrSimPackageVersion = "XrSimPackageVersion";

        // Settings properties
        public const string PendingUpdate = "PendingUpdate";
        public const string OpenInstallationDir = "OpenInstallationDir";
        public const string NotifyNewVersionAvailable = "NotifyNewVersionAvailable";
        public const string PreferredVersion = "PreferredVersion";
        public const string TriggerInstall = "TriggerInstall";
        public const string AutomaticServers = "AutomaticServers";
        public const string DisplayServers = "DisplayServers";
        public const string SelectedEnvironment = "SelectedEnvironment";
    }
}
