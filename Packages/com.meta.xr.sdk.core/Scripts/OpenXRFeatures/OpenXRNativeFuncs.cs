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
using System.Runtime.InteropServices;
using UnityEngine;

namespace Meta.XR
{
    public static partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrGetInstanceProcAddr(
            XrInstance instance,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            out IntPtr function);

        public delegate XrResult xrGetSystem(
            XrInstance instance,
            in XrSystemGetInfo getInfo,
            out XrSystemId systemId);

        public delegate XrResult xrGetSystemProperties(
            XrInstance instance,
            XrSystemId systemId,
            ref XrSystemProperties properties);

        public delegate XrResult xrLocateSpace(
            XrSpace space,
            XrSpace baseSpace,
            XrTime time,
            ref XrSpaceLocation location);

        public static XrResult GetInstanceDelegate<TDelegate>(xrGetInstanceProcAddr getInstanceProcAddr, XrInstance instance, string functionName, out TDelegate @delegate)
            where TDelegate : class
        {
            if (getInstanceProcAddr == null)
                throw new ArgumentNullException(nameof(getInstanceProcAddr));

            var result = getInstanceProcAddr(instance, functionName, out var functionPointer);
            if (result.IsSuccess())
            {
                @delegate = Marshal.GetDelegateForFunctionPointer<TDelegate>(functionPointer);
            }
            else
            {
                @delegate = null;
                Debug.LogWarning($"{result}: xrGetInstanceProcAddr failed to get function pointer for '{functionName}'.");
            }

            return result;
        }
    }
}
