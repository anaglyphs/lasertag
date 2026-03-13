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
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.MetaWand.Editor
{
    [Serializable]
    internal class MetaAssetRegistryData
    {
        [SerializeField]
        public Dictionary<string, MetaAssetInfo> RegisteredAssets = new();
    }

    [Serializable]
    internal class MetaAssetInfo
    {
        [SerializeField]
        public string AssetGuid;

        [SerializeField]
        public string AssetId;

        [SerializeField]
        public string AssetType;

        [SerializeField]
        public bool IsPreGen;

        [SerializeField]
        public long RegistrationTimestamp;

        [SerializeField] public string PromptId;
    }

    internal static class MetaAssetRegistry
    {
        private static readonly string RegistryPath = Path.Combine("UserSettings", "MetaAssetRegistry.json");
        private static MetaAssetRegistryData _registry;
        private static readonly object LockObject = new object();

        private static MetaAssetRegistryData Registry
        {
            get
            {
                if (_registry == null)
                {
                    LoadRegistry();
                }
                return _registry;
            }
        }

        private static void LoadRegistry()
        {
            lock (LockObject)
            {
                if (File.Exists(RegistryPath))
                {
                    try
                    {
                        var json = File.ReadAllText(RegistryPath);
                        _registry = JsonUtility.FromJson<MetaAssetRegistryData>(json);
                    }
                    catch (Exception)
                    {
                        _registry = new MetaAssetRegistryData();
                    }
                }
                else
                {
                    _registry = new MetaAssetRegistryData();
                }
            }
        }

        private static void SaveRegistry()
        {
            lock (LockObject)
            {
                try
                {
                    var json = JsonUtility.ToJson(_registry, true);
                    var directory = Path.GetDirectoryName(RegistryPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(RegistryPath, json);
                }
                catch (Exception)
                {
                }
            }
        }

        public static void RegisterAsset(string assetGuid, string assetId, string assetType, bool isPreGen, string promptId)
        {
            if (string.IsNullOrEmpty(assetGuid))
            {
                return;
            }

            if (Registry.RegisteredAssets.ContainsKey(assetGuid))
            {
                return;
            }

            var assetInfo = new MetaAssetInfo
            {
                AssetGuid = assetGuid,
                AssetId = assetId,
                AssetType = assetType,
                IsPreGen = isPreGen,
                RegistrationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                PromptId = promptId
            };

            Registry.RegisteredAssets.Add(assetGuid, assetInfo);
            SaveRegistry();

        }

        public static MetaAssetInfo GetAssetInfo(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid) || !Registry.RegisteredAssets.TryGetValue(assetGuid, out var assetInfo))
            {
                return null;
            }

            return assetInfo;
        }

        public static void UnregisterAsset(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid))
            {
                return;
            }

            var removed = Registry.RegisteredAssets.Remove(assetGuid);
            if (!removed) return;

            SaveRegistry();
        }
    }
}
