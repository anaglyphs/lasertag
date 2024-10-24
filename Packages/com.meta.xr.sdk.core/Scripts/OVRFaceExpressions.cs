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

/// <summary>
/// This class manages the face expressions data.
/// </summary>
/// <remarks>
/// Refers to the <see cref="OVRFaceExpressions.FaceExpression"/> enum for the list of face expressions.
/// </remarks>
[HelpURL("https://developer.oculus.com/documentation/unity/move-face-tracking/")]
[Feature(Feature.FaceTracking)]
public class OVRFaceExpressions : MonoBehaviour, IReadOnlyCollection<float>, OVRFaceExpressions.WeightProvider
{
    /// <summary>
    /// True if face tracking is enabled, otherwise false.
    /// </summary>
    public bool FaceTrackingEnabled => OVRPlugin.faceTracking2Enabled;

    public interface WeightProvider
    {
        float GetWeight(FaceExpression expression);
    }

    /// <summary>
    /// True if the facial expressions are valid, otherwise false.
    /// </summary>
    /// <remarks>
    /// This value gets updated in every frame. You should check this
    /// value before querying for face expressions.
    /// </remarks>
    public bool ValidExpressions { get; private set; }

    /// <summary>
    /// True if the eye look-related blend shapes are valid, otherwise false.
    /// </summary>
    /// <remarks>
    /// This property affects the behavior of two sets of blend shapes.
    ///
    /// **EyesLook:**
    /// - <see cref="FaceExpression.EyesLookDownL"/>
    /// - <see cref="FaceExpression.EyesLookDownR"/>
    /// - <see cref="FaceExpression.EyesLookLeftL"/>
    /// - <see cref="FaceExpression.EyesLookLeftR"/>
    /// - <see cref="FaceExpression.EyesLookRightL"/>
    /// - <see cref="FaceExpression.EyesLookRightR"/>
    /// - <see cref="FaceExpression.EyesLookUpL"/>
    /// - <see cref="FaceExpression.EyesLookUpR"/>
    ///
    /// **EyesClosed:**
    /// - <see cref="FaceExpression.EyesClosedL"/>
    /// - <see cref="FaceExpression.EyesClosedR"/>
    ///
    /// **When <see cref="EyeFollowingBlendshapesValid"/> is `false`:**
    /// - The `EyesLook` blend shapes are set to zero.
    /// - The `EyesClosed` blend shapes range from 0..1, and represent the true state of the eyelids.
    ///
    /// **When <see cref="EyeFollowingBlendshapesValid"/> is `true`:**
    /// - The `EyesLook` blend shapes are valid.
    /// - The `EyesClosed` blend shapes are modified so that the sum of the `EyesClosedX` and `EyesLookDownX` blend shapes
    ///   range from 0..1. This helps avoid double deformation of the avatar's eye lids when they may be driven by both
    ///   the `EyesClosed` and `EyesLookDown` blend shapes. To recover the true `EyesClosed` values, add the
    ///   minimum of `EyesLookDownL` and `EyesLookDownR` blend shapes back using the following formula:<br />
    ///   `EyesClosedL` += min(`EyesLookDownL`, `EyesLookDownR`)<br />
    ///   `EyesClosedR` += min(`EyesLookDownL`, `EyesLookDownR`)
    /// </remarks>
    public bool EyeFollowingBlendshapesValid { get; private set; }

    private OVRPlugin.FaceState _currentFaceState;


    private const OVRPermissionsRequester.Permission FaceTrackingPermission =
        OVRPermissionsRequester.Permission.FaceTracking;
    private const OVRPermissionsRequester.Permission RecordAudioPermission =
        OVRPermissionsRequester.Permission.RecordAudio;

    private Action<string> _onPermissionGranted;
    private static int _trackingInstanceCount;

    private void Awake()
    {
        _onPermissionGranted = OnPermissionGranted;
    }

    private void OnEnable()
    {
        _trackingInstanceCount++;

        if (!StartFaceTracking())
        {
            enabled = false;
        }
    }

