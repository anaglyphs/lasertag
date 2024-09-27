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

using MarkerPoint = OVRTelemetry.MarkerPoint;
using static OVRTelemetry;

internal static class OVRTelemetryConstants
{
    public static class OVRManager
    {
        [Markers]
        public static class MarkerId
        {
            public const int Init = 163069401;
            public const int Consent = 163056770;
        }

        public static class AnnotationTypes
        {
            public const string ProjectName = "ProjectName";
            public const string ProjectGuid = "ProjectGuid";
            public const string Origin = "Origin";
            public const string EngineVersion = "developer_platform_version";
            public const string BatchMode = "BatchMode";
        }

        public enum ConsentOrigins
        {
            Popup,
            Settings,
            Legacy
        }

        public static readonly MarkerPoint InitializeInsightPassthrough =
            new MarkerPoint("InitializeInsightPassthrough");

        public static readonly MarkerPoint InitPermissionRequest = new MarkerPoint("InitPermissionRequest");
    }

    public static class Editor
    {
        [Markers]
        public static class MarkerId
        {
            public const int Start = 163067235;
            public const int ComponentAdd = 163060094;
            public const int FeaturesInScene = 163069415;
        }

        public static class AnnotationType
        {
            public const string ComponentName = "ComponentName";
            public const string AssemblyName = "AssemblyName";
            public const string UsesProSkin = "UsesProSkin";
            public const string Origin = "Origin";
        }
    }

    public static class BB
    {
        [Markers]
        public static class MarkerId
        {
            public const int OpenWindow = 163062905;
            public const int AddBlock = 163060420;
            public const int UpdateBlock = 163064521;
            public const int RunBlock = 163063912;
            public const int InstallSDK = 163067801;
            public const int RemoveSDK = 163067560;
            public const int InstallBlockData = 163065449;
            public const int OpenSceneWithBlock = 163063649;
            public const int DownloadContent = 163067281;
            public const int VariantsWindowFlow = 163065580;
            public const int VariantsWindowOpen = 163068476;
        }

        public static class AnnotationType
        {
            public const string BlockId = "BlockId";
            public const string BlockName = "BlockName";
            public const string InstanceId = "InstanceId";
            public const string Version = "Version";
            public const string ActionTrigger = "action_trigger";
            public const string Error = "error";
            public const string SceneSizeInB = "SceneSizeInB";
            public const string InstallationRoutineId = "InstallationRoutineId";
            public const string InstallationRoutineData = "InstallationRoutineData";
        }
    }

    public static class GuidedSetup
    {
        [Markers]
        public static class MarkerId
        {
            public const int OpenSSAWindow = 163069502;
            public const int CloseSSAWindow = 163064312;
            public const int SetAppIdFromGuidedSetup = 163061548;
            public const int URLOpen = 163066819;
        }

        public static class AnnotationType
        {
            public const string ActionTrigger = "action_trigger";
            public const string HasAppId = "app_id_exist";
            public const string GSTSource = "gst_source";
            public const string URL = "url";
        }
    }

    public static class XRSim
    {
        [Markers]
        public static class MarkerId
        {
            public const int SESInteraction = 163056472;
            public const int ToggleState = 163059165;
            public const int EditorRun = 163063015;
        }

        public static class AnnotationType
        {
            public const string IsActive = "active";
            public const string Action = "action";
            public const string XRSimEnabled = "xrsimenabled";
        }
    }

    public static class Scene
    {
        [Markers]
        public static class MarkerId
        {
            public const int UseOVRSceneManager = 163061745;
            public const int UseDefaultSceneModelLoader = 163059869;
            public const int SceneOpen = 163063049;
            public const int SceneClose = 163068562;
        }

        public static class AnnotationType
        {
            public const string UsingBasicPrefabs = "basic_prefabs";
            public const string UsingPrefabOverrides = "prefab_overrides";
            public const string ActiveRoomsOnly = "active_rooms_only";
            public const string Guid = "Guid";
            public const string BuildTarget = "BuildTarget";
            public const string RuntimePlatform = "RuntimePlatform";
            public const string Features = "Features";
            public const string EnabledSettings = "FeaturesSupportInSettings";
        }
    }
}
