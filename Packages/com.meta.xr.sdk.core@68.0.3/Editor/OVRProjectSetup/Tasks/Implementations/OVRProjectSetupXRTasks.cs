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
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

#if USING_XR_MANAGEMENT
using UnityEditor.XR.Management;
#endif

#if USING_XR_SDK_OCULUS
using Unity.XR.Oculus;
#endif

#if USING_XR_SDK_OPENXR
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
#endif

[InitializeOnLoad]
internal static class OVRProjectSetupXRTasks
{
    private const string OculusXRPackageName = "com.unity.xr.oculus";
    private const string XRPluginManagementPackageName = "com.unity.xr.management";
    internal const string UnityXRPackage = "com.unity.xr.openxr";
    private const string UPMTitle = "Unity Package Manager";

    private const OVRProjectSetup.TaskGroup XRTaskGroup = OVRProjectSetup.TaskGroup.Packages;

    static OVRProjectSetupXRTasks()
    {
        OVRProjectSetup.AddTask(
            conditionalValidity: _ => OVRProjectSetupUtils.PackageManagerListAvailable,
            level: OVRProjectSetup.TaskLevel.Required,
            group: XRTaskGroup,
            isDone: _ => OVRProjectSetupUtils.IsPackageInstalled(OculusXRPackageName) || OVRProjectSetupUtils.IsPackageInstalled(UnityXRPackage),
            message: $"Either the Oculus XR ({OculusXRPackageName}) or OpenXR Plugin ({UnityXRPackage}) package must be installed through the {UPMTitle}. Please note that not all Meta XR SDK features are currently supported when using the OpenXR plugin."
        );

        OVRProjectSetup.AddTask(
            conditionalValidity: _ => OVRProjectSetupUtils.PackageManagerListAvailable,
            level: OVRProjectSetup.TaskLevel.Optional,
            group: XRTaskGroup,
            isDone: _ => !OVRProjectSetupUtils.IsPackageInstalled(UnityXRPackage),
            message: $"Some features in the Meta XR SDK are not yet supported when using the OpenXR plugin ({UnityXRPackage}). Switch to the Oculus XR plugin ({OculusXRPackageName}) to enable support for these features."
        );

        OVRProjectSetup.AddTask(
            conditionalValidity: _ => OVRProjectSetupUtils.PackageManagerListAvailable,
            level: OVRProjectSetup.TaskLevel.Required,
            group: XRTaskGroup,
            isDone: _ => OVRProjectSetupUtils.IsPackageInstalled(XRPluginManagementPackageName),
            message: $"The XR Plug-in Management ({XRPluginManagementPackageName}) package must be installed through the {UPMTitle}."
        );

        OVRProjectSetup.AddTask(
            conditionalValidity: _ => OVRProjectSetupUtils.PackageManagerListAvailable,
            level: OVRProjectSetup.TaskLevel.Required,
            group: XRTaskGroup,
            isDone: _ => !(OVRProjectSetupUtils.IsPackageInstalled(OculusXRPackageName) && OVRProjectSetupUtils.IsPackageInstalled(UnityXRPackage)),
            message: $"It's not recommended to install Oculus XR Plugin and OpenXR Plugin at the same time, which may introduce unintentional conflicts.\nClick 'Fix' to uninstall the OpenXR Plugin. If you want to uninstall Oculus XR Plugin, please use the {UPMTitle}.",
            fix: _ => OVRProjectSetupUtils.UninstallPackage(UnityXRPackage),
            fixMessage: $"Remove the {UnityXRPackage} package"
        );

        AddXrPluginManagementTasks();
    }

