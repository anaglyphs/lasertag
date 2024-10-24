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

using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Debug = UnityEngine.Debug;

namespace Meta.XR.EnvironmentDepth
{
    /// <summary>
    /// Surfaces _EnvironmentDepthTexture and complementary information
    /// for reprojection and movement compensation to shaders globally.
    /// </summary>
    public class EnvironmentDepthManager : MonoBehaviour
    {
        public const string HardOcclusionKeyword = "HARD_OCCLUSION";
        public const string SoftOcclusionKeyword = "SOFT_OCCLUSION";
        private static readonly int DepthTextureID = Shader.PropertyToID("_EnvironmentDepthTexture");
        private static readonly int ReprojectionMatricesID = Shader.PropertyToID("_EnvironmentDepthReprojectionMatrices");
        private static readonly int ZBufferParamsID = Shader.PropertyToID("_EnvironmentDepthZBufferParams");

        [SerializeField] private OcclusionShadersMode _occlusionShadersMode = OcclusionShadersMode.SoftOcclusion;
        [SerializeField] private bool _removeHands;
        [SerializeField] private Transform _trackingSpace;
        private static readonly IDepthProvider _provider = CreateProvider();
        private bool _hasPermission;
        private uint? _prevTextureId;
        private Material _preprocessMaterial;
        [CanBeNull] private RenderTexture _preprocessTexture;
        private RenderTargetSetup _preprocessRenderTargetSetup;

        [NotNull]
        private static IDepthProvider CreateProvider()
        {
#if DEPTH_API_SUPPORTED
            return new DepthProvider();
#endif
#pragma warning disable CS0162 // Unreachable code detected
            return new DepthProviderNotSupported();
#pragma warning restore CS0162
        }

        public static bool IsSupported => _provider.IsSupported;

        public bool IsDepthAvailable { get; private set; }

        /// <summary>
        /// If <see cref="OcclusionShadersMode"/> is specified, this component will enable a global shader keyword after receiving the depth texture.<br/>
        /// To enable per-object occlusion, use a provided occlusion shader or modify your custom shader to support <see cref="HardOcclusionKeyword"/> or <see cref="SoftOcclusionKeyword"/>.
        /// </summary>
        public OcclusionShadersMode OcclusionShadersMode
        {
            get => _occlusionShadersMode;
            set
            {
                if (_occlusionShadersMode == value)
                    return;
                _occlusionShadersMode = value;
                if (IsDepthAvailable)
                    SetOcclusionShaderKeywords(value);
            }
        }

        /// <summary>
        /// If set to true, hands will be removed from the depth texture.
        /// </summary>
        public bool RemoveHands
        {
            get => _removeHands;
            set
            {
                if (_removeHands == value)
                    return;
                _removeHands = value;
                if (enabled)
                    _provider.RemoveHands = value;
            }
        }

        private readonly Matrix4x4[] _reprojectionMatrices = new Matrix4x4[2];
        private XRDisplaySubsystem _xrDisplay;

        private void Awake()
        {
            Assert.AreEqual(1, FindObjectsByType<EnvironmentDepthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                $"Environment Depth: more than one {nameof(EnvironmentDepthManager)} component. Only one instance is allowed at a time. Current instance: {name}");
#if !UNITY_2022_3_OR_NEWER
            Debug.LogError("DepthAPI requires at least Unity 2022.3.0f");
#endif
            if (!IsSupported)
            {
#if UNITY_EDITOR_WIN
                Debug.LogError("Environment Depth could not be retrieved! Please ensure the following:" +
                    "\n\n" +
                    "When running over Link, the spatial data feature needs to be enabled in the Meta Quest Link app.\n" +
                    " (Settings > Beta > Spatial Data over Meta Quest Link)." +
                    "\n\n" +
                    "Check the Project Setup Tool for any project related issues.\n" +
                    " (Oculus > Tools > Project Setup Tool" +
                    "\n\n" +
                    "You are using a Quest 3 or newer device.");
#endif
                return;
            }

            var displays = new List<XRDisplaySubsystem>(1);
            SubsystemManager.GetSubsystems(displays);
            _xrDisplay = displays.Single();
            Assert.IsNotNull(_xrDisplay, nameof(_xrDisplay));

            const string shaderName = "Meta/EnvironmentDepth/Preprocessing";
            var shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader, "Depth preprocessing shader is not present in the Resources folder: " + shaderName);
            _preprocessMaterial = new Material(shader);
        }

