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

#if UNITY_EDITOR_WIN

using System;
using System.IO;
using Microsoft.Win32;

namespace Meta.XR.Simulator.Editor
{
    /// <summary>
    /// Windows-specific implementation for detecting Meta XR Simulator installation
    /// </summary>
    internal class WindowsXRSimInstallationDetector : IXRSimInstallationDetector
    {
        private const string XRSimProtocolKey = @"SOFTWARE\Classes\xrsim\shell\open\command";

        public bool IsInstalled()
        {
            return !string.IsNullOrEmpty(GetOpenXRRuntimeDirectory());
        }

        /// <summary>
        /// Gets the installation directory of Meta XR Simulator by looking up the xrsim:// protocol handler
        /// </summary>
        /// <returns>The installation directory path, or null if not found</returns>
        public string GetOpenXRRuntimeDirectory()
        {
            try
            {
                // Look up the xrsim:// protocol handler in registry
                // Check CurrentUser first since that's where it's typically installed
                using (var key = Registry.CurrentUser.OpenSubKey(XRSimProtocolKey) ??
                                Registry.LocalMachine.OpenSubKey(XRSimProtocolKey))
                {
                    if (key != null)
                    {
                        string command = key.GetValue("") as string;
                        if (!string.IsNullOrEmpty(command))
                        {
                            // Extract path from command like: C:\Program Files\MetaXRSimulator\v1\MetaXRSimulator.exe %1
                            string executablePath;
                            if (command.StartsWith("\""))
                            {
                                // Quoted path: "C:\Path\To\MetaXRSimulator.exe" %1
                                executablePath = command.Substring(1, command.IndexOf("\"", 1) - 1);
                            }
                            else
                            {
                                // Unquoted path: C:\Program Files\MetaXRSimulator\v1\MetaXRSimulator.exe %1
                                // Remove %1 parameter and trim
                                executablePath = command.Replace(" %1", "").Trim();
                            }

                            if (File.Exists(executablePath))
                            {
                                return Path.GetDirectoryName(executablePath);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Registry access failed
            }

            return null;
        }
    }
}

#endif
