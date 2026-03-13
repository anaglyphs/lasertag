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

using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.BuildingBlocks.AIBlocks
{
    internal class ObjectDetectionInstallationRoutine : AIBlocksInstallationRoutine
    {
        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            Description = "Whether object detection visualizer, which draws the bounding boxes, should also be included.",
            Default = true,
            Order = 100
        )]
        public bool includeVisualization;

        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            Description = "Whether the Passthrough Building Block should also be installed.",
            Default = true,
            Condition = nameof(CanInstallPassthrough),
            Order = 110
        )]
        public bool includePassthrough;

        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            Description = "Whether debugging tools should also be included.",
            Default = false,
            Order = 120
        )]
        public bool includeDebuggingTools;

        private bool CanInstallPassthrough()
        {
            var isPassthroughPresentInScene = FindAnyObjectByType<OVRPassthroughLayer>() != null;
            return !isPassthroughPresentInScene;
        }

        internal override IEnumerable<BlockData> ComputeOptionalDependencies()
        {
            if (includePassthrough)
            {
                yield return Meta.XR.BuildingBlocks.Editor.Utils.GetBlockData(BlockDataIds.Passthrough);
            }
        }

        public override async Task<List<GameObject>> InstallAsync(BlockData block, GameObject selectedGameObject)
        {
            var installedObjects = await base.InstallAsync(block, selectedGameObject);

            if (!includeVisualization)
            {
                DisableVisualization(installedObjects);
            }

            if (!includeDebuggingTools)
            {
                DisableDebuggingTools(installedObjects);
            }

            return installedObjects;
        }

        private static void DisableVisualization(IEnumerable<GameObject> objects)
        {
            foreach (var @object in objects)
            {
                RemoveComponent<ObjectDetectionVisualizer>(@object);
                RemoveComponent<DepthTextureAccess>(@object);
                RemoveComponent<EnvironmentDepth.EnvironmentDepthManager>(@object);
                EditorUtility.SetDirty(@object);
            }
        }

        private static void DisableDebuggingTools(IEnumerable<GameObject> objects)
        {
            foreach (var @object in objects)
            {
                RemoveComponent<ImmersiveDebugger.DebugInspector>(@object);
                EditorUtility.SetDirty(@object);
            }
        }
    }
}
