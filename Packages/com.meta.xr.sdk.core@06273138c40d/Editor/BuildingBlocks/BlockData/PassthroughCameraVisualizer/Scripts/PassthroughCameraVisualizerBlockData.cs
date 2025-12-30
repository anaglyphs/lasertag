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
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.Editor
{
    public class PassthroughCameraVisualizerBlockData : BlockData
    {
#if USING_META_XR_MR_UTILITYKIT_SDK
        private static PassthroughCameraAccess _lastCreatedAccess;

        protected override async Task<List<GameObject>> InstallRoutineAsync(GameObject selectedGameObject)
        {
            if (HasMeshRenderer(selectedGameObject))
            {
                selectedGameObject.name = $"{Utils.BlockPublicTag} {BlockName}";
                await ConfigureAsync(selectedGameObject);
                return new List<GameObject> { selectedGameObject };
            }

            var visualizers = await base.InstallRoutineAsync(selectedGameObject);
            foreach (var t in visualizers)
            {
                await ConfigureAsync(t);
            }

            return visualizers;
        }

        private static bool HasMeshRenderer(GameObject go)
        {
            return go != null && go.GetComponent<MeshRenderer>() != null;
        }

        private static async Task ConfigureAsync(GameObject visualizer)
        {
            var meshRenderer = visualizer.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                Debug.LogError("Visualizer GameObject has no MeshRenderer. Cannot configure PassthroughCameraAccess.");
                return;
            }

            var sourceMaterial = meshRenderer.sharedMaterial;
            if (sourceMaterial == null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    Debug.LogError("Shader 'Unlit/Texture' not found. Cannot create new Passthrough material.");
                    return;
                }

                sourceMaterial = new Material(shader);
                meshRenderer.material = sourceMaterial;
            }

            var sourceTexture = sourceMaterial.mainTexture;
            var allCameraAccesses = FindObjectsByType<PassthroughCameraAccess>(FindObjectsSortMode.None);
            var allBlocks = Utils.UnfilteredRegistry.ToList();
            var visualizerBlocks = allBlocks.OfType<PassthroughCameraVisualizerBlockData>().ToList();
            var visualizerGameObjects = visualizerBlocks
                .Select(block => block.GetBlock())
                .Where(bb => bb != null)
                .Select(bb => bb.gameObject).ToList();
            var allMeshRenderers = visualizerGameObjects.SelectMany(go => go.GetComponentsInChildren<MeshRenderer>())
                .ToList();

            var used = new List<PassthroughCameraAccess>();
            var unused = new List<PassthroughCameraAccess>();

            foreach (var access in allCameraAccesses)
            {
                var mat = access.TargetMaterial;

                if (mat == null)
                {
                    unused.Add(access);
                    continue;
                }

                var inUse = false;
                foreach (var t in allMeshRenderers)
                {
                    if (t.sharedMaterial == mat)
                    {
                        inUse = true;
                        break;
                    }
                }

                (inUse ? used : unused).Add(access);
            }

            var isNew = false;
            PassthroughCameraAccess target;

            if (allCameraAccesses.Length == 0)
            {
                target = await CreateCameraAccessAsync(PassthroughCameraAccess.CameraPositionType.Left, sourceTexture);
                isNew = true;
            }
            else if (unused.Count > 0)
            {
                target = unused[0];
            }
            else if (allCameraAccesses.Length < 2)
            {
                var firstEye = allCameraAccesses[0].CameraPosition;
                var secondEye = firstEye == PassthroughCameraAccess.CameraPositionType.Left
                    ? PassthroughCameraAccess.CameraPositionType.Right
                    : PassthroughCameraAccess.CameraPositionType.Left;

                target = await CreateCameraAccessAsync(secondEye, sourceTexture);
                target.TargetMaterial = _lastCreatedAccess.TargetMaterial;

                isNew = true;
            }
            else
            {
                target = allCameraAccesses[0];
            }

            if (target == null)
            {
                Debug.LogError(
                    $"Failed to obtain or create a PassthroughCameraAccess for visualizer '{visualizer.name}'.");
                return;
            }

            if (isNew)
            {
                meshRenderer.material = target.TargetMaterial;
                return;
            }

            var existingMat = target.TargetMaterial;
            if (existingMat == null)
            {
                var shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    Debug.LogError("Shader 'Unlit/Texture' not found. Cannot create new Passthrough material.");
                    return;
                }

                existingMat = new Material(shader) { mainTexture = sourceTexture };
                target.TargetMaterial = existingMat;
            }

            meshRenderer.material = existingMat;
        }

        private static async Task<PassthroughCameraAccess> CreateCameraAccessAsync(
            PassthroughCameraAccess.CameraPositionType eye, Texture sourceTexture)
        {
            var blockData = Utils.GetBlockData(BlockDataIds.PassthroughCameraAccess);
            var imported = await blockData.InstallWithDependencies();
            if (imported == null || imported.Count == 0)
            {
                Debug.LogError("Failed to install PassthroughCameraAccess block.");
                return null;
            }

            var access = imported[0].GetComponentInChildren<PassthroughCameraAccess>();
            if (access == null)
            {
                Debug.LogError($"PassthroughCameraAccess component not found on imported object '{imported[0].name}'.");
                return null;
            }

            access.CameraPosition = eye;

            var shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogError("Shader 'Unlit/Texture' not found. Cannot create new Passthrough material.");
                return access;
            }

            access.TargetMaterial = new Material(shader) { mainTexture = sourceTexture };
            _lastCreatedAccess = access;
            return access;
        }
#endif // USING_META_XR_MR_UTILITYKIT_SDK
    }
}
