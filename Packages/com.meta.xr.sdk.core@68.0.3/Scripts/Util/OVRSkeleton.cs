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
using UnityEngine;
using UnityEngine.Assertions;

public class OVRSkeleton : MonoBehaviour
{
    public interface IOVRSkeletonDataProvider
    {
        SkeletonType GetSkeletonType();
        SkeletonPoseData GetSkeletonPoseData();
        bool enabled { get; }
    }

    public struct SkeletonPoseData
    {
        public OVRPlugin.Posef RootPose { get; set; }
        public float RootScale { get; set; }
        public OVRPlugin.Quatf[] BoneRotations { get; set; }
        public bool IsDataValid { get; set; }
        public bool IsDataHighConfidence { get; set; }
        public OVRPlugin.Vector3f[] BoneTranslations { get; set; }
        public int SkeletonChangedCount { get; set; }
    }

    public enum SkeletonType
    {
        None = OVRPlugin.SkeletonType.None,
        HandLeft = OVRPlugin.SkeletonType.HandLeft,
        HandRight = OVRPlugin.SkeletonType.HandRight,
        Body = OVRPlugin.SkeletonType.Body,
        FullBody = OVRPlugin.SkeletonType.FullBody,
    }

    public enum BoneId
    {
        Invalid = OVRPlugin.BoneId.Invalid,

        // hand bones
        Hand_Start = OVRPlugin.BoneId.Hand_Start,
        Hand_WristRoot = OVRPlugin.BoneId.Hand_WristRoot, // root frame of the hand, where the wrist is located
        Hand_ForearmStub = OVRPlugin.BoneId.Hand_ForearmStub, // frame for user's forearm
        Hand_Thumb0 = OVRPlugin.BoneId.Hand_Thumb0, // thumb trapezium bone
        Hand_Thumb1 = OVRPlugin.BoneId.Hand_Thumb1, // thumb metacarpal bone
        Hand_Thumb2 = OVRPlugin.BoneId.Hand_Thumb2, // thumb proximal phalange bone
        Hand_Thumb3 = OVRPlugin.BoneId.Hand_Thumb3, // thumb distal phalange bone
        Hand_Index1 = OVRPlugin.BoneId.Hand_Index1, // index proximal phalange bone
        Hand_Index2 = OVRPlugin.BoneId.Hand_Index2, // index intermediate phalange bone
        Hand_Index3 = OVRPlugin.BoneId.Hand_Index3, // index distal phalange bone
        Hand_Middle1 = OVRPlugin.BoneId.Hand_Middle1, // middle proximal phalange bone
        Hand_Middle2 = OVRPlugin.BoneId.Hand_Middle2, // middle intermediate phalange bone
        Hand_Middle3 = OVRPlugin.BoneId.Hand_Middle3, // middle distal phalange bone
        Hand_Ring1 = OVRPlugin.BoneId.Hand_Ring1, // ring proximal phalange bone
        Hand_Ring2 = OVRPlugin.BoneId.Hand_Ring2, // ring intermediate phalange bone
        Hand_Ring3 = OVRPlugin.BoneId.Hand_Ring3, // ring distal phalange bone
        Hand_Pinky0 = OVRPlugin.BoneId.Hand_Pinky0, // pinky metacarpal bone
        Hand_Pinky1 = OVRPlugin.BoneId.Hand_Pinky1, // pinky proximal phalange bone
        Hand_Pinky2 = OVRPlugin.BoneId.Hand_Pinky2, // pinky intermediate phalange bone
        Hand_Pinky3 = OVRPlugin.BoneId.Hand_Pinky3, // pinky distal phalange bone
        Hand_MaxSkinnable = OVRPlugin.BoneId.Hand_MaxSkinnable,

        // Bone tips are position only. They are not used for skinning but are useful for hit-testing.
        // NOTE: Hand_ThumbTip == Hand_MaxSkinnable since the extended tips need to be contiguous
        Hand_ThumbTip = OVRPlugin.BoneId.Hand_ThumbTip, // tip of the thumb
        Hand_IndexTip = OVRPlugin.BoneId.Hand_IndexTip, // tip of the index finger
        Hand_MiddleTip = OVRPlugin.BoneId.Hand_MiddleTip, // tip of the middle finger
        Hand_RingTip = OVRPlugin.BoneId.Hand_RingTip, // tip of the ring finger
        Hand_PinkyTip = OVRPlugin.BoneId.Hand_PinkyTip, // tip of the pinky
        Hand_End = OVRPlugin.BoneId.Hand_End,