    private void OnPermissionGranted(string permissionId)
    {
        if (permissionId == OVRPermissionsRequester.GetPermissionId(FaceTrackingPermission) ||
            permissionId == OVRPermissionsRequester.GetPermissionId(RecordAudioPermission))
        {
            OVRPermissionsRequester.PermissionGranted -= _onPermissionGranted;
            enabled = true;
        }
    }

    private OVRPlugin.FaceTrackingDataSource[] GetRequestedFaceTrackingDataSources()
    {
        var runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
        if (runtimeSettings.RequestsAudioFaceTracking && runtimeSettings.RequestsVisualFaceTracking)
        {
            return new OVRPlugin.FaceTrackingDataSource[] { OVRPlugin.FaceTrackingDataSource.Visual, OVRPlugin.FaceTrackingDataSource.Audio };
        }
        else if (runtimeSettings.RequestsVisualFaceTracking)
        {
            return new OVRPlugin.FaceTrackingDataSource[] { OVRPlugin.FaceTrackingDataSource.Visual };
        }
        else if (runtimeSettings.RequestsAudioFaceTracking)
        {
            return new OVRPlugin.FaceTrackingDataSource[] { OVRPlugin.FaceTrackingDataSource.Audio };
        }
        else
        {
            return new OVRPlugin.FaceTrackingDataSource[] { };
        }
    }

    private bool StartFaceTracking()
    {
        if (!OVRPermissionsRequester.IsPermissionGranted(FaceTrackingPermission) &&
            !OVRPermissionsRequester.IsPermissionGranted(RecordAudioPermission))
        {
            OVRPermissionsRequester.PermissionGranted -= _onPermissionGranted;
            OVRPermissionsRequester.PermissionGranted += _onPermissionGranted;
            return false;
        }

        if (!OVRPlugin.StartFaceTracking2(GetRequestedFaceTrackingDataSources()))
        {
            Debug.LogWarning($"[{nameof(OVRFaceExpressions)}] Failed to start face tracking.");
            return false;
        }


        return true;
    }

    private void OnDisable()
    {
        if (--_trackingInstanceCount == 0)
        {
            OVRPlugin.StopFaceTracking2();
        }
    }

    private void OnDestroy()
    {
        OVRPermissionsRequester.PermissionGranted -= _onPermissionGranted;
    }

    private void Update()
    {
        ValidExpressions =
            OVRPlugin.GetFaceState2(OVRPlugin.Step.Render, -1, ref _currentFaceState)
            && _currentFaceState.Status.IsValid;

        EyeFollowingBlendshapesValid = ValidExpressions && _currentFaceState.Status.IsEyeFollowingBlendshapesValid;

    }


    /// <summary>
    /// This will return the weight of the given expression.
    /// </summary>
    /// <returns>Returns weight of expression ranged between 0.0 to 100.0.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="OVRFaceExpressions.ValidExpressions"/> is false.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="expression"/> value is not in range.
    /// </exception>
    public float this[FaceExpression expression]
    {
        get
        {
            CheckValidity();

            if (expression < 0 || expression >= FaceExpression.Max)
            {
                throw new ArgumentOutOfRangeException(nameof(expression),
                    expression,
                    $"Value must be between 0 to {(int)FaceExpression.Max}");
            }

            return _currentFaceState.ExpressionWeights[(int)expression];
        }
    }

    public float GetWeight(FaceExpression expression) => this[expression];

    /// <summary>
    /// This method tries to gets the weight of the given expression if it's available.
    /// </summary>
    /// <param name="expression" cref="FaceExpression">The expression to get the weight of.</param>
    /// <param name="weight">The output argument that will contain the expression weight or 0.0 if it's not available.</param>
    /// <returns>Returns true if the expression weight is available, false otherwise</returns>
    public bool TryGetFaceExpressionWeight(FaceExpression expression, out float weight)
    {
        if (!ValidExpressions || expression < 0 || expression >= FaceExpression.Max)
        {
            weight = 0;
            return false;
        }

        weight = _currentFaceState.ExpressionWeights[(int)expression];
        return true;
    }


