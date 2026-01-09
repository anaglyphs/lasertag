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
using System.Threading.Tasks;
using Meta.XR.Editor.RemoteContent;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class RemoteJsonContent<T> where T : struct
    {
        private RemoteJsonContent()
        {

        }

        public static async Task<DownloadResult<T>> Create(string fileName, ulong contentId, TimeSpan? cacheDuration = null)
        {
            var downloader = new RemoteJsonContentDownloader(fileName, contentId)
                .WithCacheDuration(cacheDuration ?? TimeSpan.FromDays(1))
                .WithoutMediaTypeValidation();

            var result = await downloader.Fetch();

            if (!result.IsSuccess)
            {
                return DownloadResult<T>.Failure(result.ErrorMessage);
            }

            try
            {
                var content = JsonUtility.FromJson<T>(result.Content);
                return DownloadResult<T>.Success(content, result.FileName);
            }
            catch (Exception e)
            {
                return DownloadResult<T>.Failure(e.Message);
            }
        }
    }
}
