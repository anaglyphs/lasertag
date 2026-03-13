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
using Meta.XR.Editor.UserInterface;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Telemetry;

namespace Meta.XR.Editor.Notifications
{
    internal class Notification : IIdentified
    {
        internal static class Manager
        {
            // Although it's going to be treated like a queue, we actually may have to be able to remove
            // any item an any time, so a list is more appropriate.
            private static readonly List<Notification> Notifications = new();

            // Snooze logic, keeping track of the caller object in order to remove it
            // from the set later, on release
            private static readonly HashSet<object> SnoozeSet = new();

            private static bool Snoozed => SnoozeSet.Count > 0;

            static Manager()
            {
#pragma warning disable CS4014
                WarmUp();
#pragma warning restore CS4014
            }

            /// To avoid notifications right on start-up, especially in an environment
            /// that is potentially still initializing, we are delaying notifications
            /// during the first 5 seconds
            static async Task WarmUp()
            {
                const int delayInMs = 5000;
                var task = Task.Delay(delayInMs);
                RequestSnooze(task);
                await task;
                ReleaseSnooze(task);
            }

            public static void RequestSnooze(object caller)
            {
                if (caller == null) return;

                SnoozeSet.Add(caller);
            }

            public static void ReleaseSnooze(object caller)
            {
                if (caller == null) return;

                SnoozeSet.Remove(caller);

                Refresh();
            }

            public static void Enqueue(Notification notification, Origins origin)
            {
                if (notification == null) return;
                if (Notifications.Contains(notification)) return;

                // We're still considering Open and Close telemetry to be linked to the request of showing the notification
                // independently of its actual stacking/queuing
                OVRTelemetry.Start(MarkerId.PageOpen)
                    .AddAnnotation(AnnotationType.Origin, origin.ToString())
                    .AddAnnotation(AnnotationType.Action, Origins.Notification.ToString())
                    .AddAnnotation(AnnotationType.ActionData, notification.Id)
                    .AddAnnotation(AnnotationType.ActionType, notification.GetType().Name)
                    .Send();

                Notifications.Add(notification);

                Refresh();
            }

            public static void Remove(Notification notification, Origins origin)
            {
                if (notification == null) return;
                if (!Notifications.Contains(notification)) return;

                // We're still considering Open and Close telemetry to be linked to the request of showing the notification
                // independently of its actual stacking/queuing
                OVRTelemetry.Start(MarkerId.PageClose)
                    .AddAnnotation(AnnotationType.Origin, origin.ToString())
                    .AddAnnotation(AnnotationType.Action, Origins.Notification.ToString())
                    .AddAnnotation(AnnotationType.ActionData, notification.Id)
                    .AddAnnotation(AnnotationType.ActionType, notification.GetType().Name)
                    .Send();

                // Hide if not hidden already
                notification.Hide();

                Notifications.Remove(notification);

                Refresh();
            }

            private static void Refresh()
            {
                if (Snoozed) return;

                var notificationToShow = Notifications.FirstOrDefault();
                if (notificationToShow == null
                    || notificationToShow.Shown) return;

                notificationToShow.Show();
            }
        }
        public string Id { get; }

        public TextureContent Icon { get; set; } = UserInterface.Styles.Contents.MetaIconLarge;

        public TextureContent HeaderImage { get; set; }
        public float? HeaderImageRatio { get; set; }
        public int HeaderImageBorder { get; set; }

        public IEnumerable<IUserInterfaceItem> Items { get; set; }
        public bool ShowCloseButton { get; set; }
        public float Duration { get; set; } = -1;
        public event System.Action OnShow;
        public Color GradientColor { get; set; } = UserInterface.Styles.Colors.Grey6a;
        public float ExpectedWidth { get; set; } = Styles.Constants.Width;

        private double _timestamp;
        private NotificationWindow _window;
        private bool Shown => _window != null;
        public bool DurationHasPassed => Duration > 0.0f && Time.realtimeSinceStartup - _timestamp > Duration;

        public Notification(string id)
        {
            Id = id;
        }

        public void Enqueue(Origins origin)
        {
            Manager.Enqueue(this, origin);
        }

        public void Remove(Origins origin)
        {
            Manager.Remove(this, origin);
        }

        private void Show()
        {
            if (Shown) return;

            if (!Meta.XR.Editor.UserInterface.Utils.ShouldRenderEditorUI()) return;

            _window = ScriptableObject.CreateInstance<NotificationWindow>();
            _window.Setup(this);
            _window.ShowAsTooltip();

            _timestamp = Time.realtimeSinceStartup;

            OnShow?.Invoke();
        }

        private void Hide()
        {
            if (!Shown) return;

            _window.Setup(null);
            _window.Close();
        }
    }
}