    /// <summary>
    /// List of face parts used for getting the face tracking confidence weight in <see cref="TryGetWeightConfidence"/>.
    /// </summary>
    public enum FaceRegionConfidence
    {
        /// <summary>
        /// Represents the lower part of the face. It includes the mouth, chin and a portion of the nose and cheek.
        /// </summary>
        Lower = OVRPlugin.FaceRegionConfidence.Lower,

        /// <summary>
        /// Represents the upper part of the face. It includes the eyes, eye brows and a portion of the nose and cheek.
        /// </summary>
        Upper = OVRPlugin.FaceRegionConfidence.Upper,

        /// <summary>
        /// Used to determine the size of the <see cref="FaceRegionConfidence"/> enum.
        /// </summary>
        Max = OVRPlugin.FaceRegionConfidence.Max
    }

    /// <summary>
    /// This method tries to gets the confidence weight of the given face part if it's available.
    /// </summary>
    /// <param name="region" cref="FaceRegionConfidence">The part of the face to get the confidence weight of.</param>
    /// <param name="weightConfidence">The output argument that will contain the weight confidence or 0.0 if it's not available.</param>
    /// <returns>Returns true if the weight confidence is available, false otherwise</returns>
    public bool TryGetWeightConfidence(FaceRegionConfidence region, out float weightConfidence)
    {
        if (!ValidExpressions || region < 0 || region >= FaceRegionConfidence.Max)
        {
            weightConfidence = 0;
            return false;
        }

        weightConfidence = _currentFaceState.ExpressionWeightConfidences[(int)region];
        return true;
    }

    public enum FaceTrackingDataSource
    {
        Visual = OVRPlugin.FaceTrackingDataSource.Visual,
        Audio = OVRPlugin.FaceTrackingDataSource.Audio,
        [InspectorName(null)]
        Count = OVRPlugin.FaceTrackingDataSource.Count,
    }

    public bool TryGetFaceTrackingDataSource(out FaceTrackingDataSource dataSource)
    {
        dataSource = (FaceTrackingDataSource)_currentFaceState.DataSource;
        return ValidExpressions;
    }

    internal void CheckValidity()
    {
        if (!ValidExpressions)
        {
            throw new InvalidOperationException(
                $"Face expressions are not valid at this time. Use {nameof(ValidExpressions)} to check for validity.");
        }
    }


