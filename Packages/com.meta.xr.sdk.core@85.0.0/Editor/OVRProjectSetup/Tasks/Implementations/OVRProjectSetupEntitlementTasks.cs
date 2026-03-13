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

using UnityEditor;
using System.Collections.Generic;

public enum DUCFeature
{
    UserID,
    UserProfile,
    UserAgeGroup,
    IAP,
    Subscriptions,
    Avatars,
    DeepLinking,
    Friends,
    BlockedUsers,
    Invites,
    Parties,
    Challenges,
    DeviceBan
}

[InitializeOnLoad]
internal static class OVRProjectSetupEntitlementTasks
{
    private const OVRProjectSetup.TaskGroup Group = OVRProjectSetup.TaskGroup.Features;

    // Dictionary mapping for DUC features
    public static readonly Dictionary<string, HashSet<string>> EntitlementSettings = new Dictionary<string, HashSet<string>>
    {
        { "abuse_report", new HashSet<string> { "UserID" } },
        { "achievements", new HashSet<string> { "UserID" } },
        { "analytics", new HashSet<string> { "" } },
        { "application", new HashSet<string> { "IAP", "Subscriptions" } },
        { "application_lifecycle", new HashSet<string> { "" } },
        { "asset_file", new HashSet<string> { "" } },
        { "avatar", new HashSet<string> { "Avatars" } },
        { "challenges", new HashSet<string> { "Challenges" } },
        { "colocation", new HashSet<string> { "" } },
        { "consent", new HashSet<string> { "" } },
        { "cowatching", new HashSet<string> { "" } },
        { "device_application_integrity", new HashSet<string> { "" } },
        { "entitlements", new HashSet<string> { "IAP" } },
        { "graph_api", new HashSet<string> { "" } },
        { "group_presence", new HashSet<string> { "UserID", "UserProfile", "Friends", "DeepLinking", "Invites" } },
        { "http", new HashSet<string> { "" } },
        { "iap", new HashSet<string> { "UserID", "USER_PROFILE", "IAP", "Friends" } },
        { "language_pack", new HashSet<string> { "" } },
        { "leaderboards", new HashSet<string> { "UserID", "UserProfile", "Friends", "Challenges" } },
        { "livestreaming", new HashSet<string> { "" } },
        { "media", new HashSet<string> { "" } },
        { "net_sync", new HashSet<string> { "" } },
        { "notifications", new HashSet<string> { "UserID", "UserProfile", "Invites", "Friends", "Challenges" } },
        { "parties", new HashSet<string> { "Parties" } },
        { "push_notification", new HashSet<string> { "Invites" } },
        { "rich_presence", new HashSet<string> { "UserID", "UserProfile", "Friends" } },
        { "user_age_category", new HashSet<string> { "" } },
        { "user_data_store", new HashSet<string> { "" } },
        { "users", new HashSet<string> { "UserID", "UserProfile", "Friends", "Invites", "DeepLinking", "UserAgeGroup", "Avatars", "BlockedUsers" } },
        { "voip", new HashSet<string> { "" } }
    };

    static OVRProjectSetupEntitlementTasks()
    {
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: Group,
            conditionalValidity: _ => true,
            tags: OVRProjectSetup.TaskTags.ManuallyFixable,
            message: "Complete a Data Use Checkup to meet DUC policy requirements. Please ignore if you are not using any Platform SDK APIs.",
            manualSetup: new DataUseCheckupManualSetupInfo()
        );

        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Recommended,
            group: Group,
            conditionalValidity: _ => true,
            tags: OVRProjectSetup.TaskTags.ManuallyFixable,
            message: "Set up the application ID and the package name. Please ignore if you are not using any Platform SDK APIs.",
            manualSetup: new AppIDMatchingPkgNameSetupInfo()
        );
    }
}
