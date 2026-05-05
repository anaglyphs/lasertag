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
using Unity.Collections;
using System.Buffers;
using UnityEngine;
using UnityEngine.UI;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public sealed class ImageSegmentationVisualizer : MonoBehaviour
    {
#if MRUK_INSTALLED
        [Tooltip("Prefab used to render each 3D bounding box.")]
        [SerializeField] private GameObject boundingBoxPrefab;

        [Tooltip("Enable or disable bounding box visualization")]
        [SerializeField] private bool showBoundingBoxes = true;

        [Tooltip("Enable or disable label visualization")]
        [SerializeField] private bool showLabels = true;

        [Tooltip("Scale factor for text labels relative to bounding box size")]
        [Range(0f, 1f)]
        [SerializeField] private float labelScale = 0.5f;

        private ImageSegmentationAgent _agent;
        private PassthroughCameraAccess _cam;
        private DepthTextureAccess _depth;
        private Matrix4x4[] _vpBuf;
        private float[] _depthBuf;
        private int _eyeIdx;

        private readonly List<GameObject> _live = new();
        private readonly Queue<GameObject> _pool = new();
        private readonly List<Vector3> _worldPointsCache = new();
        private readonly List<float> _depthsCache = new();
        private readonly Dictionary<GameObject, (Renderer renderer, Text label)> _componentCache = new();

        private struct FrameData
        {
            public Pose Pose;
            public float[] Depth;
            public Matrix4x4[] ViewProjectionMatrix;
        }

        private FrameData _frame;


        private void Awake()
        {
            _agent = GetComponent<ImageSegmentationAgent>();
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
            if (_cam == null)
            {
                Debug.LogError("[ImageSegmentationVisualizer] PassthroughCameraAccess not found");
                enabled = false;
                return;
            }
            _depth = GetComponent<DepthTextureAccess>();
            _eyeIdx = _cam.CameraPosition == PassthroughCameraAccess.CameraPositionType.Left ? 0 : 1;
        }

        private void OnEnable()
        {
            _agent.OnSegmentationUpdated += Draw3D;
            _depth.OnDepthTextureUpdateCPU += OnDepth;
        }

        private void OnDisable()
        {
            _agent.OnSegmentationUpdated -= Draw3D;
            _depth.OnDepthTextureUpdateCPU -= OnDepth;
            ReturnBuffers();
        }


        private void ReturnBuffers()
        {
            if (_depthBuf != null)
            {
                ArrayPool<float>.Shared.Return(_depthBuf, clearArray: true);
                _depthBuf = null;
            }

            if (_vpBuf == null)
            {
                return;
            }

            ArrayPool<Matrix4x4>.Shared.Return(_vpBuf, clearArray: true);
            _vpBuf = null;
        }

        private void OnDepth(DepthTextureAccess.DepthFrameData d)
        {
            _frame.Pose = d.CameraPose;

            if (_depthBuf == null || _depthBuf.Length < d.DepthTexturePixels.Length)
            {
                if (_depthBuf != null)
                {
                    ArrayPool<float>.Shared.Return(_depthBuf);
                }

                _depthBuf = ArrayPool<float>.Shared.Rent(d.DepthTexturePixels.Length);
            }

            if (_vpBuf == null || _vpBuf.Length < d.ViewProjectionMatrix.Length)
            {
                if (_vpBuf != null)
                {
                    ArrayPool<Matrix4x4>.Shared.Return(_vpBuf);
                }

                _vpBuf = ArrayPool<Matrix4x4>.Shared.Rent(d.ViewProjectionMatrix.Length);
            }

            NativeArray<float>.Copy(d.DepthTexturePixels, _depthBuf, d.DepthTexturePixels.Length);
            System.Array.Copy(d.ViewProjectionMatrix, _vpBuf, d.ViewProjectionMatrix.Length);

            _frame.Depth = _depthBuf;
            _frame.ViewProjectionMatrix = _vpBuf;
        }

        public void Draw3D(SegmentationResult result)
        {
            if (!showBoundingBoxes && !showLabels)
            {
                ClearAll();
                return;
            }

            if (result is not { numObjects: > 0 } || _frame.Depth == null)
            {
                ClearAll();
                return;
            }

            foreach (var g in _live)
            {
                g.SetActive(false);
                _pool.Enqueue(g);
            }

            _live.Clear();

            if (!boundingBoxPrefab)
            {
                Debug.LogWarning("[ImageSegmentation3DVisualizer] No boundingBoxPrefab assigned.");
                return;
            }

            var cameraTexture = _cam.GetTexture();
            if (!cameraTexture)
            {
                Debug.LogWarning("[ImageSegmentation3DVisualizer] Camera texture is null");
                return;
            }

            for (var i = 0; i < result.numObjects; i++)
            {
                var classId = result.classIds[i];
                var o = i * 4;
                var cxNorm = result.boxes[o + 0];
                var cyNorm = result.boxes[o + 1];
                var wNorm = result.boxes[o + 2];
                var hNorm = result.boxes[o + 3];

                var label = "Unknown";
                if (result.labels != null && classId < result.labels.Length)
                {
                    label = result.labels[classId];
                }

                var centerX = cxNorm * cameraTexture.width;
                var centerY = cyNorm * cameraTexture.height;
                var width = wNorm * cameraTexture.width;
                var height = hNorm * cameraTexture.height;

                var xmin = centerX - width * 0.5f;
                var ymin = centerY - height * 0.5f;
                var xmax = centerX + width * 0.5f;
                var ymax = centerY + height * 0.5f;

                if (!TryProject(cameraTexture, result, i, xmin, ymin, xmax, ymax, label, out var pos, out var rot,
                        out var scl))
                {
                    Debug.LogWarning($"[ImageSegmentation3DVisualizer] Failed to project object {i} ({label})");
                    continue;
                }

                var box = _pool.Count > 0 ? _pool.Dequeue() : Instantiate(boundingBoxPrefab);
                box.SetActive(true);
                box.transform.SetPositionAndRotation(pos, rot);
                box.transform.localScale = scl;
                _live.Add(box);

                if (!_componentCache.TryGetValue(box, out var cached))
                {
                    cached = (box.GetComponent<Renderer>(), box.GetComponentInChildren<Text>());
                    _componentCache[box] = cached;
                }

                if (cached.renderer)
                {
                    cached.renderer.enabled = showBoundingBoxes;
                }

                if (!cached.label) continue;
                cached.label.enabled = showLabels;

                var score = result.scores != null && i < result.scores.Length ? result.scores[i] : 0f;
                cached.label.text = $"{label} {score:0.00}";

                var avgScale = (scl.x + scl.y + scl.z) / 3f;
                var uniformScale = avgScale * labelScale;

                cached.label.transform.localScale = new Vector3(
                    uniformScale / Mathf.Max(scl.x, 0.001f),
                    uniformScale / Mathf.Max(scl.y, 0.001f),
                    uniformScale / Mathf.Max(scl.z, 0.001f)
                );
            }
        }

        private bool TryProject(Texture cameraTexture, SegmentationResult result, int objectIndex, float xmin,
            float ymin, float xmax, float ymax, string label,
            out Vector3 world, out Quaternion rot, out Vector3 scale)
        {
            world = default;
            rot = default;
            scale = default;

            var maskPixelsPerObject = result.maskWidth * result.maskHeight;
            var maskOffset = objectIndex * maskPixelsPerObject;

            _worldPointsCache.Clear();
            const int gridSize = 10;
            var stepX = (xmax - xmin) / (gridSize - 1);
            var stepY = (ymax - ymin) / (gridSize - 1);

            for (var i = 0; i < gridSize; i++)
            {
                for (var j = 0; j < gridSize; j++)
                {
                    var px = xmin + i * stepX;
                    var py = ymin + j * stepY;

                    var maskX = (int)((px / cameraTexture.width) * result.maskWidth);
                    var maskY = (int)((py / cameraTexture.height) * result.maskHeight);

                    maskX = Mathf.Clamp(maskX, 0, result.maskWidth - 1);
                    maskY = Mathf.Clamp(maskY, 0, result.maskHeight - 1);

                    var maskIdx = maskOffset + maskY * result.maskWidth + maskX;
                    if (maskIdx >= result.masks.Length || maskIdx < 0)
                    {
                        continue;
                    }

                    var maskValue = result.masks[maskIdx];
                    var isObjectPixel = result.maskAreLogits ? maskValue > 0.0f : maskValue > 0.5f;

                    if (!isObjectPixel)
                    {
                        continue;
                    }

                    var normalizedX = px / cameraTexture.width;
                    var normalizedY = py / cameraTexture.height;

                    var ray = _cam.ViewportPointToRay(new Vector2(normalizedX, 1.0f - normalizedY), _frame.Pose);

                    var world1M = ray.origin + ray.direction;
                    var clip = _frame.ViewProjectionMatrix[_eyeIdx] * new Vector4(world1M.x, world1M.y, world1M.z, 1f);
                    if (clip.w <= 0)
                    {
                        continue;
                    }

                    var uv = (new Vector2(clip.x, clip.y) / clip.w) * 0.5f + Vector2.one * 0.5f;

                    if (!_depth || !_depth.IsInitialized)
                    {
                        continue;
                    }

                    var texSize = _depth.TextureSize;
                    var sx = Mathf.Clamp((int)(uv.x * texSize), 0, texSize - 1);
                    var sy = Mathf.Clamp((int)(uv.y * texSize), 0, texSize - 1);
                    var idx = _eyeIdx * texSize * texSize + sy * texSize + sx;
                    var depth = _frame.Depth[idx];

                    if (depth is <= 0 or > 20 || float.IsInfinity(depth))
                    {
                        continue;
                    }

                    var worldPoint = ray.origin + ray.direction * depth;
                    _worldPointsCache.Add(worldPoint);
                }
            }

            if (_worldPointsCache.Count < 3)
            {
                Debug.LogWarning($"[ImageSegmentation3DVisualizer] {label}: Not enough valid depth points ({_worldPointsCache.Count})");
                return false;
            }

            world = ComputeCentroid(_worldPointsCache);

            _depthsCache.Clear();
            foreach (var p in _worldPointsCache)
            {
                var toCam = p - _frame.Pose.position;
                _depthsCache.Add(toCam.magnitude);
            }

            _depthsCache.Sort();

            var medianDepth = _depthsCache[_depthsCache.Count / 2];
            var centerX = (xmin + xmax) * 0.5f;
            var centerY = (ymin + ymax) * 0.5f;
            var normalizedCenterX = centerX / cameraTexture.width;
            var normalizedCenterY = centerY / cameraTexture.height;
            var normalizedWidth = (xmax - xmin) / cameraTexture.width;
            var normalizedHeight = (ymax - ymin) / cameraTexture.height;

            var rayCenter = _cam.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - normalizedCenterY), _frame.Pose);
            var rayLeft = _cam.ViewportPointToRay(new Vector2(normalizedCenterX - normalizedWidth * 0.5f, 1.0f - normalizedCenterY), _frame.Pose);
            var rayRight = _cam.ViewportPointToRay(new Vector2(normalizedCenterX + normalizedWidth * 0.5f, 1.0f - normalizedCenterY), _frame.Pose);
            var rayTop = _cam.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - (normalizedCenterY - normalizedHeight * 0.5f)), _frame.Pose);
            var rayBottom = _cam.ViewportPointToRay(new Vector2(normalizedCenterX, 1.0f - (normalizedCenterY + normalizedHeight * 0.5f)), _frame.Pose);

            var worldCenter = rayCenter.origin + rayCenter.direction * medianDepth;
            var worldLeft = rayLeft.origin + rayLeft.direction * medianDepth;
            var worldRight = rayRight.origin + rayRight.direction * medianDepth;
            var worldTop = rayTop.origin + rayTop.direction * medianDepth;
            var worldBottom = rayBottom.origin + rayBottom.direction * medianDepth;

            var width = Vector3.Distance(worldLeft, worldRight);
            var height = Vector3.Distance(worldTop, worldBottom);

            var viewDir = (worldCenter - _frame.Pose.position).normalized;
            var minDepthProj = float.MaxValue;
            var maxDepthProj = float.MinValue;

            foreach (var p in _worldPointsCache)
            {
                var proj = Vector3.Dot(p - world, viewDir);
                minDepthProj = Mathf.Min(minDepthProj, proj);
                maxDepthProj = Mathf.Max(maxDepthProj, proj);
            }

            var depthExtent = Mathf.Clamp(maxDepthProj - minDepthProj, 0.02f, 0.5f);

            var toObject = (world - _frame.Pose.position).normalized;
            var forward = new Vector3(toObject.x, 0, toObject.z).normalized;
            if (forward.magnitude < 0.1f)
            {
                forward = Vector3.forward;
            }

            rot = Quaternion.LookRotation(forward, Vector3.up);

            scale = new Vector3(
                Mathf.Clamp(width, 0.02f, 2.0f),
                Mathf.Clamp(height, 0.02f, 2.0f),
                depthExtent
            );

            return true;
        }


        private static Vector3 ComputeCentroid(List<Vector3> points)
        {
            var centroid = Vector3.zero;
            foreach (var p in points)
            {
                centroid += p;
            }
            return centroid / points.Count;
        }

        private void ClearAll()
        {
            foreach (var g in _live)
            {
                if (g)
                {
                    g.SetActive(false);
                    _pool.Enqueue(g);
                }
            }
            _live.Clear();
        }
#endif
    }
}
