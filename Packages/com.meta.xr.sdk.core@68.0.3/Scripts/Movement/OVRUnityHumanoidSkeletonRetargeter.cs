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
using UnityEngine.Assertions;
using SkeletonType = OVRPlugin.SkeletonType;
using BoneId = OVRPlugin.BoneId;
[Feature(Feature.BodyTracking)]
public partial class OVRUnityHumanoidSkeletonRetargeter : OVRSkeleton
{
    private OVRSkeletonMetadata _sourceSkeletonData;
    protected OVRSkeletonMetadata SourceSkeletonData => _sourceSkeletonData;

    private OVRSkeletonMetadata _sourceSkeletonTPoseData;
    protected OVRSkeletonMetadata SourceSkeletonTPoseData => _sourceSkeletonTPoseData;

    private OVRSkeletonMetadata _targetSkeletonData;
    protected OVRSkeletonMetadata TargetSkeletonData => _targetSkeletonData;

    private Animator _animatorTargetSkeleton;
    protected Animator AnimatorTargetSkeleton => _animatorTargetSkeleton;

    private Dictionary<BoneId, HumanBodyBones> _customBoneIdToHumanBodyBone =
        new Dictionary<BoneId, HumanBodyBones>();

    protected Dictionary<BoneId, HumanBodyBones> CustomBoneIdToHumanBodyBone
    {
        get => _customBoneIdToHumanBodyBone;
    }

    private readonly Dictionary<HumanBodyBones, Quaternion> _targetTPoseRotations =
        new Dictionary<HumanBodyBones, Quaternion>();
    private Dictionary<HumanBodyBones, Transform> _targetTPoseTransformDup =
        new Dictionary<HumanBodyBones, Transform>();

    protected Dictionary<HumanBodyBones, Quaternion> TargetTPoseRotations
    {
        get => _targetTPoseRotations;
    }

    private int _lastSkelChangeCount = -1;
    private Vector3 _lastTrackedScale;

    [Serializable]
    public class JointAdjustment
    {
        /// <summary>
        /// Joint to adjust.
        /// </summary>
        public HumanBodyBones Joint;

        /// <summary>
        /// Position change to apply to the joint, post-retargeting.
        /// </summary>
        public Vector3 PositionChange = Vector3.zero;

        /// <summary>
        /// Rotation to apply to the joint, post-retargeting.
        /// NOTE: deprecated, please use <inheritdoc cref="JointAdjustment.RotationTweaks"/>.
        /// </summary>
        public Quaternion RotationChange = Quaternion.identity;

        /// <summary>
        /// Allows accumulating a series of rotations to be
        /// applied to a joint, post-retargeting.
        /// </summary>
        public Quaternion[] RotationTweaks = null;

        /// <summary>
        /// Allows disable rotational transform on joint.
        /// </summary>
        public bool DisableRotationTransform = false;

        /// <summary>
        /// Allows disable position transform on joint.
        /// </summary>
        public bool DisablePositionTransform = false;

        /// <summary>
        /// Allows mapping this human body bone to OVRSkeleton bone different from the
        /// standard. An ignore value indicates to not override; remove means to exclude
        /// from retargeting. Cannot be changed at runtime.
        /// </summary>
        public OVRHumanBodyBonesMappings.FullBodyTrackingBoneId FullBodyBoneIdOverrideValue =
            OVRHumanBodyBonesMappings.FullBodyTrackingBoneId.NoOverride;
        public OVRHumanBodyBonesMappings.BodyTrackingBoneId BoneIdOverrideValue =
            OVRHumanBodyBonesMappings.BodyTrackingBoneId.NoOverride;

        /// <summary>
        /// Precomputed accumulated rotations.
        /// </summary>
        public Quaternion PrecomputedRotationTweaks { get; private set; }

