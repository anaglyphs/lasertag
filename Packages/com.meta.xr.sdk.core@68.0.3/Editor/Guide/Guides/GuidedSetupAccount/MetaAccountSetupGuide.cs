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

#if USING_META_XR_PLATFORM_SDK

using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.Guides.Editor.Items;
using Oculus.Platform;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;

namespace Meta.XR.Guides.Editor
{
    internal static class MetaAccountSetupGuide
    {
        private const string MetaDashboardURL = "https://developer.oculus.com/manage";
        private const string AppIDRetrieveDocURL = "https://developer.oculus.com/documentation/unity/unity-platform-entitlements/#retrieve-the-appid-from-the-developer-portal";
        private const string CreateOrgDocURL = "https://developer.oculus.com/resources/publish-account-management-intro/";
        private const string AddTestUserDocURL = "https://developer.oculus.com/resources/test-users/";
        private const string AddPlatformFeaturesDocURL = "https://developer.oculus.com/documentation/unity/unity-shared-spatial-anchors/#prerequisites";
        private static GuideStyles.ContentStatusType AppIdStatusType => HasAppId() ? GuideStyles.ContentStatusType.Normal : GuideStyles.ContentStatusType.Warning;

        private const int SubcontentMargin = 20;

        private const string _defaultAppIdFieldText = "Paste you App Id here";
        private static bool _appIdSet;
        private static TextFieldWithButton _appIdField;
        private static Icon _appIdValidateField;
        private static Icon _appIdStatusLabel;
        private static GuideWindow _window;

        private static bool HasValidAppId => (!String.IsNullOrEmpty(_appIdField.Text) &&
                                              _appIdField.Text.All(char.IsDigit)) ||
                                             _appIdField.Text.Equals(_defaultAppIdFieldText);

        private static void Init()
        {
            var title = "Meta Account Setup Guide";
            var description = "This will assist you in setting up your Meta developer account and guide you to retrieve the AppID to use it in your project.";

            var options = new GuideWindow.GuideOptions(GuideWindow.DefaultOptions)
            {
                ShowCloseButton = false
            };

            _window = Guide.Create(title, description, GetItems, options);

            _window.OnWindowDraw += () =>
            {
                _appIdValidateField.Hide = HasValidAppId;
            };
            _window.OnWindowDestroy += () =>
            {
                OVRTelemetry.Start(OVRTelemetryConstants.GuidedSetup.MarkerId.CloseSSAWindow)
                    .AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.HasAppId, HasAppId().ToString())
                    .Send();
            };
        }

        [MenuItem("Meta/Tools/Meta Account Setup Guide")]
        private static void SetupGuide()
        {
            ShowWindow(Utils.TriggerSource.Menu);
        }

