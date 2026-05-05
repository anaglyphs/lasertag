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
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Utils;
using Button = Meta.XR.Editor.UserInterface.Button;
using ScrollView = UnityEngine.UIElements.ScrollView;
using Styles = Meta.XR.Editor.UserInterface.Styles;

namespace Meta.XR.Guides.Editor
{
    internal class GuideWindow : EditorWindow, IIdentified
    {
        public string Id => _populatorId;

        [SerializeField] private string _title = "Meta XR Guide";
        [SerializeField] private string _description = "Description placeholder.";
        private Vector2 _scrollPosition = Vector2.zero;

        public Action OnWindowFocus;
        public Action OnWindowLostFocus;
        public Action OnWindowDraw;
        public Action OnWindowDestroy;
        public Action<Rect> DrawCustomHeaderTitle;
        public Action DrawCustomNotice;
        public Action DrawBefore;
        public Action DrawHeader;
        public Action DrawAfter;
        public Action<OVRPlugin.UnifiedEventData> AddAdditionalUnifiedEventMetadata;

        public VisualElement RootContainer => rootVisualElement;
        public VisualElement ItemContainer { get; private set; }

        private CustomBool _dontShowAgain;

        public CustomBool DontShowAgain => _dontShowAgain ??= new UserBool()
        {
            Owner = this,
            Label = "Don't Show Again",
            Uid = "DontShowAgain",
            SendTelemetry = true,
            Default = false
        };

        private Button _closeButton;
        private Meta.XR.Editor.UserInterface.RLDS.Button _closeButtonRlds;

        public Button CloseButton => _closeButton ??= new Button(new ActionLinkDescription()
        {
            Content = new GUIContent("Close"),
            Action = Close,
            ActionData = this,
            Origin = Origins.GuidedSetup,
            OriginData = this,
            Id = "CloseButton"
        }, GUILayout.MinWidth(192.0f));

        public Meta.XR.Editor.UserInterface.RLDS.Button CloseButtonRLDS => _closeButtonRlds ??= new(new ActionLinkDescription
        {
            Content = new GUIContent("Close"),
            Action = Close,
            ActionData = this,
            Origin = Origins.GuidedSetup,
            OriginData = this,
            Id = "CloseButton"
        }, Props.ButtonVariant.Primary, Props.ButtonSize.XSmall);

        [SerializeField] private GuideOptions _guideOptions;
        [SerializeField] private string _populatorId;

        private List<IUserInterfaceItem> Items { get; set; }

        [Serializable]
        public struct GuideOptions
        {
            public bool ShowCloseButton;
            public bool ShowDontShowAgainOption;
            public bool InvertDontShowAgain;
            public GUIContent OverrideDontShowAgainContent;
            public int MinWindowWidth;
            public int MaxWindowWidth;
            public int MinWindowHeight;
            public int MaxWindowHeight;
            public TextureContent HeaderImage;
            public int HeaderHeight;
            public int BottomMargin;
            public bool ShowAsUtility;
            public bool UseUIToolkit;

            public GuideOptions(GuideOptions options)
            {
                ShowCloseButton = options.ShowCloseButton;
                ShowDontShowAgainOption = options.ShowDontShowAgainOption;
                InvertDontShowAgain = options.InvertDontShowAgain;
                OverrideDontShowAgainContent = options.OverrideDontShowAgainContent;
                MinWindowWidth = options.MinWindowWidth;
                MaxWindowWidth = options.MaxWindowWidth;
                MinWindowHeight = options.MinWindowHeight;
                MaxWindowHeight = options.MaxWindowHeight;
                HeaderImage = options.HeaderImage;
                HeaderHeight = options.HeaderHeight;
                BottomMargin = options.BottomMargin;
                ShowAsUtility = options.ShowAsUtility;
                UseUIToolkit = options.UseUIToolkit;
            }
        }

        public static readonly GuideOptions DefaultOptions = new()
        {
            ShowCloseButton = true,
            ShowDontShowAgainOption = true,
            InvertDontShowAgain = false,
            OverrideDontShowAgainContent = null,
            MinWindowWidth = GuideStyles.Constants.DefaultWidth,
            MaxWindowWidth = GuideStyles.Constants.DefaultWidth,
            MinWindowHeight = GuideStyles.Constants.DefaultHeight,
            MaxWindowHeight = GuideStyles.Constants.DefaultHeight,
            HeaderHeight = GuideStyles.Constants.DefaultHeaderHeight,
            BottomMargin = LargeMargin - Margin,
            ShowAsUtility = false,
            UseUIToolkit = false
        };

