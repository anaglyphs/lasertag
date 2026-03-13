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

[InitializeOnLoad]
internal static class OVRProjectSetupVirtualKeyboard
{
    private const OVRProjectSetup.TaskGroup Group = OVRProjectSetup.TaskGroup.Features;

    static OVRProjectSetupVirtualKeyboard()
    {
        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            group: Group,
            isDone: _ => OVRProjectSetupUtils.FindComponentInScene<OVRVirtualKeyboard>() == null ||
                         OVRProjectConfig.CachedProjectConfig.renderModelSupport == OVRProjectConfig.RenderModelSupport.Enabled,
            message: "When using Virtual Keyboard in your project it's required to enable Render Model Support",
            fix: _ =>
            {
                var projectConfig = OVRProjectConfig.CachedProjectConfig;
                projectConfig.renderModelSupport = OVRProjectConfig.RenderModelSupport.Enabled;
                OVRProjectConfig.CommitProjectConfig(projectConfig);
            },
            fixMessage: "Enable Render Model Support");

        OVRProjectSetup.AddTask(
            level: OVRProjectSetup.TaskLevel.Required,
            group: Group,
            isDone: _ => OVRProjectSetupUtils.FindComponentInScene<OVRVirtualKeyboard>() == null ||
                         OVRProjectConfig.CachedProjectConfig.virtualKeyboardSupport != OVRProjectConfig.FeatureSupport.None,
            message: "When using Virtual Keyboard in your project it's required to enable its capability",
            fix: _ =>
            {
                var projectConfig = OVRProjectConfig.CachedProjectConfig;
                projectConfig.virtualKeyboardSupport = OVRProjectConfig.FeatureSupport.Supported;
                OVRProjectConfig.CommitProjectConfig(projectConfig);
            },
            fixMessage: "Enable Virtual Keyboard Support");
    }
}
