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
using Meta.XR.Util;
using UnityEngine;

[Feature(Feature.Anchors)]
public class SharedSpatialAnchorErrorHandler : MonoBehaviour
{
    [Tooltip("Disables the GUI alerts in headset.")]
    public bool DisableRuntimeGUIAlerts = false;

    [SerializeField] private GameObject AlertViewHUDPrefab;

    private string cloudPermissionMsg =
        "Your headset uses on-device point cloud data to determine its position within your room. " +
        "To expand your headset’s capabilities and enable features like local multiplayer, you’ll need to share point cloud data with Meta. You can turn off point cloud sharing anytime in Settings." +
        "\n\nSettings > Privacy > Device Permissions > Turn on \"Share Point Cloud Data\"";

    private void Awake()
    {
        if (AlertViewHUDPrefab) Instantiate(AlertViewHUDPrefab);
    }

    public void OnAnchorCreate(OVRSpatialAnchor _, OVRSpatialAnchor.OperationResult result)
    {
        if (result == OVRSpatialAnchor.OperationResult.Failure_SpaceCloudStorageDisabled)
        {
            LogWarning(cloudPermissionMsg);
            return;
        }

        if (result != OVRSpatialAnchor.OperationResult.Success)
        {
            LogWarning($"Failed to create the spatial anchor.");
        }
    }

    public void OnAnchorShare(List<OVRSpatialAnchor> _, OVRSpatialAnchor.OperationResult result)
    {
        if (result == OVRSpatialAnchor.OperationResult.Failure_SpaceCloudStorageDisabled)
        {
            LogWarning(cloudPermissionMsg);
            return;
        }

        if (result != OVRSpatialAnchor.OperationResult.Success)
        {
            LogWarning($"Failed to share the spatial anchor.");
        }
    }

    public void OnSharedSpatialAnchorLoad(List<OVRSpatialAnchor> loadedAnchors, OVRSpatialAnchor.OperationResult result)
    {
        if (result == OVRSpatialAnchor.OperationResult.Failure_SpaceCloudStorageDisabled)
        {
            LogWarning(cloudPermissionMsg);
            return;
        }
        if (loadedAnchors == null || loadedAnchors.Count == 0) LogWarning($"Failed to load the spatial anchor(s).");
    }

    public void OnAnchorEraseAll(OVRSpatialAnchor.OperationResult result)
    {
        if (result == OVRSpatialAnchor.OperationResult.Failure)
            LogWarning($"Failed to erase the spatial anchor(s).");
    }

    public void OnAnchorErase(OVRSpatialAnchor anchor, OVRSpatialAnchor.OperationResult result)
    {
        if (result == OVRSpatialAnchor.OperationResult.Failure)
            LogWarning($"Failed to erase the spatial anchor with uuid: {anchor}");
    }

    private void LogWarning(string msg)
    {
        if (!DisableRuntimeGUIAlerts)
        {
            AlertViewHUD.PostMessage(msg, AlertViewHUD.MessageType.Error);
        }

        Debug.LogWarning($"[{nameof(SharedSpatialAnchorErrorHandler)}] {msg}");
    }
}
