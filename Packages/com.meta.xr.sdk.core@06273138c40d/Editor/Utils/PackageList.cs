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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager.UI;
using UnityEditor.Search;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Meta.XR.Editor.Utils
{
    [InitializeOnLoad]
    internal static class PackageList
    {
        private static ListRequest _packageManagerListRequest;
        private static Dictionary<string, PackageInfo> _packagesDictionary;
        public static event Action OnPackageListRefreshed;

        static PackageList()
        {
            Meta.XR.Editor.Callbacks.InitializeOnLoad.Register(Initialize);
        }

        private static async void Initialize()
        {
            _packageManagerListRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
            Events.registeringPackages += RegisteringPackagesEventHandler;
            await WaitUntil(() => PackageManagerListAvailable);
            OnPackageListRefreshed?.Invoke();
        }

        private static async Task WaitUntil(Func<bool> predicate, int sleep = 50)
        {
            while (!predicate())
            {
                await Task.Delay(sleep);
            }
        }

        internal struct PackageData
        {
            public string Name;
            public string Version;
            public string SampleName;
        }

        private static void RegisteringPackagesEventHandler(PackageRegistrationEventArgs args)
        {
            if (!PackageManagerListAvailable || _packageManagerListRequest.Result == null)
            {
                return;
            }

            _packagesDictionary ??= _packageManagerListRequest.Result.ToDictionary(package => package.name);

            foreach (var addedPackage in args.added)
            {
                _packagesDictionary.Add(addedPackage.name, addedPackage);
            }

            foreach (var removedPackage in args.removed)
            {
                _packagesDictionary.Remove(removedPackage.name);
            }

            foreach (var changedTo in args.changedTo)
            {
                _packagesDictionary[changedTo.name] = changedTo;
            }
        }

        public static bool PackageManagerListAvailable => _packageManagerListRequest is { Status: StatusCode.Success };

        public static PackageInfo GetPackage(string packageId)
        {
            if (!PackageManagerListAvailable || _packageManagerListRequest.Result == null)
            {
                return null;
            }

            _packagesDictionary ??= _packageManagerListRequest.Result.ToDictionary(package => package.name);

            var package = ParsePackageId(packageId);
            return _packagesDictionary.GetValueOrDefault(package.Name);
        }

        internal static PackageData ParsePackageId(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                throw new ArgumentNullException();
            }

            var containsSampleName = packageId.Contains(":");
            var containsVersion = packageId.Contains("@");

            return (containsSampleName, containsVersion) switch
            {
                (true, true) => throw new ArgumentException(
                    "Setting both sample name and version in the packageId is not supported."),
                (true, false) => ParseWithSampleName(packageId),
                (false, true) => ParseWithVersion(packageId),
                (false, false) => new PackageData { Name = packageId }
            };

            PackageData ParseWithSampleName(string s)
            {
                var (packageName, sampleName) = SplitStringBySeparator(s, ":");

                if (string.IsNullOrEmpty(sampleName))
                {
                    throw new ArgumentException($"{nameof(sampleName)} cannot be null or empty");
                }

                return new PackageData
                {
                    Name = packageName,
                    SampleName = sampleName,
                };
            }

            PackageData ParseWithVersion(string s)
            {
                var (packageName, packageVersion) = SplitStringBySeparator(s, "@");

                if (string.IsNullOrEmpty(packageVersion))
                {
                    throw new ArgumentException($"{nameof(packageVersion)} cannot be null or empty");
                }

                return new PackageData
                {
                    Name = packageName,
                    Version = packageVersion
                };
            }

            (string, string) SplitStringBySeparator(string s, string separator)
            {
                var parts = s.Split(separator);
                var p1 = parts[0];
                var p2 = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? parts[1] : null;
                return (p1, p2);
            }
        }

        public static bool IsPackageInstalled(string packageId) => GetPackage(packageId) != null;

        public static bool IsPackageInstalledWithValidVersion(string packageId)
        {
            var expectedData = ParsePackageId(packageId);
            var installedPacked = GetPackage(packageId);

            if (installedPacked == null)
            {
                return false;
            }

            return ValidatePackageVersion() && ValidateSample();

            bool ValidatePackageVersion()
            {
                return string.IsNullOrEmpty(expectedData.Version) ||
                       IsVersionValid(expectedData.Version, installedPacked.version);
            }

            bool ValidateSample()
            {
                if (string.IsNullOrEmpty(expectedData.SampleName))
                {
                    return true;
                }

                return Sample.FindByPackage(installedPacked.name, installedPacked.version)
                    .Any(sample => sample.displayName == expectedData.SampleName && sample.isImported);
            }
        }

        internal static bool IsValidPackageName(string packageName)
        {
            const string pattern =
                @"^([a-z0-9]+(-[a-z0-9]+)*\.)+[a-z]{2,}(@([0-9]+\.){2}[0-9]+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?)?$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            return regex.IsMatch(packageName);
        }

        internal static bool IsValidPackageId(string packageId)
        {
            try
            {
                var package = ParsePackageId(packageId);

                if (!IsValidPackageName(package.Name))
                {
                    return false;
                }

                if (package.Version != null)
                {
                    return IsValidSemanticVersion(package.Version);
                }

                return package.SampleName == null || package.SampleName.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool IsValidSemanticVersion(string version)
        {
            return !string.IsNullOrEmpty(version) && Version.TryParse(NormalizeVersion(version), out _);
        }

        internal static bool IsVersionValid(string expectedVersion, string actualVersion)
        {
            actualVersion = NormalizeInternalPackageVersion(actualVersion);

            if (!IsValidSemanticVersion(expectedVersion) || !IsValidSemanticVersion(actualVersion))
                return false;

            var normalizedExpectedVersion = NormalizeVersion(expectedVersion);
            var normalizedActualVersion = NormalizeVersion(actualVersion);

            if (!Version.TryParse(normalizedExpectedVersion, out var expected) ||
                !Version.TryParse(normalizedActualVersion, out var actual))
            {
                return false;
            }

            if (expectedVersion.StartsWith("~"))
            {
                return expected.Major == actual.Major && expected.Minor == actual.Minor &&
                       actual.Build >= expected.Build;
            }

            if (expectedVersion.StartsWith("^"))
            {
                return expected.Major == actual.Major && actual.Minor >= expected.Minor &&
                       (expected.Minor != actual.Minor || actual.Build >= expected.Build);
            }

            if (expectedVersion.StartsWith(">="))
            {
                return actual >= expected;
            }

            return expected.Equals(actual);
        }

        private static string NormalizeVersion(string version)
        {
            var prefixes = new[] { "^", "~", ">=" };

            foreach (var prefix in prefixes)
            {
                if (version.StartsWith(prefix))
                {
                    return version[prefix.Length..];
                }
            }

            return version;
        }

        private static string NormalizeInternalPackageVersion(string version)
        {
            if (version == null)
            {
                return null;
            }

            var dashIndex = version.IndexOf('-');
            return dashIndex >= 0 ? version[..dashIndex] : version;
        }

        internal static int? ComputePackageVersion(string packageName)
            => ComputePackageVersion(packageName, packageInfo => packageInfo.version);

        internal static int? ComputeLatestPackageVersion(string packageName)
            => ComputePackageVersion(packageName, packageInfo => packageInfo.versions.latest);

        private static int? ComputePackageVersion(string packageName,
            Func<PackageInfo, string> extractVersionFromPackageInfo)
        {
            // Returning null as an indicator that it has not been retrieved yet
            if (!PackageManagerListAvailable) return null;

            var version = 0;
            var package = GetPackage(packageName);

            if (package == null) return version;

            var versionParts = extractVersionFromPackageInfo.Invoke(package).Split('.');
            if (versionParts.Length > 0)
            {
                int.TryParse(versionParts[0], out version);
            }

            return version;
        }
    }
}
