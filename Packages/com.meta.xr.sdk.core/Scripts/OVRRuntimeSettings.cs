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
using System.IO;
using System;

#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
#endif

public class OVRRuntimeSettings : OVRRuntimeAssetsBase
{
    private const string _assetName = "OculusRuntimeSettings";
    private static OVRRuntimeSettings _instance;

    public static OVRRuntimeSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = GetRuntimeSettings();
            }

            return _instance;
        }
    }

    public OVRManager.ColorSpace colorSpace = OVRManager.ColorSpace.P3;

    [SerializeField] private bool requestsVisualFaceTracking = true;
    public bool RequestsVisualFaceTracking
    {
        get => requestsVisualFaceTracking;
        set => requestsVisualFaceTracking = value;
    }

    [SerializeField] private bool requestsAudioFaceTracking = true;
    public bool RequestsAudioFaceTracking
    {
        get => requestsAudioFaceTracking;
        set => requestsAudioFaceTracking = value;
    }


    [SerializeField] private string telemetryProjectGuid;
    internal string TelemetryProjectGuid
    {
        get
        {
            if (string.IsNullOrEmpty(telemetryProjectGuid))
            {
                telemetryProjectGuid = Guid.NewGuid().ToString();
#if UNITY_EDITOR
                CommitRuntimeSettings(this);
#endif
            }
            return telemetryProjectGuid;
        }
    }


    [SerializeField] private OVRPlugin.BodyTrackingFidelity2 bodyTrackingFidelity = OVRPlugin.BodyTrackingFidelity2.Low;

    public OVRPlugin.BodyTrackingFidelity2 BodyTrackingFidelity
    {
        get => bodyTrackingFidelity;
        set => bodyTrackingFidelity = value;
    }

    [SerializeField] private OVRPlugin.BodyJointSet bodyTrackingJointSet = OVRPlugin.BodyJointSet.UpperBody;

    public OVRPlugin.BodyJointSet BodyTrackingJointSet
    {
        get => bodyTrackingJointSet;
        set => bodyTrackingJointSet = value;
    }

#if UNITY_EDITOR
    public static string GetOculusRuntimeSettingsAssetPath()
    {
        return GetAssetPath(_assetName);
    }

    public static void CommitRuntimeSettings(OVRRuntimeSettings runtimeSettings)
    {
        string runtimeSettingsAssetPath = GetOculusRuntimeSettingsAssetPath();
        if (AssetDatabase.GetAssetPath(runtimeSettings) != runtimeSettingsAssetPath)
        {
            Debug.LogWarningFormat("The asset path of RuntimeSettings is wrong. Expect {0}, get {1}",
                runtimeSettingsAssetPath, AssetDatabase.GetAssetPath(runtimeSettings));
        }

        EditorUtility.SetDirty(runtimeSettings);
    }
#endif

    public static OVRRuntimeSettings GetRuntimeSettings()
    {
        LoadAsset(out OVRRuntimeSettings settings, _assetName);
#if !UNITY_EDITOR
        if (settings == null)
        {
            Debug.LogWarning("Failed to load runtime settings. Using default runtime settings instead.");
            settings = ScriptableObject.CreateInstance<OVRRuntimeSettings>();
        }
#endif
        return settings;
    }
}
