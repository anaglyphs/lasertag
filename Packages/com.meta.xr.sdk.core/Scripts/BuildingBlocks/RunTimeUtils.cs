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

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Static container class of utility methods used at runtime and related to <see cref="BuildingBlock"/>s.
    /// </summary>
    public static class RunTimeUtils
    {
        /// <summary>
        /// Returns the first (order not guaranteed) instance of a <see cref="MonoBehaviour"/> implementing the
        /// interface <see cref="T"/>.
        /// </summary>
        /// <param name="monoBehaviour"><see cref="MonoBehaviour"/> instance, as caller, on whose
        /// <see cref="GameObject"/> the search will occur.</param>
        /// <typeparam name="T">Interface type expected</typeparam>
        /// <returns>The first instance of <see cref="T"/> found.</returns>
        public static T GetInterfaceComponent<T>(this MonoBehaviour monoBehaviour) where T : class
        {
            foreach (var component in monoBehaviour.GetComponents<MonoBehaviour>())
            {
                if (component is T interfaceComponent)
                {
                    return interfaceComponent;
                }
            }
            return null;
        }
    }
}
