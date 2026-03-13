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

using System.IO;
using UnityEngine;

#if UNITY_EDITOR_OSX
using System.Globalization;
#endif

namespace Meta.XR.Simulator.Editor
{
    internal class XRSimUtils
    {
        private static string ConfigPath => Path.GetFullPath(Path.Join(XRSimConstants.AppDataFolderPath, "config", Application.isBatchMode
                        ? "sim_core_configuration_ci.json"
                        : "sim_core_configuration.json"));

        public virtual bool IsSimulatorActivated()
        {
            return Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey) == XRSimConstants.MetaOpenXrSimulationJsonPath;
        }

        public virtual void ActivateSimulator(bool forceHideDialog, Origin origin)
        {
            ToolbarItem.ToolDescriptor.Usage.RecordUsage();

            using var marker = new OVRTelemetryMarker(XRSimTelemetryConstants.MarkerId.ToggleState);
            marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.IsActive, true.ToString());
            marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.Origin, origin.ToString());

#if UNITY_EDITOR_OSX
            if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(SystemInfo.processorType, "Intel", CompareOptions.IgnoreCase) >= 0)
            {
                Utils.LogUtils.DisplayDialogOrError("Meta XR Simulator Not Supported",
                                "Apple Silicon Mac is required. Intel-based Mac is not currently supported.",
                                forceHideDialog);
                marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.ErrorMessage, "Mac intel is not supported");
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                return;
            }
#endif

            bool isInstalled = XRSimInstallationDetector.IsXRSim2Installed();

            if (!isInstalled)
            {
                Utils.LogUtils.DisplayDialogOrError("Meta XR Simulator Not Installed",
                                "Installation failed, please report the issue on the bug tracker.",
                                forceHideDialog);
                Utils.LogUtils.DisplayDialogOrError("Meta XR Simulator Not Installed",
                                "Try using different version. Open Edit > Preferences... > Meta XR > Meta XR Simulator and choose different selected version. ",
                                forceHideDialog);
                marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.ErrorMessage, "Meta XR Simulator Not Installed");
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                return;
            }

#if UNITY_EDITOR_OSX
            var openXRLoaderInstalled = Utils.PackageManagerUtils.IsPackageInstalledWithValidVersion(XRSimConstants.UnityXRPackage);
            var openXRLoaderErrorMessage = $"OpenXR Plugin ({XRSimConstants.UnityXRPackage}) package must be installed through the Unity Package Manager in order for your application to be run with XRSimulator.";
#else
            var openXRLoaderInstalled = Utils.PackageManagerUtils.IsPackageInstalled(XRSimConstants.OculusXRPackageName) || Utils.PackageManagerUtils.IsPackageInstalled(XRSimConstants.UnityXRPackage);
            var openXRLoaderErrorMessage =
                            $"Either the Oculus XR ({XRSimConstants.OculusXRPackageName}) or OpenXR Plugin ({XRSimConstants.UnityXRPackage}) package must be installed through the Unity Package Manager in order for your application to be run with XRSimulator.";
