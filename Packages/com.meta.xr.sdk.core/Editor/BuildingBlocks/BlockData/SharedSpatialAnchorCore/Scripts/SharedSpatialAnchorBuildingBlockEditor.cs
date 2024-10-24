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
using Meta.XR.Editor.Tags;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using Meta.XR.Guides.Editor;
using Meta.XR.Guides.Editor.Items;

#if USING_META_XR_PLATFORM_SDK
using Oculus.Platform;
#endif // USING_META_XR_PLATFORM_SDK

namespace Meta.XR.BuildingBlocks.Editor
{
    [CustomEditor(typeof(SharedSpatialAnchorCoreBuildingBlock))]
    public class SharedSpatialAnchorBuildingBlockEditor : BuildingBlockEditor
    {
        protected override void ShowAdditionals()
        {
            EditorGUILayout.BeginVertical(Styles.GUIStyles.ErrorHelpBox);
            new Icon(Styles.Contents.InfoIcon, Color.white, "<b>A Meta Quest AppID is required to use Shared Spatial Anchor.</b>").Draw();
#if USING_META_XR_PLATFORM_SDK
            if (HasAppId())
            {
                var appId = "";
#if UNITY_ANDROID
                appId = PlatformSettings.MobileAppID;
#else // UNITY_ANDROID
                appId = PlatformSettings.AppID;
#endif // UNITY_ANDROID
                new Icon(Styles.Contents.SuccessIcon, Color.white, $"<b>AppID found in Platform Settings: {appId}</b>").Draw();
            }
            else
            {
                new Icon(Styles.Contents.ErrorIcon, Color.white, "<b>AppID is missing. Use <color=#66aaff>Meta Account Setup Guide</color> to configure your project.</b>").Draw();
            }

            EditorGUILayout.Space();
            DrawButtons();
#else // USING_META_XR_PLATFORM_SDK
            new Icon(Styles.Contents.ErrorIcon, Color.white, "<b>Meta Platform SDK is missing.</b>").Draw();
            EditorGUILayout.Space();

#endif // USING_META_XR_PLATFORM_SDK
            EditorGUILayout.EndVertical();
        }

#if USING_META_XR_PLATFORM_SDK
        private void DrawButtons()
        {
            EditorGUILayout.BeginVertical();
            if (GUILayout.Button("Open Meta Account Setup Guide"))
            {
                MetaAccountSetupGuide.ShowWindow(Guides.Editor.Utils.TriggerSource.Inspector, true);
            }

            if (GUILayout.Button("Open Platform Settings"))
            {
                Selection.activeObject = PlatformSettings.Instance;
            }
            EditorGUILayout.EndVertical();
        }

        internal static bool HasAppId()
        {
#if UNITY_ANDROID
            return !String.IsNullOrEmpty(PlatformSettings.MobileAppID);
#else
            return !String.IsNullOrEmpty(PlatformSettings.AppID);
#endif
        }
#endif // USING_META_XR_PLATFORM_SDK
    }
}
