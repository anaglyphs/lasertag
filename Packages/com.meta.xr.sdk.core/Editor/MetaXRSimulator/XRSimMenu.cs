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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.Simulator.Editor
{
    /// <summary>
    /// Unity menu items for Meta XR Simulator 2.0 functionality
    /// </summary>
    internal static class XRSimMenu
    {
        private const string ActivateMenuPath = XRSimConstants.MenuPath + "/Activate";
        private const string DeactivateMenuPath = XRSimConstants.MenuPath + "/Deactivate";
        private const string StatusMenuPath = XRSimConstants.MenuPath + "/Status";

        [MenuItem(ActivateMenuPath, false, 1)]
        public static void ActivateSimulator()
        {
#pragma warning disable CS4014
            Utils.XRSimUtils.ActivateSimulator(false, Origin.Menu);
#pragma warning restore CS4014
        }

        [MenuItem(DeactivateMenuPath, false, 2)]
        public static void DeactivateSimulator()
        {
            Utils.XRSimUtils.DeactivateSimulator(false, Origin.Menu);
        }

        [MenuItem(StatusMenuPath, false, 4)]
        public static void ShowStatus()
        {
            bool isActive = Utils.XRSimUtils.IsSimulatorActivated();
            bool isInstalled = XRSimInstallationDetector.IsXRSim2Installed();

            string status = $"{XRSimConstants.PublicName} Status:\n" +
                           $"• Installed: {(isInstalled ? "Yes" : "No")}\n" +
                           $"• Active: {(isActive ? "Yes" : "No")}\n" +
                           $"• Runtime Path: {XRSimConstants.MetaOpenXrSimulationJsonPath}";

            Debug.Log(status);
            EditorUtility.DisplayDialog($"{XRSimConstants.PublicName} Status", status, "OK");
        }

        // Validation methods to enable/disable menu items based on installation and current state
        [MenuItem(ActivateMenuPath, true)]
        public static bool ValidateActivateSimulator()
        {
            return XRSimInstallationDetector.IsXRSim2Installed() && !Utils.XRSimUtils.IsSimulatorActivated();
        }

        [MenuItem(DeactivateMenuPath, true)]
        public static bool ValidateDeactivateSimulator()
        {
            return XRSimInstallationDetector.IsXRSim2Installed() && Utils.XRSimUtils.IsSimulatorActivated();
        }

    }
}
