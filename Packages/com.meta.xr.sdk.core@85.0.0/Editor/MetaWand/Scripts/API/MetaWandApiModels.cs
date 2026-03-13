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

namespace Meta.XR.MetaWand.Editor.API
{

    [Serializable]
    public class CheckUsage
    {
        public string usage_filter;
        public string access_token;
        public Error error;
    }

    [Serializable]
    public class CheckUsageResponse
    {
        public int mesh_preview_gen_usage_limit;
        public int mesh_full_gen_usage_limit;
        public int mesh_preview_gen_recent_usage_count;
        public int mesh_full_gen_recent_usage_count;
        public bool success;
        public string error_message;
        public Error error;
    }

    [Serializable]
    public class FetchAssetRequest
    {
        public string request_id;
        public string asset_id;
        public bool query_b64s = false;
        public SearchAssetsAttributes attributes;
        public string access_token;
        public AppInfoAttribute app_info = new AppInfoAttribute
        {
            name = Constants.CoreSDKPackageName,
            version = Utils.CoreSdkVersion.ToString(),
            build_channel = "release"
        };
    }

    [Serializable]
    public class FetchAssetResponse
    {
        public bool success;
        public string asset_id;
        public string status;
        public string asset_type;
        public string asset_sub_type;
        public string asset_short_name;
        public string gen_model;
        public PreviewUrls preview_urls;
        public AssetPart[] asset_parts;
        public AssetMeta[] asset_metas;
        public string error_message;
        public Error error;
    }

    [Serializable]
    public class AssetMeta
    {
        public int polycount;
        public int[] all_polycounts;
    }

    [Serializable]
    public class PreviewUrls
    {
        public string image;
    }

    [Serializable]
    public class MeshUrls
    {
        public string glb;
        public string fbx;
    }

    [Serializable]
    public class TextureUrl
    {
        public string albedo;
        public string normal;
        public string roughness;
        public string metallic;
    }



    [Serializable]
    public class AssetPart
    {
        public MeshUrls mesh_urls;
        public TextureUrl[] texture_urls;
    }

    [Serializable]
    public class SearchAssetsRequest
    {
        public string request_id;
        public string search_text;
        public string top_k = "4";
        public SearchAssetsAttributes attributes;
        public string access_token;

        public AppInfoAttribute app_info = new AppInfoAttribute
        {
            name = Constants.CoreSDKPackageName,
            version = Utils.CoreSdkVersion.ToString(),
            build_channel = "release"
        };
    }

    [Serializable]
    public class SearchAssetsAttributes
    {
        public MeshAttribute mesh;
    }

    [Serializable]
    public class MeshAttribute
    {
        public int target_polycount;
    }

    [Serializable]
    public class AppInfoAttribute
    {
        public string name;
        public string version;
        public string version_code;
        public string build_channel;
    }

    [Serializable]
    public class SearchAssetsResponse
    {
        public bool success;
        public SearchAssetResult[] assets;
        public string error_message;
        public Error error;
    }

    [Serializable]
    public class SearchAssetResult
    {
        public SearchAsset asset;
        public float similarity_score;
    }

    [Serializable]
    public class SearchAsset
    {
        public string asset_id;
        public string status;
        public string asset_type;
        public string asset_sub_type;
        public string gen_model;
        public PreviewUrls preview_urls;
        public AssetPart[] asset_parts;
        public AssetMeta[] asset_metas;
        public bool success;
    }

    [Serializable]
    public class Error
    {
        public string message;
        public string type;
        public string code;
        public string error_subcode;
        public string error_user_title;
        public string error_user_msg;
    }
}