        // body bones
        Body_Start = OVRPlugin.BoneId.Body_Start,
        Body_Root = OVRPlugin.BoneId.Body_Root,
        Body_Hips = OVRPlugin.BoneId.Body_Hips,
        Body_SpineLower = OVRPlugin.BoneId.Body_SpineLower,
        Body_SpineMiddle = OVRPlugin.BoneId.Body_SpineMiddle,
        Body_SpineUpper = OVRPlugin.BoneId.Body_SpineUpper,
        Body_Chest = OVRPlugin.BoneId.Body_Chest,
        Body_Neck = OVRPlugin.BoneId.Body_Neck,
        Body_Head = OVRPlugin.BoneId.Body_Head,
        Body_LeftShoulder = OVRPlugin.BoneId.Body_LeftShoulder,
        Body_LeftScapula = OVRPlugin.BoneId.Body_LeftScapula,
        Body_LeftArmUpper = OVRPlugin.BoneId.Body_LeftArmUpper,
        Body_LeftArmLower = OVRPlugin.BoneId.Body_LeftArmLower,
        Body_LeftHandWristTwist = OVRPlugin.BoneId.Body_LeftHandWristTwist,
        Body_RightShoulder = OVRPlugin.BoneId.Body_RightShoulder,
        Body_RightScapula = OVRPlugin.BoneId.Body_RightScapula,
        Body_RightArmUpper = OVRPlugin.BoneId.Body_RightArmUpper,
        Body_RightArmLower = OVRPlugin.BoneId.Body_RightArmLower,
        Body_RightHandWristTwist = OVRPlugin.BoneId.Body_RightHandWristTwist,
        Body_LeftHandPalm = OVRPlugin.BoneId.Body_LeftHandPalm,
        Body_LeftHandWrist = OVRPlugin.BoneId.Body_LeftHandWrist,
        Body_LeftHandThumbMetacarpal = OVRPlugin.BoneId.Body_LeftHandThumbMetacarpal,
        Body_LeftHandThumbProximal = OVRPlugin.BoneId.Body_LeftHandThumbProximal,
        Body_LeftHandThumbDistal = OVRPlugin.BoneId.Body_LeftHandThumbDistal,
        Body_LeftHandThumbTip = OVRPlugin.BoneId.Body_LeftHandThumbTip,
        Body_LeftHandIndexMetacarpal = OVRPlugin.BoneId.Body_LeftHandIndexMetacarpal,
        Body_LeftHandIndexProximal = OVRPlugin.BoneId.Body_LeftHandIndexProximal,
        Body_LeftHandIndexIntermediate = OVRPlugin.BoneId.Body_LeftHandIndexIntermediate,
        Body_LeftHandIndexDistal = OVRPlugin.BoneId.Body_LeftHandIndexDistal,
        Body_LeftHandIndexTip = OVRPlugin.BoneId.Body_LeftHandIndexTip,
        Body_LeftHandMiddleMetacarpal = OVRPlugin.BoneId.Body_LeftHandMiddleMetacarpal,
        Body_LeftHandMiddleProximal = OVRPlugin.BoneId.Body_LeftHandMiddleProximal,
        Body_LeftHandMiddleIntermediate = OVRPlugin.BoneId.Body_LeftHandMiddleIntermediate,
        Body_LeftHandMiddleDistal = OVRPlugin.BoneId.Body_LeftHandMiddleDistal,
        Body_LeftHandMiddleTip = OVRPlugin.BoneId.Body_LeftHandMiddleTip,
        Body_LeftHandRingMetacarpal = OVRPlugin.BoneId.Body_LeftHandRingMetacarpal,
        Body_LeftHandRingProximal = OVRPlugin.BoneId.Body_LeftHandRingProximal,
        Body_LeftHandRingIntermediate = OVRPlugin.BoneId.Body_LeftHandRingIntermediate,
        Body_LeftHandRingDistal = OVRPlugin.BoneId.Body_LeftHandRingDistal,
        Body_LeftHandRingTip = OVRPlugin.BoneId.Body_LeftHandRingTip,
        Body_LeftHandLittleMetacarpal = OVRPlugin.BoneId.Body_LeftHandLittleMetacarpal,
        Body_LeftHandLittleProximal = OVRPlugin.BoneId.Body_LeftHandLittleProximal,
        Body_LeftHandLittleIntermediate = OVRPlugin.BoneId.Body_LeftHandLittleIntermediate,
        Body_LeftHandLittleDistal = OVRPlugin.BoneId.Body_LeftHandLittleDistal,
        Body_LeftHandLittleTip = OVRPlugin.BoneId.Body_LeftHandLittleTip,
        Body_RightHandPalm = OVRPlugin.BoneId.Body_RightHandPalm,
        Body_RightHandWrist = OVRPlugin.BoneId.Body_RightHandWrist,
        Body_RightHandThumbMetacarpal = OVRPlugin.BoneId.Body_RightHandThumbMetacarpal,
        Body_RightHandThumbProximal = OVRPlugin.BoneId.Body_RightHandThumbProximal,
        Body_RightHandThumbDistal = OVRPlugin.BoneId.Body_RightHandThumbDistal,
        Body_RightHandThumbTip = OVRPlugin.BoneId.Body_RightHandThumbTip,
        Body_RightHandIndexMetacarpal = OVRPlugin.BoneId.Body_RightHandIndexMetacarpal,
        Body_RightHandIndexProximal = OVRPlugin.BoneId.Body_RightHandIndexProximal,
        Body_RightHandIndexIntermediate = OVRPlugin.BoneId.Body_RightHandIndexIntermediate,
        Body_RightHandIndexDistal = OVRPlugin.BoneId.Body_RightHandIndexDistal,
        Body_RightHandIndexTip = OVRPlugin.BoneId.Body_RightHandIndexTip,
        Body_RightHandMiddleMetacarpal = OVRPlugin.BoneId.Body_RightHandMiddleMetacarpal,
        Body_RightHandMiddleProximal = OVRPlugin.BoneId.Body_RightHandMiddleProximal,
        Body_RightHandMiddleIntermediate = OVRPlugin.BoneId.Body_RightHandMiddleIntermediate,
        Body_RightHandMiddleDistal = OVRPlugin.BoneId.Body_RightHandMiddleDistal,
        Body_RightHandMiddleTip = OVRPlugin.BoneId.Body_RightHandMiddleTip,
        Body_RightHandRingMetacarpal = OVRPlugin.BoneId.Body_RightHandRingMetacarpal,
        Body_RightHandRingProximal = OVRPlugin.BoneId.Body_RightHandRingProximal,
        Body_RightHandRingIntermediate = OVRPlugin.BoneId.Body_RightHandRingIntermediate,
        Body_RightHandRingDistal = OVRPlugin.BoneId.Body_RightHandRingDistal,
        Body_RightHandRingTip = OVRPlugin.BoneId.Body_RightHandRingTip,
        Body_RightHandLittleMetacarpal = OVRPlugin.BoneId.Body_RightHandLittleMetacarpal,
        Body_RightHandLittleProximal = OVRPlugin.BoneId.Body_RightHandLittleProximal,
        Body_RightHandLittleIntermediate = OVRPlugin.BoneId.Body_RightHandLittleIntermediate,
        Body_RightHandLittleDistal = OVRPlugin.BoneId.Body_RightHandLittleDistal,
        Body_RightHandLittleTip = OVRPlugin.BoneId.Body_RightHandLittleTip,
        Body_End = OVRPlugin.BoneId.Body_End,

