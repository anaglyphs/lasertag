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

#if USING_XR_SDK_OPENXR

using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

#if UNITY_EDITOR
namespace Meta.XR
{
    [AttributeUsage(AttributeTargets.Class)]
    public class MetaOpenXRFeatureAttribute : OpenXRFeatureAttribute
    {
        public MetaOpenXRFeatureAttribute(string featureId, string uiName, string desc, string version,
            string targetApiVersion = null, string category = FeatureCategory.Feature, params string[] extensions)
        {
            UiName = uiName;
            BuildTargetGroups = new[]
            {
                BuildTargetGroup.Standalone,
                BuildTargetGroup.Android
            };
            Category = category;
            Company = "Meta";
            Desc = desc;
            DocumentationLink = "https://developers.meta.com/horizon/develop/unity";
            OpenxrExtensionStrings = string.Join(' ', extensions);
#if UNITY_OPENXR_PLUGIN_1_16_0_OR_NEWER
            if (!string.IsNullOrEmpty(targetApiVersion))
            {
                TargetOpenXRApiVersion = targetApiVersion;
            }
#endif
            Version = version;
            FeatureId = featureId;
        }
    }
}
#endif
#endif
