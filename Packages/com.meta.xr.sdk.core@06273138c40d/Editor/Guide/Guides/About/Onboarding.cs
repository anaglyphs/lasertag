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
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.Editor.Id;
using Meta.XR.Editor.Settings;
using Meta.XR.Editor.StatusMenu;
using Meta.XR.Editor.UserInterface;
using Meta.XR.Editor.Utils;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Utils;
using static Meta.XR.Guides.Editor.About.Styles;


namespace Meta.XR.Guides.Editor.About
{
    [GuideItems]
    internal class Onboarding : GuidedSetup
    {
        // Nested class that handles optional remote string from a optional remote json
        internal class RemoteString
        {
            [Serializable]
            public struct RemoteContent
            {
                [Serializable]
                public struct KeyValuePair
                {
                    public string key;
                    public string value;
                }

                public KeyValuePair[] dictionary;

                private static Dictionary<string, string> _dictionary;

                public static async void Initialize(Action callback)
                {
                    if (_dictionary != null) return;

                    var result =
                        await RemoteJsonContent<RemoteContent>.Create("onboarding_copy.json", 30362822839999478);
                    if (!result.IsSuccess) return;

                    _dictionary = result.Content.dictionary.ToDictionary(pair => pair.key, pair => pair.value);
                    callback?.Invoke();
                }

                public static string GetText(string key, string fallback)
                {
                    string text;
                    if (_dictionary == null || !_dictionary.TryGetValue(key, out text))
                    {
                        text = fallback;
                    }

                    return text;
                }
            }

            private readonly string _key;
            private readonly string _fallback;

            // Constructor to initialize with variable name and fallback string
            public RemoteString(string variableName, string fallback)
            {
                _key = variableName;
                _fallback = fallback;
            }

            // Implicit conversion from RemoteString to string
            public static implicit operator string(RemoteString remoteString)
            {
                if (remoteString == null)
                    return null;
                return RemoteContent.GetText(remoteString._key, remoteString._fallback);
            }

            // Override ToString() to return the remote text
            public override string ToString()
            {
                return RemoteContent.GetText(_key, _fallback);
            }
        }

        private enum PageId
        {
            Welcome,
            Role,
            Tools,
            Resources,
            ReleaseNotes,
        }

        private UserString _selectedDiscovery = null;

        private UserString SelectedDiscovery => _selectedDiscovery ??= new UserString()
        {
            Default = null,
            Label = "Discovery",
            Uid = "Discovery",
            SendTelemetry = true,
            Owner = this
        };

        private UserString _selectedRole = null;

        private UserString SelectedRole => _selectedRole ??= new UserString()
        {
            Default = null,
            Label = "Role",
            Uid = "Role",
            SendTelemetry = true,
            Owner = this
        };

        private static GuideWindow _window;
        private MultiPage _pageSystem;
        private Icon _metaIcon;

        // Session-based page persistence
        private static readonly SessionInt _currentPageIndex = new()
        {
            Owner = null,
            Uid = "CurrentPageIndex",
            SendTelemetry = false,
            Default = 0
        };

        // For discover page
        private const string CardIdNewToXR = "card_id_newToXR";
        private const string CardIdVeteranToXR = "card_id_veteranToXR";
        private const string CardIdUpdate = "card_id_update";

        // For role page
        private const string CardIdRole1 = "card_id_role1";
        private const string CardIdRole2 = "card_id_role2";
        private const string CardIdRole3 = "card_id_role3";
        private const string CardIdRole4 = "card_id_role4";
        private const string CardIdRole5 = "card_id_role5";

        // Role configuration structure
        private struct RoleConfig
        {
            public string Id;
            public RemoteString Title;

            public RoleConfig(string id, RemoteString title)
            {
                Id = id;
                Title = title;
            }
        }

        // Cached role configurations
        private RoleConfig[] _roleConfigs;

        private RoleConfig[] GetRoleConfigs() => _roleConfigs ??= new RoleConfig[]
        {
            new(CardIdRole1, Role1Title),
            new(CardIdRole2, Role2Title),
            new(CardIdRole3, Role3Title),
            new(CardIdRole4, Role4Title),
            new(CardIdRole5, Role5Title)
        };

        // Resource configuration structure
        public struct ResourceConfig
        {
            public string Id;
            public RemoteString Title;
            public RemoteString Content;
            public RemoteString Url;
            public System.Func<string> DynamicUrl; // For dynamic URLs like UrlApi
            public TextureContent ActionIcon;

            public ResourceConfig(string id, RemoteString title, RemoteString content, RemoteString url,
                TextureContent actionIcon = null)
            {
                Id = id;
                Title = title;
                Content = content;
                Url = url;
                DynamicUrl = null;
                ActionIcon = actionIcon ?? BuildingBlocks.Editor.Styles.Contents.SelectIcon;
            }

            public ResourceConfig(string id, RemoteString title, RemoteString content, System.Func<string> dynamicUrl,
                TextureContent actionIcon = null)
            {
                Id = id;
                Title = title;
                Content = content;
                Url = null;
                DynamicUrl = dynamicUrl;
                ActionIcon = actionIcon ?? BuildingBlocks.Editor.Styles.Contents.SelectIcon;
            }

            public string GetUrl() => DynamicUrl?.Invoke() ?? Url;
        }

        // Cached resource configurations
        private ResourceConfig[] _resourceConfigs;

        public ResourceConfig[] GetResourceConfigs() => _resourceConfigs ??= new ResourceConfig[]
        {
            new(CardIdBuild, BuildTitle, BuildContent, UrlBuildingWithUnity),
            new(CardIdAPI, ApiReferenceTitle, ApiReferenceContent, () => UrlApi), // Dynamic URL for API reference
            new(CardIdSamples, SamplesTitle, SamplesContent, UrlSamples),
            new(CardIdMQDH, MQDHTitle, MQDHContent, UrlMQDH),
            new(CardIdDevDashboard, DeveloperDashboardTitle, DeveloperDashboardContent, UrlDevDashboard),
        };

