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
using Meta.XR.BuildingBlocks.Editor;
using UnityEditor;
using UnityEngine;

public class PassthroughWindowBlockData : BlockData
{
    protected override List<GameObject> InstallRoutine(GameObject selectedGameObject)
    {
        if (!OVRPassthroughHelper.IsAnyPassthroughLayerUnderlay())
        {
            var pt = new GameObject("OVRPassthroughLayer").AddComponent<OVRPassthroughLayer>();
            pt.overlayType = OVROverlay.OverlayType.Underlay;
            Undo.RegisterCreatedObjectUndo(pt.gameObject, "Instantiate PT layer.");
        }

        if (selectedGameObject == null)
        {
            return base.InstallRoutine(selectedGameObject);
        }

        if (!selectedGameObject.TryGetComponent<Renderer>(out var renderer))
        {
            throw new Exception("A Renderer component is missing. Unable to use this surface as passthrough window.");
        }

        Undo.RegisterFullObjectHierarchyUndo(selectedGameObject, "Apply selective passthrough.");
        renderer.sharedMaterial = Prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial;

        if (!Utils.FindComponentInScene<EnableUnpremultipliedAlpha>())
        {
            selectedGameObject.AddComponent<EnableUnpremultipliedAlpha>();
        }

        return new List<GameObject>();
    }
}
