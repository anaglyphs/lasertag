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

using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor.PackageManager;

namespace Meta.XR.Simulator.Editor
{
    internal class PackageManagerUtils
    {
        public virtual bool IsPackageInstalled(string packageName)
        {
            return PackageList.IsPackageInstalled(packageName);
        }

        public virtual bool IsPackageInstalledWithValidVersion(string packageId)
        {
            return PackageList.IsPackageInstalledWithValidVersion(packageId);
        }

        /// <summary>
        /// Asynchronously removes a package using Unity's Package Manager
        /// </summary>
        /// <param name="packageName">The name of the package to remove</param>
        /// <returns>Task that resolves to true if removal was successful, false otherwise</returns>
        public virtual async Task<bool> RemovePackageAsync(string packageName)
        {
            try
            {
                var removeRequest = Client.Remove(packageName);

                // Wait for completion with timeout
                const int timeoutMs = 30000; // 30 seconds
                const int checkIntervalMs = 100;

                Stopwatch sw = new Stopwatch();
                sw.Start();

                while (!removeRequest.IsCompleted && sw.ElapsedMilliseconds <= timeoutMs)
                {
                    await Task.Delay(checkIntervalMs);
                }

                if (!removeRequest.IsCompleted)
                {
                    UnityEngine.Debug.LogError($"[Meta XR Simulator] Package removal timed out after {timeoutMs}ms");
                    return false;
                }

                if (removeRequest.Status == StatusCode.Success)
                {
                    UnityEngine.Debug.Log($"[Meta XR Simulator] Successfully removed package: {packageName}");
                    return true;
                }
                else
                {
                    UnityEngine.Debug.LogError($"[Meta XR Simulator] Failed to remove package '{packageName}': {removeRequest.Error?.message}");
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[Meta XR Simulator] Exception during package removal: {ex.Message}");
                return false;
            }
        }
    }
}
