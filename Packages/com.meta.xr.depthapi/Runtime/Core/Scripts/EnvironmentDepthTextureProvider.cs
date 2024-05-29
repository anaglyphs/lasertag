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
using Unity.XR.Oculus;
using UnityEngine;
using UnityEngine.XR;

namespace Meta.XR.Depth
{
    /// <summary>
    /// Surfaces _EnvironmentDepthTexture and complementary information
    /// for reprojection and movement compensation to shaders globally.
    /// </summary>
    public class EnvironmentDepthTextureProvider : MonoBehaviour
    {
#if DEPTH_API_SUPPORTED
        public static readonly string DepthTexturePropertyName = "_EnvironmentDepthTexture";
        public static readonly string ReprojectionMatricesPropertyName = "_EnvironmentDepthReprojectionMatrices";
        public static readonly string Reprojection3DOFMatricesPropertyName = "_EnvironmentDepth3DOFReprojectionMatrices";
        public static readonly string ZBufferParamsPropertyName = "_EnvironmentDepthZBufferParams";

        public static readonly int DepthTextureID = Shader.PropertyToID(DepthTexturePropertyName);
        public static readonly int ReprojectionMatricesID = Shader.PropertyToID(ReprojectionMatricesPropertyName);
        public static readonly int Reprojection3DOFMatricesID = Shader.PropertyToID(Reprojection3DOFMatricesPropertyName);
        public static readonly int ZBufferParamsID = Shader.PropertyToID(ZBufferParamsPropertyName);

        public Action<bool> OnDepthTextureAvailabilityChanged;

        // Required for per object occlusion shaders
        public bool Enable6DoFCalculations = true;

        // Required for screenspace shaders
        public bool Enable3DoFCalculations = false;

        private const int FRAMES_TO_WAIT_FOR_COLD_START = 1;
        private int _framesToWaitForColdStart;

        private bool _isDepthTextureAvailable;
        private bool _isPermissionBeingQueried;

        private bool _areHandsRemoved;

        private XRDisplaySubsystem _xrDisplay;

        [SerializeField]
        private Transform _customTrackingSpaceTransform = null;

        private readonly Matrix4x4[] _reprojectionMatrices =
            new Matrix4x4[2] { Matrix4x4.identity, Matrix4x4.identity };

        private void Start()
        {
            if (!Utils.GetEnvironmentDepthSupported())
            {
                _isDepthTextureAvailable = false;
                enabled = false;
                return;
            }

            _xrDisplay = OVRManager.GetCurrentDisplaySubsystem();

            if (_customTrackingSpaceTransform == null)
            {
                _customTrackingSpaceTransform = FindObjectOfType<OVRCameraRig>()?.trackingSpace;
            }
        }

        public void SetEnvironmentDepthEnabled(bool isEnabled)
        {
            if (isEnabled)
            {
                EnableEnvironmentDepth();
            }
            else
            {
                DisableEnvironmentDepth();
            }
        }

        public void EnableEnvironmentDepth()
        {
            if (!Utils.GetEnvironmentDepthSupported() || EnvironmentDepthUtils.IsDepthRenderingRequestEnabled)
            {
                return;
            }

            EnvironmentDepthUtils.IsDepthRenderingRequestEnabled = true;

            if (!Utils.IsScenePermissionGranted())
            {
                Debug.LogWarning($"Environment Depth requires {OVRPermissionsRequester.ScenePermission} permission. Waiting for permission...");

                if (_isPermissionBeingQueried)
                    return;

                _isPermissionBeingQueried = true;
#if !UNITY_EDITOR && UNITY_ANDROID
                var permissionCallbacks = new UnityEngine.Android.PermissionCallbacks();
                //permissionCallbacks.PermissionGranted += ScenePermissionGrantedCallback;
                //permissionCallbacks.PermissionDenied += ScenePermissionDenied;

                UnityEngine.Android.Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, permissionCallbacks);

                return;
#endif // !UNITY_EDITOR && UNITY_ANDROID
            }

            StartDepthRendering();
        }

		void OnApplicationFocus(bool hasFocus)
		{
			if (hasFocus && _isPermissionBeingQueried)
			{
                EnvironmentDepthUtils.IsDepthRenderingRequestEnabled = false;
				_isPermissionBeingQueried = false;
				EnableEnvironmentDepth();
			}
		}

		public void DisableEnvironmentDepth()
        {
            if (!EnvironmentDepthUtils.IsDepthRenderingRequestEnabled)
            {
                return;
            }
            Utils.ShutdownEnvironmentDepth();
            Utils.SetEnvironmentDepthRendering(false);
            EnvironmentDepthUtils.IsDepthRenderingRequestEnabled = false;
            _isDepthTextureAvailable = false;
            OnDepthTextureAvailabilityChanged?.Invoke(false);
        }

        public bool GetEnvironmentDepthEnabled()
        {
            return EnvironmentDepthUtils.IsDepthRenderingRequestEnabled;
        }
        public void RemoveHands(bool areHandsRemoved)
        {
            _areHandsRemoved = areHandsRemoved;
            Utils.SetEnvironmentDepthHandRemoval(areHandsRemoved);
        }

        private void StartDepthRendering()
        {
            Utils.SetupEnvironmentDepth(new Utils.EnvironmentDepthCreateParams() { removeHands = _areHandsRemoved });
            Utils.SetEnvironmentDepthRendering(true);
            _framesToWaitForColdStart = FRAMES_TO_WAIT_FOR_COLD_START;
        }