        public void PrecomputeRotationTweaks()
        {
            PrecomputedRotationTweaks = Quaternion.identity;
            if (RotationTweaks == null || RotationTweaks.Length == 0)
            {
                return;
            }

            foreach (var rotationTweak in RotationTweaks)
            {
                // Make sure the quaternion is valid. Quaternions are initialized to all
                // zeroes by default, which makes them invalid.
                if (rotationTweak.w < Mathf.Epsilon && rotationTweak.x < Mathf.Epsilon &&
                    rotationTweak.y < Mathf.Epsilon && rotationTweak.z < Mathf.Epsilon)
                {
                    continue;
                }

                PrecomputedRotationTweaks *= rotationTweak;
            }
        }
    }

    public OVRUnityHumanoidSkeletonRetargeter()
    {
        _skeletonType = SkeletonType.Body;
    }

    [SerializeField]
    protected JointAdjustment[] _adjustments =
    {
        new JointAdjustment
        {
            Joint = HumanBodyBones.Hips,
            RotationChange = Quaternion.Euler(60.0f, 0.0f, 0.0f)
        }
    };

    protected JointAdjustment[] Adjustments
    {
        get => _adjustments;
    }

    [SerializeField]
    protected OVRHumanBodyBonesMappings.BodySection[] _fullBodySectionsToAlign =
    {
        OVRHumanBodyBonesMappings.BodySection.LeftArm, OVRHumanBodyBonesMappings.BodySection.RightArm,
        OVRHumanBodyBonesMappings.BodySection.LeftHand, OVRHumanBodyBonesMappings.BodySection.RightHand,
        OVRHumanBodyBonesMappings.BodySection.Hips, OVRHumanBodyBonesMappings.BodySection.Back,
        OVRHumanBodyBonesMappings.BodySection.Neck, OVRHumanBodyBonesMappings.BodySection.Head,
        OVRHumanBodyBonesMappings.BodySection.LeftLeg, OVRHumanBodyBonesMappings.BodySection.LeftFoot,
        OVRHumanBodyBonesMappings.BodySection.RightLeg, OVRHumanBodyBonesMappings.BodySection.RightFoot
    };
    [SerializeField]
    protected OVRHumanBodyBonesMappings.BodySection[] _bodySectionsToAlign =
    {
        OVRHumanBodyBonesMappings.BodySection.LeftArm, OVRHumanBodyBonesMappings.BodySection.RightArm,
        OVRHumanBodyBonesMappings.BodySection.LeftHand, OVRHumanBodyBonesMappings.BodySection.RightHand,
        OVRHumanBodyBonesMappings.BodySection.Hips, OVRHumanBodyBonesMappings.BodySection.Back,
        OVRHumanBodyBonesMappings.BodySection.Neck, OVRHumanBodyBonesMappings.BodySection.Head
    };

    protected OVRHumanBodyBonesMappings.BodySection[] FullBodySectionsToAlign
    {
        get => _fullBodySectionsToAlign;
    }
    protected OVRHumanBodyBonesMappings.BodySection[] BodySectionsToAlign
    {
        get => _bodySectionsToAlign;
    }

    [SerializeField]
    protected OVRHumanBodyBonesMappings.BodySection[] _fullBodySectionToPosition =
    {
        OVRHumanBodyBonesMappings.BodySection.LeftArm, OVRHumanBodyBonesMappings.BodySection.RightArm,
        OVRHumanBodyBonesMappings.BodySection.LeftHand, OVRHumanBodyBonesMappings.BodySection.RightHand,
        OVRHumanBodyBonesMappings.BodySection.Hips, OVRHumanBodyBonesMappings.BodySection.Neck,
        OVRHumanBodyBonesMappings.BodySection.Head,
        OVRHumanBodyBonesMappings.BodySection.LeftLeg, OVRHumanBodyBonesMappings.BodySection.LeftFoot,
        OVRHumanBodyBonesMappings.BodySection.RightLeg, OVRHumanBodyBonesMappings.BodySection.RightFoot
    };
    [SerializeField]
    protected OVRHumanBodyBonesMappings.BodySection[] _bodySectionToPosition =
    {
        OVRHumanBodyBonesMappings.BodySection.LeftArm, OVRHumanBodyBonesMappings.BodySection.RightArm,
        OVRHumanBodyBonesMappings.BodySection.LeftHand, OVRHumanBodyBonesMappings.BodySection.RightHand,
        OVRHumanBodyBonesMappings.BodySection.Hips, OVRHumanBodyBonesMappings.BodySection.Neck,
        OVRHumanBodyBonesMappings.BodySection.Head
    };

