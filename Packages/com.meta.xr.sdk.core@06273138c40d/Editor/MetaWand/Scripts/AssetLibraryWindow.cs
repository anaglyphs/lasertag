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
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.UserInterface.RLDS;
using Meta.XR.MetaWand.Editor.API;
using Meta.XR.MetaWand.Editor.Telemetry;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.RLDS.Styles;
using Button = Meta.XR.Editor.UserInterface.RLDS.Button;

namespace Meta.XR.MetaWand.Editor
{
    [DefaultExecutionOrder(3000)] // To prevent duplicate window on domain reload
    internal class AssetLibraryWindow : EditorWindow, IIdentified, IHasCustomMenu
    {
        private static AssetLibraryWindow _window;
        private static int _largeGridSize;

        private readonly XR.Editor.UserInterface.Utils.Repainter _repainter = new();
        private bool _canFit;
        private Rect _containerRect;

        private Button _loginButton;
        private Rect _miniBannerRect;
        private int _numGenItemPerRow;
        private int _numItemPerRow;
        private Vector2 _scrollPosition = Vector2.zero;
        private Button _searchButton;
        private TextField _searchTextField;
        private bool _sessionIsDirty;
        private bool _showLoginError;
        private UserBool _userHasAccess;

        public static AssetLibraryWindow Window =>
            _window ??= GetWindow<AssetLibraryWindow>(Utils.ToolDescriptorAssetLibrary.Name);

        private Button LoginButton => _loginButton ??=
            new Button(new ActionLinkDescription
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Action = () => OpenLogin(),
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                OriginData = Window,
                Content = new GUIContent("Log into existing Meta account"),
                BackgroundColor = BrightBlue,
                Color = Color.white
            }, Buttons.SecondarySmall, Styles.Constants.ButtonWidth);

        private Button SearchButton => _searchButton ??=
            new Button(new ActionLinkDescription
            {
                Content = new GUIContent("Search"),
                Action = Search
            }, Buttons.SecondarySmall);

        private TextField SearchTextField => _searchTextField ??=
            new TextField("", "", Constants.SearchPlaceholderText, GUILayout.Height(24));

        private string SearchText => SearchTextField.Text.Trim();
        public void SetSearchText(string text) => SearchTextField.Text = text;

        private void OnEnable()
        {
            ResetState();
            _sessionIsDirty = AssetLibrarySession.LoadCachedSession();

            if (ShouldLogIn())
            {
                MetaWandEvent.Send(new MetaWandEvent.Data
                {
                    Name = Constants.Telemetry.EventNamePageImpression,
                    Entrypoint = Constants.Telemetry.EntrypointSignUp,
                    IsEssential = true
                });
            }
            else
            {
                if (_sessionIsDirty)
                    MetaWandEvent.Send(new MetaWandEvent.Data
                    {
                        Name = Constants.Telemetry.EventNamePageImpression,
                        Entrypoint = Constants.Telemetry.EntrypointLoadState,
                        Target = Constants.Telemetry.TargetLoadingResultsPanel,
                        Metadata = new Dictionary<string, string>
                        {
                            { Constants.Telemetry.ParamSessionId, AssetLibrarySession.ActivePrompt?.Id ?? string.Empty }
                        }
                    });
                else
                    MetaWandEvent.Send(new MetaWandEvent.Data
                    {
                        Name = Constants.Telemetry.EventNamePageImpression,
                        Entrypoint = Constants.Telemetry.EntrypointNullState,
                        Target = Constants.Telemetry.TargetStartPanel,
                        Metadata = new Dictionary<string, string>
                        {
                            { Constants.Telemetry.ParamSessionId, AssetLibrarySession.ActivePrompt?.Id ?? string.Empty }
                        }
                    });
            }
        }

