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
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Button = Meta.XR.Editor.UserInterface.RLDS.Button;
using Label = Meta.XR.Editor.UserInterface.Label;
using Spinner = Meta.XR.Editor.UserInterface.RLDS.Spinner;

namespace Meta.XR.MetaWand.Editor
{
    internal class MetaWandAuthWindow : EditorWindow
    {
        private static MetaWandAuthWindow _window;
        internal static bool IsActive => _window != null;
        internal static Action OnClose;

        private XR.Editor.UserInterface.Utils.Repainter _repainter;
        private StyleSheet _currentStyleSheet;

        internal static void ShowWindow()
        {
            if (_window != null)
            {
                _window.Focus();
                return;
            }

            _window = CreateInstance<MetaWandAuthWindow>();
            _window.Init();
            _window.ShowUtility();
        }

        private void Init()
        {
            _window.titleContent.text = "Meta Horizon";
            _window.minSize = new Vector2(Styles.Constants.AuthWindowMinWidth, Styles.Constants.AuthWindowMaxWidth);
            _window.maxSize = new Vector2(Styles.Constants.AuthWindowMinWidth, Styles.Constants.AuthWindowMaxWidth);
        }

        internal static void CloseWindow()
        {
            OnClose?.Invoke();
            _window.Close();
        }

        private void OnEnable()
        {
            if (_window == null)
            {
                _window = this;
            }

            _repainter = new();
        }

        private void OnDestroy() => OnClose?.Invoke();

        public void CreateGUI()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                BuildUI(rootVisualElement);
            });
        }

        private void BuildUI(VisualElement root)
        {
            root.Clear();
            LoadStyleSheet();
            root.AddToClassList(Props.Surface.Secondary);

            var closeButton = new GroupedItem(XR.Editor.UserInterface.Utils.UIItemPlacementType.Horizontal,
                new List<IUserInterfaceItem>
                {
                    new AddSpace(true),
                    new Button(new ActionLinkDescription
                    {
                        Content = new GUIContent("Cancel"),
                        Action = CloseWindow
                    }, Props.ButtonVariant.Secondary, Props.ButtonSize.Small)
                }, Props.Flexbox.AlignEnd);
            closeButton.Get().AddToClassList(Props.Flexbox.SelfStretch);

            var element = new GroupedItem(XR.Editor.UserInterface.Utils.UIItemPlacementType.Vertical,
                new List<IUserInterfaceItem>
                {
                    new AddSpace(true),
                    new GroupedItem(XR.Editor.UserInterface.Utils.UIItemPlacementType.Horizontal,
                        new List<IUserInterfaceItem>
                        {
                            new AddSpace(XR.Editor.UserInterface.RLDS.Styles.Spacing.Space5XL,
                                AddSpace.SpaceDirection.Horizontal),
                            new GroupedItem(XR.Editor.UserInterface.Utils.UIItemPlacementType.Vertical,
                                new List<IUserInterfaceItem>
                                {
                                    new Label("Continue in your browser", Props.Typography.Heading3),
                                    new AddSpace(XR.Editor.UserInterface.RLDS.Styles.Spacing.SpaceMD),
                                    new Label("To confirm your Meta login, please continue in the\nbrowser window.",
                                        Props.Typography.Body2SupportingText)
                                }, Props.Flexbox.Grow1),
                            new GroupedItem(XR.Editor.UserInterface.Utils.UIItemPlacementType.Horizontal,
                                new List<IUserInterfaceItem>
                            {
                                new Spinner(RingSize.Size24, RingColor.Disabled, 720f,
                                    Props.Flexbox.SelfCenter)
                            }, Props.Flexbox.Grow1)
                        }, Props.Flexbox.Grow1, Props.Flexbox.AlignCenter),
                    new AddSpace(true),
                    closeButton
                }, Props.Utilities.MarginLG, Props.Flexbox.Grow1).Get();

            root.Add(element);
        }

        private void LoadStyleSheet()
        {
            var root = rootVisualElement;

            if (_currentStyleSheet != null)
            {
                root.styleSheets.Remove(_currentStyleSheet);
            }

            _currentStyleSheet = RLDSUtils.LoadStyleSheet(!EditorGUIUtility.isProSkin);
            if (_currentStyleSheet != null)
            {
                root.styleSheets.Add(_currentStyleSheet);
            }
        }
    }
}
