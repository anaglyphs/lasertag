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
using Meta.XR.Editor.UserInterface;

namespace Meta.XR.MetaWand.Editor
{
    [Serializable]
    internal struct RemoteContent
    {
        [Serializable]
        public struct KeyValuePair
        {
            public string key;
            public string value;
        }

        public KeyValuePair[] dictionary;

        private static Dictionary<string, string> Content { get; set; } = new();

        public static void Initialize(Action onComplete)
        {
            RemoteJsonContent<RemoteContent>.Create("mw.json", 24149289421422440).ContinueWith(t =>
            {
                if (!t.Result.IsSuccess) return;

                Content = t.Result.Content.dictionary.ToDictionary(pair => pair.key, pair => pair.value);
                onComplete?.Invoke();
            });
        }

        public static string GetText(string key, string fallback) => Content.GetValueOrDefault(key, fallback);
    }
}
