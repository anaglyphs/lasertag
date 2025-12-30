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
using UnityEngine;

namespace Meta.XR.Simulator.Editor
{
    /// <summary>
    /// Main class for Meta XR Simulator 2.0 installation detection
    /// This addresses the requirement: "Functionality to check if XRSimulator is installed - does not exist now"
    /// </summary>
    internal static class XRSimInstallationDetector
    {
        private const string Name = "Meta XR Simulator";
        private static IXRSimInstallationDetector _detector;

        /// <summary>
        /// Checks if Meta XR Simulator 2.0 is installed on the system
        /// This is the main functionality that was missing and needed to be implemented
        /// </summary>
        /// <returns>True if Meta XR Simulator 2.0 is installed, false otherwise</returns>
        public static bool IsXRSim2Installed()
        {
            try
            {
                var detector = GetDetector();
                return detector.IsInstalled();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{Name}] Failed to check installation status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the installation directory of Meta XR Simulator 2.0 on the current platform
        /// Uses the xrsim:// protocol handler registration on Windows to locate the installation
        /// </summary>
        /// <returns>The installation directory path, or null if not found</returns>
        public static string GetXRSimInstallationDirectory()
        {
            try
            {
                var detector = GetDetector();
                return detector.GetOpenXRRuntimeDirectory();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{Name}] Failed to get installation directory: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the appropriate installation detector for the current platform
        /// </summary>
        /// <returns>Platform-specific installation detector</returns>
        private static IXRSimInstallationDetector GetDetector()
        {
            // Use cached instance if available
            if (_detector != null)
            {
                return _detector;
            }

            // Create platform-specific detector
#if UNITY_EDITOR_WIN
            _detector = new WindowsXRSimInstallationDetector();
#elif UNITY_EDITOR_OSX
            _detector = new MacOSXRSimInstallationDetector();
#else
            _detector = new UnsupportedPlatformDetector();
#endif

            return _detector;
        }

        /// <summary>
        /// For testing purposes - allows injection of a custom detector
        /// </summary>
        internal static void SetDetectorForTesting(IXRSimInstallationDetector detector)
        {
            _detector = detector;
        }

        /// <summary>
        /// For testing purposes - clears the cached detector
        /// </summary>
        internal static void ClearDetectorCache()
        {
            _detector = null;
        }

        /// <summary>
        /// Fallback detector for unsupported platforms
        /// </summary>
        private class UnsupportedPlatformDetector : IXRSimInstallationDetector
        {
            public bool IsInstalled()
            {
                return false;
            }

            public string GetOpenXRRuntimeDirectory()
            {
                return null;
            }
        }
    }
}
