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
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public class GpuNms
    {
        private readonly ComputeShader _shader;
        private readonly int _kernel;

        private ComputeBuffer _boxesBuffer;
        private ComputeBuffer _scoresBuffer;
        private ComputeBuffer _keepBuffer;
        private static readonly int NumBoxes = Shader.PropertyToID("numBoxes");
        private static readonly int IouThreshold = Shader.PropertyToID("iouThreshold");
        private static readonly int Boxes = Shader.PropertyToID("boxes");
        private static readonly int Scores = Shader.PropertyToID("scores");
        private static readonly int Keep = Shader.PropertyToID("keep");

        public GpuNms(ComputeShader shader)
        {
            _shader = shader;
            _kernel = shader.FindKernel("CSMain");
        }

        public List<int> RunNms(List<Vector4> boxes, List<float> scores, float iouThreshold = 0.5f)
        {
            var count = boxes.Count;
            if (count == 0)
            {
                return new List<int>();
            }

            ReleaseBuffers();

            _boxesBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            _scoresBuffer = new ComputeBuffer(count, sizeof(float));
            _keepBuffer = new ComputeBuffer(count, sizeof(int));

            _boxesBuffer.SetData(boxes);
            _scoresBuffer.SetData(scores);

            _shader.SetInt(NumBoxes, count);
            _shader.SetFloat(IouThreshold, iouThreshold);
            _shader.SetBuffer(_kernel, Boxes, _boxesBuffer);
            _shader.SetBuffer(_kernel, Scores, _scoresBuffer);
            _shader.SetBuffer(_kernel, Keep, _keepBuffer);

            var threadGroups = Mathf.CeilToInt(count / 64.0f);
            _shader.Dispatch(_kernel, threadGroups, 1, 1);

            var keepFlags = new int[count];
            _keepBuffer.GetData(keepFlags);

            var kept = new List<int>();
            for (var i = 0; i < count; i++)
            {
                if (keepFlags[i] == 1)
                    kept.Add(i);
            }

            ReleaseBuffers();
            return kept;
        }

        private void ReleaseBuffers()
        {
            _boxesBuffer?.Release();
            _scoresBuffer?.Release();
            _keepBuffer?.Release();
        }
    }
}
