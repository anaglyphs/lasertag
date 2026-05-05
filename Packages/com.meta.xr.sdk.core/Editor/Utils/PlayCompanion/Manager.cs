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
using System.Linq;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using UnityEditor;
using static Meta.XR.Editor.ToolingSupport.ToolDescriptor;

namespace Meta.XR.Editor.PlayCompanion
{
    [InitializeOnLoad]
    internal static class Manager
    {
        private static readonly List<Item> Items = new List<Item>();
        private static readonly HashSet<Item> SelectedItems = new HashSet<Item>();

        public static readonly CustomBool Enabled =
            new UserBool()
            {
                Owner = null,
                Uid = "PlayCompanion.Enabled",
                Default = true,
                Label = "Shows Play Companion toolbar",
                Tooltip = "Requires domain reload to refresh"
            };

        static Manager()
        {
            SetupMenu();

            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;
            OVRUserSettingsProvider.Register("Toolbar", OnSettingsGUI);

            Update();
        }

        public static IReadOnlyList<Item> RegisteredItems => Items;

        public static void RegisterItem(Item item)
        {
            if (Items.Contains(item)) return;

            Items.Add(item);
            Items.Sort((x, y) => x.Order.CompareTo(y.Order));
        }

        public static void UnregisterItem(Item item)
        {
            Items.Remove(item);
            SelectedItems.Remove(item);
        }

        public static bool IsSelected(Item item)
        {
            return SelectedItems.Contains(item);
        }

        public static void Select(Item item, bool automated = false)
        {
            if (!automated && EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (item == null) return;
            if (SelectedItems.Contains(item)) return;

            SelectedItems.Add(item);

            if (!automated)
            {
                item.OnSelect?.Invoke();
            }
        }

        public static void Unselect(Item item, bool automated = false)
        {
            if (item == null) return;
            if (!SelectedItems.Contains(item)) return;

            SelectedItems.Remove(item);

            if (!automated)
            {
                item.OnUnselect?.Invoke();
            }
        }

        public static void Toggle(Item item)
        {
            if (IsSelected(item))
            {
                Unselect(item);
            }
            else
            {
                Select(item);
            }
        }

        private static void Update()
        {
            TestItems();
        }

        private static void TestItems()
        {
            foreach (var item in RegisteredItems)
            {
                if (item.ShouldBeSelected?.Invoke() ?? false)
                {
                    Select(item, true);
                }

                if (item.ShouldBeUnselected?.Invoke() ?? false)
                {
                    Unselect(item, true);
                }
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            switch (stateChange)
            {
                case PlayModeStateChange.ExitingEditMode:
                    foreach (var item in SelectedItems.ToList())
                    {
                        item.OnEnteringPlayMode?.Invoke();
                    }
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    foreach (var item in SelectedItems.ToList())
                    {
                        item.OnExitingPlayMode?.Invoke();
                    }
                    break;
            }
        }

        private static void OnEditorQuitting()
        {
            foreach (var item in SelectedItems.ToList())
            {
                item.OnEditorQuitting?.Invoke();
            }
        }

        private static void SetupMenu()
        {
            var defaultItem = new Item()
            {
                Order = 0,
                Name = "Default Play Mode",
                Icon = Styles.Contents.DefaultPlayModeIcon,
                Color = UserInterface.Styles.Colors.LightGray,
                ShouldBeSelected = () => SelectedItems.Count == 0,
                Show = false
            };
            Manager.RegisterItem(defaultItem);

            var buildItem = new Item()
            {
                Order = 99,
                Name = "Build and Run on headset",
                Icon = Styles.Contents.BuildIcon,
                Color = UserInterface.Styles.Colors.LightGray,
                Show = false
            };
            Manager.RegisterItem(buildItem);
        }

        private static void OnSettingsGUI()
        {
            Enabled.DrawForGUI(Origins.UserSettings, null, UnityEditor.EditorUtility.RequestScriptReload);
        }
    }
}
