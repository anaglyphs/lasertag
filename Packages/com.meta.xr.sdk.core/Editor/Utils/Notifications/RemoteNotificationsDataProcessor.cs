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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.Notifications
{
    internal static class RemoteNotificationsDataProcessor
    {
        private static IEnumerable<IUserInterfaceItem> GetNotificationGuideItems(this NotificationData data)
        {
            yield return new Label(data.title, UIStyles.GUIStyles.Title);
            yield return new Label(data.message, UIStyles.GUIStyles.Label);

            if (data.urlButton.IsValid)
            {
                yield return new GroupedItem(data.GetNotificationButtons());
            }
        }

        private static void OpenUrlOrTool(this NotificationUrlButton button)
        {
            var url = button.url;
            const string toolPrefix = "toolid://";

            if (url.StartsWith(toolPrefix))
            {
                var toolId = url[toolPrefix.Length..];
                OpenToolById(toolId);
            }
            else
            {
                Application.OpenURL(url);
            }
        }

        private static void OpenToolById(string toolId)
        {
            var tool = ToolRegistry.Registry.FirstOrDefault(entry => entry.Id == toolId);
            if (tool == null)
            {
                return;
            }

            tool.OnClickDelegate?.Invoke(Origins.Notification);
        }

        private static IEnumerable<IUserInterfaceItem> GetNotificationButtons(this NotificationData data)
        {
            var buttonLayout = new[] { GUILayout.Height(20f), GUILayout.MinWidth(80f) };
            var highlightedButtonColor = Meta.XR.Editor.UserInterface.Styles.Colors.LightMeta;

            yield return new AddSpace(flexibleSpace: true);
            yield return new Button(
                new ActionLinkDescription
                {
                    Content = new GUIContent(data.urlButton.label),
                    Action = () => data.urlButton.OpenUrlOrTool(),
                    BackgroundColor = highlightedButtonColor
                },
                buttonLayout);
        }

        public static async Task<Notification> BuildNotificationFromData(this NotificationData data)
        {
            var notification = new Notification(data.id)
            {
                Items = data.GetNotificationGuideItems(),
                ShowCloseButton = !data.hideCloseButton,
                Duration = data.duration
            };

            if (!string.IsNullOrEmpty(data.gradientColor))
            {
                notification.GradientColor = UserInterface.Utils.HexToColor(data.gradientColor);
            }

            if (data.headerContentId != 0)
            {
                var remoteTextureResult =
                    await RemoteTextureContent.CreateAsync(data.headerContentId, Styles.Contents.NotificationsTextures);

                if (remoteTextureResult.IsSuccess)
                {
                    notification.HeaderImage = remoteTextureResult.Content;
                }
            }

            notification.OnShow += () =>
            {
                EditorPrefs.SetInt(
                    data.GetShownNotificationKey(),
                    EditorPrefs.GetInt(data.GetShownNotificationKey()) + 1);
            };

            return notification;
        }

        public static bool IsNotificationValid(this NotificationData data, Validator validator)
        {
            var hasReachedShowLimit = EditorPrefs.GetInt(data.GetShownNotificationKey(), -1) >= data.timesShown;

            if (hasReachedShowLimit)
            {
                return false;
            }

            return (data.filters ?? Enumerable.Empty<NotificationFilter>())
                .All(validator.ValidateFilter);
        }

        private static string GetShownNotificationKey(this NotificationData data) => $"Notification_Shown_{data.id}";
    }
}
