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
    /// depth testing for the <see cref="CompositionLayer"/> instance
    /// on the same game object.
    ///
    /// Support for this component is up the the instance of <see cref="ILayerProvider" />
    /// currently assigned to the <see cref="Unity.XR.CompositionLayers.Services.CompositionLayerManager" />.
    ///
    /// If this extension is not added to a layer game object, it is expected that
    /// the provider will assume no depth testing for this composition layer.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("XR/Composition Layers/Extensions/Depth Test")]
    public class DepthTestExtension : CompositionLayerExtension
    {
        const uint XR_FB_composition_layer_depth_test = 1000212000;

        public enum XrCompareOp
        {
            Never = 0,
            Less = 1,
            Equal = 2,
            LessOrEqual = 3,
            Greater = 4,
            NotEqual = 5,
            GreatorOrEqual = 6,
            Always = 7
        }

        /// <summary>
        /// Options for which type of object this extension should be associated with.
        /// </summary>
        public override ExtensionTarget Target => ExtensionTarget.Layer;

        [SerializeField]
        [Tooltip("If depth testing is enabled for this layer")]
        bool m_DepthMask = false;

        [SerializeField]
        [Tooltip("The depth testing compare operation used for this layer")]
        XrCompareOp m_CompareOp = XrCompareOp.Less;

        NativeArray<Native.XrCompositionLayerDepthTestFB> m_NativeArray;

        /// <summary>
        /// The value used to scale a given color by.
        /// </summary>
        public bool DepthMask
        {
            get => m_DepthMask;
            set => m_DepthMask = UpdateValue(m_DepthMask, value);
        }

        /// <summary>
        /// The value used to scale a given color by.
        /// </summary>
        public XrCompareOp CompareOp
        {
            get => m_CompareOp;
            set => m_CompareOp = UpdateValue(m_CompareOp, value);
        }

        ///<summary>
        /// Return a pointer to this extension's native struct.
        /// </summary>
        /// <returns>the pointer to the depth test extension's native struct.</returns>
        public override unsafe void* GetNativeStructPtr()
        {
            var openXRStruct = new Native.XrCompositionLayerDepthTestFB(XR_FB_composition_layer_depth_test, null, m_DepthMask, (uint)m_CompareOp);

            if (!m_NativeArray.IsCreated)
                m_NativeArray = new NativeArray<Native.XrCompositionLayerDepthTestFB>(1, Allocator.Persistent);

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
            public unsafe struct XrCompositionLayerDepthTestFB
            {
                public XrCompositionLayerDepthTestFB(uint type, void* next, bool depthMask, uint compareOp)
                {
                    this.type = type;
                    this.next = next;
                    this.depthMask = depthMask;
                    this.compareOp = compareOp;
                }

                private uint type;
                private void* next;
                private bool depthMask;
                private uint compareOp;
            }
        }
    }
}
#endif
