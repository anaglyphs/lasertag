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
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using UnityEditor;
using UnityEngine;

namespace Oculus.VR.Editor.OVRProjectSetup
{
    [InitializeOnLoad]
    internal static class UPSTContentManager
    {
        private const double CacheDurationInHours = 6;
        private const string DownloadUrl = "https://www.facebook.com/upst_configuration";

        private static readonly RemoteJsonContentDownloader Downloader;
        private static readonly HashSet<string> DisabledRuleIds = new();

        static UPSTContentManager()
        {
            Downloader = new RemoteJsonContentDownloader("upst_configuration.json", DownloadUrl)
                .WithCacheDuration(TimeSpan.FromHours(CacheDurationInHours))
                .WithMachineIdUrlParameter()
                .WithSDKVersionUrlParameter();

#pragma warning disable CS4014
            InitializeAsync();
#pragma warning restore CS4014
        }

        public static async Task InitializeAsync()
        {
            var successfulLoad = await Reload(false);
            if (!successfulLoad)
            {
                await Reload(true);
            }
        }

        public static bool IsTaskDisabled(OVRConfigurationTask task)
        {
            return DisabledRuleIds.Contains(task.Uid.ToString());
        }

        public static async Task<bool> Reload(bool forceRedownload)
        {
            if (forceRedownload)
            {
                Downloader.ClearCache();
            }

            var result = await Downloader.Fetch();
            return result.IsSuccess && LoadContentJsonData(result.Content);
        }

        [Serializable]
        internal struct UpstConfiguration
        {
            public DisabledRule[] disabled_rules;
        }

        [Serializable]
        internal struct DisabledRule
        {
            public string uid;
        }

        public static bool LoadContentJsonData(string jsonData)
        {
            var response = ParseJsonData(jsonData);
            if (response.disabled_rules != null)
            {
                DisabledRuleIds.Clear();
                foreach (var rule in response.disabled_rules)
                {
                    DisabledRuleIds.Add(rule.uid);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        public static UpstConfiguration ParseJsonData(string jsonData)
        {
            UpstConfiguration response;
            try
            {
                response = JsonUtility.FromJson<UpstConfiguration>(jsonData);
            }
            catch (Exception)
            {
                response = default;
            }

            return response;
        }
    }
}