        private void TryFetchDepthTexture()
        {
            if (!EnvironmentDepthUtils.IsDepthRenderingRequestEnabled)
            {
                return;
            }
            if (_framesToWaitForColdStart > 0)
            {
                _framesToWaitForColdStart--;
                return;
            }
            uint textureId = 0;

            if (Utils.GetEnvironmentDepthTextureId(ref textureId) && _xrDisplay != null && _xrDisplay.running)
            {
                var rt = _xrDisplay.GetRenderTexture(textureId);
                Shader.SetGlobalTexture(DepthTextureID, rt);
                if (_isDepthTextureAvailable != true)
                {
                    _isDepthTextureAvailable = true;
                    OnDepthTextureAvailabilityChanged?.Invoke(_isDepthTextureAvailable);
                }
            }
            else
            {
                Debug.LogWarning("DepthAPI: no environment texture");
                if (_isDepthTextureAvailable != false)
                {
                    _isDepthTextureAvailable = false;
                    OnDepthTextureAvailabilityChanged?.Invoke(_isDepthTextureAvailable);
                }
            }
        }

        private void Update()
        {
            TryFetchDepthTexture();

            if (!_isDepthTextureAvailable)
            {
                return;
            }

            OVRPlugin.Frustumf2 leftEyeFrustrum;
            OVRPlugin.Frustumf2 rightEyeFrustrum;
            if (!OVRPlugin.GetNodeFrustum2(OVRPlugin.Node.EyeLeft, out leftEyeFrustrum) ||
                !OVRPlugin.GetNodeFrustum2(OVRPlugin.Node.EyeRight, out rightEyeFrustrum))
            {
                return;
            }
#if !UNITY_EDITOR && UNITY_ANDROID
            if (OculusSettings.s_Settings.SymmetricProjection)
            {
                leftEyeFrustrum.Fov.RightTan = rightEyeFrustrum.Fov.RightTan;
                rightEyeFrustrum.Fov.LeftTan = leftEyeFrustrum.Fov.LeftTan;
            }
#endif // !UNITY_EDITOR && UNITY_ANDROID

            var leftEyeData = Utils.GetEnvironmentDepthFrameDesc(0);
            var rightEyeData = Utils.GetEnvironmentDepthFrameDesc(1);

            // Assume NearZ and FarZ are the same for left and right eyes
            float depthNearZ = leftEyeData.nearZ;
            float depthFarZ = leftEyeData.farZ;

            // Calculate Environment Depth Camera parameters
            Vector4 depthZBufferParams = EnvironmentDepthUtils.ComputeNdcToLinearDepthParameters(depthNearZ, depthFarZ);
            Shader.SetGlobalVector(ZBufferParamsID,
                depthZBufferParams);

            if (Enable6DoFCalculations)
            {
                // Calculate 6DOF reprojection matrices

//#if UNITY_EDITOR
//                var cameraRig = FindObjectOfType<OVRCameraRig>();
//                _reprojectionMatrices[0] = EnvironmentDepthUtils.CalculateReprojection(leftEyeData, cameraRig.leftEyeAnchor.position);
//                _reprojectionMatrices[1] = EnvironmentDepthUtils.CalculateReprojection(rightEyeData, cameraRig.rightEyeAnchor.position);
//#else
                _reprojectionMatrices[0] = EnvironmentDepthUtils.CalculateReprojection(leftEyeData);
                _reprojectionMatrices[1] = EnvironmentDepthUtils.CalculateReprojection(rightEyeData);
//#endif // UNITY_EDITOR

                if (_customTrackingSpaceTransform != null && !_customTrackingSpaceTransform.worldToLocalMatrix.isIdentity)
                {
                    var worldToLocalMatrix = _customTrackingSpaceTransform.worldToLocalMatrix;
                    _reprojectionMatrices[0] *= worldToLocalMatrix;
                    _reprojectionMatrices[1] *= worldToLocalMatrix;
                }

                Shader.SetGlobalMatrixArray(ReprojectionMatricesID, _reprojectionMatrices);
            }

            if (Enable3DoFCalculations)
            {
                // Calculate 3DOF reprojection matrices
                _reprojectionMatrices[0] = EnvironmentDepthUtils.Calculate3DOFReprojection(leftEyeData, leftEyeFrustrum.Fov);
                _reprojectionMatrices[1] = EnvironmentDepthUtils.Calculate3DOFReprojection(rightEyeData, rightEyeFrustrum.Fov);

                Shader.SetGlobalMatrixArray(Reprojection3DOFMatricesID, _reprojectionMatrices);
            }
        }
#if !UNITY_EDITOR && UNITY_ANDROID
        //private void ScenePermissionGrantedCallback(string permissionName)
        //{
        //    if (permissionName != OVRPermissionsRequester.ScenePermission) return;
        //    _isPermissionBeingQueried = false;

        //    if (!EnvironmentDepthUtils.IsDepthRenderingRequestEnabled) return;
        //    StartDepthRendering();
        //}
        //private void ScenePermissionDenied(string permissionName)
        //{
        //    if (permissionName != OVRPermissionsRequester.ScenePermission) return;
        //    _isPermissionBeingQueried = false;
        //}
#endif // !UNITY_EDITOR && UNITY_ANDROID

#endif // DEPTH_API_SUPPORTED
            }

        }