        // For tools page
        private const string CardIdBB = "card_id_mruk";
        private const string CardIdXrSim = "card_id_xr_sim";
        private const string CardIdID = "card_id_id";

        // For resource page
        private const string CardIdBuild = "card_id_build";
        private const string CardIdAPI = "card_id_api";
        private const string CardIdSamples = "card_id_samples";
        private const string CardIdMQDH = "card_id_mqdh";
        private const string CardIdDevDashboard = "card_id_dev_dashboard";


        #region Remote strings

        public readonly RemoteString UrlBuildingWithUnity =
            new(nameof(UrlBuildingWithUnity), "https://developers.meta.com/horizon/develop/unity");

        public readonly RemoteString UrlSamples =
            new(nameof(UrlSamples), "https://developers.meta.com/horizon/code-samples/unity");

        public readonly RemoteString UrlMQDH =
            new(nameof(UrlMQDH), "https://developers.meta.com/horizon/documentation/unity/ts-mqdh/");

        public readonly RemoteString UrlDevDashboard =
            new(nameof(UrlDevDashboard), "https://developers.meta.com/horizon/manage");

        public readonly RemoteString UrlUpdate =
            new(nameof(UrlUpdate), "https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm");

        public readonly RemoteString UrlInstallXrSimulator =
            new(nameof(UrlInstallXrSimulator), "https://developers.meta.com/horizon/documentation/unity/xrsim-intro");

        // Content strings
        public readonly RemoteString WelcomeHeader =
            new(nameof(WelcomeHeader), "Welcome to Meta XR SDK");

        public readonly RemoteString WelcomeUpdate =
            new(nameof(WelcomeUpdate), "Unlock powerful new features and tools with the latest Meta XR SDK!");

        public readonly RemoteString WelcomeIntro =
            new(nameof(WelcomeIntro), "You're about to embark on an exciting journey into the world" +
                                      " of virtual reality. Whether you're just starting out or you're a" +
                                      " seasoned XR expert, we've tailored paths to suit your experience level." +
                                      " Get started on creating immersive experiences together!");

        public readonly RemoteString WelcomeNewToXRTitle =
            new(nameof(WelcomeNewToXRTitle), "I’m new to XR");

        public readonly RemoteString WelcomeNewToXRContent =
            new(nameof(WelcomeNewToXRContent),
                "Show me the relevant starting resources and tools to get started on the right foot");

        public readonly RemoteString WelcomeVeteranToXRTitle =
            new(nameof(WelcomeVeteranToXRTitle), "I’m experienced to XR");

        public readonly RemoteString WelcomeVeteranToXRContent =
            new(nameof(WelcomeVeteranToXRContent),
                "Show me advanced tools and solutions to get to the path of building quicker");

        public readonly RemoteString WelcomeUpdateLink =
            new(nameof(WelcomeUpdateLink), "See release notes");

        public readonly RemoteString WelcomeDescriptionUptoDate =
            new(nameof(WelcomeDescriptionUptoDate), "• Up to date");

        public readonly RemoteString ReleaseNotesHeader =
            new(nameof(ReleaseNotesHeader), "Release Notes");

        public readonly RemoteString ResourcesHeader =
            new(nameof(ResourcesHeader), "Resources");

        public readonly RemoteString ResourcesDescription =
            new(nameof(ResourcesDescription), "Documentation and external tools to get the most out of Meta XR SDK");

        public readonly RemoteString ResourcesIntro =
            new(nameof(ResourcesIntro), "Here are some resources to troubleshoot any issues, " +
                                        "understand APIs deeper and get further inspiration from reference content.");

        public readonly RemoteString ToolsHeader =
            new(nameof(ToolsHeader), "Tools");

        public readonly RemoteString ToolsDescription =
            new(nameof(ToolsDescription), "Meta XR Tools Menu");

        public readonly RemoteString ToolsIntro =
            new(nameof(ToolsIntro), "The Meta XR Tools menu provides quick access to all the tools" +
                                    " included in the Meta SDK, designed to enhance your development experience." +
                                    " It is conveniently located in the top left corner of your editor.");

        public readonly RemoteString ApiReferenceTitle =
            new(nameof(ApiReferenceTitle), "API Reference");

        public readonly RemoteString ApiReferenceContent =
            new(nameof(ApiReferenceContent), "For detailed reference information on Meta XR SDK classes and methods");

        public readonly RemoteString SamplesTitle =
            new(nameof(SamplesTitle), "Samples and Showcases");

        public readonly RemoteString SamplesContent =
            new(nameof(SamplesContent), "Explore common usages of the Meta XR SDK features with our sample projects");

        public readonly RemoteString BuildTitle =
            new(nameof(BuildTitle), "Building with Unity");

        public readonly RemoteString BuildContent =
            new(nameof(BuildContent),
                "Our documentation hub for building interactive and immersive experiences for Horizon OS with Unity");

        public readonly RemoteString MQDHTitle =
            new(nameof(MQDHTitle), "Meta Quest Developer Hub");

        public readonly RemoteString MQDHContent =
            new(nameof(MQDHContent),
                "Streamline your development workflow with this desktop companion app, featuring device management, performance analysis, and more");

        public readonly RemoteString DeveloperDashboardTitle =
            new(nameof(DeveloperDashboardTitle), "Developer Dashboard");

        public readonly RemoteString DeveloperDashboardContent =
            new(nameof(DeveloperDashboardContent),
                "Publish your app and setup your Meta XR Platform features for the Meta Quest Store");

