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

using System.Collections.Generic;
using UnityEngine;

namespace com.meta.xr.depthapi.utils
{
    public class OcclusionDepthBias : MonoBehaviour
    {
        [field: SerializeField] public float DepthBiasValue { get; private set; }
        public bool DoesAffectChildren;

        private List<Material> _materials;

        void Awake()
        {
            _materials = new List<Material>();
            if (DoesAffectChildren)
            {
                var renderers = GetComponentsInChildren<Renderer>();
                foreach (var rend in renderers)
                {
                    foreach (var material in rend.materials)
                        _materials.Add(material);
                }
            }
            else
            {
                TryGetComponent(out Renderer rend);
                if (rend != null)
                    foreach (var material in rend.materials)
                        _materials.Add(material);
            }
            SetDepthBias(DepthBiasValue);
        }

        public void SetDepthBias(float value)
        {
            DepthBiasValue = value;
            if (_materials.Count <= 0)
            {
                Debug.LogWarning("No materials found on object. This component will not do anything");
            }
            else
            {
                foreach (var material in _materials)
                {
                    material.SetFloat("_EnvironmentDepthBias", DepthBiasValue);
                }
            }
        }

        public void AdjustDepthBias(float value)
        {
            DepthBiasValue += value;
            SetDepthBias(DepthBiasValue);
        }
    }
}
