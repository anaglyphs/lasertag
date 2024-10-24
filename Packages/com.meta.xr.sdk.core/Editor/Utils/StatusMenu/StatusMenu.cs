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
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Editor.StatusMenu
{
    internal class StatusMenu : EditorWindow
    {

        private static readonly List<Item> Items = new List<Item>();
        private static StatusMenu _instance;

        public static List<Item> RegisteredItems => Items;

        public static void RegisterItem(Item item)
        {
            Items.Add(item);
            Items.Sort((x, y) => x.Order.CompareTo(y.Order));
        }

        public static Item GetHighestItem()
        {
            foreach (var item in Items)
            {
                var (_, color, showNotification) = item.PillIcon?.Invoke() ?? default;

                if (!showNotification)
                {
                    continue;
                }

                if (color.HasValue)
                {
                    return item;
                }
            }

            return default;
        }

        public static void ShowDropdown(Vector2 position)
        {
            if (_instance != null)
            {
                _instance.Close();
            }

            if (Items.Count == 0)
            {
                return;
            }

            _instance = CreateInstance<StatusMenu>();
            _instance.ShowAsDropDown(new Rect(position, Vector2.zero),
                new Vector2(Styles.Constants.Width, _instance.ComputeHeight()));
            _instance.wantsMouseMove = true;
            _instance.Focus();
        }

        private float ComputeHeight()
        {
            return ItemHeight * Items.Count + 2;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(Styles.GUIStyles.BackgroundAreaStyle);
            {
                foreach (var item in Items)
                {
                    item.Show(Close, false, Item.Origins.StatusMenu);
                }
            }
            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }
        }
    }
}
