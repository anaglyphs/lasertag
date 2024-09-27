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
/// <summary>
/// An enum representing the different formats of hands / skeletons which are supported.
/// </summary>
public enum OVRHandSkeletonVersion
{
    [InspectorName(null)]
    Uninitialized = -1,
    [InspectorName("V1 (Legacy Skeleton)")]
    V1 = 0, // The skeleton configuration used up to January 2023. Changed to V2 because it wasn't compliant with OVRSpecification for several reasons.
    [InspectorName("V2 (OpenXR Skeleton)")]
    V2 = 1, // An updated skeleton standard used after January 2023. Matches the OVR Specification for hands. Differs from Default in that it supports some
            // extra bones and the data arrives from OVRPlugin with bone rotations in global space, not local space.
}