    /// <summary>
    /// Copies expression weights to a pre-allocated array.
    /// </summary>
    /// <param name="array">Pre-allocated destination array for expression weights</param>
    /// <param name="startIndex">Starting index in the destination array</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="array"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when there is not enough capacity to copy weights to <paramref name="array"/> at <paramref name="startIndex"/> index.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="startIndex"/> value is out of <paramref name="array"/> bounds.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="OVRFaceExpressions.ValidExpressions"/> is false.
    /// </exception>
    public void CopyTo(float[] array, int startIndex = 0)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (startIndex < 0 || startIndex >= array.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex),
                startIndex,
                $"Value must be between 0 to {array.Length - 1}");
        }

        if (array.Length - startIndex < (int)FaceExpression.Max)
        {
            throw new ArgumentException(
                $"Capacity is too small - required {(int)FaceExpression.Max}, available {array.Length - startIndex}.",
                nameof(array));
        }

        CheckValidity();
        for (int i = 0; i < (int)FaceExpression.Max; i++)
        {
            array[i + startIndex] = _currentFaceState.ExpressionWeights[i];
        }
    }

    /// <summary>
    /// Allocates a float array and copies expression weights to it.
    /// </summary>
    public float[] ToArray()
    {
        var array = new float[(int)OVRFaceExpressions.FaceExpression.Max];
        this.CopyTo(array);
        return array;
    }

    /// <summary>
    /// List of face expressions.
    /// </summary>
    public enum FaceExpression
    {
        [InspectorName("None")]
        Invalid = OVRPlugin.FaceExpression2.Invalid,
        BrowLowererL = OVRPlugin.FaceExpression2.Brow_Lowerer_L,
        BrowLowererR = OVRPlugin.FaceExpression2.Brow_Lowerer_R,
        CheekPuffL = OVRPlugin.FaceExpression2.Cheek_Puff_L,
        CheekPuffR = OVRPlugin.FaceExpression2.Cheek_Puff_R,
        CheekRaiserL = OVRPlugin.FaceExpression2.Cheek_Raiser_L,
        CheekRaiserR = OVRPlugin.FaceExpression2.Cheek_Raiser_R,
        CheekSuckL = OVRPlugin.FaceExpression2.Cheek_Suck_L,
        CheekSuckR = OVRPlugin.FaceExpression2.Cheek_Suck_R,
        ChinRaiserB = OVRPlugin.FaceExpression2.Chin_Raiser_B,
        ChinRaiserT = OVRPlugin.FaceExpression2.Chin_Raiser_T,
        DimplerL = OVRPlugin.FaceExpression2.Dimpler_L,
        DimplerR = OVRPlugin.FaceExpression2.Dimpler_R,
        EyesClosedL = OVRPlugin.FaceExpression2.Eyes_Closed_L,
        EyesClosedR = OVRPlugin.FaceExpression2.Eyes_Closed_R,
        EyesLookDownL = OVRPlugin.FaceExpression2.Eyes_Look_Down_L,
        EyesLookDownR = OVRPlugin.FaceExpression2.Eyes_Look_Down_R,
        EyesLookLeftL = OVRPlugin.FaceExpression2.Eyes_Look_Left_L,
        EyesLookLeftR = OVRPlugin.FaceExpression2.Eyes_Look_Left_R,
        EyesLookRightL = OVRPlugin.FaceExpression2.Eyes_Look_Right_L,
        EyesLookRightR = OVRPlugin.FaceExpression2.Eyes_Look_Right_R,
        EyesLookUpL = OVRPlugin.FaceExpression2.Eyes_Look_Up_L,
        EyesLookUpR = OVRPlugin.FaceExpression2.Eyes_Look_Up_R,
        InnerBrowRaiserL = OVRPlugin.FaceExpression2.Inner_Brow_Raiser_L,
        InnerBrowRaiserR = OVRPlugin.FaceExpression2.Inner_Brow_Raiser_R,
        JawDrop = OVRPlugin.FaceExpression2.Jaw_Drop,
        JawSidewaysLeft = OVRPlugin.FaceExpression2.Jaw_Sideways_Left,
        JawSidewaysRight = OVRPlugin.FaceExpression2.Jaw_Sideways_Right,
        JawThrust = OVRPlugin.FaceExpression2.Jaw_Thrust,
        LidTightenerL = OVRPlugin.FaceExpression2.Lid_Tightener_L,
        LidTightenerR = OVRPlugin.FaceExpression2.Lid_Tightener_R,
        LipCornerDepressorL = OVRPlugin.FaceExpression2.Lip_Corner_Depressor_L,
        LipCornerDepressorR = OVRPlugin.FaceExpression2.Lip_Corner_Depressor_R,
        LipCornerPullerL = OVRPlugin.FaceExpression2.Lip_Corner_Puller_L,
        LipCornerPullerR = OVRPlugin.FaceExpression2.Lip_Corner_Puller_R,
        LipFunnelerLB = OVRPlugin.FaceExpression2.Lip_Funneler_LB,
        LipFunnelerLT = OVRPlugin.FaceExpression2.Lip_Funneler_LT,
        LipFunnelerRB = OVRPlugin.FaceExpression2.Lip_Funneler_RB,
        LipFunnelerRT = OVRPlugin.FaceExpression2.Lip_Funneler_RT,
        LipPressorL = OVRPlugin.FaceExpression2.Lip_Pressor_L,
        LipPressorR = OVRPlugin.FaceExpression2.Lip_Pressor_R,
        LipPuckerL = OVRPlugin.FaceExpression2.Lip_Pucker_L,
        LipPuckerR = OVRPlugin.FaceExpression2.Lip_Pucker_R,
        LipStretcherL = OVRPlugin.FaceExpression2.Lip_Stretcher_L,
        LipStretcherR = OVRPlugin.FaceExpression2.Lip_Stretcher_R,
        LipSuckLB = OVRPlugin.FaceExpression2.Lip_Suck_LB,
        LipSuckLT = OVRPlugin.FaceExpression2.Lip_Suck_LT,
        LipSuckRB = OVRPlugin.FaceExpression2.Lip_Suck_RB,
        LipSuckRT = OVRPlugin.FaceExpression2.Lip_Suck_RT,
        LipTightenerL = OVRPlugin.FaceExpression2.Lip_Tightener_L,
        LipTightenerR = OVRPlugin.FaceExpression2.Lip_Tightener_R,
        LipsToward = OVRPlugin.FaceExpression2.Lips_Toward,
        LowerLipDepressorL = OVRPlugin.FaceExpression2.Lower_Lip_Depressor_L,
        LowerLipDepressorR = OVRPlugin.FaceExpression2.Lower_Lip_Depressor_R,
        MouthLeft = OVRPlugin.FaceExpression2.Mouth_Left,
        MouthRight = OVRPlugin.FaceExpression2.Mouth_Right,
        NoseWrinklerL = OVRPlugin.FaceExpression2.Nose_Wrinkler_L,
        NoseWrinklerR = OVRPlugin.FaceExpression2.Nose_Wrinkler_R,
        OuterBrowRaiserL = OVRPlugin.FaceExpression2.Outer_Brow_Raiser_L,
        OuterBrowRaiserR = OVRPlugin.FaceExpression2.Outer_Brow_Raiser_R,
        UpperLidRaiserL = OVRPlugin.FaceExpression2.Upper_Lid_Raiser_L,
        UpperLidRaiserR = OVRPlugin.FaceExpression2.Upper_Lid_Raiser_R,
        UpperLipRaiserL = OVRPlugin.FaceExpression2.Upper_Lip_Raiser_L,
        UpperLipRaiserR = OVRPlugin.FaceExpression2.Upper_Lip_Raiser_R,
        TongueTipInterdental = OVRPlugin.FaceExpression2.Tongue_Tip_Interdental,
        TongueTipAlveolar = OVRPlugin.FaceExpression2.Tongue_Tip_Alveolar,
        TongueFrontDorsalPalate = OVRPlugin.FaceExpression2.Tongue_Front_Dorsal_Palate,
        TongueMidDorsalPalate = OVRPlugin.FaceExpression2.Tongue_Mid_Dorsal_Palate,
        TongueBackDorsalVelar = OVRPlugin.FaceExpression2.Tongue_Back_Dorsal_Velar,
        TongueOut = OVRPlugin.FaceExpression2.Tongue_Out,
        TongueRetreat = OVRPlugin.FaceExpression2.Tongue_Retreat,
        [InspectorName(null)]
        Max = OVRPlugin.FaceExpression2.Max,
    }

    #region Face expressions enumerator

    public FaceExpressionsEnumerator GetEnumerator() =>
        new FaceExpressionsEnumerator(_currentFaceState.ExpressionWeights);

    IEnumerator<float> IEnumerable<float>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => _currentFaceState.ExpressionWeights?.Length ?? 0;

    public struct FaceExpressionsEnumerator : IEnumerator<float>
    {
        private float[] _faceExpressions;

        private int _index;

        private int _count;

        internal FaceExpressionsEnumerator(float[] array)
        {
            _faceExpressions = array;
            _index = -1;
            _count = _faceExpressions?.Length ?? 0;
        }

        public bool MoveNext() => ++_index < _count;

        public float Current => _faceExpressions[_index];

        object IEnumerator.Current => Current;

        public void Reset() => _index = -1;

        public void Dispose()
        {
        }
    }

    #endregion

}
