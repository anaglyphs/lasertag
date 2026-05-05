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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Roboflow provider – supports object detection and instance segmentation via cloud (serverless) or local inference server.
    /// Object Detection normalized shape:
    ///   [{ "score":float, "label":string, "box":[xmin,ymin,xmax,ymax] }, ...]
    /// Instance Segmentation returns masks derived from polygon points.
    /// </summary>
    /// <remarks>
    /// Roboflow provides both hosted (serverless) endpoints and a local inference server.
    /// Cloud: https://serverless.roboflow.com/{model_id}?api_key={key}
    /// Local: http://{server_ip}:{port}/infer/object_detection or /infer/instance_segmentation
    /// See: https://docs.roboflow.com/
    /// </remarks>
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/Roboflow Provider")]
    public sealed class RoboflowProvider : AIProviderBase, IUsesCredential, IObjectDetectionTask, IImageSegmentationTask
    {
        [ThreadStatic]
        private static System.Collections.Generic.List<float> _scanlineIntersections;
        /// <summary>
        /// Inference mode: Cloud (serverless) or LocalServer.
        /// </summary>
        public enum InferenceMode
        {
            Cloud,
            LocalServer
        }

        [Tooltip("Your Roboflow API key.")]
        [SerializeField] internal string apiKey;

        [Tooltip("If ON, use this asset's API key instead of CredentialStorage.")]
        [SerializeField] internal bool overrideApiKey;

        [Tooltip("Model ID (e.g., 'my-model/1' or 'workspace/project/version').")]
        [SerializeField] internal string modelId;

        [Tooltip("Inference mode: Cloud (serverless) or LocalServer.")]
        [SerializeField] internal InferenceMode inferenceMode = InferenceMode.Cloud;

        [Tooltip("Local server endpoint (e.g., 'http://192.168.1.100:9001'). Only used when InferenceMode is LocalServer.")]
        [SerializeField] internal string localServerEndpoint = "http://localhost:9001";

        [Tooltip("Cloud endpoint for serverless inference.")]
        [SerializeField] internal string cloudEndpoint = "https://serverless.roboflow.com";

        [Tooltip("Confidence threshold for detections (0-1).")]
        [SerializeField][Range(0f, 1f)] internal float confidenceThreshold = 0.5f;

        [Tooltip("Maximum number of detections to return.")]
        [SerializeField] internal int maxDetections = 100;

        [Tooltip("Width of the segmentation mask output in pixels. Higher values provide more detail but use more memory.")]
        [SerializeField] internal int segmentationMaskWidth = DefaultMaskSize;

        [Tooltip("Height of the segmentation mask output in pixels. Higher values provide more detail but use more memory.")]
        [SerializeField] internal int segmentationMaskHeight = DefaultMaskSize;

        private const int DefaultMaskSize = 160;
        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud | InferenceType.LocalServer;
        string IUsesCredential.ProviderId => "Roboflow";
        bool IUsesCredential.OverrideApiKey { get => overrideApiKey; set => overrideApiKey = value; }

        ProviderTestConfig IUsesCredential.GetTestConfig()
        {
            return new ProviderTestConfig
            {
                Endpoint = inferenceMode == InferenceMode.Cloud ? cloudEndpoint : localServerEndpoint,
                Model = modelId,
                ProviderId = ((IUsesCredential)this).ProviderId
            };
        }

        /// <summary>
        /// Runs object detection using Roboflow and returns structured detections as JSON.
        /// </summary>
        /// <param name="imageJpgOrPng">Encoded image data (JPEG or PNG).</param>
        /// <param name="ct">Cancellation token for HTTP.</param>
        /// <returns>JSON-formatted detection array: [{"score":float, "label":string, "box":[xmin,ymin,xmax,ymax]}]</returns>
        public async Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
        {
            ValidateConfiguration(apiKey, null, modelId);
            if (imageJpgOrPng == null || imageJpgOrPng.Length == 0)
            {
                throw new ArgumentException("Image data is null or empty.", nameof(imageJpgOrPng));
            }

            var base64Image = Convert.ToBase64String(imageJpgOrPng);

            string rawJson;
            if (inferenceMode == InferenceMode.Cloud)
            {
                rawJson = await InferCloudAsync(base64Image, ct);
            }
            else
            {
                rawJson = await InferLocalAsync(base64Image, ct);
            }

            return TransformDetections(rawJson);
        }

        /// <summary>
        /// Performs object detection on a RenderTexture by encoding it to JPEG and calling the byte[] overload.
        /// Uses async GPU readback to avoid blocking the main thread.
        /// </summary>
        /// <param name="src">Source RenderTexture to analyze.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        /// <returns>Array of predictions containing bounding boxes, scores, and class labels.</returns>
        public async Task<ObjectDetectionPrediction[]> DetectAsync(RenderTexture src, CancellationToken ct = default)
        {
            if (!src) throw new ArgumentNullException(nameof(src));

            // Use async GPU readback to avoid blocking main thread
            var jpg = await EncodeTextureToJpegAsync(src);
            if (jpg == null)
            {
                return Array.Empty<ObjectDetectionPrediction>();
            }

            var json = await DetectAsync(jpg, ct);

            if (string.IsNullOrEmpty(json))
            {
                return Array.Empty<ObjectDetectionPrediction>();
            }

            if (TryParseDetectionJson(json, out var predictions))
            {
                return predictions;
            }

            return Array.Empty<ObjectDetectionPrediction>();
        }

        private async Task<string> InferCloudAsync(string base64Image, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var endpoint = $"{cloudEndpoint.TrimEnd('/')}/{modelId}?api_key={Uri.EscapeDataString(apiKey)}";

            if (confidenceThreshold > 0)
            {
                endpoint += $"&confidence={confidenceThreshold}";
            }
            if (maxDetections > 0)
            {
                endpoint += $"&max_detections={maxDetections}";
            }

            var http = new HttpTransport(null);
            var body = Encoding.UTF8.GetBytes(base64Image);
            return await http.PostBinaryAsync(endpoint, body, "text/plain", null, ct);
        }

        private async Task<string> InferLocalAsync(string base64Image, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var endpoint = $"{localServerEndpoint.TrimEnd('/')}/infer/object_detection";
            var payload = BuildLocalRequestPayload(base64Image);
            var http = new HttpTransport(null);
            var result = await http.PostJsonAsync(endpoint, payload, null, ct);

            if (!string.IsNullOrEmpty(result)) return result;
            Debug.LogError($"[RoboflowProvider] Local inference returned null/empty. Endpoint: {endpoint}. " +
                           "Check that: 1) The Roboflow server is running, 2) The device can reach the server IP, " +
                           "3) Port 9001 is open in your firewall, 4) 'Allow downloads over HTTP' is enabled in Player Settings.");
            return null;

        }

        private string BuildLocalRequestPayload(string base64Image)
        {
            var sb = new StringBuilder(256);
            sb.Append("{");
            sb.Append("\"api_key\":\"").Append(EscapeJson(apiKey)).Append("\",");
            sb.Append("\"model_id\":\"").Append(EscapeJson(modelId)).Append("\",");
            sb.Append("\"image\":{\"type\":\"base64\",\"value\":\"").Append(base64Image).Append("\"}");

            if (confidenceThreshold > 0)
            {
                sb.Append(",\"confidence\":").Append(confidenceThreshold.ToString("0.###"));
            }
            if (maxDetections > 0)
            {
                sb.Append(",\"max_detections\":").Append(maxDetections);
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string TransformDetections(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson))
            {
                Debug.LogWarning("[RoboflowProvider] TransformDetections received null/empty rawJson.");
                return "[]";
            }

            try
            {
                var predictionsArray = ExtractPredictionsArray(rawJson);
                var wrapper = JsonUtility.FromJson<RoboflowResponseWrapper>("{\"predictions\":" + predictionsArray + "}");
                if (wrapper?.predictions == null || wrapper.predictions.Length == 0)
                {
                    Debug.LogWarning("[RoboflowProvider] TransformDetections: No predictions in parsed wrapper.");
                    return "[]";
                }

                var sb = new StringBuilder(wrapper.predictions.Length * 64);
                sb.Append('[');
                var first = true;

                foreach (var p in wrapper.predictions)
                {
                    var halfWidth = p.width * 0.5f;
                    var halfHeight = p.height * 0.5f;
                    var xmin = p.x - halfWidth;
                    var ymin = p.y - halfHeight;
                    var xmax = p.x + halfWidth;
                    var ymax = p.y + halfHeight;

                    if (!first) sb.Append(',');
                    first = false;

                    sb.Append("{\"score\":")
                      .Append(p.confidence.ToString("0.###"))
                      .Append(",\"label\":\"")
                      .Append(EscapeJson(p.@class ?? ""))
                      .Append("\",\"box\":[")
                      .Append(xmin.ToString("0.##")).Append(',')
                      .Append(ymin.ToString("0.##")).Append(',')
                      .Append(xmax.ToString("0.##")).Append(',')
                      .Append(ymax.ToString("0.##"))
                      .Append("]}");
                }

                sb.Append(']');
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoboflowProvider] Failed to parse response: {ex.Message}\nRaw JSON: {rawJson.Substring(0, Mathf.Min(300, rawJson.Length))}");
                return "[]";
            }
        }

        private static string ExtractPredictionsArray(string json)
        {
            var startIdx = json.IndexOf("\"predictions\"", StringComparison.Ordinal);
            if (startIdx < 0) return "[]";

            var colonIdx = json.IndexOf(':', startIdx);
            if (colonIdx < 0) return "[]";

            var bracketIdx = json.IndexOf('[', colonIdx);
            if (bracketIdx < 0) return "[]";

            var depth = 1;
            var endIdx = bracketIdx + 1;
            while (endIdx < json.Length && depth > 0)
            {
                if (json[endIdx] == '[') depth++;
                else if (json[endIdx] == ']') depth--;
                endIdx++;
            }

            return json.Substring(bracketIdx, endIdx - bracketIdx);
        }

        [Serializable]
        private class RoboflowPrediction
        {
            public float x;
            public float y;
            public float width;
            public float height;
            public float confidence;
            public string @class;
            public int class_id;
        }

        [Serializable]
        private class RoboflowResponseWrapper
        {
            public RoboflowPrediction[] predictions;
        }

        [Serializable]
        private class RoboflowPoint
        {
            public float x;
            public float y;
        }

        [Serializable]
        private class RoboflowSegmentationPrediction
        {
            public float x;
            public float y;
            public float width;
            public float height;
            public float confidence;
            public string @class;
            public int class_id;
            public RoboflowPoint[] points;
        }

        [Serializable]
        private class RoboflowSegmentationResponseWrapper
        {
            public RoboflowSegmentationPrediction[] predictions;
        }

        /// <summary>
        /// Performs instance segmentation on a RenderTexture using Roboflow.
        /// Roboflow returns polygon points which are converted to per-pixel masks.
        /// Uses async GPU readback to avoid blocking the main thread.
        /// </summary>
        /// <param name="src">Source RenderTexture to analyze.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        /// <returns>SegmentationResult containing masks, boxes, class IDs, and labels.</returns>
        public async Task<SegmentationResult> SegmentAsync(RenderTexture src, CancellationToken ct = default)
        {
            if (!src) throw new ArgumentNullException(nameof(src));

            ValidateConfiguration(apiKey, null, modelId);

            // Use async GPU readback to avoid blocking main thread
            var jpg = await EncodeTextureToJpegAsync(src);
            if (jpg == null)
            {
                throw new ArgumentException("Failed to encode texture to JPEG.");
            }

            var base64Image = Convert.ToBase64String(jpg);

            string rawJson;
            if (inferenceMode == InferenceMode.Cloud)
            {
                rawJson = await InferCloudAsync(base64Image, ct);
            }
            else
            {
                rawJson = await InferLocalSegmentationAsync(base64Image, ct);
            }

            return ParseSegmentationResponse(rawJson, src.width, src.height);
        }

        private async Task<string> InferLocalSegmentationAsync(string base64Image, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var endpoint = $"{localServerEndpoint.TrimEnd('/')}/infer/instance_segmentation";

            var payload = BuildLocalRequestPayload(base64Image);
            var http = new HttpTransport(null);
            return await http.PostJsonAsync(endpoint, payload, null, ct);
        }

        private SegmentationResult ParseSegmentationResponse(string rawJson, int inputWidth, int inputHeight)
        {
            if (string.IsNullOrEmpty(rawJson))
            {
                return CreateEmptySegmentationResult(inputWidth, inputHeight);
            }

            try
            {
                var predictionsJson = ExtractPredictionsArray(rawJson);
                var wrapper = JsonUtility.FromJson<RoboflowSegmentationResponseWrapper>("{\"predictions\":" + predictionsJson + "}");

                if (wrapper?.predictions == null || wrapper.predictions.Length == 0)
                {
                    return CreateEmptySegmentationResult(inputWidth, inputHeight);
                }

                var numObjects = wrapper.predictions.Length;
                var maskWidth = segmentationMaskWidth > 0 ? segmentationMaskWidth : DefaultMaskSize;
                var maskHeight = segmentationMaskHeight > 0 ? segmentationMaskHeight : DefaultMaskSize;

                var boxes = new float[numObjects * 4];
                var classIds = new int[numObjects];
                var scores = new float[numObjects];
                var masks = new float[numObjects * maskHeight * maskWidth];

                var maxClassId = 0;
                foreach (var p in wrapper.predictions)
                {
                    if (p.class_id > maxClassId) maxClassId = p.class_id;
                }

                var labels = new string[maxClassId + 1];

                for (var i = 0; i < numObjects; i++)
                {
                    var p = wrapper.predictions[i];

                    boxes[i * 4 + 0] = p.x / inputWidth;
                    boxes[i * 4 + 1] = p.y / inputHeight;
                    boxes[i * 4 + 2] = p.width / inputWidth;
                    boxes[i * 4 + 3] = p.height / inputHeight;

                    classIds[i] = p.class_id;
                    scores[i] = p.confidence;

                    if (p.class_id >= 0 && p.class_id < labels.Length)
                    {
                        labels[p.class_id] = p.@class ?? $"cls_{p.class_id}";
                    }

                    if (p.points is { Length: >= 3 })
                    {
                        RasterizePolygonToMask(p.points, masks, i, maskWidth, maskHeight, inputWidth, inputHeight);
                    }
                    else
                    {
                        Debug.LogWarning($"[RoboflowProvider] Prediction {i} ({p.@class}) has no polygon points.");
                    }
                }

                return new SegmentationResult
                {
                    inputWidth = inputWidth,
                    inputHeight = inputHeight,
                    maskWidth = maskWidth,
                    maskHeight = maskHeight,
                    numObjects = numObjects,
                    boxes = boxes,
                    classIds = classIds,
                    scores = scores,
                    masks = masks,
                    labels = labels,
                    maskAreLogits = false
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RoboflowProvider] Failed to parse segmentation response: {ex.Message}\n{ex.StackTrace}");
                return CreateEmptySegmentationResult(inputWidth, inputHeight);
            }
        }

        private SegmentationResult CreateEmptySegmentationResult(int width, int height)
        {
            return new SegmentationResult
            {
                inputWidth = width,
                inputHeight = height,
                maskWidth = segmentationMaskWidth > 0 ? segmentationMaskWidth : DefaultMaskSize,
                maskHeight = segmentationMaskHeight > 0 ? segmentationMaskHeight : DefaultMaskSize,
                numObjects = 0,
                boxes = Array.Empty<float>(),
                classIds = Array.Empty<int>(),
                scores = Array.Empty<float>(),
                masks = Array.Empty<float>(),
                labels = Array.Empty<string>(),
                maskAreLogits = false
            };
        }

        /// <summary>
        /// Rasterizes a polygon defined by points into a binary mask using scanline fill algorithm.
        /// Uses the even-odd rule: a point is inside if a ray from the point crosses an odd number of edges.
        /// Points are in input image coordinates and are scaled to mask coordinates.
        /// </summary>
        private static void RasterizePolygonToMask(RoboflowPoint[] points, float[] masks, int objectIndex,
            int maskWidth, int maskHeight, int inputWidth, int inputHeight)
        {
            var maskOffset = objectIndex * maskHeight * maskWidth;

            var scaleX = (float)maskWidth / inputWidth;
            var scaleY = (float)maskHeight / inputHeight;

            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var point in points)
            {
                var scaledY = point.y * scaleY;
                if (scaledY < minY) minY = scaledY;
                if (scaledY > maxY) maxY = scaledY;
            }

            var startY = Mathf.Max(0, Mathf.FloorToInt(minY));
            var endY = Mathf.Min(maskHeight - 1, Mathf.CeilToInt(maxY));

            for (var y = startY; y <= endY; y++)
            {
                _scanlineIntersections ??= new System.Collections.Generic.List<float>(16);
                _scanlineIntersections.Clear();
                var scanY = y + 0.5f;

                for (var i = 0; i < points.Length; i++)
                {
                    var p1 = points[i];
                    var p2 = points[(i + 1) % points.Length];

                    var p1y = p1.y * scaleY;
                    var p2y = p2.y * scaleY;
                    var p1x = p1.x * scaleX;
                    var p2x = p2.x * scaleX;

                    if ((p1y <= scanY && p2y > scanY) || (p2y <= scanY && p1y > scanY))
                    {
                        var dy = p2y - p1y;
                        if (Mathf.Abs(dy) < 1e-6f) continue;

                        var t = (scanY - p1y) / dy;
                        var xIntersect = p1x + t * (p2x - p1x);
                        _scanlineIntersections.Add(xIntersect);
                    }
                }

                _scanlineIntersections.Sort();

                for (var i = 0; i + 1 < _scanlineIntersections.Count; i += 2)
                {
                    var xStart = Mathf.Max(0, Mathf.CeilToInt(_scanlineIntersections[i]));
                    var xEnd = Mathf.Min(maskWidth - 1, Mathf.FloorToInt(_scanlineIntersections[i + 1]));

                    for (var x = xStart; x <= xEnd; x++)
                    {
                        masks[maskOffset + y * maskWidth + x] = 1.0f;
                    }
                }
            }
        }
    }
}
