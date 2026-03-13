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
using Meta.XR.Editor.Settings;

namespace Meta.XR.Editor.ToolingSupport
{
    internal class ToolUsage
    {
        private readonly UserInt _usageCount;
        private readonly UserString _lastUsageDate;
        private readonly UserInt _lastUsedInSDKVersion;
        public const int MissingSDKVersion = -1;
        public readonly string ToolId;

        public ToolUsage(string toolId)
        {
            ToolId = toolId;

            _usageCount = new UserInt
            {
                Uid = $"UsageCount.{toolId}"
            };

            _lastUsageDate = new UserString
            {
                Uid = $"LastUsageDate.{toolId}"
            };

            _lastUsedInSDKVersion = new UserInt
            {
                Uid = $"LastUsedInSDKVersion.{toolId}",
                Default = MissingSDKVersion
            };
        }

        public void RecordUsage()
        {
            _usageCount.SetValue(_usageCount + 1);
            _lastUsageDate.SetValue(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            var sdkVersion = GetSdkVersion();
            if (sdkVersion.HasValue)
            {
                _lastUsedInSDKVersion.SetValue(sdkVersion.Value);
            }
        }

        public bool IsUsed => _usageCount > 0;

        public int TimesUsed => _usageCount;

        public int DaysSinceLastUsed
        {
            get
            {
                if (!long.TryParse(_lastUsageDate, out var lastUsageDate))
                {
                    return 0;
                }

                var storedDate = DateTimeOffset.FromUnixTimeSeconds(lastUsageDate);
                var elapsed = DateTimeOffset.UtcNow - storedDate;
                return (int)elapsed.TotalDays;
            }
        }

        public int? LastUsedInSDKVersion =>
            _lastUsedInSDKVersion == MissingSDKVersion ? null : _lastUsedInSDKVersion;

        public static int? GetSdkVersion()
        {
            var versionZero = new Version(0, 0, 0);
            if (OVRPlugin.wrapperVersion == null || OVRPlugin.wrapperVersion == versionZero)
            {
                return null;
            }

            return OVRPlugin.wrapperVersion.Minor - 32;
        }
    }
}
