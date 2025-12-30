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

namespace Meta.XR.ImmersiveDebugger.UserInterface
{
    /// <summary>
    /// Interface for registering debug panels dynamically with the DebugInterface.
    /// Implement this interface to create panels that can register themselves at runtime.
    /// </summary>
    public interface IPanelRegistrar
    {
        /// <summary>
        /// Register a debug panel with the debug interface.
        /// This method will be called automatically when the debug interface is ready.
        /// </summary>
        /// <param name="debugInterface">The debug interface to register the panel with</param>
        void RegisterPanel(DebugInterface debugInterface);
    }
}