        public readonly RemoteString BuildingBlocksHeader =
            new(nameof(BuildingBlocksHeader), "Building Blocks");

        public readonly RemoteString BuildingBlocksOpen =
            new(nameof(BuildingBlocksOpen), "Open");

        public readonly RemoteString BuildingBlocksContent =
            new(nameof(BuildingBlocksContent),
                "Start building your project by adding Building Blocks directly into your scene");

        public readonly RemoteString ImmersiveDebuggerHeader =
            new(nameof(ImmersiveDebuggerHeader), "Immersive Debugger");

        public readonly RemoteString ImmersiveDebuggerContent =
            new(nameof(ImmersiveDebuggerContent), "Inspect and tweak your scene in headset");

        public readonly RemoteString ImmersiveDebuggerEnabled =
            new(nameof(ImmersiveDebuggerEnabled), "Enabled");

        public readonly RemoteString ImmersiveDebuggerDisabled =
            new(nameof(ImmersiveDebuggerDisabled), "Disabled");

        public readonly RemoteString ImmersiveDebuggerOpen =
            new(nameof(ImmersiveDebuggerOpen), "Open");

        public readonly RemoteString XrSimulatorHeader =
            new(nameof(XrSimulatorHeader), "Meta XR Simulator");

        public readonly RemoteString XrSimulatorContent =
            new(nameof(XrSimulatorContent), "Iterate quickly without a headset with our OpenXR simulator");

        public readonly RemoteString XrSimulatorStatusDefault =
            new(nameof(XrSimulatorStatusDefault), "Not installed");

        public readonly RemoteString MetaXRSimulatorInstall =
            new(nameof(MetaXRSimulatorInstall), "Install");

        public readonly RemoteString MetaXRSimulatorEnable =
            new(nameof(MetaXRSimulatorEnable), "Enable");

        public readonly RemoteString XrSimulatorEnabled =
            new(nameof(XrSimulatorEnabled), "Enabled");

        public readonly RemoteString XrSimulatorDisabled =
            new(nameof(XrSimulatorDisabled), "Disabled");

        public readonly RemoteString ResourceView =
            new(nameof(ResourceView), "View");

        public readonly RemoteString WelcomeUpdateOpenUPM =
            new(nameof(WelcomeUpdateOpenUPM), "Update");

        // Role page content strings
        public readonly RemoteString RoleHeader =
            new(nameof(RoleHeader), "Welcome to Meta XR SDK");

        public readonly RemoteString RoleIntro =
            new(nameof(RoleIntro),
                "Selecting a role will help us to recommend, as well as grant you early access to new tools / resources.");

        public readonly RemoteString Role1Title =
            new(nameof(Role1Title), "Game Developer");

        public readonly RemoteString Role2Title =
            new(nameof(Role2Title), "Q/A Tester");

        public readonly RemoteString Role3Title =
            new(nameof(Role3Title), "Artist");

        public readonly RemoteString Role4Title =
            new(nameof(Role4Title), "Production Manager");

        public readonly RemoteString Role5Title =
            new(nameof(Role5Title), "Other");
        #endregion

        public string BuildingBlocksStatus =>
            $"{BuildingBlocks.Editor.Utils.FilteredRegistry.Count(data => !data.Hidden)} blocks available!";

        public string ReleaseNotesDescription =>
            $"Meta XR Core SDK • Version {Version}";

        public string WelcomeDescription =>
            $"Version {About.Version}";

        public string WelcomeDescriptionUpdate =>
            "• New version available! ";

        public string WelcomeUpdateTitle =>
            $"Version {About.LatestVersion} is available!";

        public string UrlApi =>
            $"https://developers.meta.com/horizon/reference/unity/v{About.Version}";

        private readonly Dictionary<string, RadioButton> _radioButtonsMap = new();
        private readonly Dictionary<string, Card> _cardsMap = new();

        private readonly Repainter _repainter = new();
        private BulletedLabel _bbStatus;
        private BulletedLabel _xrSimStatus;
        private BulletedLabel _versionStatus;
        private BulletedLabel _idStatus;

        private PageId _currentPage = PageId.Welcome;
        private TextureContent _leftPanelTexture = null;
        private TextureContent _leftPanelLastTexture = null;
        private TextureContent _leftPanelBottomTexture = null;

        private ChangelogFetcher _changelogFetcher;
        private const string PackageName = "meta-xr-core-sdk";
        private Task<bool> _taskChangeLog;

        private Tween _fader;

        private int Version => _changelogFetcher != null && _changelogFetcher.LatestVersion != null
            ? _changelogFetcher.LatestVersion.Major
            : About.LatestVersion != null
                ? About.LatestVersion.Value
                : 0;

        internal override GuideWindow CreateWindow()
        {
            if (_window != null) return _window;

            const int width = Constants.Width;
            const int height = Constants.Height;
            var options = new GuideWindow.GuideOptions(GuideWindow.DefaultOptions)
            {
                ShowCloseButton = false,
                MinWindowWidth = width,
                MaxWindowWidth = width,
                MinWindowHeight = height,
                MaxWindowHeight = height,
                HeaderImage = null,
                ShowDontShowAgainOption = false,
                InvertDontShowAgain = true,
                ShowAsUtility = true,
            };
            _window = Guide.Create(About.ToolDescriptor.Name, null, this, options);
            return _window;
        }