        private void OnEnable()
        {
            if (!IsSupported)
            {
                Debug.LogError($"Environment Depth is not supported. Please check {nameof(EnvironmentDepthManager)}.{nameof(IsSupported)} before enabling {nameof(EnvironmentDepthManager)}.\n" +
                                            "Open 'Oculus -> Tools -> Project Setup Tool' to see requirements.\n");
                enabled = false;
                return;
            }

            _hasPermission = Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
            if (_hasPermission)
                _provider.SetDepthEnabled(true, _removeHands);
            else Log(LogType.Warning, $"Environment Depth requires {OVRPermissionsRequester.ScenePermission} permission. Waiting for permission...");
        }

        private void ResetDepthTextureIfAvailable()
        {
            if (IsDepthAvailable)
            {
                IsDepthAvailable = false;
                Shader.SetGlobalTexture(DepthTextureID, null);
                if (_occlusionShadersMode != OcclusionShadersMode.None)
                    SetOcclusionShaderKeywords(OcclusionShadersMode.None);
            }
        }

        private void OnDisable()
        {
            ResetDepthTextureIfAvailable();
            if (IsSupported && _hasPermission)
                _provider.SetDepthEnabled(false, false);
        }

        private void OnDestroy()
        {
            if (_preprocessMaterial != null)
                Destroy(_preprocessMaterial);
            if (_preprocessTexture != null)
                Destroy(_preprocessTexture);
        }

        private void Update()
        {
            if (!_hasPermission)
            {
                if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
                    return;
                _hasPermission = true;
                _provider.SetDepthEnabled(true, _removeHands);
            }

            TryFetchDepthTexture();
            if (!IsDepthAvailable)
                return;

            // Calculate Environment Depth Camera parameters
            // Assume NearZ and FarZ are the same for left and right eyes
            var leftEyeData = _provider.GetFrameDesc(0);
            var rightEyeData = _provider.GetFrameDesc(1);
            var depthZBufferParams = EnvironmentDepthUtils.ComputeNdcToLinearDepthParameters(leftEyeData.nearZ, leftEyeData.farZ);
            Shader.SetGlobalVector(ZBufferParamsID, depthZBufferParams);
            
            Assert.IsNotNull(_trackingSpace, $"{nameof(OVRCameraRig)} is not present in the scene.");
            var trackingSpaceViewMatrix = _trackingSpace.worldToLocalMatrix;
            _reprojectionMatrices[0] = EnvironmentDepthUtils.CalculateReprojection(leftEyeData) * trackingSpaceViewMatrix;
            _reprojectionMatrices[1] = EnvironmentDepthUtils.CalculateReprojection(rightEyeData) * trackingSpaceViewMatrix;
            Shader.SetGlobalMatrixArray(ReprojectionMatricesID, _reprojectionMatrices);
        }

        private static void SetOcclusionShaderKeywords(OcclusionShadersMode mode)
        {
            switch (mode)
            {
                case OcclusionShadersMode.HardOcclusion:
                    Shader.DisableKeyword(SoftOcclusionKeyword);
                    Shader.EnableKeyword(HardOcclusionKeyword);
                    break;
                case OcclusionShadersMode.SoftOcclusion:
                    Shader.DisableKeyword(HardOcclusionKeyword);
                    Shader.EnableKeyword(SoftOcclusionKeyword);
                    break;
                case OcclusionShadersMode.None:
                    Shader.DisableKeyword(HardOcclusionKeyword);
                    Shader.DisableKeyword(SoftOcclusionKeyword);
                    break;
                default:
                    Debug.LogError($"Environment Depth: unknown {nameof(EnvironmentDepth.OcclusionShadersMode)} {mode}");
                    break;
            }
        }

