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
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;

namespace Meta.XR.MetaWand.Editor.API
{
    /// <summary>
    /// High-level manager for MetaWand API operations that provides simplified workflows
    /// </summary>
    internal class MetaWandApiManager
    {

        private TimeSpan ImageCacheDuration { get; set; } = TimeSpan.FromDays(1);
        private readonly MetaWandApiClient _client;


        public const string StatusPreviewCompleted = "preview_completed";
        public const string StatusCompleted = "completed";

        public MetaWandApiManager(string accessToken)
        {
            _client = new MetaWandApiClient(accessToken);
        }


        /// <summary>
        /// Get asset generation usage limit information
        /// </summary>
        public async Task<UsageResult> CheckUsage(string usageFilter = "mesh_generation")
        {
            try
            {
                var response = await _client.CheckUsage(usageFilter);
                return new UsageResult(response.success, response);
            }
            catch (Exception ex)
            {
                return new UsageResult(false, null, ex.Message);
            }
        }

        /// <summary>
        /// Search for existing assets in the database based on text prompt
        /// </summary>
        /// <param name="searchText">Text prompt to search for</param>
        /// <param name="topK">Number of top results to return (default: 4)</param>
        /// <param name="polyCount">Target poly count for assets</param>
        /// <returns>Search result containing assets and similarity scores</returns>
        public async Task<SearchResult> SearchAssets(string searchText, int topK = 4, int polyCount = 0, string requestId = null)
        {
            try
            {
                var searchResponse = await _client.SearchAssets(searchText, topK, polyCount, requestId);
                if (!searchResponse.success)
                {
                    return new SearchResult(
                        success: false,
                        errorMessage: ContainsErrorSubCode(searchResponse.error) ?
                            searchResponse.error.error_user_msg :
                            searchResponse.error_message ?? "Asset search failed",
                        errorSubCode: ContainsErrorSubCode(searchResponse.error) ? searchResponse.error.error_subcode : null
                    );
                }

                return new SearchResult(
                    success: true,
                    searchText: searchText,
                    results: searchResponse.assets
                );
            }
            catch (Exception ex)
            {
                return new SearchResult(
                    success: false,
                    errorMessage: ex.Message
                );
            }
        }

        public async Task<DownloadResult<byte[]>> DownloadAsset(string assetUrl, string assetId,
            ScopedProgressDisplayer progressDisplayer, string cacheId = "genai_image_cache")
        {
            try
            {
                var url = new Uri(assetUrl);
                var downloader = new RemoteBinaryContentDownloader(assetId, url.GetLeftPart(UriPartial.Path))
                    .WithCacheDuration(ImageCacheDuration)
                    .WithCacheDirectory(cacheId)
                    .WithProgressDisplay(progressDisplayer);
                var urlParams = Utils.SplitUrlParameters(url.Query);
                foreach (var key in urlParams.Keys)
                {
                    downloader.WithUrlParameter(key, urlParams[key]);
                }

                var result = await downloader.Fetch();
                return !result.IsSuccess ? DownloadResult<byte[]>.Failure(result.ErrorMessage) : result;
            }
            catch (Exception ex)
            {
                return DownloadResult<byte[]>.Failure(ex.Message);
            }
        }

        public async Task<AssetResult> FetchLibraryAsset(string assetId, int polyCount, string requestId)
        {
            var response = await _client.FetchLibraryAsset(assetId, polyCount, false, requestId);
            if (!response.success)
            {
                var hasErrorSubCode = ContainsErrorSubCode(response.error);
                return new AssetResult(
                    success: false,
                    errorMessage: hasErrorSubCode ? response.error.error_user_msg : Constants.Failure,
                    errorSubCode: hasErrorSubCode ? response.error.error_subcode : null
                );
            }

            return new AssetResult
            (
                success: true,
                assetId: response.asset_id,
                assetParts: response.asset_parts,
                assetMetas: response.asset_metas
            );
        }

        /// <summary>
        /// Check if <see cref="Error"/> has any <see cref="Error.error_subcode"/>.
        /// </summary>
        /// <returns>True, if <see cref="Error"/> has <see cref="Error.error_subcode"/>. Otherwise, false.</returns>
        public static bool ContainsErrorSubCode(Error error) => error is { error_subcode: not null }
                                                                && Constants.ErrorCodes
                                                                    .ContainsKey(error.error_subcode);
    }


    /// <summary>
    /// Result of a 3D asset generation request
    /// </summary>
    internal readonly struct AssetResult : IResult
    {
        public bool Success { get; }
        public string Prompt { get; }
        public string AssetId { get; }
        public string PreviewImageUrl { get; }
        public AssetPart[] AssetParts { get; }
        public AssetMeta[] AssetMetas { get; }
        public string ErrorMessage { get; }
        public string ErrorSubCode { get; }


        public AssetResult(bool success, string prompt = null, string assetId = null,
            string previewImageUrl = null, AssetPart[] assetParts = null, AssetMeta[] assetMetas = null,
            string errorMessage = null, string errorSubCode = null)
        {
            Success = success;
            Prompt = prompt;
            AssetId = assetId;
            PreviewImageUrl = previewImageUrl;
            AssetParts = assetParts;
            ErrorMessage = errorMessage;
            ErrorSubCode = errorSubCode;
            AssetMetas = assetMetas;
        }
    }

    internal readonly struct UsageResult : IResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public CheckUsageResponse Usage { get; }

        public UsageResult(bool success, CheckUsageResponse usage = null, string errorMessage = null)
        {
            Success = success;
            Usage = usage;
            ErrorMessage = usage is { error: { error_subcode: not null } } ? usage.error.error_user_msg : errorMessage;
        }
    }

    /// <summary>
    /// Result of an asset search request
    /// </summary>
    internal readonly struct SearchResult : IResult
    {
        public bool Success { get; }
        public string SearchText { get; }
        public SearchAssetResult[] Results { get; }
        public string ErrorMessage { get; }
        public string ErrorSubCode { get; }

        public SearchResult(bool success, string searchText = null, SearchAssetResult[] results = null,
            string errorMessage = null, string errorSubCode = null)
        {
            Success = success;
            SearchText = searchText;
            Results = results;
            ErrorMessage = errorMessage;
            ErrorSubCode = errorSubCode;
        }
    }

    public interface IResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
    }
}
