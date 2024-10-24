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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class OVREnumExtensions
{
    public static bool IsHand(this OVRSkeleton.SkeletonType skeletonType)
    {
        if (skeletonType == OVRSkeleton.SkeletonType.HandLeft || skeletonType == OVRSkeleton.SkeletonType.HandRight)
        {
            return true;
        }
        return false;
    }

    public static bool IsLeft(this OVRSkeleton.SkeletonType type)
    {
        if (type == OVRSkeleton.SkeletonType.HandLeft)
        {
            return true;
        }
        return false;
    }

    public static OVRHand.Hand AsHandType(this OVRSkeleton.SkeletonType skeletonType)
    {
        switch (skeletonType)
        {
            case OVRSkeleton.SkeletonType.HandLeft:
                return OVRHand.Hand.HandLeft;
            case OVRSkeleton.SkeletonType.HandRight:
                return OVRHand.Hand.HandRight;
            default:
                return OVRHand.Hand.None;
        }
    }

    public static OVRSkeleton.SkeletonType AsSkeletonType(this OVRHand.Hand hand)
    {
        switch (hand)
        {
            case OVRHand.Hand.HandLeft:
                return OVRSkeleton.SkeletonType.HandLeft;
            case OVRHand.Hand.HandRight:
                return OVRSkeleton.SkeletonType.HandRight;
            default:
                return OVRSkeleton.SkeletonType.None;
        }
    }

    public static OVRMesh.MeshType AsMeshType(this OVRHand.Hand hand)
    {
        switch (hand)
        {
            case OVRHand.Hand.HandLeft:
                return OVRMesh.MeshType.HandLeft;
            case OVRHand.Hand.HandRight:
                return OVRMesh.MeshType.HandRight;
            default:
                return OVRMesh.MeshType.None;
        }
    }

    public static OVRHand.Hand AsHandType(this OVRMesh.MeshType meshType)
    {
        switch (meshType)
        {
            case OVRMesh.MeshType.HandLeft:
                return OVRHand.Hand.HandLeft;
            case OVRMesh.MeshType.HandRight:
                return OVRHand.Hand.HandRight;
            default:
                return OVRHand.Hand.None;
        }
    }
}
