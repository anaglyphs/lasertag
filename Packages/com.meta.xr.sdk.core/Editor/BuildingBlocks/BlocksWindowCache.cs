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
using System.Linq;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    internal static class BlocksWindowCache
    {
        private static readonly Dictionary<string, BlockDataCache> _cache = new();

        // Cached list of BuildingBlocks in scene - reset once per frame to avoid repeated FindObjectsByType calls
        private static BuildingBlock[] _cachedBuildingBlocksInScene;
        private static bool _buildingBlocksCacheDirty = true;

        /// <summary>
        /// Gets the cached array of BuildingBlocks in the scene.
        /// This avoids expensive FindObjectsByType calls for each block.
        /// </summary>
        internal static BuildingBlock[] GetCachedBuildingBlocksInScene()
        {
            if (_buildingBlocksCacheDirty || _cachedBuildingBlocksInScene == null)
            {
                _cachedBuildingBlocksInScene = Object.FindObjectsByType<BuildingBlock>(FindObjectsSortMode.None);
                _buildingBlocksCacheDirty = false;
            }
            return _cachedBuildingBlocksInScene;
        }

        /// <summary>
        /// Marks the building blocks cache as dirty, causing it to refresh on next access.
        /// Should be called when the scene changes (blocks added/removed).
        /// </summary>
        internal static void InvalidateBuildingBlocksCache()
        {
            _buildingBlocksCacheDirty = true;
        }

        /// <summary>
        /// Checks if a block with the given ID is present in the scene using the cached blocks list.
        /// </summary>
        internal static bool IsBlockPresentInSceneCached(string blockId)
        {
            var blocks = GetCachedBuildingBlocksInScene();
            return blocks.Any(x => x.BlockId == blockId);
        }

        /// <summary>
        /// Counts the number of blocks with the given ID in the scene using the cached blocks list.
        /// </summary>
        internal static int CountBlocksInSceneCached(string blockId)
        {
            var blocks = GetCachedBuildingBlocksInScene();
            return blocks.Count(x => x.BlockId == blockId);
        }

        public class BlockDataCache
        {
            private readonly BlockBaseData _block;
            private int? _numberOfBlocksInScene;
            private bool? _isInteractable;
            private bool? _hasMissingPackageDependencies;
            private bool? _isInstallable;
            private bool? _isSingletonAndAlreadyPresent;

            public int NumberOfBlocksInScene => _numberOfBlocksInScene ??= CountBlocksInSceneCached(_block.Id);

            /// <summary>
            /// Gets whether the block is interactable using cached data.
            /// </summary>
            public bool IsInteractable => _isInteractable ??= _block.IsInteractable;

            public bool HasMissingPackageDependencies => _hasMissingPackageDependencies ??=
                (_block as BlockData)?.HasMissingPackageDependencies ?? false;

            /// <summary>
            /// Gets whether the block is installable using cached data.
            /// </summary>
            public bool IsInstallable => _isInstallable ??= _block.IsInstallable;

            /// <summary>
            /// Gets whether the block is a singleton and already present using cached building blocks data.
            /// This avoids repeated FindObjectsByType calls.
            /// </summary>
            public bool IsSingletonAndAlreadyPresent
            {
                get
                {
                    if (_isSingletonAndAlreadyPresent.HasValue)
                        return _isSingletonAndAlreadyPresent.Value;

                    var blockData = _block as BlockData;
                    if (blockData == null)
                    {
                        _isSingletonAndAlreadyPresent = false;
                        return false;
                    }

                    // Use cached building blocks list to check if singleton block is already present
                    _isSingletonAndAlreadyPresent = blockData.IsSingleton && IsBlockPresentInSceneCached(_block.Id);
                    return _isSingletonAndAlreadyPresent.Value;
                }
            }

            public BlockDataCache(BlockBaseData block)
            {
                _block = block;
            }

            public void Reset()
            {
                _numberOfBlocksInScene = null;
                _isInteractable = null;
                _hasMissingPackageDependencies = null;
                _isInstallable = null;
                _isSingletonAndAlreadyPresent = null;
            }
        }

        public static BlockDataCache GetCache(this BlockBaseData block)
        {
            if (_cache.TryGetValue(block.Id, out var cache)) return cache;

            cache = new BlockDataCache(block);
            _cache.Add(block.Id, cache);
            return cache;
        }

        /// <summary>
        /// Resets all per-block caches and invalidates the building blocks cache.
        /// Should be called at the start of each frame/layout event.
        /// </summary>
        public static void ResetAllCaches()
        {
            foreach (var cache in _cache.Values)
            {
                cache.Reset();
            }
            InvalidateBuildingBlocksCache();
        }
    }
}