        // Full body bones
        FullBody_Start = OVRPlugin.BoneId.FullBody_Start,
        FullBody_Root = OVRPlugin.BoneId.FullBody_Root,
        FullBody_Hips = OVRPlugin.BoneId.FullBody_Hips,
        FullBody_SpineLower = OVRPlugin.BoneId.FullBody_SpineLower,
        FullBody_SpineMiddle = OVRPlugin.BoneId.FullBody_SpineMiddle,
        FullBody_SpineUpper = OVRPlugin.BoneId.FullBody_SpineUpper,
        FullBody_Chest = OVRPlugin.BoneId.FullBody_Chest,
        FullBody_Neck = OVRPlugin.BoneId.FullBody_Neck,
        FullBody_Head = OVRPlugin.BoneId.FullBody_Head,
        FullBody_LeftShoulder = OVRPlugin.BoneId.FullBody_LeftShoulder,
        FullBody_LeftScapula = OVRPlugin.BoneId.FullBody_LeftScapula,
        FullBody_LeftArmUpper = OVRPlugin.BoneId.FullBody_LeftArmUpper,
        FullBody_LeftArmLower = OVRPlugin.BoneId.FullBody_LeftArmLower,
        FullBody_LeftHandWristTwist = OVRPlugin.BoneId.FullBody_LeftHandWristTwist,
        FullBody_RightShoulder = OVRPlugin.BoneId.FullBody_RightShoulder,
        FullBody_RightScapula = OVRPlugin.BoneId.FullBody_RightScapula,
        FullBody_RightArmUpper = OVRPlugin.BoneId.FullBody_RightArmUpper,
        FullBody_RightArmLower = OVRPlugin.BoneId.FullBody_RightArmLower,
        FullBody_RightHandWristTwist = OVRPlugin.BoneId.FullBody_RightHandWristTwist,
        FullBody_LeftHandPalm = OVRPlugin.BoneId.FullBody_LeftHandPalm,
        FullBody_LeftHandWrist = OVRPlugin.BoneId.FullBody_LeftHandWrist,
        FullBody_LeftHandThumbMetacarpal = OVRPlugin.BoneId.FullBody_LeftHandThumbMetacarpal,
        FullBody_LeftHandThumbProximal = OVRPlugin.BoneId.FullBody_LeftHandThumbProximal,
        FullBody_LeftHandThumbDistal = OVRPlugin.BoneId.FullBody_LeftHandThumbDistal,
        FullBody_LeftHandThumbTip = OVRPlugin.BoneId.FullBody_LeftHandThumbTip,
        FullBody_LeftHandIndexMetacarpal = OVRPlugin.BoneId.FullBody_LeftHandIndexMetacarpal,
        FullBody_LeftHandIndexProximal = OVRPlugin.BoneId.FullBody_LeftHandIndexProximal,
        FullBody_LeftHandIndexIntermediate = OVRPlugin.BoneId.FullBody_LeftHandIndexIntermediate,
        FullBody_LeftHandIndexDistal = OVRPlugin.BoneId.FullBody_LeftHandIndexDistal,
        FullBody_LeftHandIndexTip = OVRPlugin.BoneId.FullBody_LeftHandIndexTip,
        FullBody_LeftHandMiddleMetacarpal = OVRPlugin.BoneId.FullBody_LeftHandMiddleMetacarpal,
        FullBody_LeftHandMiddleProximal = OVRPlugin.BoneId.FullBody_LeftHandMiddleProximal,
        FullBody_LeftHandMiddleIntermediate = OVRPlugin.BoneId.FullBody_LeftHandMiddleIntermediate,
        FullBody_LeftHandMiddleDistal = OVRPlugin.BoneId.FullBody_LeftHandMiddleDistal,
        FullBody_LeftHandMiddleTip = OVRPlugin.BoneId.FullBody_LeftHandMiddleTip,
        FullBody_LeftHandRingMetacarpal = OVRPlugin.BoneId.FullBody_LeftHandRingMetacarpal,
        FullBody_LeftHandRingProximal = OVRPlugin.BoneId.FullBody_LeftHandRingProximal,
        FullBody_LeftHandRingIntermediate = OVRPlugin.BoneId.FullBody_LeftHandRingIntermediate,
        FullBody_LeftHandRingDistal = OVRPlugin.BoneId.FullBody_LeftHandRingDistal,
        FullBody_LeftHandRingTip = OVRPlugin.BoneId.FullBody_LeftHandRingTip,
        FullBody_LeftHandLittleMetacarpal = OVRPlugin.BoneId.FullBody_LeftHandLittleMetacarpal,
        FullBody_LeftHandLittleProximal = OVRPlugin.BoneId.FullBody_LeftHandLittleProximal,
        FullBody_LeftHandLittleIntermediate = OVRPlugin.BoneId.FullBody_LeftHandLittleIntermediate,
        FullBody_LeftHandLittleDistal = OVRPlugin.BoneId.FullBody_LeftHandLittleDistal,
        FullBody_LeftHandLittleTip = OVRPlugin.BoneId.FullBody_LeftHandLittleTip,
        FullBody_RightHandPalm = OVRPlugin.BoneId.FullBody_RightHandPalm,
        FullBody_RightHandWrist = OVRPlugin.BoneId.FullBody_RightHandWrist,
        FullBody_RightHandThumbMetacarpal = OVRPlugin.BoneId.FullBody_RightHandThumbMetacarpal,
        FullBody_RightHandThumbProximal = OVRPlugin.BoneId.FullBody_RightHandThumbProximal,
        FullBody_RightHandThumbDistal = OVRPlugin.BoneId.FullBody_RightHandThumbDistal,
        FullBody_RightHandThumbTip = OVRPlugin.BoneId.FullBody_RightHandThumbTip,
        FullBody_RightHandIndexMetacarpal = OVRPlugin.BoneId.FullBody_RightHandIndexMetacarpal,
        FullBody_RightHandIndexProximal = OVRPlugin.BoneId.FullBody_RightHandIndexProximal,
        FullBody_RightHandIndexIntermediate = OVRPlugin.BoneId.FullBody_RightHandIndexIntermediate,
        FullBody_RightHandIndexDistal = OVRPlugin.BoneId.FullBody_RightHandIndexDistal,
        FullBody_RightHandIndexTip = OVRPlugin.BoneId.FullBody_RightHandIndexTip,
        FullBody_RightHandMiddleMetacarpal = OVRPlugin.BoneId.FullBody_RightHandMiddleMetacarpal,
        FullBody_RightHandMiddleProximal = OVRPlugin.BoneId.FullBody_RightHandMiddleProximal,
        FullBody_RightHandMiddleIntermediate = OVRPlugin.BoneId.FullBody_RightHandMiddleIntermediate,
        FullBody_RightHandMiddleDistal = OVRPlugin.BoneId.FullBody_RightHandMiddleDistal,
        FullBody_RightHandMiddleTip = OVRPlugin.BoneId.FullBody_RightHandMiddleTip,
        FullBody_RightHandRingMetacarpal = OVRPlugin.BoneId.FullBody_RightHandRingMetacarpal,
        FullBody_RightHandRingProximal = OVRPlugin.BoneId.FullBody_RightHandRingProximal,
        FullBody_RightHandRingIntermediate = OVRPlugin.BoneId.FullBody_RightHandRingIntermediate,
        FullBody_RightHandRingDistal = OVRPlugin.BoneId.FullBody_RightHandRingDistal,
        FullBody_RightHandRingTip = OVRPlugin.BoneId.FullBody_RightHandRingTip,
        FullBody_RightHandLittleMetacarpal = OVRPlugin.BoneId.FullBody_RightHandLittleMetacarpal,
        FullBody_RightHandLittleProximal = OVRPlugin.BoneId.FullBody_RightHandLittleProximal,
        FullBody_RightHandLittleIntermediate = OVRPlugin.BoneId.FullBody_RightHandLittleIntermediate,
        FullBody_RightHandLittleDistal = OVRPlugin.BoneId.FullBody_RightHandLittleDistal,
        FullBody_RightHandLittleTip = OVRPlugin.BoneId.FullBody_RightHandLittleTip,
        FullBody_LeftUpperLeg = OVRPlugin.BoneId.FullBody_LeftUpperLeg,
        FullBody_LeftLowerLeg = OVRPlugin.BoneId.FullBody_LeftLowerLeg,
        FullBody_LeftFootAnkleTwist = OVRPlugin.BoneId.FullBody_LeftFootAnkleTwist,
        FullBody_LeftFootAnkle = OVRPlugin.BoneId.FullBody_LeftFootAnkle,
        FullBody_LeftFootSubtalar = OVRPlugin.BoneId.FullBody_LeftFootSubtalar,
        FullBody_LeftFootTransverse = OVRPlugin.BoneId.FullBody_LeftFootTransverse,
        FullBody_LeftFootBall = OVRPlugin.BoneId.FullBody_LeftFootBall,
        FullBody_RightUpperLeg = OVRPlugin.BoneId.FullBody_RightUpperLeg,
        FullBody_RightLowerLeg = OVRPlugin.BoneId.FullBody_RightLowerLeg,
        FullBody_RightFootAnkleTwist = OVRPlugin.BoneId.FullBody_RightFootAnkleTwist,
        FullBody_RightFootAnkle = OVRPlugin.BoneId.FullBody_RightFootAnkle,
        FullBody_RightFootSubtalar = OVRPlugin.BoneId.FullBody_RightFootSubtalar,
        FullBody_RightFootTransverse = OVRPlugin.BoneId.FullBody_RightFootTransverse,
        FullBody_RightFootBall = OVRPlugin.BoneId.FullBody_RightFootBall,
        FullBody_End = OVRPlugin.BoneId.FullBody_End,

        // add new bones here

        Max = OVRPlugin.BoneId.Max,
    }

    [SerializeField]
    protected SkeletonType _skeletonType = SkeletonType.None;

    [SerializeField]
    private IOVRSkeletonDataProvider _dataProvider;

    [SerializeField]
    private bool _updateRootPose = false;

    [SerializeField]
    private bool _updateRootScale = false;

    [SerializeField]
    private bool _enablePhysicsCapsules = false;

    [SerializeField]
    private bool _applyBoneTranslations = true;

    private GameObject _bonesGO;
    private GameObject _bindPosesGO;
    private GameObject _capsulesGO;

    protected List<OVRBone> _bones;
    private List<OVRBone> _bindPoses;
    private List<OVRBoneCapsule> _capsules;

    protected OVRPlugin.Skeleton2 _skeleton = new OVRPlugin.Skeleton2();
    private readonly Quaternion wristFixupRotation = new Quaternion(0.0f, 1.0f, 0.0f, 0.0f);

    public bool IsInitialized { get; private set; }
    public bool IsDataValid { get; private set; }
    public bool IsDataHighConfidence { get; private set; }
    public IList<OVRBone> Bones { get; protected set; }
    public IList<OVRBone> BindPoses { get; private set; }
    public IList<OVRBoneCapsule> Capsules { get; private set; }

