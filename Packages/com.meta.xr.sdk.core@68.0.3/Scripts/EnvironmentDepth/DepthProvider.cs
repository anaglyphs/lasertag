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

#if DEPTH_API_SUPPORTED
using Unity.XR.Oculus;

namespace Meta.XR.EnvironmentDepth
{
    internal class DepthProvider : IDepthProvider
    {
        public bool IsSupported => Utils.GetEnvironmentDepthSupported();
        public bool RemoveHands { set => Utils.SetEnvironmentDepthHandRemoval(value); }

        public bool GetDepthTextureId(ref uint textureId) => Utils.GetEnvironmentDepthTextureId(ref textureId);

        public void SetDepthEnabled(bool isEnabled, bool removeHands)
        {
            if (isEnabled)
            {
                Utils.SetupEnvironmentDepth(new Utils.EnvironmentDepthCreateParams { removeHands = removeHands });
                Utils.SetEnvironmentDepthRendering(true);
            }
            else
            {
                Utils.ShutdownEnvironmentDepth();
                Utils.SetEnvironmentDepthRendering(false);
            }
        }

        public DepthFrameDesc GetFrameDesc(int eye)
        {
            var d = Utils.GetEnvironmentDepthFrameDesc(eye);
            return new DepthFrameDesc
            {
                createPoseLocation = d.createPoseLocation,
                createPoseRotation = d.createPoseRotation,
                fovLeftAngle = d.fovLeftAngle,
                fovRightAngle = d.fovRightAngle,
                fovTopAngle = d.fovTopAngle,
                fovDownAngle = d.fovDownAngle,
                nearZ = d.nearZ,
                farZ = d.farZ
            };
        }
    }
}
#endif // DEPTH_API_SUPPORTED
