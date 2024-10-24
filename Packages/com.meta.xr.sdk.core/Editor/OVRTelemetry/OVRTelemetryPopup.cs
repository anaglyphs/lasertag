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

using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class OVRTelemetryPopup
{
    static OVRTelemetryPopup()
    {
        EditorApplication.update += Update;
    }

    private static void Update()
    {
        EditorApplication.update -= Update;
        if (ShouldShowPopup())
        {
            ShowPopup();
        }

        OVRTelemetryConsent.SendConsentEvent(OVRTelemetryConstants.OVRManager.ConsentOrigins.Legacy);
    }

    private static bool ShouldShowPopup()
    {
        if (Application.isBatchMode)
        {
            return false;
        }

        return !UserHasPreviouslyAnswered;
    }

    private static bool UserHasPreviouslyAnswered => OVRTelemetryConsent.HasSetTelemetryEnabled;

    private static void ShowPopup()
    {
        var consent = EditorUtility.DisplayDialog(
            "Help to Improve Meta SDKs",
            "Allow Meta to collect usage data on its SDKs, such as package name, class names and plugin configuration in your projects using Meta SDKs on this machine." +
            " This data helps improve the Meta SDKs and is collected in accordance with Meta's Privacy Policy." +
            $"\n\nYou can always change your selection at:  Edit > Preferences > {OVREditorUtils.MetaXRPublicName} > Telemetry",
            "Send usage statistics",
            "Don't send");

        RecordConsent(consent);
    }

    private static void RecordConsent(bool consent) =>
        OVRTelemetryConsent.SetTelemetryEnabled(consent, OVRTelemetryConstants.OVRManager.ConsentOrigins.Popup);
}
