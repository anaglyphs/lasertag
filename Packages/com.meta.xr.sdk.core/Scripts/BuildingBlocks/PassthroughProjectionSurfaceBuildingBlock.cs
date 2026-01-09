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

namespace Meta.XR.BuildingBlocks
{
    /// <summary>
    /// Projects passthrough over a surface when using the Surface Projected Passthrough Building Block.
    /// </summary>
    /// <remarks>
    /// The surface geometries provided by the app should match real-world surfaces as closely as possible.
    /// If they differ significantly, users will receive conflicting depth cues and objects may appear too small or large.
    ///
    /// The Passthrough API enables you to show the user's real environment in your mixed reality experiences.
    /// It offers several options to customize the appearance of passthrough, such as adjusting opacity, highlight salient edges in the image, or control the color reproduction.
    /// For passthrough to be visible, it must be enabled in <see cref="OVRManager"/>
    /// via the <see cref="OVRManager.isInsightPassthroughEnabled"/> field.
    ///
    /// Find out more about [passthrough and its features](https://developer.oculus.com/documentation/unity/unity-passthrough/)
    /// or follow along with these [tutorials](https://developer.oculus.com/documentation/unity/unity-passthrough-tutorial).
    /// </remarks>
    public class PassthroughProjectionSurfaceBuildingBlock : MonoBehaviour
    {
        /// <summary>
        /// A required MeshFilter field that will be used to project Passthrough onto.
        /// </summary>
        public MeshFilter projectionObject;

        // Start is called before the first frame update
        private void Start()
        {
            var ptLayers = FindObjectsByType<OVRPassthroughLayer>(FindObjectsSortMode.None);
            var foundLayer = false;

            foreach (var ptLayer in ptLayers)
            {
                if (!ptLayer.GetComponent<BuildingBlock>())
                {
                    continue;
                }

                foundLayer = true;
                ptLayer.AddSurfaceGeometry(projectionObject.gameObject, true);
            }

            if (foundLayer)
            {
                // The MeshRenderer component renders the quad as a blue outline
                // we only use this when Passthrough isn't visible
                var quadOutline = projectionObject.GetComponent<MeshRenderer>();
                quadOutline.enabled = false;
            }
            else
            {
                throw new InvalidOperationException("A Building Block with the passthrough overlay layer was not found");
            }
        }
    }
}
