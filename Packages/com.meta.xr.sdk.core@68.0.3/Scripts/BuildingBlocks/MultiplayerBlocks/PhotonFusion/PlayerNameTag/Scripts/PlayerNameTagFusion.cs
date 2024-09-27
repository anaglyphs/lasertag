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

using Fusion;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Meta.XR.MultiplayerBlocks.Fusion
{
    public class PlayerNameTagFusion : NetworkBehaviour
    {
        [Networked, OnChangedRender(nameof(OnPlayerNameChange))]
        public NetworkString<_64> OculusName { get; set; }

        [SerializeField] private Text nameTag;
        [SerializeField] private GameObject nameTagGO;
        [SerializeField] private GameObject nameTagPanel;
        [SerializeField] private Transform nameTagContainer;
        [SerializeField] private float heightOffset = 0.3f;

        private Transform _centerEye;

        private void Start()
        {
            nameTagGO.SetActive(!Object.HasStateAuthority);
            OnPlayerNameChange();

            if (OVRManager.instance)
            {
                _centerEye = OVRManager.instance.GetComponentInChildren<OVRCameraRig>().centerEyeAnchor;
            }
        }

        private IEnumerator UpdateNameUI(string playerName)
        {
            nameTag.text = playerName;

            // refresh nameTag panel
            yield return new WaitForFixedUpdate();
            var vlg = nameTagPanel.GetComponent<VerticalLayoutGroup>();
            vlg.enabled = false;
            // ReSharper disable once Unity.InefficientPropertyAccess
            vlg.enabled = true;
        }

        private void OnPlayerNameChange()
        {
            StartCoroutine(UpdateNameUI(OculusName.ToString()));
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }

            // Subtle name tag movements
            var position = transform.position;
            nameTagContainer.localPosition = new Vector3(
                position.x,
                Mathf.Sin(Time.time * 2f),
                position.z) * 0.005f;

            var destination = _centerEye.transform.position;
            destination.y += heightOffset;
            position = Vector3.Lerp(position, destination, 0.1f);
            transform.position = position;
        }

        private void Update()
        {
            if (Object.HasStateAuthority)
            {
                return;
            }

            if (_centerEye != null)
            {
                nameTagGO.transform.LookAt(_centerEye.position, Vector3.up);
            }
        }
    }
}
