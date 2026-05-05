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

#if USING_XR_COMPOSITION_LAYERS
using System;
using UnityEngine;
using Unity.XR.CompositionLayers.Provider;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;

namespace Unity.XR.CompositionLayers.Extensions
{
    /// <summary>
    /// Subclass of <see cref="CompositionLayerExtension" /> to support
    /// composition layer filtering settings for the <see cref="CompositionLayer"/> instance
    /// on the same game object.
    ///
    /// Support for this component is up the the instance of <see cref="ILayerProvider" />
    /// currently assigned to the <see cref="Unity.XR.CompositionLayers.Services.CompositionLayerManager" />.
    ///
    /// If this extension is not added to a layer game object, it is expected that
    /// the provider will assume no filtering layer flags are applied.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("XR/Composition Layers/Extensions/Filtering Settings")]
    public class FilteringSettingsExtension : CompositionLayerExtension
    {
        const uint XR_FB_composition_layer_settings = 1000204000;

        [Flags]
        public enum XrCompositionLayerSettingsFlags
        {
            NormalSuperSampling = 0x1,
            QualitySuperSampling = 0x2,
            NormalSharpening = 0x4,
            QualitySharpening = 0x8,
            AutoLayerFilter = 0x20
        }

        /// <summary>
        /// Options for which type of object this extension should be associated with.
        /// </summary>
        public override ExtensionTarget Target => ExtensionTarget.Layer;

        [SerializeField]
        [Tooltip("The filtering layer flags to apply to this layer")]
        XrCompositionLayerSettingsFlags m_LayerFlags = 0;

        NativeArray<Native.XrCompositionLayerSettingsFB> m_NativeArray;

        /// <summary>
        /// The value used to scale a given color by.
        /// </summary>
        public XrCompositionLayerSettingsFlags LayerFlags
        {
            get => m_LayerFlags;
            set => m_LayerFlags = UpdateValue(m_LayerFlags, value);
        }

        ///<summary>
        /// Return a pointer to this extension's native struct.
        /// </summary>
        /// <returns>the pointer to the composition layer settings extension's native struct.</returns>
        public override unsafe void* GetNativeStructPtr()
        {
            var openXRStruct = new Native.XrCompositionLayerSettingsFB(XR_FB_composition_layer_settings, null, (ulong)m_LayerFlags);

            if (!m_NativeArray.IsCreated)
                m_NativeArray = new NativeArray<Native.XrCompositionLayerSettingsFB>(1, Allocator.Persistent);

            m_NativeArray[0] = openXRStruct;
            return m_NativeArray.GetUnsafePtr();
        }

        /// <inheritdoc cref="MonoBehaviour"/>
        public override void OnDestroy()
        {
            base.OnDestroy();

            if (m_NativeArray.IsCreated)
                m_NativeArray.Dispose();
        }

        private static class Native
        {
            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct XrCompositionLayerSettingsFB
            {
                public XrCompositionLayerSettingsFB(uint type, void* next, ulong layerFlags)
                {
                    this.type = type;
                    this.next = next;
                    this.layerFlags = layerFlags;
                }

                private uint type;
                private void* next;
                private ulong layerFlags;
            }
        }
    }
}
#endif
