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
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class ChatBox : IUserInterfaceItem
    {
        private Vector2 _scrollPosition;
        private readonly List<ChatItem> _chatItems = new();
        private readonly Dictionary<string, ChatItem> _chatItemsMap = new();
        private readonly GUILayoutOption[] _options;
        private readonly GUIStyle _style;

        public ChatBox(GUIStyle style = null, params GUILayoutOption[] options)
        {
            _options = options;
            _style = style;
        }

        public void Draw()
        {
            var style = _style ?? GUI.skin.scrollView;
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, false, GUIStyle.none,
                GUI.skin.verticalScrollbar,
                style, _options);
            EditorGUILayout.BeginVertical();
            foreach (var item in _chatItems)
            {
                item.Draw();
                new AddSpace().Draw();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        public void ScrollToBottom() => _scrollPosition.y = float.MaxValue;

        public bool AddOrUpdateChatItem(ChatItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id))
                return false;

            if (_chatItemsMap.ContainsKey(item.Id))
            {
                _chatItemsMap[item.Id] = item;
                var index = _chatItems.FindIndex(ci => ci.Id == item.Id);
                if (index == -1)
                {
                    return false;
                }

                _chatItems[index] = item;
                return true;
            }

            _chatItems.Add(item);
            _chatItemsMap[item.Id] = item;
            return true;
        }

        public ChatItem GetItemById(string id) => _chatItemsMap.GetValueOrDefault(id);

        public void ClearChat()
        {
            _chatItems.Clear();
            _chatItemsMap.Clear();
        }

        public bool Hide { get; set; }
    }
}