        [Init]
        private void InitializeWindow(GuideWindow window)
        {
            _window = window;
            _window.AddAdditionalTelemetryAnnotations = marker =>
                marker.AddAnnotation(OVRTelemetryConstants.GuidedSetup.AnnotationType.HasNewVersionAvailable,
                    About.Version < About.LatestVersion);
        }


#if USING_META_XR_SIMULATOR
        private void RefreshXrSimulatorStatus()
        {
            if (_xrSimStatus == null) return;

            var activated = Meta.XR.Simulator.Editor.Enabler.Activated;
            var status = activated
                ? UIStyles.ContentStatusType.Success
                : UIStyles.ContentStatusType.Disabled; // Using the new Disabled status type
            var message = activated
                ? XrSimulatorEnabled
                : XrSimulatorDisabled;

            // Set label first, then status to ensure color is applied correctly
            _xrSimStatus.SetLabel(message);
            _xrSimStatus.SetStatus(status);
        }
#endif


        private void RefreshImmersiveDebuggerStatus()
        {
            if (_idStatus == null) return;

            var enabled = Meta.XR.ImmersiveDebugger.Editor.Utils.IsEnabled;
            var message = enabled
                ? ImmersiveDebuggerEnabled
                : ImmersiveDebuggerDisabled;
            var status = enabled
                ? UIStyles.ContentStatusType.Success
                : UIStyles.ContentStatusType.Disabled; // Using the new Disabled status type

            // Set label first, then status to ensure color is applied correctly
            _idStatus.SetLabel(message);
            _idStatus.SetStatus(status);
        }

        private void OnRefresh()
        {
            _cardsMap.Clear();
            _radioButtonsMap.Clear();

            // Refresh card data and user choices
            if (_currentPage != PageId.Welcome)
            {
                BuildWelcomePage();
            }
            RefreshPage(_currentPage);
        }

        private void OnDraw() => _repainter.RequestRepaint();

        private void DrawHeader()
        {
        }

        private void DrawBefore()
        {
            if (_taskChangeLog is { IsCompleted: true } &&
                Event.current.type == EventType.Layout && _taskChangeLog.Result)
            {
                UpdatePagesWithRemoteContent();
                _taskChangeLog = null;
            }

            _repainter.Assess(_window);

            if (_leftPanelTexture != null && _leftPanelTexture != _leftPanelLastTexture)
            {
                _leftPanelBottomTexture = _leftPanelLastTexture;
                _leftPanelLastTexture = _leftPanelTexture;
                ResetFader();
                _fader.Activate();
            }

            _leftPanelTexture = GetPageImage(_currentPage);

#if USING_META_XR_SIMULATOR
            RefreshXrSimulatorStatus();
#endif
            RefreshImmersiveDebuggerStatus();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            DrawLeftPanelTexture();
            EditorGUILayout.EndVertical();
        }

        private void DrawLeftPanelTexture()
        {
            var rect = GUILayoutUtility.GetRect(Constants.LeftPaneWidth, _window.minSize.y);
            if (_leftPanelBottomTexture?.Valid ?? false)
            {
                GUI.DrawTexture(rect, _leftPanelBottomTexture.Image, ScaleMode.ScaleAndCrop);
            }

            if (_leftPanelTexture?.Valid ?? false)
            {
                var color = new Color(1, 1, 1, _fader.Current);
                GUI.DrawTexture(rect, _leftPanelTexture.Image, ScaleMode.ScaleAndCrop,
                    true, 0.0f, color, Vector4.zero, Vector4.zero);
            }
        }

        private TextureContent GetPageImage(PageId currentPage)
        {
            return currentPage switch
            {
                PageId.Welcome => GuideStyles.Contents.ObWelcome,
                PageId.Role => GuideStyles.Contents.ObWelcome,
                PageId.Tools => GuideStyles.Contents.ObTools,
                PageId.Resources => GuideStyles.Contents.ObResources,
                PageId.ReleaseNotes => GuideStyles.Contents.ObRelease,
                _ => GuideStyles.Contents.BannerImage
            };
        }

        private static void DrawAfter() => EditorGUILayout.EndHorizontal();

        private void InitChangeLogFetcher()
        {
            _changelogFetcher = new ChangelogFetcher(PackageName);
            _taskChangeLog = _changelogFetcher.Fetch();
        }

        private void OnPackageListRefreshed()
        {
            RefreshPage(PageId.Welcome);
        }

        private void UpdatePagesWithRemoteContent()
        {
            RefreshPage(PageId.ReleaseNotes);
        }

        private void ResetFader()
        {
            _fader?.Deactivate();
            _fader = Tween.Fetch(this);
            _fader.Reset();
            _fader.Start = 0;
            _fader.Target = 1f;
            _fader.Speed = 12.0f;
            _fader.Epsilon = 0.01f;
        }

        [GuideItems]
        private List<IUserInterfaceItem> GetItems()
        {
            RemoteString.RemoteContent.Initialize(OnRefresh);

            ResetFader();
            _window.OnWindowDraw = OnDraw;
            _window.DrawBefore = DrawBefore;
            _window.DrawHeader = DrawHeader;
            _window.DrawAfter = DrawAfter;

            _metaIcon = new Icon(GuideStyles.Contents.HeaderIcon, Color.white, string.Empty,
                GUILayout.Width(UIStyles.GUIStyles.HeaderIconStyleLarge.fixedWidth),
                GUILayout.Height(UIStyles.GUIStyles.HeaderIconStyleLarge.fixedHeight));

            var pages = new List<Page>()
            {
                new(PageId.Welcome.ToString(), Enumerable.Empty<IUserInterfaceItem>(), UIItemPlacementType.Vertical)
                {
                    HasCompletedActionDelegate = HasCompletedPage,
                    Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.PageGroup,
                },
                new(PageId.Role.ToString(), Enumerable.Empty<IUserInterfaceItem>(), UIItemPlacementType.Vertical)
                {
                    HasCompletedActionDelegate = HasCompletedPage,
                    Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.PageGroup,
                },
                new(PageId.Tools.ToString(), Enumerable.Empty<IUserInterfaceItem>(), UIItemPlacementType.Vertical)
                {
                    HasCompletedActionDelegate = HasCompletedPage,
                },
                new(PageId.Resources.ToString(), Enumerable.Empty<IUserInterfaceItem>(), UIItemPlacementType.Vertical)
                {
                    HasCompletedActionDelegate = HasCompletedPage,
                },
                new(PageId.ReleaseNotes.ToString(), Enumerable.Empty<IUserInterfaceItem>(),
                    UIItemPlacementType.Vertical)
                {
                    HasCompletedActionDelegate = HasCompletedPage,
                },
            };

            InitChangeLogFetcher();

            var options = MultiPage.DefaultOptions;
            options.Height = Constants.PageHeight;
            _pageSystem = new MultiPage(this, pages, options)
            {
                OnPageChangeBegin = OnPageChangeBegin
            };

            // Restore last page if available, otherwise start at 0
            var savedPageIndex = _currentPageIndex.Value;
            if (savedPageIndex > 0 && savedPageIndex < _pageSystem.Pages.Count)
            {
                _pageSystem.JumpToPage(savedPageIndex);
            }
            else
            {
                _pageSystem.JumpToPage(0);
            }

            return new List<IUserInterfaceItem>
            {
                _pageSystem
            };
        }

