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
using UnityEngine;

/// <summary>
/// Represents the Pose of the anchor. Enabling it will localize the anchor.
/// </summary>
/// <remarks>
/// This component can be accessed from an <see cref="OVRAnchor"/> that supports it by calling
/// <see cref="OVRAnchor.GetComponent{T}"/> from the anchor.
///
/// This component needs to be enabled before requesting its Pose. See <see cref="IsEnabled"/> and
/// <see cref="SetEnabledAsync"/>
///
/// Read more about anchors generally at
/// [Spatial Anchors Overview](https://developer.oculus.com/documentation/unity/unity-spatial-anchors-overview/)
/// </remarks>
/// <seealso cref="TrackingSpacePose"/>
/// <seealso cref="TryGetSceneAnchorPose"/>
/// <seealso cref="TryGetSpatialAnchorPose"/>
public readonly partial struct OVRLocatable : IOVRAnchorComponent<OVRLocatable>, IEquatable<OVRLocatable>
{
    /// <summary>
    /// Tracking space position and rotation of the anchor.
    /// </summary>
    /// <remarks>
    /// Obtain a <see cref="TrackingSpacePose"/> from <see cref="OVRLocatable.TryGetSceneAnchorPose"/> or
    /// <see cref="OVRLocatable.TryGetSpatialAnchorPose"/> depending on the type of anchor you wish to interpret it as.
    ///
    /// Position and rotation are both nullable [Vector3](https://docs.unity3d.com/ScriptReference/Vector3.html) and
    /// [Quaternion](https://docs.unity3d.com/ScriptReference/Quaternion.html) and might be null independently if one of
    /// them or both are invalid.
    /// </remarks>
    /// <seealso cref="Position"/>
    /// <seealso cref="IsPositionTracked"/>
    /// <seealso cref="ComputeWorldPosition"/>
    /// <seealso cref="Rotation"/>
    /// <seealso cref="IsRotationTracked"/>
    /// <seealso cref="ComputeWorldRotation"/>
    public readonly struct TrackingSpacePose
    {
        /// <summary>
        /// Position in tracking space of the anchor.
        /// </summary>
        /// <remarks>
        /// Null if and when the rotation is invalid. This constitutes the positional aspect of the anchor's pose. See
        /// <see cref="Rotation"/> for the rotational part.
        /// </remarks>
        /// <seealso cref="Rotation"/>
        /// <seealso cref="ComputeWorldPosition"/>
        /// <seealso cref="ComputeWorldRotation"/>
        public Vector3? Position { get; }

        /// <summary>
        /// Rotation in tracking space of the anchor.
        /// </summary>
        /// <remarks>
        /// Null if and when the rotation is invalid. This constitutes the rotational aspect of the anchor's pose. See
        /// <see cref="Position"/> for the positional part.
        /// </remarks>
        /// <seealso cref="Position"/>
        /// <seealso cref="ComputeWorldPosition"/>
        /// <seealso cref="ComputeWorldRotation"/>
        public Quaternion? Rotation { get; }

        /// <summary>
        /// Indicates whether the position is currently tracked.
        /// </summary>
        public bool IsPositionTracked => _flags.IsPositionTracked();

        /// <summary>
        /// Indicates whether the rotation is currently tracked.
        /// </summary>
        public bool IsRotationTracked => _flags.IsOrientationTracked();

        private readonly OVRPlugin.SpaceLocationFlags _flags;

        internal TrackingSpacePose(Vector3 position, Quaternion rotation, OVRPlugin.SpaceLocationFlags flags)
        {
            _flags = flags;
            Position = _flags.IsPositionValid() ? position : default(Vector3?);
            Rotation = _flags.IsOrientationValid() ? rotation : default(Quaternion?);
        }

        private const string localToWorldPoseDeprecationMessage = "Using this method after 'await locatable.SetEnabledAsync(true);' is error-prone. OVRTask finishes the execution before OVRCameraRig.Update(), " +
                                                                  "so camera will still use a pose from the previous frame. This results in descrepancy when localizing anchors against the stale camera pose.\n" +
                                                                  "Use an overload with the 'trackingSpaceToWorldSpaceTransform' parameter instead.";

        /// <summary>
        /// \deprecated Computes the world space position of the anchor
        /// </summary>
        /// <param name="camera">A <see cref="Camera"/> component that will be use to compute the transform to world space</param>
        /// <returns>
        /// Returns the nullable [Vector3](https://docs.unity3d.com/ScriptReference/Vector3.html) position in world
        /// space which may be null if and when <see cref="Position"/> is invalid or head pose is invalid.
        /// </returns>
        /// <seealso cref="Position"/>
        /// <seealso cref="Rotation"/>
        /// <seealso cref="ComputeWorldRotation"/>
        /// <exception cref="ArgumentNullException">If <paramref name="camera"/> is null</exception>
        [Obsolete(localToWorldPoseDeprecationMessage)]
        public Vector3? ComputeWorldPosition(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (!Position.HasValue) return null;

            var headPose = OVRPose.identity;
            if (!OVRNodeStateProperties.GetNodeStatePropertyVector3(UnityEngine.XR.XRNode.Head,
                    NodeStatePropertyType.Position, OVRPlugin.Node.Head, OVRPlugin.Step.Render, out headPose.position))
                return null;

            if (!OVRNodeStateProperties.GetNodeStatePropertyQuaternion(UnityEngine.XR.XRNode.Head,
                    NodeStatePropertyType.Orientation, OVRPlugin.Node.Head, OVRPlugin.Step.Render,
                    out headPose.orientation))
                return null;

            headPose = headPose.Inverse();

            var headTrackingPosition = headPose.position + headPose.orientation * Position.Value;
            return camera.transform.localToWorldMatrix.MultiplyPoint(headTrackingPosition);
        }

        /// <summary>
        /// \deprecated Computes the world space rotation of the anchor
        /// </summary>
        /// <param name="camera">A <see cref="Camera"/> component that will be use to compute the transform to world space</param>
        /// <returns>
        /// The nullable [Quaternion](https://docs.unity3d.com/ScriptReference/Quaternion.html) rotation in world space
        /// which may be null if and when <see cref="Rotation"/> is invalid or if head rotation is invalid.
        /// </returns>
        /// <seealso cref="Position"/>
        /// <seealso cref="Rotation"/>
        /// <seealso cref="ComputeWorldPosition"/>
        /// <exception cref="ArgumentNullException">If <paramref name="camera"/> is null</exception>
        [Obsolete(localToWorldPoseDeprecationMessage)]
        public Quaternion? ComputeWorldRotation(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (!Rotation.HasValue) return null;

            if (!OVRNodeStateProperties.GetNodeStatePropertyQuaternion(UnityEngine.XR.XRNode.Head,
                    NodeStatePropertyType.Orientation, OVRPlugin.Node.Head, OVRPlugin.Step.Render,
                    out var headPoseRotation))
                return null;

            headPoseRotation = Quaternion.Inverse(headPoseRotation);

            var headTrackingOrientation = headPoseRotation * Rotation.Value;
            return camera.transform.rotation * headTrackingOrientation;
        }

        /// <summary>
        /// Computes the world-space position of the anchor.
        /// </summary>
        /// <param name="trackingSpaceToWorldSpaceTransform">Uses this transform to convert position from tracking-space to world-space.</param>
        /// <returns>The world-space position of the anchor, or `null` if <see cref="Position"/> does not have a value.</returns>
        public Vector3? ComputeWorldPosition(Transform trackingSpaceToWorldSpaceTransform)
        {
            if (trackingSpaceToWorldSpaceTransform == null)
            {
                throw new ArgumentNullException(nameof(trackingSpaceToWorldSpaceTransform));
            }
            if (!Position.HasValue)
            {
                return null;
            }
            return trackingSpaceToWorldSpaceTransform.TransformPoint(Position.Value);
        }

        /// <summary>
        /// Computes the world-space rotation of the anchor.
        /// </summary>
        /// <param name="trackingSpaceToWorldSpaceTransform">Uses this transform to convert rotation from tracking-space to world-space.</param>
        /// <returns>The world-space rotation of the anchor, or `null` if <see cref="Rotation"/> does not have a value.</returns>
        public Quaternion? ComputeWorldRotation(Transform trackingSpaceToWorldSpaceTransform)
        {
            if (trackingSpaceToWorldSpaceTransform == null)
            {
                throw new ArgumentNullException(nameof(trackingSpaceToWorldSpaceTransform));
            }
            if (!Rotation.HasValue)
            {
                return null;
            }
            return trackingSpaceToWorldSpaceTransform.rotation * Rotation.Value;
        }
    }

    /// <summary>
    /// Tries to get the <see cref="TrackingSpacePose"/> representing the position and rotation of this anchor, treated as a scene anchor, in tracking space.
    /// </summary>
    /// <param name="pose">The out <see cref="TrackingSpacePose"/> which will get filled in.</param>
    /// <returns>
    /// True if the request was successful, False otherwise.
    /// </returns>
    /// <remarks>
    /// <para>Although the request may succeed and provide a valid <see cref="TrackingSpacePose"/>, actual Position and Rotation provided
    /// may not be valid and/or tracked, see <see cref="TrackingSpacePose"/> for more information on how to use its data.</para>
    /// <para>Scene anchors follow a different transform from the raw OpenXR data than spatial anchors'.</para>
    /// </remarks>
    public bool TryGetSceneAnchorPose(out TrackingSpacePose pose)
    {
        if (!OVRPlugin.TryLocateSpace(Handle, OVRPlugin.GetTrackingOriginType(), out var posef, out var locationFlags))
        {
            pose = default;
            return false;
        }

        // Transform from OpenXR Right-handed coordinate system
        // to Unity Left-handed coordinate system with additional 180 rotation around +y
        var position = posef.Position.FromFlippedZVector3f();
        var rotation = new Quaternion(-posef.Orientation.z, posef.Orientation.w, -posef.Orientation.x,
            posef.Orientation.y);
        pose = new TrackingSpacePose(position, rotation, locationFlags);
        return true;
    }

    /// <summary>
    /// Tries to get the <see cref="TrackingSpacePose"/> representing the position and rotation of this anchor, treated as a spatial anchor, in tracking space.
    /// </summary>
    /// <param name="pose">The out <see cref="TrackingSpacePose"/> which will get filled in.</param>
    /// <returns>
    /// True if the request was successful, False otherwise.
    /// </returns>
    /// <remarks>
    /// <para>Although the request may succeed and provide a valid <see cref="TrackingSpacePose"/>, actual position and rotation provided
    /// may not be valid and/or tracked, see <see cref="TrackingSpacePose"/> for more information on how to use its data.</para>
    /// <para>Spatial anchors follow a different transform from the raw OpenXR data than scene anchors'.</para>
    /// </remarks>
    public bool TryGetSpatialAnchorPose(out TrackingSpacePose pose)
    {
        if (!OVRPlugin.TryLocateSpace(Handle, OVRPlugin.GetTrackingOriginType(), out var posef, out var locationFlags))
        {
            pose = default;
            return false;
        }

        // Transform from OpenXR Right-handed coordinate system
        // to Unity Left-handed coordinate system
        var position = posef.Position.FromFlippedZVector3f();
        var rotation = posef.Orientation.FromFlippedZQuatf();
        pose = new TrackingSpacePose(position, rotation, locationFlags);
        return true;
    }
}
