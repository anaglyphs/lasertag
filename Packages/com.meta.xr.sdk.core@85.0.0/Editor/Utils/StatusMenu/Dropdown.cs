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
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Reflection;
using Meta.XR.Editor.Settings;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using Utils = Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.Editor.StatusMenu
{
    [InitializeOnLoad]
    [Reflection]
    internal static class Dropdown
    {
        private const string ElementClass = "unity-editor-toolbar-element";
        private const string Title = "Meta XR Tools";

        private static readonly CustomBool StatusIconEnabled =
            new UserBool()
            {
                Owner = null,
                Uid = "StatusIcon.Enabled",
                Default = true,
                Label = "Shows Meta XR Tools menu",
                Tooltip = "Requires domain reload to refresh",
            };

        private static EditorToolbarButton _editorToolbarButton;
        private static VisualElement _pill;
        private static Vector2 _rawPosition = Vector2.zero;
        private static Color _hoverColor;
        private static readonly StyleColor NullStyle = new StyleColor(StyleKeyword.Null);
        private static readonly Color _invisibleColor = new Color(0, 0, 0, 0);
        private static Color _pillColor = Color.black;

        static Dropdown()
        {
            if (!Utils.ShouldRenderEditorUI()) return;

            if (StatusIconEnabled.Value)
            {
                EditorApplication.update += Initialize;
            }
        }

#if USE_MAINTOOLBAR
        private static MainToolbarButton _mainToolbarButton;

        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbars.MainToolbar", Name = "SetDisplayedAll")]
        private static readonly StaticMethodInfoHandleWithWrapperAction<string, bool> SetDisplayedAll = new();

        private const string MainToolbarPath = "MetaXR/StatusMenu";

        /// <summary>
        /// Creates the toolbar button for Unity 6+ using the MainToolbarElement API.
        /// Called automatically by Unity's toolbar system.
        /// </summary>
        /// <returns>The created MainToolbarElement, or null if the status icon is disabled.</returns>
        [MainToolbarElement(MainToolbarPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        public static MainToolbarElement CreateStatusMenuButton()
        {
            if (!StatusIconEnabled.Value) return null;

            var icon = Styles.Contents.MetaIcon.GUIContent.image as Texture2D;
            var content = new MainToolbarContent(icon) { text = Title };

            _mainToolbarButton = new MainToolbarButton(content, ShowDropdown);

            return _mainToolbarButton;
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
#else
        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbar")]
        private static readonly TypeHandle ToolbarType = new();

        [Reflection(AssemblyTypeReference = typeof(UnityEditor.Editor), TypeName = "UnityEditor.Toolbar", Name = "m_Root")]
        private static readonly FieldInfoHandle<VisualElement> Root = new();

        private const string PlayModeGroupId = "ToolbarZoneLeftAlign";

        private static UnityEngine.Object _toolbar;

        private static VisualElement FetchParent()
        {
            if (_toolbar == null)
            {
                var toolbars = Resources.FindObjectsOfTypeAll(ToolbarType.Target);
                _toolbar = toolbars.FirstOrDefault();
            }

            if (_toolbar != null)
            {
                var root = Root.Get(_toolbar);
                return root?.Q(PlayModeGroupId);
            }

            return null;
        }

        private static void Attach(EditorToolbarButton button)
        {
            if (button == null) return;

            var parent = FetchParent();

            if (button.parent == parent) return;

            parent?.Add(_editorToolbarButton);
        }
#endif

        private static void CustomizeButton(EditorToolbarButton button)
        {
            Styles.Contents.MetaIcon.RegisterToImageLoaded(loadedImage => button.icon = loadedImage as Texture2D);
            button.AddToClassList(ElementClass);
            button.RegisterCallback<GeometryChangedEvent>(evt => RefreshRawPosition());
            button.clicked += () =>
            {
                _hoverColor = button.resolvedStyle.backgroundColor;
                RefreshRawPosition();
                ShowDropdown();
            };

            var arrowIcon = new VisualElement();
            arrowIcon.AddToClassList("unity-icon-arrow");
            arrowIcon.style.marginLeft = Padding;
            button.Add(arrowIcon);

            _pill = new VisualElement
            {
                style =
                {
                    top = 4,
                    width = 6,
                    height = 6,
                    marginRight = Padding,
                    borderBottomLeftRadius = 50,
                    borderBottomRightRadius = 50,
                    borderTopLeftRadius = 50,
                    borderTopRightRadius = 50,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1
                }
            };
            button.Insert(1, _pill);

            if (button.Children().FirstOrDefault() is UnityEngine.UIElements.Image image)
            {
                image.style.marginRight = Margin;
                image.tintColor = UserInterface.Styles.Colors.UnselectedWhite;
            }
        }

        private static void Initialize()
        {
#if USE_MAINTOOLBAR
            if (StatusIconEnabled.Value)
            {
                SetDisplayedAll.Invoke?.Invoke(MainToolbarPath, StatusIconEnabled.Value);
            }

            _editorToolbarButton = FindEditorToolbarButton();
#else
            _editorToolbarButton = new EditorToolbarButton()
            {
                text = Title,
                style =
                {
                    paddingLeft = Margin,
                    paddingRight = Padding
                }
            };

            Attach(_editorToolbarButton);
#endif
            EditorApplication.update -= Initialize;
            if (_editorToolbarButton == null) return;

            CustomizeButton(_editorToolbarButton);
            EditorApplication.update += Update;
        }

        private static void Update()
        {
#if USE_MAINTOOLBAR
            // Check if the button reference is stale (e.g., button was hidden/shown via toolbar menu)
            if (!IsButtonReferenceValid())
            {
                ReinitializeButton();
            }
#endif
            UpdateHoverState();
            UpdatePill();
        }

        private static void UpdateHoverState()
        {
            if (_editorToolbarButton == null) return;

            _editorToolbarButton.style.backgroundColor = StatusMenu.Visible ? _hoverColor : NullStyle;
        }

        private static void UpdatePill()
        {
            if (_pill == null) return;

            var pillColor = ComputePillColor();
            if (pillColor == _pillColor) return;
            _pillColor = pillColor;

            _pill.style.backgroundColor = pillColor;

            var borderColor = pillColor == _invisibleColor ? UserInterface.Styles.Colors.DarkerGray : pillColor;
            _pill.style.borderTopColor = borderColor;
            _pill.style.borderLeftColor = borderColor;
            _pill.style.borderRightColor = borderColor;
            _pill.style.borderBottomColor = borderColor;
        }

        private static Color ComputePillColor()
        {
            var item = StatusMenu.GetHighestItem();
            if (item?.PillIcon == null)
            {
                return _invisibleColor;
            }

            var (_, color, showNotification) = item.PillIcon();
            if (color != null && showNotification)
            {
                return color ?? Color.white;
            }

            return _invisibleColor;
        }

        /// <summary>
        /// Renders the dropdown settings in the user settings GUI.
        /// </summary>
        public static void OnSettingsGUI()
        {
            StatusIconEnabled.DrawForGUI(Origins.UserSettings, null, UnityEditor.EditorUtility.RequestScriptReload);
        }

        private static void RefreshRawPosition()
        {
            _rawPosition = GUIUtility.GUIToScreenPoint(Vector2.zero);
        }

        private static Rect ComputeCurrentRect()
        {
            if (_editorToolbarButton == null) return Rect.zero;

            var position = _rawPosition;

            var parent = _editorToolbarButton as VisualElement;
            while (parent != null)
            {
                position += parent.layout.position;
                parent = parent.parent;
            }

            return new Rect(position, _editorToolbarButton.layout.size);
        }

        internal static void ShowDropdown()
        {
            StatusMenu.ShowDropdown(ComputeCurrentRect());
        }

#if USE_MAINTOOLBAR
        private static bool IsButtonReferenceValid()
        {
            if (_editorToolbarButton == null) return false;

            var element = _editorToolbarButton as VisualElement;
            if (element == null) return false;

            var current = element;
            while (current.parent != null)
            {
                current = current.parent;
            }

            return current.GetType().Name == "EditorPanelRootElement";
        }

        private static EditorToolbarButton FindEditorToolbarButton()
        {
            var panels = FindAllEditorPanelRoots();
            return panels
                .Select(panel => panel.Query<EditorToolbarButton>()
                    .ToList()
                    .FirstOrDefault(b => b.text == Title))
                .FirstOrDefault(button => button != null);
        }

        private static void ReinitializeButton()
        {
            var newButton = FindEditorToolbarButton();

            if (newButton != null && newButton != _editorToolbarButton)
            {
                _editorToolbarButton = newButton;
                CustomizeButton(_editorToolbarButton);
                _pillColor = Color.clear;
            }
        }
#endif
    }
}
