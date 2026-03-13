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
using System.Linq;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.ToolingSupport;
using UnityEngine;

namespace Meta.XR.Editor.Notifications
{
    internal class Validator
    {
        private abstract class FilterChecker
        {
            public abstract bool CheckCondition(string @operator, string value);
        }

        private abstract class BaseFilterChecker<T> : FilterChecker
        {
            protected abstract T GetField();

            public override bool CheckCondition(string @operator, string value)
            {
                if (!TryParse(value, out var parsedValue))
                {
                    return false;
                }

                var field = GetField();

                return @operator switch
                {
                    "=" => Equals(field, parsedValue),
                    "!=" => !Equals(field, parsedValue),
                    ">" when field is IComparable comparableField => comparableField.CompareTo(parsedValue) > 0,
                    "<" when field is IComparable comparableField => comparableField.CompareTo(parsedValue) < 0,
                    ">=" when field is IComparable comparableField => comparableField.CompareTo(parsedValue) >= 0,
                    "<=" when field is IComparable comparableField => comparableField.CompareTo(parsedValue) <= 0,
                    _ => false
                };
            }

            private static bool TryParse(string value, out T result)
            {
                try
                {
                    if (typeof(T) == typeof(Version))
                    {
                        result = (T)(object)Version.Parse(value);
                        return true;
                    }

                    result = (T)Convert.ChangeType(value, typeof(T));
                    return true;
                }
                catch
                {
                    result = default;
                    return false;
                }
            }
        }

        private class ValueFilterChecker<T> : BaseFilterChecker<T>
        {
            private readonly T _field;

            public ValueFilterChecker(T field)
            {
                _field = field;
            }

            protected override T GetField() => _field;
        }

        private class CallbackFilterChecker<T> : BaseFilterChecker<T>
        {
            private readonly Func<T> _fieldCallback;

            public CallbackFilterChecker(Func<T> fieldCallback)
            {
                _fieldCallback = fieldCallback;
            }

            protected override T GetField() => _fieldCallback();
        }

        private readonly IReadOnlyDictionary<string, FilterChecker> _checkers;

        private static IEnumerable<(string Key, FilterChecker Checker)> GetFiltersForToolUsage(
            ToolUsage toolUsage)
        {
            yield return (
                $"{toolUsage.ToolId}_is_used",
                new CallbackFilterChecker<bool>(() => toolUsage.IsUsed));

            yield return (
                $"{toolUsage.ToolId}_times_used",
                new CallbackFilterChecker<int>(() => toolUsage.TimesUsed));

            yield return (
                $"{toolUsage.ToolId}_days_since_last_used",
                new CallbackFilterChecker<int>(() => toolUsage.DaysSinceLastUsed));

            yield return (
                $"{toolUsage.ToolId}_last_used_in_sdk_version",
                new CallbackFilterChecker<int>(() => toolUsage.LastUsedInSDKVersion ?? ToolUsage.MissingSDKVersion));
        }

        public Validator()
        {
            var checkers = new Dictionary<string, FilterChecker>
            {
                { "platform", new ValueFilterChecker<string>(Application.platform.ToString()) },
                { "unity_version", new ValueFilterChecker<Version>(ParseUnityVersion(Application.unityVersion)) },
                {
                    "number_active_sessions", new CallbackFilterChecker<int>(() => UsageSettings.NumberOfActiveSessions)
                },
                {
                    "days_since_activation", new CallbackFilterChecker<int>(() =>
                    {
                        if (!long.TryParse(UsageSettings.UserActivationDate, out var activationTime))
                        {
                            return 0;
                        }

                        var storedDate = DateTimeOffset.FromUnixTimeSeconds(activationTime);
                        var elapsed = DateTimeOffset.UtcNow - storedDate;
                        return (int)elapsed.TotalDays;
                    })
                }
            };

            var sdkVersion = ToolUsage.GetSdkVersion();
            if (sdkVersion.HasValue)
            {
                checkers.Add("sdk_version", new ValueFilterChecker<int>(sdkVersion.Value));
            }

            foreach (var tool in ToolRegistry.Registry)
            {
                foreach (var (key, checker) in GetFiltersForToolUsage(tool.Usage))
                {
                    if (checkers.TryAdd(key, checker))
                    {
                        continue;
                    }
                }
            }

            _checkers = checkers;
        }

        public bool ValidateFilter(NotificationFilter filter)
        {
            if (string.IsNullOrEmpty(filter.field) || string.IsNullOrEmpty(filter.@operator))
            {
                return false;
            }

            if (!_checkers.TryGetValue(filter.field, out var checker))
            {
                return false;
            }

            try
            {
                return checker.CheckCondition(filter.@operator, filter.value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Version ParseUnityVersion(string versionString)
        {
            try
            {
                var versionParts = versionString.Split('.');

                if (versionParts.Length < 2)
                {
                    return new Version(0, 0, 0);
                }

                if (!int.TryParse(versionParts[0], out var major) || !int.TryParse(versionParts[1], out var minor))
                {
                    return new Version(0, 0, 0);
                }

                var patch = 0;
                if (versionParts.Length > 2)
                {
                    var patchString = new string(versionParts[2].TakeWhile(char.IsDigit).ToArray());
                    if (!string.IsNullOrEmpty(patchString))
                    {
                        int.TryParse(patchString, out patch);
                    }
                }

                return new Version(major, minor, patch);
            }
            catch (Exception)
            {
                return new Version(0, 0, 0);
            }
        }
    }
}
