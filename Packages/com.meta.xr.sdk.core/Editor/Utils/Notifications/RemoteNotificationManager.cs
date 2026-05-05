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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using UnityEditor;
using UnityEngine;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Editor.Callbacks;

namespace Meta.XR.Editor.Notifications
{
    [InitializeOnLoad]
    internal static class RemoteNotificationManager
    {
        private static RemoteJsonContentDownloader _downloader;
        private static Validator _validator;

        private static readonly CustomBool InitSession = new OnlyOncePerSessionBool
        {
            Owner = null,
            Uid = "RemoteNotificationManager.InitSession",
            SendTelemetry = false
        };

        static RemoteNotificationManager()
        {
            InitializeOnLoad.Register(Initialize);
        }

        private static void Initialize()
        {
            _validator = new Validator();
            _downloader = new RemoteJsonContentDownloader(
                    cacheFile: "remote_notifications.json",
                    url: "https://www.facebook.com/developerframeworktools/unity/notifications")
                .WithCacheDuration(TimeSpan.FromHours(2))
                .WithMachineIdUrlParameter()
                .WithSDKVersionUrlParameter();

#pragma warning disable CS4014
            InitializeAsync();
#pragma warning restore CS4014
        }

        private static async Task InitializeAsync()
        {
            if (!InitSession)
            {
                return;
            }

            UsageSettings.NumberOfActiveSessions.SetValue(UsageSettings.NumberOfActiveSessions + 1);
            UsageSettings.UserActivationDate.SetValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

            await FeatureRampUpManager.WaitForKeysFetchingAsync();
            var successfulLoad = await Reload();
            if (!successfulLoad)
            {
                await ForceDownloadAsync();
            }
        }


        private static async Task ForceDownloadAsync()
        {
            _downloader.ClearCache();
            await Reload();
        }

        private static async Task<bool> Reload()
        {
            var result = await _downloader.Fetch();
            if (!result.IsSuccess)
            {
                return false;
            }

            var notifications = await GetNotificationsFromJsonString(result.Content);
            EnqueueNotifications(notifications);
            return true;
        }

        private static bool IsNotificationRampedUp(this NotificationData data)
        {
            return string.IsNullOrEmpty(data.rampUpKey)
                   || FeatureRampUpManager.GetRemoteKeysResult(data.rampUpKey);
        }

        private static Task<Notification[]> GetNotificationsFromJsonString(string jsonData)
        {
            try
            {
                var response = JsonUtility.FromJson<NotificationsResponse>(jsonData);
                var notificationsTasks = (response.notifications ?? Enumerable.Empty<NotificationData>())
                    .Where(data => data.IsNotificationValid(_validator))
                    .Where(IsNotificationRampedUp)
                    .Select(data => data.BuildNotificationFromData());

                return Task.WhenAll(notificationsTasks);
            }
            catch (Exception)
            {
                return Task.FromResult(Array.Empty<Notification>());
            }
        }

        private static void EnqueueNotifications(IEnumerable<Notification> notifications)
        {
            foreach (var notification in notifications)
            {
                notification.Enqueue(Origins.Remote);
            }
        }

    }
}
