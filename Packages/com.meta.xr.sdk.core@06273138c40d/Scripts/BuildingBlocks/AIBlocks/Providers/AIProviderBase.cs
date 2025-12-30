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

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <remarks>
    /// Your project already uses InferenceType as a bitmask in editor code
    /// (see AvailableInferenceTypesForBlockId bitwise checks), so we rely on that.
    /// If your enum is missing the [Flags] attribute, add it there (values stay the same).
    /// </remarks>

    [Flags]
    public enum InferenceType
    {
        None = 0,
        Cloud = 1 << 0, // 1
        LocalServer = 1 << 1, // 2
        OnDevice = 1 << 2, // 4
    }

    public abstract class AIProviderBase : ScriptableObject
    {
        [SerializeField] private InferenceType supportedInferenceTypes = InferenceType.None;

        protected abstract InferenceType DefaultSupportedTypes { get; }

        public InferenceType SupportedInferenceTypes => supportedInferenceTypes;
        public bool SupportsAny(InferenceType mask) => (supportedInferenceTypes & mask) != 0;
        public bool SupportsAll(InferenceType mask) => (supportedInferenceTypes & mask) == mask;

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (supportedInferenceTypes != InferenceType.None || DefaultSupportedTypes == InferenceType.None)
            {
                return;
            }

            supportedInferenceTypes = DefaultSupportedTypes;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
