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
using UnityEditor;
using UnityEngine;
using static OVRPlugin;

internal static class OVRTelemetryConsent
{
    public static Action<bool> OnTelemetrySet;
    private const string HasSentConsentEventKey = "OVRTelemetry.HasSentConsentEvent";
    private const string TelemetryEnabledKey = "OVRTelemetry.TelemetryEnabled";

    private static bool HasSentConsentEvent
    {
        get => EditorPrefs.GetBool(HasSentConsentEventKey, false);
        set => EditorPrefs.SetBool(HasSentConsentEventKey, value);
    }

    public static bool TelemetryEnabled
    {
        get
        {
            const bool defaultTelemetryStatus = false;

            return EditorPrefs.GetBool(TelemetryEnabledKey, defaultTelemetryStatus);
        }

        private set => EditorPrefs.SetBool(TelemetryEnabledKey, value);
    }

    public static bool HasSetTelemetryEnabled => EditorPrefs.HasKey(TelemetryEnabledKey);

    public static bool SetTelemetryEnabled(bool enabled, OVRTelemetryConstants.OVRManager.ConsentOrigins origin)
    {
        Result result = OVRPlugin.SetDeveloperTelemetryConsent(enabled ? OVRPlugin.Bool.True : OVRPlugin.Bool.False);
        TelemetryEnabled = enabled;
        OVRPlugin.Qpl.SetConsent(enabled ? OVRPlugin.Bool.True : OVRPlugin.Bool.False);
        SendConsentEvent(origin);
        OnTelemetrySet?.Invoke(enabled);
        return result == Result.Success;
    }

    public static void SendConsentEvent(OVRTelemetryConstants.OVRManager.ConsentOrigins origin)
    {
        if (HasSentConsentEvent && origin != OVRTelemetryConstants.OVRManager.ConsentOrigins.Settings)
        {
            return;
        }

        if (!HasSetTelemetryEnabled)
        {
            return;
        }


        OVRTelemetry.Start(OVRTelemetryConstants.OVRManager.MarkerId.Consent)
            .AddAnnotation(OVRTelemetryConstants.OVRManager.AnnotationTypes.Origin, origin.ToString())
            .SetResult(TelemetryEnabled ? OVRPlugin.Qpl.ResultType.Success : OVRPlugin.Qpl.ResultType.Fail)
            .Send();

        HasSentConsentEvent = true;
    }
}
