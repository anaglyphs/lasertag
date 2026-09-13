using System;
using System.Threading;
using System.Collections.Generic;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XR.SharedSpaces;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;
using UnityEngine.XR;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// The desktop server panel. The operator does not play: this machine hosts over
	/// LAN with the map's supported alignment method as soon as the game starts, and the panel is there
	/// to run maps and matches for the headsets that join.
	/// </summary>
	[DefaultExecutionOrder(100)]
	[RequireComponent(typeof(UIDocument))]
	[RequireComponent(typeof(HeadsetConfiguration))]
	public class OperatorMenu : MonoBehaviour
	{
		private const float refreshIntervalSeconds = 1f;

		private LocalizedString hostError;
		private LocalizedString configurationMessage;
		private Label sessionStateLabel;
		private Label sessionAddressLabel;
		private Label localAddressLabel;
		private Button hostButton;
		private Button disconnectButton;

		private NavView matchNav;
		private NavPage playingPage;

		private NavView mapsNav;
		private NavPage mapEditingPage;
		private MapNameBinder mapName;
		private SpaceDetailsBinder spaceDetails;
		private NavPage spaceDetailsPage;
		private bool mapSettingsPresented;
		private MenuErrorPresenter mapErrors;

		private Label clientCountLabel;
		private MultiColumnListView clientList;
		private readonly List<OperatorHost.ConnectedClient> clients = new();
		private HeadsetConfiguration headsetConfiguration;
		private Toggle requireMenuPasswordToggle;
		private TextField menuPasswordField;
		private Toggle pinHeadsetsToHostToggle;
		private Label pinHostAddressLabel;
		private Label headsetConfigurationStatus;

		private readonly UIEventBindings bindings = new();
		private CancellationTokenSource refreshCancellation;
		private MapManagerUI mapManager;
		private MatchSettingsBinder matchSettings;

		private VisualElement viewport;
		private Camera viewportCamera;

		private const string sidebarWidthPref = "OperatorMenu.SidebarWidth";
		private const string networkHeightPref = "OperatorMenu.NetworkHeight";
		private const string viewportHeightPref = "OperatorMenu.ViewportHeight";
		private const string clientListWidthPref = "OperatorMenu.ClientListWidth";

		private TwoPaneSplitView rootSplit;
		private TwoPaneSplitView sidebarSplit;
		private TwoPaneSplitView mainSplit;
		private TwoPaneSplitView clientConfigSplit;

		private void Awake() => mapErrors = new MenuErrorPresenter(UserErrorArea.Game);
		private void OnDestroy() => mapErrors?.Dispose();

		private void OnEnable()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"OperatorMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			// The panel shows several navigation views at once, so every element is
			// looked up under the one it belongs to rather than across the document.
			VisualElement networkNav = Require<NavView>(root, "network-nav");
			matchNav = Require<NavView>(root, "match-nav");

			playingPage = matchNav.GetPage("playing-page");

			sessionStateLabel = Require<Label>(networkNav, "session-state");
			sessionAddressLabel = Require<Label>(networkNav, "session-address");
			localAddressLabel = Require<Label>(networkNav, "local-address");
			hostButton = Require<Button>(networkNav, "host-button");
			disconnectButton = Require<Button>(networkNav, "disconnect-button");

			VisualElement clientPanel = Require<VisualElement>(root, "client-list-panel");
			clientList = Require<MultiColumnListView>(clientPanel, "client-list");
			ConfigureClientList();
			clientCountLabel = Require<Label>(clientPanel, "client-count");

			headsetConfiguration = GetComponent<HeadsetConfiguration>();
			requireMenuPasswordToggle =
				Require<Toggle>(clientPanel, "require-menu-password-toggle");
			menuPasswordField = Require<TextField>(clientPanel, "menu-password-field");
			menuPasswordField.isPasswordField = true;
			pinHeadsetsToHostToggle =
				Require<Toggle>(clientPanel, "pin-headsets-to-host-toggle");
			pinHostAddressLabel = Require<Label>(clientPanel, "pin-host-address");
			headsetConfigurationStatus =
				Require<Label>(clientPanel, "headset-configuration-status");
			bindings.Click(Require<Button>(clientPanel, "apply-headset-configuration-button"),
				ApplyHeadsetConfiguration);
			RefreshHeadsetConfigurationControls();

			matchSettings = new MatchSettingsBinder(matchNav);
			BindMapEditing(root);

			BindViewportToCamera(root);
			RestoreSplitSizes(root);

			bindings.Click(hostButton, StartHosting);
			bindings.Click(disconnectButton, NetcodeManagement.Disconnect);
			bindings.Click(matchSettings.StartButton,
				() => MatchReferee.Instance?.QueueMatch(matchSettings.Settings));
			bindings.Click(Require<Button>(matchNav, "stop-button"),
				() => MatchReferee.Instance?.EndMatch());

			MenuCopy.Changed += RefreshLocalizedCopy;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MatchReferee.StateChanged += OnMatchStateChanged;
			HeadsetConfiguration.Changed += RefreshHeadsetConfigurationControls;

			OnNetcodeStateChanged(NetcodeManagement.State);
			OnMatchStateChanged(MatchReferee.State);
			BeginRefreshLoop();
		}

		private void OnDisable()
		{
			refreshCancellation?.Cancel();
			refreshCancellation?.Dispose();
			refreshCancellation = null;
			bindings.Dispose();
			mapManager?.Unbind();
			matchSettings?.Dispose();
			matchSettings = null;
			MenuCopy.Changed -= RefreshLocalizedCopy;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			HeadsetConfiguration.Changed -= RefreshHeadsetConfigurationControls;
			if (mapsNav != null) mapsNav.Changed -= OnMapsPageChanged;
			mapErrors?.Unbind();
			spaceDetails?.Dispose();
			mapName?.Dispose();
			spaceDetails = null;
			mapName = null;
			mapSettingsPresented = false;
			mapsNav = null;
			SaveSplitSizes();
			UnbindViewportFromCamera();
			matchNav = null;
		}

		private void Update() => RefreshMapSettings();

		/// <summary>
		/// The operator edits map names and space alignment without an editing mode:
		/// placing objects needs a headset, so the page is reached by navigating to it.
		/// </summary>
		private void BindMapEditing(VisualElement root)
		{
			mapsNav = Require<NavView>(root, "maps-nav");
			mapManager = GetComponent<MapManagerUI>();
			if (mapManager == null)
				throw new InvalidOperationException("OperatorMenu requires MapManagerUI on the same object.");
			mapEditingPage = mapsNav.GetPage("map-editing-page");
			spaceDetailsPage = mapsNav.GetPage("space-details");
			spaceDetails = new SpaceDetailsBinder(spaceDetailsPage, operatorMode: true);
			mapManager.Bind(Require<VisualElement>(mapsNav, "map-catalog-section"), spaceDetailsPage,
				() => mapsNav.GoToPage(mapEditingPage));
			mapName = new MapNameBinder(Require<VisualElement>(mapEditingPage, "map-name-section"));

			bindings.Click(Require<Button>(mapEditingPage, "finish-editing-button"), mapsNav.GoBack);

			mapsNav.Changed += OnMapsPageChanged;
			mapErrors.Bind(mapsNav);
			OnMapsPageChanged(mapsNav.CurrentPage);
		}

		private void OnMapsPageChanged(NavPage page)
		{
			mapSettingsPresented = page == mapEditingPage;
			RefreshMapSettings();
		}

		private void RefreshMapSettings()
		{
			if (mapSettingsPresented) mapName.Refresh();
			if (mapsNav?.CurrentPage == spaceDetailsPage) spaceDetails?.Refresh();
		}

		private async void Start()
		{
			LocalizedString error;
			try
			{
				error = await OperatorHost.StartSessionAsync(destroyCancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			ShowHostError(error);
		}

		private void StartHosting()
		{
			OperatorHost.TryStartHosting(out LocalizedString error);
			ShowHostError(error);
		}

		private void ShowHostError(LocalizedString error)
		{
			if (error == null)
				return;

			hostError = error;
			RefreshLocalizedCopy();
			Debug.LogError($"[{nameof(OperatorMenu)}] {error.GetLocalizedString()}");
		}

		private void ApplyHeadsetConfiguration()
		{
			if (headsetConfiguration.TrySetOperatorSettings(
				    requireMenuPasswordToggle.value,
				    menuPasswordField.value,
				    pinHeadsetsToHostToggle.value,
				    out LocalizedString error))
			{
				menuPasswordField.SetValueWithoutNotify("");
				configurationMessage = MenuCopy.String("Operator", OperatorHost.IsHosting ? "configuration.sent" : "configuration.saved");
				RefreshConfigurationMessage();
				headsetConfigurationStatus.RemoveFromClassList("warning");
				return;
			}

			configurationMessage = error;
			RefreshConfigurationMessage();
			headsetConfigurationStatus.AddToClassList("warning");
		}

		private void RefreshHeadsetConfigurationControls()
		{
			if (headsetConfiguration == null || requireMenuPasswordToggle == null)
				return;

			HeadsetConfiguration.OperatorSettings settings =
				headsetConfiguration.GetOperatorSettings();
			requireMenuPasswordToggle.SetValueWithoutNotify(settings.requireMenuPassword);
			pinHeadsetsToHostToggle.SetValueWithoutNotify(settings.pinHeadsetsToHost);
			pinHostAddressLabel.text = string.IsNullOrEmpty(settings.hostAddress)
				? MenuCopy.Get("Operator", "address.unavailable")
				: MenuCopy.Format("Operator", "address.pinned", settings.hostAddress);
			configurationMessage = null;
			RefreshConfigurationMessage();
			headsetConfigurationStatus.RemoveFromClassList("warning");
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			hostError = null;
			RefreshLocalizedCopy();

			bool hosting = state != NetcodeState.Disconnected;
			SetDisplayed(hostButton, !hosting);
			SetDisplayed(disconnectButton, hosting);

			matchSettings.StartButton.SetEnabled(state == NetcodeState.Connected);
			Refresh();
		}

		/// <summary>
		/// Renders the 3D scene inside the "viewport" element by shrinking the camera's
		/// normalized viewport rect to match it, instead of going through a render texture.
		/// </summary>
		private void BindViewportToCamera(VisualElement root)
		{
			viewport = Require<VisualElement>(root, "viewport");
			viewportCamera = Camera.main;

			// The viewport is sized by flex, so it lays out again whenever the window
			// or the panel scale changes.
			viewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
			MatchCameraRectToViewport();
		}

		private void UnbindViewportFromCamera()
		{
			viewport?.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);

			if (viewportCamera != null)
				viewportCamera.rect = new Rect(0, 0, 1, 1);

			viewport = null;
			viewportCamera = null;
		}

		private void OnViewportGeometryChanged(GeometryChangedEvent _) =>
			MatchCameraRectToViewport();

		private void MatchCameraRectToViewport()
		{
			if (viewportCamera == null || viewport == null)
				return;

			// Layout is in panel points rather than pixels, so the rect is taken as a
			// fraction of the panel. That stays correct at any scale or resolution.
			Rect panel = viewport.panel?.visualTree.layout ?? default;
			if (panel.width <= 0 || panel.height <= 0)
				return;

			Rect bounds = viewport.worldBound;
			if (bounds.width <= 0 || bounds.height <= 0 || float.IsNaN(bounds.x))
				return;

			// Panel space runs downwards from the top left; camera rects run upwards
			// from the bottom left.
			viewportCamera.rect = new Rect(
				(bounds.x - panel.x) / panel.width,
				1f - (bounds.yMax - panel.y) / panel.height,
				bounds.width / panel.width,
				bounds.height / panel.height);
		}

		/// <summary>
		/// Applies the operator's last split dimensions. The panel has not
		/// laid out yet, so the stored sizes are what the split views start from.
		/// </summary>
		private void RestoreSplitSizes(VisualElement root)
		{
			rootSplit = Require<TwoPaneSplitView>(root, "root-split");
			sidebarSplit = Require<TwoPaneSplitView>(root, "sidebar-split");
			mainSplit = Require<TwoPaneSplitView>(root, "main-split");
			clientConfigSplit = Require<TwoPaneSplitView>(root, "client-list-panel");

			if (PlayerPrefs.HasKey(sidebarWidthPref))
				rootSplit.fixedPaneInitialDimension = PlayerPrefs.GetFloat(sidebarWidthPref);

			if (PlayerPrefs.HasKey(networkHeightPref))
				sidebarSplit.fixedPaneInitialDimension = PlayerPrefs.GetFloat(networkHeightPref);

			if (PlayerPrefs.HasKey(viewportHeightPref))
				mainSplit.fixedPaneInitialDimension = PlayerPrefs.GetFloat(viewportHeightPref);

			if (PlayerPrefs.HasKey(clientListWidthPref))
				clientConfigSplit.fixedPaneInitialDimension =
					PlayerPrefs.GetFloat(clientListWidthPref);
		}

		private void SaveSplitSizes()
		{
			StoreDimension(sidebarWidthPref, rootSplit?.fixedPane?.resolvedStyle.width ?? 0);
			StoreDimension(networkHeightPref, sidebarSplit?.fixedPane?.resolvedStyle.height ?? 0);
			StoreDimension(viewportHeightPref, mainSplit?.fixedPane?.resolvedStyle.height ?? 0);
			StoreDimension(clientListWidthPref,
				clientConfigSplit?.fixedPane?.resolvedStyle.width ?? 0);
			PlayerPrefs.Save();

			rootSplit = null;
			sidebarSplit = null;
			mainSplit = null;
			clientConfigSplit = null;
		}

		private static void StoreDimension(string key, float dimension)
		{
			if (dimension > 0 && !float.IsNaN(dimension))
				PlayerPrefs.SetFloat(key, dimension);
		}

		private void OnMatchStateChanged(MatchState state)
		{
			matchNav.SetModalPresented(playingPage, state != MatchState.NotPlaying, 20);
		}

		private async void BeginRefreshLoop()
		{
			refreshCancellation?.Cancel();
			refreshCancellation?.Dispose();
			refreshCancellation = new CancellationTokenSource();
			CancellationToken token = refreshCancellation.Token;
			try
			{
				while (!token.IsCancellationRequested)
				{
					Refresh();
					await Awaitable.WaitForSecondsAsync(
						refreshIntervalSeconds, token);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void RefreshConfigurationMessage()
		{
			headsetConfigurationStatus.text = configurationMessage?.GetLocalizedString() ??
				MenuCopy.Get("Operator", headsetConfiguration.GetOperatorSettings().hasMenuPassword
					? "configuration.password-set" : "configuration.no-password");
		}

		private void RefreshLocalizedCopy()
		{
			foreach (Column column in clientList.columns)
				column.title = MenuCopy.Get("Operator", "column." + column.name);
			sessionStateLabel.text = MenuCopy.Get("Operator", hostError != null ? "session.failed" :
				NetcodeManagement.State == NetcodeState.Connecting ? "session.starting" :
				NetcodeManagement.State == NetcodeState.Connected ? "session.hosting" : "session.stopped");
			RefreshConfigurationMessage();
			Refresh();
		}

		private void Refresh()
		{
			// The IP is what an operator reads out to the room, so it is shown whether or
			// not the session is up.
			localAddressLabel.text = MenuCopy.Format("Operator", "address.local", OperatorHost.LocalAddress);
			if (pinHostAddressLabel != null)
			{
				string address = OperatorHost.LocalAddress;
				pinHostAddressLabel.text = string.IsNullOrEmpty(address)
					? MenuCopy.Get("Operator", "address.unavailable")
					: MenuCopy.Format("Operator", "address.pinned", address);
			}

			sessionAddressLabel.text = hostError?.GetLocalizedString() ?? OperatorHost.SessionAddress;

			RefreshClientList();
		}

		private void ConfigureClientList()
		{
			foreach (Column column in clientList.columns)
			{
				string columnName = column.name;
				column.makeCell = () =>
				{
					Label label = new();
					label.AddToClassList("client-cell");
					label.EnableInClassList("client-cell--name", columnName == "client");
					label.EnableInClassList("client-cell--status", columnName == "status");
					return label;
				};
				column.bindCell = (element, index) =>
					BindClientCell((Label)element, clients[index], columnName);
			}

			clientList.itemsSource = clients;
		}

		private void RefreshClientList()
		{
			clients.Clear();
			int playerCount = 0;

			if (OperatorHost.IsHosting)
			{
				foreach (OperatorHost.ConnectedClient client in OperatorHost.GetConnectedClients())
				{
					clients.Add(client);
					if (!client.isThisServer)
						playerCount++;
				}
			}

			clientList.RefreshItems();
			clientCountLabel.text = !OperatorHost.IsHosting
				? MenuCopy.Get("Operator", "session.stopped")
				: MenuCopy.Format("Operator", "client.count", playerCount);
		}

		private static void BindClientCell(
			Label label, OperatorHost.ConnectedClient client, string columnName)
		{
			if (label.userData is string previousSeverity)
				label.RemoveFromClassList(previousSeverity);

			PlayerAvatar avatar = client.avatar;
			string text = "";
			string severity = null;

			if (columnName == "client")
				text = MenuCopy.Format("Operator", "client.name", client.clientId);
			else if (columnName == "status")
				text = client.isThisServer ? MenuCopy.Get("Operator", "client.server")
					: avatar == null ? MenuCopy.Get("Operator", "client.joining") : avatar.IsAlive ? "" : MenuCopy.Get("Operator", "client.down");
			else if (!client.isThisServer && avatar != null)
			{
				HeadsetTelemetry telemetry = avatar.Telemetry;
				(text, severity) = columnName switch
				{
					"battery" => (DescribeBattery(telemetry), BatteryChipClass(telemetry)),
					"alignment" => (DescribeAlignment(telemetry), AlignmentChipClass(telemetry)),
					"source" => (DescribeActiveSource(avatar), null),
					"left-hand" => (DescribeTracking(telemetry.leftHandTracking),
						TrackingChipClass(telemetry.leftHandTracking)),
					"right-hand" => (DescribeTracking(telemetry.rightHandTracking),
						TrackingChipClass(telemetry.rightHandTracking)),
					"team" => ($"{avatar.Team}", null),
					"score" => (MenuCopy.Format("Operator", "client.score", avatar.Score), null),
					_ => ("", null),
				};
			}

			label.text = text;
			label.tooltip = text;
			label.EnableInClassList("chip", severity != null);
			if (severity != null)
				label.AddToClassList(severity);
			label.userData = severity;
		}

		/// <summary>
		/// "rot" is a controller the headset can still orient but has lost the position of -
		/// out of camera view, or in the dark. It aims wrong long before it stops responding.
		/// </summary>
		private static string DescribeTracking(byte tracking)
		{
			if (tracking == HeadsetTelemetry.TrackingUnavailable)
				return MenuCopy.Get("Operator", "tracking.unavailable");

			InputTrackingState state = (InputTrackingState)tracking;
			bool hasPosition = (state & InputTrackingState.Position) != 0;
			bool hasRotation = (state & InputTrackingState.Rotation) != 0;

			if (hasPosition && hasRotation)
				return MenuCopy.Get("Operator", "tracking.ok");

			if (hasRotation)
				return MenuCopy.Get("Operator", "tracking.rotation");

			if (hasPosition)
				return MenuCopy.Get("Operator", "tracking.position");

			return MenuCopy.Get("Operator", "tracking.lost");
		}

		private static string TrackingChipClass(byte tracking)
		{
			if (tracking == HeadsetTelemetry.TrackingUnavailable)
				return "chip--unknown";

			InputTrackingState state = (InputTrackingState)tracking;
			InputTrackingState pose = InputTrackingState.Position | InputTrackingState.Rotation;

			if ((state & pose) == pose)
				return "chip--good";

			return state == InputTrackingState.None ? "chip--critical" : "chip--fair";
		}

		private static string DescribeBattery(HeadsetTelemetry telemetry)
		{
			if (!telemetry.BatteryIsKnown)
				return MenuCopy.Get("Operator", "battery.unknown");

			return telemetry.isCharging
				? MenuCopy.Format("Operator", "battery.charging", telemetry.batteryPercent)
				: MenuCopy.Format("Operator", "battery.percent", telemetry.batteryPercent);
		}

		private static string BatteryChipClass(HeadsetTelemetry telemetry)
		{
			if (!telemetry.BatteryIsKnown)
				return "chip--unknown";

			return telemetry.batteryPercent switch
			{
				>= 60 => "chip--good",
				>= 35 => "chip--fair",
				>= 15 => "chip--low",
				_ => "chip--critical",
			};
		}

		/// <summary>
		/// Reads as "aligned 3/4": how many of the references that headset can see agree with
		/// the alignment it is standing in.
		/// </summary>
		private static string DescribeActiveSource(PlayerAvatar avatar)
		{
			if (!avatar.HeadsetStatus) return "";
			var state = avatar.HeadsetStatus.Readiness;
			string key = state.activeMethod switch {
				ColocationManager.ColocationMethod.AprilTag => "alignment.tags",
				ColocationManager.ColocationMethod.TwoAprilTags => "alignment.two-tags",
				ColocationManager.ColocationMethod.SystemDetermined => "alignment.system", _ => "alignment.anchors" };
			string source = MenuCopy.Get("Game", key);
			return state.transition is Maps.ReferenceTransitionPhase.Preparing or Maps.ReferenceTransitionPhase.Validating or Maps.ReferenceTransitionPhase.Persisting or Maps.ReferenceTransitionPhase.HandingOver
				? MenuCopy.Format("Game", "alignment.transition", MenuCopy.Get("Game", "alignment.phase." + state.transition), source) : source;
		}
		private static string DescribeAlignment(HeadsetTelemetry telemetry) => telemetry.alignment switch
		{
			ColocationAlignmentState.Localized => telemetry.constraintCount > 0
				? MenuCopy.Format("Operator", "alignment.count", telemetry.agreeingConstraintCount, telemetry.constraintCount)
				: MenuCopy.Get("Operator", "alignment.aligned"),
			ColocationAlignmentState.Searching => MenuCopy.Get("Operator", "alignment.searching"),
			ColocationAlignmentState.Lost => MenuCopy.Get("Operator", "alignment.lost"),
			_ => MenuCopy.Get("Operator", "alignment.inactive"),
		};

		private static string AlignmentChipClass(HeadsetTelemetry telemetry)
		{
			switch (telemetry.alignment)
			{
				case ColocationAlignmentState.Localized:
					// Aligned, but not every reference it can see agrees with where it stands.
					bool allAgree = telemetry.agreeingConstraintCount == telemetry.constraintCount;
					return allAgree ? "chip--good" : "chip--fair";

				case ColocationAlignmentState.Searching:
					return "chip--fair";

				case ColocationAlignmentState.Lost:
					return "chip--critical";

				default:
					return "chip--unknown";
			}
		}

		private static void SetDisplayed(VisualElement element, bool displayed)
		{
			element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
