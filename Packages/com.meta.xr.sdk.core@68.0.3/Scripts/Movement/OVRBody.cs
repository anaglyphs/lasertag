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
using Meta.XR.Util;
using UnityEngine;

/// <summary>
/// Manages data related to body tracking.
/// </summary>
/// <remarks>
/// Typically, you would use this in conjunction with an <see cref="OVRSkeleton"/> and/or
/// <see cref="OVRSkeletonRenderer"/>.
/// </remarks>
[HelpURL("https://developer.oculus.com/documentation/unity/move-body-tracking/")]
[Feature(Feature.BodyTracking)]
public class OVRBody : MonoBehaviour,
    OVRSkeleton.IOVRSkeletonDataProvider,
    OVRSkeletonRenderer.IOVRSkeletonRendererDataProvider
{
    private OVRPlugin.BodyState _bodyState;

    private OVRPlugin.Quatf[] _boneRotations;

    private OVRPlugin.Vector3f[] _boneTranslations;

    private bool _dataChangedSinceLastQuery;

    private bool _hasData;

    private const OVRPermissionsRequester.Permission BodyTrackingPermission =
        OVRPermissionsRequester.Permission.BodyTracking;

    private Action<string> _onPermissionGranted;

    [SerializeField]
    [Tooltip("The skeleton data type to be provided. Should be sync with OVRSkeleton. For selecting the tracking mode on the device, check settings in OVRManager.")]
    private OVRPlugin.BodyJointSet _providedSkeletonType = OVRPlugin.BodyJointSet.UpperBody;

    public OVRPlugin.BodyJointSet ProvidedSkeletonType
    {
        get => _providedSkeletonType;
        set => _providedSkeletonType = value;
    }

    private static int _trackingInstanceCount;

    /// <summary>
    /// The raw <see cref="BodyState"/> data used to populate the <see cref="OVRSkeleton"/>.
    /// </summary>
    public OVRPlugin.BodyState? BodyState => _hasData ? _bodyState : default(OVRPlugin.BodyState?);

    private void Awake()
    {
        _onPermissionGranted = OnPermissionGranted;
    }

    private void OnEnable()
    {
        _dataChangedSinceLastQuery = false;
        _hasData = false;
        var manager = FindObjectOfType<OVRManager>();
        if (manager != null && manager.SimultaneousHandsAndControllersEnabled)
        {
            Debug.LogWarning("Currently, Body API and simultaneous hands and controllers cannot be enabled at the same time", this);
            enabled = false;
            return;
        }

        if (_providedSkeletonType == OVRPlugin.BodyJointSet.FullBody &&
            OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingJointSet == OVRPlugin.BodyJointSet.UpperBody)
        {
            Debug.LogWarning(
                $"[{nameof(OVRBody)}] Full body skeleton is used, but Full body tracking is disabled. Check settings in OVRManager.");
        }

        _trackingInstanceCount++;
        if (!StartBodyTracking())
        {
            enabled = false;
            return;
        }

        if (OVRPlugin.nativeXrApi == OVRPlugin.XrApi.OpenXR)
        {
            GetBodyState(OVRPlugin.Step.Render);
        }
        else
        {
            enabled = false;
            Debug.LogWarning($"[{nameof(OVRBody)}] Body tracking is only supported by OpenXR and is unavailable.");
        }
    }

    private void OnPermissionGranted(string permissionId)
    {
        if (permissionId == OVRPermissionsRequester.GetPermissionId(BodyTrackingPermission))
        {
            OVRPermissionsRequester.PermissionGranted -= _onPermissionGranted;
            enabled = true;
        }
    }

    private static bool StartBodyTracking()
    {
        OVRPlugin.BodyJointSet jointSet = OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingJointSet;
        if (!OVRPlugin.StartBodyTracking2(jointSet))
        {
            Debug.LogWarning(
                $"[{nameof(OVRBody)}] Failed to start body tracking with joint set {jointSet}.");
            return false;
        }

        OVRPlugin.BodyTrackingFidelity2 fidelity = OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingFidelity;
        bool fidelityChangeSuccessful = OVRPlugin.RequestBodyTrackingFidelity(fidelity);
        if (!fidelityChangeSuccessful)
        {
            // Fidelity suggestion failed but body tracking might still work.
            Debug.LogWarning($"[{nameof(OVRBody)}] Failed to set Body Tracking fidelity to: {fidelity}");
        }

        return true;
    }

    private void OnDisable()
    {

        if (--_trackingInstanceCount == 0)
        {
            OVRPlugin.StopBodyTracking();
        }
    }

    private void OnDestroy()
    {
        OVRPermissionsRequester.PermissionGranted -= _onPermissionGranted;
    }

    private void Update() => GetBodyState(OVRPlugin.Step.Render);

    public static bool SetRequestedJointSet(OVRPlugin.BodyJointSet jointSet)
    {
        var activeJointSet = OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingJointSet;
        if (jointSet != activeJointSet)
        {
            OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingJointSet = jointSet;
            if (_trackingInstanceCount > 0)
            {
                OVRPlugin.StopBodyTracking();
                return StartBodyTracking();
            }
        }

        return true;
    }


    public static bool SuggestBodyTrackingCalibrationOverride(float height) =>
        OVRPlugin.SuggestBodyTrackingCalibrationOverride(new OVRPlugin.BodyTrackingCalibrationInfo { BodyHeight = height });
    public static bool ResetBodyTrackingCalibration() => OVRPlugin.ResetBodyTrackingCalibration();

    public OVRPlugin.BodyTrackingCalibrationState GetBodyTrackingCalibrationStatus()
    {
        if (!_hasData)
            return OVRPlugin.BodyTrackingCalibrationState.Invalid;

        return _bodyState.CalibrationStatus;
    }

    public OVRPlugin.BodyTrackingFidelity2 GetBodyTrackingFidelityStatus()
    {
        return _bodyState.Fidelity;
    }

    private void GetBodyState(OVRPlugin.Step step)
    {
        if (OVRPlugin.GetBodyState4(step, _providedSkeletonType, ref _bodyState))
        {
            _hasData = true;
            _dataChangedSinceLastQuery = true;
        }
        else
        {
            _hasData = false;
        }

    }

    OVRSkeleton.SkeletonType OVRSkeleton.IOVRSkeletonDataProvider.GetSkeletonType()
    {
        return _providedSkeletonType switch
        {
            OVRPlugin.BodyJointSet.UpperBody => OVRSkeleton.SkeletonType.Body,
            OVRPlugin.BodyJointSet.FullBody => OVRSkeleton.SkeletonType.FullBody,
            _ => OVRSkeleton.SkeletonType.None,
        };
    }

    OVRSkeleton.SkeletonPoseData OVRSkeleton.IOVRSkeletonDataProvider.GetSkeletonPoseData()
    {
        if (!_hasData)
            return default;

        if (_dataChangedSinceLastQuery)
        {
            // Make sure arrays have been allocated
            Array.Resize(ref _boneRotations, _bodyState.JointLocations.Length);
            Array.Resize(ref _boneTranslations, _bodyState.JointLocations.Length);

            // Copy joint poses into bone arrays
            for (var i = 0; i < _bodyState.JointLocations.Length; i++)
            {
                var jointLocation = _bodyState.JointLocations[i];
                if (jointLocation.OrientationValid)
                {
                    _boneRotations[i] = jointLocation.Pose.Orientation;
                }

                if (jointLocation.PositionValid)
                {
                    _boneTranslations[i] = jointLocation.Pose.Position;
                }
            }

            _dataChangedSinceLastQuery = false;
        }

        return new OVRSkeleton.SkeletonPoseData
        {
            IsDataValid = true,
            IsDataHighConfidence = _bodyState.Confidence > .5f,
            RootPose = _bodyState.JointLocations[(int)OVRPlugin.BoneId.Body_Root].Pose,
            RootScale = 1.0f,
            BoneRotations = _boneRotations,
            BoneTranslations = _boneTranslations,
            SkeletonChangedCount = (int)_bodyState.SkeletonChangedCount,
        };
    }

    OVRSkeletonRenderer.SkeletonRendererData
        OVRSkeletonRenderer.IOVRSkeletonRendererDataProvider.GetSkeletonRendererData() => _hasData
        ? new OVRSkeletonRenderer.SkeletonRendererData
        {
            RootScale = 1.0f,
            IsDataValid = true,
            IsDataHighConfidence = true,
            ShouldUseSystemGestureMaterial = false,
        }
        : default;

    /// <summary>
    /// Body Tracking Fidelity defines the quality of the tracking
    /// </summary>
    public static OVRPlugin.BodyTrackingFidelity2 Fidelity
    {
        get => OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingFidelity;
        set
        {
            OVRRuntimeSettings.GetRuntimeSettings().BodyTrackingFidelity = value;
            OVRPlugin.RequestBodyTrackingFidelity(value);
        }
    }
}