#endif
            if (!openXRLoaderInstalled)
            {
                Utils.LogUtils.DisplayDialogOrError("XRSimulator requires an Open XR Loader to function",
                    openXRLoaderErrorMessage,
                    forceHideDialog);
                marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.ErrorMessage, "OpenXR Loader not installed");
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                return;
            }

            if (IsSimulatorActivated())
            {
                Utils.LogUtils.ReportInfo("Meta XR Simulator", "Meta XR Simulator is already activated.");
                return;
            }

            // update XR_RUNTIME_JSON
            {
                var runtimeEnv = Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrRuntimeEnvKey);
                if (runtimeEnv == XRSimConstants.MetaOpenXrSimulationJsonPath)
                {
                    // Set the PreviouseOpenXrRuntimeEnvKey to empty string to avoid unable to deactivate the simulator
                    runtimeEnv = "";
                }
                Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.PreviousOpenXrRuntimeEnvKey,
                    runtimeEnv);
                Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.OpenXrRuntimeEnvKey, XRSimConstants.MetaOpenXrSimulationJsonPath);
            }

            // update XR_SELECTED_RUNTIME_JSON
            {
                var selectedRuntimeEnv = Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey);
                if (selectedRuntimeEnv == XRSimConstants.MetaOpenXrSimulationJsonPath)
                {
                    // Set the PreviousOpenXrSelectedRuntimeEnvKey to empty string to avoid unable to deactivate the simulator
                    selectedRuntimeEnv = "";
                }
                // ReportInfo("Meta XR Simulator", "changing Env from " + runtimeEnv + " to " + XRSimConstants.JsonPath);
                Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.PreviousOpenXrSelectedRuntimeEnvKey,
                    selectedRuntimeEnv);

                Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey, XRSimConstants.MetaOpenXrSimulationJsonPath);
            }

            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.PreviousXrSimConfigEnvKey,
                Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.XrSimConfigEnvKey));
            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.XrSimConfigEnvKey, ConfigPath);

            var runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
            if (runtimeSettings != null)
            {
                Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.ProjectTelemetryId, runtimeSettings.TelemetryProjectGuid);
            }

            Utils.LogUtils.ReportInfo("Meta XR Simulator is activated",
                $"{XRSimConstants.OpenXrSelectedRuntimeEnvKey} is set to {Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey)}\n{XRSimConstants.XrSimConfigEnvKey} is set to {Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.XrSimConfigEnvKey)}");
        }

        public virtual void DeactivateSimulator(bool forceHideDialog, Origin origin)
        {
            using var marker = new OVRTelemetryMarker(XRSimTelemetryConstants.MarkerId.ToggleState);
            marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.IsActive, false.ToString());
            marker.AddAnnotation(OVRTelemetryConstants.Editor.AnnotationType.Origin, origin.ToString());

            if (!XRSimInstallationDetector.IsXRSim2Installed())
            {
                Utils.LogUtils.DisplayDialogOrError("Meta XR Simulator",
                    $"{XRSimConstants.MetaOpenXrSimulationJsonPath} was not found, make sure you have Meta XR Simulator installed.",
                    forceHideDialog);
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                return;
            }

            if (!IsSimulatorActivated())
            {
                Utils.LogUtils.ReportInfo("Meta XR Simulator", "Meta XR Simulator is not activated.");
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                return;
            }

            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.OpenXrRuntimeEnvKey,
                Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.PreviousOpenXrRuntimeEnvKey));
            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.PreviousOpenXrRuntimeEnvKey, "");

            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey,
                Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.PreviousOpenXrSelectedRuntimeEnvKey));
            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.PreviousOpenXrSelectedRuntimeEnvKey, "");

            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.XrSimConfigEnvKey,
                Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.PreviousXrSimConfigEnvKey));
            Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.PreviousXrSimConfigEnvKey, "");

            Utils.LogUtils.ReportInfo("Meta XR Simulator is deactivated",
                $"{XRSimConstants.OpenXrSelectedRuntimeEnvKey} is set to {Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey)}\n{XRSimConstants.XrSimConfigEnvKey} is set to {Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.XrSimConfigEnvKey)}");
        }

        public virtual void VerifyAndCorrectActivation()
        {
            if (XRSimInstallationDetector.IsXRSim2Installed() && IsSimulatorActivated())
            {
                // Ensure XR_RUNTIME_JSON matches XR_SELECTED_RUNTIME_JSON
                var runtimeEnv = Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrRuntimeEnvKey);
                var selectedRuntimeEnv = Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey);

                if (runtimeEnv != selectedRuntimeEnv)
                {
                    Utils.LogUtils.ReportInfo("Meta XR Simulator", $"{XRSimConstants.OpenXrRuntimeEnvKey} was modified. Reset it to {Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey)}");
                    Utils.SystemUtils.SetEnvironmentVariable(XRSimConstants.OpenXrRuntimeEnvKey, Utils.SystemUtils.GetEnvironmentVariable(XRSimConstants.OpenXrSelectedRuntimeEnvKey));
                }
            }
        }

        public virtual void ToggleSimulator(bool forceHideDialog, Origin origin)
        {
            if (IsSimulatorActivated())
            {
                DeactivateSimulator(forceHideDialog, origin);
            }
            else
            {
                ActivateSimulator(forceHideDialog, origin);
            }
        }
    }
}
