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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Meta.XR.Editor.Telemetry
{
    internal class ProjectComplexityAuditor : IPreprocessBuildWithReport
    {
        private const string RampUpKey = "unity_project_complexity_auditor";
        private static bool _isCollecting;

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            LogComplexity();
        }

        private static async void LogComplexity()
        {
            var isEnabled = await FeatureRampUpManager.GetFeatureKeysResultAsync(RampUpKey, false);
            if (!isEnabled)
            {
                return;
            }

            if (_isCollecting)
            {
                return;
            }

            _isCollecting = true;
            try
            {
                await CollectProjectMetricsAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _isCollecting = false;
            }
        }

        private static async Task CollectProjectMetricsAsync()
        {
            var metrics = new Dictionary<string, string>
            {
                { "Scripts", "t:MonoScript" },
                { "Scenes", "t:Scene" },
                { "Prefabs", "t:Prefab" },
                { "Models", "t:Model" },
                { "Textures", "t:Texture" },
                { "Materials", "t:Material" },
                { "Shaders", "t:Shader" },
                { "AnimationClips", "t:AnimationClip" },
                { "AudioClips", "t:AudioClip" }
            };

            var unifiedEvent = new OVRPlugin.UnifiedEventData("PROJECT_COMPLEXITY_METRICS")
            {
                isEssential = OVRPlugin.Bool.True,
                productType = OVRPlugin.ProductType.Editor
            };

            var stopwatch = new Stopwatch();
            long totalFindAssetsTimeMs = 0;
            int totalAssetsCount = 0;

            foreach (var metric in metrics)
            {
                stopwatch.Restart();
                string[] assetGuids = AssetDatabase.FindAssets(metric.Value, new[] { "Assets" });
                stopwatch.Stop();

                unifiedEvent.SetMetadata($"{metric.Key}Count", assetGuids.Length);
                totalAssetsCount += assetGuids.Length;
                totalFindAssetsTimeMs += stopwatch.ElapsedMilliseconds;
                await Task.Yield();
            }

            unifiedEvent.SetMetadata("TotalAssetsCount", totalAssetsCount);
            unifiedEvent.SetMetadata("FindAssetsTimeMs", totalFindAssetsTimeMs);

            unifiedEvent.Send();
        }
    }
}
