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
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks
{
    public class SharedSpatialAnchorCore : SpatialAnchorCoreBuildingBlock
    {
        public UnityEvent<List<OVRSpatialAnchor>, OVRSpatialAnchor.OperationResult> OnSpatialAnchorsShareCompleted
        {
            get => _onSpatialAnchorsShareCompleted;
            set => _onSpatialAnchorsShareCompleted = value;
        }
        public UnityEvent<List<OVRSpatialAnchor>, OVRSpatialAnchor.OperationResult> OnSharedSpatialAnchorsLoadCompleted
        {
            get => _onSharedSpatialAnchorsLoadCompleted;
            set => _onSharedSpatialAnchorsLoadCompleted = value;
        }

        [SerializeField] private UnityEvent<List<OVRSpatialAnchor>, OVRSpatialAnchor.OperationResult> _onSpatialAnchorsShareCompleted;
        [SerializeField] private UnityEvent<List<OVRSpatialAnchor>, OVRSpatialAnchor.OperationResult> _onSharedSpatialAnchorsLoadCompleted;

        private Action<OVRSpatialAnchor.OperationResult, IEnumerable<OVRSpatialAnchor>> _onShareCompleted;

        private void Start() => _onShareCompleted += OnShareCompleted;

        public async new void InstantiateSpatialAnchor(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                prefab = new GameObject("Shared Spatial Anchor");
            }

            var anchorGameObject = Instantiate(prefab, position, rotation);
            var spatialAnchor = anchorGameObject.AddComponent<OVRSpatialAnchor>();
            await InitSpatialAnchor(spatialAnchor);
        }

        private async Task InitSpatialAnchor(OVRSpatialAnchor anchor)
        {
            await WaitForInit(anchor);
            if (Result == OVRSpatialAnchor.OperationResult.Failure)
            {
                OnAnchorCreateCompleted?.Invoke(anchor, Result);
                return;
            }

            await SaveAsync(anchor);
            if (Result.IsError())
            {
                OnAnchorCreateCompleted?.Invoke(anchor, Result);
                return;
            }

            OnAnchorCreateCompleted?.Invoke(anchor, Result);
        }

        public new void LoadAndInstantiateAnchors(GameObject prefab, List<Guid> uuids)
        {
            if (uuids == null)
            {
                throw new ArgumentNullException();
            }

            if (uuids.Count == 0)
            {
                throw new ArgumentException($"[{nameof(SpatialAnchorCoreBuildingBlock)}] Uuid list is empty.");
            }

            LoadSharedSpatialAnchorsRoutine(prefab, uuids);
        }

        private async void LoadSharedSpatialAnchorsRoutine(GameObject prefab, IEnumerable<Guid> uuids)
        {

            // Load unbounded anchors
            using var unboundAnchorsPoolHandle =
                new OVRObjectPool.ListScope<OVRSpatialAnchor.UnboundAnchor>(out var unboundAnchors);
            var result = await OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(uuids, unboundAnchors);
            if (!result.Success)
            {
                Debug.LogWarning($"[{nameof(SharedSpatialAnchorCore)}] Failed to load the shared spatial anchors: {result.Status}");
                OnSpatialAnchorsShareCompleted?.Invoke(null, result.Status);
                return;
            }
            if (unboundAnchors.Count == 0)
            {
                Debug.LogWarning($"[{nameof(SharedSpatialAnchorCore)}] There's no shared spatial anchors being loaded.");
                OnSpatialAnchorsShareCompleted?.Invoke(null, result.Status);
                return;
            }

            // Localize the anchors
            using var _ = new OVRObjectPool.ListScope<OVRSpatialAnchor>(out var loadedAnchors);
            foreach (var unboundAnchor in unboundAnchors)
            {
                if (!unboundAnchor.Localized)
                {
                    if (!await unboundAnchor.LocalizeAsync())
                    {
                        Debug.LogWarning($"[{nameof(SharedSpatialAnchorCore)}] Failed to localize the anchor. Uuid: {unboundAnchor.Uuid}");
                        continue;
                    }
                }

                var isPoseValid = unboundAnchor.TryGetPose(out var pose);
                if (!isPoseValid)
                {
                    Debug.LogWarning("Unable to acquire initial anchor pose. Instantiating prefab at the origin.");
                }

                var spatialAnchorGo = isPoseValid
                    ? Instantiate(prefab, pose.position, pose.rotation)
                    : Instantiate(prefab);
                var anchor = spatialAnchorGo.AddComponent<OVRSpatialAnchor>();
                unboundAnchor.BindTo(anchor);
                loadedAnchors.Add(anchor);
            }

            OnSharedSpatialAnchorsLoadCompleted?.Invoke(new List<OVRSpatialAnchor>(loadedAnchors), result.Status);
        }

        public void ShareSpatialAnchors(List<OVRSpatialAnchor> anchors, List<OVRSpaceUser> users)
        {
            if (anchors == null || users == null)
            {
                throw new ArgumentNullException();
            }

            if (anchors.Count == 0 || users.Count == 0)
            {
                throw new ArgumentException($"[{nameof(SharedSpatialAnchorCore)}] Anchors or users cannot be zero.");
            }

            OVRSpatialAnchor.ShareAsync(anchors, users).ContinueWith(_onShareCompleted, anchors);
        }

        private void OnShareCompleted(OVRSpatialAnchor.OperationResult result, IEnumerable<OVRSpatialAnchor> anchors)
        {
            if (result != OVRSpatialAnchor.OperationResult.Success)
            {
                OnSpatialAnchorsShareCompleted?.Invoke(null, result);
                return;
            }

            using var _ = new OVRObjectPool.ListScope<OVRSpatialAnchor>(out var sharedAnchors);
            sharedAnchors.AddRange(anchors);

            OnSpatialAnchorsShareCompleted?.Invoke(new List<OVRSpatialAnchor>(sharedAnchors), OVRSpatialAnchor.OperationResult.Success);
        }

        private void OnDestroy() => _onShareCompleted -= OnShareCompleted;
    }
}