    protected OVRHumanBodyBonesMappings.BodySection[] FullBodySectionToPosition
    {
        get => _fullBodySectionToPosition;
    }
    protected OVRHumanBodyBonesMappings.BodySection[] BodySectionToPosition
    {
        get => _bodySectionToPosition;
    }

    public enum UpdateType
    {
        FixedUpdateOnly = 0,
        UpdateOnly,
        FixedUpdateAndUpdate
    }
    /// <summary>
    /// Controls if we run retargeting from FixedUpdate, Update,
    /// or both.
    /// </summary>
    [SerializeField]
    [Tooltip("Controls if we run retargeting from FixedUpdate, Update, or both.")]
    protected UpdateType _updateType = UpdateType.UpdateOnly;

    private OVRHumanBodyBonesMappingsInterface _bodyBonesMappingInterface =
        new OVRHumanBodyBonesMappings();
    /// <summary>
    /// Returns body bone mappings interface.
    /// </summary>
    public OVRHumanBodyBonesMappingsInterface BodyBoneMappingsInterface
    {
        get => _bodyBonesMappingInterface;
        set => _bodyBonesMappingInterface = value;
    }

    protected override void Start()
    {
        base.Start();
        _lastTrackedScale = transform.lossyScale;
        Assert.IsTrue(OVRSkeleton.IsBodySkeleton(_skeletonType));

        ValidateGameObjectForUnityHumanoidRetargeting(gameObject);
        _animatorTargetSkeleton = gameObject.GetComponent<Animator>();

        CreateCustomBoneIdToHumanBodyBoneMapping();
        StoreTTargetPoseRotations();

        _targetSkeletonData = new OVRSkeletonMetadata(_animatorTargetSkeleton, _bodyBonesMappingInterface);
        _targetSkeletonData.BuildCoordinateAxesForAllBones();

        PrecomputeAllRotationTweaks();
    }

    private void PrecomputeAllRotationTweaks()
    {
        if (_adjustments == null || _adjustments.Length == 0)
        {
            return;
        }
        foreach (var adjustment in _adjustments)
        {
            adjustment.PrecomputeRotationTweaks();
        }
    }

    protected virtual void OnValidate()
    {
        // Only do this from the editor.
#if UNITY_EDITOR
        if (!UnityEditor.EditorApplication.isPlaying)
        {
            return;
        }
#endif
        PrecomputeAllRotationTweaks();
    }

    internal static void ValidateGameObjectForUnityHumanoidRetargeting(GameObject go)
    {
        if (go.GetComponent<Animator>() == null)
        {
            throw new InvalidOperationException(
                $"Retargeting to Unity Humanoid requires an {nameof(Animator)} component with a humanoid avatar on T-Pose");
        }
    }

    private void StoreTTargetPoseRotations()
    {
        for (var i = HumanBodyBones.Hips; i < HumanBodyBones.LastBone; i++)
        {
            var boneTransform = _animatorTargetSkeleton.GetBoneTransform(i);
            _targetTPoseRotations[i] = boneTransform ? boneTransform.rotation : Quaternion.identity;
        }

        Transform tPoseCopy = CreateDuplicateTransformHierarchy(
            _animatorTargetSkeleton.GetBoneTransform(HumanBodyBones.Hips));
        tPoseCopy.name = $"{this.name}-tPose";
        tPoseCopy.SetParent(this.transform, false);
    }

    private Transform CreateDuplicateTransformHierarchy(
        Transform transformFromOriginalHierarchy)
    {
        var newGameObject = new GameObject(transformFromOriginalHierarchy.name + "-tPose");
        var newTransform = newGameObject.transform;
        newTransform.localPosition = transformFromOriginalHierarchy.localPosition;
        newTransform.localRotation = transformFromOriginalHierarchy.localRotation;
        newTransform.localScale = transformFromOriginalHierarchy.localScale;

        var humanBodyBone = FindHumanBodyBoneFromTransform(transformFromOriginalHierarchy);
        if (humanBodyBone != HumanBodyBones.LastBone)
        {
            _targetTPoseTransformDup[humanBodyBone] = newTransform;
        }

        foreach (Transform originalChild in transformFromOriginalHierarchy)
        {
            var newChild = CreateDuplicateTransformHierarchy(originalChild);
            newChild.SetParent(newTransform, false);
        }

        return newTransform;
    }

