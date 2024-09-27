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
using UnityEngine;

namespace Meta.XR.BuildingBlocks
{
    public class FollowAndCreateAnchorAtHandPositionBuildingBlock : MonoBehaviour
    {
        public GameObject AnchorPrefab
        {
            set
            {
                _anchorPrefab = value;

                if (_anchorPrefabTransform) Destroy(_anchorPrefabTransform.gameObject);
                _anchorPrefabTransform = Instantiate(AnchorPrefab, _cameraRig.rightControllerAnchor).transform;
                _anchorPrefabTransform.localPosition = Vector3.zero;
                _anchorPrefabTransform.localRotation = Quaternion.identity;

                _anchorPrefabTransform.gameObject.SetActive(enabled);
            }
            get { return _anchorPrefab; }
        }

        [Tooltip("A placeholder object to place in the anchor's position.")]
        [SerializeField]
        private GameObject _anchorPrefab;

        private OVRCameraRig _cameraRig;
        private Transform _anchorPrefabTransform;
        private SpatialAnchorCoreBuildingBlock _spatialAnchorCore;

        private void Awake()
        {
            _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            _spatialAnchorCore = SpatialAnchorCoreBuildingBlock.GetBaseInstances()[0];
            AnchorPrefab = _anchorPrefab;
        }

        /// <summary>
        /// Create an anchor.
        /// </summary>
        /// <param name="position">Position for the new anchor.</param>
        /// <param name="rotation">Orientation of the new anchor</param>
        public void CreateSpatialAnchor(Vector3 position, Quaternion rotation)
        {
            _spatialAnchorCore.InstantiateSpatialAnchor(AnchorPrefab, position, rotation);
        }

        /// <summary>
        /// Create an anchor at the <see cref="AnchorPrefab"/>'s position.
        /// </summary>
        public void CreateSpatialAnchor()
        {
            CreateSpatialAnchor(_anchorPrefabTransform.position, _anchorPrefabTransform.rotation);
        }

        /// <summary>
        /// Loads and instantiates anchors from a list of uuids.
        /// </summary>
        /// <remarks>If </remarks>
        public virtual void LoadAndInstantiateAnchors(List<Guid> uuids)
        {
            _spatialAnchorCore.LoadAndInstantiateAnchors(AnchorPrefab, uuids);
        }

        /// <summary>
        /// Loads anchors from local storage.
        /// </summary>
        /// <remarks><see cref="SpatialAnchorLocalStorageManagerBuildingBlock"/> component is required to load anchors from
        /// default local storage</remarks>
        /// <remarks>If </remarks>
        public virtual void LoadAnchorsFromDefaultLocalStorage()
        {
            var spatialAnchorLocalStorageManagerBuildingBlock = FindAnyObjectByType<SpatialAnchorLocalStorageManagerBuildingBlock>();
            if (!spatialAnchorLocalStorageManagerBuildingBlock)
            {
                Debug.Log($"[{nameof(SpatialAnchorLocalStorageManagerBuildingBlock)}] component is missing.");
                return;
            }

            var uuids = spatialAnchorLocalStorageManagerBuildingBlock.GetAnchorAnchorUuidFromLocalStorage();
            if (uuids == null) return;
            _spatialAnchorCore.LoadAndInstantiateAnchors(AnchorPrefab, uuids);
        }
    }
}
