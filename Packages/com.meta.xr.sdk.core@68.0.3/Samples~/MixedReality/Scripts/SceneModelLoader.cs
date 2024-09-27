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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// This class loads a Scene Model using the OVRAnchor API.
///   * We query by iterating over all rooms and the Scene Anchor child elements.
///   * A prefab will be spawned per Scene Anchor object.
///   * The spawned object will be placed and scaled according to the Scene data.
///   * If there is no Scene Model, Scene Capture will be invoked on-device only.
///
/// Note: this class is for learning. It contains inefficiencies in order to
///       keep things simple (Linq and avoidable GC allocations).
/// </summary>
public class SceneModelLoader : MonoBehaviour
{
    public GameObject SceneObjectPrefab;
    [SerializeField] private Transform _trackingSpace;

    void Start()
    {
        LoadSceneModel();
    }

    async void LoadSceneModel()
    {
        if (!await HasQueryableSceneModel())
            return;

        // fetch all rooms by querying for all anchors with the room layout component
        var rooms = new List<OVRAnchor>();
        await OVRAnchor.FetchAnchorsAsync(rooms, new OVRAnchor.FetchOptions
        {
            SingleComponentType = typeof(OVRRoomLayout),
        });

        // fetch room elements, create objects for them asynchronously
        var tasks = rooms.Select(async room =>
        {
            var roomObject = new GameObject($"Room-{room.Uuid}");
            if (!room.TryGetComponent(out OVRAnchorContainer container))
                return;

            var children = new List<OVRAnchor>();
            await container.FetchAnchorsAsync(children);
            await CreateSceneObjects(roomObject, children);
        });
        await Task.WhenAll(tasks);
    }

    async Task CreateSceneObjects(GameObject roomGameObject, List<OVRAnchor> anchors)
    {
        // we create tasks to iterate through all anchors asynchronously
        var tasks = anchors.Select(async anchor =>
        {
            // can we locate it in the world?
            if (!anchor.TryGetComponent(out OVRLocatable locatable))
                return;
            await locatable.SetEnabledAsync(true);

            // get semantic classification for object name
            var classifications = new HashSet<OVRSemanticLabels.Classification>
            {
                OVRSemanticLabels.Classification.Other
            };
            if (anchor.TryGetComponent(out OVRSemanticLabels labels))
                labels.GetClassifications(classifications);

            // create and parent Unity game object
            var gObj = Instantiate(SceneObjectPrefab, roomGameObject.transform);
            gObj.name = string.Join(',', classifications);

            // set pose of object
            if (locatable.TryGetSceneAnchorPose(out var pose))
            {
                gObj.transform.SetPositionAndRotation(
                    pose.ComputeWorldPosition(_trackingSpace).GetValueOrDefault(),
                    pose.ComputeWorldRotation(_trackingSpace).GetValueOrDefault()
                );
            }

            // set child object's dimensions to the geometry of the scene object
            var childTransform = gObj.transform.GetChild(0);
            if (anchor.TryGetComponent(out OVRBounded3D bounds3D) && bounds3D.IsEnabled)
            {
                childTransform.localPosition = new Vector3(
                    0, 0, -bounds3D.BoundingBox.size.z / 2);
                childTransform.localScale = bounds3D.BoundingBox.size;
            }
            else if (anchor.TryGetComponent(out OVRBounded2D bounds2D) && bounds2D.IsEnabled)
            {
                childTransform.localEulerAngles = new Vector3(0, 180, 0);
                childTransform.localScale = new Vector3(
                    bounds2D.BoundingBox.size.x,
                    bounds2D.BoundingBox.size.y,
                    0.01f);
            }
        });
        await Task.WhenAll(tasks);
    }

    async OVRTask<bool> HasQueryableSceneModel()
    {
        // check Spatial Data permission
        const string permission = "com.oculus.permission.USE_SCENE";
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
        {
            Debug.LogError("Spatial Data permission has not been granted. " +
                "Use OVRCameraRig's OVRManager Permission Requests On Startup " +
                "to perform the runtime permission request, or use " +
                "Unity's Android Permission API.");
            return false;
        }

        // check that we have room data
        var rooms = new List<OVRAnchor>();
        await OVRAnchor.FetchAnchorsAsync(rooms, new OVRAnchor.FetchOptions
        {
            SingleComponentType = typeof(OVRRoomLayout),
        });
        if (rooms.Count != 0)
            return true;

#if UNITY_EDITOR
        Debug.LogError("No Scene Model found. " +
            "When using Meta Quest Link, ensure that you have enabled " +
            "Spatial Data over Meta Quest Link (Settings > Beta).\n" +
            "If you have not yet captured a Scene Model, run Space Setup " +
            "on-device, as doing this on Meta Quest Link is not supported");
#endif
        return await OVRScene.RequestSpaceSetup();
    }
}