    public SkeletonType GetSkeletonType()
    {
        return _skeletonType;
    }

    internal virtual void SetSkeletonType(SkeletonType type)
    {
        bool shouldUpdate = IsInitialized && type != _skeletonType;
        _skeletonType = type;
        if (shouldUpdate)
        {
            Initialize();
            var meshRenderer = GetComponent<OVRMeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.ForceRebind();
            }
        }
    }

    internal OVRPlugin.BodyJointSet GetRequiredBodyJointSet()
    {
        return _skeletonType switch
        {
            SkeletonType.Body => OVRPlugin.BodyJointSet.UpperBody,
            SkeletonType.FullBody => OVRPlugin.BodyJointSet.FullBody,
            _ => OVRPlugin.BodyJointSet.None
        };
    }

    public bool IsValidBone(BoneId bone)
    {
        return OVRPlugin.IsValidBone((OVRPlugin.BoneId)bone, (OVRPlugin.SkeletonType)_skeletonType);
    }

    public int SkeletonChangedCount { get; private set; }

    protected virtual void Awake()
    {
        if (_dataProvider == null)
        {
            var foundDataProvider = SearchSkeletonDataProvider();
            if (foundDataProvider != null)
            {
                _dataProvider = foundDataProvider;
                if (_dataProvider is MonoBehaviour mb)
                {
                    Debug.Log($"Found IOVRSkeletonDataProvider reference in {mb.name} due to unassigned field.");
                }
            }
        }

        _bones = new List<OVRBone>();
        Bones = _bones.AsReadOnly();

        _bindPoses = new List<OVRBone>();
        BindPoses = _bindPoses.AsReadOnly();

        _capsules = new List<OVRBoneCapsule>();
        Capsules = _capsules.AsReadOnly();
    }

    internal IOVRSkeletonDataProvider SearchSkeletonDataProvider()
    {

        var oldProviders = gameObject.GetComponentsInParent<IOVRSkeletonDataProvider>(true);
        foreach (var dataProvider in oldProviders)
        {
            if (dataProvider.GetSkeletonType() == _skeletonType)
            {
                return dataProvider;
            }
        }

        return null;
    }

    /// <summary>
    /// Start this instance.
    /// Initialize data structures.
    /// </summary>
    protected virtual void Start()
    {
        if (_dataProvider == null && _skeletonType == SkeletonType.Body)
        {
            Debug.LogWarning("OVRSkeleton and its subclasses requires OVRBody to function.");
        }

        if (ShouldInitialize())
        {
            Initialize();
        }
    }

    private bool ShouldInitialize()
    {
        if (IsInitialized)
        {
            return false;
        }

        if (_dataProvider != null && !_dataProvider.enabled)
        {
            return false;
        }

        if (_skeletonType == SkeletonType.None)
        {
            return false;
        }
        else if (IsHandSkeleton(_skeletonType))
        {
#if UNITY_EDITOR
            return OVRInput.IsControllerConnected(OVRInput.Controller.Hands);
#else
            return true;
#endif
        }
        else
        {
            return true;
        }
    }

    private void Initialize()
    {
        if (OVRPlugin.GetSkeleton2((OVRPlugin.SkeletonType)_skeletonType, ref _skeleton))
        {
            InitializeBones();
            InitializeBindPose();
            InitializeCapsules();

            IsInitialized = true;
        }
    }

    protected virtual Transform GetBoneTransform(BoneId boneId) => null;

    protected virtual void InitializeBones()
    {
        bool flipX = IsHandSkeleton(_skeletonType);

        if (!_bonesGO)
        {
            _bonesGO = new GameObject("Bones");
            _bonesGO.transform.SetParent(transform, false);
            _bonesGO.transform.localPosition = Vector3.zero;
            _bonesGO.transform.localRotation = Quaternion.identity;
        }

        if (_bones == null || _bones.Count != _skeleton.NumBones)
        {
            if (_bones != null)
            {
                for (int i = 0; i < _bones.Count; i++)
                {
                    _bones[i].Dispose();
                }
                _bones.Clear();
            }

            _bones = new List<OVRBone>(new OVRBone[_skeleton.NumBones]);
            Bones = _bones.AsReadOnly();
        }

        bool newBonesCreated = false;
        // pre-populate bones list before attempting to apply bone hierarchy
        for (int i = 0; i < _bones.Count; ++i)
        {
            OVRBone bone = _bones[i] ?? (_bones[i] = new OVRBone());
            bone.Id = (OVRSkeleton.BoneId)_skeleton.Bones[i].Id;
            bone.ParentBoneIndex = _skeleton.Bones[i].ParentBoneIndex;
            Assert.IsTrue((int)bone.Id >= 0 && bone.Id <= BoneId.Max);

            // don't create new bones each time; rely on
            // pre-existing bone transforms.
            if (bone.Transform == null)
            {
                newBonesCreated = true;
                bone.Transform = GetBoneTransform(bone.Id);
                if (bone.Transform == null)
                {
                    bone.Transform = new GameObject(BoneLabelFromBoneId(_skeletonType, bone.Id)).transform;
                }
            }

            // if allocated bone here before, make sure the name is correct.
            if (GetBoneTransform(bone.Id) == null)
            {
                bone.Transform.name = BoneLabelFromBoneId(_skeletonType, bone.Id);
            }
        }

        if (newBonesCreated)
        {
            for (int i = 0; i < _bones.Count; ++i)
            {
                if (!IsValidBone((BoneId)_bones[i].ParentBoneIndex) ||
                    IsBodySkeleton(_skeletonType))  // Body bones are always in tracking space
                {
                    _bones[i].Transform.SetParent(_bonesGO.transform, false);
                }
                else
                {
                    _bones[i].Transform.SetParent(_bones[_bones[i].ParentBoneIndex].Transform, false);
                }
            }
        }

        for (int i = 0; i < _bones.Count; i++)
        {
            var bone = _bones[i];
            var pose = _skeleton.Bones[i].Pose;

            if (_applyBoneTranslations)
            {
                bone.Transform.localPosition = flipX
                    ? pose.Position.FromFlippedXVector3f()
                    : pose.Position.FromFlippedZVector3f();
            }

            bone.Transform.localRotation = (flipX
                ? pose.Orientation.FromFlippedXQuatf()
                : pose.Orientation.FromFlippedZQuatf());
        }
    }

    protected virtual void InitializeBindPose()
    {
        if (!_bindPosesGO)
        {
            _bindPosesGO = new GameObject("BindPoses");
            _bindPosesGO.transform.SetParent(transform, false);
            _bindPosesGO.transform.localPosition = Vector3.zero;
            _bindPosesGO.transform.localRotation = Quaternion.identity;
        }

        if (_bindPoses != null)
        {
            for (int i = 0; i < _bindPoses.Count; i++)
            {
                _bindPoses[i].Dispose();
            }
            _bindPoses.Clear();
        }

        // Clear previous bind pose objects.
        // To prevent them lingering when you change skeleton version.
        if (_bindPosesGO != null)
        {
            List<Transform> bindPoseChildren = new List<Transform>();
            for (int i = 0; i < _bindPosesGO.transform.childCount; i++)
            {
                bindPoseChildren.Add(_bindPosesGO.transform.GetChild(i));
            }

            for (int i = 0; i < bindPoseChildren.Count; i++)
            {
                Destroy(bindPoseChildren[i].gameObject);
            }
        }

        if (_bindPoses == null || _bindPoses.Count != _bones.Count)
        {
            _bindPoses = new List<OVRBone>(new OVRBone[_bones.Count]);
            BindPoses = _bindPoses.AsReadOnly();
        }

        // pre-populate bones list before attempting to apply bone hierarchy
        for (int i = 0; i < _bindPoses.Count; ++i)
        {
            OVRBone bone = _bones[i];
            OVRBone bindPoseBone = _bindPoses[i] ?? (_bindPoses[i] = new OVRBone());
            bindPoseBone.Id = bone.Id;
            bindPoseBone.ParentBoneIndex = bone.ParentBoneIndex;

            Transform trans = bindPoseBone.Transform
                ? bindPoseBone.Transform
                : (bindPoseBone.Transform =
                    new GameObject(BoneLabelFromBoneId(_skeletonType, bindPoseBone.Id)).transform);
            trans.localPosition = bone.Transform.localPosition;
            trans.localRotation = bone.Transform.localRotation;
        }

        for (int i = 0; i < _bindPoses.Count; ++i)
        {
            if (!IsValidBone((BoneId)_bindPoses[i].ParentBoneIndex) ||
                IsBodySkeleton(_skeletonType)) // Body bones are always in tracking space
            {
                _bindPoses[i].Transform.SetParent(_bindPosesGO.transform, false);
            }
            else
            {
                _bindPoses[i].Transform.SetParent(_bindPoses[_bindPoses[i].ParentBoneIndex].Transform, false);
            }
        }
    }

    private void InitializeCapsules()
    {
        bool flipX = IsHandSkeleton(_skeletonType);

        if (_enablePhysicsCapsules)
        {
            if (!_capsulesGO)
            {
                _capsulesGO = new GameObject("Capsules");
                _capsulesGO.transform.SetParent(transform, false);
                _capsulesGO.transform.localPosition = Vector3.zero;
                _capsulesGO.transform.localRotation = Quaternion.identity;
            }

            if (_capsules != null)
            {
                for (int i = 0; i < _capsules.Count; i++)
                {
                    _capsules[i].Cleanup();
                }
                _capsules.Clear();
            }

            if (_capsules == null || _capsules.Count != _skeleton.NumBoneCapsules)
            {
                _capsules = new List<OVRBoneCapsule>(new OVRBoneCapsule[_skeleton.NumBoneCapsules]);
                Capsules = _capsules.AsReadOnly();
            }

            for (int i = 0; i < _capsules.Count; ++i)
            {
                OVRBone bone = _bones[_skeleton.BoneCapsules[i].BoneIndex];
                OVRBoneCapsule capsule = _capsules[i] ?? (_capsules[i] = new OVRBoneCapsule());
                capsule.BoneIndex = _skeleton.BoneCapsules[i].BoneIndex;

                if (capsule.CapsuleRigidbody == null)
                {
                    capsule.CapsuleRigidbody =
                        new GameObject(BoneLabelFromBoneId(_skeletonType, bone.Id) + "_CapsuleRigidbody")
                            .AddComponent<Rigidbody>();
                    capsule.CapsuleRigidbody.mass = 1.0f;
                    capsule.CapsuleRigidbody.isKinematic = true;
                    capsule.CapsuleRigidbody.useGravity = false;
                    capsule.CapsuleRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }

                GameObject rbGO = capsule.CapsuleRigidbody.gameObject;
                rbGO.transform.SetParent(_capsulesGO.transform, false);
                rbGO.transform.position = bone.Transform.position;
                rbGO.transform.rotation = bone.Transform.rotation;

                if (capsule.CapsuleCollider == null)
                {
                    capsule.CapsuleCollider =
                        new GameObject(BoneLabelFromBoneId(_skeletonType, bone.Id) + "_CapsuleCollider")
                            .AddComponent<CapsuleCollider>();
                    capsule.CapsuleCollider.isTrigger = false;
                }

                var p0 = flipX
                    ? _skeleton.BoneCapsules[i].StartPoint.FromFlippedXVector3f()
                    : _skeleton.BoneCapsules[i].StartPoint.FromFlippedZVector3f();
                var p1 = flipX
                    ? _skeleton.BoneCapsules[i].EndPoint.FromFlippedXVector3f()
                    : _skeleton.BoneCapsules[i].EndPoint.FromFlippedZVector3f();
                var delta = p1 - p0;
                var mag = delta.magnitude;
                var rot = Quaternion.FromToRotation(Vector3.right, delta);
                capsule.CapsuleCollider.radius = _skeleton.BoneCapsules[i].Radius;
                capsule.CapsuleCollider.height = mag + _skeleton.BoneCapsules[i].Radius * 2.0f;
                capsule.CapsuleCollider.direction = 0;
                capsule.CapsuleCollider.center = Vector3.right * mag * 0.5f;

                GameObject ccGO = capsule.CapsuleCollider.gameObject;
                ccGO.transform.SetParent(rbGO.transform, false);
                ccGO.transform.localPosition = p0;
                ccGO.transform.localRotation = rot;
            }
        }
    }

    protected virtual void Update()
    {
        UpdateSkeleton();
    }

    protected void UpdateSkeleton()
    {
        if (ShouldInitialize())
        {
            Initialize();
        }

        if (!IsInitialized || _dataProvider == null)
        {
            IsDataValid = false;
            IsDataHighConfidence = false;
            return;
        }

        var data = _dataProvider.GetSkeletonPoseData();

        IsDataValid = data.IsDataValid;

        if (!data.IsDataValid)
        {
            return;
        }

        if (SkeletonChangedCount != data.SkeletonChangedCount)
        {
            SkeletonChangedCount = data.SkeletonChangedCount;
            IsInitialized = false;
            Initialize();
        }

        IsDataHighConfidence = data.IsDataHighConfidence;

        if (_updateRootPose)
        {
            transform.localPosition = data.RootPose.Position.FromFlippedZVector3f();
            transform.localRotation = data.RootPose.Orientation.FromFlippedZQuatf();
        }

        if (_updateRootScale)
        {
            transform.localScale = new Vector3(data.RootScale, data.RootScale, data.RootScale);
        }

        for (var i = 0; i < _bones.Count; ++i)
        {
            var boneTransform = _bones[i].Transform;
            if (boneTransform == null) continue;

            if (IsBodySkeleton(_skeletonType))
            {
                boneTransform.localPosition = data.BoneTranslations[i].FromFlippedZVector3f();
                boneTransform.localRotation = data.BoneRotations[i].FromFlippedZQuatf();
            }
            else if (IsHandSkeleton(_skeletonType))
            {
                boneTransform.localRotation = data.BoneRotations[i].FromFlippedXQuatf();

                if (_bones[i].Id == BoneId.Hand_WristRoot)
                {
                    boneTransform.localRotation *= wristFixupRotation;
                }
            }
            else
            {
                boneTransform.localRotation = data.BoneRotations[i].FromFlippedZQuatf();
            }
        }
    }

    protected void FixedUpdate()
    {
        if (!IsInitialized || _dataProvider == null)
        {
            IsDataValid = false;
            IsDataHighConfidence = false;

            return;
        }

        Update();

        if (_enablePhysicsCapsules)
        {
            var data = _dataProvider.GetSkeletonPoseData();

            IsDataValid = data.IsDataValid;
            IsDataHighConfidence = data.IsDataHighConfidence;

            for (int i = 0; i < _capsules.Count; ++i)
            {
                OVRBoneCapsule capsule = _capsules[i];
                var capsuleGO = capsule.CapsuleRigidbody.gameObject;

                if (data.IsDataValid && data.IsDataHighConfidence)
                {
                    Transform bone = _bones[(int)capsule.BoneIndex].Transform;

                    if (capsuleGO.activeSelf)
                    {
                        capsule.CapsuleRigidbody.MovePosition(bone.position);
                        capsule.CapsuleRigidbody.MoveRotation(bone.rotation);
                    }
                    else
                    {
                        capsuleGO.SetActive(true);
                        capsule.CapsuleRigidbody.position = bone.position;
                        capsule.CapsuleRigidbody.rotation = bone.rotation;
                    }
                }
                else
                {
                    if (capsuleGO.activeSelf)
                    {
                        capsuleGO.SetActive(false);
                    }
                }
            }
        }
    }

    public BoneId GetCurrentStartBoneId()
    {
        switch (_skeletonType)
        {
            case SkeletonType.HandLeft:
            case SkeletonType.HandRight:
                return BoneId.Hand_Start;
            case SkeletonType.Body:
                return BoneId.Body_Start;
            case SkeletonType.FullBody:
                return BoneId.FullBody_Start;
            case SkeletonType.None:
            default:
                return BoneId.Invalid;
        }
    }

    public BoneId GetCurrentEndBoneId()
    {
        switch (_skeletonType)
        {
            case SkeletonType.HandLeft:
            case SkeletonType.HandRight:
                return BoneId.Hand_End;
            case SkeletonType.Body:
                return BoneId.Body_End;
            case SkeletonType.FullBody:
                return BoneId.FullBody_End;
            case SkeletonType.None:
            default:
                return BoneId.Invalid;
        }
    }

    private BoneId GetCurrentMaxSkinnableBoneId()
    {
        switch (_skeletonType)
        {
            case SkeletonType.HandLeft:
            case SkeletonType.HandRight:
                return BoneId.Hand_MaxSkinnable;
            case SkeletonType.Body:
                return BoneId.Body_End;
            case SkeletonType.FullBody:
                return BoneId.FullBody_End;
            case SkeletonType.None:
            default:
                return BoneId.Invalid;
        }
    }

    public int GetCurrentNumBones()
    {
        switch (_skeletonType)
        {
            case SkeletonType.HandLeft:
            case SkeletonType.HandRight:
            case SkeletonType.Body:
            case SkeletonType.FullBody:
                return GetCurrentEndBoneId() - GetCurrentStartBoneId();
            case SkeletonType.None:
            default:
                return 0;
        }
    }

    public int GetCurrentNumSkinnableBones()
    {
        switch (_skeletonType)
        {
            case SkeletonType.HandLeft:
            case SkeletonType.HandRight:
            case SkeletonType.Body:
            case SkeletonType.FullBody:
                return GetCurrentMaxSkinnableBoneId() - GetCurrentStartBoneId();
            case SkeletonType.None:
            default:
                return 0;
        }
    }

    // force aliased enum values to the more appropriate value
    public static string BoneLabelFromBoneId(SkeletonType skeletonType, BoneId boneId)
    {
        if (skeletonType == SkeletonType.Body)
        {
            switch (boneId)
            {
                case BoneId.Body_Root:
                    return "Body_Root";
                case BoneId.Body_Hips:
                    return "Body_Hips";
                case BoneId.Body_SpineLower:
                    return "Body_SpineLower";
                case BoneId.Body_SpineMiddle:
                    return "Body_SpineMiddle";
                case BoneId.Body_SpineUpper:
                    return "Body_SpineUpper";
                case BoneId.Body_Chest:
                    return "Body_Chest";
                case BoneId.Body_Neck:
                    return "Body_Neck";
                case BoneId.Body_Head:
                    return "Body_Head";
                case BoneId.Body_LeftShoulder:
                    return "Body_LeftShoulder";
                case BoneId.Body_LeftScapula:
                    return "Body_LeftScapula";
                case BoneId.Body_LeftArmUpper:
                    return "Body_LeftArmUpper";
                case BoneId.Body_LeftArmLower:
                    return "Body_LeftArmLower";
                case BoneId.Body_LeftHandWristTwist:
                    return "Body_LeftHandWristTwist";
                case BoneId.Body_RightShoulder:
                    return "Body_RightShoulder";
                case BoneId.Body_RightScapula:
                    return "Body_RightScapula";
                case BoneId.Body_RightArmUpper:
                    return "Body_RightArmUpper";
                case BoneId.Body_RightArmLower:
                    return "Body_RightArmLower";
                case BoneId.Body_RightHandWristTwist:
                    return "Body_RightHandWristTwist";
                case BoneId.Body_LeftHandPalm:
                    return "Body_LeftHandPalm";
                case BoneId.Body_LeftHandWrist:
                    return "Body_LeftHandWrist";
                case BoneId.Body_LeftHandThumbMetacarpal:
                    return "Body_LeftHandThumbMetacarpal";
                case BoneId.Body_LeftHandThumbProximal:
                    return "Body_LeftHandThumbProximal";
                case BoneId.Body_LeftHandThumbDistal:
                    return "Body_LeftHandThumbDistal";
                case BoneId.Body_LeftHandThumbTip:
                    return "Body_LeftHandThumbTip";
                case BoneId.Body_LeftHandIndexMetacarpal:
                    return "Body_LeftHandIndexMetacarpal";
                case BoneId.Body_LeftHandIndexProximal:
                    return "Body_LeftHandIndexProximal";
                case BoneId.Body_LeftHandIndexIntermediate:
                    return "Body_LeftHandIndexIntermediate";
                case BoneId.Body_LeftHandIndexDistal:
                    return "Body_LeftHandIndexDistal";
                case BoneId.Body_LeftHandIndexTip:
                    return "Body_LeftHandIndexTip";
                case BoneId.Body_LeftHandMiddleMetacarpal:
                    return "Body_LeftHandMiddleMetacarpal";
                case BoneId.Body_LeftHandMiddleProximal:
                    return "Body_LeftHandMiddleProximal";
                case BoneId.Body_LeftHandMiddleIntermediate:
                    return "Body_LeftHandMiddleIntermediate";
                case BoneId.Body_LeftHandMiddleDistal:
                    return "Body_LeftHandMiddleDistal";
                case BoneId.Body_LeftHandMiddleTip:
                    return "Body_LeftHandMiddleTip";
                case BoneId.Body_LeftHandRingMetacarpal:
                    return "Body_LeftHandRingMetacarpal";
                case BoneId.Body_LeftHandRingProximal:
                    return "Body_LeftHandRingProximal";
                case BoneId.Body_LeftHandRingIntermediate:
                    return "Body_LeftHandRingIntermediate";
                case BoneId.Body_LeftHandRingDistal:
                    return "Body_LeftHandRingDistal";
                case BoneId.Body_LeftHandRingTip:
                    return "Body_LeftHandRingTip";
                case BoneId.Body_LeftHandLittleMetacarpal:
                    return "Body_LeftHandLittleMetacarpal";
                case BoneId.Body_LeftHandLittleProximal:
                    return "Body_LeftHandLittleProximal";
                case BoneId.Body_LeftHandLittleIntermediate:
                    return "Body_LeftHandLittleIntermediate";
                case BoneId.Body_LeftHandLittleDistal:
                    return "Body_LeftHandLittleDistal";
                case BoneId.Body_LeftHandLittleTip:
                    return "Body_LeftHandLittleTip";
                case BoneId.Body_RightHandPalm:
                    return "Body_RightHandPalm";
                case BoneId.Body_RightHandWrist:
                    return "Body_RightHandWrist";
                case BoneId.Body_RightHandThumbMetacarpal:
                    return "Body_RightHandThumbMetacarpal";
                case BoneId.Body_RightHandThumbProximal:
                    return "Body_RightHandThumbProximal";
                case BoneId.Body_RightHandThumbDistal:
                    return "Body_RightHandThumbDistal";
                case BoneId.Body_RightHandThumbTip:
                    return "Body_RightHandThumbTip";
                case BoneId.Body_RightHandIndexMetacarpal:
                    return "Body_RightHandIndexMetacarpal";
                case BoneId.Body_RightHandIndexProximal:
                    return "Body_RightHandIndexProximal";
                case BoneId.Body_RightHandIndexIntermediate:
                    return "Body_RightHandIndexIntermediate";
                case BoneId.Body_RightHandIndexDistal:
                    return "Body_RightHandIndexDistal";
                case BoneId.Body_RightHandIndexTip:
                    return "Body_RightHandIndexTip";
                case BoneId.Body_RightHandMiddleMetacarpal:
                    return "Body_RightHandMiddleMetacarpal";
                case BoneId.Body_RightHandMiddleProximal:
                    return "Body_RightHandMiddleProximal";
                case BoneId.Body_RightHandMiddleIntermediate:
                    return "Body_RightHandMiddleIntermediate";
                case BoneId.Body_RightHandMiddleDistal:
                    return "Body_RightHandMiddleDistal";
                case BoneId.Body_RightHandMiddleTip:
                    return "Body_RightHandMiddleTip";
                case BoneId.Body_RightHandRingMetacarpal:
                    return "Body_RightHandRingMetacarpal";
                case BoneId.Body_RightHandRingProximal:
                    return "Body_RightHandRingProximal";
                case BoneId.Body_RightHandRingIntermediate:
                    return "Body_RightHandRingIntermediate";
                case BoneId.Body_RightHandRingDistal:
                    return "Body_RightHandRingDistal";
                case BoneId.Body_RightHandRingTip:
                    return "Body_RightHandRingTip";
                case BoneId.Body_RightHandLittleMetacarpal:
                    return "Body_RightHandLittleMetacarpal";
                case BoneId.Body_RightHandLittleProximal:
                    return "Body_RightHandLittleProximal";
                case BoneId.Body_RightHandLittleIntermediate:
                    return "Body_RightHandLittleIntermediate";
                case BoneId.Body_RightHandLittleDistal:
                    return "Body_RightHandLittleDistal";
                case BoneId.Body_RightHandLittleTip:
                    return "Body_RightHandLittleTip";
                default:
                    return "Body_Unknown";
            }
        }
        else if (skeletonType == SkeletonType.FullBody)
        {
            switch (boneId)
            {
                case BoneId.FullBody_Root:
                    return "FullBody_Root";
                case BoneId.FullBody_Hips:
                    return "FullBody_Hips";
                case BoneId.FullBody_SpineLower:
                    return "FullBody_SpineLower";
                case BoneId.FullBody_SpineMiddle:
                    return "FullBody_SpineMiddle";
                case BoneId.FullBody_SpineUpper:
                    return "FullBody_SpineUpper";
                case BoneId.FullBody_Chest:
                    return "FullBody_Chest";
                case BoneId.FullBody_Neck:
                    return "FullBody_Neck";
                case BoneId.FullBody_Head:
                    return "FullBody_Head";
                case BoneId.FullBody_LeftShoulder:
                    return "FullBody_LeftShoulder";
                case BoneId.FullBody_LeftScapula:
                    return "FullBody_LeftScapula";
                case BoneId.FullBody_LeftArmUpper:
                    return "FullBody_LeftArmUpper";
                case BoneId.FullBody_LeftArmLower:
                    return "FullBody_LeftArmLower";
                case BoneId.FullBody_LeftHandWristTwist:
                    return "FullBody_LeftHandWristTwist";
                case BoneId.FullBody_RightShoulder:
                    return "FullBody_RightShoulder";
                case BoneId.FullBody_RightScapula:
                    return "FullBody_RightScapula";
                case BoneId.FullBody_RightArmUpper:
                    return "FullBody_RightArmUpper";
                case BoneId.FullBody_RightArmLower:
                    return "FullBody_RightArmLower";
                case BoneId.FullBody_RightHandWristTwist:
                    return "FullBody_RightHandWristTwist";
                case BoneId.FullBody_LeftHandPalm:
                    return "FullBody_LeftHandPalm";
                case BoneId.FullBody_LeftHandWrist:
                    return "FullBody_LeftHandWrist";
                case BoneId.FullBody_LeftHandThumbMetacarpal:
                    return "FullBody_LeftHandThumbMetacarpal";
                case BoneId.FullBody_LeftHandThumbProximal:
                    return "FullBody_LeftHandThumbProximal";
                case BoneId.FullBody_LeftHandThumbDistal:
                    return "FullBody_LeftHandThumbDistal";
                case BoneId.FullBody_LeftHandThumbTip:
                    return "FullBody_LeftHandThumbTip";
                case BoneId.FullBody_LeftHandIndexMetacarpal:
                    return "FullBody_LeftHandIndexMetacarpal";
                case BoneId.FullBody_LeftHandIndexProximal:
                    return "FullBody_LeftHandIndexProximal";
                case BoneId.FullBody_LeftHandIndexIntermediate:
                    return "FullBody_LeftHandIndexIntermediate";
                case BoneId.FullBody_LeftHandIndexDistal:
                    return "FullBody_LeftHandIndexDistal";
                case BoneId.FullBody_LeftHandIndexTip:
                    return "FullBody_LeftHandIndexTip";
                case BoneId.FullBody_LeftHandMiddleMetacarpal:
                    return "FullBody_LeftHandMiddleMetacarpal";
                case BoneId.FullBody_LeftHandMiddleProximal:
                    return "FullBody_LeftHandMiddleProximal";
                case BoneId.FullBody_LeftHandMiddleIntermediate:
                    return "FullBody_LeftHandMiddleIntermediate";
                case BoneId.FullBody_LeftHandMiddleDistal:
                    return "FullBody_LeftHandMiddleDistal";
                case BoneId.FullBody_LeftHandMiddleTip:
                    return "FullBody_LeftHandMiddleTip";
                case BoneId.FullBody_LeftHandRingMetacarpal:
                    return "FullBody_LeftHandRingMetacarpal";
                case BoneId.FullBody_LeftHandRingProximal:
                    return "FullBody_LeftHandRingProximal";
                case BoneId.FullBody_LeftHandRingIntermediate:
                    return "FullBody_LeftHandRingIntermediate";
                case BoneId.FullBody_LeftHandRingDistal:
                    return "FullBody_LeftHandRingDistal";
                case BoneId.FullBody_LeftHandRingTip:
                    return "FullBody_LeftHandRingTip";
                case BoneId.FullBody_LeftHandLittleMetacarpal:
                    return "FullBody_LeftHandLittleMetacarpal";
                case BoneId.FullBody_LeftHandLittleProximal:
                    return "FullBody_LeftHandLittleProximal";
                case BoneId.FullBody_LeftHandLittleIntermediate:
                    return "FullBody_LeftHandLittleIntermediate";
                case BoneId.FullBody_LeftHandLittleDistal:
                    return "FullBody_LeftHandLittleDistal";
                case BoneId.FullBody_LeftHandLittleTip:
                    return "FullBody_LeftHandLittleTip";
                case BoneId.FullBody_RightHandPalm:
                    return "FullBody_RightHandPalm";
                case BoneId.FullBody_RightHandWrist:
                    return "FullBody_RightHandWrist";
                case BoneId.FullBody_RightHandThumbMetacarpal:
                    return "FullBody_RightHandThumbMetacarpal";
                case BoneId.FullBody_RightHandThumbProximal:
                    return "FullBody_RightHandThumbProximal";
                case BoneId.FullBody_RightHandThumbDistal:
                    return "FullBody_RightHandThumbDistal";
                case BoneId.FullBody_RightHandThumbTip:
                    return "FullBody_RightHandThumbTip";
                case BoneId.FullBody_RightHandIndexMetacarpal:
                    return "FullBody_RightHandIndexMetacarpal";
                case BoneId.FullBody_RightHandIndexProximal:
                    return "FullBody_RightHandIndexProximal";
                case BoneId.FullBody_RightHandIndexIntermediate:
                    return "FullBody_RightHandIndexIntermediate";
                case BoneId.FullBody_RightHandIndexDistal:
                    return "FullBody_RightHandIndexDistal";
                case BoneId.FullBody_RightHandIndexTip:
                    return "FullBody_RightHandIndexTip";
                case BoneId.FullBody_RightHandMiddleMetacarpal:
                    return "FullBody_RightHandMiddleMetacarpal";
                case BoneId.FullBody_RightHandMiddleProximal:
                    return "FullBody_RightHandMiddleProximal";
                case BoneId.FullBody_RightHandMiddleIntermediate:
                    return "FullBody_RightHandMiddleIntermediate";
                case BoneId.FullBody_RightHandMiddleDistal:
                    return "FullBody_RightHandMiddleDistal";
                case BoneId.FullBody_RightHandMiddleTip:
                    return "FullBody_RightHandMiddleTip";
                case BoneId.FullBody_RightHandRingMetacarpal:
                    return "FullBody_RightHandRingMetacarpal";
                case BoneId.FullBody_RightHandRingProximal:
                    return "FullBody_RightHandRingProximal";
                case BoneId.FullBody_RightHandRingIntermediate:
                    return "FullBody_RightHandRingIntermediate";
                case BoneId.FullBody_RightHandRingDistal:
                    return "FullBody_RightHandRingDistal";
                case BoneId.FullBody_RightHandRingTip:
                    return "FullBody_RightHandRingTip";
                case BoneId.FullBody_RightHandLittleMetacarpal:
                    return "FullBody_RightHandLittleMetacarpal";
                case BoneId.FullBody_RightHandLittleProximal:
                    return "FullBody_RightHandLittleProximal";
                case BoneId.FullBody_RightHandLittleIntermediate:
                    return "FullBody_RightHandLittleIntermediate";
                case BoneId.FullBody_RightHandLittleDistal:
                    return "FullBody_RightHandLittleDistal";
                case BoneId.FullBody_RightHandLittleTip:
                    return "FullBody_RightHandLittleTip";
                case BoneId.FullBody_LeftUpperLeg:
                    return "FullBody_LeftUpperLeg";
                case BoneId.FullBody_LeftLowerLeg:
                    return "FullBody_LeftLowerLeg";
                case BoneId.FullBody_LeftFootAnkleTwist:
                    return "FullBody_LeftFootAnkleTwist";
                case BoneId.FullBody_LeftFootAnkle:
                    return "FullBody_LeftFootAnkle";
                case BoneId.FullBody_LeftFootSubtalar:
                    return "FullBody_LeftFootSubtalar";
                case BoneId.FullBody_LeftFootTransverse:
                    return "FullBody_LeftFootTransverse";
                case BoneId.FullBody_LeftFootBall:
                    return "FullBody_LeftFootBall";
                case BoneId.FullBody_RightUpperLeg:
                    return "FullBody_RightUpperLeg";
                case BoneId.FullBody_RightLowerLeg:
                    return "FullBody_RightLowerLeg";
                case BoneId.FullBody_RightFootAnkleTwist:
                    return "FullBody_RightFootAnkleTwist";
                case BoneId.FullBody_RightFootAnkle:
                    return "FullBody_RightFootAnkle";
                case BoneId.FullBody_RightFootSubtalar:
                    return "FullBody_RightFootSubtalar";
                case BoneId.FullBody_RightFootTransverse:
                    return "FullBody_RightFootTransverse";
                case BoneId.FullBody_RightFootBall:
                    return "FullBody_RightFootBall";
                default:
                    return "FullBody_Unknown";
            }
        }
        else if (IsHandSkeleton(skeletonType))
        {
            if (skeletonType == SkeletonType.HandLeft || skeletonType == SkeletonType.HandRight)
            {
                switch (boneId)
                {
                    case BoneId.Hand_WristRoot:
                        return "Hand_WristRoot";
                    case BoneId.Hand_ForearmStub:
                        return "Hand_ForearmStub";
                    case BoneId.Hand_Thumb0:
                        return "Hand_Thumb0";
                    case BoneId.Hand_Thumb1:
                        return "Hand_Thumb1";
                    case BoneId.Hand_Thumb2:
                        return "Hand_Thumb2";
                    case BoneId.Hand_Thumb3:
                        return "Hand_Thumb3";
                    case BoneId.Hand_Index1:
                        return "Hand_Index1";
                    case BoneId.Hand_Index2:
                        return "Hand_Index2";
                    case BoneId.Hand_Index3:
                        return "Hand_Index3";
                    case BoneId.Hand_Middle1:
                        return "Hand_Middle1";
                    case BoneId.Hand_Middle2:
                        return "Hand_Middle2";
                    case BoneId.Hand_Middle3:
                        return "Hand_Middle3";
                    case BoneId.Hand_Ring1:
                        return "Hand_Ring1";
                    case BoneId.Hand_Ring2:
                        return "Hand_Ring2";
                    case BoneId.Hand_Ring3:
                        return "Hand_Ring3";
                    case BoneId.Hand_Pinky0:
                        return "Hand_Pinky0";
                    case BoneId.Hand_Pinky1:
                        return "Hand_Pinky1";
                    case BoneId.Hand_Pinky2:
                        return "Hand_Pinky2";
                    case BoneId.Hand_Pinky3:
                        return "Hand_Pinky3";
                    case BoneId.Hand_ThumbTip:
                        return "Hand_ThumbTip";
                    case BoneId.Hand_IndexTip:
                        return "Hand_IndexTip";
                    case BoneId.Hand_MiddleTip:
                        return "Hand_MiddleTip";
                    case BoneId.Hand_RingTip:
                        return "Hand_RingTip";
                    case BoneId.Hand_PinkyTip:
                        return "Hand_PinkyTip";
                    default:
                        return "Hand_Unknown";
                }
            }
            else
            {
                return "Hand_Unknown";
            }
        }
        else
        {
            return "Skeleton_Unknown";
        }
    }

    internal static bool IsBodySkeleton(SkeletonType type) =>
        type == SkeletonType.Body || type == SkeletonType.FullBody;

    private static bool IsHandSkeleton(SkeletonType type) =>
        type.IsHand();
}

