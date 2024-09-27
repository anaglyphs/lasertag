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
using Meta.XR.Editor.UserInterface;
using Meta.XR.Guides.Editor.Items;

namespace Meta.XR.Guides.Editor
{
    internal static class OcclusionBlockSetupInfo
    {
        private static string ImportantTag = $"<color={XR.Editor.UserInterface.Utils.ColorToHex(Styles.Colors.Yellow)}>[Important]</color>";

        public static void Show(bool forceShow = false)
        {
            var title = "Occlusion Building Block";
            var description = ImportantTag + " Occlusion Building Block made the following critical changes in the current project.";
            Guide.Create(title, description, GetItems).Show(forceShow);
        }

        [GuideItems]
        private static List<IGuideItem> GetItems()
        {
            var graphicsAPIInfo = "Set the <b>Graphics API</b> to <b>Vulkan</b> only in Unity Player Settings.";
            var quest3Info = "It's a <b>Quest 3</b> only feature and set the target device to <b>Quest 3</b>.";
            return new List<IGuideItem>
            {
                new BulletedLabel(graphicsAPIInfo, GuideStyles.ContentStatusType.Warning),
                new BulletedLabel(quest3Info, GuideStyles.ContentStatusType.Warning),
                new AddSpace(),
                new Label("<b>Additional notes:</b>"),
                new BulletedLabel(
                    "Occlusion / DepthAPI requires access to spatial data. <b>Scene Support</b> has been enabled."),
                new BulletedLabel("Minimum required Unity Editor version: <b>2022.3.1</b> or <b>2023.2.</b>"),
                new AddSpace(),
                new GroupedGuideItem(new List<IGuideItem>
                {
                    new Label("Visit the following link for more details:"),
                    new LinkLabel("DepthAPI documentation",
                        () => Utils.OpenURL("https://developer.oculus.com/documentation/unity/unity-depthapi/",
                            nameof(OcclusionBlockSetupInfo))),
                    new AddSpace(flexibleSpace: true)
                })
            };
        }
    }
}
