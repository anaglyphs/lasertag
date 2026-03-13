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

using UnityEngine;

namespace Meta.XR.ImmersiveDebugger.UserInterface.Generic
{
    /// <summary>
    /// Specialized overlay canvas for the immersive debugger UI that extends OVROverlayCanvas.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    public class OverlayCanvas : OVROverlayCanvas
    {
        /// <summary>
        /// Gets or sets whether imposter rendering should be enabled.
        /// True in editor (no os-level overlay),
        /// False in builds (as we'll rely on the actual overlay))
        /// </summary>
        public static bool ShouldRenderImposters =
#if UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>
        /// Gets whether this canvas should be rendered with priority.
        /// Returns the inverse of ShouldRenderImposters.
        /// In editor, we don't need to render with priority as the overlay is not active anyway.
        /// In builds, we always render with priority.
        /// </summary>
        public override bool IsCanvasPriority => !ShouldRenderImposters;
    }
}
