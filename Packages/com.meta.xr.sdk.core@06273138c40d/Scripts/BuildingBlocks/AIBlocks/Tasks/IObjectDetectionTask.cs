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

using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public interface IObjectDetectionTask
    {
        // JSON-based providers (HuggingFace)
        Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default);

        // Optional binary path for byte[] (not used by HF, can throw)
        Task<byte[]> DetectBinaryAsync(byte[] imageJpgOrPng, CancellationToken ct = default);

        // Fastest path (UnityInferenceEngine)
        Task<byte[]> DetectAsync(RenderTexture src, CancellationToken ct = default);
    }
}