        public static void ShowWindow(Utils.TriggerSource source, bool forceShow = false)
        {
            Init();
            _window.Show(forceShow);
            OVRTelemetry.Start(OVRTelemetryConstants.GuidedSetup.MarkerId.OpenSSAWindow)
                .AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.ActionTrigger, source.ToString())
                .AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.HasAppId, HasAppId().ToString())
                .Send();
        }

        [GuideItems]
        private static List<IGuideItem> GetItems()
        {
            if(_window == null)
            {
                Init();
            }

            _appIdStatusLabel = new Icon(GuideStyles.Contents.SuccessIcon, Colors.OffWhite, "");
            UpdateAppIdStatus();

            return new List<IGuideItem>
            {
                OpenDashboardUI(),
                new AddSpace(),
                AppIdSetUI(),
                new AddSpace(),
                DataUseCheckUI(),
                new AddSpace(),
                TestUserAddUI(),
                new AddSpace(flexibleSpace: true),
                _appIdStatusLabel,
                new AddSpace(),
                ButtonsGroupUI()
            };
        }

        private static IGuideItem OpenDashboardUI()
        {
            var labelStyle = new GUIStyle(GuideStyles.GUIStyles.Label)
            {
                wordWrap = false
            };
            var textContent = new GUIContent("Click here to open");
            var widthText = labelStyle.CalcSize(textContent).x;

            return new GroupedGuideItem(new List<IGuideItem>
            {
                new BulletedLabel(textContent.text, labelStyle, GuideStyles.ContentStatusType.Normal, GUILayout.Width(widthText)),
                new LinkLabel("Meta Quest Developer Dashboard", () => Utils.OpenURL(MetaDashboardURL, nameof(MetaAccountSetupGuide))),
                new AddSpace(flexibleSpace: true)
            });
        }

        private static IGuideItem AppIdSetUI()
        {
            var labelStyle = new GUIStyle(GuideStyles.GUIStyles.Label) { wordWrap = false };
            var t0 = new GUIContent("Follow the steps to");
            var w0 = labelStyle.CalcSize(t0).x;

            var t1 = new GUIContent("If you already have an Organization follow these");
            var w1 = labelStyle.CalcSize(t1).x;

            var link0 = new GUIContent("create an Organization.");
            var link1 = new GUIContent("steps to retrieve AppID.");

            var part1 = new GroupedGuideItem(new List<IGuideItem>
            {
                new BulletedLabel(t0.text, labelStyle, GuideStyles.ContentStatusType.Normal, GUILayout.Width(w0)),
                new LinkLabel(link0.text, () => Utils.OpenURL(CreateOrgDocURL, nameof(MetaAccountSetupGuide))),
                new AddSpace(flexibleSpace: true)
            });

            var groupStyle = new GUIStyle() { margin = new RectOffset(SubcontentMargin, 0, 0, 0) };

            var part2 = new GroupedGuideItem(new List<IGuideItem>
            {
                new Label(t1.text, labelStyle, GUILayout.Width(w1)),
                new LinkLabel(link1.text, () => Utils.OpenURL(AppIDRetrieveDocURL, nameof(MetaAccountSetupGuide))),
                new AddSpace(flexibleSpace: true)
            }, groupStyle);

            _appIdField = new TextFieldWithButton("", _defaultAppIdFieldText, "Set", _ =>
            {
                if (!HasValidAppId) return;

                PlatformSettings.MobileAppID = _appIdField.Text;
                PlatformSettings.AppID = _appIdField.Text;
                Selection.activeObject = PlatformSettings.Instance;
                _appIdSet = true;
                UpdateAppIdStatus();

                OVRTelemetry.Start(OVRTelemetryConstants.GuidedSetup.MarkerId.SetAppIdFromGuidedSetup).Send();
            });

            _appIdValidateField = new Icon(GuideStyles.Contents.StatusIcon, Colors.ErrorColor, "Invalid AppID.");

            var part3 = new GroupedGuideItem(new List<IGuideItem>
            {
                _appIdField,
                new AddSpace(4),
                _appIdValidateField
            }, groupStyle, Utils.GuideItemPlacementType.Vertical);

            return new GroupedGuideItem(new List<IGuideItem> { part1, part2, part3 }, Utils.GuideItemPlacementType.Vertical);
        }

        private static IGuideItem DataUseCheckUI()
        {
            var dataUseLabel = new BulletedLabel("To use the Shared Spatial Anchor, the <b>UserID</b> " +
                                                 "and <b>UserProfile</b> Platform\n" +
                                                 "features must be enabled in <b>Data Use Checkup</b>.");

            var labelStyle = new GUIStyle(GuideStyles.GUIStyles.Label) { wordWrap = false };
            var t0 = new GUIContent("Please refer to the");
            var w0 = labelStyle.CalcSize(t0).x - Constants.TextWidthOffset;

            var t1 = new GUIContent("section for more details.");
            var w1 = labelStyle.CalcSize(t1).x - Constants.TextWidthOffset;

            var linkText = new GUIContent("App Configuration");

            var appConfigGroup = new GroupedGuideItem(new List<IGuideItem>
            {
                new AddSpace(SubcontentMargin),
                new Label(t0.text, labelStyle, GUILayout.Width(w0)),
                new LinkLabel(linkText.text, () => Utils.OpenURL(AddPlatformFeaturesDocURL, nameof(MetaAccountSetupGuide))),
                new Label(t1.text, labelStyle, GUILayout.Width(w1)),
                new AddSpace(flexibleSpace: true)
            });

            return new GroupedGuideItem(new List<IGuideItem> { dataUseLabel, appConfigGroup }, Utils.GuideItemPlacementType.Vertical);
        }

        private static IGuideItem TestUserAddUI()
        {
            var labelStyle = new GUIStyle(GuideStyles.GUIStyles.Label) { wordWrap = false };
            var t0 = new GUIContent("Follow these");
            var w0 = labelStyle.CalcSize(t0).x - Constants.TextWidthOffset;

            var linkText = "steps to add test users in Members Management";

            var part1 = new GroupedGuideItem(new List<IGuideItem>
            {
                new BulletedLabel(t0.text, labelStyle, GuideStyles.ContentStatusType.Normal, GUILayout.Width(w0)),
                new LinkLabel(linkText, () => Utils.OpenURL(AddTestUserDocURL, nameof(MetaAccountSetupGuide))),
                new AddSpace(true)
            });

            var part2Style = new GUIStyle(GuideStyles.GUIStyles.Label);
            part2Style.margin.left = SubcontentMargin;
            var part2 = new Label("to test your Shared Spatial Anchor app before publishing it publicly.", part2Style);

            return new GroupedGuideItem(new List<IGuideItem> { part1, part2 }, Utils.GuideItemPlacementType.Vertical);
        }

        private static void UpdateAppIdStatus()
        {
            _appIdStatusLabel.Hide = !HasAppId();

            var appId = "";
#if UNITY_ANDROID
            appId = PlatformSettings.MobileAppID;
#else
            appId = PlatformSettings.AppID;
#endif

            if (HasAppId() && !_appIdSet)
            {
                _appIdStatusLabel.LabelText = $"Your project already has an AppID: {appId}";
            }
            else if(_appIdSet)
            {
                _appIdStatusLabel.LabelText = $"Plaform settings has been set with AppID: {appId}";
            }
        }

        private static IGuideItem ButtonsGroupUI()
        {
            return new GroupedGuideItem(new List<IGuideItem>
            {
                new Button("Open Platform Settings", () => Selection.activeObject = PlatformSettings.Instance),
                new Button("Close", () => _window.Close())
            });
        }

        private static bool HasAppId()
        {
#if UNITY_ANDROID
            return !String.IsNullOrEmpty(PlatformSettings.MobileAppID);
#else
            return !String.IsNullOrEmpty(PlatformSettings.AppID);
#endif
        }
    }
}

#endif // USING_META_XR_PLATFORM_SDK
