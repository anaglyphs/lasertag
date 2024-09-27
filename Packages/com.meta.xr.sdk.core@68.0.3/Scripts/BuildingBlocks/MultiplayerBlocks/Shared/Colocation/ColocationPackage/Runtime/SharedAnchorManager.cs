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
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Meta.XR.MultiplayerBlocks.Colocation
{
    internal class SharedAnchorManager
    {
        /// <summary>
        ///   Handles interacting with the SharedSpatialAnchorCore API to create, save, localize, and share a OVRSpatialAnchor
        /// </summary>
        private readonly List<OVRSpatialAnchor> _localAnchors = new();

        private readonly List<OVRSpatialAnchor> _sharedAnchors = new();

        private readonly HashSet<OVRSpaceUser> _userShareList = new();

        private const int SaveAnchorWaitTimeThreshold = 10000;
        private bool _saveAnchorSaveToCloudIsSuccessful;

        private const int ShareAnchorWaitTimeThreshold = 10000;
        private bool _shareAnchorIsSuccessful;

        private const int RetrieveAnchorWaitTimeThreshold = 10000;
        private bool _retrieveAnchorIsSuccessful;

        private List<Task> _localizationTasks;
        private List<TaskCompletionSource<bool>> _localizationTcsList;

        public GameObject AnchorPrefab { get; set; }
        public IReadOnlyList<OVRSpatialAnchor> LocalAnchors => _localAnchors;
        private SharedSpatialAnchorCore _ssaCore;

        public SharedAnchorManager(SharedSpatialAnchorCore ssaCore)
        {
            _ssaCore = ssaCore;
        }

        public async Task<(OVRSpatialAnchor, OVRSpatialAnchor.OperationResult)> CreateAnchor(Vector3 position, Quaternion orientation)
        {
            Logger.Log($"{nameof(SharedAnchorManager)}: Attempt to InstantiateAnchor", LogLevel.Verbose);

            var (anchor, result) = await AnchorCreationTask(position, orientation);
            if (!anchor || !anchor.Created || result != OVRSpatialAnchor.OperationResult.Success)
            {
                Logger.Log($"{nameof(SharedAnchorManager)}: Anchor creation failed with result: {result}", LogLevel.SharedSpatialAnchorsError);
                return (null, result);
            }

            Logger.Log($"{nameof(SharedAnchorManager)}: Created anchor with id {anchor.Uuid}", LogLevel.Info);

            _localAnchors.Add(anchor);
            return (anchor, result);
        }

        private async Task<(OVRSpatialAnchor, OVRSpatialAnchor.OperationResult)> AnchorCreationTask(Vector3 position, Quaternion orientation)
        {
            var task = new TaskCompletionSource<(OVRSpatialAnchor, OVRSpatialAnchor.OperationResult)>();
            _ssaCore.InstantiateSpatialAnchor(AnchorPrefab, position, orientation);
            _ssaCore.OnAnchorCreateCompleted.AddListener((anchor, result) =>
            {
                _saveAnchorSaveToCloudIsSuccessful = true;
                task.TrySetResult((anchor, result));
            });

            _saveAnchorSaveToCloudIsSuccessful = false;
            CheckIfSavingAnchorsServiceHung();

            return await task.Task;
        }

        private async void CheckIfSavingAnchorsServiceHung()
        {
            await Task.Delay(SaveAnchorWaitTimeThreshold);
            if (!_saveAnchorSaveToCloudIsSuccessful)
            {
                Logger.Log(
                    $"SharedAnchorManager: It has been {SaveAnchorWaitTimeThreshold}ms since attempting to save to the cloud. Anchors service may have failed",
                    LogLevel.Warning);
            }
        }

        public async Task<IReadOnlyList<OVRSpatialAnchor>> RetrieveAnchors(List<Guid> anchorIds)
        {
            Assert.IsTrue(anchorIds.Count <= OVRPlugin.SpaceFilterInfoIdsMaxSize,
                "SpaceFilterInfoIdsMaxSize exceeded.");

            var task = new TaskCompletionSource<IReadOnlyList<OVRSpatialAnchor>>();
            _retrieveAnchorIsSuccessful = false;
            CheckIfRetrievingAnchorServiceHung();
            Logger.Log($"{nameof(SharedAnchorManager)}: Querying anchors: {string.Join(", ", anchorIds)}", LogLevel.Verbose);

            _ssaCore.LoadAndInstantiateAnchors(AnchorPrefab, anchorIds);
            _ssaCore.OnSharedSpatialAnchorsLoadCompleted.AddListener((loadedAnchors, result) =>
            {
                if (result == OVRSpatialAnchor.OperationResult.Success)
                {
                    _retrieveAnchorIsSuccessful = true;
                    _sharedAnchors.AddRange(loadedAnchors);
                    task.TrySetResult(loadedAnchors);
                }
            });

            return await task.Task;
        }

        private async void CheckIfRetrievingAnchorServiceHung()
        {
            await Task.Delay(RetrieveAnchorWaitTimeThreshold);
            if (!_retrieveAnchorIsSuccessful)
            {
                Logger.Log(
                    $"{nameof(SharedAnchorManager)}: It has been {RetrieveAnchorWaitTimeThreshold}ms since attempting to retrieve anchor(s). Anchors service may have failed",
                    LogLevel.Warning);
            }
        }

        public async Task<bool> ShareAnchorsWithUser(ulong userId)
        {
            _userShareList.Add(new OVRSpaceUser(userId));

            _shareAnchorIsSuccessful = false;
            CheckIfSharingAnchorServiceHung();

            if (_localAnchors.Count == 0)
            {
                Logger.Log($"{nameof(SharedAnchorManager)}: No anchors to share.", LogLevel.Warning);
                return true;
            }

            Logger.Log($"{nameof(SharedAnchorManager)}: Sharing {_localAnchors.Count} anchors with users: {userId}",
                LogLevel.Verbose);

            var task = new TaskCompletionSource<bool>();

            var users = new List<OVRSpaceUser>();
            users.AddRange(_userShareList);
            _ssaCore.ShareSpatialAnchors(_localAnchors, users);
            _ssaCore.OnSpatialAnchorsShareCompleted.AddListener((uuids, result) =>
            {
                Logger.Log($"{nameof(SharedAnchorManager)}: result of sharing the anchor is {result}", LogLevel.Verbose);
                task.TrySetResult(result == OVRSpatialAnchor.OperationResult.Success);
                _shareAnchorIsSuccessful = true;
            });

            return await task.Task;
        }

        private async void CheckIfSharingAnchorServiceHung()
        {
            await Task.Delay(ShareAnchorWaitTimeThreshold);
            if (!_shareAnchorIsSuccessful)
            {
                Logger.Log(
                    $"{nameof(SharedAnchorManager)}: It has been {ShareAnchorWaitTimeThreshold}ms since attempting to share anchor(s). Anchors service may have failed",
                    LogLevel.Warning);
            }
        }

        public void StopSharingAnchorsWithUser(ulong userId)
        {
            _userShareList.RemoveWhere(el => el.Id == userId);
        }
    }
}
