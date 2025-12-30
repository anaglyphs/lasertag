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
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine.Networking;

namespace Meta.XR.Simulator.Editor
{
    internal static class Installer
    {
        private const string Name = "Meta XR Simulator Installer";
        public static event Action OnInstalled;
        private static UnityWebRequest _webRequest = null;

        public static async Task<bool> EnsureMetaXRSimulatorInstalled()
        {

            if (XRSimInstallationDetector.IsXRSim2Installed())
            {
                return true;
            }

            return await DownloadXRSimulator(XRSimConstants.DownloadURL);
        }

        internal static async Task<bool> DownloadXRSimulator(string downloadUrl)
        {
            var marker = new OVRTelemetryMarker(XRSimTelemetryConstants.MarkerId.BinariesInstalled);
            marker.AddAnnotation(XRSimTelemetryConstants.AnnotationType.XRSimVersion, "2");

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            return await DownloadXRSimInstaller(downloadUrl, () =>
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                OnInstalled?.Invoke();
                marker.SetResult(OVRPlugin.Qpl.ResultType.Success);
            }, errorMessage =>
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                Utils.LogUtils.DisplayDialogOrError(Name, errorMessage, true);
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
            });
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // Cancel entering play mode
                UnityEngine.Debug.LogError($"Synthetic Environments installation is in progress. Please wait until is finished.");
                EditorApplication.isPlaying = false;
            }
        }

        private static async Task<bool> DownloadXRSimInstaller(string downloadUrl, Action onSuccess, Action<string> onError)
        {
#if UNITY_EDITOR_WIN
            var downloadedInstallerPath =
                            Path.Combine(XRSimConstants.DownloadFolderPath, $"meta_xr_simulator.msi");
#elif UNITY_EDITOR_OSX
            var downloadedInstallerPath =
                            Path.Combine(XRSimConstants.DownloadFolderPath, $"meta_xr_simulator.dmg");
#endif
            if (!await DownloadInstaller(downloadedInstallerPath, downloadUrl, errorMessage =>
                {
                    onError?.Invoke(errorMessage);
                }))
            {
                return false;
            }

            // Open the folder containing the downloaded file so user can install it
            try
            {
#if UNITY_EDITOR_WIN
                // Windows: Open Explorer and select the downloaded file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{downloadedInstallerPath}\"");
#elif UNITY_EDITOR_OSX
                // Mac: Open Finder and reveal the downloaded file
                System.Diagnostics.Process.Start("open", $"-R \"{downloadedInstallerPath}\"");
#endif
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Meta XR Simulator 2] Failed to open download folder: {ex.Message}");
            }

            onSuccess?.Invoke();
            return true;
        }

        internal static async Task<bool> DownloadInstaller(string downloadPath, string url, Action<string> onError)
        {
            int progressId = Utils.LogUtils.CreateProgress(Name, false);

            _webRequest = UnityWebRequest.Get(url);
            var handler = new DownloadHandlerFile(downloadPath);
            _webRequest.downloadHandler = handler;
            handler.removeFileOnAbort = true;

            UnityWebRequestAsyncOperation operation = _webRequest.SendWebRequest();
            operation.completed += _ =>
            {
                Utils.LogUtils.ReportInfo(Name, "finished downloading " + url);
            };

            while (!_webRequest.downloadHandler.isDone && !operation.isDone)
            {
                Progress.Report(progressId, _webRequest.downloadProgress, "Downloading package");
                await Task.Yield();
            }

            if (_webRequest.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(_webRequest.error);
                Progress.Finish(progressId, Progress.Status.Failed);
                return false;
            }

            Utils.LogUtils.ReportInfo(Name, "Finished saving data to " + downloadPath);
            _webRequest = null;
            Progress.Remove(progressId);

#if UNITY_EDITOR_OSX
            // Remove quarantine attribute from downloaded file
            {
                const string Attribute = "com.apple.provenance";
                var args = Utils.EscapeArguments(new string[] { "-d", Attribute, downloadPath });
                var (retCode, contents) = Utils.ProcessUtils.ExecuteProcess("xattr", args);
                if (retCode != 0)
                {
                    Utils.LogUtils.ReportError(Name, string.Format("failed to remove {0}, retCode={1}, contents={2}", Attribute, retCode, contents));
                }
            }
#endif
            return true;
        }
    }
}
