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
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

/// <summary>
/// Represents the polyhedral mesh representation of a room.
/// </summary>
/// <remarks>
/// This component can be accessed from an <see cref="OVRAnchor"/> that supports it by calling
/// <see cref="OVRAnchor.GetComponent{T}"/> from the anchor.
///
/// The room mesh component is part of the Meta Quest Scene Model. Read more at
/// [Scene Overview](https://developer.oculus.com/documentation/unity/unity-scene-overview/).
/// </remarks>
public partial struct OVRRoomMesh
{
    /// <summary>
    /// Represents information about a single face within the <see cref="OVRRoomMesh"/>.
    /// </summary>
    /// <remarks>
    /// A face is uniquely identified with a <see cref="Guid"/>, and may optionally
    /// have a parent <see cref="Guid"/> if it is completely embedded within
    /// another face.
    ///
    /// Each face is planar, may be convex and may contain interior boundaries (holes).
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct Face
    {
        public Guid Uuid;
        public Guid ParentUuid;
        public OVRSemanticLabels.Classification SemanticLabel;
    }

    /// <summary>
    /// Gets the number of vertices and faces in the room mesh.
    /// </summary>
    /// <remarks>
    /// Use this method to get the required sizes of the vertex and face buffers. Once these buffers
    /// have been created, use these in conjunction with <see cref="TryGetRoomMesh"/>>.
    /// </remarks>
    /// <param name="vertexCount">The number of vertices in the room mesh.</param>
    /// <param name="faceCount">The number of faces in the room mesh.</param>
    /// <returns>Returns true if the counts were retrieved; otherwise, false.</returns>
    public bool TryGetRoomMeshCounts(out int vertexCount, out int faceCount)
    {
        unsafe
        {
            var result = OVRPlugin.GetSpaceRoomMesh(Handle, 0, out var vCount, null, 0, out var fCount, null);
            vertexCount = (int)vCount;
            faceCount = (int)fCount;
            return result == OVRPlugin.Result.Success;
        }
    }

    /// <summary>
    /// Gets the room mesh by populating the provided arrays.
    /// </summary>
    /// <remarks>
    /// The caller owns the memory of the input arrays and is responsible for allocating them to the appropriate size
    /// before passing them to this method. Use <see cref="TryGetRoomMeshCounts"/> to determine the required size of each array.
    ///
    /// The vertex data provided by this method has been converted from the right-handed coordinate space defined by
    /// OpenXR to Unity's coordinate space.
    /// </remarks>
    /// <param name="vertices">The vertex positions of the room mesh.</param>
    /// <param name="faces">The faces of the room mesh.</param>
    /// <returns>Return true if the room mesh data was retrieved; otherwise, false.</returns>
    public bool TryGetRoomMesh(NativeArray<Vector3> vertices, NativeArray<Face> faces)
    {
        unsafe
        {
            var roomFaces = faces.Reinterpret<OVRPlugin.RoomFace>();
            var result = OVRPlugin.GetSpaceRoomMesh(Handle, (uint)vertices.Length, out var vertexCountOutput,
                (OVRPlugin.Vector3f*)vertices.GetUnsafePtr(), (uint)roomFaces.Length, out var faceCountOutput,
                (OVRPlugin.RoomFace*)faces.GetUnsafePtr());

            if (!result.IsSuccess())
                return false;

            if (vertexCountOutput != vertices.Length)
            {
                Debug.LogError($"{nameof(TryGetRoomMesh)}: vertices size differs from the system.");
                return false;
            }

            if (faceCountOutput != faces.Length)
            {
                Debug.LogError($"{nameof(TryGetRoomMesh)}: faces size differs from the system.");
                return false;
            }

            // coordinate space difference between OpenXR (RHS) and Unity (LHS)
            for (var i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3(-v.x, v.y, v.z);
            }

            return true;
        }
    }
    /// <summary>
    /// Gets the number of indices for a given face of the room mesh.
    /// </summary>
    /// <remarks>
    /// Use this method to get the required size index buffer. Once the buffer
    /// has been created, use it in conjunction with <see cref="TryGetRoomFaceIndices"/>>.
    ///
    /// The indices of the face are with respect to the global room mesh vertices, and they form
    /// a triangulated planar surface that may be convex and contain interior boundaries (holes).
    /// </remarks>
    /// <param name="faceUuid">The uuid of the face in question.</param>
    /// <param name="faceIndexCount">The number of indices in the face of the room mesh.</param>
    /// <returns>Returns true if the count was retrieved; otherwise, false.</returns>
    public bool TryGetRoomFaceIndexCount(Guid faceUuid, out int faceIndexCount)
    {
        unsafe
        {
            var result = OVRPlugin.GetSpaceRoomFaceIndices(Handle, faceUuid, 0, out var fIndexCount, null);
            faceIndexCount = (int)fIndexCount;
            return result == OVRPlugin.Result.Success;
        }
    }

    /// <summary>
    /// Gets the indices for a face in the room mesh.
    /// </summary>
    /// <remarks>
    /// The caller owns the memory of the input array and is responsible for allocating it to the appropriate size
    /// before passing it to this method. Use <see cref="TryGetRoomFaceIndexCount"/> to determine the required size of the array.
    ///
    /// The index data provided by this method has been converted from the right-handed coordinate space defined by
    /// OpenXR to Unity's coordinate space.
    ///
    /// The indices of the face are with respect to the global room mesh vertices, and they form
    /// a triangulated planar surface that may be convex and contain interior boundaries (holes).
    /// </remarks>
    /// <param name="faceUuid">The uuid of the face in question.</param>
    /// <param name="faceIndices">The triangle indices of the face of the room mesh.</param>
    /// <returns>Returns true if the face index data was retrieved; otherwise, false.</returns>
    public bool TryGetRoomFaceIndices(Guid faceUuid, NativeArray<uint> faceIndices)
    {
        unsafe
        {
            var result = OVRPlugin.GetSpaceRoomFaceIndices(Handle, faceUuid, (uint)faceIndices.Length,
                out var indexCountOutput, (uint*)faceIndices.GetUnsafePtr());
            if (!result.IsSuccess())
                return false;

            if (indexCountOutput != faceIndices.Length)
            {
                Debug.LogError($"{nameof(TryGetRoomMesh)}: indices size differs from the system.");
                return false;
            }

            // OpenXR stores triangle ACB vs Unity's ABC
            for (var i = 0; i < faceIndices.Length; i += 3)
            {
                (faceIndices[i + 2], faceIndices[i + 1]) = (faceIndices[i + 1], faceIndices[i + 2]);
            }

            return true;
        }
    }
}

