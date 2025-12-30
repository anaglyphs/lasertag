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
using UnityEditor;
using UnityEngine;
using Button = Meta.XR.Editor.UserInterface.RLDS.Button;

namespace Meta.XR.MetaWand.Editor
{
    internal class MetaWandAuthWindow : EditorWindow
    {
        private static MetaWandAuthWindow _window;
        internal static bool IsActive => _window != null;
        internal static Action OnClose;

        private XR.Editor.UserInterface.Utils.Repainter _repainter;

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

        private void OnGUI()
        {
            _repainter.RequestRepaint();

            new GroupedItem(new List<IUserInterfaceItem>
                {
                    new AddSpace(true),
                    new GroupedItem(new List<IUserInterfaceItem>
                    {
                        new AddSpace(XR.Editor.UserInterface.RLDS.Styles.Spacing.Space5XL),
                        new GroupedItem(new List<IUserInterfaceItem>
                        {
                            new Label("Continue in your browser", Styles.GUIStyles.Heading3),
                            new AddSpace(XR.Editor.UserInterface.RLDS.Styles.Spacing.SpaceMD),
                            new Label("To confirm your Meta login, please continue in the\nbrowser window.",
                                Styles.GUIStyles.Body2SupportingTextNormal)
                        }, XR.Editor.UserInterface.Utils.UIItemPlacementType.Vertical, GUILayout.Width(360)),
                        new GroupedItem(new List<IUserInterfaceItem>
                        {
                            new AddSpace(XR.Editor.UserInterface.RLDS.Styles.Spacing.Space4XL),
                            new AddSpace(XR.Editor.UserInterface.RLDS.Styles.Spacing.Space2XL),
                            Utils.Spinner,
                        }, Styles.GUIStyles.PaddingTop)
                    }),
                    new AddSpace(true),
                    new GroupedItem(new List<IUserInterfaceItem>
                    {
                        new AddSpace(true),
                        new Button(new ActionLinkDescription
                            {
                                Content = new GUIContent("Cancel"),
                                Action = CloseWindow
                            }, XR.Editor.UserInterface.RLDS.Styles.Buttons.SecondaryXSmall,
                            Styles.Constants.ButtonWidthSmall)
                    })
                }, XR.Editor.UserInterface.RLDS.Styles.Divs.PaddingSpaceMD,
                XR.Editor.UserInterface.Utils.UIItemPlacementType.Vertical).Draw();

            _repainter.Assess(this);
        }
    }
}
