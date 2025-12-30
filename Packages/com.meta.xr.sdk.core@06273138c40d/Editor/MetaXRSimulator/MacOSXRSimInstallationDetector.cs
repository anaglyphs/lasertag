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

#if UNITY_EDITOR_OSX

using System.IO;

namespace Meta.XR.Simulator.Editor
{
    /// <summary>
    /// macOS-specific implementation for detecting Meta XR Simulator installation
    /// </summary>
    internal class MacOSXRSimInstallationDetector : IXRSimInstallationDetector
    {
        private const string ApplicationsPath = "/Applications";
        private const string MetaXRSimulatorAppName = "MetaXRSimulator.app";

        private static readonly string MetaXRSimulatorPath = Path.Combine(ApplicationsPath, MetaXRSimulatorAppName);

        public bool IsInstalled()
        {
            return Directory.Exists(MetaXRSimulatorPath);
        }

        /// <summary>
        /// Gets the installation directory of Meta XR Simulator on macOS
        /// </summary>
        /// <returns>The installation directory path, or null if not found</returns>
        public string GetOpenXRRuntimeDirectory()
        {
            if (Directory.Exists(MetaXRSimulatorPath))
            {
                // We need to get in Contents/Resources/MetaXRSimulator as the calling methods are expecting to find
                // meta_openxr_simulator.json in the root directory after calling this method
                var appContents = Path.Join(MetaXRSimulatorPath, "Contents");
                return Path.Join(appContents, "Resources", "MetaXRSimulator");
            }

            return null;
        }
    }
}

#endif
