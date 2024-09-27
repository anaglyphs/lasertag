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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
#endif

public class OVROverlayCanvasSettings : OVRRuntimeAssetsBase
{
    private const string _assetName = "OVROverlayCanvasSettings";
    private static OVROverlayCanvasSettings _instance;

    public static OVROverlayCanvasSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = GetOverlayCanvasSettings();
            }

            return _instance;
        }
    }

    [SerializeField]
    private Shader _overrideCanvasShader = null;

    [SerializeField]
    private Shader _transparentImposterShader = null;

    [SerializeField]
    private Shader _opaqueImposterShader = null;

    [SerializeField]
    private bool _overrideDefaultCanvasMaterial = false;


#if UNITY_EDITOR
    public static string GetOculusOverlayCanvasSettingsAssetPath()
    {
        return GetAssetPath(_assetName);
    }

    public static void CommitOverlayCanvasSettings(OVROverlayCanvasSettings settings)
    {
        string runtimeSettingsAssetPath = GetOculusOverlayCanvasSettingsAssetPath();
        if (AssetDatabase.GetAssetPath(settings) != runtimeSettingsAssetPath)
        {
            Debug.LogWarningFormat("The asset path of OverlayCanvasSettings is wrong. Expect {0}, get {1}",
                runtimeSettingsAssetPath, AssetDatabase.GetAssetPath(settings));
        }

        EditorUtility.SetDirty(settings);
    }
#endif

    public static OVROverlayCanvasSettings GetOverlayCanvasSettings()
    {
        LoadAsset(out OVROverlayCanvasSettings settings, _assetName);
#if !UNITY_EDITOR
        if (settings == null)
        {
            Debug.LogWarning("Failed to load runtime settings. Using default runtime settings instead.");
            settings = ScriptableObject.CreateInstance<OVROverlayCanvasSettings>();
        }
#endif
        return settings;
    }

    public void ApplyGlobalSettings()
    {
        if (_overrideDefaultCanvasMaterial)
        {
            Canvas.GetDefaultCanvasMaterial().shader = _overrideCanvasShader;
        }
    }

    public Shader GetShader(OVROverlayCanvas.DrawMode drawMode)
    {
        switch (drawMode)
        {
            case OVROverlayCanvas.DrawMode.Opaque:
            case OVROverlayCanvas.DrawMode.OpaqueWithClip:
            case OVROverlayCanvas.DrawMode.AlphaToMask:
                return _opaqueImposterShader;
            case OVROverlayCanvas.DrawMode.TransparentCorrectAlpha:
            case OVROverlayCanvas.DrawMode.TransparentDefaultAlpha:
            default:
                return _transparentImposterShader;

        }
    }
}
