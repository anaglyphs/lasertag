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
using Meta.XR.Editor.UserInterface;
using UnityEngine;
using Meta.XR.Editor.RemoteContent;

namespace Meta.XR.Guides.Editor.About
{
    internal class ChangelogFetcher
    {
        private readonly SortedDictionary<Version, ChangelogEntry> _changelogDictionary =
            new(Comparer<Version>.Create((x, y) => y.CompareTo(x)));

        public IEnumerable<Version> AvailableVersions => _changelogDictionary.Keys;

        public Version LatestVersion => _changelogDictionary.Any() ? _changelogDictionary.First().Key : null;

        public ChangelogEntry GetEntry(Version version) => _changelogDictionary[version];

        private readonly string _packageName;
        private readonly int? _maxVersions;

        public ChangelogFetcher(string packageName, int? maxVersions = null)
        {
            _packageName = packageName;
            _maxVersions = maxVersions;
        }

        public async Task<bool> Fetch(bool forceRedownload = false)
        {
            var downloader = new RemoteJsonContentDownloader(cacheFile: $"{_packageName}_changelog.json",
                    url:
                    $"https://developers.meta.com/horizon/changelog/package/{_packageName}{(_maxVersions.HasValue ? $"?max_versions={_maxVersions}" : "")}")
                .WithCacheDuration(TimeSpan.FromHours(6))
                .WithMachineIdUrlParameter()
                .WithSDKVersionUrlParameter();

            if (forceRedownload)
            {
                downloader.ClearCache();
            }

            var result = await downloader.Fetch();
            return result.IsSuccess && ParseJsonData(result.Content);
        }

        public struct ChangelogEntry
        {
            public IEnumerable<IUserInterfaceItem> ChangelogUIItems;
            public IEnumerable<IUserInterfaceItem> WhatsNewUIItems;
        }

        internal bool ParseJsonData(string jsonData)
        {
            ChangelogSerializedData[] response;
            var success = true;

            try
            {
                var parsedData = JsonUtility.FromJson<ChangelogsSerializedData>($"{{ \"changelogs\": {jsonData} }}");
                response = parsedData.changelogs;
            }
            catch (Exception)
            {
                response = Array.Empty<ChangelogSerializedData>();
                success = false;
            }

            _changelogDictionary.Clear();
            foreach (var data in response)
            {
                if (Version.TryParse(data.version, out var v))
                {
                    _changelogDictionary.Add(v, new ChangelogEntry
                    {
                        ChangelogUIItems = MarkdownUtils.GetGuideItemsForMarkdownText(data.release_notes),
                        WhatsNewUIItems = ExtractWhatsNewSection(data.release_notes)
                    });
                }
            }

            return success;
        }

        [Serializable]
        private struct ChangelogsSerializedData
        {
            public ChangelogSerializedData[] changelogs;
        }

        [Serializable]
        private struct ChangelogSerializedData
        {
            public string release_notes;
            public string version;
        }

        private static IEnumerable<IUserInterfaceItem> ExtractWhatsNewSection(string input)
        {
            const string pattern = @"(## \*\*What's New\*\*\n.*?)(?=\n## |\z)";

            var match = Regex.Match(input, pattern, RegexOptions.Singleline);
            if (!match.Success)
            {
                return Enumerable.Empty<IUserInterfaceItem>();
            }

            var content = match.Groups[1].Value.Trim();
            return MarkdownUtils.GetGuideItemsForMarkdownText(content);
        }
    }
}
