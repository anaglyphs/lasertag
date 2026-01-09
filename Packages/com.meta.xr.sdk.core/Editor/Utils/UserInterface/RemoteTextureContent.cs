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
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;

namespace Meta.XR.Editor.UserInterface
{
    internal class RemoteTextureContent : TextureContent
    {
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromDays(7);
        private readonly GUIContent _guiContent;
        private event OnImageLoadedDelegate OnImageLoadedEvent;
        private bool _valid;


        public RemoteTextureContent(string fileName, ulong contentId, Category category, string tooltip = null)
            : base(fileName, category, tooltip)
        {
            if (!IsValidImageFileName(fileName))
            {
                throw new ArgumentException("The provided name is not a valid image file name.", nameof(fileName));
            }

            _guiContent = new GUIContent
            {
                tooltip = Tooltip,
                image = new Texture2D(2, 2)
                {
                    hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy
                }
            };

            _ = FetchImageAsync(fileName, contentId);
        }

        private async Task FetchImageAsync(string fileName, ulong contentId)
        {
            var downloader = new RemoteContent.RemoteBinaryContentDownloader(fileName, contentId)
                .WithCacheDuration(CacheDuration)
                .WithCacheDirectory("remote_textures");

            var result = await downloader.Fetch();

            if (!result.IsSuccess)
            {
                return;
            }

            ((Texture2D)Image).LoadImage(result.Content);
            _valid = true;

            OnImageLoadedEvent?.Invoke(Image);
            OnImageLoadedEvent = null;
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

        private static readonly string[] ValidImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        private static bool IsValidImageFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (!ValidImageExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            return fileName.IndexOfAny(invalidChars) < 0;
        }
    }
}
