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
using UnityEngine;
using UnityEngine.UI;
using Meta.XR.Samples;
using static OVRInput;
using static SystemHaptics;

[MetaCodeSample("CoreSDK-Haptics")]
public class HapticsSample : MonoBehaviour
{
    [SerializeField] private Text leftControllerInfo, rightControllerInfo;
    private float[] m_hapticsBuffer = new float[(int)OVRPlugin.HapticsConstants.MaxSamples];

    [SerializeField] private Text currentSystemHapticsPatternText;
    private SystemHapticsPattern[] patterns;
    private int systemHapticsCurrentPatternIndex = 0;

    public void TriggerSimpleHaptics()
    {
        SetControllerVibration(1, 1, Controller.RTouch);
        SetControllerVibration(1, 1, Controller.LTouch);
    }

    public void TriggerAmplitudeEnvelopeHaptics()
    {
        HapticsAmplitudeEnvelopeVibration hapticsVibration;
        hapticsVibration.SamplesCount = m_hapticsBuffer.Length;
        hapticsVibration.Samples = m_hapticsBuffer;
        hapticsVibration.Duration = 2.0f;

        for (int i = 0; i < hapticsVibration.SamplesCount; i++)
        {
            if (i <= hapticsVibration.SamplesCount / 2) //Triangle shape
            {
                hapticsVibration.Samples[i] = i / (hapticsVibration.SamplesCount / 2.0f);
            }
            else
            {
                hapticsVibration.Samples[i] = -(2.0f * i) / hapticsVibration.SamplesCount + 2;
            }
        }

        SetControllerHapticsAmplitudeEnvelope(hapticsVibration, Controller.RTouch);
        SetControllerHapticsAmplitudeEnvelope(hapticsVibration, Controller.LTouch);
    }

    public void TriggerPCMHaptics()
    {
        HapticsPcmVibration hapticsVibration;
        hapticsVibration.SamplesCount = m_hapticsBuffer.Length;
        hapticsVibration.Samples = m_hapticsBuffer;
        hapticsVibration.SampleRateHz = GetControllerSampleRateHz(Controller.RTouch);
        hapticsVibration.Append = false;

        for (int i = 0; i < hapticsVibration.SamplesCount; i++)
        {
            hapticsVibration.Samples[i] = Mathf.Sin(157.0f * (i / 1000.0f) * Mathf.PI * 2);
        }

        SetControllerHapticsPcm(hapticsVibration, Controller.RTouch);
        SetControllerHapticsPcm(hapticsVibration, Controller.LTouch);
    }

    public void TriggerParametricHaptics()
    {
        HapticsParametricVibration hapticsVibration = new HapticsParametricVibration();

        HapticsParametricPoint[] amplitudePoints = new HapticsParametricPoint[]
        {
            new HapticsParametricPoint { Time = 0, Value = 0.0f },
            new HapticsParametricPoint { Time = 4000000000, Value = 1.0f },
            new HapticsParametricPoint { Time = 10000000000, Value = 1.0f },
        };
        OVRInput.HapticsParametricPoint[] frequencyPoints = new OVRInput.HapticsParametricPoint[]
        {
            new HapticsParametricPoint { Time = 0, Value = 1.0f },
            new HapticsParametricPoint { Time = 6000000000, Value = 1.0f },
            new HapticsParametricPoint { Time = 10000000000, Value = 0.0f },
        };
        HapticsParametricTransient[] transients = new HapticsParametricTransient[]
        {
            new HapticsParametricTransient { Time = 3000000000, Amplitude = 1.0f, Frequency = 1.0f },
        };

        hapticsVibration.AmplitudePoints = amplitudePoints;
        hapticsVibration.FrequencyPoints = frequencyPoints;
        hapticsVibration.Transients = transients;
        hapticsVibration.MinFrequencyHz = (int)OVRPlugin.HapticsConstants.ParametricHapticsUnspecifiedFrequency;
        hapticsVibration.MaxFrequencyHz = (int)OVRPlugin.HapticsConstants.ParametricHapticsUnspecifiedFrequency;

        SetControllerHapticsParametric(hapticsVibration, Controller.RTouch);
        SetControllerHapticsParametric(hapticsVibration, Controller.LTouch);
    }

    public void GetControllerParametricProperties()
    {
        HapticsParametricProperties rightControllerParametricProperties = OVRInput.GetControllerParametricProperties(Controller.RTouch);
        rightControllerInfo.text = "\n IdealFrameSubmissionRate " + rightControllerParametricProperties.IdealFrameSubmissionRate +
                                  "\n MinimumFirstFrameDuration " + rightControllerParametricProperties.MinimumFirstFrameDuration +
                                  "\n MinFrequencyHz " + rightControllerParametricProperties.MinFrequencyHz +
                                  "\n MaxFrequencyHz " + rightControllerParametricProperties.MaxFrequencyHz;

        HapticsParametricProperties leftControllerParametricProperties = OVRInput.GetControllerParametricProperties(Controller.LTouch);
        leftControllerInfo.text = "\n IdealFrameSubmissionRate " + leftControllerParametricProperties.IdealFrameSubmissionRate +
                                  "\n MinimumFirstFrameDuration " + leftControllerParametricProperties.MinimumFirstFrameDuration +
                                  "\n MinFrequencyHz " + leftControllerParametricProperties.MinFrequencyHz +
                                  "\n MaxFrequencyHz " + leftControllerParametricProperties.MaxFrequencyHz;
    }

    // System Haptics Panel: Allows selection of pattern to play by cycling through the list of patterns
    void Start()
    {
        // Cache all enum values for easy cycling
        patterns = (SystemHapticsPattern[])Enum.GetValues(typeof(SystemHapticsPattern));
        SystemHapticsUpdatePlayButtonText();
    }

    private void SystemHapticsUpdatePlayButtonText()
    {
        // Displays the current pattern name on the Play button
        currentSystemHapticsPatternText.text = $"System Haptics: \n Pattern: {patterns[systemHapticsCurrentPatternIndex]}";
    }

    public void SystemHapticsPreviousPattern()
    {
        systemHapticsCurrentPatternIndex = (systemHapticsCurrentPatternIndex - 1 + patterns.Length) % patterns.Length;
        SystemHapticsUpdatePlayButtonText();
    }

    public void SystemHapticsNextPattern()
    {
        systemHapticsCurrentPatternIndex = (systemHapticsCurrentPatternIndex + 1) % patterns.Length;
        SystemHapticsUpdatePlayButtonText();
    }

    public void SystemHapticsPlaySelectedPattern()
    {
        var selectedPattern = patterns[systemHapticsCurrentPatternIndex];
        SystemHapticsPlayPattern(selectedPattern, Controller.RTouch);
        SystemHapticsPlayPattern(selectedPattern, Controller.LTouch);
    }
}
