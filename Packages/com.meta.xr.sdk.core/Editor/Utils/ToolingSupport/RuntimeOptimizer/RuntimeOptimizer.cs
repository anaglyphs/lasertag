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

using Meta.XR.Editor.Id;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;

namespace Meta.XR.Editor.ToolingSupport.RuntimeOptimizer
{
    [InitializeOnLoad]
    internal static class RuntimeOptimizer
    {
        static RuntimeOptimizer()
        {
        }

        public static readonly TextureContent Icon = TextureContent.CreateContent("ovr_icon_runtimeoptimizer.png",
            new TextureContent.Category("Icons", false, "Meta.XR.Editor.ToolingSupport.RuntimeOptimizer"));

        public const string PackageName = "com.meta.xr.runtimeoptimizer";
        public const string PublicName = "Quest Runtime Optimizer";
        public const string Description = "Diagnose Performance Issues";
        private const string AssetStoreUrl = "https://assetstore.unity.com/packages/tools/integration/meta-quest-runtime-optimizer-325194";
        private const string ClickLabel = "Install from Unity Asset Store";

        private static UrlLinkDescription _installLink;
        private static UrlLinkDescription InstallLink => _installLink ??=
            new UrlLinkDescription()
            {
                Content = new GUIContent(ClickLabel),
                URL = AssetStoreUrl,
                Origin = Origins.StatusMenu,
                OriginData = ToolDescriptor
            };

        public static readonly ToolDescriptor ToolDescriptor = new()
        {
            Name = PublicName,
            MenuDescription = Description,
            Icon = Icon,
            Order = 98,
            AddToStatusMenu = true,
            CanBeNew = true,
            AddToMenu = true,
            InfoTextDelegate = InfoTextDelegate,
            OnClickDelegate = OnClickDelegate,
#if !RUNTIME_OPTIMIZER_INSTALLED
            GreyedOut = true,
#endif
        };

        private static void OnClickDelegate(Origins obj)
        {
#if RUNTIME_OPTIMIZER_INSTALLED
            Meta.XR.RuntimeOptimizer.Editor.RuntimeOptimizerWindow.ShowWindow();
#else
            InstallLink.Click();
#endif
        }

        private static (string, Color?) InfoTextDelegate()
        {
#if RUNTIME_OPTIMIZER_INSTALLED
            return (null, null);
#else
            return (ClickLabel, DisabledColor);
#endif
        }
    }
}
