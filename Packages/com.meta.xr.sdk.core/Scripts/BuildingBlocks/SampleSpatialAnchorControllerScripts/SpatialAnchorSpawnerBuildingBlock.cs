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

using UnityEngine;

namespace Meta.XR.BuildingBlocks
{
    [RequireComponent(typeof(SpatialAnchorSpawnerBuildingBlock))]
    public class SpatialAnchorSpawnerBuildingBlock : MonoBehaviour
    {
        public GameObject AnchorPrefab
        {
            get => _anchorPrefab;
            set
            {
                _anchorPrefab = value;
                if (_anchorPrefabTransform) Destroy(_anchorPrefabTransform.gameObject);
                _anchorPrefabTransform = Instantiate(AnchorPrefab).transform;
                FollowHand = _followHand;
            }
        }

        public bool FollowHand
        {
            get => _followHand;
            set
            {
                _followHand = value;
                if (_followHand)
                {
                    _initialPosition = _anchorPrefabTransform.position;
                    _initialRotation = _anchorPrefabTransform.rotation;
                    _anchorPrefabTransform.parent = _cameraRig.rightControllerAnchor;
                    _anchorPrefabTransform.localPosition = Vector3.zero;
                    _anchorPrefabTransform.localRotation = Quaternion.identity;
                }
                else
                {
                    _anchorPrefabTransform.parent = null;
                    _anchorPrefabTransform.SetPositionAndRotation(_initialPosition, _initialRotation);
                }
            }
        }

        [Tooltip("A placeholder object to place in the anchor's position.")]
        [SerializeField]
        private GameObject _anchorPrefab;

        [Tooltip("Anchor prefab GameObject will follow the user's right hand.")]
        [SerializeField] private bool _followHand = true;

        private SpatialAnchorCoreBuildingBlock _spatialAnchorCore;
        private OVRCameraRig _cameraRig;
        private Transform _anchorPrefabTransform;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation;

        private void Awake()
        {
            _spatialAnchorCore = SpatialAnchorCoreBuildingBlock.GetBaseInstances()[0];
            _cameraRig = FindAnyObjectByType<OVRCameraRig>();
            AnchorPrefab = _anchorPrefab;
            FollowHand = _followHand;
        }

        /// <summary>
        /// Spawn a spatial anchor.
        /// </summary>
        /// <param name="position">Position for the new anchor.</param>
        /// <param name="rotation">Orientation of the new anchor</param>
        public void SpawnSpatialAnchor(Vector3 position, Quaternion rotation)
        {
            _spatialAnchorCore.InstantiateSpatialAnchor(AnchorPrefab, position, rotation);
        }

        internal void SpawnSpatialAnchor()
        {
            if (!FollowHand)
                SpawnSpatialAnchor(AnchorPrefab.transform.position, AnchorPrefab.transform.rotation);
            else
                SpawnSpatialAnchor(_anchorPrefabTransform.position, _anchorPrefabTransform.rotation);
        }
    }
}
