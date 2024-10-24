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

using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class OVRPassthroughHelper
{
    internal static bool EnablePassthrough()
    {
        var ovrManager = OVRProjectSetupUtils.FindComponentInScene<OVRManager>();

        if (ovrManager is null)
        {
            return false;
        }

        ovrManager.isInsightPassthroughEnabled = true;
        return true;
    }

    internal static bool IsAnyPassthroughLayerUnderlay()
    {
        return OVRProjectSetupUtils.FindComponentsInScene<OVRPassthroughLayer>()
            .Any(p => p.overlayType == OVROverlay.OverlayType.Underlay);
    }

    internal static bool InitPassthroughLayerUnderlay(GameObject ovrCameraRig)
    {
        var passthroughLayers = OVRProjectSetupUtils.FindComponentsInScene<OVRPassthroughLayer>().ToList();

        // no PT layers
        if (passthroughLayers.Count == 0)
        {
            var underLay = ovrCameraRig.AddComponent<OVRPassthroughLayer>();
            underLay.overlayType = OVROverlay.OverlayType.Underlay;
        }
        // there are layers but non of them are Underlay
        else if (passthroughLayers.All(l => l.overlayType != OVROverlay.OverlayType.Underlay))
        {
            // if there is only one PT layer, change it to Underlay
            if (passthroughLayers.Count == 1)
            {
                passthroughLayers.First().overlayType = OVROverlay.OverlayType.Underlay;
            }
            else
            {
                Debug.LogError(
                    "There are multiple OVRPassthroughLayer instances in the scene, but none of them is an Underlay. Set one of the layer's Placement to Underlay.");
                return false;
            }
        }

        SaveScene();
        return true;
    }

    internal static bool HasCentralCamera(OVRCameraRig ovrCameraRig) =>
        (ovrCameraRig.centerEyeAnchor != null ? ovrCameraRig.centerEyeAnchor.GetComponent<Camera>() : null) != null;

    internal static Camera GetCentralCamera(OVRCameraRig ovrCameraRig) =>
        ovrCameraRig.centerEyeAnchor != null ? ovrCameraRig.centerEyeAnchor.GetComponent<Camera>() : null;

    internal static bool IsBackgroundClear(OVRCameraRig ovrCameraRig)
    {
        var centerCamera = GetCentralCamera(ovrCameraRig);

        if (centerCamera is null)
        {
            throw new System.Exception("Central camera was not found");
        }

        return centerCamera.clearFlags == CameraClearFlags.SolidColor && centerCamera.backgroundColor.a < 1;
    }

    internal static void ClearBackground(OVRCameraRig ovrCameraRig)
    {
        var centerCamera = GetCentralCamera(ovrCameraRig);

        if (centerCamera is null)
        {
            throw new System.Exception("Central camera was not found");
        }

        centerCamera.clearFlags = CameraClearFlags.SolidColor;
        centerCamera.backgroundColor = Color.clear;

        SaveScene();
    }

    private static void SaveScene()
    {
        if (!IsCurrentSceneSaved())
            return;

        var activeScene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);
    }

    private static bool IsCurrentSceneSaved()
    {
        var scenePath = SceneManager.GetActiveScene().path;
        return !string.IsNullOrEmpty(scenePath);
    }
}
