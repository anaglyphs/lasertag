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

namespace Meta.XR.MetaWand.Editor
{
    internal static class Constants
    {
        public const string AssetLibraryPublicName = "Asset library";
        public const string AssetLibraryMenuDescriptionKey = "menu_description";
        public const string AssetLibraryMenuDescription = "Explore a central repository of pre-generated 3D assets";

        public const string MetaSubtextKey = "powered_by";
        public const string MetaSubtextFallback = "";
        public const string CoreSDKPackageName = "com.meta.xr.sdk.core";

        public const string SearchPlaceholderText = "Write here to search";
        public const string AssetFolder = "MetaAssets";
        public const int SearchResultQueryCount = 18;
        public const string CacheDir = "SessionData";
        public const string ParentFolderPath = "Assets";

        public const string PreGenPrefix = "pre_gen_";

        public const string ModelLod0 = "LOD 0 (XL)";
        public const string ModelLod1 = "LOD 1 (L)";
        public const string ModelLod2 = "LOD 2 (M)";
        public const string ModelLod3 = "LOD 3 (S)";

        public const int DefaultModelSize = 4096;
        public const string LearnMoreUrlKey = "learn_more_meta_account_url";
        public const string LearnMoreFallback = "https://www.meta.com/help/quest/1336626146870772/";
        public const string HelpCenterUrlKey = "help_center_url";
        public const string HelpCenterUrlFallback = "https://developers.meta.com/horizon/develop/unity";

        // Errors
        public const string Success = "Success";
        public const string Failure = "Failure";
        public const string SomethingWrong = "Something went wrong.";
        public const string SomethingWrongTryAgain = "Something went wrong. Please try again.";
        public const string ErrorPermissionDenied = "Permission denied";
        public const string ErrorInvalidContent = "Invalid content";
        public const string ErrorLimitExceeded = "Limit exceeded";
        public const string ErrorUnexpectedError = "Unexpected error";
        public const string ErrorInvalidParam = "Invalid param";

        public const string ErrorMessageDefaultFailedToLoad = "Unable to display pre-created 3D assets. Try entering a new prompt or file a bug.";
        public const string FeedbackText = "Share your feedback";

        // Error sub-codes
        public static readonly Dictionary<string, string> ErrorCodes = new()
        {
            { "4778001", ErrorPermissionDenied },
            { "4778002", ErrorInvalidContent },
            { "4778003", ErrorLimitExceeded },
            { "4778004", ErrorUnexpectedError },
            { "4778005", ErrorUnexpectedError },
            { "4778006", ErrorUnexpectedError },
            { "4778008", ErrorInvalidParam },
            { "4778009", ErrorUnexpectedError }
        };

        public static class Telemetry
        {
            public const string TargetSearchButton = "metawand_search_button";
            public const string EventNamePreviewsGenerated = "METAWAND_PREVIEWS_GENERATED";
            public const string EventNameLinkClick = "METAWAND_LINK_CLICK";
            public const string EventNameLoginSuccess = "META_LOGIN_SUCCESS";
            public const string EventNameLoginFailure = "META_LOGIN_FAILURE";
            public const string EventNamePageImpression = "METAWAND_PAGE_IMPRESSION";
            public const string EventNameObjectAddedToScene = "METAWAND_OBJECT_ADDED_TO_SCENE";
            public const string EventNamePreGenerationFailure = "METAWAND_PREGEN_GENERATION_FAILURE";
            public const string EventNameBannedUserError = "METAWAND_BANNED_USER_ERROR";

            public const string EntrypointAuthToolbar = "METAWAND_AUTH_TOOLBAR";
            public const string EntrypointSignUp = "METAWAND_SIGN_UP";
            public const string EntrypointNullState = "METAWAND_NULLSTATE";
            public const string EntrypointLoadState = "METAWAND_LOADSTATE";
            public const string EntrypointBannedUserState = "METAWAND_BANNED_USER_STATE";
            public const string EntrypointRecentsState = "METAWAND_RECENTS_STATE";

            public const string TargetLearnMoreButton = "learn_more_about_meta_accounts_button";
            public const string TargetLoginButton = "login_existing_meta_account_button";
            public const string TargetLoadingResultsPanel = "metawand_loading_results_panel";
            public const string TargetStartPanel = "metawand_start_panel";
            public const string TargetDismissButton = "metawand_dismiss_button";
            public const string TargetRetryButton = "metawand_retry_button";
            public const string TargetPreviewPanel = "metawand_preview_panel";
            public const string TargetAddToSceneButton = "add_to_scene_button";
            public const string TargetResultTile = "metawand_result_tile";
            public const string TargetTryPreviewAgainButton = "metawand_preview_generation_error_try_new_prompt_button";
            public const string TargetBannedUserPannel = "metawand_banned_user_panel";
            public const string TargetBannedUserReapplyButton = "metawand_banned_user_reapply_button";
            public const string TargetRecentsPanel = "metawand_recents_panel";
            public const string TargetRecentChat = "metawand_recent_chat";

            public const string ParamInputText = "metawand_input_text";
            public const string ParamShowLibraryResults = "show_results_meta_3d_asset_library";
            public const string Param3dModelDetail = "3d_model_detail_size";
            public const string ParamIsPregenResult = "is_pregen_result";
            public const string ParamNumSuccessTiles = "num_success_tiles";
            public const string ParamNumErrorTiles = "num_error_tiles";
            public const string ParamAssetId = "Metawand_asset_id";
            public const string ParamSessionId = "metawand_session_id";
        }
    }
}
