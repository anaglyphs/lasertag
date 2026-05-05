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

#if USING_XR_SDK_OPENXR

using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR.OpenXR.Input;
using static OVRPlugin;

namespace Meta.XR
{
    partial class MetaXRFeature
    {
        internal unsafe OVRPlugin.Bool ovrp_SetControllerVibration(uint controllerMask, float frequency, float amplitude)
        {
            if (Session == 0)
                return OVRPlugin.Bool.False;

            if (Command.xrApplyHapticFeedback == null)
            {
                LogError("xrApplyHapticFeedback command was not loaded.");
                return OVRPlugin.Bool.False;
            }

            OVRPlugin.Bool finalResult = OVRPlugin.Bool.True;
            if ((controllerMask & (uint)OVRPlugin.Controller.LTouch) != 0)
            {
                var path = StringToPath("/user/hand/left");
                var hapticActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticVibration
                {
                    Type = XrHapticVibration.StructureType,
                    Duration = XrDuration.FromSeconds(2.0f),
                    Frequency = 0,
                    Amplitude = amplitude,
                };

                hapticsInfo.Action = (XrAction)hapticActionHandle;
                var result = Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration);

                finalResult &= result.ToOVRPluginType() == OVRPlugin.Result.Success ? OVRPlugin.Bool.True : OVRPlugin.Bool.False;
            }

            if ((controllerMask & (uint)OVRPlugin.Controller.RTouch) != 0)
            {
                var path = StringToPath("/user/hand/right");
                var hapticActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticVibration
                {
                    Type = XrHapticVibration.StructureType,
                    Duration = XrDuration.FromSeconds(2.0f),
                    Frequency = 0,
                    Amplitude = amplitude,
                };

                hapticsInfo.Action = (XrAction)hapticActionHandle;
                var result = Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration);

                finalResult &= result.ToOVRPluginType() == OVRPlugin.Result.Success ? OVRPlugin.Bool.True : OVRPlugin.Bool.False;
            }

