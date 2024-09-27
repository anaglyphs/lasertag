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

#if !(UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || (UNITY_ANDROID && !UNITY_EDITOR))
#define OVRPLUGIN_UNSUPPORTED_PLATFORM
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Assets.Oculus.VR.Editor;
using Meta.XR.Editor.Tags;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.Editor
{
    [InitializeOnLoad]
    internal static class BlocksContentManager
    {
        private const double CacheDurationInHours = 6;
        private static string CacheDirectory => Path.Combine(Path.GetTempPath(), "Meta", "Unity", "Editor");
        private static string CacheFilePath => Path.Combine(CacheDirectory, "bb_content.json");

        private static string LastDownloadTimestampKey =>
            $"BlocksContentManager.LastDownloadTimestamp.{SdkVersion.GetValueOrDefault(0)}";

        private static BlockData[] _contentFilter;
        private static OVRPlatformTool.EditorCoroutine _editorCoroutine;

#if OVRPLUGIN_UNSUPPORTED_PLATFORM
        private static int? SdkVersion => null;
#else
        private static int? SdkVersion => OVRPlugin.version.Minor - 32;
#endif

        static BlocksContentManager()
        {
            if (HasValidCache())
            {
                if (LoadCache())
                {
                    return;
                }

                ClearCache();
            }

            StartDownload(onComplete: RecordCache);
        }

        #region Data Caching

        private static bool HasValidCache()
        {
            var lastDownloadTimestamp = SessionState.GetString(LastDownloadTimestampKey, null);
            if (!File.Exists(CacheFilePath) || string.IsNullOrEmpty(lastDownloadTimestamp))
            {
                return false;
            }

            var lastDownloadTime = DateTime.Parse(lastDownloadTimestamp, CultureInfo.InvariantCulture);
            return DateTime.Now - lastDownloadTime <= TimeSpan.FromHours(CacheDurationInHours);
        }

        private static bool LoadCache()
        {
            return SetContentJsonData(File.ReadAllText(CacheFilePath));
        }

        private static void RecordCache(string jsonData)
        {
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllText(CacheFilePath, jsonData);
            SessionState.SetString(LastDownloadTimestampKey, DateTime.Now.ToString(CultureInfo.InvariantCulture));
        }

        private static void ClearCache()
        {
            if (File.Exists(CacheFilePath))
            {
                File.Delete(CacheFilePath);
            }

            SessionState.EraseString(LastDownloadTimestampKey);
        }

        #endregion

        #region Data Parsing

        // ReSharper disable InconsistentNaming
        [Serializable]
        internal struct BlockData
        {
            public string id;
            public string blockName;
            public string description;
            public string[] tags;
        }

        [Serializable]
        internal struct BlockDataResponse
        {
            public BlockData[] content;
        }
        // ReSharper restore InconsistentNaming

        internal static BlockData[] ParseJsonData(string jsonData)
        {
            try
            {
                var response = JsonUtility.FromJson<BlockDataResponse>(jsonData);
                return response.content;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Blocks TextureContent

        internal static bool SetContentJsonData(string jsonData)
        {
            _contentFilter = ParseJsonData(jsonData);
            return _contentFilter != null;
        }

        internal static void ClearContent()
        {
            _contentFilter = null;
            ClearCache();
        }

        public static IReadOnlyList<BlockBaseData> FilterBlockWindowContent(IReadOnlyList<BlockBaseData> content)
        {
            return FilterBlockWindowContent(content, _contentFilter);
        }

        private static void ClearOverrides(IEnumerable<BlockBaseData> content)
        {
            foreach (var block in content)
            {
                block.BlockName.RemoveOverride();
                block.Description.RemoveOverride();
                block.OverridableTags.RemoveOverride();
            }
        }

        internal static IReadOnlyList<BlockBaseData> FilterBlockWindowContent(IReadOnlyList<BlockBaseData> content,
            BlockData[] contentFilter)
        {
            if (contentFilter == null)
            {
                ClearOverrides(content);
                return content;
            }

            var contentFilterDictionary = contentFilter
                .Select((value, index) => new { value, index })
                .ToDictionary(pair => pair.value.id, pair => new { pair.value, pair.index });

            var filteredContent = content
                .Where(block => contentFilterDictionary.ContainsKey(block.Id))
                .OrderBy(block => contentFilterDictionary[block.Id].index)
                .ToList();

            foreach (var blockBaseData in filteredContent)
            {
                blockBaseData.BlockName.SetOverride(contentFilterDictionary[blockBaseData.Id].value.blockName);
                blockBaseData.Description.SetOverride(contentFilterDictionary[blockBaseData.Id].value.description);
                blockBaseData.OverridableTags.SetOverride(GenerateTagArrayFromTags(contentFilterDictionary[blockBaseData.Id].value.tags));
            }

            return filteredContent;
        }

        private static TagArray GenerateTagArrayFromTags(string[] tags)
        {
            if (tags == null)
            {
                return null;
            }

            var tagArray = new TagArray();
            tagArray.Add(tags.Select(tag => new Tag(tag)));
            return tagArray;
        }

        #endregion

        #region Data Download

        private static IEnumerator DownloadContent(Action<string> onComplete)
        {
            using var marker = new OVRTelemetryMarker(OVRTelemetryConstants.BB.MarkerId.DownloadContent);

            var path = "https://www.facebook.com/building-blocks-content";

            if (SdkVersion.HasValue)
            {
                path += $"?sdk_version={SdkVersion.Value}";
            }

            using var request = UnityWebRequest.Get(path);

            yield return request.SendWebRequest();

            while (request.result == UnityWebRequest.Result.InProgress)
            {
                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                yield break;
            }

            var jsonData = request.downloadHandler.text;
            SetContentJsonData(jsonData);
            onComplete?.Invoke(jsonData);
        }

        internal static void StartDownload(Action<string> onComplete = null)
        {
            if (_editorCoroutine != null && !_editorCoroutine.GetCompleted())
            {
                _editorCoroutine.Stop();
            }

            _editorCoroutine = OVRPlatformTool.EditorCoroutine.Start(DownloadContent(onComplete));
        }

        #endregion
    }
}
