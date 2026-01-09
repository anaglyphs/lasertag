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
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
#endif

public class OVROverlayCanvasSettings : OVRRuntimeAssetsBase
{
    private const string kAssetName = "OVROverlayCanvasSettings";

#if !UNITY_2020_1_OR_NEWER
    private const string kOverrideUiShaderName = "UI/Default Correct";
#endif

    private const string kBuiltInOpaqueShaderName = "UI/Prerendered Opaque";
    private const string kUrpOpaqueShaderName = "URP/UI/Prerendered Opaque";
    private const string kBuiltInTransparentShaderName = "UI/Prerendered";
    private const string kUrpTransparentShaderName = "URP/UI/Prerendered";

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

#if !UNITY_2020_1_OR_NEWER
    [SerializeField]
    private Shader _overrideCanvasShader = null;
#endif

    [SerializeField]
    private Shader _transparentImposterShader = null;

    [SerializeField]
    private Shader _opaqueImposterShader = null;

#if !UNITY_2020_1_OR_NEWER
    [SerializeField]
    private bool _overrideDefaultCanvasMaterial = false;
#endif

    public int MaxSimultaneousCanvases = 1;
    public int CanvasRenderLayer = 31;
    public int CanvasLayer = -1;

#if UNITY_EDITOR
    public static string GetOculusOverlayCanvasSettingsAssetPath()
    {
        return GetAssetPath(kAssetName);
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

    private static OVROverlayCanvasSettings GetOverlayCanvasSettings()
    {
        LoadAsset(out OVROverlayCanvasSettings settings, kAssetName);
#if !UNITY_EDITOR
        if (settings == null)
        {
            Debug.LogWarning("Failed to load runtime settings. Using default runtime settings instead.");
            settings = ScriptableObject.CreateInstance<OVROverlayCanvasSettings>();
        }
#else
        if (settings == null)
        {
            throw new UnityEditor.Build.BuildFailedException("OVROverlayCanvasSettings must be created before building player.");
        }
#endif
        settings.EnsureInitialized();
        return settings;
    }

    public void ApplyGlobalSettings()
    {
#if !UNITY_2020_1_OR_NEWER
        if (_overrideDefaultCanvasMaterial)
        {
            Canvas.GetDefaultCanvasMaterial().shader = _overrideCanvasShader;
        }
#endif
    }

    public Shader GetShader(OVROverlayCanvas.DrawMode drawMode)
    {
        switch (drawMode)
        {
            case OVROverlayCanvas.DrawMode.Opaque:
            case OVROverlayCanvas.DrawMode.OpaqueWithClip:
            case OVROverlayCanvas.DrawMode.AlphaToMask:
                return _opaqueImposterShader;
            case OVROverlayCanvas.DrawMode.Transparent:
            default:
                return _transparentImposterShader;
        }
    }

    private static bool UsingBuiltInRenderPipeline()
    {
        return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == default;
    }

    private static void EnsureShaderInitialized(ref Shader shader, string shaderName, string replaceShaderName)
    {
        if (shader != null && shader.name != replaceShaderName)
        {
            return;
        }
        var s = Shader.Find(shaderName);
        if (s == null)
        {
            Debug.LogError($"Failed to find shader \"{shaderName}\"");
            return;
        }
        shader = s;
    }

    private void EnsureInitialized()
    {
#if !UNITY_2020_1_OR_NEWER
        EnsureShaderInitialized(ref _overrideCanvasShader, kOverrideUiShaderName, string.Empty);
#endif

        bool useBuiltInShaders = UsingBuiltInRenderPipeline();
        EnsureShaderInitialized(
            ref _opaqueImposterShader,
            useBuiltInShaders ? kBuiltInOpaqueShaderName : kUrpOpaqueShaderName,
            useBuiltInShaders ? kUrpOpaqueShaderName : kBuiltInOpaqueShaderName);
        EnsureShaderInitialized(
            ref _transparentImposterShader,
            useBuiltInShaders ? kBuiltInTransparentShaderName : kUrpTransparentShaderName,
            useBuiltInShaders ? kUrpTransparentShaderName : kBuiltInTransparentShaderName);
    }

    private void OnValidate()
    {
        EnsureInitialized();
    }
}
