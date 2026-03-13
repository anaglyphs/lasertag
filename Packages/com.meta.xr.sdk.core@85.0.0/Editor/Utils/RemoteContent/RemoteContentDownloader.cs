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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.Editor.RemoteContent
{
    internal readonly struct DownloadResult<T>
    {
        public bool IsSuccess { get; }
        public T Content { get; }
        public string ErrorMessage { get; }
        public string FileName { get; }

        private DownloadResult(bool isSuccess, T content, string errorMessage, string fileName)
        {
            IsSuccess = isSuccess;
            Content = content;
            ErrorMessage = errorMessage;
            FileName = fileName;
        }

        public static DownloadResult<T> Success(T content) =>
            new(isSuccess: true, content: content, errorMessage: null, fileName: null);

        public static DownloadResult<T> Success(T content, string fileName) => new(isSuccess: true, content: content,
            errorMessage: null, fileName: fileName);

        public static DownloadResult<T> Failure(string errorMessage = null) => new(isSuccess: false, content: default,
            errorMessage: errorMessage, fileName: null);
    }

    internal static class RemoteContentHttpClient
    {
        internal static HttpClient Client { get; set; } = new();
    }

    internal abstract class RemoteContentDownloader<T> where T : RemoteContentDownloader<T>
    {
        private readonly string _url;
        private string _fileName;
        private readonly Dictionary<string, string> _urlParameters = new();

        private TimeSpan _cacheDuration = TimeSpan.FromDays(1);
        private string _cacheDirectory = "remote_content";
        private string _cacheFilePath;
        private bool _useCache = true;
        private IScopedProgressDisplayer _progressDisplayer = NullScopedScopedProgressDisplayer.Instance;
        private bool _enforceMediaType = true;
        private bool _missingMachineIdParam;


        protected string CacheFilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(_cacheFilePath))
                {
                    return _cacheFilePath;
                }

                var directory = Path.Combine(Path.GetTempPath(), "Meta", _cacheDirectory);
                Directory.CreateDirectory(directory);
                _cacheFilePath = Path.Combine(directory, _fileName);
                return _cacheFilePath;
            }
        }

        private static int? SdkVersion
        {
            get
            {
                if (OVRPlugin.wrapperVersion == null || OVRPlugin.wrapperVersion == new Version(0, 0, 0))
                {
                    return null;
                }

                return OVRPlugin.wrapperVersion.Minor - 32;
            }
        }

        public T WithoutMediaTypeValidation()
        {
            _enforceMediaType = false;
            return (T)this;
        }

        public T WithProgressDisplay(IScopedProgressDisplayer progressDisplayer)
        {
            _progressDisplayer = progressDisplayer;
            return (T)this;
        }

        public T WithoutCache()
        {
            _useCache = false;
            return (T)this;
        }

        public T WithCacheDuration(TimeSpan duration)
        {
            _cacheDuration = duration;
            return (T)this;
        }

        public T WithCacheDirectory(string directory)
        {
            _cacheDirectory = directory;
            return (T)this;
        }

        public T WithCachePerSDKVersion()
        {
            if (SdkVersion.HasValue)
            {
                _fileName = $"{SdkVersion.Value}_{_fileName}";
            }
            return (T)this;
        }

        public T WithUrlParameter(string key, string value)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
            {
                _urlParameters[key] = value;
            }

            return (T)this;
        }

        public T WithSDKVersionUrlParameter()
        {
            return WithUrlParameter("sdk_version", SdkVersion.HasValue ? SdkVersion.Value.ToString() : string.Empty);
        }

        public T WithMachineIdUrlParameter(bool required = false)
        {
            var machineID = OVRPlugin.GetMachineID();

            if (required && string.IsNullOrEmpty(machineID))
            {
                _missingMachineIdParam = true;
            }

            return WithUrlParameter("machine_id", machineID);
        }

        protected RemoteContentDownloader(string fileName, string url)
        {
            _fileName = fileName;
            _url = url;

            if (url.Contains("?"))
            {
                throw new ArgumentException(
                    $"Url must not contain parameters, please use {nameof(WithUrlParameter)} instead.)");
            }
        }

        protected RemoteContentDownloader(string fileName, ulong contentId)
            : this(fileName, UrlFromContentId(contentId))
        {
        }

        protected RemoteContentDownloader(ulong contentId) :
            this(contentId.ToString(), UrlFromContentId(contentId))
        {
        }

        private bool HasValidCache() =>
            File.Exists(CacheFilePath) &&
            DateTime.Now - File.GetLastWriteTime(CacheFilePath) < _cacheDuration;

        public void ClearCache()
        {
            if (File.Exists(CacheFilePath))
            {
                File.Delete(CacheFilePath);
            }
        }

        private async Task<DownloadResult<byte[]>> DownloadContent(IScopedProgressDisplayer scopedProgressDisplayer)
        {
            using var marker = new OVRTelemetryMarker(OVRTelemetryConstants.Utils.MarkerId.DownloadContent);
            try
            {
                var path = BuildRequestPath();
                var response =
                    await RemoteContentHttpClient.Client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                if (_enforceMediaType && !ValidateContentType(contentType))
                {
                    throw new Exception("Unexpected media type");
                }

                var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                scopedProgressDisplayer.SetDescription($"Downloading file {fileName}");

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync();

                using var memoryStream = totalBytes > 0 ? new MemoryStream((int)totalBytes) : new MemoryStream();
                await using var progressStream =
                    new ProgressReportingStream(memoryStream, totalBytes, scopedProgressDisplayer);
                await contentStream.CopyToAsync(progressStream, 81920);

                marker.SetResult(OVRPlugin.Qpl.ResultType.Success);
                return DownloadResult<byte[]>.Success(memoryStream.ToArray(), fileName);
            }
            catch (Exception ex)
            {
                marker.AddAnnotation(OVRTelemetryConstants.Utils.AnnotationType.ErrorMessage, ex.Message);
                marker.SetResult(OVRPlugin.Qpl.ResultType.Fail);
                return DownloadResult<byte[]>.Failure(ex.Message);
            }
        }

        protected abstract bool ValidateContentType(string contentType);

        internal string BuildRequestPath()
        {
            if (_urlParameters.Count <= 0)
            {
                return _url;
            }

            var queryString = string.Join("&", _urlParameters.Select(p => $"{p.Key}={p.Value}"));
            return _url + "?" + queryString;
        }

        protected async Task<DownloadResult<TContent>> Fetch<TContent>()
        {
            try
            {
                if (_missingMachineIdParam)
                {
                    return DownloadResult<TContent>.Failure("Machine ID is required but not available");
                }

                if (_useCache && HasValidCache())
                {
                    var cachedContent = await ReadFromCache<TContent>();
                    return DownloadResult<TContent>.Success(cachedContent);
                }

                var downloadResult = await DownloadRawContent();

                if (!downloadResult.IsSuccess)
                {
                    return DownloadResult<TContent>.Failure(downloadResult.ErrorMessage);
                }

                var convertedContent = ConvertFromBytes<TContent>(downloadResult.Content);

                if (_useCache)
                {
                    await WriteToCache(convertedContent);
                }

                return DownloadResult<TContent>.Success(convertedContent, downloadResult.FileName);
            }
            catch (Exception exception)
            {
                return DownloadResult<TContent>.Failure(exception.Message);
            }
        }

        protected async Task<bool> PreDownload<TContent>()
        {
            try
            {
                if (_useCache && HasValidCache())
                {
                    return true;
                }

                var downloadResult = await DownloadRawContent();

                if (!downloadResult.IsSuccess)
                {
                    return false;
                }

                var convertedContent = ConvertFromBytes<TContent>(downloadResult.Content);

                if (_useCache)
                {
                    await WriteToCache(convertedContent);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<DownloadResult<byte[]>> DownloadRawContent() => await DownloadContent(_progressDisplayer);

        private static string UrlFromContentId(ulong contentId) =>
            $"https://www.facebook.com/framework_tools/remote_content_fetch/{contentId}";

        protected abstract Task<TContent> ReadFromCache<TContent>();
        protected abstract Task WriteToCache<TContent>(TContent content);
        protected abstract TContent ConvertFromBytes<TContent>(byte[] bytes);
    }

    internal class RemoteJsonContentDownloader : RemoteContentDownloader<RemoteJsonContentDownloader>
    {
        public RemoteJsonContentDownloader(string cacheFile, string url)
            : base(cacheFile, url)
        {
        }

        public RemoteJsonContentDownloader(string cacheFile, ulong contentId)
            : base(cacheFile, contentId)
        {
        }

        public async Task<DownloadResult<string>> Fetch()
        {
            return await Fetch<string>();
        }

        protected override bool ValidateContentType(string contentType)
        {
            return contentType == "application/json";
        }

        protected override async Task<TContent> ReadFromCache<TContent>()
        {
            if (typeof(TContent) != typeof(string))
            {
                throw new InvalidOperationException(
                    $"RemoteJsonContentDownloader only supports string content type, got {typeof(TContent)}");
            }

            var content = await File.ReadAllTextAsync(CacheFilePath);
            return (TContent)(object)content;
        }

        protected override async Task WriteToCache<TContent>(TContent content)
        {
            if (typeof(TContent) != typeof(string))
            {
                throw new InvalidOperationException(
                    $"RemoteJsonContentDownloader only supports string content type, got {typeof(TContent)}");
            }

            await File.WriteAllTextAsync(CacheFilePath, (string)(object)content);
        }

        protected override TContent ConvertFromBytes<TContent>(byte[] bytes)
        {
            if (typeof(TContent) != typeof(string))
            {
                throw new InvalidOperationException(
                    $"RemoteJsonContentDownloader only supports string content type, got {typeof(TContent)}");
            }

            var jsonString = System.Text.Encoding.UTF8.GetString(bytes);
            return (TContent)(object)jsonString;
        }
    }

    internal class RemoteBinaryContentDownloader : RemoteContentDownloader<RemoteBinaryContentDownloader>
    {
        public RemoteBinaryContentDownloader(string cacheFile, string url)
            : base(cacheFile, url)
        {
        }

        public RemoteBinaryContentDownloader(ulong contentId)
            : base(contentId)
        {
        }

        public async Task<DownloadResult<byte[]>> Fetch()
        {
            return await Fetch<byte[]>();
        }

        protected override bool ValidateContentType(string contentType)
        {
            return true;
        }

        protected override async Task<TContent> ReadFromCache<TContent>()
        {
            if (typeof(TContent) != typeof(byte[]))
            {
                throw new InvalidOperationException(
                    $"RemoteBinaryContentDownloader only supports byte[] content type, got {typeof(TContent)}");
            }

            var content = await File.ReadAllBytesAsync(CacheFilePath);
            return (TContent)(object)content;
        }

        protected override async Task WriteToCache<TContent>(TContent content)
        {
            if (typeof(TContent) != typeof(byte[]))
            {
                throw new InvalidOperationException(
                    $"RemoteBinaryContentDownloader only supports byte[] content type, got {typeof(TContent)}");
            }

            await File.WriteAllBytesAsync(CacheFilePath, (byte[])(object)content);
        }

        protected override TContent ConvertFromBytes<TContent>(byte[] bytes)
        {
            if (typeof(TContent) != typeof(byte[]))
            {
                throw new InvalidOperationException(
                    $"RemoteBinaryContentDownloader only supports byte[] content type, got {typeof(TContent)}");
            }

            return (TContent)(object)bytes;
        }

        private Task PreDownload()
        {
            return base.PreDownload<byte[]>();
        }

        public static async Task PreloadDownloaders(IEnumerable<RemoteBinaryContentDownloader> downloaders)
        {
            var downloadTasks = downloaders
                .Select(downloader => downloader.PreDownload())
                .ToArray();

            const int batchSize = 5;

            for (var i = 0; i < downloadTasks.Length; i += batchSize)
            {
                var batch = downloadTasks.Skip(i).Take(batchSize);
                await Task.WhenAll(batch);
            }
        }
    }
}
