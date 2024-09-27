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
using System.Reflection;
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Editor.PlayCompanion
{
    [InitializeOnLoad]
    internal static class Toolbar
    {
        private const string StripElementClass = "unity-editor-toolbar__button-strip-element";
        private const string StripElementLeftClass = "unity-editor-toolbar__button-strip-element--left";
        private const string StripElementRightClass = "unity-editor-toolbar__button-strip-element--right";
        private const string StripElementMiddleClass = "unity-editor-toolbar__button-strip-element--middle";
        private const string PlayModeGroupId = "PlayMode";

        private const string ToolbarTooltip =
#if UNITY_2022_2_OR_NEWER
            "Meta XR Toolbar\n<i>Additional settings available in Edit > Preferences > Meta XR</i>";
#else
            "Meta XR Toolbar\nAdditional settings available in Edit > Preferences > Meta XR";
#endif

        internal static readonly Type ToolbarType;
        internal static readonly FieldInfo Root;
        internal static readonly VisualElement DummyOffset;
        internal static readonly VisualElement MarginOffset;
        internal static readonly VisualElement MetaIcon;

        internal static readonly HashSet<Item> Items = new();
        internal static readonly List<(Item, EditorToolbarButton)> Buttons = new();
        internal static bool Enabled { get; set; }

        private static VisualElement _parent;

        static Toolbar()
        {
            if (!Utils.IsMainEditor()) return;

            var editorAssembly = typeof(UnityEditor.Editor).Assembly;
            const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            ToolbarType = editorAssembly.GetType("UnityEditor.Toolbar");
            Root = ToolbarType?.GetField("m_Root", bindingFlags);

            MetaIcon = new EditorToolbarButton()
            {
                icon = Styles.Contents.MetaIcon.Image as Texture2D,
                style =
                {
                    width = Styles.Constants.ButtonWidth,
                    maxWidth = Styles.Constants.ButtonWidth,
                    minWidth = Styles.Constants.ButtonWidth,
                    paddingRight = MiniPadding,
                    paddingLeft = DoubleMargin - MiniPadding,
                    marginRight = 0,
                    backgroundColor = Styles.Colors.ToolbarBackground
                },
                tooltip = ToolbarTooltip
            };
            MetaIcon.AddToClassList(StripElementClass);
            MetaIcon.AddToClassList(StripElementLeftClass);

            if (MetaIcon.Children().FirstOrDefault() is UnityEngine.UIElements.Image image)
            {
                image.tintColor = UserInterface.Styles.Colors.UnselectedWhite;
            }

            MarginOffset = new VisualElement()
            {
                style =
                {
                    marginRight = 0,
                    marginLeft = 0,
                    minWidth = Margin,
                    maxWidth = Margin,
                    width = Margin
                }
            };

            DummyOffset = new VisualElement()
            {
                style =
                {
                    marginRight = 0,
                    marginLeft = 0,
                    minWidth = 0,
                    maxWidth = 0,
                    width = 0
                }
            };

            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!Manager.Enabled.Value && Enabled)
            {
                Disable();
                return;
            }

            if (Manager.Enabled.Value && !Enabled)
            {
                Enable();
            }

            var shouldBeEnabled = !EditorApplication.isPlayingOrWillChangePlaymode;
            Buttons.ForEach(button => button.Item2.SetEnabled(shouldBeEnabled));

            Buttons.ForEach(UpdateButton);
        }

        private static void Enable()
        {
            var unityEditorToolbar = Resources.FindObjectsOfTypeAll(ToolbarType).FirstOrDefault();

            if (unityEditorToolbar != null)
            {
                var root = Root.GetValue(unityEditorToolbar) as VisualElement;
                var toolbarZone = root?.Q(PlayModeGroupId);
                _parent = toolbarZone?.Children().FirstOrDefault();
            }

            var wouldHaveAnyButton = Manager.RegisteredItems.Count(item => item.Show) > 0;
            if (wouldHaveAnyButton)
            {
                _parent?.Add(DummyOffset);
                _parent?.Insert(0, MarginOffset);
                _parent?.Insert(0, MetaIcon);

                RefreshButtons();

                SetDummyOffsetCompensation();
            }

            Enabled = true;
        }

        private static void Disable()
        {
            Buttons.ForEach(button => button.Item2.RemoveFromHierarchy());
            Buttons.Clear();
            Items.Clear();

            DummyOffset.RemoveFromHierarchy();
            MetaIcon.RemoveFromHierarchy();
            MarginOffset.RemoveFromHierarchy();

            Enabled = false;
        }

        private static void Insert(VisualElement visualElement)
        {
            if (visualElement == null) return;

            var parent = MetaIcon?.parent;
            if (parent == null) return;


            var index = parent.IndexOf(MetaIcon) + 1;

            parent.Insert(index, visualElement);

            if (Buttons.Count > 0)
            {
                var previous = parent.Children().ElementAt(index);
                previous.AddToClassList(StripElementClass);
                previous.RemoveFromClassList(StripElementLeftClass);
                if (Buttons.Count != 2)
                {
                    previous.RemoveFromClassList(StripElementRightClass);
                    previous.AddToClassList(StripElementMiddleClass);
                }

                visualElement.AddToClassList(StripElementClass);
                visualElement.AddToClassList(StripElementClass);
                visualElement.AddToClassList(StripElementLeftClass);
            }
        }

        private static void RefreshButtons()
        {
            // Remove Buttons that don't exist
            foreach (var button in Buttons.Where(item => !item.Item1.IsRegistered))
            {
                RemoveButton(button);
            }

            // Add new buttons
            foreach (var item in Manager.RegisteredItems.Where(item => item.Show && !Items.Contains(item)))
            {
                CreateButton(item);
            }
        }

        private static void UpdateButton((Item, EditorToolbarButton) button)
        {
            button.Item2.style.backgroundColor = button.Item1.IsSelected
                ? new StyleColor(Styles.Colors.SelectedBackground)
                : new StyleColor(StyleKeyword.Null);

            if (button.Item2.Children().FirstOrDefault() is not UnityEngine.UIElements.Image image) return;

            image.tintColor = button.Item1.IsSelected ? UserInterface.Styles.Colors.SelectedWhite : UserInterface.Styles.Colors.UnselectedWhite;
        }

        private static void CreateButton(Item item)
        {
            var button = new EditorToolbarButton()
            {
                icon = item.Icon.Image as Texture2D,
                style =
                {
                    width = Styles.Constants.ButtonWidth,
                    maxWidth = Styles.Constants.ButtonWidth,
                    minWidth = Styles.Constants.ButtonWidth,
                    paddingRight = 0,
                    marginRight = 0,
                    marginLeft = 0,
                    paddingLeft = 0
                },
                tooltip = item.Tooltip
            };
            button.clicked += () => Manager.Toggle(item);

            Insert(button);

            Buttons.Add((item, button));
            Items.Add(item);
        }

        private static void RemoveButton((Item, EditorToolbarButton) button)
        {
            button.Item2.RemoveFromHierarchy();

            Buttons.Remove(button);
            Items.Remove(button.Item1);
        }

        private static void SetDummyOffsetCompensation()
        {
            var requiredOffset = (Buttons.Count + Border) * (Styles.Constants.ButtonWidth + Border) + Margin;
            DummyOffset.style.minWidth = requiredOffset;
            DummyOffset.style.maxWidth = requiredOffset;
            DummyOffset.style.width = requiredOffset;
        }
    }
}
