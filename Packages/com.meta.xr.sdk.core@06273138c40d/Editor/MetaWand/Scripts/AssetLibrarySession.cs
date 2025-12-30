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
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.Editor.Settings;
using Meta.XR.MetaWand.Editor.API;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.MetaWand.Editor
{
    [InitializeOnLoad]
    internal static class AssetLibrarySession
    {
        private static MetaWandApiManager _apiManager;

        private static readonly CustomBool ClearSessionData = new OnlyOncePerSessionBool
        {
            Owner = null,
            Uid = "AssetLibraryClearSessionData",
            SendTelemetry = false
        };

        private static bool SessionIsEmpty => ActivePrompt == null ||
                                       string.IsNullOrEmpty(ActivePrompt.PromptText) ||
                                       !ActivePrompt.Assets.Any();

        static AssetLibrarySession()
        {
            RemoteContent.Initialize(OnRemoteContentReady);

            // Save session before domain reload
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Clear cache on editor start
            if (ClearSessionData.Value)
            {
                Utils.ClearCache();
                return;
            }

            if (!SessionIsEmpty)
            {
                return;
            }

            LoadCachedSession();
        }

        public static Prompt ActivePrompt { get; private set; }
        public static bool RemoteContentReady { get; private set; }

        public static void SetActivePrompt(Prompt prompt) => ActivePrompt = prompt;

        private static void OnRemoteContentReady() => RemoteContentReady = true;

        private static void OnBeforeAssemblyReload() => _ = SaveSession();

        public static void PerformSearch(string searchText)
        {
            if (AssetLibraryWindow.ShouldLogIn()) return;

            _apiManager ??= new MetaWandApiManager(MetaWandAuth.Data.AccessToken);
            var prompt = new Prompt(Guid.NewGuid().ToString(), searchText, _apiManager, true);
            ActivePrompt = prompt;
            ActivePrompt.Selected = true;
        }

        public static async Task SaveSession()
        {
            if (SessionIsEmpty)
            {
                return;
            }

            var data = new SessionData();
            data.Data.Add(new PromptData
            {
                Id = ActivePrompt.Id,
                Prompt = ActivePrompt.PromptText,
                Assets = ActivePrompt.Assets
            });


            await Utils.WriteToCache(data);
        }

        public static bool LoadCachedSession()
        {
            _apiManager ??= new MetaWandApiManager(MetaWandAuth.Data.AccessToken);
            var data = Utils.ReadFromCache<SessionData>();
            if (data == null || data.Data.Count == 0)
            {
                return false;
            }

            foreach (var promptData in data.Data.Where(promptData => promptData.Assets.Count != 0))
            {
                ActivePrompt = new Prompt(promptData, _apiManager, true);
            }
            return true;
        }

        public static async Task<string> CheckIfUserIsAllowedToUse()
        {
            _apiManager = new MetaWandApiManager(MetaWandAuth.Data.AccessToken);
            var result = await _apiManager.CheckUsage();
            if (result.Success) return Constants.Success;

            if (result is { Usage: { error: { error_subcode: not null } } } &&
                Constants.ErrorCodes.TryGetValue(result.Usage.error.error_subcode, out var val))
                return val;

            return Constants.ErrorUnexpectedError;
        }
    }
}