        private void TryFetchDepthTexture()
        {
            uint textureId = 0;
            if (!_xrDisplay.running || !_provider.GetDepthTextureId(ref textureId))
                return;

#if UNITY_2022_3_OR_NEWER
            var depthTexture = _xrDisplay.GetRenderTexture(textureId);
#else
            RenderTexture depthTexture = null;
#endif
            if (depthTexture == null) // can be null when the headset is awaking from sleep
            {
                ResetDepthTextureIfAvailable();
                return;
            }

            if (_prevTextureId == textureId)
                return;
            _prevTextureId = textureId;

            Assert.IsTrue(depthTexture.IsCreated(), "depthTexture.IsCreated()");
            Shader.SetGlobalTexture(DepthTextureID, depthTexture);
            if (!IsDepthAvailable)
            {
                IsDepthAvailable = true;
                if (_occlusionShadersMode != OcclusionShadersMode.None)
                    SetOcclusionShaderKeywords(_occlusionShadersMode);
            }

            if (_occlusionShadersMode == OcclusionShadersMode.SoftOcclusion)
                PreprocessDepthTexture(depthTexture);
        }

        private void PreprocessDepthTexture(RenderTexture depthTexture)
        {
            const int numSlices = 2;
            if (_preprocessTexture == null)
            {
                _preprocessTexture = new RenderTexture(depthTexture.width, depthTexture.height, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.None)
                {
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = numSlices,
                    name = nameof(_preprocessTexture),
                    depth = 0,
                };
                _preprocessTexture.Create();
                Shader.SetGlobalTexture("_PreprocessedEnvironmentDepthTexture", _preprocessTexture);

                _preprocessRenderTargetSetup = new RenderTargetSetup
                {
                    color = new[] { _preprocessTexture.colorBuffer },
                    depth = _preprocessTexture.depthBuffer,
                    depthSlice = -1,
                    colorLoad = new[] { RenderBufferLoadAction.DontCare },
                    colorStore = new[] { RenderBufferStoreAction.Store },
                    depthLoad = RenderBufferLoadAction.DontCare,
                    depthStore = RenderBufferStoreAction.DontCare,
                    mipLevel = 0,
                    cubemapFace = CubemapFace.Unknown
                };
            }

            Graphics.SetRenderTarget(_preprocessRenderTargetSetup);
            _preprocessMaterial.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3, numSlices);
        }

        [Conditional("UNITY_ASSERTIONS")]
        private static void Log(LogType type, string msg) => Debug.unityLogger.Log(type, msg);
    }

    internal interface IDepthProvider
    {
        bool IsSupported { get; }
        bool RemoveHands { set; }
        void SetDepthEnabled(bool isEnabled, bool removeHands);
        DepthFrameDesc GetFrameDesc(int eye);
        bool GetDepthTextureId(ref uint textureId);
    }

    internal class DepthProviderNotSupported : IDepthProvider
    {
        public bool IsSupported => false;
        public bool RemoveHands
        {
            set { }
        }
        public void SetDepthEnabled(bool isEnabled, bool removeHands) { }
        public DepthFrameDesc GetFrameDesc(int eye) => throw new NotSupportedException();
        public bool GetDepthTextureId(ref uint textureId) => throw new NotSupportedException();
    }

    internal struct DepthFrameDesc
    {
        internal Vector3 createPoseLocation;
        internal Vector4 createPoseRotation;
        internal float fovLeftAngle;
        internal float fovRightAngle;
        internal float fovTopAngle;
        internal float fovDownAngle;
        internal float nearZ;
        internal float farZ;
    }
}
