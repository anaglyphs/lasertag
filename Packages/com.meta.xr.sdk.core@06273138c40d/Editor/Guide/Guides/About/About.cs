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
using System.Threading.Tasks;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.ToolingSupport;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.Utils;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Contents;

namespace Meta.XR.Guides.Editor.About
{
    [InitializeOnLoad]
    internal static class About
    {
        public const string PackageName = "com.meta.xr.sdk.core";

        // It may be too early to retrieve version, so we use a nullable int to add the flag of
        // whether or not we could retrieve it
        private static int? _version;
        public static int? Version => _version ??= PackageList.ComputePackageVersion(PackageName);

        private static WelcomePage _welcomePage;
        private static WelcomePage WelcomePage => _welcomePage ??= new WelcomePage();

        private static Onboarding _onboarding;
        private static Onboarding Onboarding => _onboarding ??= new Onboarding();

        private const string RampUpKey = "onboarding_new_flow";
        public static Task<bool> UseOnboarding() =>
                FeatureRampUpManager.GetFeatureKeysResultAsync(RampUpKey, false);
        public static async Task<GuidedSetup> FetchGuide()
        {
            var useNewFlow = await UseOnboarding();
            return useNewFlow ? Onboarding : WelcomePage;
        }

        private static int? _latestVersion;
        public static int? LatestVersion => _latestVersion ??= PackageList.ComputeLatestPackageVersion(PackageName);

        [MenuItem("Meta/About Meta XR SDK", false, 2000)]
        private static void SetupGuide()
        {
            ShowGuide(Origins.Menu, true);
        }

        public static ToolDescriptor ToolDescriptor = new()
        {
            Order = -11,
            Icon = MetaWhiteIcon,
            Name = "Welcome to Meta XR SDK",
            MenuDescription = "Get Started",
            AddToStatusMenu = true,
            AddToMenu = false,
            OnClickDelegate = (origin) => ShowGuide(origin, true),
            InfoTextDelegate = ComputeInfoText,
            PillIcon = ComputePillIcon,
            IsStatusMenuItemDarker = true
        };

        private static readonly OnlyOncePerSessionBool _shouldShow = new()
        {
            Uid = "ShowAbout",
            Owner = ToolDescriptor,
            SendTelemetry = false
        };

        static About()
        {
            OVRTelemetryConsent.OnLibrariesConsentSet += OnConsentSet;
        }

        private static void OnConsentSet(bool enabled)
        {
            _ = enabled; // ignored, preferring to trust HasUnifiedConsentValue instead

            // delayCall so that the window waits for the full editor to be loaded before popping up
            EditorApplication.delayCall += () =>
            {
                if (!OVREditorUtils.IsMainEditor()
                 || !OVRTelemetryConsent.HasUnifiedConsentValue
                 || !_shouldShow.Value
                 || Application.isBatchMode)
                    return;

                ShowGuide(Origins.Self);
            };
        }

        private static async void ShowGuide(Origins origin, bool forceShow = false)
        {
            var guide = await FetchGuide();
            guide.ShowWindow(origin, forceShow);
        }

        private static (string, Color?) ComputeInfoText()
        {
            if (Version < LatestVersion)
            {
                return ($"Version {Version} (New Version {LatestVersion} Available!)", NewColor);
            }
            else
            {
                return ($"Version {Version}", DisabledColor);
            }
        }

        private static (TextureContent, Color?, bool) ComputePillIcon()
        {
            if (Version < LatestVersion)
            {
                return (UpdateIcon, NewColor, true);
            }
            else
            {
                return (null, null, false);
            }
        }
    }
}