public class OVRBone : System.IDisposable
{
    public OVRSkeleton.BoneId Id { get; set; }
    public short ParentBoneIndex { get; set; }
    public Transform Transform { get; set; }

    public OVRBone()
    {
    }

    public OVRBone(OVRSkeleton.BoneId id, short parentBoneIndex, Transform trans)
    {
        Id = id;
        ParentBoneIndex = parentBoneIndex;
        Transform = trans;
    }

    public void Dispose()
    {
        if (Transform != null)
        {
            GameObject.Destroy(Transform.gameObject);
            Transform = null;
        }
    }
}

public class OVRBoneCapsule
{
    public short BoneIndex { get; set; }
    public Rigidbody CapsuleRigidbody { get; set; }
    public CapsuleCollider CapsuleCollider { get; set; }

    public OVRBoneCapsule()
    {
    }

    public OVRBoneCapsule(short boneIndex, Rigidbody capsuleRigidBody, CapsuleCollider capsuleCollider)
    {
        BoneIndex = boneIndex;
        CapsuleRigidbody = capsuleRigidBody;
        CapsuleCollider = capsuleCollider;
    }

    public void Cleanup()
    {
        if (CapsuleRigidbody != null)
        {
            GameObject.Destroy(CapsuleRigidbody.gameObject);
        }
        CapsuleRigidbody = null;
        CapsuleCollider = null;
    }
}
