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
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;
using System.Linq;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

/// <summary>
/// Simple script for running the ControllerDrivenHandPosesSample
/// </summary>
[DisallowMultipleComponent]
[HelpURL("https://developer.oculus.com/documentation/unity/move-body-tracking/#appendix-b-isdk-integration")]
public class OVRControllerDrivenHandPosesSample : MonoBehaviour
{
    [SerializeField]
    private Button buttonOff;
    [SerializeField]
    private Button buttonConforming;
    [SerializeField]
    private Button buttonNatural;

    public OVRCameraRig cameraRig;

    // Unity event functions
    void Awake()
    {
        switch (OVRManager.instance.controllerDrivenHandPosesType)
        {
            case OVRManager.ControllerDrivenHandPosesType.None:
                SetControllerDrivenHandPosesTypeToNone();
                break;
            case OVRManager.ControllerDrivenHandPosesType.ConformingToController:
                SetControllerDrivenHandPosesTypeToControllerConforming();
                break;
            case OVRManager.ControllerDrivenHandPosesType.Natural:
                SetControllerDrivenHandPosesTypeToNatural();
                break;

        }
    }

    public void SetControllerDrivenHandPosesTypeToNone()
    {
        OVRManager.instance.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.None;
        buttonOff.interactable = false;
        buttonConforming.interactable = true;
        buttonNatural.interactable = true;
    }

    public void SetControllerDrivenHandPosesTypeToControllerConforming()
    {
        OVRManager.instance.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
        buttonOff.interactable = true;
        buttonConforming.interactable = false;
        buttonNatural.interactable = true;
    }

    public void SetControllerDrivenHandPosesTypeToNatural()
    {
        OVRManager.instance.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.Natural;
        buttonOff.interactable = true;
        buttonConforming.interactable = true;
        buttonNatural.interactable = false;
    }
}