        public void Setup(string title, string description, IIdentified populator, GuideOptions guideOptions)
        {
            _guideOptions = guideOptions;
            _populatorId = populator.Id;
            SetupWindow(title, description);
        }

        private void SetupWindow(
            string windowTitle,
            string description)
        {
            _title = windowTitle;
            _description = description;
            name = windowTitle;
            titleContent = new GUIContent(windowTitle);
            minSize = new Vector2(_guideOptions.MinWindowWidth, _guideOptions.MinWindowHeight);
            maxSize = new Vector2(_guideOptions.MaxWindowWidth, _guideOptions.MaxWindowHeight);
        }

        internal void Show(Origins origin, bool ignoreDontShowAgainFlag = false)
        {
            if (Application.isBatchMode || hasFocus) return;

            if (ignoreDontShowAgainFlag || !DontShowAgain.Value)
            {
                OnOpen(origin);
                if (_guideOptions.ShowAsUtility)
                {
                    ShowUtility();
                }
                else
                {
                    base.Show();
                }

                if (docked)
                {
                    ShowTab();
                }
                else
                {
                    // Ensure Guide Window is centered (for better visibility and avoiding the risk of it
                    // being somewhere else (other screen or in a corner)
                    this.CenterWindow();
                }

                if (ignoreDontShowAgainFlag)
                {
                    base.Focus();
                }
            }
        }

        private void Awake()
        {
            Guide.NotifyWindowAwake(_populatorId, this);
        }

        private void OnEnable()
        {
            // Restore titleContent from serialized _title field.
            // titleContent is not serialized by Unity, so we need to restore it manually
            // after deserialization (e.g., after domain reload or panel maximize/restore).
            if (!string.IsNullOrEmpty(_title))
            {
                titleContent = new GUIContent(_title);
            }
        }


        private void InitGuideItems(bool forceUpdate = false)
        {
            // Initialization, only once
            if (Items != null && !forceUpdate)
            {
                return;
            }

            // Search for the bespoke initialization method and call it
            GuideProcessor.InitializeWindow(_populatorId, this);

            // Further initialize
            DrawHeader ??= DrawDefaultHeader;

            // Search for the bespoke items
            Items = GuideProcessor.GetItems(_populatorId);

            // If initialization failed, give up and close
            if (Items == null)
            {
                Close();
                return;
            }
        }

        private void CreateGUI()
        {
            if (!_guideOptions.UseUIToolkit)
            {
                return;
            }

            var lightMode = !EditorGUIUtility.isProSkin;
            var root = rootVisualElement;
            var styleSheet = RLDSUtils.LoadStyleSheet(lightMode);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            root.AddToClassList(Props.Surface.Primary);

            // At this point Unity.EditorStyles is not initialized yet, hence delay execution.
            root.schedule.Execute(() =>
            {
                InitGuideItems();

                DrawBefore?.Invoke();

                var scrollview = new ScrollView(ScrollViewMode.Vertical);
                scrollview.AddToClassList(Props.Utilities.NoMargin);
                root.Add(scrollview);

                if (DrawHeader == DrawDefaultHeader)
                {
                    root.Add(new IMGUIContainer(DrawHeader));
                }
                else
                {
                    DrawHeader?.Invoke();
                }

                ItemContainer = new VisualElement();
                ItemContainer.AddToClassList(Props.Flexbox.Grow1);
                ItemContainer.AddToClassList(Props.Utilities.MarginTopXS);
                ItemContainer.AddToClassList(Props.Utilities.Padding2xMD);
                root.Add(ItemContainer);

                foreach (var item in Items)
                {
                    if (item == null || item.Hide) continue;
                    ItemContainer.Add(item.Get());
                }

                // Footer
                ItemContainer.Add(new AddSpace(true).Get());
                ItemContainer.Add(DrawFootersVisualElement());

                DrawAfter?.Invoke();
            });

        }

