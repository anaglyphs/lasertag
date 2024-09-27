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
using System.Collections.Generic;
using UnityEngine;

namespace Meta.XR.ImmersiveDebugger
{
    public enum DebugColor
    {
        Red,
        Gray
    }

    /// <summary>
    /// Annotate field, property, functions with this will show in Immersive Debugger panel in runtime.
    /// Without additional parameters specified, by default we're watching fields/properties,
    /// and provide a button to call function without parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    [Serializable]
    public class DebugMember : Attribute
    {
        private static readonly Dictionary<DebugColor, Color> ParsedColors = new Dictionary<DebugColor, Color>()
        {
            { DebugColor.Red, UnityEngine.Color.red },
            { DebugColor.Gray, UnityEngine.Color.gray }
        };

        /// <summary>
        /// Draw gizmo in space according to the runtime value of the field/property data.
        /// </summary>
        public DebugGizmoType GizmoType = DebugGizmoType.None;
        /// <summary>
        /// The color used for DebugGizmo line drawing and inspector row pill icon
        /// </summary>
        public Color Color = UnityEngine.Color.gray;
        /// <summary>
        /// Specify whether this field/property is tweakable, will show control UI in panel.
        /// For now only supports float and use together with Min, Max param.
        /// </summary>
        public bool Tweakable = false;
        /// <summary>
        /// Minimum value for the tweak slider
        /// </summary>
        public float Min = 0.0f;
        /// <summary>
        /// Maximum value for the tweak slider
        /// </summary>
        public float Max = 1.0f;

        /// <summary>
        /// Optional category for a specific tab in Inspector Panel
        /// </summary>
        public string Category = null;

        public DebugMember(DebugColor color = DebugColor.Gray)
        {
            ParsedColors.TryGetValue(color, out Color);
        }
    }
}

