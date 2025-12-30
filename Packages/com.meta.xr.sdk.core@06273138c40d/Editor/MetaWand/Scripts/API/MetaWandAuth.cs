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

using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Meta.XR.MetaWand.Editor.API
{
    internal static class MetaWandAuth
    {
        public record ProfileData
        {
            public ulong ProfileId;
            public string AccessToken;
            public bool IsValid;

            public static readonly ProfileData Invalid = new() { IsValid = false };
        }

        private static ProfileData _cachedProfileData;

        public static ProfileData Data
        {
            get
            {
                if (_cachedProfileData != null)
                {
                    return _cachedProfileData;
                }

                const int maxLength = 1024;
                var textPtr = Marshal.AllocHGlobal(sizeof(byte) * maxLength);
                var result = OVRPlugin.MetaWandAuth.GetAccessToken(textPtr, maxLength);

                if (result != OVRPlugin.Result.Success)
                {
                    Marshal.FreeHGlobal(textPtr);
                    return ProfileData.Invalid;
                }

                var data = Marshal.PtrToStringAnsi(textPtr);
                Marshal.FreeHGlobal(textPtr);

                if (string.IsNullOrEmpty(data))
                {
                    return ProfileData.Invalid;
                }

                var parts = data.Split(':');
                if (parts.Length < 2)
                {
                    return ProfileData.Invalid;
                }

                if (!ulong.TryParse(parts[0], out var profileId))
                {
                    return ProfileData.Invalid;
                }

                _cachedProfileData = new ProfileData
                {
                    ProfileId = profileId,
                    AccessToken = parts[1],
                    IsValid = true
                };
                return _cachedProfileData;
            }
        }

        public static bool IsLoggedIn => Data.IsValid;

        public static bool IsAuthenticating => OVRPlugin.MetaWandAuth.IsAuthenticating() == OVRPlugin.Result.Success;

        public static async Task<bool> Authenticate()
        {
            return await Task.Run(() =>
            {
                var result = OVRPlugin.MetaWandAuth.Authenticate();
                if (result == OVRPlugin.Result.Success)
                {
                    _cachedProfileData = null;
                }
                return result == OVRPlugin.Result.Success;
            });
        }

        public static void Stop() => OVRPlugin.MetaWandAuth.Stop();

        public static async Task<bool> Logout()
        {
            return await Task.Run(() =>
            {
                var result = OVRPlugin.MetaWandAuth.Logout();
                if (result == OVRPlugin.Result.Success)
                {
                    _cachedProfileData = ProfileData.Invalid;
                }
                return result == OVRPlugin.Result.Success;
            });
        }
    }
}
