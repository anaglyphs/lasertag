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
    /// <summary>
    /// Interface for object detection providers supporting both cloud (JSON) and on-device (binary) execution.
    /// </summary>
    public interface IObjectDetectionTask
    {
        /// <summary>
        /// Performs object detection on an encoded image (JPEG/PNG).
        /// Used by cloud providers (e.g., HuggingFace) that accept binary image uploads.
        /// </summary>
        /// <param name="imageJpgOrPng">Encoded image bytes in JPEG or PNG format.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        /// <returns>JSON string containing detection results with boxes, scores, and labels.</returns>
        Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default);

        /// <summary>
        /// Performs object detection directly on a GPU texture (fastest path).
        /// Used by on-device providers (e.g., UnityInferenceEngine) to avoid CPU-GPU transfers.
        /// </summary>
        /// <param name="src">Source RenderTexture containing the image to analyze.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        /// <returns>Binary-encoded detection results for efficient downstream processing.</returns>
        Task<byte[]> DetectAsync(RenderTexture src, CancellationToken ct = default);
    }
}