        private async void OnDisable()
        {
            await AssetLibrarySession.SaveSession();

            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNameLinkClick,
                Entrypoint = Constants.Telemetry.EntrypointLoadState,
                Target = Constants.Telemetry.TargetDismissButton,
                Metadata = new Dictionary<string, string>
                {
                    { Constants.Telemetry.ParamSessionId, AssetLibrarySession.ActivePrompt?.Id ?? string.Empty }
                }
            });
        }

        private void OnGUI()
        {
            using var _ = new RepainterScope(_repainter);

            if (ShouldLogIn())
            {
                DrawLoginView();
                return;
            }

            if (_sessionIsDirty)
            {
                new AddSpace(Spacing.SpaceXS).Draw();
                DrawMiniBanner();
                DrawSearchArea();
                new Separator(false).Draw();

                var style = new GUIStyle(Divs.PaddingSpaceSM)
                {
                    padding =
                    {
                        top = 0
                    }
                };
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, false, GUIStyle.none,
                    GUI.skin.verticalScrollbar, style);
                var scrollableAreaWidth = Window.position.width - 32;
                DrawSearchResults(AssetLibrarySession.ActivePrompt.ContentPlaceholdersPreGenAssets,
                    scrollableAreaWidth);
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.BeginVertical(Divs.PaddingSpaceSM);
                DrawBanner();
                new AddSpace(Spacing.Space3XS).Draw();
                DrawSearchArea();
                new AddSpace(true).Draw();
                EditorGUILayout.BeginHorizontal();
                new Label(RemoteContent.GetText(Constants.MetaSubtextKey, Constants.MetaSubtextFallback),
                    Styles.GUIStyles.Body2TextTiny).Draw();
                Utils.DrawFlexibleSpace();
                Utils.DrawActionLabel(Constants.FeedbackText, Styles.GUIStyles.BodySmallURL, 110, Utils.OnFeedbackIconClicked);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Logout"), false, () => _ = Logout());
            menu.AddItem(new GUIContent("Clear cache"), false, () =>
            {
                Utils.ClearCache();
                ResetState();
            });
        }

        public string Id => GetType().ToString();

        public static void ShowWindow(Origins origin)
        {
            _window = GetWindow<AssetLibraryWindow>(Utils.ToolDescriptorAssetLibrary.Name);
            _window.minSize = new Vector2(Styles.Constants.MinWidth, Styles.Constants.MinHeight);
            _window.maxSize = new Vector2(Styles.Constants.MaxWidth, Styles.Constants.MaxHeight);
        }

        private void Search()
        {
            if (string.IsNullOrEmpty(SearchText) || SearchText == Constants.SearchPlaceholderText)
                return;

            Utils.ToolDescriptorAssetLibrary.Usage.RecordUsage();

            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNameLinkClick,
                Entrypoint = Constants.Telemetry.EntrypointNullState,
                Target = Constants.Telemetry.TargetSearchButton,
                Metadata = new Dictionary<string, string>
                {
                    { Constants.Telemetry.ParamInputText, SearchText },
                    { Constants.Telemetry.ParamSessionId, AssetLibrarySession.ActivePrompt?.Id ?? string.Empty }
                }
            });

            InitAssetSearch(SearchText);

            GUI.FocusControl(null);
        }

        private void InitAssetSearch(string search)
        {
            AssetLibrarySession.PerformSearch(search);
            _sessionIsDirty = true;
        }

        public static bool ShouldLogIn()
        {
            return !MetaWandAuth.IsLoggedIn;
        }

        private void DrawMiniBanner()
        {
            var rect = EditorGUILayout.BeginVertical(Styles.GUIStyles.SearchAreaBannerMini);
            EditorGUILayout.Space();
            var texture = Styles.Contents.AssetLibraryBannerMini.Image;
            var ratio = texture.width / (float)texture.height;
            Utils.DrawRoundedBackground(rect, Radius.RadiusXS, texture, ratio);
            Utils.DrawRoundedBackground(rect, Radius.RadiusXS, Styles.Colors.TransparentBlack(0.65f).ToTexture());

            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint || rect.width > 0) _miniBannerRect = rect;

            GUILayout.BeginArea(_miniBannerRect);
            new GroupedItem(new List<IUserInterfaceItem>
            {
                new Icon(Styles.Contents.AssetLibraryIcon, Colors.TextPrimary, Styles.GUIStyles.TitleIconMini),
                new Label(Constants.AssetLibraryPublicName, Styles.GUIStyles.TitleMini)
            }).Draw();
            GUILayout.EndArea();
        }

        private void DrawBanner()
        {
            var rect = EditorGUILayout.BeginVertical(Styles.GUIStyles.SearchAreaBanner);
            EditorGUILayout.Space();
            var texture = Styles.Contents.AssetLibraryBanner.Image;
            var ratio = texture.width / (float)texture.height;
            Utils.DrawRoundedBackground(rect, Radius.RadiusXS, texture, ratio);
            Utils.DrawRoundedBackground(rect, Radius.RadiusXS, Styles.Colors.TransparentBlack(0.65f).ToTexture());

            DrawHeader();

            EditorGUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            new GroupedItem(new List<IUserInterfaceItem>
                {
                    new AddSpace(Spacing.Space2XL),
                    new GroupedItem(new List<IUserInterfaceItem>
                    {
                        new AddSpace(true),
                        new Icon(Styles.Contents.AssetLibraryIcon, Colors.TextSecondary, Styles.GUIStyles.HeaderIcon),
                        new AddSpace(true)
                    }, Styles.GUIStyles.HeaderIconContainer),
                    new AddSpace(Spacing.SpaceXS),
                    new Label(Constants.AssetLibraryPublicName, Styles.GUIStyles.Title),
                    new AddSpace(Spacing.SpaceXS),
                    new Label(
                        RemoteContent.GetText(Constants.AssetLibraryMenuDescriptionKey,
                            Constants.AssetLibraryMenuDescription),
                        Styles.GUIStyles.HeaderDescription)
                }, Styles.GUIStyles.HeaderContainerNewState, XR.Editor.UserInterface.Utils.UIItemPlacementType.Vertical)
                .Draw();
        }

        private void DrawSearchArea()
        {
            var style = new GUIStyle(Styles.GUIStyles.PromptAreaContainer);
            if (_sessionIsDirty) style.margin.bottom = Spacing.Space2XS;

            var rect = EditorGUILayout.BeginVertical(style);
            Utils.DrawRoundedBackground(rect, Radius.RadiusXS, Colors.SurfaceSecondaryBackground.ToTexture());

            var e = Event.current;
            const string controlName = "SearchTextField";
            GUI.SetNextControlName(controlName);

            EditorGUI.BeginChangeCheck();
            var focused = GUI.GetNameOfFocusedControl() == controlName;

            // Enter key press
            if (e.type == EventType.KeyDown &&
                e.keyCode is KeyCode.Return or KeyCode.KeypadEnter &&
                focused)
            {
                Search();
                e.Use();
            }

            new AddSpace(Spacing.Space4XS).Draw();
            SearchTextField.Draw();
            new AddSpace(Spacing.Space3XS).Draw();
            SearchButton.Draw();
            EditorGUILayout.EndVertical();
        }

        private void DrawSearchResults(IReadOnlyList<ContentPlaceholder> contentPlaceholders, float contentWidth)
        {
            if (AssetLibrarySession.ActivePrompt.FailedToLoadPreGen)
            {
                new AddSpace(Spacing.SpaceXS).Draw();
                Utils.DrawIconLabel(Styles.Contents.CubeFilledIcon, $"<b>Failed to fetch pre-generated assets</b>");
                new AddSpace(Spacing.SpaceXS).Draw();
                EditorGUILayout.HelpBox(AssetLibrarySession.ActivePrompt.FailedToLoadPreGenErrorMessage, MessageType.Error);
                return;
            }

            if (!contentPlaceholders.Any()) return;

            var style = new GUIStyle(Styles.GUIStyles.ContentContainer)
            {
                fixedWidth = contentWidth
            };
            var rect = EditorGUILayout.BeginVertical(style);
            EditorGUILayout.Space(Spacing.Space4XS);

            if (Event.current.type == EventType.Repaint || rect.width > 0) _containerRect = rect;

            const int minGap = Spacing.SpaceXS;

            if (Event.current.type == EventType.Layout)
            {
                _canFit = _containerRect.width >
                          Styles.Constants.MaxItemPerRow * Styles.Constants.LargeGridItemSize;
                _numItemPerRow = _canFit ? Styles.Constants.MaxItemPerRow : Styles.Constants.MinItemPerRow;
                _largeGridSize =
                    Utils.CalculateGridSizeForWidth(_containerRect, Styles.Constants.LargeGridItemSize,
                        _numItemPerRow,
                        minGap);
            }

            DrawGrid(_numItemPerRow, minGap, _largeGridSize, contentPlaceholders);

            EditorGUILayout.EndVertical();
        }

        private void DrawGrid(int numColumn, int minGap, int gridSize,
            IReadOnlyList<ContentPlaceholder> contentPlaceholders)
        {
            var numRow = Mathf.CeilToInt(contentPlaceholders.Count / (float)numColumn);
            for (var i = 0; i < numRow; i++)
            {
                EditorGUILayout.BeginHorizontal();
                new AddSpace(true).Draw();
                for (var j = 0; j < numColumn; j++)
                {
                    var index = i * numColumn + j;
                    if (index == contentPlaceholders.Count)
                    {
                        new AddSpace(minGap).Draw();
                        break;
                    }

                    contentPlaceholders[index].Draw(gridSize);
                    if (j < numColumn - 1) new AddSpace(minGap).Draw();
                }

                new AddSpace(true).Draw();
                EditorGUILayout.EndHorizontal();
                new AddSpace(minGap).Draw();
            }
        }

        private void DrawLoginView()
        {
            EditorGUILayout.BeginVertical(Divs.PaddingSpaceSM);
            Utils.DrawFlexibleSpace();

            Utils.DrawCenterAligned(new Icon(XR.Editor.UserInterface.Styles.Contents.MetaIconLarge,
                Colors.TextSecondary, string.Empty,
                GUILayout.Width(Styles.Constants.MetaIconSize),
                GUILayout.Height(Styles.Constants.MetaIconSize)));

            new AddSpace(Spacing.SpaceSM).Draw();

            Utils.DrawCenterAligned(new Label("Log into your Meta account", Styles.GUIStyles.BodyLargeCenterAlign));

            new AddSpace(Spacing.Space4XL).Draw();

            Utils.DrawCenterAligned(LoginButton);

            new AddSpace(Spacing.SpaceSM).Draw();

            var learnMetaAccountUrl = RemoteContent.GetText(Constants.LearnMoreUrlKey, Constants.LearnMoreFallback);
            Utils.DrawURLLabel("Learn more about Meta accounts", learnMetaAccountUrl,
                Styles.GUIStyles.BodySmallCenterAlign, () =>
                {
                    MetaWandEvent.Send(new MetaWandEvent.Data
                    {
                        Name = Constants.Telemetry.EventNameLinkClick,
                        Entrypoint = Constants.Telemetry.EntrypointAuthToolbar,
                        Target = Constants.Telemetry.TargetLearnMoreButton,
                        IsEssential = true
                    });
                });

            if (_showLoginError)
            {
                using var _ =
                    new XR.Editor.UserInterface.Utils.ColorScope(XR.Editor.UserInterface.Utils.ColorScope.Scope.Content,
                        Colors.TextNegative);
                new AddSpace(Spacing.SpaceMD).Draw();
                Utils.DrawCenterAligned(new Label("Log in failed. Please try again.",
                    Styles.GUIStyles.BodySmallCenterAlign));
            }

            Utils.DrawFlexibleSpace();

            var helpCenterUrl = RemoteContent.GetText(Constants.HelpCenterUrlKey, Constants.HelpCenterUrlFallback);
            Utils.DrawURLLabel("Help Center", helpCenterUrl, Styles.GUIStyles.BodySmallURL);

            EditorGUILayout.EndVertical();
        }

        private async Task OpenLogin()
        {
            if (MetaWandAuth.IsAuthenticating) return;

            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNameLinkClick,
                Entrypoint = Constants.Telemetry.EntrypointAuthToolbar,
                Target = Constants.Telemetry.TargetLoginButton,
                IsEssential = true
            });

            _showLoginError = false;
            var authTask = MetaWandAuth.Authenticate();

            // Wait until auth process start. Then show status window.
            var timeout = 200; // 2sec
            while (!MetaWandAuth.IsAuthenticating)
            {
                var delta = 10;
                await Task.Delay(delta);
                timeout -= delta;
                if (timeout > 0) continue;

                _showLoginError = true;
                return;
            }

            MetaWandAuthWindow.ShowWindow();
            MetaWandAuthWindow.OnClose += OnCloseAuthWindow;

            var success = await authTask;
            if (!success)
            {
                _showLoginError = true;
                MetaWandAuthWindow.CloseWindow();

                MetaWandEvent.Send(new MetaWandEvent.Data
                {
                    Name = Constants.Telemetry.EventNameLoginFailure,
                    Entrypoint = Constants.Telemetry.EntrypointAuthToolbar,
                    IsEssential = true
                });

                return;
            }

            var result = await AssetLibrarySession.CheckIfUserIsAllowedToUse();
            var access = result switch
            {
                Constants.Success => true,
                Constants.ErrorLimitExceeded => true,
                _ => false
            };
            _userHasAccess.SetValue(access);

            MetaWandEvent.Send(new MetaWandEvent.Data
            {
                Name = Constants.Telemetry.EventNameLoginSuccess,
                Entrypoint = Constants.Telemetry.EntrypointAuthToolbar,
                IsEssential = true
            });

            MetaWandAuthWindow.CloseWindow();
        }

        private void OnCloseAuthWindow()
        {
            // User closed the auth window
            if (MetaWandAuth.IsAuthenticating) MetaWandAuth.Stop();

            ResetState();
        }

        private void ResetState()
        {
            _userHasAccess ??= new UserBool
            {
                Owner = Utils.ToolDescriptorAssetLibrary,
                Uid = "mal_user_access_key",
                Default = false,
                SendTelemetry = false
            };
            _sessionIsDirty = false;
            AssetLibrarySession.SetActivePrompt(null);
            SearchTextField.Text = string.Empty;
        }

        private async Task Logout()
        {
            await MetaWandAuth.Logout();
            _userHasAccess.SetValue(false);
            ResetState();
        }

        private struct RepainterScope : IDisposable
        {
            private static XR.Editor.UserInterface.Utils.Repainter _repainter;

            public RepainterScope(XR.Editor.UserInterface.Utils.Repainter repainter)
            {
                _repainter = repainter;
                _repainter.RequestRepaint();
            }

            public void Dispose()
            {
                _repainter.Assess(_window);
            }
        }
    }
}