        private bool HasCompletedPage(Page page)
        {
            if (!Enum.TryParse(page.PageId, out PageId pageId)) return true;
            switch (pageId)
            {
                case PageId.Welcome:
                    return _radioButtonsMap[CardIdNewToXR].State
                           || _radioButtonsMap[CardIdVeteranToXR].State;

                case PageId.Role:
                    var roleConfigs = GetRoleConfigs();
                    return roleConfigs.Any(config =>
                        _radioButtonsMap.ContainsKey(config.Id) && _radioButtonsMap[config.Id].State);

                default:
                    return true;
            }
        }

        private void OnPageChangeBegin(int pageIndex)
        {
            var pages = _pageSystem.Pages;
            if (pageIndex < 0 || pageIndex >= pages.Count ||
                !Enum.TryParse(pages[pageIndex].PageId, out _currentPage))
            {
                // Going past the last page - close first, then open Meta XR Tools menu with delay
                _window.Close();
                EditorApplication.delayCall += () => StatusIcon.ShowDropdown();
                return;
            }

            // Save current page index for persistence
            _currentPageIndex.SetValue(pageIndex);

            _pageSystem.ScrollPosition = new Vector2(_pageSystem.ScrollPosition.x, 0);
            _leftPanelTexture = GetPageImage(_currentPage);

            // Update Page Content
            if (_currentPage != PageId.Welcome && !(_cardsMap.Any() && _radioButtonsMap.Any()))
            {
                BuildWelcomePage();
            }
            pages[pageIndex].Items = RefreshPageContent(_currentPage);

            // Dont Show Again
            if (pageIndex == pages.Count - 1)
            {
                _window.DontShowAgain.SetValue(true);
                About.ToolDescriptor.Usage.RecordUsage();
            }
        }

        private void RefreshPage(PageId pageId)
        {
            var pages = _pageSystem.Pages;
            var page = pages.FirstOrDefault(page => page.PageId == pageId.ToString());
            if (page == null) return;
            page.Items = RefreshPageContent(pageId);
        }

        private List<IUserInterfaceItem> GetPageContent(PageId pageId)
        {
            return pageId switch
            {
                PageId.Welcome => BuildWelcomePage(),
                PageId.Role => BuildRolePage(),
                PageId.Tools => BuildToolsPage(),
                PageId.Resources => BuildResourcesPage(),
                PageId.ReleaseNotes => BuildReleaseNotesContent(),
                _ => null
            };
        }

        private List<IUserInterfaceItem> RefreshPageContent(PageId pageId)
        {
            return GetPageContent(pageId);
        }

        private GroupedItem Header(string title) => new(new List<IUserInterfaceItem>
        {
            _metaIcon,
            new Label(title, GUIStyles.HeaderBoldLabelLarge),
        }, GUIStyles.HeaderTitleContainer);

        private static GroupedItem HeaderSubtitle(List<IUserInterfaceItem> items) =>
            new(items, GUIStyles.HeaderSubtitleContainer);

        private static GroupedItem HeaderSubtitle(string label) =>
            HeaderSubtitle(new List<IUserInterfaceItem> { new Label(label, GUIStyles.HeaderSubtitle) });

        private void OnSelect(string id)
        {
            if (id == null || !_radioButtonsMap.ContainsKey(id) || !_cardsMap.ContainsKey(id))
                return;

            _cardsMap[id].SetSelected(true);
            _radioButtonsMap[id].State = true;

            switch (id)
            {
                case CardIdNewToXR:
                    _cardsMap[CardIdVeteranToXR].SetSelected(false);
                    _radioButtonsMap[CardIdVeteranToXR].State = false;
                    break;
                case CardIdVeteranToXR:
                    _cardsMap[CardIdNewToXR].SetSelected(false);
                    _radioButtonsMap[CardIdNewToXR].State = false;
                    break;
                default:
                    // Check if it's a role card and handle mutual exclusion generically
                    var roleConfigs = GetRoleConfigs();
                    if (roleConfigs.Any(config => config.Id == id))
                    {
                        DeselectOtherRoles(id);
                    }
                    break;
            }
        }

        private void DeselectOtherRoles(string selectedId)
        {
            var roleConfigs = GetRoleConfigs();
            foreach (var roleConfig in roleConfigs)
            {
                if (roleConfig.Id != selectedId)
                {
                    if (_cardsMap.ContainsKey(roleConfig.Id)) _cardsMap[roleConfig.Id].SetSelected(false);
                    if (_radioButtonsMap.ContainsKey(roleConfig.Id)) _radioButtonsMap[roleConfig.Id].State = false;
                }
            }
        }

