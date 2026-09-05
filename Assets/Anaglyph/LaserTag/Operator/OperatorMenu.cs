using System;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XR.SharedSpaces;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// The desktop server panel. The operator does not play: this machine hosts over
	/// LAN with AprilTag colocation as soon as the game starts, and the panel is there
	/// to run maps and matches for the headsets that join.
	/// </summary>
	[DefaultExecutionOrder(100)]
	[RequireComponent(typeof(UIDocument))]
	public class OperatorMenu : MonoBehaviour
	{
		private const float refreshIntervalSeconds = 1f;

		private Label sessionStateLabel;
		private Label sessionAddressLabel;
		private Label localAddressLabel;
		private Button hostButton;
		private Button disconnectButton;

		private NavView matchNav;
		private NavPage playingPage;

		private Label clientCountLabel;
		private ScrollView clientList;

		private MatchSettingsBinder matchSettings;

		private VisualElement viewport;
		private Camera viewportCamera;

		private const string sidebarWidthPref = "OperatorMenu.SidebarWidth";
		private const string tabsWidthPref = "OperatorMenu.TabsWidth";

		private TwoPaneSplitView sidebarSplit;
		private TwoPaneSplitView mainSplit;

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
			
			VisualElement clientListPanel = Require<VisualElement>(root, "client-list-panel");
			clientList = Require<ScrollView>(clientListPanel, "client-list");
			clientCountLabel = Require<Label>(clientListPanel, "client-count");

			matchSettings = new MatchSettingsBinder(matchNav);

			BindViewportToCamera(root);
			RestoreSplitSizes(root);

			hostButton.clicked += StartHosting;
			disconnectButton.clicked += NetcodeManagement.Disconnect;
			matchSettings.StartButton.clicked +=
				() => MatchReferee.Instance?.QueueMatch(matchSettings.Settings);
			Require<Button>(matchNav, "stop-button").clicked +=
				() => MatchReferee.Instance?.EndMatch();

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MatchReferee.StateChanged += OnMatchStateChanged;

			OnNetcodeStateChanged(NetcodeManagement.State);
			OnMatchStateChanged(MatchReferee.State);
			BeginRefreshLoop();
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			SaveSplitSizes();
			UnbindViewportFromCamera();
			matchNav = null;
		}

		private async void Start()
		{
			string error;
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
			OperatorHost.TryStartHosting(out string error);
			ShowHostError(error);
		}

		private void ShowHostError(string error)
		{
			if (string.IsNullOrEmpty(error))
				return;

			sessionStateLabel.text = "Could not host";
			sessionAddressLabel.text = error;
			Debug.LogError($"[{nameof(OperatorMenu)}] {error}");
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			sessionStateLabel.text = state switch
			{
				NetcodeState.Connecting => "Starting...",
				NetcodeState.Connected => "Hosting",
				_ => "Not hosting",
			};

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
		/// Applies the operator's last sidebar and tab widths. The panel has not
		/// laid out yet, so the stored sizes are what the split views start from.
		/// </summary>
		private void RestoreSplitSizes(VisualElement root)
		{
			sidebarSplit = Require<TwoPaneSplitView>(root, "sidebar-split");
			mainSplit = Require<TwoPaneSplitView>(root, "main-split");

			if (PlayerPrefs.HasKey(sidebarWidthPref))
				sidebarSplit.fixedPaneInitialDimension = PlayerPrefs.GetFloat(sidebarWidthPref);

			if (PlayerPrefs.HasKey(tabsWidthPref))
				mainSplit.fixedPaneInitialDimension = PlayerPrefs.GetFloat(tabsWidthPref);
		}

		private void SaveSplitSizes()
		{
			StoreDimension(sidebarWidthPref, sidebarSplit?.fixedPane?.resolvedStyle.width ?? 0);
			StoreDimension(tabsWidthPref, mainSplit?.fixedPane?.resolvedStyle.width ?? 0);
			PlayerPrefs.Save();

			sidebarSplit = null;
			mainSplit = null;
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
			try
			{
				while (isActiveAndEnabled)
				{
					Refresh();
					await Awaitable.WaitForSecondsAsync(
						refreshIntervalSeconds, destroyCancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void Refresh()
		{
			// The IP is what an operator reads out to the room, so it is shown whether or
			// not the session is up.
			localAddressLabel.text = $"This machine: {OperatorHost.LocalAddress}";

			sessionAddressLabel.text = OperatorHost.SessionAddress;

			RefreshClientList();
		}

		private void RefreshClientList()
		{
			clientList.Clear();

			if (!OperatorHost.IsHosting)
			{
				clientCountLabel.text = "Not hosting";
				return;
			}

			int playerCount = 0;

			foreach (OperatorHost.ConnectedClient client in OperatorHost.GetConnectedClients())
			{
				if (!client.isThisServer)
					playerCount++;

				clientList.Add(BuildClientRow(client));
			}

			clientCountLabel.text = playerCount == 1
				? "1 player connected"
				: $"{playerCount} players connected";
		}

		private static VisualElement BuildClientRow(OperatorHost.ConnectedClient client)
		{
			VisualElement row = new();
			row.AddToClassList("client-row");
			AddLabel(row, $"Client {client.clientId}", "client-row__name");

			if (client.isThisServer)
			{
				AddLabel(row, "this server", "client-row__detail");
				return row;
			}

			PlayerAvatar avatar = client.avatar;

			// A client is listed the moment it connects; its avatar spawns a beat later.
			if (avatar == null)
			{
				AddLabel(row, "joining", "client-row__detail");
				return row;
			}

			HeadsetTelemetry telemetry = avatar.Telemetry;
			AddChip(row, DescribeBattery(telemetry), BatteryChipClass(telemetry));
			AddChip(row, DescribeAlignment(telemetry), AlignmentChipClass(telemetry));
			AddHandChip(row, "L", telemetry.leftHandTracking);
			AddHandChip(row, "R", telemetry.rightHandTracking);

			string detail = $"team {avatar.Team} · {avatar.Score} pts";
			if (!avatar.IsAlive)
				detail += " · down";

			AddLabel(row, detail, "client-row__detail");

			return row;
		}

		private static void AddLabel(VisualElement row, string text, string className)
		{
			Label label = new(text);
			label.AddToClassList(className);
			row.Add(label);
		}

		private static void AddChip(VisualElement row, string text, string severityClass)
		{
			Label chip = new(text);
			chip.AddToClassList("chip");
			chip.AddToClassList(severityClass);
			row.Add(chip);
		}

		private static void AddHandChip(VisualElement row, string hand, byte tracking)
		{
			Label chip = new($"{hand} {DescribeTracking(tracking)}");
			chip.AddToClassList("chip");
			chip.AddToClassList(TrackingChipClass(tracking));
			row.Add(chip);
		}

		/// <summary>
		/// "rot" is a controller the headset can still orient but has lost the position of -
		/// out of camera view, or in the dark. It aims wrong long before it stops responding.
		/// </summary>
		private static string DescribeTracking(byte tracking)
		{
			if (tracking == HeadsetTelemetry.TrackingUnavailable)
				return "n/a";

			InputTrackingState state = (InputTrackingState)tracking;
			bool hasPosition = (state & InputTrackingState.Position) != 0;
			bool hasRotation = (state & InputTrackingState.Rotation) != 0;

			if (hasPosition && hasRotation)
				return "ok";

			if (hasRotation)
				return "rot";

			if (hasPosition)
				return "pos";

			return "lost";
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
				return "battery ?";

			return telemetry.isCharging
				? $"{telemetry.batteryPercent}% chg"
				: $"{telemetry.batteryPercent}%";
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
		private static string DescribeAlignment(HeadsetTelemetry telemetry) => telemetry.alignment switch
		{
			ColocationAlignmentState.Localized => telemetry.constraintCount > 0
				? $"aligned {telemetry.agreeingConstraintCount}/{telemetry.constraintCount}"
				: "aligned",
			ColocationAlignmentState.Searching => "aligning",
			ColocationAlignmentState.Lost => "lost alignment",
			_ => "not aligning",
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

		private static T Require<T>(VisualElement root, string name)
			where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