        internal void OnGUI()
        {
            UpdateMinimumSize();

            if (_guideOptions.UseUIToolkit)
            {
                return;
            }

            InitGuideItems();

            DrawBefore?.Invoke();

            // Scroll View
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, false, false, GUIStyle.none,
                GUI.skin.verticalScrollbar, Styles.GUIStyles.NoMargin);

            // Header
            DrawHeader?.Invoke();

            EditorGUILayout.BeginVertical(UIStyles.GUIStyles.ContentPadding);

            // Content
            XRGuideBeginVertical();
            BeforeItemDraw();

            foreach (var item in Items)
            {
                if (item == null || item.Hide) continue;
                item.Draw();
            }

            AfterItemDraw();
            XRGuideEndVertical();

            GUILayout.FlexibleSpace();

            // Footers
            DrawFooters();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();

            // Draw After
            DrawAfter?.Invoke();

            OnWindowDraw?.Invoke();
        }

        private void UpdateMinimumSize()
        {
            // Only update size when doing a repaint. That's best practice has doing it doing the layout may provide
            // rects that have not been computed yet
            if (Event.current.type != EventType.Repaint) return;

            // When docked, minSize and maxSize have no effect, there is no need to refresh it
            if (docked) return;

            // We'll want to limit min and max widths and heights to screen size when possible
            // Get the screen resolution, but then apply the DPI scaling (that's when there is a scale applied to the
            // resolution (like for Retina display, or recommended scaling by windows for high resolutions)
            var mainViewSize = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height);
            var scale = 96.0f / Screen.dpi; // 96 is the standard DPI, the 100%
            mainViewSize *= scale;
            mainViewSize *= 0.8f; // Some buffer padding

            var idealMinWidth = Mathf.Min(_guideOptions.MinWindowWidth, mainViewSize.x);
            var idealMinHeight = Mathf.Min(_guideOptions.MinWindowHeight, mainViewSize.y);
            var idealMinSize = new Vector2(idealMinWidth, idealMinHeight);
            minSize = idealMinSize;

