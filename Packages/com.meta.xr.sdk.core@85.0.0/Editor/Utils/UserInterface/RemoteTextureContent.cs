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
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class RemoteTextureContent : TextureContent
    {
        public ulong ContentId { get; }
        private readonly GUIContent _guiContent;
        private event OnImageLoadedDelegate OnImageLoadedEvent;
        public static TimeSpan CacheDuration { get; set; } = TimeSpan.FromDays(7);
        private bool _valid;

        private RemoteTextureContent(ulong contentId, Category category, string tooltip = null)
            : base("remoteImage", category, tooltip)
        {
            ContentId = contentId;

            _guiContent = new GUIContent
            {
                tooltip = Tooltip,
                image = new Texture2D(2, 2)
                {
                    hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy
                }
            };
        }

        public static RemoteTextureContent CreateWithAutoDownload(ulong contentId, Category category, string tooltip = null)
        {
            var instance = new RemoteTextureContent(contentId, category, tooltip);
            _ = instance.FetchImageAsync(contentId);
            return instance;
        }

        public static async Task<DownloadResult<RemoteTextureContent>> CreateAsync(ulong contentId, Category category,
            string tooltip = null)
        {
            var instance = new RemoteTextureContent(contentId, category, tooltip);
            var result = await instance.FetchImageAsync(contentId);

            return result.IsSuccess
                ? DownloadResult<RemoteTextureContent>.Success(instance)
                : DownloadResult<RemoteTextureContent>.Failure(result.ErrorMessage);
        }

        private static RemoteBinaryContentDownloader BuildDownloader(ulong contentId)
        {
            return new RemoteBinaryContentDownloader(contentId)
                .WithCacheDuration(CacheDuration)
                .WithCacheDirectory("remote_textures");
        }

        private async Task<DownloadResult<byte[]>> FetchImageAsync(ulong contentId)
        {
            var downloader = BuildDownloader(contentId);

            var result = await downloader.Fetch();

            if (!result.IsSuccess)
            {
                return result;
            }

            ((Texture2D)Image).LoadImage(result.Content);
            _valid = true;

            OnImageLoadedEvent?.Invoke(Image);
            OnImageLoadedEvent = null;

            return result;
        }

        public override void RegisterToImageLoaded(OnImageLoadedDelegate @delegate)
        {
            if (Valid)
            {
                @delegate(Image);
                return;
            }

            OnImageLoadedEvent += @delegate;
        }

        public override Texture Image => _guiContent.image;
        public override bool Valid => _valid;
        public override GUIContent GUIContent => _guiContent;
        public override GUIContent Content => GUIContent;

        public static async Task PreloadRemoteTextures(IEnumerable<ulong> contentIds)
        {
            var downloaders = contentIds
                .Where(id => id != 0)
                .Select(BuildDownloader);

            await RemoteBinaryContentDownloader.PreloadDownloaders(downloaders);
        }
    }
}
