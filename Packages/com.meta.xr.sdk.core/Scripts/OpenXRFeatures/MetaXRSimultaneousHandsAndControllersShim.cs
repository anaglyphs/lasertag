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
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Meta.XR
{
    partial class MetaXRFeature
    {
        private static OVRPlugin.Bool isMultiModalityHandsControllersEnabled = OVRPlugin.Bool.False;
        internal unsafe OVRPlugin.Result ovrp_SetSimultaneousHandsAndControllersEnabled(OVRPlugin.Bool enabled)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (!_simultaneousHandsAndControllersEnabled)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (!_simultaneousHandsAndControllersSupported)
            {
                return OVRPlugin.Result.Failure_Unsupported;
            }

            if (Command.xrPauseSimultaneousHandsAndControllersTrackingMETA == null || Command.xrResumeSimultaneousHandsAndControllersTrackingMETA == null)
            {
                LogError("xrPauseSimultaneousHandsAndControllersTrackingMETA and xrResumeSimultaneousHandsAndControllersTrackingMETA commands were not loaded.");
                return OVRPlugin.Result.Failure_Unsupported;
            }

            var result = OVRPlugin.Result.Success;
            if (enabled == OVRPlugin.Bool.True)
            {
                var resumeInfo = new XrSimultaneousHandsAndControllersTrackingResumeInfoMETA {
                    Type = XrSimultaneousHandsAndControllersTrackingResumeInfoMETA.StructureType
                };
                result = Command.xrResumeSimultaneousHandsAndControllersTrackingMETA(Session, in resumeInfo).ToOVRPluginType();
            }
            else
            {
                var pauseInfo = new XrSimultaneousHandsAndControllersTrackingPauseInfoMETA {
                    Type = XrSimultaneousHandsAndControllersTrackingPauseInfoMETA.StructureType
                };
                result = Command.xrPauseSimultaneousHandsAndControllersTrackingMETA(Session, in pauseInfo).ToOVRPluginType();
            }

            isMultiModalityHandsControllersEnabled = enabled;
            return result;
        }

        internal unsafe OVRPlugin.Result ovrp_IsMultimodalHandsControllersSupported(ref OVRPlugin.Bool enabled)
        {
            enabled = isMultiModalityHandsControllersEnabled;
            return OVRPlugin.Result.Success;
        }
    }
}
#endif
