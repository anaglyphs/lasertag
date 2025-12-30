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
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using Meta.XR.Editor.PlayCompanion;
using Meta.XR.Editor.StatusMenu;
using Styles = Meta.XR.Editor.PlayCompanion.Styles;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Simulator.Editor;
using Utils = Meta.XR.Simulator.Editor.Utils;

namespace Meta.XR.Simulator
{
    [InitializeOnLoad]
    internal static class ToolbarItem
    {

        public static Origin ToSimulatorOrigin(this string origin)
        {
            if (!Enum.TryParse(origin, out Origin simulatorOrigin))
            {
                return Origin.Unknown;
            }
            return simulatorOrigin;
        }

        private const string ToolbarItemTooltip =
#if UNITY_2022_2_OR_NEWER
            "Set Play mode to use Meta XR Simulator\n<i>Simulates Meta Quest headset and features on desktop</i>";
#else
            "Set Play mode to use Meta XR Simulator\nSimulates Meta Quest headset and features on desktop";
#endif

        internal static readonly ToolDescriptor ToolDescriptor = new()
        {
            Name = XRSimConstants.PublicName,
            MenuDescription = "Iterate faster in Editor",
            Color = Meta.XR.Editor.UserInterface.Styles.Colors.Meta,
            Icon = Styles.Contents.MetaXRSimulator,
            MqdhCategoryId = XRSimConstants.MqdhCategoryId,
            AddToStatusMenu = true,
            AddToMenu = false,
            PillIcon = () =>
                Utils.XRSimUtils.IsSimulatorActivated()
                    ? (Meta.XR.Editor.UserInterface.Styles.Contents.CheckIcon,
                        Meta.XR.Editor.UserInterface.Styles.Colors.Meta,
                        false)
                    : (null, null, false),
            InfoTextDelegate = () => Utils.XRSimUtils.IsSimulatorActivated() ?
                ("Enabled", Meta.XR.Editor.UserInterface.Styles.Colors.SuccessColor) :
                ("Disabled", Meta.XR.Editor.UserInterface.Styles.Colors.DisabledColor),
            OnClickDelegate = origin => Utils.XRSimUtils.ToggleSimulator(true, origin.ToString().ToSimulatorOrigin()),
            Order = 10,
            CloseOnClick = false
        };

        static ToolbarItem()
        {
            // Only register toolbar items if XR Simulator 2 is installed
            if (XRSimInstallationDetector.IsXRSim2Installed())
            {
                // Check for package conflicts and handle automatic removal if needed
                if (IsOldXRSimPackageInstalled())
                {
                    ResolvePackageConflict();
                }
                RegisterToolbarItems();
            }
            else
            {
                // XRSim2 not installed but XRSim1 is, prompt to download XRSim2
                RegisterDownloadTask();
            }
        }

        /// <summary>
        /// Registers a Project Setup Tool task to prompt users to download XR Simulator 2 if they have XR Simulator 1 installed
        /// </summary>
        private static void RegisterDownloadTask()
        {
            try
            {
                OVRProjectSetup.RegisterTask(
                    group: OVRProjectSetup.TaskGroup.Features,
                    isDone: buildTargetGroup => XRSimInstallationDetector.IsXRSim2Installed(),
                    level: OVRProjectSetup.TaskLevel.Recommended,
                    message: "Newest Meta XR Simulator not installed, consider installing it",
                    fixMessage: "Download Meta XR Simulator",
                    fix: async buildTargetGroup => await Installer.EnsureMetaXRSimulatorInstalled(),
                    url: "https://developer.oculus.com/documentation/unity/unity-xr-simulator/"
                );
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[Meta XR Simulator] Failed to register Project Setup Tool task: {ex.Message}");
            }
        }


        private static bool IsOldXRSimPackageInstalled()
        {
            try
            {
                // Use Unity's PackageManager API directly with timeout
                var listRequest = UnityEditor.PackageManager.Client.List(offlineMode: true, includeIndirectDependencies: false);

                const int timeoutMs = 5000;
                const int checkIntervalMs = 50;

                Stopwatch sw = new Stopwatch();
                sw.Start();

                while (!listRequest.IsCompleted && sw.ElapsedMilliseconds < timeoutMs)
                {
                    System.Threading.Thread.Sleep(checkIntervalMs);
                }

                if (!listRequest.IsCompleted || listRequest.Status != UnityEditor.PackageManager.StatusCode.Success)
                {
                    return false;
                }

                // Check if the old package is in the list
                foreach (var package in listRequest.Result)
                {
                    if (package.name == XRSimConstants.LegacyPackageName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Automatically removes the old package when a conflict is detected and shows a notification
        /// </summary>
        private async static void ResolvePackageConflict()
        {
            var packageManager = new PackageManagerUtils();

            // Automatically remove the old package without asking for user consent
            bool removalSuccess = await RemovePackageAsync(packageManager, XRSimConstants.LegacyPackageName);

            if (removalSuccess)
            {
                // Show notification that we automatically resolved the conflict
                UnityEditor.EditorUtility.DisplayDialog(
                    "Meta XR Simulator Upgraded",
                    $"The previous Meta XR Simulator package ('{XRSimConstants.LegacyPackageName}') has been removed.",
                    "OK"
                );
            }
            else
            {
                // Show manual removal instructions as fallback
                UnityEditor.EditorUtility.DisplayDialog(
                    "Manual Removal Required",
                    $"Failed to automatically remove '{XRSimConstants.LegacyPackageName}'.\n\n" +
                    $"Please manually remove it as it is not needed anymore by using:\n" +
                    $"Window > Package Manager > In Project > Meta XR Simulator > Remove\n\n",
                    "OK"
                );
            }
        }

        /// <summary>
        /// Helper method to remove a package asynchronously with timeout and error handling
        /// </summary>
        private static async Task<bool> RemovePackageAsync(PackageManagerUtils packageManager, string packageName)
        {
            return await packageManager.RemovePackageAsync(packageName);
        }

        /// <summary>
        /// Registers the XR Simulator toolbar items with the toolbar system
        /// </summary>
        private static void RegisterToolbarItems()
        {
            var xrSimulatorItem = new Meta.XR.Editor.PlayCompanion.Item()
            {
                Order = 10,
                Name = XRSimConstants.PublicName,
                Tooltip = ToolbarItemTooltip,
                Icon = Styles.Contents.MetaXRSimulator,
                Color = Meta.XR.Editor.UserInterface.Styles.Colors.Meta,
                Show = true,
                ShouldBeSelected = () => Utils.XRSimUtils.IsSimulatorActivated(),
                ShouldBeUnselected = () => !Utils.XRSimUtils.IsSimulatorActivated(),
                OnSelect = () => { Utils.XRSimUtils.ActivateSimulator(true, Origin.Toolbar); },
                OnUnselect = () =>
                {
                    Utils.XRSimUtils.DeactivateSimulator(true, Origin.Toolbar);
                },
                OnEnteringPlayMode = () =>
                {
                },
                OnExitingPlayMode = () =>
                {
                },
                OnEditorQuitting = () =>
                {
                },
            };

            Manager.RegisterItem(xrSimulatorItem);
        }
    }
}
