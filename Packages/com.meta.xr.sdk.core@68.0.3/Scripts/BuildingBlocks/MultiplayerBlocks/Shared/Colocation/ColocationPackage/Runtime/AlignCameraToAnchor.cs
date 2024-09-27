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

namespace Meta.XR.MultiplayerBlocks.Colocation
{
    /// <summary>
    ///     Class that handles aligning the camera to a given anchor
    /// </summary>
    [DefaultExecutionOrder(10)]
    internal class AlignCameraToAnchor : MonoBehaviour
    {
        public OVRSpatialAnchor CameraAlignmentAnchor { get; set; }

        private void Update()
        {
            Align(CameraAlignmentAnchor);
        }

        public void RealignToAnchor()
        {
            Align(CameraAlignmentAnchor);
        }

        private void Align(OVRSpatialAnchor anchor)
        {
            // Align the scene by transforming the camera.
            // The inverse anchor pose is used to move the camera so that the scene appears as if it was parented to the anchor.

            // Get the anchor's raw tracking space pose to align the camera.
            // Note that the anchor's world space pose is dependent on the camera position, in order to maintain consistent world-locked rendering.

            // Position the anchor in tracking space

            Transform anchorTransform = anchor.transform;
            var trackingSpacePose = anchorTransform.ToTrackingSpacePose(Camera.main);
            anchorTransform.SetPositionAndRotation(trackingSpacePose.position, trackingSpacePose.orientation);

            // Transform the camera to the inverse of the anchor pose to align the scene
            transform.position = anchorTransform.InverseTransformPoint(Vector3.zero);
            transform.eulerAngles = new Vector3(0, -anchorTransform.eulerAngles.y, 0);

            // Update the world space position of the anchor so it renders in a consistent world-locked position.
            OVRPose worldSpacePose = trackingSpacePose.ToWorldSpacePose(Camera.main);
            anchorTransform.SetPositionAndRotation(worldSpacePose.position, worldSpacePose.orientation);
        }
    }
}