        private Card CreateRoleCard(RoleConfig roleConfig)
        {
            var action = new ActionLinkDescription()
            {
                Id = roleConfig.Id,
                Content = new GUIContent(roleConfig.Title),
                Origin = Origins.GuidedSetup,
                ActionData = SelectedRole,
                OriginData = this
            };
            action.Action = () => { SelectedRole.SetValue(roleConfig.Id, Origins.Self, action); };

            var card = GetCard(roleConfig.Title, string.Empty, roleConfig.Id, true, false, null, action, null);

            // Set up mutual exclusion for this card
            var roleConfigs = GetRoleConfigs();
            card.Disabled = id => roleConfigs.Any(config =>
                config.Id != roleConfig.Id && _radioButtonsMap.ContainsKey(config.Id) &&
                _radioButtonsMap[config.Id].State);

            return card;
        }

        private GroupedItem CenteredRadioButton(RadioButton btn) => CenteredItem(btn);

        private GroupedItem CenteredItem(IUserInterfaceItem item) => new GroupedItem(
            new List<IUserInterfaceItem>
            {
                new AddSpace(true),
                item,
                new AddSpace(true),
            }, Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardIconGroup, UIItemPlacementType.Vertical);

        private RadioButton GetRadio(string id)
        {
            if (_radioButtonsMap.TryGetValue(id, out var button)) return button;
            button = new RadioButton(id, XR.Editor.UserInterface.Styles.Colors.LightGray,
                XR.Editor.UserInterface.Styles.Colors.Meta, "");
            // Disable self-click handling since the parent card will handle clicks
            button.HandleOwnClicks = false;
            button.OnSelect = OnSelect; // Keep this for potential direct usage
            _radioButtonsMap[id] = button;
            return button;
        }

        private Card GetCard(string title,
            string description,
            string id,
            bool requiresRadio,
            bool showAction,
            IUserInterfaceItem dynamicContent,
            LinkDescription onSelect,
            TextureContent icon = null,
            TextureContent actionIcon = null)
        {
            var cardExists = _cardsMap.TryGetValue(id, out var card);
            if (!cardExists)
            {
                card = new Card(id, true, UIItemPlacementType.Horizontal);
                _cardsMap[id] = card;
            }

            // Rebuild the callback and update the colours if needed
            card.OnSelect = _id =>
            {
                if (requiresRadio)
                {
                    OnSelect(_id);
                }

                onSelect?.Click();
            };

            if (!requiresRadio)
            {
                card.BorderColor = XR.Editor.UserInterface.Styles.Colors.DarkBorder;
                card.BorderHoverColor = XR.Editor.UserInterface.Styles.Colors.DarkGrayHover;
            }

            // Always rebuild the card content to ensure dynamic content is updated
            var titleLabel = new Label(title, GUIStyles.DynamicCardTitle)
            { FetchDynamicColor = card.FetchDynamicColor };
            var titleGroup = new GroupedItem(new List<IUserInterfaceItem> { titleLabel },
                Styles.GUIStyles.DynamicCardTitleGroup, UIItemPlacementType.Horizontal);
            if (dynamicContent != null)
            {
                titleGroup.Items.Add(dynamicContent);
                titleGroup.Items.Add(new AddSpace(true));
            }

            var contentItems = new List<IUserInterfaceItem> { titleGroup };

            // Only add description label if description is not null or empty
            if (!string.IsNullOrEmpty(description))
            {
                contentItems.Add(new Label(description, GUIStyles.DynamicCardContent)
                { FetchDynamicColor = card.FetchDynamicColor });
            }

            card.Items = new List<IUserInterfaceItem>();
            if (requiresRadio)
            {
                var button = GetRadio(id);
                button.FetchDynamicColor = card.FetchDynamicIconColor;
                card.Items.Add(CenteredRadioButton(button));
            }
            else if (icon != null)
            {
                var iconGroup = new GroupedItem(
                    new List<IUserInterfaceItem>
                    {
                        new Icon(icon),
                    }, Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardIconGroup, UIItemPlacementType.Vertical);
                card.Items.Add(iconGroup);
            }

            // If we have no description and no dynamic content, add title directly to avoid extra GroupedItem padding
            if (string.IsNullOrEmpty(description) && dynamicContent == null)
            {
                card.Items.Add(titleLabel);
            }
            else
            {
                var content = new GroupedItem(contentItems,
                    Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardContentGroup, UIItemPlacementType.Vertical);
                card.Items.Add(content);
            }

            if (showAction && onSelect != null)
            {
                card.Items.Add(new LinkLabel(onSelect));
                if (actionIcon != null)
                {
                    card.Items.Add(new Icon(actionIcon)
                    {
                        Color = Meta.XR.Editor.UserInterface.Styles.Colors.MetaForLink,
                    });
                }
            }

            return card;
        }

