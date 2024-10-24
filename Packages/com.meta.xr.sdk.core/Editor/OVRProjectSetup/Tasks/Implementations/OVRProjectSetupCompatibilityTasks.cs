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
using UnityEditor;

[InitializeOnLoad]
internal static class OVRProjectSetupCompatibilityTasks
{
    public static bool IsTargetingARM64 =>
        (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0;

    public static readonly Action<BuildTargetGroup> SetARM64Target = (_) =>
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

    static OVRProjectSetupCompatibilityTasks()
    {
        const OVRProjectSetup.TaskGroup compatibilityTaskGroup = OVRProjectSetup.TaskGroup.Compatibility;

        // [Required] Platform has to be supported
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            group: compatibilityTaskGroup,
            isDone: OVRProjectSetup.IsPlatformSupported,
            conditionalMessage: buildTargetGroup =>
                OVRProjectSetup.IsPlatformSupported(buildTargetGroup)
                    ? $"Build Target ({buildTargetGroup}) is supported"
                    : $"Build Target ({buildTargetGroup}) is not supported"
        );

        // [Required] Android minimum level API
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: _ => PlayerSettings.Android.minSdkVersion >= AndroidSdkVersions.AndroidApiLevel29,
            message: "Minimum Android API Level must be at least 29",
            fix: _ => PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29,
            fixMessage: "PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29"
        );

#if UNITY_2023_2_OR_NEWER
        // Force using GameActivity on Unity 2023.2+ (reference: T169740072)
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: buildTargetGroup =>
                PlayerSettings.Android.applicationEntry == AndroidApplicationEntry.GameActivity,
            message: "Always specify single \"GameActivity\" application entry on Unity 2023.2+",
            fix: buildTargetGroup =>
                PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity,
            fixMessage: "PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity"
        );
#endif

        // [Recommended] Android target level API
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: _ => PlayerSettings.Android.targetSdkVersion == TargetAPILevel,
            message: $"Target API should be set to {TargetAPILevelName} as to ensure latest version",
            fix: _ => PlayerSettings.Android.targetSdkVersion = TargetAPILevel,
            fixMessage: $"PlayerSettings.Android.targetSdkVersion = {TargetAPILevelName}"
        );

        // [Required] Install Location
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: _ =>
                PlayerSettings.Android.preferredInstallLocation == AndroidPreferredInstallLocation.Auto,
            message: "Install Location should be set to \"Automatic\"",
            fix: _ =>
                PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto,
            fixMessage: "PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto"
        );

        // [Required] Generate Android Manifest
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Optional,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: _ => OVRManifestPreprocessor.DoesAndroidManifestExist(),
            message: "An Android Manifest file is required",
            fix: _ => OVRManifestPreprocessor.GenerateManifestForSubmission(),
            fixMessage: "Generates a default Manifest file"
        );

        // ConfigurationTask : IL2CPP when ARM64
        OVRProjectSetup.AddTask(
            conditionalLevel: _ =>
                IsTargetingARM64 ? OVRProjectSetup.TaskLevel.Required : OVRProjectSetup.TaskLevel.Recommended,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: buildTargetGroup =>
                PlayerSettings.GetScriptingBackend(buildTargetGroup) == ScriptingImplementation.IL2CPP,
            conditionalMessage: _ =>
                IsTargetingARM64
                    ? "Building the ARM64 architecture requires using IL2CPP as the scripting backend"
                    : "Using IL2CPP as the scripting backend is recommended",
            fix: buildTargetGroup =>
                PlayerSettings.SetScriptingBackend(buildTargetGroup, ScriptingImplementation.IL2CPP),
            fixMessage: "PlayerSettings.SetScriptingBackend(buildTargetGroup, ScriptingImplementation.IL2CPP)"
        );

        // ConfigurationTask : ARM64 is recommended
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            group: compatibilityTaskGroup,
            platform: BuildTargetGroup.Android,
            isDone: _ => IsTargetingARM64,
            message: "Use ARM64 as target architecture",
            fix: SetARM64Target,
            fixMessage: "PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64"
        );

        // ConfigurationTask : No Alpha or Beta for production
        // This is a task that CANNOT BE FIXED
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: compatibilityTaskGroup,
            isDone: _ => !OVRManager.IsUnityAlphaOrBetaVersion(),
            message: $"We recommend using a stable version for {OVREditorUtils.MetaXRPublicName} Development"
        );

        // ConfigurationTask : Check that Android TV Compatibility is disabled
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            platform: BuildTargetGroup.Android,
            group: compatibilityTaskGroup,
            isDone: _ => !PlayerSettings.Android.androidTVCompatibility,
            message: "Apps with Android TV Compatibility enabled are not accepted by the Oculus Store",
            fix: _ => PlayerSettings.Android.androidTVCompatibility = false,
            fixMessage: "PlayerSettings.Android.androidTVCompatibility = false"
        );

        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: compatibilityTaskGroup,
            isDone: _ =>
            {
                if (TryGetXRSimPackageVersion(out var xrSimVersion))
                {
                    return xrSimVersion == SDKVersion || xrSimVersion == SDKVersion - 1;
                }

                return true;
            },
            conditionalValidity: _ =>
            {
#if UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            },
            conditionalMessage: _ => TryGetXRSimPackageVersion(out var xrSimVersion)
                ? $"The Oculus Integration SDK (v{SDKVersion}) and Meta XR Simulator package (v{xrSimVersion}) versions must match to ensure correct functionality"
                : "The Oculus Integration SDK and Meta XR Simulator package versions must match");
    }

    private static bool TryGetXRSimPackageVersion(out int version)
    {
        version = default;
        var package = OVRProjectSetupUtils.GetPackage("com.meta.xr.simulator");
        if (package == null)
        {
            return false;
        }

        var versionParts = package.version.Split('.');
        return versionParts.Length > 0 && int.TryParse(versionParts[0], out version);
    }

    private static int SDKVersion => OVRPlugin.version.Minor - 32;

    private static AndroidSdkVersions TargetAPILevel =>
        Enum.TryParse("AndroidApiLevel32", out AndroidSdkVersions androidSdkVersion) ? androidSdkVersion : AndroidSdkVersions.AndroidApiLevelAuto;

    private static string TargetAPILevelName =>
        TargetAPILevel == AndroidSdkVersions.AndroidApiLevelAuto ? "Auto" : $"{(int)TargetAPILevel}";
}
