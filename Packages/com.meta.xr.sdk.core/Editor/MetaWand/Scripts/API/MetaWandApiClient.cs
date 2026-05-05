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
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Telemetry;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Meta.XR.MetaWand.Editor.API
{
    /// <summary>
    /// Minimal API client to generate mesh preview with user prompt using HttpClient
    /// </summary>
    internal class MetaWandApiClient
    {
        private const string SEARCH_ASSETS_URL = "https://graph.oculus.com/meta_wand_v2_search_sync";
        private const string CHECK_USAGE = "https://graph.oculus.com/meta_wand_v2_check_usage";
        private const string FETCH_FROM_LIBRARY_URL = "https://graph.oculus.com/meta_wand_v2_fetch_from_asset_database";
        private const string TELEMETRY_URL = "https://graph.oculus.com/meta_wand_v2_telemetry";

        private string _accessToken;
        private readonly HttpClient _httpClient;

        public MetaWandApiClient(string accessToken, HttpClient httpClient = null)
        {
            _accessToken = accessToken;
            _httpClient = httpClient ?? new HttpClient();
        }


        /// <summary>
        /// Check the user's usage limit
        /// </summary>
        public async Task<CheckUsageResponse> CheckUsage(string usageFilter = "mesh_generation")
        {
            var request = new CheckUsage()
            {
                usage_filter = usageFilter,
                access_token = _accessToken
            };

            var json = JsonUtility.ToJson(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(CHECK_USAGE, content);
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<CheckUsageResponse>(responseText);
            result.success = response.IsSuccessStatusCode;

            return result;
        }

        /// <summary>
        /// Search for assets in the asset database based on text prompt
        /// </summary>
        /// <param name="searchText">The text prompt to search for</param>
        /// <param name="topK">Number of top results to return (default: 4)</param>
        /// <param name="polyCount">Target poly count for assets</param>
        /// <param name="requestId">(Optional) An Unique id for request.</param>
        /// <returns>Search results with assets and similarity scores</returns>
        public async Task<SearchAssetsResponse> SearchAssets(string searchText, int topK = 4, int polyCount = 0, string requestId = null)
        {
            var request = new SearchAssetsRequest
            {
                request_id = requestId ?? System.Guid.NewGuid().ToString(),
                search_text = searchText,
                top_k = topK.ToString(),
                attributes = polyCount == 0
                    ? null
                    : new SearchAssetsAttributes
                    {
                        mesh = new MeshAttribute
                        {
                            target_polycount = polyCount
                        }
                    },
                access_token = _accessToken
            };

            var json = JsonUtility.ToJson(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(SEARCH_ASSETS_URL, content);
            var responseText = await response.Content.ReadAsStringAsync();

            try
            {
                var result = JsonUtility.FromJson<SearchAssetsResponse>(responseText);
                if (result == null)
                {
                    throw new NullReferenceException(response.ReasonPhrase);
                }
                result.success = response.IsSuccessStatusCode;
                return result;
            }
            catch (Exception e)
            {
                IssueTracker.TrackError(IssueTracker.SDK.MetaWand, "metawand-search-assets-failed",
                    $"Failed to search assets with prompt '{searchText}': {e.Message}", enableDebugLog: false);
                return new SearchAssetsResponse()
                {
                    success = false,
                    error_message = response.ReasonPhrase,
                    assets = null
                };
            }
        }

        /// <summary>
        /// Fetch asset from asset library
        /// </summary>
        public async Task<FetchAssetResponse> FetchLibraryAsset(string assetId, int polyCount, bool includeBase64 = false, string requestId = null)
        {
            var request = new FetchAssetRequest
            {
                request_id = requestId ?? System.Guid.NewGuid().ToString(),
                asset_id = assetId,
                attributes = new SearchAssetsAttributes { mesh = new MeshAttribute { target_polycount = polyCount } },
                query_b64s = includeBase64,
                access_token = _accessToken
            };

            var json = JsonUtility.ToJson(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(FETCH_FROM_LIBRARY_URL, content);
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<FetchAssetResponse>(responseText);
            result.success = response.IsSuccessStatusCode;

            return result;
        }

        /// <summary>
        /// Test if the client has feedback telemetry permissions
        /// </summary>
        /// <param name="requestId">(Optional) A unique id for request.</param>
        /// <returns>TelemetryResponse indicating success or failure</returns>
        public async Task<TelemetryResponse> ShouldDisplayFeedbackUI(string requestId = null)
        {
            var request = new TelemetryRequest
            {
                action = "test_feedback_telemetry",
                request_id = requestId ?? System.Guid.NewGuid().ToString(),
                access_token = _accessToken
            };

            var json = JsonUtility.ToJson(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(TELEMETRY_URL, content);
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<TelemetryResponse>(responseText);
            result.success = response.IsSuccessStatusCode;

            return result;
        }

        /// <summary>
        /// Log feedback (like or dislike) for a specific asset
        /// </summary>
        /// <param name="assetId">The asset ID to provide feedback for</param>
        /// <param name="action">The feedback action (use Constants.AssetFeedbackActionLike or Constants.AssetFeedbackActionDislike)</param>
        /// <param name="requestId">(Optional) A unique id for request.</param>
        /// <returns>TelemetryResponse indicating success or failure</returns>
        public async Task<TelemetryResponse> AssetFeedback(string assetId, string action, string requestId = null)
        {
            var request = new TelemetryRequest
            {
                action = action,
                asset_id = assetId,
                request_id = requestId ?? System.Guid.NewGuid().ToString(),
                access_token = _accessToken
            };

            var json = JsonUtility.ToJson(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(TELEMETRY_URL, content);
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<TelemetryResponse>(responseText);
            result.success = response.IsSuccessStatusCode;

            return result;
        }

        /// <summary>
        /// Log feedback (like or dislike) for a search query's overall results
        /// </summary>
        /// <param name="targetRequestId">The request_id of the original search request</param>
        /// <param name="originalSearchText">The query of the original search request</param>
        /// <param name="action">The feedback action (use Constants.SearchFeedbackActionLike or Constants.SearchFeedbackActionDislike)</param>
        /// <param name="requestId">(Optional) A unique id for request.</param>
        /// <returns>TelemetryResponse indicating success or failure</returns>
        public async Task<TelemetryResponse> SearchFeedback(string targetRequestId, string originalSearchText, string action, string requestId = null)
        {
            var request = new TelemetryRequest
            {
                action = action,
                target_request_id = targetRequestId,
                original_search_text = originalSearchText,
                request_id = requestId ?? System.Guid.NewGuid().ToString(),
                access_token = _accessToken
            };

            var json = JsonUtility.ToJson(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(TELEMETRY_URL, content);
            var responseText = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<TelemetryResponse>(responseText);
            result.success = response.IsSuccessStatusCode;

            return result;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
