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

#if UNITY_6000_3_OR_NEWER
#define USE_MAINTOOLBAR
#endif

using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.Editor.PlayCompanion
{
    /// <summary>
    /// Manages the Meta XR toolbar buttons that appear next to Unity's Play Mode controls.
    /// Supports both Unity 6.3+ MainToolbarElement API and pre-Unity 6.3 reflection-based approach.
    /// Dynamically manages multiple toggle buttons for play mode companions like XR Simulator.
    /// </summary>
    [InitializeOnLoad]
    [Reflection]
    internal static class Toolbar
    {
#if !USE_MAINTOOLBAR
        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbar")]
        internal static readonly TypeHandle ToolbarType = new();

        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbar", Name = "m_Root")]
        internal static readonly FieldInfoHandle<VisualElement> Root = new();
#else
        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbars.MainToolbar", Name = "SetDisplayedAll")]
        private static readonly StaticMethodInfoHandleWithWrapperAction<string, bool> SetDisplayedAll = new();

        private const string MainToolbarPath = "MetaXR/PlayCompanion";
        private const string ContainerClass = "metaxr-playcompanion-container";
        private const string ContainerName = "MetaXRPlayCompanion";
        private const string UnityOverlayId = "unity-overlay";
        private const string OverlayContentId = "overlay-content";
        private const string OverlayToolbarTypeName = "OverlayToolbar";
        private const string UnityToolbarOverlayClass = "unity-toolbar-overlay";
        private const string UnityEditorToolbarButtonStripClass = "unity-editor-toolbar__button-strip";
        private static VisualElement _container;

        /// <summary>
        /// StyleSheet for the toolbar buttons.
        /// </summary>
        private static StyleSheet _styleSheet;
#endif

        private const string StripElementClass = "unity-editor-toolbar__button-strip-element";
        private const string StripElementLeftClass = "unity-editor-toolbar__button-strip-element--left";
        private const string StripElementRightClass = "unity-editor-toolbar__button-strip-element--right";
        private const string StripElementMiddleClass = "unity-editor-toolbar__button-strip-element--middle";
        private const string PlayModeGroupId = "PlayMode";
        private const string UnityBaseFieldClass = "unity-base-field";
        private const string UnityBaseFieldNoLabelClass = "unity-base-field--no-label";
        private const string UnityToolbarToggleClass = "unity-toolbar-toggle";
        private const string UnityEditorToolbarToggleClass = "unity-editor-toolbar-toggle";
        private const string UnityEditorToolbarElementClass = "unity-editor-toolbar-element";

        private const string ToolbarTooltip =
#if UNITY_2022_2_OR_NEWER
            "Meta XR Toolbar\n<i>Additional settings available in Edit > Preferences > Meta XR</i>";
#else
            "Meta XR Toolbar\nAdditional settings available in Edit > Preferences > Meta XR";
#endif

        internal static readonly VisualElement DummyOffset;
        internal static readonly VisualElement MarginOffset;
        internal static readonly EditorToolbarButton MetaIcon;

        internal static readonly HashSet<Item> Items = new();
        internal static readonly List<(Item, VisualElement)> Buttons = new();
        internal static bool Enabled { get; set; }

        private static Object _toolbar;
        private static VisualElement _parent;

#if USE_MAINTOOLBAR
        private static MainToolbarElement _mainToolbarElement;

        [MainToolbarElement(MainToolbarPath,
            defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement CreatePlayCompanionContainer()
        {
            return null;
        }

        private static List<VisualElement> FindAllEditorPanelRoots()
        {
            var panelRoots = new List<VisualElement>();

            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();

            foreach (var window in allWindows)
            {
                if (window == null || window.rootVisualElement == null) continue;

                var element = window.rootVisualElement;
                while (element.parent != null)
                {
                    element = element.parent;
                }

                if (element.GetType().Name == "EditorPanelRootElement")
                {
                    if (!panelRoots.Contains(element))
                    {
                        panelRoots.Add(element);
                    }
                }
            }

            return panelRoots;
        }

        private static VisualElement FindContainer()
        {
            // Return cached container if still valid
            if (_container is { parent: { } })
            {
                return _container;
            }

            // Try to find by custom class first
            var panels = FindAllEditorPanelRoots();
            _container = panels
                .Select(panel => panel.Q<VisualElement>(className: ContainerClass))
                .FirstOrDefault(element => element != null);

            if (_container != null)
            {
                return _container;
            }

            // Search for our overlay's OverlayToolbar that Unity creates for us
            foreach (var panel in panels)
            {
                var overlays = panel.Query<VisualElement>().ToList();

                foreach (var overlay in overlays)
                {
                    if (overlay.name == null || !overlay.name.Contains(MainToolbarPath)) continue;

                    var unityOverlay = overlay.Q(UnityOverlayId);
                    var overlayContent = unityOverlay?.Q(OverlayContentId);

                    if (overlayContent == null) continue;

                    var overlayToolbar = overlayContent.Children()
                        .FirstOrDefault(c => c.GetType().Name == OverlayToolbarTypeName ||
                                            c.ClassListContains(UnityToolbarOverlayClass));

                    if (overlayToolbar == null) continue;

                    if (!overlayToolbar.ClassListContains(UnityEditorToolbarButtonStripClass))
                    {
                        overlayToolbar.AddToClassList(UnityEditorToolbarButtonStripClass);
                    }

                    overlayToolbar.name = ContainerName;

                    // Load and apply the custom stylesheet
                    LoadAndApplyStyleSheet(overlayToolbar);

                    _container = overlayToolbar;
                    return _container;
                }
            }

            return _container;
        }

        /// <summary>
        /// Loads and applies the custom USS stylesheet to the toolbar container.
        /// </summary>
        /// <param name="container">The container element to apply the stylesheet to.</param>
        private static void LoadAndApplyStyleSheet(VisualElement container)
        {
            if (_styleSheet == null)
            {
                // Find the stylesheet in the project
                var guids = AssetDatabase.FindAssets("t:StyleSheet Toolbar");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith("PlayCompanion/Toolbar.uss"))
                    {
                        _styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                        break;
                    }
                }

                if (_styleSheet == null)
                {
                    return;
                }
            }

            if (!container.styleSheets.Contains(_styleSheet))
            {
                container.styleSheets.Add(_styleSheet);
            }
        }
#endif

        static Toolbar()
        {
            if (!ShouldRenderEditorUI()) return;

            MetaIcon = new EditorToolbarButton
            {
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
            Styles.Contents.MetaIcon.RegisterToImageLoaded(loadedImage => MetaIcon.icon = loadedImage as Texture2D);
            MetaIcon.AddToClassList(StripElementClass);
            MetaIcon.AddToClassList(StripElementLeftClass);

            if (MetaIcon.Children().FirstOrDefault() is Image image)
            {
                image.tintColor = UserInterface.Styles.Colors.UnselectedWhite;
            }

            MarginOffset = new VisualElement
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

            DummyOffset = new VisualElement
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
            if (!Manager.Enabled.Value)
            {
                if (Enabled)
                {
                    Disable();
                }
                return;
            }

            if (!Enabled)
            {
                Enable();
            }

            if (Buttons.Count == 0) return;

            var shouldBeEnabled = !EditorApplication.isPlayingOrWillChangePlaymode;

            foreach (var button in Buttons)
            {
                button.Item2.SetEnabled(shouldBeEnabled);
                UpdateButton(button);
            }
        }

        private static VisualElement FetchParent()
        {
#if USE_MAINTOOLBAR
            SetDisplayedAll.Invoke?.Invoke(MainToolbarPath, true);
            return FindContainer();
#else
            if (_toolbar == null)
            {
                var toolbars = Resources.FindObjectsOfTypeAll(ToolbarType.Target);
                _toolbar = toolbars.FirstOrDefault();
            }

            if (_toolbar != null)
            {
                var root = Root.Get(_toolbar);
                var toolbarZone = root?.Q(PlayModeGroupId);
                return toolbarZone?.Children().FirstOrDefault();
            }

            return null;
#endif
        }

        private static void Enable()
        {
            var parent = FetchParent();
            if (parent == null)
            {
                return;
            }

            if (_parent == parent && Enabled)
            {
                return;
            }

            Disable();

            _parent = parent;

            var hasButtons = Manager.RegisteredItems.Any(item => item.Show);
            if (hasButtons && _parent != null)
            {
                _parent.Add(MetaIcon);
                RefreshButtons();
            }

            Enabled = true;
        }

        private static void Disable()
        {
            if (!Enabled) return;

            Buttons.ForEach(button => button.Item2.RemoveFromHierarchy());
            Buttons.Clear();
            Items.Clear();

            MetaIcon.RemoveFromHierarchy();

            Enabled = false;
        }

        private static void Insert(VisualElement visualElement)
        {
            if (visualElement == null) return;

            var parent = MetaIcon?.parent;
            if (parent == null) return;

            parent.Add(visualElement);
        }

        private static void RefreshButtons()
        {
            foreach (var button in Buttons.Where(item => !item.Item1.IsRegistered))
            {
                RemoveButton(button);
            }

            foreach (var item in Manager.RegisteredItems.Where(item => item.Show && !Items.Contains(item)))
            {
                CreateButton(item);
            }

            UpdateButtonPositionClasses();
        }

        private static void UpdateButtonPositionClasses()
        {
            if (Buttons.Count == 0) return;

            if (Buttons.Count == 1)
            {
                var element = Buttons[0].Item2;
                element.RemoveFromClassList(StripElementLeftClass);
                element.RemoveFromClassList(StripElementMiddleClass);
                element.RemoveFromClassList(StripElementRightClass);
                return;
            }

            for (int i = 0; i < Buttons.Count; i++)
            {
                var element = Buttons[i].Item2;

                element.RemoveFromClassList(StripElementLeftClass);
                element.RemoveFromClassList(StripElementMiddleClass);
                element.RemoveFromClassList(StripElementRightClass);

                if (i == 0)
                {
                    element.AddToClassList(StripElementLeftClass);
                }
                else if (i == Buttons.Count - 1)
                {
                    element.AddToClassList(StripElementRightClass);
                }
                else
                {
                    element.AddToClassList(StripElementMiddleClass);
                }
            }
        }

        private static void UpdateButton((Item, VisualElement) button)
        {
            if (button.Item2 is EditorToolbarToggle toggle)
            {
                toggle.SetValueWithoutNotify(button.Item1.IsSelected);
            }
            else
            {
                button.Item2.style.backgroundColor = button.Item1.IsSelected
                    ? new StyleColor(Styles.Colors.SelectedBackground)
                    : new StyleColor(StyleKeyword.Null);
            }

            if (button.Item2.Children().FirstOrDefault() is not Image image) return;

            image.tintColor =
                button.Item1.TintColor?.Invoke() ??
                (button.Item1.IsSelected ?
                    UserInterface.Styles.Colors.SelectedWhite
                    : UserInterface.Styles.Colors.UnselectedWhite);
        }

        private static void CreateButton(Item item)
        {
            VisualElement element;

            if (item.IsButton)
            {
                var button = new EditorToolbarButton
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
                button.clicked += () => item.OnSelect?.Invoke();
                element = button;
            }
            else
            {
                var toggle = new EditorToolbarToggle
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
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != item.IsSelected)
                    {
                        Manager.Toggle(item);
                    }
                });
                element = toggle;
            }

            element.AddToClassList(UnityBaseFieldClass);
            element.AddToClassList(UnityBaseFieldNoLabelClass);
            element.AddToClassList(UnityToolbarToggleClass);
            element.AddToClassList(UnityEditorToolbarToggleClass);
            element.AddToClassList(UnityEditorToolbarElementClass);
            element.AddToClassList(StripElementClass);
            element.style.marginRight = 2;

            Insert(element);

            Buttons.Add((item, element));
            Items.Add(item);
        }

        private static void RemoveButton((Item, VisualElement) button)
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
