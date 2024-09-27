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
using UnityEngine;
using static OVRUnityHumanoidSkeletonRetargeter.OVRHumanBodyBonesMappings;
using BoneId = OVRSkeleton.BoneId;

/// <summary>
/// Defines interface for mapping OVRBoneId to humanoid bones, joint pairs for
/// bones, et cetera.
/// </summary>
public interface OVRHumanBodyBonesMappingsInterface
{
    /// <summary>
    /// Returns HumanBodyBones to joint pair map.
    /// </summary>
    public Dictionary<HumanBodyBones, Tuple<HumanBodyBones, HumanBodyBones>> GetBoneToJointPair { get; }

    /// <summary>
    /// Returns HumanBodeBones to body section map.
    /// </summary>
    public Dictionary<HumanBodyBones, BodySection> GetBoneToBodySection { get; }

    /// <summary>
    /// Returns full-body bone ID to HumanBodyBone map.
    /// </summary>
    public Dictionary<BoneId, HumanBodyBones> GetFullBodyBoneIdToHumanBodyBone { get; }

    /// <summary>
    /// Returns half-body bone ID to HumanBodyBone map.
    /// </summary>
    public Dictionary<BoneId, HumanBodyBones> GetBoneIdToHumanBodyBone { get; }

    /// <summary>
    /// Returns BoneId to joint pair map, for full-body.
    /// </summary>
    public Dictionary<BoneId, Tuple<BoneId, BoneId>>
        GetFullBodyBoneIdToJointPair
    { get; }

    /// <summary>
    /// Returns BoneId to joint pair map, for half-body.
    /// </summary>
    public Dictionary<BoneId, Tuple<BoneId, BoneId>>
        GetBoneIdToJointPair
    { get; }
}