            return finalResult;
        }

        internal unsafe OVRPlugin.Result ovrp_SetControllerLocalizedVibration(OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsLocation hapticsLocationMask, float frequency, float amplitude)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrApplyHapticFeedback == null)
            {
                LogError("xrApplyHapticFeedback command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            var result = OVRPlugin.Result.Success;
            if ((controllerMask & OVRPlugin.Controller.LTouch) != 0)
            {
                var hapticActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));
                var hapticThumbActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.HapticThumb));
                var hapticTriggerActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.HapticTrigger));

                var path = StringToPath("/user/hand/left");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticVibration
                {
                    Type = XrHapticVibration.StructureType,
                    Duration = XrDuration.FromSeconds(2.0f),
                    Frequency = 0,
                    Amplitude = amplitude,
                };

                if ((hapticsLocationMask & OVRPlugin.HapticsLocation.Hand) != 0)
                {
                    hapticsInfo.Action = (XrAction)hapticActionHandle;
                    if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                        result = OVRPlugin.Result.Failure;
                }

                if ((hapticsLocationMask & OVRPlugin.HapticsLocation.Thumb) != 0)
                {
                    hapticsInfo.Action = (XrAction)hapticThumbActionHandle;
                    if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                        result = OVRPlugin.Result.Failure;
                }

                if ((hapticsLocationMask & OVRPlugin.HapticsLocation.Index) != 0)
                {
                    hapticsInfo.Action = (XrAction)hapticTriggerActionHandle;
                    if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                        result = OVRPlugin.Result.Failure;
                }
            }

            if ((controllerMask & OVRPlugin.Controller.RTouch) != 0)
            {
                var hapticActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));
                var hapticThumbActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.HapticThumb));
                var hapticTriggerActionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.HapticTrigger));

                var path = StringToPath("/user/hand/right");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticVibration
                {
                    Type = XrHapticVibration.StructureType,
                    Duration = XrDuration.FromSeconds(2.0f),
                    Frequency = frequency,
                    Amplitude = amplitude,
                };

                if ((hapticsLocationMask & OVRPlugin.HapticsLocation.Hand) != 0)
                {
                    hapticsInfo.Action = (XrAction)hapticActionHandle;
                    if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                        result = OVRPlugin.Result.Failure;
                }

                if ((hapticsLocationMask & OVRPlugin.HapticsLocation.Thumb) != 0)
                {
                    hapticsInfo.Action = (XrAction)hapticThumbActionHandle;
                    if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                        result = OVRPlugin.Result.Failure;
                }

                if ((hapticsLocationMask & OVRPlugin.HapticsLocation.Index) != 0)
                {
                    hapticsInfo.Action = (XrAction)hapticTriggerActionHandle;
                    if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                        result = OVRPlugin.Result.Failure;
                }
            }
            return result;
        }

        // HapticsAmplitudeEnvlope works for main haptic path only
        internal unsafe OVRPlugin.Result ovrp_SetControllerHapticsAmplitudeEnvelope(
            OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsAmplitudeEnvelopeVibration hapticsVibration)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (!_hapticsAmplitudeEnvelopeEnabled)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (Command.xrApplyHapticFeedback == null)
            {
                LogError("xrApplyHapticFeedback command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            var result = OVRPlugin.Result.Success;
            if ((controllerMask & OVRPlugin.Controller.LTouch) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/left");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticAmplitudeEnvelopeVibrationFB
                {
                    Type = XrHapticAmplitudeEnvelopeVibrationFB.StructureType,
                    Duration = XrDuration.FromSeconds(hapticsVibration.Duration),
                    AmplitudeCount = hapticsVibration.AmplitudeCount,
                    Amplitudes = (float*)hapticsVibration.Amplitudes,
                };

                if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                    result = OVRPlugin.Result.Failure;
            }

            if ((controllerMask & OVRPlugin.Controller.RTouch) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/right");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticAmplitudeEnvelopeVibrationFB
                {
                    Type = XrHapticAmplitudeEnvelopeVibrationFB.StructureType,
                    Duration = XrDuration.FromSeconds(hapticsVibration.Duration),
                    AmplitudeCount = hapticsVibration.AmplitudeCount,
                    Amplitudes = (float*)hapticsVibration.Amplitudes,
                };

                if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                    result = OVRPlugin.Result.Failure;
            }

            return result;
        }


        internal unsafe OVRPlugin.Result ovrp_SetControllerHapticsPcm(
            OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsPcmVibration hapticsVibration)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrApplyHapticFeedback == null)
            {
                LogError("xrApplyHapticFeedback command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            var result = OVRPlugin.Result.Success;
            if ((controllerMask & OVRPlugin.Controller.LTouch) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/left");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticPcmVibrationFB
                {
                    Type = XrHapticPcmVibrationFB.StructureType,
                    BufferSize = hapticsVibration.BufferSize,
                    Buffer = (float*)hapticsVibration.Buffer,
                    SampleRate = hapticsVibration.SampleRateHz,
                    SamplesConsumed = (uint*)hapticsVibration.SamplesConsumed,
                    Append = (hapticsVibration.Append == OVRPlugin.Bool.True)
                };

                if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                    result = OVRPlugin.Result.Failure;
            }

            if ((controllerMask & OVRPlugin.Controller.RTouch) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/right");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticPcmVibrationFB
                {
                    Type = XrHapticPcmVibrationFB.StructureType,
                    BufferSize = hapticsVibration.BufferSize,
                    Buffer = (float*)hapticsVibration.Buffer,
                    SampleRate = hapticsVibration.SampleRateHz,
                    SamplesConsumed = (uint*)hapticsVibration.SamplesConsumed,
                    Append = (hapticsVibration.Append == OVRPlugin.Bool.True)
                };

                if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                    result = OVRPlugin.Result.Failure;
            }

            return result;
        }

        internal unsafe OVRPlugin.Result ovrp_SetControllerHapticsParametric(
            OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsParametricVibration hapticsVibration)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (!_parametricHapticsEnabled)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (!_parametricHapticsSupported)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (Command.xrApplyHapticFeedback == null)
            {
                LogError("xrApplyHapticFeedback command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            var result = OVRPlugin.Result.Success;
            if ((controllerMask & OVRPlugin.Controller.LTouch) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/left");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticParametricVibrationEXTX1
                {
                    Type = XrHapticParametricVibrationEXTX1.StructureType,
                    AmplitudePointCount = (uint)hapticsVibration.AmplitudePointCount,
                    AmplitudePoints = (XrHapticParametricPointEXTX1*)hapticsVibration.AmplitudePoints,
                    FrequencyPointCount = (uint)hapticsVibration.FrequencyPointCount,
                    FrequencyPoints = (XrHapticParametricPointEXTX1*)hapticsVibration.FrequencyPoints,
                    TransientCount = (uint)hapticsVibration.TransientCount,
                    Transients = (XrHapticParametricTransientEXTX1*)hapticsVibration.Transients,
                    MinFrequencyHz = hapticsVibration.MinFrequencyHz,
                    MaxFrequencyHz = hapticsVibration.MaxFrequencyHz,
                    StreamFrameType = (XrHapticParametricStreamFrameTypeEXTX1)hapticsVibration.StreamFrameType,
                };


                if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                    result = OVRPlugin.Result.Failure;
            }

            if ((controllerMask & OVRPlugin.Controller.RTouch) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/right");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                var hapticVibration = new XrHapticParametricVibrationEXTX1
                {
                    Type = XrHapticParametricVibrationEXTX1.StructureType,
                    AmplitudePointCount = (uint)hapticsVibration.AmplitudePointCount,
                    AmplitudePoints = (XrHapticParametricPointEXTX1*)hapticsVibration.AmplitudePoints,
                    FrequencyPointCount = (uint)hapticsVibration.FrequencyPointCount,
                    FrequencyPoints = (XrHapticParametricPointEXTX1*)hapticsVibration.FrequencyPoints,
                    TransientCount = (uint)hapticsVibration.TransientCount,
                    Transients = (XrHapticParametricTransientEXTX1*)hapticsVibration.Transients,
                    MinFrequencyHz = hapticsVibration.MinFrequencyHz,
                    MaxFrequencyHz = hapticsVibration.MaxFrequencyHz,
                    StreamFrameType = (XrHapticParametricStreamFrameTypeEXTX1)hapticsVibration.StreamFrameType,
                };

                if (Command.xrApplyHapticFeedback(Session, in hapticsInfo, (XrHapticBaseHeader*)&hapticVibration) != XrResult.Success)
                    result = OVRPlugin.Result.Failure;
            }

            return result;
        }

        internal unsafe OVRPlugin.Result ovrp_GetControllerSampleRateHz(OVRPlugin.Controller controller, out float sampleRateHz)
        {
            sampleRateHz = 0.0f;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (!_hapticPcmEnabled)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (Command.xrGetDeviceSampleRateFB == null)
            {
                LogError("xrGetDeviceSampleRateFB command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            XrDevicePcmSampleRateStateFB deviceSampleRate = new XrDevicePcmSampleRateStateFB
            {
                Type = XrDevicePcmSampleRateStateFB.StructureType
            };
            if (controller == OVRPlugin.Controller.LTouch || controller == OVRPlugin.Controller.LHand)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/left");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                if (Command.xrGetDeviceSampleRateFB(Session, in hapticsInfo, ref deviceSampleRate) != XrResult.Success)
                    return OVRPlugin.Result.Failure;
            }
            else if (controller == OVRPlugin.Controller.RTouch || controller == OVRPlugin.Controller.RHand)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/right");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                if (Command.xrGetDeviceSampleRateFB(Session, in hapticsInfo, ref deviceSampleRate) != XrResult.Success)
                    return OVRPlugin.Result.Failure;
            }

            sampleRateHz = deviceSampleRate.SampleRate;
            return OVRPlugin.Result.Success;
        }

        internal unsafe OVRPlugin.Result ovrp_GetControllerParametricProperties(OVRPlugin.Controller controllerMask,
            out OVRPlugin.HapticsParametricProperties hapticsProperties)
        {
            hapticsProperties = new OVRPlugin.HapticsParametricProperties();

            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (!_parametricHapticsEnabled)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (!_parametricHapticsSupported)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (Command.xrHapticParametricGetPropertiesEXTX1 == null)
            {
                LogError("xrHapticParametricGetPropertiesEXTX1 command was not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            XrHapticParametricPropertiesEXTX1 properties = new XrHapticParametricPropertiesEXTX1
            {
                Type = XrHapticParametricPropertiesEXTX1.StructureType
            };
            if ((controllerMask & OVRPlugin.Controller.LTouch) != 0 || (controllerMask & OVRPlugin.Controller.LHand) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.LeftHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/left");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                if (Command.xrHapticParametricGetPropertiesEXTX1(Session, in hapticsInfo, ref properties) != XrResult.Success)
                    return OVRPlugin.Result.Failure;
            }
            else if ((controllerMask & OVRPlugin.Controller.RTouch) != 0 || (controllerMask & OVRPlugin.Controller.RHand) != 0)
            {
                var actionHandle = OpenXRInput.GetActionHandle(MetaQuestActionMap.GetAction(MetaQuestActionMap.QuestTouchDevice + MetaQuestActionMap.RightHand + MetaQuestActionMap.Haptic));
                var path = StringToPath("/user/hand/right");

                var hapticsInfo = new XrHapticActionInfo
                {
                    Type = XrHapticActionInfo.StructureType,
                    Action = (XrAction)actionHandle,
                    SubactionPath = (XrPath)path
                };

                if (Command.xrHapticParametricGetPropertiesEXTX1(Session, in hapticsInfo, ref properties) != XrResult.Success)
                    return OVRPlugin.Result.Failure;
            }

            hapticsProperties.IdealFrameSubmissionRate = properties.IdealFrameSubmissionRate.Nanoseconds;
            hapticsProperties.MinimumFirstFrameDuration = properties.MinimumFirstFrameDuration.Nanoseconds;
            hapticsProperties.MinFrequencyHz = properties.MinFrequencyHz;
            hapticsProperties.MaxFrequencyHz = properties.MaxFrequencyHz;

            return OVRPlugin.Result.Success;
        }
    }
}
#endif // USING_XR_SDK_OPENXR