        private List<IUserInterfaceItem> BuildWelcomePage()
        {
            PackageList.OnPackageListRefreshed -= OnPackageListRefreshed;
            PackageList.OnPackageListRefreshed += OnPackageListRefreshed;

            // Update Card
            Card updateCard = null;
            if (About.Version < About.LatestVersion)
            {
                var actionUpdate = new ActionLinkDescription
                {
                    Id = WelcomeUpdateTitle,
                    Content = new GUIContent(WelcomeUpdateOpenUPM),
                    Origin = Origins.GuidedSetup,
                    ActionData = null,
                    OriginData = this,
                    Action = () => UnityEditor.PackageManager.UI.Window.Open(About.PackageName),
                    Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardActionWithIcon,
                    Color = Meta.XR.Editor.UserInterface.Styles.Colors.MetaForLink
                };
                updateCard = GetCard(WelcomeUpdateTitle,
                    WelcomeUpdate,
                    CardIdUpdate, false, true, null, actionUpdate);
                updateCard.BorderWidth = 1;
                updateCard.BackgroundColor = Meta.XR.Editor.UserInterface.Styles.Colors.DarkerGray;
                updateCard.BackgroundHoverColor = Meta.XR.Editor.UserInterface.Styles.Colors.DarkGray;
            }

            // New to XR Card
            var actionNewToXR = new ActionLinkDescription()
            {
                Id = CardIdNewToXR,
                Content = new GUIContent(WelcomeNewToXRTitle),
                Origin = Origins.GuidedSetup,
                ActionData = SelectedDiscovery,
                OriginData = this
            };
            actionNewToXR.Action = () =>
            {
                SelectedDiscovery.SetValue(CardIdNewToXR,
                    Origins.Self,
                    actionNewToXR);
            };
            var cardNewToXR = GetCard(WelcomeNewToXRTitle,
                WelcomeNewToXRContent,
                CardIdNewToXR, true, false, null, actionNewToXR, null);

            cardNewToXR.Disabled = id => _radioButtonsMap[CardIdVeteranToXR].State;

            // Veteran to XR Card
            var actionVeteranToXR = new ActionLinkDescription()
            {
                Id = CardIdVeteranToXR,
                Content = new GUIContent(WelcomeVeteranToXRTitle),
                Origin = Origins.GuidedSetup,
                ActionData = SelectedDiscovery,
                OriginData = this
            };
            actionVeteranToXR.Action = () =>
            {
                SelectedDiscovery.SetValue(CardIdVeteranToXR,
                    Origins.Self,
                    actionVeteranToXR);
            };
            var cardVeteranToXR = GetCard(WelcomeVeteranToXRTitle,
                WelcomeVeteranToXRContent,
                CardIdVeteranToXR, true, false, null, actionVeteranToXR, null);

            cardVeteranToXR.Disabled = id => _radioButtonsMap[CardIdNewToXR].State;

            // Pre-select cards based on stored user preferences
            OnSelect(SelectedDiscovery.Value);

            // Build the page
            return new List<IUserInterfaceItem>
            {
                Header(WelcomeHeader),
                ComputeVersionSubtitle(),
                new AddSpace(DoubleMargin),
                updateCard != null ? updateCard : new Label(WelcomeIntro),
                new AddSpace(LargeMargin), // Bespoke margin
                cardNewToXR,
                cardVeteranToXR
            };
        }

        private GroupedItem ComputeVersionSubtitle()
        {
            var versionSubtitle = string.Empty;
            var versionMessage = string.Empty;
            var versionStyle = new GUIStyle(GUIStyles.HeaderSubtitleNoWrap);
            if (About.Version != null)
            {
                versionSubtitle = WelcomeDescription;
                if (About.Version < About.LatestVersion)
                {
                    versionMessage = WelcomeDescriptionUpdate;
                    versionStyle.normal.textColor = XR.Editor.UserInterface.Styles.Colors.NewColor;
                }
                else
                {
                    versionMessage = WelcomeDescriptionUptoDate;
                    versionStyle.normal.textColor = XR.Editor.UserInterface.Styles.Colors.SuccessColor;
                }
            }

            return HeaderSubtitle(new List<IUserInterfaceItem>
            {
                new Label(versionSubtitle, GUIStyles.HeaderSubtitle),
                new Label(versionMessage, versionStyle),
                new AddSpace(true),
            });
        }

        private List<IUserInterfaceItem> BuildRolePage()
        {
            var roleConfigs = GetRoleConfigs();
            var roleCards = roleConfigs.Select(CreateRoleCard).ToList();

            var pageItems = new List<IUserInterfaceItem>
            {
                Header(RoleHeader),
                ComputeVersionSubtitle(),
                new AddSpace(DoubleMargin),
                new Label(RoleIntro),
                new AddSpace(DoubleMargin)
            };

            pageItems.AddRange(roleCards);

            // Pre-select role card based on stored user preference
            OnSelect(SelectedRole.Value);

            return pageItems;
        }

        public List<IUserInterfaceItem> BuildResourcesPage()
        {
            if (!_radioButtonsMap.TryGetValue(CardIdNewToXR, out var radioButton))
            {
                return new List<IUserInterfaceItem>();
            }
            var beginner = radioButton.State;

            var allResourceCards = CreateAllResourceCards();

            return new List<IUserInterfaceItem>
            {
                Header(ResourcesHeader),
                HeaderSubtitle(ResourcesDescription),
                new AddSpace(DoubleMargin),
                new Label(ResourcesIntro),
                new AddSpace(8),
                beginner ? allResourceCards[CardIdBuild] : allResourceCards[CardIdAPI],
                allResourceCards[CardIdSamples],
                beginner ? allResourceCards[CardIdMQDH] : allResourceCards[CardIdDevDashboard],
            };
        }

        public Card CreateResourceCard(ResourceConfig resourceConfig)
        {
            return GetCard(resourceConfig.Title,
                resourceConfig.Content,
                resourceConfig.Id, false, true, null, new UrlLinkDescription()
                {
                    Id = resourceConfig.Id,
                    OriginData = this,
                    Origin = Origins.GuidedSetup,
                    Content = new GUIContent(ResourceView),
                    URL = resourceConfig.GetUrl(), // Use GetUrl() to handle both static and dynamic URLs
                    Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardActionWithIcon,
                    Color = Meta.XR.Editor.UserInterface.Styles.Colors.MetaForLink
                }, actionIcon: resourceConfig.ActionIcon);
        }

        public Dictionary<string, Card> CreateAllResourceCards()
        {
            var resourceConfigs = GetResourceConfigs();
            return resourceConfigs.ToDictionary(config => config.Id, CreateResourceCard);
        }

