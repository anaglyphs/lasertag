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
using System;
using System.Collections.Generic;
using static OVRInput;

/// <summary>
/// Provides a static API for managing and playing system haptic feedback patterns on supported controllers.
/// </summary>
/// <remarks>
/// This class is intended for use in applications that require consistent, system-level haptic feedback, such as
/// UI interactions, Notifications or other standardized events.
///
/// The <c>SystemHaptics</c> class loads, caches, and plays predefined haptic patterns (such as "Test", "Hover", "Press")
/// from resources at runtime. Patterns are defined as <c>.systemhaptic</c> files, which are parsed into
/// <see cref="HapticsParametricVibration"/> structs for playback via the ParametricHaptics API.
///
/// On initialization (before scene load), the class attempts to load all available haptic patterns from the
/// <c>Resources/SystemHapticsPatternClips/</c> directory, mapping each <see cref="SystemHapticsPattern"/> enum value
/// to its corresponding vibration data. Patterns are cached for efficient lookup and playback.
///
/// To play a haptic pattern, call <see cref="SystemHapticsPlayPattern"/> with the desired pattern and target controller.
/// If the pattern is not found in the cache, a warning is logged.
///
/// To customize and play back haptic events, please refer to <a href="https://developer.oculus.com/resources/haptics-overview/">
/// Meta Haptics Studio</a> and the <a href="https://developers.meta.com/horizon/documentation/unity/unity-haptics-sdk/">
/// Meta XR Haptics SDK</a> for Unity.
/// </remarks>
public static class SystemHaptics
{
    // Notes:
    // - Patterns must be present as <c>.systemhaptic</c> files in the expected resources directory and named after their enum variant.
    // - The class is static and should not be instantiated.
    // - Initialization occurs automatically before the first scene load.
    // Set up dictionary to hold the system haptics variants and corresponding vibration patterns
    private static readonly Dictionary<SystemHapticsPattern, HapticsParametricVibration> Cache = new();
    private static bool _initialized;