    private HumanBodyBones FindHumanBodyBoneFromTransform(Transform candidateTransform)
    {
        for (var i = HumanBodyBones.Hips; i < HumanBodyBones.LastBone; i++)
        {
            if (_animatorTargetSkeleton.GetBoneTransform(i) == candidateTransform)
            {
                return i;
            }
        }

        return HumanBodyBones.LastBone;
    }

    private void AlignHierarchies(Transform transformToAlign, Transform referenceTransform)
    {
        transformToAlign.localRotation = referenceTransform.localRotation;
        transformToAlign.localPosition = referenceTransform.localPosition;
        transformToAlign.localScale = referenceTransform.localScale;

        for (int i = 0; i < referenceTransform.childCount; i++)
        {
            AlignHierarchies(transformToAlign.GetChild(i),
                referenceTransform.GetChild(i));
        }
    }

    private void CreateCustomBoneIdToHumanBodyBoneMapping()
    {
        CopyBoneIdToHumanBodyBoneMapping();
        AdjustCustomBoneIdToHumanBodyBoneMapping();
    }

    private void CopyBoneIdToHumanBodyBoneMapping()
    {
        _customBoneIdToHumanBodyBone.Clear();
        if (_skeletonType == SkeletonType.FullBody)
        {
            foreach (var keyValuePair in _bodyBonesMappingInterface.GetFullBodyBoneIdToHumanBodyBone)
            {
                _customBoneIdToHumanBodyBone.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }
        else
        {
            foreach (var keyValuePair in _bodyBonesMappingInterface.GetBoneIdToHumanBodyBone)
            {
                _customBoneIdToHumanBodyBone.Add(keyValuePair.Key, keyValuePair.Value);
            }
        }
    }

    private void AdjustCustomBoneIdToHumanBodyBoneMapping()
    {
        // if there is a mapping override that the user provided,
        // enforce it.
        foreach (var adjustment in _adjustments)
        {
            bool fullJointSet = _skeletonType == SkeletonType.FullBody;
            if ((fullJointSet &&
                 adjustment.FullBodyBoneIdOverrideValue ==
                 OVRHumanBodyBonesMappings.FullBodyTrackingBoneId.NoOverride) ||
                adjustment.BoneIdOverrideValue == OVRHumanBodyBonesMappings.BodyTrackingBoneId.NoOverride)

            {
                continue;
            }
            if ((fullJointSet &&
                 adjustment.FullBodyBoneIdOverrideValue == OVRHumanBodyBonesMappings.FullBodyTrackingBoneId.Remove) ||
                adjustment.BoneIdOverrideValue == OVRHumanBodyBonesMappings.BodyTrackingBoneId.Remove)

            {
                RemoveMappingCorrespondingToHumanBodyBone(adjustment.Joint);
            }
            else
            {
                if (fullJointSet)
                {
                    _customBoneIdToHumanBodyBone[(BoneId)adjustment.FullBodyBoneIdOverrideValue]
                        = adjustment.Joint;
                }
                else
                {
                    _customBoneIdToHumanBodyBone[(BoneId)adjustment.BoneIdOverrideValue]
                        = adjustment.Joint;
                }
            }
        }
    }

    private void RemoveMappingCorrespondingToHumanBodyBone(HumanBodyBones boneId)
    {
        foreach (var key in _customBoneIdToHumanBodyBone.Keys)
        {
            if (_customBoneIdToHumanBodyBone[key] == boneId)
            {
                _customBoneIdToHumanBodyBone.Remove(key);
                return;
            }
        }
    }

    protected override void Update()
    {
        if (!ShouldRunUpdateThisFrame())
        {
            return;
        }

        UpdateSkeleton();

        RecomputeSkeletalOffsetsIfNecessary();

        AlignTargetWithSource();
    }

    protected bool ShouldRunUpdateThisFrame()
    {
        bool isFixedUpdate = Time.inFixedTimeStep;
        switch (_updateType)
        {
            case UpdateType.FixedUpdateOnly:
                return isFixedUpdate;
            case UpdateType.UpdateOnly:
                return !isFixedUpdate;
            default:
                return true;
        }
    }

    protected void RecomputeSkeletalOffsetsIfNecessary()
    {
        if (OffsetComputationNeededThisFrame())
        {
            ComputeOffsetsUsingSkeletonComponent();
        }
    }

    /// <summary>
    /// Indicates if skeletal offsets will be computed during this
    /// frame. This usually happens if there is a change in the
    /// input skeletal data, a change in the target skeletal scale,
    /// et cetera.
    /// </summary>
    /// <returns>True if computation is required; false if not.</returns>
    protected bool OffsetComputationNeededThisFrame()
    {
        bool skeletonIsNotReady = !IsInitialized ||
            BindPoses == null || BindPoses.Count == 0;
        if (skeletonIsNotReady)
        {
            return false;
        }

        bool skeletalCountChange = _lastSkelChangeCount != SkeletonChangedCount;
        bool scaleChanged =
          (transform.lossyScale - _lastTrackedScale).sqrMagnitude
          > Mathf.Epsilon;

        return skeletalCountChange || scaleChanged;
    }

    /// <summary>
    /// Compute skeletal offsets using the current input skeletal data
    /// and the target skeletal data.
    /// </summary>
    protected void ComputeOffsetsUsingSkeletonComponent()
    {
        if (!IsInitialized ||
            BindPoses == null || BindPoses.Count == 0)
        {
            return;
        }

        if (_sourceSkeletonData == null)
        {
            _sourceSkeletonData = new OVRSkeletonMetadata(this, false, _customBoneIdToHumanBodyBone,
                _skeletonType == SkeletonType.FullBody,
                _bodyBonesMappingInterface);
        }
        else
        {
            if (_skeletonType == SkeletonType.FullBody)
            {
                _sourceSkeletonData.BuildBoneDataSkeletonFullBody(this, false,
                    _customBoneIdToHumanBodyBone, _bodyBonesMappingInterface);
            }
            else
            {
                _sourceSkeletonData.BuildBoneDataSkeleton(this, false,
                    _customBoneIdToHumanBodyBone, _bodyBonesMappingInterface);
            }
        }

        _sourceSkeletonData.BuildCoordinateAxesForAllBones();

        if (_sourceSkeletonTPoseData == null)
        {
            _sourceSkeletonTPoseData = new OVRSkeletonMetadata(this, true, _customBoneIdToHumanBodyBone,
                _skeletonType == SkeletonType.FullBody,
                _bodyBonesMappingInterface);
        }
        else
        {
            if (_skeletonType == SkeletonType.FullBody)
            {
                _sourceSkeletonTPoseData.BuildBoneDataSkeletonFullBody(this, true,
                    _customBoneIdToHumanBodyBone, _bodyBonesMappingInterface);
            }
            else
            {
                _sourceSkeletonTPoseData.BuildBoneDataSkeleton(this, true,
                    _customBoneIdToHumanBodyBone, _bodyBonesMappingInterface);
            }
        }

        _sourceSkeletonTPoseData.BuildCoordinateAxesForAllBones();

        // snap the target to t-pose, then rebuild its data
        // this forces the T-pose to respect the current scale values.
        AlignHierarchies(_animatorTargetSkeleton.GetBoneTransform(HumanBodyBones.Hips),
            _targetTPoseTransformDup[HumanBodyBones.Hips]);
        _targetSkeletonData.BuildCoordinateAxesForAllBones();

        for (var i = 0; i < BindPoses.Count; i++)
        {
            if (!_customBoneIdToHumanBodyBone.TryGetValue(BindPoses[i].Id, out var humanBodyBone))
            {
                continue;
            }

            if (!_targetSkeletonData.BodyToBoneData.TryGetValue(humanBodyBone, out var targetData))
            {
                continue;
            }

            var bodySection = _bodyBonesMappingInterface.GetBoneToBodySection[humanBodyBone];

            if (!IsBodySectionInArray(bodySection,
                    _skeletonType == SkeletonType.FullBody ? _fullBodySectionsToAlign : _bodySectionsToAlign
                ))
            {
                continue;
            }

            if (!_sourceSkeletonTPoseData.BodyToBoneData.TryGetValue(humanBodyBone, out var sourceTPoseData))
            {
                continue;
            }

            if (!_sourceSkeletonData.BodyToBoneData.TryGetValue(humanBodyBone, out var sourcePoseData))
            {
                continue;
            }

            // if encountered degenerate source bones, skip
            if (sourceTPoseData.DegenerateJoint || sourcePoseData.DegenerateJoint)
            {
                targetData.CorrectionQuaternion = null;
                continue;
            }

            var forwardSource = sourceTPoseData.JointPairOrientation * Vector3.forward;
            var forwardTarget = targetData.JointPairOrientation * Vector3.forward;
            var targetToSrc = Quaternion.FromToRotation(forwardTarget, forwardSource);

            var sourceRotationValueInv = Quaternion.Inverse(BindPoses[i].Transform.rotation);

            targetData.CorrectionQuaternion =
                sourceRotationValueInv * targetToSrc *
                 _animatorTargetSkeleton.GetBoneTransform(humanBodyBone).rotation;
        }

        _lastSkelChangeCount = SkeletonChangedCount;
        _lastTrackedScale = transform.lossyScale;
    }

    protected static bool IsBodySectionInArray(
        OVRHumanBodyBonesMappings.BodySection bodySectionToCheck,
        OVRHumanBodyBonesMappings.BodySection[] sectionArrayToCheck)
    {
        foreach (var bodySection in sectionArrayToCheck)
        {
            if (bodySection == bodySectionToCheck)
            {
                return true;
            }
        }

        return false;
    }

    private void AlignTargetWithSource()
    {
        if (!IsInitialized || Bones == null || Bones.Count == 0)
        {
            return;
        }

        for (var i = 0; i < Bones.Count; i++)
        {
            if (!_customBoneIdToHumanBodyBone.TryGetValue(Bones[i].Id, out var humanBodyBone))
            {
                continue;
            }

            if (!_targetSkeletonData.BodyToBoneData.TryGetValue(humanBodyBone, out var targetData))
            {
                continue;
            }

            // Skip if we cannot map the joint at all.
            if (!targetData.CorrectionQuaternion.HasValue)
            {
                continue;
            }

            var targetJoint = targetData.OriginalJoint;
            var correctionQuaternion = targetData.CorrectionQuaternion.Value;
            var adjustment = FindAdjustment(humanBodyBone);

            var bodySectionOfJoint = _bodyBonesMappingInterface.GetBoneToBodySection[humanBodyBone];
            var shouldUpdatePosition = IsBodySectionInArray(
                bodySectionOfJoint,
                _skeletonType == SkeletonType.FullBody ? _fullBodySectionToPosition : _bodySectionToPosition

            );

            if (adjustment == null)
            {
                targetJoint.rotation = Bones[i].Transform.rotation * correctionQuaternion;
                if (shouldUpdatePosition)
                {
                    targetJoint.position = Bones[i].Transform.position;
                }
            }
            else
            {
                if (!adjustment.DisableRotationTransform)
                {
                    targetJoint.rotation = Bones[i].Transform.rotation * correctionQuaternion;
                }

                targetJoint.rotation *= adjustment.RotationChange;
                targetJoint.rotation *= adjustment.PrecomputedRotationTweaks;

                if (!adjustment.DisablePositionTransform && shouldUpdatePosition)
                {
                    targetJoint.position = Bones[i].Transform.position;
                }
                targetJoint.position += adjustment.PositionChange;
            }
        }
    }

    protected JointAdjustment FindAdjustment(HumanBodyBones boneId)
    {
        foreach (var adjustment in _adjustments)
        {
            if (adjustment.Joint == boneId)
            {
                return adjustment;
            }
        }

        return null;
    }
}
