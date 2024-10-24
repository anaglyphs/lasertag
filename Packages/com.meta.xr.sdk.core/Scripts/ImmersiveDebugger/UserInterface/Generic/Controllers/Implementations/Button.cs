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
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    public class Button : InteractableController
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() // reset static fields in case of domain reload disabled
        {
            _hapticsClip = null;
        }

        private static OVRHapticsClip _hapticsClip;
        private static OVRHapticsClip HapticsClip
        {
            get
            {
                if (OVRHaptics.Config.SampleSizeInBytes == 0)
                {
                    return null;
                }

                return _hapticsClip ??= new OVRHapticsClip(new byte[5] { 128, 255, 255, 128, 255 }, 5);
            }
        }

        public Action Callback { get; set; }

        public override void OnPointerClick()
        {
            Callback?.Invoke();
        }

        protected override void OnHoverChanged()
        {
            base.OnHoverChanged();
            if (Hover)
            {
                PlayHaptics(HapticsClip);
            }
        }
    }
}