        public List<Card> CreateAllResourceCardsList()
        {
            var resourceConfigs = GetResourceConfigs();
            return resourceConfigs.Select(CreateResourceCard).ToList();
        }

        private List<IUserInterfaceItem> BuildReleaseNotesContent()
        {
            // Building the page
            var content = new List<IUserInterfaceItem>
            {
                Header(ReleaseNotesHeader),
                HeaderSubtitle(ReleaseNotesDescription),
                new AddSpace(DoubleMargin),
            };

            var latestVersion = _changelogFetcher.LatestVersion;
            if (latestVersion == null)
            {
                return content;
            }

            // Adding Change Log Items
            var changeLogItems = _changelogFetcher.GetEntry(latestVersion).ChangelogUIItems
                .ToList();
            for (var i = 0; i < changeLogItems.Count; i++)
            {
                // Inserting space (because adding a margin doesn't work in Pages)
                var item = changeLogItems[i];
                if (item is not Label label) continue;
                if (label.GUIStyle.fontSize <= 12) continue;
                changeLogItems.Insert(i++, new AddSpace(DoubleMargin));
            }

            foreach (var item in changeLogItems)
            {
                // Add origin data for links
                if (item is not LinkLabel linkLabel) continue;
                linkLabel.LinkDescription.Origin = Origins.GuidedSetup;
                linkLabel.LinkDescription.OriginData = this;
            }

            // Wrapping around a Scroll View
            var scrollView = new ScrollView(changeLogItems, UIItemPlacementType.Vertical);
            content.Add(scrollView);

            return content;
        }

        private List<IUserInterfaceItem> BuildToolsPage()
        {
            // Building Blocks Card
            _bbStatus = new BulletedLabel(BuildingBlocksStatus, Styles.GUIStyles.DynamicCardDynamicContent,
                UIStyles.ContentStatusType.Success)
            {
                HorizontalStyle = XR.Editor.UserInterface.Styles.GUIStyles.BulletedLabelHorizontal
            };
            _bbStatus.LabelItem.FetchDynamicColor = item => _bbStatus.Color;
            var actionBB = new ActionLinkDescription()
            {
                Id = CardIdBB,
                Action = () => BuildingBlocksWindow.ShowWindow(Origins.GuidedSetup, this),
                Content = new GUIContent(BuildingBlocksOpen),
                ActionData = BuildingBlocks.Editor.Utils.ToolDescriptor,
                OriginData = this,
                Origin = Origins.GuidedSetup,
                Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardAction,
                Color = Meta.XR.Editor.UserInterface.Styles.Colors.MetaForLink
            };
            var cardBB = GetCard(BuildingBlocksHeader,
                BuildingBlocksContent,
                CardIdBB, false, true, _bbStatus, actionBB, BuildingBlocks.Editor.Utils.ToolDescriptor.Icon);

            // Immersive Debugger Card
            _idStatus = new BulletedLabel(string.Empty, Styles.GUIStyles.DynamicCardDynamicContent,
                UIStyles.ContentStatusType.Normal);
            _idStatus.LabelItem.FetchDynamicColor = item => _idStatus.Color;
            var actionId = new ActionLinkDescription()
            {
                Id = CardIdID,
                Action = () =>
                    Meta.XR.ImmersiveDebugger.Editor.Utils.ToolDescriptor.OpenProjectSettings(Origins.GuidedSetup),
                Content = new GUIContent(ImmersiveDebuggerOpen),
                ActionData = BuildingBlocks.Editor.Utils.ToolDescriptor,
                OriginData = this,
                Origin = Origins.GuidedSetup,
                Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardAction,
                Color = Meta.XR.Editor.UserInterface.Styles.Colors.MetaForLink
            };
            var cardID = GetCard(ImmersiveDebuggerHeader,
                ImmersiveDebuggerContent,
                CardIdID, false, true, _idStatus, actionId, Meta.XR.ImmersiveDebugger.Editor.Utils.ToolDescriptor.Icon);

            // Meta XR Simulator Card
            _xrSimStatus = new BulletedLabel(XrSimulatorStatusDefault, Styles.GUIStyles.DynamicCardDynamicContent,
                UIStyles.ContentStatusType.Error)
            {
                HorizontalStyle = XR.Editor.UserInterface.Styles.GUIStyles.BulletedLabelHorizontal
            };
            _xrSimStatus.LabelItem.FetchDynamicColor = item => _xrSimStatus.Color;
            var actionXrSim = new ActionLinkDescription()
            {
                Id = CardIdXrSim,
#if USING_META_XR_SIMULATOR
                Action = () => { Meta.XR.Simulator.Editor.Enabler.ActivateSimulator(false); },
                Content = new GUIContent(MetaXRSimulatorEnable),
#else
                Action = () => Application.OpenURL(UrlInstallXrSimulator),
                Content = new GUIContent(MetaXRSimulatorInstall),
#endif
                OriginData = this,
                Origin = Origins.GuidedSetup,
                Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardAction,
                Color = Meta.XR.Editor.UserInterface.Styles.Colors.MetaForLink
            };
            var cardXrSim = GetCard(XrSimulatorHeader,
                XrSimulatorContent,
                CardIdXrSim, false, true, _xrSimStatus, actionXrSim,
                Meta.XR.Editor.PlayCompanion.Styles.Contents.MetaXRSimulator);

            // Building the page
            return new List<IUserInterfaceItem>
            {
                Header(ToolsHeader),
                HeaderSubtitle(ToolsDescription),
                new AddSpace(DoubleMargin),
                new Label(ToolsIntro),
                new AddSpace(DoubleMargin),
                cardBB,
                cardID,
                cardXrSim
            };
        }
    }
}
