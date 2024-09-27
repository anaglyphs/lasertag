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
using System.Collections;
using System.Collections.Generic;
using Meta.XR.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[Feature(Feature.Hands)]
public class OVRHand : MonoBehaviour,
    OVRInputModule.InputSource,
    OVRSkeleton.IOVRSkeletonDataProvider,
    OVRSkeletonRenderer.IOVRSkeletonRendererDataProvider,
    OVRMesh.IOVRMeshDataProvider,
    OVRMeshRenderer.IOVRMeshRendererDataProvider
{

    public enum Hand
    {
        None = OVRPlugin.Hand.None,
        HandLeft = OVRPlugin.Hand.HandLeft,
        HandRight = OVRPlugin.Hand.HandRight,
    }

    public enum HandFinger
    {
        Thumb = OVRPlugin.HandFinger.Thumb,
        Index = OVRPlugin.HandFinger.Index,
        Middle = OVRPlugin.HandFinger.Middle,
        Ring = OVRPlugin.HandFinger.Ring,
        Pinky = OVRPlugin.HandFinger.Pinky,
        Max = OVRPlugin.HandFinger.Max,
    }

    public enum TrackingConfidence
    {
        Low = OVRPlugin.TrackingConfidence.Low,
        High = OVRPlugin.TrackingConfidence.High
    }

    [SerializeField]
    internal Hand HandType = Hand.None;

    // Track which hand skeleton version is loaded, changing which version is loaded requires reloading the mesh from the OVR Plugin.
    private OVRHandSkeletonVersion _handSkeletonVersion;

    [SerializeField]
    private Transform _pointerPoseRoot = null;

    /// <summary>
    /// Determines if the controller should be hidden based on held state.
    /// </summary>
    public OVRInput.InputDeviceShowState m_showState = OVRInput.InputDeviceShowState.ControllerNotInHand;

    /// <summary>
    /// An optional component for provind shell like ray functionality - highlighting where you're selecting in the UI and responding to pinches / button presses.
    /// </summary>
    public OVRRayHelper RayHelper;

    private GameObject _pointerPoseGO;
    private OVRPlugin.HandState _handState = new OVRPlugin.HandState();

    // Track index pinching for UI interactions.
    private bool _wasIndexPinching = false;
    private bool _wasReleased = false;


    public bool IsDataValid { get; private set; }
    public bool IsDataHighConfidence { get; private set; }
    public bool IsTracked { get; private set; }
    public bool IsSystemGestureInProgress { get; private set; }
    public bool IsPointerPoseValid { get; private set; }
    public Transform PointerPose
    {
        get
        {
            if (_pointerPoseGO == null)
            {
                InitializePointerPose();
            }
            return _pointerPoseGO.transform;
        }
    }
    public float HandScale { get; private set; }
    public TrackingConfidence HandConfidence { get; private set; }
    public bool IsDominantHand { get; private set; }

    private void InitializePointerPose()
    {
        _pointerPoseGO = new GameObject($"{HandType} {nameof(PointerPose)}");
        _pointerPoseGO.hideFlags = HideFlags.HideAndDontSave;
        if (_pointerPoseRoot != null)
        {
            PointerPose.SetParent(_pointerPoseRoot, false);
        }
        else
        {
            PointerPose.SetParent(transform, false);
        }
    }

    private void Awake()
    {
        if (_pointerPoseGO == null)
        {
            InitializePointerPose();
        }

        if (RayHelper != null)
        {
            RayHelper.transform.SetParent(PointerPose, false);
        }
        GetHandState(OVRPlugin.Step.Render);
    }

    private void Update()
    {
        GetHandState(OVRPlugin.Step.Render);

        bool newPinching = GetFingerIsPinching(HandFinger.Index);
        _wasReleased = !newPinching && _wasIndexPinching;
        _wasIndexPinching = newPinching;

        if (RayHelper && !IsActive() && RayHelper.isActiveAndEnabled)
        {
            RayHelper.gameObject.SetActive(false);
        }
    }

    private void FixedUpdate()
    {
        if (OVRPlugin.nativeXrApi != OVRPlugin.XrApi.OpenXR)
        {
            GetHandState(OVRPlugin.Step.Physics);
        }

        if (RayHelper != null)
        {
            RayHelper.gameObject.SetActive(IsDataValid);
        }
    }

    private void GetHandState(OVRPlugin.Step step)
    {
        if (OVRPlugin.GetHandState(step, (OVRPlugin.Hand)HandType, ref _handState))
        {
            IsTracked = (_handState.Status & OVRPlugin.HandStatus.HandTracked) != 0;
            IsSystemGestureInProgress = (_handState.Status & OVRPlugin.HandStatus.SystemGestureInProgress) != 0;
            IsPointerPoseValid = (_handState.Status & OVRPlugin.HandStatus.InputStateValid) != 0;
            IsDominantHand = (_handState.Status & OVRPlugin.HandStatus.DominantHand) != 0;
            PointerPose.localPosition = _handState.PointerPose.Position.FromFlippedZVector3f();
            PointerPose.localRotation = _handState.PointerPose.Orientation.FromFlippedZQuatf();
            HandScale = _handState.HandScale;
            HandConfidence = (TrackingConfidence)_handState.HandConfidence;

            IsDataValid = true;
            IsDataHighConfidence = IsTracked && HandConfidence == TrackingConfidence.High;

            // Hands cannot be doing pointer poses or system gestures when they are holding controllers
            //OVRInput.Hand inputHandType = (HandType == Hand.)
            OVRInput.ControllerInHandState controllerInHandState =
                OVRInput.GetControllerIsInHandState((OVRInput.Hand)HandType);
            if (controllerInHandState == OVRInput.ControllerInHandState.ControllerInHand)
            {
                // This hand is holding a controller
                IsSystemGestureInProgress = false;
                IsPointerPoseValid = false;
            }

            switch (m_showState)
            {
                case OVRInput.InputDeviceShowState.Always:
                    // intentionally blank
                    break;
                case OVRInput.InputDeviceShowState.ControllerInHandOrNoHand:
                    if (controllerInHandState == OVRInput.ControllerInHandState.ControllerNotInHand)
                    {
                        IsDataValid = false;
                    }
                    break;
                case OVRInput.InputDeviceShowState.ControllerInHand:
                    if (controllerInHandState != OVRInput.ControllerInHandState.ControllerInHand)
                    {
                        IsDataValid = false;
                    }
                    break;
                case OVRInput.InputDeviceShowState.ControllerNotInHand:
                    if (controllerInHandState != OVRInput.ControllerInHandState.ControllerNotInHand)
                    {
                        IsDataValid = false;
                    }
                    break;
                case OVRInput.InputDeviceShowState.NoHand:
                    if (controllerInHandState != OVRInput.ControllerInHandState.NoHand)
                    {
                        IsDataValid = false;
                    }
                    break;
            }
        }
        else
        {
            IsTracked = false;
            IsSystemGestureInProgress = false;
            IsPointerPoseValid = false;
            PointerPose.localPosition = Vector3.zero;
            PointerPose.localRotation = Quaternion.identity;
            HandScale = 1.0f;
            HandConfidence = TrackingConfidence.Low;

            IsDataValid = false;
            IsDataHighConfidence = false;
        }
    }

    public bool GetFingerIsPinching(HandFinger finger)
    {
        return IsDataValid && (((int)_handState.Pinches & (1 << (int)finger)) != 0);
    }

    public float GetFingerPinchStrength(HandFinger finger)
    {
        if (IsDataValid
            && _handState.PinchStrength != null
            && _handState.PinchStrength.Length == (int)OVRPlugin.HandFinger.Max)
        {
            return _handState.PinchStrength[(int)finger];
        }

        return 0.0f;
    }

    public TrackingConfidence GetFingerConfidence(HandFinger finger)
    {
        if (IsDataValid
            && _handState.FingerConfidences != null
            && _handState.FingerConfidences.Length == (int)OVRPlugin.HandFinger.Max)
        {
            return (TrackingConfidence)_handState.FingerConfidences[(int)finger];
        }

        return TrackingConfidence.Low;
    }

    OVRSkeleton.SkeletonType OVRSkeleton.IOVRSkeletonDataProvider.GetSkeletonType()
    {
        switch (HandType)
        {
            case Hand.HandLeft:
                return OVRSkeleton.SkeletonType.HandLeft;
            case Hand.HandRight:
                return OVRSkeleton.SkeletonType.HandRight;
            case Hand.None:
            default:
                return OVRSkeleton.SkeletonType.None;
        }
    }

    OVRSkeleton.SkeletonPoseData OVRSkeleton.IOVRSkeletonDataProvider.GetSkeletonPoseData()
    {
        var data = new OVRSkeleton.SkeletonPoseData();
        data.IsDataValid = IsDataValid;
        if (IsDataValid)
        {
            data.RootPose = _handState.RootPose;
            data.RootScale = _handState.HandScale;
            data.BoneRotations = _handState.BoneRotations;
            data.IsDataHighConfidence = IsTracked && HandConfidence == TrackingConfidence.High;
        }

        return data;
    }

    OVRSkeletonRenderer.SkeletonRendererData OVRSkeletonRenderer.IOVRSkeletonRendererDataProvider.
        GetSkeletonRendererData()
    {
        var data = new OVRSkeletonRenderer.SkeletonRendererData();

        data.IsDataValid = IsDataValid;
        if (IsDataValid)
        {
            data.RootScale = _handState.HandScale;
            data.IsDataHighConfidence = IsTracked && HandConfidence == TrackingConfidence.High;
            data.ShouldUseSystemGestureMaterial = IsSystemGestureInProgress;
        }

        return data;
    }



    OVRMesh.MeshType OVRMesh.IOVRMeshDataProvider.GetMeshType()
    {
        switch (HandType)
        {
            case Hand.None:
                return OVRMesh.MeshType.None;
            case Hand.HandLeft:
                return OVRMesh.MeshType.HandLeft;
            case Hand.HandRight:
                return OVRMesh.MeshType.HandRight;
            default:
                return OVRMesh.MeshType.None;
        }
    }

    OVRMeshRenderer.MeshRendererData OVRMeshRenderer.IOVRMeshRendererDataProvider.GetMeshRendererData()
    {
        var data = new OVRMeshRenderer.MeshRendererData();

        data.IsDataValid = IsDataValid;
        if (IsDataValid)
        {
            data.IsDataHighConfidence = IsTracked && HandConfidence == TrackingConfidence.High;
            data.ShouldUseSystemGestureMaterial = IsSystemGestureInProgress;
        }

        return data;
    }

    public void OnEnable()
    {
        OVRInputModule.TrackInputSource(this);
        SceneManager.activeSceneChanged += OnSceneChanged;
        if (RayHelper && ShouldShowHandUIRay())
        {
            RayHelper.gameObject.SetActive(true);
        }
    }

    public void OnDisable()
    {
        OVRInputModule.UntrackInputSource(this);
        SceneManager.activeSceneChanged -= OnSceneChanged;
        if (RayHelper)
        {
            RayHelper.gameObject.SetActive(false);
        }
    }

    // handle scene changes if we're marked Don't Destroy On Load.
    private void OnSceneChanged(Scene unloading, Scene loading)
    {
        OVRInputModule.TrackInputSource(this);
    }

    public void OnValidate()
    {
        // Verify that all hand side based components on this object are using the same hand side.
        var skeleton = GetComponent<OVRSkeleton>();
        if (skeleton != null)
        {
            if ((skeleton.GetSkeletonType()).AsHandType() != HandType)
            {
                skeleton.SetSkeletonType(HandType.AsSkeletonType());
            }
        }

        var mesh = GetComponent<OVRMesh>();
        if (mesh != null)
        {
            if (mesh.GetMeshType().AsHandType() != HandType)
            {
                mesh.SetMeshType(HandType.AsMeshType());
            }
        }
    }

    public bool IsPressed()
    {
        return GetFingerIsPinching(HandFinger.Index);
    }

    public bool IsReleased()
    {
        return _wasReleased;
    }

    public Transform GetPointerRayTransform()
    {
        PointerPose.name = name;
        return PointerPose;
    }

    private bool ShouldShowHandUIRay()
    {
        return m_showState != OVRInput.InputDeviceShowState.ControllerInHand || OVRPlugin.AreControllerDrivenHandPosesNatural();
    }

    public bool IsValid()
    {
        return this != null;
    }

    public bool IsActive()
    {
        return ShouldShowHandUIRay() && IsDataValid;
    }

    public OVRPlugin.Hand GetHand()
    {
        return (OVRPlugin.Hand)HandType;
    }

    public void UpdatePointerRay(OVRInputRayData rayData)
    {
        if (RayHelper)
        {
            rayData.ActivationStrength = GetFingerPinchStrength(HandFinger.Index);
            RayHelper.UpdatePointerRay(rayData);
        }
    }
}
