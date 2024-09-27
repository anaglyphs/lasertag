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
using Meta.XR.BuildingBlocks;
using UnityEngine;

#if META_INTERACTION_SDK_DEFINED
using Oculus.Interaction;
#endif // META_INTERACTION_SDK_DEFINED

namespace Meta.XR.MultiplayerBlocks.Shared
{
    public interface ITransferOwnership
    {
        public void TransferOwnershipToLocalPlayer();
        public bool HasOwnership();
    }

    public class TransferOwnershipOnSelect : MonoBehaviour
    {
#if META_INTERACTION_SDK_DEFINED
        public bool UseGravity;
        private Grabbable _grabbable;
        private Rigidbody _rigidbody;
        private ITransferOwnership _transferOwnership;

        private void Awake()
        {
            _grabbable = GetComponentInChildren<Grabbable>();

            if (_grabbable == null)
            {
                throw new InvalidOperationException("Object requires a Grabbable component");
            }

            _grabbable.WhenPointerEventRaised += OnPointerEventRaised;

            _transferOwnership = this.GetInterfaceComponent<ITransferOwnership>();
            if (_transferOwnership == null)
            {
                throw new InvalidOperationException("Object requires an ITransferOwnership component");
            }

            if (!UseGravity)
            {
                return;
            }
            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null)
            {
                throw new InvalidOperationException("Object requires a Rigidbody component when useGravity enabled");
            }
        }

        private void OnDestroy()
        {
            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised -= OnPointerEventRaised;
            }
        }

        private void OnPointerEventRaised(PointerEvent pointerEvent)
        {
            if (_grabbable == null || pointerEvent.Type != PointerEventType.Select)
            {
                return;
            }

            if (_grabbable.SelectingPointsCount == 1)
            {
                if (!_transferOwnership.HasOwnership())
                {
                    _transferOwnership.TransferOwnershipToLocalPlayer();
                }
            }
        }

        private void LateUpdate()
        {
            if (_transferOwnership.HasOwnership() && UseGravity)
            {
                // When network objects transferring ownership during interactions from ISDK, we need to guarantee a proper
                // kinematic state. We recommend developers to use RigidbodyKinematicLocker for other custom isKinematic controls.
                _rigidbody.isKinematic = _rigidbody.IsLocked();
            }
        }

#endif // META_INTERACTION_SDK_DEFINED
    }
}
