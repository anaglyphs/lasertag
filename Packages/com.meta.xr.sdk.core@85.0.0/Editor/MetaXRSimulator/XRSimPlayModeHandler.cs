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

using UnityEditor;

namespace Meta.XR.Simulator.Editor
{
    /// <summary>
    /// Handles play mode state changes for Meta XR Simulator 2.0 integration.
    /// Automatically starts XRSim 2.0 when entering play mode if the simulator is activated,
    /// replicating the behavior of the old XR simulator.
    /// </summary>
    [InitializeOnLoad]
    internal static class XRSimPlayModeHandler
    {
        private static XRSimUtils _xrSimUtils;

        static XRSimPlayModeHandler()
        {
            _xrSimUtils = new XRSimUtils();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                // Verify activation state when entering play mode
                // This ensures environment variables are properly set
                _xrSimUtils.VerifyAndCorrectActivation();
            }
        }
    }
}
