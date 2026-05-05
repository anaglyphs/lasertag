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
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Shared utilities for parsing streaming responses from various LLM providers.
    /// Handles SSE (Server-Sent Events) and newline-delimited JSON formats.
    /// Maintains internal buffers to handle partial chunks correctly.
    /// </summary>
    public static class StreamingParser
    {
        private static readonly object _sseLock = new object();
        private static readonly object _jsonLock = new object();
        private static readonly Dictionary<int, string> SseBuffers = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> JsonBuffers = new Dictionary<int, string>();

        /// <summary>
        /// Parses Server-Sent Events (SSE) formatted chunks and extracts text deltas.
        /// Handles partial chunks by maintaining a buffer per stream context.
        /// </summary>
        /// <param name="chunk">Raw chunk data from HTTP stream</param>
        /// <param name="extractor">Function to extract text from a JSON data line</param>
        /// <param name="streamId">Optional stream identifier for buffer isolation (defaults to 0)</param>
        /// <param name="isFinalChunk">Set to true on the last chunk to flush remaining buffer</param>
        /// <returns>List of extracted text tokens</returns>
        public static List<string> ParseSse(string chunk, Func<string, string> extractor, int streamId = 0, bool isFinalChunk = false)
        {
            lock (_sseLock)
            {
                var tokens = new List<string>();
                if (string.IsNullOrEmpty(chunk) && !isFinalChunk) return tokens;

                if (!SseBuffers.TryGetValue(streamId, out var buffer))
                {
                    buffer = string.Empty;
                }

                buffer += chunk ?? string.Empty;

                var lines = buffer.Split(new[] { '\n' }, StringSplitOptions.None);
                var completeLineCount = isFinalChunk ? lines.Length : lines.Length - 1;

                for (var i = 0; i < completeLineCount; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    var trimmedLine = line.Trim();

                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("event:"))
                    {
                        continue;
                    }

                    if (!trimmedLine.StartsWith("data: ")) continue;
                    var jsonData = trimmedLine.Substring(6).Trim();
                    if (jsonData == "[DONE]") continue;

                    try
                    {
                        var text = extractor(jsonData);
                        if (!string.IsNullOrEmpty(text))
                        {
                            tokens.Add(text);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[StreamingParser] Failed to parse SSE chunk: {ex.Message}. Data: {jsonData}");
                    }
                }

                if (isFinalChunk)
                {
                    SseBuffers.Remove(streamId);
                }
                else
                {
                    SseBuffers[streamId] = completeLineCount < lines.Length ? lines[lines.Length - 1] : string.Empty;
                }

                return tokens;
            }
        }

        /// <summary>
        /// Parses newline-delimited JSON chunks and extracts text using the provided extractor.
        /// Handles partial chunks by maintaining a buffer per stream context.
        /// </summary>
        /// <param name="chunk">Raw chunk data containing newline-separated JSON objects</param>
        /// <param name="extractor">Function to extract text from a JSON line</param>
        /// <param name="streamId">Optional stream identifier for buffer isolation (defaults to 0)</param>
        /// <param name="isFinalChunk">Set to true on the last chunk to flush remaining buffer</param>
        /// <returns>List of extracted text tokens</returns>
        public static List<string> ParseNewlineJson(string chunk, Func<string, string> extractor, int streamId = 0, bool isFinalChunk = false)
        {
            lock (_jsonLock)
            {
                var tokens = new List<string>();
                if (string.IsNullOrEmpty(chunk) && !isFinalChunk) return tokens;

                if (!JsonBuffers.TryGetValue(streamId, out var buffer))
                {
                    buffer = string.Empty;
                }

                buffer += chunk ?? string.Empty;

                var lines = buffer.Split(new[] { '\n' }, StringSplitOptions.None);
                var completeLineCount = isFinalChunk ? lines.Length : lines.Length - 1;

                for (var i = 0; i < completeLineCount; i++)
                {
                    var line = lines[i].TrimEnd('\r').Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        var text = extractor(line);
                        if (!string.IsNullOrEmpty(text))
                        {
                            tokens.Add(text);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[StreamingParser] Failed to parse newline-delimited JSON: {ex.Message}. Line: {line}");
                    }
                }

                if (isFinalChunk)
                {
                    JsonBuffers.Remove(streamId);
                }
                else
                {
                    JsonBuffers[streamId] = completeLineCount < lines.Length ? lines[lines.Length - 1] : string.Empty;
                }

                return tokens;
            }
        }

        /// <summary>
        /// Clears the buffer for a specific stream. Call this when a stream is cancelled or encounters an error.
        /// </summary>
        /// <param name="streamId">Stream identifier to clear</param>
        public static void ClearBuffers(int streamId = 0)
        {
            lock (_sseLock)
            {
                SseBuffers.Remove(streamId);
            }
            lock (_jsonLock)
            {
                JsonBuffers.Remove(streamId);
            }
        }

        /// <summary>
        /// Helper to extract a field from JSON using JsonUtility with a typed class.
        /// </summary>
        /// <returns>The parsed object, or null if parsing fails</returns>
        public static T ParseJson<T>(string json) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StreamingParser] Failed to parse JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tries to extract a field from JSON using JsonUtility with a typed class.
        /// </summary>
        /// <param name="json">The JSON string to parse</param>
        /// <param name="result">The parsed object if successful, null otherwise</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseJson<T>(string json, out T result) where T : class
        {
            result = null;
            try
            {
                result = JsonUtility.FromJson<T>(json);
                return result != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StreamingParser] Failed to parse JSON: {ex.Message}");
                return false;
            }
        }
    }
}
