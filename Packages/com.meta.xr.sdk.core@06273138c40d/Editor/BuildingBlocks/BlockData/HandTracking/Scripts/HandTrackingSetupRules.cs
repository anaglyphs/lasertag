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

using System.Linq;
using UnityEditor;

namespace Meta.XR.BuildingBlocks.Editor
{
    [InitializeOnLoad]
    internal static class HandTrackingSetupRules
    {
        private static string InputMappingScriptDefine = "OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING";
        private static UnityEditor.Build.NamedBuildTarget[] BuildTargetsToCheck =
        {
            UnityEditor.Build.NamedBuildTarget.Android,
            UnityEditor.Build.NamedBuildTarget.Standalone
        };

        static HandTrackingSetupRules()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Compatibility,
                isDone: _ =>
                {
                    if (!HandTrackingBuildingBlockExists())
                    {
                        return true;
                    }

                    var projectConfig = OVRProjectConfig.CachedProjectConfig;
                    return projectConfig.handTrackingSupport != OVRProjectConfig.HandTrackingSupport.ControllersOnly;

                },
                message: $"Hand Tracking must be enabled in OVRManager when using its {Utils.BlockPublicName}",
                fix: _ =>
                {
                    var projectConfig = OVRProjectConfig.CachedProjectConfig;
                    projectConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                    OVRProjectConfig.CommitProjectConfig(projectConfig);
                },
                fixMessage: $"Enable Hand Tracking must be enabled in OVRManager"
            );

            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Compatibility,
                isDone: _ =>
                {
                    if (!HandTrackingBuildingBlockExists())
                    {
                        return true;
                    }

                    var projectConfig = OVRProjectConfig.CachedProjectConfig;
                    return projectConfig.handTrackingVersion == OVRProjectConfig.HandTrackingVersion.V2;

                },
                message: $"Hand Tracking V2 is required when using the Hand Tracking {Utils.BlockPublicName}",
                fix: _ =>
                {
                    var projectConfig = OVRProjectConfig.CachedProjectConfig;
                    projectConfig.handTrackingVersion = OVRProjectConfig.HandTrackingVersion.V2;
                    OVRProjectConfig.CommitProjectConfig(projectConfig);
                },
                fixMessage: $"Select Hand Tracking V2"
            );

            // Rule to assist in deprecation of legacy hand input mapping
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Recommended,
                group: OVRProjectSetup.TaskGroup.Compatibility,
                isDone: _ =>
                {
                    foreach (var buildTarget in BuildTargetsToCheck)
                    {
                        if (!PlayerSettings.GetScriptingDefineSymbols(buildTarget).Contains(InputMappingScriptDefine))
                            return false;
                    }
                    return true;
                },
                message: $"Disable hand pinch detection through OVRInput, which will be deprecated in a future release. " +
                $"If your project utilizes this behavior, please update your project to use the recommended API for detecting " +
                $"hand pinches going forward: OVRHand.GetFingerIsPinching.",
                url: "https://developers.meta.com/horizon/documentation/unity/unity-handtracking-interactions/#pinch",
                fix: _ =>
                {
                    foreach (var buildTarget in BuildTargetsToCheck)
                    {
                        string curDefines = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
                        if (!curDefines.Contains(InputMappingScriptDefine))
                        {
                            PlayerSettings.SetScriptingDefineSymbols(buildTarget,
                                curDefines != "" ? $"{curDefines};{InputMappingScriptDefine}" : InputMappingScriptDefine);
                        }
                    }
                },
                fixMessage: $"Set script define {InputMappingScriptDefine} to disable legacy mapping"
            );
        }

        private static bool HandTrackingBuildingBlockExists()
        {
            var handObjects = OVRProjectSetupUtils.FindComponentsInScene<OVRHand>();
            return handObjects.Any(hand => hand.GetComponent<BuildingBlock>());
        }
    }
}
