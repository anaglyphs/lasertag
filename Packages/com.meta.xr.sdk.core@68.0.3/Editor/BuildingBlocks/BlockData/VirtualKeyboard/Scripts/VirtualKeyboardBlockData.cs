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
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    public class VirtualKeyboardBlockData : BlockData
    {
        protected override List<GameObject> InstallRoutine(GameObject selectedGameObject)
        {
            var virtualKeyboardGo = Instantiate(Prefab, Vector3.zero, Quaternion.identity);
            virtualKeyboardGo.name = $"{Utils.BlockPublicTag} Virtual Keyboard";
            Undo.RegisterCreatedObjectUndo(virtualKeyboardGo, "Create virtual keyboard.");
            var virtualKeyboard = virtualKeyboardGo.GetComponent<OVRVirtualKeyboard>();
            if (virtualKeyboard == null)
            {
                throw new InvalidOperationException(
                    "The Virtual Keyboard component is missing.");
            }

            var controllerBuildingBlocks = Utils.GetBlocksWithType<OVRControllerHelper>();
            var leftController = controllerBuildingBlocks.First(controller => controller.m_controller == OVRInput.Controller.LTouch);
            var rightController = controllerBuildingBlocks.First(controller => controller.m_controller == OVRInput.Controller.RTouch);

            var interactorPos = new Vector3(0f, 0f, 0.062f);
            var interactorScale = 0.01f * Vector3.one;
            var interactorAnchorLeft = new GameObject
            {
                transform =
                {
                    localPosition = interactorPos,
                    localScale = interactorScale,
                    parent = leftController.gameObject.transform,
                    name = "KeyboardInteractorAnchorLeft"
                }
            };
            Undo.RegisterCreatedObjectUndo(interactorAnchorLeft, "Create left interactor.");

            var interactorAnchorRight = new GameObject
            {
                transform =
                {
                    localPosition = interactorPos,
                    localScale = interactorScale,
                    parent = rightController.gameObject.transform,
                    name = "KeyboardInteractorAnchorRight"
                }
            };
            Undo.RegisterCreatedObjectUndo(interactorAnchorRight, "Create right interactor.");

            var cameraRigBuildingBlock = Utils.GetBlocksWithType<OVRCameraRig>().First();

            virtualKeyboard.leftControllerRootTransform = cameraRigBuildingBlock.leftControllerAnchor;
            virtualKeyboard.rightControllerRootTransform = cameraRigBuildingBlock.rightControllerAnchor;
            virtualKeyboard.leftControllerDirectTransform = interactorAnchorLeft.transform;
            virtualKeyboard.rightControllerDirectTransform = interactorAnchorRight.transform;

            var handBuildingBlocks = Utils.GetBlocksWithType<OVRHand>();
            var leftHand = handBuildingBlocks.First(hand => hand.HandType == OVRHand.Hand.HandLeft);
            var rightHand = handBuildingBlocks.First(hand => hand.HandType == OVRHand.Hand.HandRight);

            virtualKeyboard.handLeft = leftHand;
            virtualKeyboard.handRight = rightHand;

            return new List<GameObject> { virtualKeyboardGo };
        }
    }
}
