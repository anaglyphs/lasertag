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
using UnityEngine;
using UnityEngine.Rendering;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// GPU-accelerated Non-Maximum Suppression utilities. Uses a compute shader
    /// to filter and compact detections so that only a small set of results needs
    /// to be read back to the CPU. See <see cref="ComputeShader"/> and
    /// <see cref="ComputeBuffer"/> for the underlying Unity APIs.
    /// </summary>
    public class GpuNms
    {
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        private ComputeBuffer _boxesBuffer;
        private ComputeBuffer _scoresBuffer;
        private ComputeBuffer _keepBuffer;
        private ComputeBuffer _keptIndicesBuffer;
        private ComputeBuffer _keptCountBuffer;

        private static readonly int NumBoxes = Shader.PropertyToID("numBoxes");
        private static readonly int IouThreshold = Shader.PropertyToID("iouThreshold");
        private static readonly int MinConfidence = Shader.PropertyToID("minConfidence");
        private static readonly int MaxKeep = Shader.PropertyToID("maxKeep");
        private static readonly int Boxes = Shader.PropertyToID("boxes");
        private static readonly int Scores = Shader.PropertyToID("scores");
        private static readonly int Keep = Shader.PropertyToID("keep");
        private static readonly int KeptIndices = Shader.PropertyToID("keptIndices");
        private static readonly int KeptCount = Shader.PropertyToID("keptCount");
        private static readonly int ClassIds = Shader.PropertyToID("classIds");
        private static readonly int OutDetections = Shader.PropertyToID("outDetections");
        private static readonly int ScaleX = Shader.PropertyToID("scaleX");
        private static readonly int ScaleY = Shader.PropertyToID("scaleY");

        public GpuNms(ComputeShader shader)
        {
            _shader = shader;
            _kernel = shader.FindKernel("CSMain");
        }

        /// <summary>
        /// Runs NMS on the GPU and returns kept indices for the provided
        /// CPU lists of boxes and scores. Use when detection results are
        /// already on the CPU. See also <see cref="RunNmsAsync(UnityEngine.ComputeBuffer,UnityEngine.ComputeBuffer,UnityEngine.ComputeBuffer,int,float,float,int,float,float)"/>
        /// for the zero-copy GPU gather path.
        /// </summary>
        public Task<List<int>> RunNmsIndicesAsync(List<Vector4> boxes, List<float> scores, List<int> classIds,
            float iouThreshold = 0.5f, float minConf = 0.5f, int maxKeep = 100)
        {
            var tcs = new TaskCompletionSource<List<int>>();
            var count = boxes.Count;
            if (count == 0)
            {
                tcs.SetResult(new List<int>());
                return tcs.Task;
            }

            ReleaseBuffers();

            _boxesBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            _scoresBuffer = new ComputeBuffer(count, sizeof(float));
            var classIdsBuffer = new ComputeBuffer(count, sizeof(int));
            _keepBuffer = new ComputeBuffer(count, sizeof(int));
            _keptIndicesBuffer = new ComputeBuffer(maxKeep, sizeof(int));
            _keptCountBuffer = new ComputeBuffer(1, sizeof(uint));

            var outDetections = new ComputeBuffer(maxKeep,
                sizeof(float) * 4 + // box
                sizeof(float) + // score
                sizeof(int) + // id
                sizeof(float) * 2 // pad
            );

            _boxesBuffer.SetData(boxes);
            _scoresBuffer.SetData(scores);
            classIdsBuffer.SetData(classIds);
            _keptCountBuffer.SetData(new[] { 0u });

            _shader.SetInt(NumBoxes, count);
            _shader.SetFloat(IouThreshold, iouThreshold);
            _shader.SetFloat(MinConfidence, minConf);
            _shader.SetInt(MaxKeep, maxKeep);
            _shader.SetFloat(ScaleX, 1.0f);
            _shader.SetFloat(ScaleY, 1.0f);
            _shader.SetBuffer(_kernel, Boxes, _boxesBuffer);
            _shader.SetBuffer(_kernel, Scores, _scoresBuffer);
            _shader.SetBuffer(_kernel, ClassIds, classIdsBuffer);
            _shader.SetBuffer(_kernel, Keep, _keepBuffer);
            _shader.SetBuffer(_kernel, KeptIndices, _keptIndicesBuffer);
            _shader.SetBuffer(_kernel, KeptCount, _keptCountBuffer);
            _shader.SetBuffer(_kernel, OutDetections, outDetections);

            _shader.Dispatch(_kernel, Mathf.CeilToInt(count / 64.0f), 1, 1);

            AsyncGPUReadback.Request(_keptCountBuffer, req =>
            {
                if (req.hasError)
                {
                    outDetections.Release();
                    classIdsBuffer.Release();
                    ReleaseBuffers();
                    tcs.SetResult(new List<int>());
                    return;
                }

                var k = Mathf.Min((int)req.GetData<uint>()[0], maxKeep);
                if (k <= 0)
                {
                    outDetections.Release();
                    classIdsBuffer.Release();
                    ReleaseBuffers();
                    tcs.SetResult(new List<int>());
                    return;
                }

                AsyncGPUReadback.Request(_keptIndicesBuffer, k * sizeof(int), 0, req2 =>
                {
                    var kept = new List<int>(k);
                    if (!req2.hasError)
                    {
                        var data = req2.GetData<int>();
                        for (var i = 0; i < k; i++)
                        {
                            kept.Add(data[i]);
                        }
                    }

                    outDetections.Release();
                    classIdsBuffer.Release();
                    ReleaseBuffers();
                    tcs.SetResult(kept);
                });
            });

            return tcs.Task;
        }

        private struct DetectionRaw
        {
            public Vector4 Box; // 16
            public float Score; //  4
            public int ID; //  4
            public Vector2 Pad; //  8
        }

        /// <summary>
        /// Compact detection produced by the GPU gather path. Each instance contains the scaled rectangle,
        /// confidence, and class id for a single kept box after Non-Maximum Suppression. This payload is
        /// intentionally minimal so providers can read back only a few bytes per frame and avoid large CPU
        /// downloads of full model tensors.
        /// </summary>
        /// <remarks>
        /// Coordinates in <see cref="Box"/> are already scaled from the model’s input size into source texture
        /// space (x, y, w, h). Map <see cref="ID"/> to a human-readable label using your class-labels array
        /// (for example, those loaded by <c>UnityInferenceEngineProvider</c>). Scores are unitless confidences
        /// in [0,1] and are suitable for thresholding or UI display.
        /// </remarks>
        public struct DetectionDto
        {
            /// <summary>
            /// Axis-aligned rectangle in source texture space as (x, y, w, h). The provider applies scaling
            /// from model input dimensions so consumers can directly overlay this on the original frame
            /// without additional normalization or aspect-ratio math.
            /// </summary>
            public Vector4 Box;

            /// <summary>
            /// Confidence score in the [0,1] range reflecting the model’s belief that this box contains the
            /// predicted class. Use this to sort, filter, or annotate UI elements with a percentage value.
            /// </summary>
            public float Score;

            /// <summary>
            /// Zero-based class index emitted by the model for this detection. Resolve to a descriptive label
            /// using the same labels table that was used at inference time to ensure consistent categories.
            /// </summary>
            public int ID;
        }

        /// <summary>
        /// Zero-copy GPU gather: runs NMS and writes compact detections
        /// directly on the GPU, then reads back only the first <c>maxKeep</c>
        /// results. Use when model outputs are on the GPU (Compute backend).
        /// </summary>
        /// <param name="boxesBuffer">GPU buffer of float4 boxes (xywh).</param>
        /// <param name="scoresBuffer">GPU buffer of scores.</param>
        /// <param name="classIdsBuffer">GPU buffer of class IDs.</param>
        /// <param name="count">Number of candidate boxes.</param>
        /// <param name="iouThreshold">IoU overlap threshold for suppression.</param>
        /// <param name="minConfidence">Minimum score to consider a box.</param>
        /// <param name="maxKeep">Maximum number of kept detections.</param>
        /// <param name="scaleX">Scale from model input width to source texture width.</param>
        /// <param name="scaleY">Scale from model input height to source texture height.</param>
        public Task<List<DetectionDto>> RunNmsAsync(
            ComputeBuffer boxesBuffer,
            ComputeBuffer scoresBuffer,
            ComputeBuffer classIdsBuffer,
            int count,
            float iouThreshold,
            float minConfidence,
            int maxKeep,
            float scaleX,
            float scaleY)
        {
            var tcs = new TaskCompletionSource<List<DetectionDto>>();

            ReleaseBuffers();

            _keepBuffer = new ComputeBuffer(count, sizeof(int));
            _keptIndicesBuffer = new ComputeBuffer(maxKeep, sizeof(int));
            _keptCountBuffer = new ComputeBuffer(1, sizeof(uint));

            var outDetections = new ComputeBuffer(maxKeep,
                sizeof(float) * 4 + // box
                sizeof(float) + // score
                sizeof(int) + // id
                sizeof(float) * 2 // pad
            );

            _keptCountBuffer.SetData(new[] { 0u });

            _shader.SetInt(NumBoxes, count);
            _shader.SetFloat(IouThreshold, iouThreshold);
            _shader.SetFloat(MinConfidence, minConfidence);
            _shader.SetInt(MaxKeep, maxKeep);
            _shader.SetFloat(ScaleX, scaleX);
            _shader.SetFloat(ScaleY, scaleY);

            _shader.SetBuffer(_kernel, Boxes, boxesBuffer);
            _shader.SetBuffer(_kernel, Scores, scoresBuffer);
            _shader.SetBuffer(_kernel, ClassIds, classIdsBuffer);
            _shader.SetBuffer(_kernel, Keep, _keepBuffer);
            _shader.SetBuffer(_kernel, KeptIndices, _keptIndicesBuffer);
            _shader.SetBuffer(_kernel, KeptCount, _keptCountBuffer);
            _shader.SetBuffer(_kernel, OutDetections, outDetections);

            _shader.Dispatch(_kernel, Mathf.CeilToInt(count / 64.0f), 1, 1);

            // Read back small header, then K compact detections
            AsyncGPUReadback.Request(_keptCountBuffer, req =>
            {
                if (req.hasError)
                {
                    outDetections.Release();
                    ReleaseBuffers();
                    tcs.SetResult(new List<DetectionDto>());
                    return;
                }

                var k = Mathf.Min((int)req.GetData<uint>()[0], maxKeep);
                if (k <= 0)
                {
                    outDetections.Release();
                    ReleaseBuffers();
                    tcs.SetResult(new List<DetectionDto>());
                    return;
                }

                var bytes = k * 32;
                AsyncGPUReadback.Request(outDetections, bytes, 0, req2 =>
                {
                    var list = new List<DetectionDto>(k);
                    if (!req2.hasError)
                    {
                        var data = req2.GetData<DetectionRaw>();
                        for (var i = 0; i < k; i++)
                        {
                            var r = data[i];
                            list.Add(new DetectionDto
                            {
                                Box = r.Box,
                                Score = r.Score,
                                ID = r.ID
                            });
                        }
                    }

                    outDetections.Release();
                    ReleaseBuffers();
                    tcs.SetResult(list);
                });
            });

            return tcs.Task;
        }

        private void ReleaseBuffers()
        {
            _boxesBuffer?.Release();
            _boxesBuffer = null;
            _scoresBuffer?.Release();
            _scoresBuffer = null;
            _keepBuffer?.Release();
            _keepBuffer = null;
            _keptIndicesBuffer?.Release();
            _keptIndicesBuffer = null;
            _keptCountBuffer?.Release();
            _keptCountBuffer = null;
        }
    }
}