            var idealMaxWidth = Mathf.Min(_guideOptions.MaxWindowWidth, mainViewSize.x);
            var idealMaxHeight = Mathf.Min(_guideOptions.MaxWindowHeight, mainViewSize.y);
            var idealMaxSize = new Vector2(idealMaxWidth, idealMaxHeight);
            maxSize = idealMaxSize;
        }

        private void OnFocus() => OnWindowFocus?.Invoke();
        private void OnLostFocus() => OnWindowLostFocus?.Invoke();

        private void OnDestroy()
        {
            OnClose();
            OnWindowDestroy?.Invoke();
        }

        private void OnClose()
        {
            var unifiedEvent = new OVRPlugin.UnifiedEventData(XR.Editor.UserInterface.Telemetry.FalcoEventName.PageClose)
            {
                isEssential = OVRPlugin.Bool.False,
                productType = OVRPlugin.ProductType.Editor
            };
            AddFalcoTelemetryMetadata(unifiedEvent, Origins.Self);
            unifiedEvent.Send();

            Meta.XR.Editor.Notifications.Notification.Manager.ReleaseSnooze(this);
        }

        private void OnOpen(Origins origin)
        {
            var unifiedEvent = new OVRPlugin.UnifiedEventData(XR.Editor.UserInterface.Telemetry.FalcoEventName.PageOpen)
            {
                isEssential = OVRPlugin.Bool.True,
                productType = OVRPlugin.ProductType.Editor
            };
            AddFalcoTelemetryMetadata(unifiedEvent, origin);
            unifiedEvent.Send();

            Meta.XR.Editor.Notifications.Notification.Manager.RequestSnooze(this);
        }

        private void AddFalcoTelemetryMetadata(OVRPlugin.UnifiedEventData unifiedEvent, Origins origin)
        {
            unifiedEvent.SetMetadata(XR.Editor.UserInterface.Telemetry.AnnotationType.Origin, origin.ToString());
            unifiedEvent.SetMetadata(XR.Editor.UserInterface.Telemetry.AnnotationType.Action, Origins.GuidedSetup.ToString());
            unifiedEvent.SetMetadata(XR.Editor.UserInterface.Telemetry.AnnotationType.ActionData, Id);
            unifiedEvent.SetMetadata(XR.Editor.UserInterface.Telemetry.AnnotationType.ActionType, GetType().Name);

            // Allow additional metadata to be added by callers
            AddAdditionalUnifiedEventMetadata?.Invoke(unifiedEvent);
        }

        internal void DrawDefaultHeader()
        {
            DrawHeaderImage();
            DrawHeaderTitle();
            DrawNotice();
        }

        internal void DrawHeaderImage()
        {
            var headerImage = _guideOptions.HeaderImage;
            var isHeaderImageValid = headerImage?.Valid ?? false;
            var image = isHeaderImageValid ? headerImage.Image : GuideStyles.Contents.BannerImage.Image;
            var expectedHeight = _guideOptions.HeaderHeight;
            var rect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, expectedHeight);
            GUI.DrawTexture(rect, image, ScaleMode.ScaleAndCrop);
        }

        private void DrawNotice()
        {
            if (DrawCustomNotice != null)
            {
                DrawCustomNotice();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_description, UIStyles.GUIStyles.SubtitleLabel,
                GUILayout.Width(position.width - LargeMargin));
            EditorGUILayout.EndHorizontal();
        }

        internal void DrawHeaderTitle()
        {
            var expectedHeight = _guideOptions.HeaderHeight;
            var headerTitleRect = new Rect(0, 0, EditorGUIUtility.currentViewWidth,
                expectedHeight);
            GUILayout.BeginArea(headerTitleRect);

            if (DrawCustomHeaderTitle != null)
            {
                DrawCustomHeaderTitle(headerTitleRect);
                GUILayout.EndArea();
                return;
            }

            EditorGUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal(UIStyles.GUIStyles.Header);
            using (new ColorScope(ColorScope.Scope.Content, OffWhite))
            {
                EditorGUILayout.LabelField(GuideStyles.Contents.HeaderIcon, UIStyles.GUIStyles.HeaderIconStyle,
                    GUILayout.Width(32.0f),
                    GUILayout.ExpandWidth(false));
            }

            var titleGuiContent = new GUIContent(_title);
            EditorGUILayout.LabelField(titleGuiContent, UIStyles.GUIStyles.HeaderBoldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.EndArea();
        }

        private void DrawFooters()
        {
            if (!_guideOptions.ShowCloseButton && !_guideOptions.ShowDontShowAgainOption) return;

            XRGuideBeginVertical();
            EditorGUILayout.BeginHorizontal();
            if (_guideOptions.ShowDontShowAgainOption)
            {
                DontShowAgainGUI();
            }

            GUILayout.FlexibleSpace();

            if (_guideOptions.ShowCloseButton)
            {
                CloseButton.Draw();
            }

            EditorGUILayout.EndHorizontal();
            XRGuideEndVertical();
        }

        private void DontShowAgainGUI()
        {
            DontShowAgain.DrawForGUI(new CustomBool.DrawOptions()
            {
                origin = Origins.Self,
                originData = this,
                callback = null,
                OnLeft = true,
                Inverted = _guideOptions.InvertDontShowAgain,
                Content = string.IsNullOrEmpty(_guideOptions.OverrideDontShowAgainContent?.text)
                    ? DontShowAgain.Content
                    : _guideOptions.OverrideDontShowAgainContent,
            });
        }

        private VisualElement DrawFootersVisualElement()
        {
            var container = new VisualElement();
            container.AddToClassList(Props.Utilities.MarginTopXS);
            container.AddToClassList(Props.Flexbox.Row);
            container.AddToClassList(Props.Flexbox.AlignCenter);

            if (_guideOptions.ShowDontShowAgainOption)
            {
                container.Add(new IMGUIContainer(DontShowAgainGUI));
            }

            container.Add(new AddSpace(true).Get());
            if (_guideOptions.ShowCloseButton)
            {
                container.Add(CloseButtonRLDS.Get());
            }

            return container;
        }

        /// <summary>
        /// Adds custom GUI elements before drawing <see cref="IUserInterfaceItem"/>(s).
        /// </summary>
        protected virtual void BeforeItemDraw()
        {
        }

        /// <summary>
        /// Adds custom GUI elements after drawing <see cref="IUserInterfaceItem"/>(s).
        /// </summary>
        protected virtual void AfterItemDraw()
        {
        }

        internal Rect XRGuideBeginVertical() => EditorGUILayout.BeginVertical(UIStyles.GUIStyles.ContentMargin);
        internal void XRGuideEndVertical() => EditorGUILayout.EndVertical();
    }
}