    // Reset static fields at Domain Reload
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Cache.Clear();
        _initialized = false;
    }

    /// <summary>
    /// Initializes the system haptics cache (i.e. dictionary).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_initialized) return;

        int clipCount = 0;
        foreach (SystemHapticsPattern pattern in Enum.GetValues(typeof(SystemHapticsPattern)))
        {
            // Assumes .systemhaptic files are in Resources/SystemHapticsPatternClips/ and named after the corresponding
            // enum variant.
            string path = $"SystemHapticsPatternClips/{pattern}";
            SystemHapticsClipData systemHapticsClipData = Resources.Load<SystemHapticsClipData>(path);

            if (systemHapticsClipData != null)
            {
                clipCount += 1;
                if (TryParseSystemHapticsClipJsonToHapticsParametricVibration(systemHapticsClipData,
                        out HapticsParametricVibration vibration))
                {
                    Cache.Add(pattern, vibration);
                }
                else
                {
                    Debug.LogWarning($"[SystemHaptics] Failed to parse clip {pattern} to HapticsParametricVibration.");
                }
            }
            else
            {
                Debug.LogWarning($"[SystemHaptics] Haptic clip not found for pattern: {pattern} at {path}");
            }
        }

        // Check if cache has been populated and thus successfully initialized
        if (Cache.Count == clipCount)
        {
            _initialized = true;
        }
        else
        {
            Debug.LogError($"[SystemHaptics] Initializing cache failed.");
        }
    }

    /// <summary>
    /// Plays a system haptic pattern from the cached dictionary at the desired playback location.
    /// </summary>
    /// <param name="pattern">The desired <see cref="SystemHapticsPattern"/> haptic effect.</param>
    /// <param name="controller">The target controller to play back the haptic effect on.</param>
    public static void SystemHapticsPlayPattern(SystemHapticsPattern pattern, Controller controller)
    {
        if (Cache.TryGetValue(pattern, out HapticsParametricVibration vibration))
        {
            SetControllerHapticsParametric(vibration, controller);

            var eventData = new OVRPlugin.UnifiedEventData("systemhaptics_play_pattern")
            {
                isEssential = OVRPlugin.Bool.True,
                productType = "core_sdk",
                project_name = "systemhaptics"
            };

            OVRPlugin.SendUnifiedEvent(eventData);
        }
        else
        {
            throw new ArgumentException($"[SystemHaptics] Pattern {pattern} not found in cache.", nameof(pattern));
        }
    }

    /// <summary>
    /// Attempts to parse a <c>.systemhaptic</c> clip object and output a <see cref="HapticsParametricVibration"/> struct
    /// required to play back through the ParametricHaptics API.
    /// </summary>
    /// <param name="systemHapticsClipJson">
    /// An object representing a JSON-encoded <c>.systemhaptic</c> clip, typically returned by the
    /// <c>SystemHapticsClipImporter</c> upon importing a <c>.systemhaptic</c> clip.
    /// </param>
    /// <param name="vibration">
    /// When this method returns, contains the parsed <see cref="HapticsParametricVibration"/> struct if parsing succeeded,
    /// or the default value if parsing failed.
    /// </param>
    /// <returns>
    /// <c>true</c> if parsing was successful and the resulting struct is valid; otherwise, <c>false</c>.
    /// </returns>
    private static bool TryParseSystemHapticsClipJsonToHapticsParametricVibration(
        SystemHapticsClipData systemHapticsClipJson,
    out HapticsParametricVibration vibration)
    {
        vibration = new HapticsParametricVibration();

        var amplitudeEnvelope = systemHapticsClipJson.signals.continuous.envelopes.amplitude;
        var frequencyEnvelope = systemHapticsClipJson.signals.continuous.envelopes.frequency;

        // A valid clip must at least contain an amplitude envelope.
        if (amplitudeEnvelope == null || amplitudeEnvelope.Length < 2)
        {
            Debug.LogWarning("[SystemHaptics] Parsed clip contains no amplitude envelope, or less than two points.");
            return false;
        }

        try
        {
            // AmplitudePoints
            var amplitudePoints = new HapticsParametricPoint[amplitudeEnvelope.Length];
            for (int i = 0; i < amplitudeEnvelope.Length; i++)
            {
                var e = amplitudeEnvelope[i];
                amplitudePoints[i] = new HapticsParametricPoint
                {
                    Time = (long)(e.time * 1e9), // Convert seconds to nanoseconds
                    Value = e.amplitude
                };
            }
            vibration.AmplitudePoints = amplitudePoints;

            // FrequencyPoints
            var frequencyPoints = new HapticsParametricPoint[frequencyEnvelope.Length];
            for (int i = 0; i < frequencyEnvelope.Length; i++)
            {
                var e = frequencyEnvelope[i];
                frequencyPoints[i] = new HapticsParametricPoint
                {
                    Time = (long)(e.time * 1e9),
                    Value = e.frequency
                };
            }
            vibration.FrequencyPoints = frequencyPoints;

            // Transients
            var transientsList = new List<HapticsParametricTransient>();
            foreach (var e in amplitudeEnvelope)
            {
                if (e.emphasis != null)
                {
                    transientsList.Add(new HapticsParametricTransient
                    {
                        Time = (long)(e.time * 1e9),
                        Amplitude = e.emphasis.amplitude,
                        Frequency = e.emphasis.frequency
                    });
                }
            }
            vibration.Transients = transientsList.ToArray();

            // Provide default values for rendering range (see XR_EXTX1_haptic_parametric specs for details).
            vibration.MinFrequencyHz = (float)OVRPlugin.HapticsConstants.ParametricHapticsUnspecifiedFrequency;
            vibration.MaxFrequencyHz = (float)OVRPlugin.HapticsConstants.ParametricHapticsUnspecifiedFrequency;

            // Check if the amplitude envelope contains at least two breakpoints.
            bool hasPoints = vibration.AmplitudePoints is { Length: > 1 };
            return hasPoints;
        }
        catch
        {
            vibration = default;
            return false;
        }
    }

    /// <summary>
    /// Represents the set of predefined system haptic feedback patterns.
    /// </summary>
    /// <remarks>
    /// A pattern can be played back on a target playback location using <see cref="SystemHapticsPlayPattern"/>.
    /// </remarks>
    public enum SystemHapticsPattern
    {
        /// <summary>
        /// Use to indicate a successful action, completion of a task, or "positive" toast (notification).
        /// </summary>
        Success,
        /// <summary>
        /// Use to indicate a warning or non-critical issue that requires user attention.
        /// </summary>
        Warning,
        /// <summary>
        /// Use to indicate a critical error or failure that requires immediate user attention.
        /// </summary>
        Error,
        /// <summary>
        /// Use when the user hovers over an interactive element or object.
        /// </summary>
        Hover,
        /// <summary>
        /// Use when the user presses a button or similar control.
        /// </summary>
        Press,
        /// <summary>
        /// Use to confirm a selection action, such as toggling a switch on or activating a checkbox.
        /// </summary>
        Select,
        /// <summary>
        /// Use to confirm a deselection action, such as toggling a switch off or deactivating a checkbox.
        /// </summary>
        Deselect,
        /// <summary>
        /// Use when the user grabs or picks up a component or object.
        /// </summary>
        Grab,
        /// <summary>
        /// Use when the user releases a previously grabbed component or object.
        /// </summary>
        Release
    }
}
