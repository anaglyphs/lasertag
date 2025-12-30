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
using System.Threading;
using System.Threading.Tasks;
using Meta.XR.Editor.Callbacks;
using Meta.XR.Editor.Settings;
using UnityEditor;
using UnityEditor.MPE;
using UnityEngine;

namespace Meta.XR.Editor.RemoteContent
{
    [InitializeOnLoad]
    internal static class FeatureRampUpManager
    {
        private static readonly CustomBool InitSession =
            new SessionBool
            {
                Owner = null,
                Uid = "FeatureRampUpInitOnce",
                SendTelemetry = false,
                Default = true,
            };

        private static readonly SessionStateBoolDictionary RampKeysCache = new("FeatureRampUp_");

        private const double CacheDurationInHours = 6;
        private const string DownloadUrl = "https://www.facebook.com/devtools_feature_ramp_up";
        private const string CacheFileName = "feature_ramp_up_keys.json";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10.0f);

        private static RemoteJsonContentDownloader _downloader;

        static FeatureRampUpManager()
        {
            InitializeOnLoad.Register(Initialize);
        }

        private static bool IsMainEditorProcess()
        {
#if UNITY_2021_1_OR_NEWER
            return (uint)ProcessService.level != (uint)ProcessLevel.Secondary;
#else
            return (uint)UnityEditor.MPE.ProcessService.level != (uint)UnityEditor.MPE.ProcessLevel.Slave;
#endif
        }

        private static void Initialize()
        {
            if (!IsMainEditorProcess())
            {
                return;
            }

            if (!InitSession.Value)
            {
                return;
            }

            _downloader = new RemoteJsonContentDownloader(CacheFileName, DownloadUrl)
                .WithCacheDuration(TimeSpan.FromHours(CacheDurationInHours))
                .WithMachineIdUrlParameter(required: true)
                .WithSDKVersionUrlParameter();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            LoadAndSendEvent();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private static async Task LoadAndSendEvent()
        {
            var successfulLoad = await Reload(false);
            if (!successfulLoad)
            {
                await Reload(true);
            }

            SendEvent();
        }

        private static void SendEvent()
        {
            var keysString = RampKeysCache.ToFormattedString();
            OVRTelemetry.Start(OVRTelemetryConstants.Editor.MarkerId.StartRampKeys)
                .AddAnnotation(OVRTelemetryConstants.OVRManager.AnnotationTypes.FeatureRampUpValues, keysString)
                .Send();
        }

        private static async Task<bool> Reload(bool forceRedownload)
        {
            if (forceRedownload)
            {
                _downloader.ClearCache();
            }

            var result = await _downloader.Fetch();
            return result.IsSuccess && LoadContentJsonData(result.Content);
        }


        public static Task<bool> GetFeatureKeysResultAsync(string key, bool defaultValue = false)
        {
            return GetRemoteKeysResultAsync(key, defaultValue);
        }

        public static bool GetRemoteKeysResult(string key, bool defaultValue = false)
        {
            return RampKeysCache.Get(key, defaultValue);
        }

        private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, int sleep = 50)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                while (!predicate())
                {
                    await Task.Delay(sleep, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public static async Task WaitForKeysFetchingAsync()
        {
            await WaitUntil(() => InitSession.Get() == false, Timeout);
        }

        public static async Task<bool> GetRemoteKeysResultAsync(string key, bool defaultValue = false)
        {
            await WaitForKeysFetchingAsync();
            return GetRemoteKeysResult(key);
        }

        public static IEnumerable<string> GetKeys()
        {
            return RampKeysCache.Keys;
        }

        [Serializable]
        internal struct FeatureRampUpResult
        {
            public List<SingleKeyResult> bool_result;
        }

        [Serializable]
        internal struct SingleKeyResult
        {
            public string key;
            public bool value;
        }

        private static bool LoadContentJsonData(string jsonData)
        {
            var response = ParseJsonData(jsonData);
            if (response.bool_result == null)
            {
                return false;
            }

            // Clear existing cache and load new data
            RampKeysCache.Clear();

            foreach (var singleKeyResult in response.bool_result)
            {
                RampKeysCache.Add(singleKeyResult.key, singleKeyResult.value);
            }

            InitSession.Set(false);

            return true;

        }

        private static FeatureRampUpResult ParseJsonData(string jsonData)
        {
            FeatureRampUpResult response;
            try
            {
                response = JsonUtility.FromJson<FeatureRampUpResult>(jsonData);
            }
            catch (Exception)
            {
                response = default;
            }

            return response;
        }
    }
}
