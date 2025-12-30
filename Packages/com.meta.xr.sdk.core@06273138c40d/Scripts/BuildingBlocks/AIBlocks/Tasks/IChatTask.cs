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

using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using System;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [Serializable]
    public struct ImageInput
    {
        public string url;
        public byte[] bytes;
        public string mimeType;
    }

    [Serializable]
    public sealed class ChatRequest
    {
        public string text;
        public List<ImageInput> images;

        public ChatRequest(string text, List<ImageInput> images = null)
        {
            this.text = text;
            this.images = images;
        }
    }

    [Serializable]
    public sealed class ChatDelta
    {
        public string textFragment;

        public ChatDelta(string fragment)
        {
            textFragment = fragment;
        }
    }

    [Serializable]
    public sealed class ChatResponse
    {
        public string text;
        public readonly object Raw;

        public ChatResponse(string text, object raw = null)
        {
            this.text = text;
            Raw = raw;
        }
    }

    public interface IChatTask
    {
        bool SupportsVision { get; }

        Task<ChatResponse> ChatAsync(ChatRequest req,
            IProgress<ChatDelta> stream = null,
            CancellationToken ct = default);
    }

    public static class ChatImages
    {
        /// <summary>
        /// Safely encodes any Texture2D to JPG bytes, even if not readable or compressed.
        /// </summary>
        public static ImageInput FromTexture(Texture2D tex, string mime = "image/jpeg", int jpgQuality = 85)
        {
            if (!tex) return default;

            var jpg = SafeEncodeToJpg(tex, jpgQuality);
            return new ImageInput { bytes = jpg, mimeType = mime };
        }

        /// <summary>
        /// Wrap raw base64 image data into a proper data URI.
        /// </summary>
        public static string ToDataUri(string base64, string mime = "image/jpeg")
        {
            return string.IsNullOrEmpty(base64) ? null : $"data:{mime};base64,{base64}";
        }

        private static byte[] SafeEncodeToJpg(Texture2D tex, int jpgQuality)
        {
            var readable = tex.isReadable && !GraphicsFormatUtility.IsCompressedFormat(tex.graphicsFormat);
            if (readable)
            {
                return tex.EncodeToJPG(jpgQuality);
            }

            // GPU copy → readable Texture2D
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;

            var tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tmp.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            var jpg = tmp.EncodeToJPG(jpgQuality);
            UnityEngine.Object.Destroy(tmp);
            return jpg;
        }

        public static ImageInput FromUrl(string url)
        {
            return new ImageInput { url = url };
        }
    }

    internal static class ImageInputUtils
    {
        // Download URL → bytes (with redirect and size checks)
        public static async Task<byte[]> DownloadBytesAsync(string url, int maxBytes, CancellationToken ct)
        {
            using var uwr = UnityWebRequest.Get(url);
            uwr.downloadHandler = new DownloadHandlerBuffer();
            uwr.redirectLimit = 8;

            var op = uwr.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested) uwr.Abort();
                await Task.Yield();
            }

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ImageInputUtils] GET failed: {url} ({uwr.responseCode} {uwr.error})");
                return null;
            }

            var data = uwr.downloadHandler.data;
            if (data == null || data.Length == 0) return null;

            if (maxBytes > 0 && data.Length > maxBytes)
            {
                Debug.LogWarning($"[ImageInputUtils] Too large ({data.Length} > {maxBytes}): {url}");
                return null;
            }
            return data;
        }

        // HEAD/GET to resolve final URL (no body needed)
        public static async Task<string> ResolveRedirectAsync(string url, CancellationToken ct)
        {
            using var head = UnityWebRequest.Head(url);
            head.redirectLimit = 8;
            var op = head.SendWebRequest();
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested) head.Abort();
                await Task.Yield();
            }

            var ok = head.result == UnityWebRequest.Result.Success;
            if (ok) return head.url;

            using var get = UnityWebRequest.Get(url);
            get.downloadHandler = new DownloadHandlerBuffer();
            get.redirectLimit = 8;
            var op2 = get.SendWebRequest();
            while (!op2.isDone)
            {
                if (ct.IsCancellationRequested) get.Abort();
                await Task.Yield();
            }

            if (get.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[ImageInputUtils] Redirect resolution failed: {url} ({get.responseCode} {get.error})");
                return url;
            }
            return get.url;
        }

        // Guess MIME from header/bytes/url
        public static string GuessMime(byte[] bytes, string mimeFromHeader, string urlOrNull)
        {
            if (!string.IsNullOrEmpty(mimeFromHeader))
            {
                var m = mimeFromHeader.ToLowerInvariant();
                if (m.StartsWith("image/")) return m.Split(';')[0].Trim();
                if (m.Contains("png")) return "image/png";
                if (m.Contains("jpeg") || m.Contains("jpg")) return "image/jpeg";
                if (m.Contains("gif")) return "image/gif";
                if (m.Contains("webp")) return "image/webp";
            }

            if (!string.IsNullOrEmpty(urlOrNull))
            {
                var u = urlOrNull.ToLowerInvariant();
                if (u.Contains(".png")) return "image/png";
                if (u.Contains(".gif")) return "image/gif";
                if (u.Contains(".jpg") || u.Contains(".jpeg")) return "image/jpeg";
                if (u.Contains(".webp")) return "image/webp";
            }

            if (bytes != null && bytes.Length >= 4)
            {
                if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
                if (bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
                if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "image/gif";
            }

            return "image/png";
        }

        // ImageInput → data URI
        public static async Task<string> ToDataUriAsync(ImageInput img, int maxInlineBytes, CancellationToken ct)
        {
            // bytes → data:
            if (img.bytes is { Length: > 0 })
            {
                var mime = string.IsNullOrEmpty(img.mimeType) ? "image/png" : img.mimeType;
                return $"data:{mime};base64,{Convert.ToBase64String(img.bytes)}";
            }

            // data: URL already
            if (!string.IsNullOrEmpty(img.url) &&
                img.url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return img.url.Contains(";base64,") ? img.url : null;
            }

            // http(s) URL → download → data:
            if (!string.IsNullOrEmpty(img.url) &&
                (img.url.StartsWith("http://") || img.url.StartsWith("https://")))
            {
                var bytes = await DownloadBytesAsync(img.url, maxInlineBytes, ct);
                if (bytes == null) return null;

                var mime = GuessMime(bytes, null, img.url);
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }

            return null;
        }

        // ImageInput → raw base64 (no prefix)
        public static async Task<string> ToBase64Async(ImageInput img, int maxInlineBytes, CancellationToken ct)
        {
            if (img.bytes is { Length: > 0 }) return Convert.ToBase64String(img.bytes);

            if (!string.IsNullOrEmpty(img.url) &&
                img.url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = img.url.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 ? img.url[(idx + "base64,".Length)..] : null;
            }

            if (!string.IsNullOrEmpty(img.url) &&
                (img.url.StartsWith("http://") || img.url.StartsWith("https://")))
            {
                var bytes = await DownloadBytesAsync(img.url, maxInlineBytes, ct);
                return bytes == null ? null : Convert.ToBase64String(bytes);
            }

            return null;
        }
    }
}
