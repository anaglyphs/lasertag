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

#if USING_XR_SDK_OPENXR

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;

namespace Meta.XR
{
    /// <summary>
    /// Base class for Meta <see cref="OpenXRFeature"/>s.
    /// </summary>
    /// <typeparam name="T">The specific <see cref="OpenXRFeature"/>.</typeparam>
    public abstract class MetaFeatureBase<T> : OpenXRFeature where T : MetaFeatureBase<T>
    {
        /// <summary>
        /// The singleton instance of this specific <see cref="OpenXRFeature"/>, or `null`.
        /// </summary>
        /// <remarks>
        /// This class is designed as a singleton. This field is set when the OpenXR <see cref="Instance"/> is
        /// created (<see cref="OnInstanceCreate"/>) and set to `null` when the OpenXR <see cref="Instance"/> is
        /// destroyed (<see cref="OnInstanceDestroy"/>).
        /// </remarks>
        internal static T s_featureInstance;

        /// <summary>
        /// The delegate used to resolve OpenXR function pointers.
        /// </summary>
        /// <remarks>
        /// This is set automatically in <see cref="HookGetInstanceProcAddr"/> and used by
        /// <see cref="GetInstanceDelegate{T}"/> and <see cref="GetInstanceDelegate{TDelegate}"/> to resolve delegates.
        ///
        /// It can also be used with <see cref="OpenXRNativeFuncs.GetInstanceDelegate{T}"/> and
        /// <see cref="OpenXRNativeFuncs.GetInstanceDelegate{TDelegate}"/> to accomplish a similar purpose.
        /// </remarks>
        private OpenXRNativeFuncs.xrGetInstanceProcAddr _xrGetInstanceProcAddr { get; set; }

        /// <summary>
        /// Get the singleton feature, if it is enabled.
        /// </summary>
        /// <param name="feature">The <see cref="OpenXRFeature"/> to retrieve.</param>
        /// <returns>Returns true if an instance of the feature exists and is enabled, otherwise false.</returns>
        public static bool TryGet(out T feature) => (feature = s_featureInstance) && feature.enabled;

        /// <summary>
        /// The <see cref="XrSession"/> associated with this <see cref="OpenXRFeature"/>.
        /// </summary>
        /// <remarks>
        /// This property is set by <see cref="OnSessionCreate"/> and <see cref="OnSessionDestroy"/>.
        /// </remarks>
        [field: NonSerialized]
        public XrSession Session { get; protected set; }

        /// <summary>
        /// The <see cref="XrInstance"/> associated with this <see cref="OpenXRFeature"/>.
        /// </summary>
        /// <remarks>
        /// This property is set by <see cref="OnInstanceCreate"/> and <see cref="OnInstanceDestroy"/>.
        /// </remarks>
        [field: NonSerialized]
        public XrInstance Instance { get; protected set; }

        /// <summary>
        /// The <see cref="XrSystemId"/> associated with this <see cref="OpenXRFeature"/>.
        /// </summary>
        /// <remarks>
        /// This property is set by <see cref="OnSystemChange"/>.
        /// </remarks>
        [field: NonSerialized]
        public XrSystemId SystemId { get; protected set; }

        /// <summary>
        /// The <see cref="XrSpace"/> associated with this <see cref="OpenXRFeature"/>.
        /// </summary>
        /// <remarks>
        /// This property is set by <see cref="OnAppSpaceChange"/>.
        /// </remarks>
        [field: NonSerialized]
        public XrSpace AppSpace { get; protected set; }

        /// <inheritdoc />
        protected override void OnSystemChange(ulong xrSystem)
        {
            SystemId = (XrSystemId)xrSystem;
            base.OnSystemChange(xrSystem);
        }

        /// <inheritdoc />
        protected override void OnAppSpaceChange(ulong xrSpace)
        {
            AppSpace = (XrSpace)xrSpace;
            base.OnAppSpaceChange(xrSpace);
        }

        /// <inheritdoc />
        protected override void OnSessionCreate(ulong xrSession)
        {
            Session = (XrSession)xrSession;
            base.OnSessionCreate(xrSession);
        }

        /// <inheritdoc />
        protected override void OnSessionDestroy(ulong xrSession)
        {
            Session = 0;
            base.OnSessionDestroy(xrSession);
        }

        /// <inheritdoc />
        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            s_featureInstance = (T)this;
            Instance = (XrInstance)xrInstance;
            BindFunctionPointers();
            return base.OnInstanceCreate(xrInstance);
        }

        /// <inheritdoc />
        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            UnbindFunctionPointers();
            Instance = 0;
            _xrGetInstanceProcAddr = null;
            s_featureInstance = null;
            base.OnInstanceDestroy(xrInstance);
        }

        /// <inheritdoc />
        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            _xrGetInstanceProcAddr = Marshal.GetDelegateForFunctionPointer<OpenXRNativeFuncs.xrGetInstanceProcAddr>(func);
            return base.HookGetInstanceProcAddr(func);
        }

        /// <summary>
        /// Resolves OpenXR function delegates by name.
        /// </summary>
        /// <remarks>
        /// Use this in <see cref="BindFunctionPointers"/> to resolve OpenXR function delegates by their name.
        ///
        /// This requires a valid <see cref="Instance"/> and should not be called before <see cref="OnInstanceCreate"/>
        /// or after <see cref="OnInstanceDestroy"/>. Typically, you only need to use this method in your implementation
        /// of <see cref="BindFunctionPointers"/>.
        /// </remarks>
        /// <param name="functionName">The name of the function to resolve.</param>
        /// <param name="delegate">If successful, contains a reference to the delegate named <paramref name="functionName"/>; otherwise `null`.</param>
        /// <typeparam name="TDelegate">The type of the function delegate.</typeparam>
        /// <returns>Returns the result of the attempt to resolve the function delegate.</returns>
        /// <exception cref="InvalidOperationException">Thrown if there is no method available to resolve function pointers,
        /// which typically means this method is invoked without an active OpenXR instance (<see cref="OnInstanceCreate"/>).</exception>
        /// <seealso cref="BindFunctionPointers"/>
        protected XrResult GetInstanceDelegate<TDelegate>(string functionName, out TDelegate @delegate)
            where TDelegate : class
        {
            if (_xrGetInstanceProcAddr == null)
            {
                throw new InvalidOperationException($"No delegate for xrGetInstanceProcAddr. " +
                                                    $"This usually means there is either no OpenXR runtime available, or " +
                                                    $"you are trying to obtain a delegate before initialization.");
            }

            return OpenXRNativeFuncs.GetInstanceDelegate(_xrGetInstanceProcAddr, Instance, functionName, out @delegate);
        }

        /// <summary>
        /// Implement this method to bind function pointers.
        /// </summary>
        /// <remarks>
        /// This method is invoked to allow derived classes to resolve function pointers. Define function delegates
        /// that match their OpenXR counterparts and use <see cref="GetInstanceDelegate{TDelegate}"/> to look up each
        /// delegate by string name.
        /// </remarks>
        /// <seealso cref="UnbindFunctionPointers"/>
        protected abstract void BindFunctionPointers();

        /// <summary>
        /// Implement this method to unbind function pointers.
        /// </summary>
        /// <remarks>
        /// This method is invoked to allow derived classes to reset function delegates set in
        /// <see cref="BindFunctionPointers"/>.
        /// </remarks>
        /// <seealso cref="BindFunctionPointers"/>
        protected abstract void UnbindFunctionPointers();
    }
}

#endif
