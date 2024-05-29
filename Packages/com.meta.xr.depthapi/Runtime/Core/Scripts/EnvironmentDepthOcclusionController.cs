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

using Unity.XR.Oculus;
using UnityEngine;

namespace Meta.XR.Depth
{
    [RequireComponent(typeof(EnvironmentDepthTextureProvider))]
    public class EnvironmentDepthOcclusionController : MonoBehaviour
    {
#if DEPTH_API_SUPPORTED
        public static readonly string HardOcclusionKeyword = "HARD_OCCLUSION";
        public static readonly string SoftOcclusionKeyword = "SOFT_OCCLUSION";

        [SerializeField] private OcclusionType _occlusionType = OcclusionType.NoOcclusion;

        private EnvironmentDepthTextureProvider _depthTextureProvider;

        private void Awake()
        {
            _depthTextureProvider = GetComponent<EnvironmentDepthTextureProvider>();
        }

        private void OnEnable()
        {
            _depthTextureProvider.OnDepthTextureAvailabilityChanged += HandleDepthTextureChanged;
        }

        private void OnDisable()
        {
            _depthTextureProvider.OnDepthTextureAvailabilityChanged -= HandleDepthTextureChanged;
        }

        private void Start()
        {
            if (_occlusionType != OcclusionType.NoOcclusion)
            {
                EnableOcclusionType(_occlusionType);
            }
        }

#if UNITY_EDITOR
        private void OnApplicationQuit()
        {
            SetOcclusionShaderKeywords(OcclusionType.NoOcclusion);
        }
#endif
        /// <summary>
        /// Sets the global value of occlusions
        /// </summary>
        /// <param name="newOcclusionType"></param>
        /// <param name="updateDepthTextureProvider"> If this is set to true, it disables the depthtextureprovider if newOcclusionType is set to NoOcclusions and enables it otherwise</param>
        public void EnableOcclusionType(OcclusionType newOcclusionType, bool updateDepthTextureProvider = true)
        {
            _occlusionType = Utils.GetEnvironmentDepthSupported() ? newOcclusionType : OcclusionType.NoOcclusion;
            bool enableDepthTextureFlag = _occlusionType != OcclusionType.NoOcclusion;//true for no occlusion i.e. we want to enable it, false for occlusions
            if ((updateDepthTextureProvider) &&
                (_depthTextureProvider.GetEnvironmentDepthEnabled() != enableDepthTextureFlag)) //we only SetEnvironmentEnabled if needed i.e. the state that it is in right now is different than the state that we want it to be in
            {
                _depthTextureProvider.SetEnvironmentDepthEnabled(isEnabled: enableDepthTextureFlag);
            }
            else
            {
                SetOcclusionShaderKeywords(_occlusionType);
            }
        }

        private void SetOcclusionShaderKeywords(OcclusionType newOcclusionType)
        {
            switch (newOcclusionType)
            {
                case OcclusionType.HardOcclusion:
                    Shader.DisableKeyword(SoftOcclusionKeyword);
                    Shader.EnableKeyword(HardOcclusionKeyword);
                    break;
                case OcclusionType.SoftOcclusion:
                    Shader.DisableKeyword(HardOcclusionKeyword);
                    Shader.EnableKeyword(SoftOcclusionKeyword);
                    break;
                default:
                    Shader.DisableKeyword(HardOcclusionKeyword);
                    Shader.DisableKeyword(SoftOcclusionKeyword);
                    break;
            }
        }

        private void HandleDepthTextureChanged(bool isDepthTextureAvailable)
        {
            if (isDepthTextureAvailable)
            {
                SetOcclusionShaderKeywords(_occlusionType);
            }
            else
            {
                SetOcclusionShaderKeywords(OcclusionType.NoOcclusion);
            }
        }
#endif
    }
}
