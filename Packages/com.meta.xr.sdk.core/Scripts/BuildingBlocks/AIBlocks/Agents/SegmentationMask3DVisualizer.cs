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

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public sealed class SegmentationMask3DVisualizer : MonoBehaviour
    {
#if MRUK_INSTALLED
        [Tooltip("Enable or disable 3D mask visualization")]
        [SerializeField] private bool showMasks = true;

        [Tooltip("Ray cache grid resolution (lower = faster, higher = more accurate)")]
        [Range(8, 32)]
        [SerializeField] private int rayCacheSize = 16;

        [Tooltip("Mesh resolution quality (higher = more detailed, lower = faster)")]
        [Range(10, 200)]
        [SerializeField] private int meshQuality = 100;

        [Tooltip("Maximum vertices per mesh (Quest performance limit)")]
        [Range(1000, 10000)]
        [SerializeField] private int maxVertices = 10000;

        [Tooltip("Minimum confidence threshold for mask pixels")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float minConfidence = 0.5f;

        [Tooltip("Colors for different detected objects")]
        [SerializeField] private Color[] colors =
        {
            new(0f, 1f, 0.5f, 0.75f),
            new(1f, 0.5f, 0f, 0.75f),
            new(0.5f, 0f, 1f, 0.75f),
        };

        [Tooltip("Maximum edge length for triangles (prevents stretched triangles)")]
        [SerializeField] private float maxEdgeLength = 0.15f;

        [Tooltip("Maximum depth distance to consider valid (in meters)")]
        [SerializeField] private float maxDepthDistance = 20f;

        [Tooltip("Material used for rendering 3D segmentation masks. Should use an unlit shader with alpha blending and vertex color support.")]
        [SerializeField] private Material segmentationMaterial;

        private ImageSegmentationAgent _agent;
        private PassthroughCameraAccess _cam;
        private DepthTextureAccess _depth;
        private Material _material;
        private Matrix4x4[] _vpBuf;
        private Matrix4x4 _currentVpMatrix;
        private Pose _pose;

        private static readonly int ColorID = Shader.PropertyToID("_BaseColor");
        private float[] _depthBuf;
        private int[] _grid;
        private int _eyeIdx;

        private readonly List<MeshObject> _pool = new();
        private Vector3[] _verts;
        private Color[] _cols;
        private int[] _tris;
        private int _vertCount;
        private int _triCount;
        private Ray[] _rayCache;
        private Vector2[] _depthUVCache;
        private int _rayCacheResolution;
        private Pose _rayCachePose;
        private int _rayCacheFrameCount = -1;
        private readonly Dictionary<int, int> _classIdToColorIndex = new();

        private class MeshObject
        {
            public GameObject Go;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Props;
            public bool Active;
        }

        private void Awake()
        {
            _agent = GetComponent<ImageSegmentationAgent>();
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
            if (!_cam)
            {
                Debug.LogError("[SegmentationMask3DVisualizer] PassthroughCameraAccess not found in scene. Component disabled.");
                enabled = false;
                return;
            }

            _depth = GetComponent<DepthTextureAccess>();
            _eyeIdx = _cam.CameraPosition == PassthroughCameraAccess.CameraPositionType.Left ? 0 : 1;

            if (segmentationMaterial != null)
            {
                _material = new Material(segmentationMaterial);
            }
            else
            {
                Debug.LogWarning("[SegmentationMask3DVisualizer] No material assigned. Using fallback Unlit/Color shader. For best results, assign a material with an unlit shader that supports vertex colors and alpha blending.");
                var shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    _material = new Material(shader);
                }
                else
                {
                    Debug.LogError("[SegmentationMask3DVisualizer] Could not find fallback shader. Component disabled.");
                    enabled = false;
                }
            }
        }

        private void OnEnable()
        {
            _agent.OnSegmentationUpdated += OnSegmentation;
            _depth.OnDepthTextureUpdateCPU += OnDepth;
        }

        private void OnDisable()
        {
            _agent.OnSegmentationUpdated -= OnSegmentation;
            _depth.OnDepthTextureUpdateCPU -= OnDepth;

            if (_depthBuf != null)
            {
                ArrayPool<float>.Shared.Return(_depthBuf, clearArray: true);
                _depthBuf = null;
            }

            if (_vpBuf == null) return;
            ArrayPool<Matrix4x4>.Shared.Return(_vpBuf, clearArray: true);
            _vpBuf = null;
        }

        private void OnDestroy()
        {
            foreach (var m in _pool)
            {
                if (m.Mesh != null)
                {
                    Destroy(m.Mesh);
                }

                if (m.Go != null)
                {
                    Destroy(m.Go);
                }
            }

            if (_material != null)
            {
                Destroy(_material);
            }
        }

        private void OnDepth(DepthTextureAccess.DepthFrameData d)
        {
            _pose = d.CameraPose;
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
            _currentVpMatrix = _vpBuf[_eyeIdx];
        }

        private void OnSegmentation(SegmentationResult result)
        {
            var shouldDisableAll = !showMasks || result.numObjects == 0 || _depthBuf == null || _vpBuf == null;

            if (shouldDisableAll)
            {
                foreach (var m in _pool)
                {
                    m.Go.SetActive(false);
                }
                return;
            }

            var tex = _cam.GetTexture();
            BuildRayCache();

            foreach (var m in _pool) m.Active = false;

            for (var i = 0; i < result.numObjects; i++)
            {
                CreateMesh(result, i, tex);
            }

            foreach (var m in _pool)
            {
                if (!m.Active)
                {
                    m.Go.SetActive(false);
                }
            }
        }

        private MeshObject GetMesh()
        {
            foreach (var m in _pool)
            {
                if (m.Active)
                {
                    continue;
                }

                m.Active = true;
                m.Go.SetActive(true);
                return m;
            }

            var mesh = new Mesh { name = $"Seg{_pool.Count}" };
            mesh.MarkDynamic();

            var go = new GameObject($"Seg{_pool.Count}");
            go.transform.SetParent(transform);

            var filter = go.AddComponent<MeshFilter>();
            filter.mesh = mesh;

            var meshRend = go.AddComponent<MeshRenderer>();
            meshRend.sharedMaterial = _material;

            var obj = new MeshObject
            {
                Go = go,
                Mesh = mesh,
                Renderer = meshRend,
                Props = new MaterialPropertyBlock(),
                Active = true
            };

            _pool.Add(obj);
            return obj;
        }

        private void CreateMesh(SegmentationResult result, int objIdx, Texture tex)
        {
            var maskOff = objIdx * result.maskWidth * result.maskHeight;
            var texSize = _depth.TextureSize;

            if (result.boxes == null || result.boxes.Length < (objIdx + 1) * 4 ||
                result.masks == null || result.masks.Length < maskOff + result.maskWidth * result.maskHeight ||
                result.classIds == null || result.classIds.Length <= objIdx ||
                _depthBuf.Length < texSize * texSize * 2)
            {
                return;
            }

            var texWidth = tex.width;
            var texHeight = tex.height;

            var o = objIdx * 4;
            var cx = result.boxes[o + 0] * texWidth;
            var cy = result.boxes[o + 1] * texHeight;
            var w = result.boxes[o + 2] * texWidth;
            var h = result.boxes[o + 3] * texHeight;

            var xmin = cx - w * 0.5f;
            var ymin = cy - h * 0.5f;
            var xmax = cx + w * 0.5f;
            var ymax = cy + h * 0.5f;

            var classId = result.classIds[objIdx];
            if (!_classIdToColorIndex.TryGetValue(classId, out var colorIndex))
            {
                colorIndex = _classIdToColorIndex.Count;
                _classIdToColorIndex[classId] = colorIndex;

                if (colorIndex >= colors.Length)
                {
                    System.Array.Resize(ref colors, colors.Length + 1);
                    colors[colorIndex] = new Color(
                        Random.Range(0f, 1f),
                        Random.Range(0f, 1f),
                        Random.Range(0f, 1f),
                        0.75f
                    );
                }
            }

            var color = colors[colorIndex];
            var density = meshQuality;
            var gridSize = density * density;

            if (gridSize > maxVertices)
            {
                density = (int)Mathf.Sqrt(maxVertices);
                gridSize = density * density;
            }

            if (density <= 1) return;

            var stepX = (xmax - xmin) / (density - 1);
            var stepY = (ymax - ymin) / (density - 1);

            if (_grid == null || _grid.Length < gridSize)
            {
                var newSize = Mathf.NextPowerOfTwo(gridSize);
                _grid = new int[newSize];
            }
            if (_verts == null || _verts.Length < gridSize)
            {
                var newSize = Mathf.NextPowerOfTwo(gridSize);
                _verts = new Vector3[newSize];
                _cols = new Color[newSize];
            }
            for (var i = 0; i < gridSize; i++) _grid[i] = -1;
            _vertCount = 0;

            for (var i = 0; i < density; i++)
            {
                for (var j = 0; j < density; j++)
                {
                    var px = xmin + i * stepX;
                    var py = ymin + j * stepY;

                    var maskX = Mathf.Clamp((int)((px / texWidth) * result.maskWidth), 0, result.maskWidth - 1);
                    var maskY = Mathf.Clamp((int)((py / texHeight) * result.maskHeight), 0, result.maskHeight - 1);
                    var maskIdx = maskOff + maskY * result.maskWidth + maskX;
                    if (maskIdx >= result.masks.Length) continue;

                    var maskVal = result.masks[maskIdx];
                    if (!(result.maskAreLogits ? maskVal > 0f : maskVal > minConfidence)) continue;

                    var nx = px / texWidth;
                    var ny = py / texHeight;
                    var ray = GetInterpolatedRay(nx, 1f - ny);
                    var uv = GetInterpolatedDepthUV(nx, 1f - ny);

                    if (uv.x < 0f) continue;

                    var sx = (int)(uv.x * texSize);
                    var sy = (int)(uv.y * texSize);
                    if (sx < 0 || sx >= texSize || sy < 0 || sy >= texSize) continue;
                    var idx = _eyeIdx * texSize * texSize + sy * texSize + sx;
                    if (idx >= _depthBuf.Length)
                    {
                        continue;
                    }
                    var depth = _depthBuf[idx];
                    if (depth <= 0 || depth > maxDepthDistance || float.IsInfinity(depth))
                    {
                        continue;
                    }

                    _grid[j * density + i] = _vertCount;
                    _verts[_vertCount] = ray.origin + ray.direction * depth;
                    _cols[_vertCount] = color;
                    _vertCount++;
                }
            }

            if (_vertCount < 3)
            {
                return;
            }

            if (_tris == null || _tris.Length < gridSize * 6)
            {
                _tris = new int[gridSize * 6];
            }

            _triCount = 0;
            for (var y = 0; y < density - 1; y++)
            {
                for (var x = 0; x < density - 1; x++)
                {
                    var v00 = _grid[y * density + x];
                    var v10 = _grid[y * density + x + 1];
                    var v01 = _grid[(y + 1) * density + x];
                    var v11 = _grid[(y + 1) * density + x + 1];

                    if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0)
                    {
                        continue;
                    }

                    var p00 = _verts[v00];
                    var p10 = _verts[v10];
                    var p01 = _verts[v01];
                    var p11 = _verts[v11];

                    var d00 = (p00 - _pose.position).magnitude;
                    var d10 = (p10 - _pose.position).magnitude;
                    var d01 = (p01 - _pose.position).magnitude;
                    var d11 = (p11 - _pose.position).magnitude;

                    var avgDepth = (d00 + d10 + d01 + d11) * 0.25f;
                    var depthFactor = Mathf.Clamp01(avgDepth);
                    var adaptiveMaxEdge = maxEdgeLength * (1f + depthFactor);
                    var maxEdgeSq = adaptiveMaxEdge * adaptiveMaxEdge;

                    var e0010 = p10 - p00;
                    var e0001 = p01 - p00;
                    var e1011 = p11 - p10;
                    var e0111 = p11 - p01;

                    if (e0010.sqrMagnitude > maxEdgeSq || e0001.sqrMagnitude > maxEdgeSq ||
                        e1011.sqrMagnitude > maxEdgeSq || e0111.sqrMagnitude > maxEdgeSq)
                    {
                        continue;
                    }

                    _tris[_triCount++] = v00;
                    _tris[_triCount++] = v01;
                    _tris[_triCount++] = v10;
                    _tris[_triCount++] = v10;
                    _tris[_triCount++] = v01;
                    _tris[_triCount++] = v11;
                }
            }

            if (_triCount == 0)
            {
                return;
            }

            var m = GetMesh();
            m.Mesh.Clear();
            m.Mesh.SetVertices(_verts, 0, _vertCount);
            m.Mesh.SetColors(_cols, 0, _vertCount);
            m.Mesh.SetTriangles(_tris, 0, _triCount, 0);
            m.Mesh.RecalculateBounds();
            m.Props.SetColor(ColorID, color);
            m.Renderer.SetPropertyBlock(m.Props);
        }

        private void BuildRayCache()
        {
            if (_rayCacheFrameCount == Time.frameCount && _rayCachePose.Equals(_pose))
            {
                return;
            }

            var cacheSize = rayCacheSize;
            var cacheCount = cacheSize * cacheSize;

            if (_rayCache == null || _rayCacheResolution != cacheSize)
            {
                _rayCache = new Ray[cacheCount];
                _depthUVCache = new Vector2[cacheCount];
                _rayCacheResolution = cacheSize;
            }

            var step = 1f / (cacheSize - 1);

            for (var y = 0; y < cacheSize; y++)
            {
                for (var x = 0; x < cacheSize; x++)
                {
                    var nx = x * step;
                    var ny = y * step;
                    var idx = y * cacheSize + x;
                    var ray = _cam.ViewportPointToRay(new Vector2(nx, ny), _pose);
                    _rayCache[idx] = ray;

                    var w1M = ray.origin + ray.direction;
                    var clip = _currentVpMatrix * new Vector4(w1M.x, w1M.y, w1M.z, 1f);
                    if (clip.w > 0)
                    {
                        _depthUVCache[idx] = (new Vector2(clip.x, clip.y) / clip.w) * 0.5f + Vector2.one * 0.5f;
                    }
                    else
                    {
                        _depthUVCache[idx] = new Vector2(-1f, -1f);
                    }
                }
            }

            _rayCacheFrameCount = Time.frameCount;
            _rayCachePose = _pose;
        }

        private void GetBilinearWeights(float u, float v, out int idx00, out int idx10, out int idx01, out int idx11, out float w00, out float w10, out float w01, out float w11)
        {
            var size = _rayCacheResolution;
            var sizeMax = size - 1;
            var x = u * sizeMax;
            var y = v * sizeMax;
            var x0 = Mathf.Min((int)x, sizeMax - 1);
            var y0 = Mathf.Min((int)y, sizeMax - 1);
            var x1 = x0 + 1;
            var y1 = y0 + 1;
            var fx = x - x0;
            var fy = y - y0;
            var ifx = 1f - fx;
            var ify = 1f - fy;

            idx00 = y0 * size + x0;
            idx10 = y0 * size + x1;
            idx01 = y1 * size + x0;
            idx11 = y1 * size + x1;

            w00 = ifx * ify;
            w10 = fx * ify;
            w01 = ifx * fy;
            w11 = fx * fy;
        }

        private Ray GetInterpolatedRay(float u, float v)
        {
            GetBilinearWeights(u, v, out var idx00, out var idx10, out var idx01, out var idx11, out var w00, out var w10, out var w01, out var w11);

            var r00 = _rayCache[idx00];
            var r10 = _rayCache[idx10];
            var r01 = _rayCache[idx01];
            var r11 = _rayCache[idx11];

            var origin = r00.origin * w00 + r10.origin * w10 + r01.origin * w01 + r11.origin * w11;
            var direction = r00.direction * w00 + r10.direction * w10 + r01.direction * w01 + r11.direction * w11;
            direction.Normalize();

            return new Ray(origin, direction);
        }

        private Vector2 GetInterpolatedDepthUV(float u, float v)
        {
            GetBilinearWeights(u, v, out var idx00, out var idx10, out var idx01, out var idx11, out var w00, out var w10, out var w01, out var w11);
            return _depthUVCache[idx00] * w00 + _depthUVCache[idx10] * w10 + _depthUVCache[idx01] * w01 + _depthUVCache[idx11] * w11;
        }
#endif
    }
}