    private static void AddXrPluginManagementTasks()
    {
#if USING_XR_MANAGEMENT && USING_XR_SDK_OCULUS
        OVRProjectSetup.AddTask(
            conditionalValidity: _ =>
                OVRProjectSetupUtils.IsPackageInstalled(XRPluginManagementPackageName),
            level: OVRProjectSetup.TaskLevel.Required,
            group: XRTaskGroup,
            isDone: buildTargetGroup =>
            {
                var settings = GetXRGeneralSettingsForBuildTarget(buildTargetGroup, false);
                return settings != null && settings.Manager != null && settings.Manager.activeLoaders.Any(loader => loader is OculusLoader);
            },
            message: "Oculus must be added to the XR Plugin active loaders",
            fix: buildTargetGroup =>
            {
                var settings = GetXRGeneralSettingsForBuildTarget(buildTargetGroup, true);
                if (settings == null)
                {
                    throw new OVRConfigurationTaskException("Could not find XR Plugin Manager settings");
                }

                var loadersList = AssetDatabase.FindAssets($"t: {nameof(OculusLoader)}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<OculusLoader>).ToList();
                OculusLoader oculusLoader;
                if (loadersList.Count > 0)
                {
                    oculusLoader = loadersList[0];
                }
                else
                {
                    oculusLoader = ScriptableObject.CreateInstance<OculusLoader>();
                    EnsureIsValidFolder("Assets/XR/Loaders");
                    AssetDatabase.CreateAsset(oculusLoader, "Assets/XR/Loaders/Oculus Loader.asset");
                }

                settings.Manager.TryAddLoader(oculusLoader);
                EditorUtility.SetDirty(settings);
            },
            fixMessage: "Add Oculus to the XR Plugin active loaders"
        );
#endif
#if USING_XR_MANAGEMENT && USING_XR_SDK_OPENXR
        OVRProjectSetup.AddTask(
            conditionalValidity: buildTargetGroup =>
                OVRProjectSetupUtils.IsPackageInstalled(XRPluginManagementPackageName),
            level: OVRProjectSetup.TaskLevel.Required,
            group: XRTaskGroup,
            isDone: buildTargetGroup =>
            {
                var settings = GetXRGeneralSettingsForBuildTarget(buildTargetGroup, false);
                if (settings == null)
                {
                    return false;
                }

                foreach (var loader in settings.Manager.activeLoaders)
                {
                    if (loader as OpenXRLoader != null)
                    {
                        return true;
                    }
                }

                return false;
            },
            message: "OpenXR must be added to the XR Plugin active loaders",
            fix: buildTargetGroup =>
            {
                var settings = GetXRGeneralSettingsForBuildTarget(buildTargetGroup, true);
                if (settings == null)
                {
                    throw new OVRConfigurationTaskException("Could not find XR Plugin Manager settings");
                }

                var loadersList = AssetDatabase.FindAssets($"t: {nameof(OpenXRLoader)}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<OpenXRLoader>).ToList();
                OpenXRLoader openXRLoader;
                if (loadersList.Count > 0)
                {
                    openXRLoader = loadersList[0];
                }
                else
                {
                    openXRLoader = ScriptableObject.CreateInstance<OpenXRLoader>();
                    EnsureIsValidFolder("Assets/XR/Loaders");
                    AssetDatabase.CreateAsset(openXRLoader, "Assets/XR/Loaders/OpenXR Loader.asset");
                }

                settings.Manager.TryAddLoader(openXRLoader);
                EditorUtility.SetDirty(settings);
            },
            fixMessage: "Add OpenXR to the XR Plugin active loaders"
        );

        OVRProjectSetup.AddTask(
            conditionalValidity: BuildTargetGroup => OVRProjectSetupUtils.IsPackageInstalled(XRPluginManagementPackageName),
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: XRTaskGroup,
            isDone: buildTargetGroup =>
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                if (settings == null)
                {
                    return false;
                }

                bool touchFeatureEnabled = false;
                foreach(var feature in settings.GetFeatures<OpenXRInteractionFeature>())
                {
                    if (feature.enabled)
                    {
                        if (feature is OculusTouchControllerProfile)
                        {
                            touchFeatureEnabled = true;
                        }
                    }
                }
                return touchFeatureEnabled;
            },
            message: "When using OpenXR Plugin, Oculus Touch Interaction Profile should be included for full OVRInput support.",
            fix: buildTargetGroup =>
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
                if (settings == null)
                {
                    throw new OVRConfigurationTaskException("Could not find OpenXR Plugin settings");
                }

                var touchFeature = settings.GetFeature<OculusTouchControllerProfile>();
                if (touchFeature == null)
                {
                    throw new OVRConfigurationTaskException("Could not find Oculus Touch Interaction Profile in OpenXR settings");
                }
                touchFeature.enabled = true;
            }
        );
#endif
    }

#if USING_XR_MANAGEMENT && (USING_XR_SDK_OCULUS || USING_XR_SDK_OPENXR)
    private static void EnsureIsValidFolder(string path)
    {
        var folders = path.Split('/');
        string fullPath = null;
        foreach (var folder in folders)
        {
            var newPath = string.IsNullOrEmpty(fullPath) ? folder : fullPath + "/" + folder;
            if (!AssetDatabase.IsValidFolder(newPath))
                AssetDatabase.CreateFolder(fullPath, folder);
            fullPath = newPath;
        }
    }

    private static UnityEngine.XR.Management.XRGeneralSettings GetXRGeneralSettingsForBuildTarget(
        BuildTargetGroup buildTargetGroup, bool create)
    {
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(buildTargetGroup);
        if (!create || settings != null)
        {
            return settings;
        }

        // we have to create these settings ourselves as
        // long as Unity doesn't expose the internal function
        // XRGeneralSettingsPerBuildTarget.GetOrCreate()
        var settingsKey = UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey;
        EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(
            settingsKey, out var settingsPerBuildTarget);

        if (settingsPerBuildTarget == null)
        {
            settingsPerBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            EnsureIsValidFolder("Assets/XR");
            const string assetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            AssetDatabase.CreateAsset(settingsPerBuildTarget, assetPath);
            AssetDatabase.SaveAssets();

            EditorBuildSettings.AddConfigObject(settingsKey, settingsPerBuildTarget, true);
        }

        if (!settingsPerBuildTarget.HasManagerSettingsForBuildTarget(buildTargetGroup))
        {
            settingsPerBuildTarget.CreateDefaultManagerSettingsForBuildTarget(buildTargetGroup);
        }

        return XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(buildTargetGroup);
    }
#endif
}
