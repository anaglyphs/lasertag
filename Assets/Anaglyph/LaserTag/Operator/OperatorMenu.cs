using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.VariableObjects;
using Anaglyph.XR.DepthKit.EnvScanning;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

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

		[SerializeField] private FloatObject aprilTagSizeSetting;

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

			clientCountLabel = Require<Label>(networkNav, "client-count");
			clientList = Require<ScrollView>(networkNav, "client-list");

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
			// Nothing to host with until the networking prefabs have spawned.
			try
			{
				while (NetworkManager.Singleton == null || ColocationManager.Instance == null ||
				       MapManager.Instance == null)
					await Awaitable.NextFrameAsync(destroyCancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			LoadLastAprilTagMap();
			StartHosting();
			
			EnvMesher.Instance.SetChunksVisible(true);
		}

		private static void LoadLastAprilTagMap()
		{
			if (MapManager.Instance.CurrentMap != null)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
				if (map.HasTags)
				{
					MapManager.Instance.LoadMap(map.id);
					return;
				}
		}

		private void StartHosting()
		{
			float tagSizeCm = aprilTagSizeSetting != null ? aprilTagSizeSetting.Value : 10f;

			if (!DesktopHostController.TryStartHost(
				    useRelay: false, useAprilTags: true, tagSizeCm, out string error))
			{
				sessionStateLabel.text = "Could not host";
				sessionAddressLabel.text = error;
				Debug.LogError($"[{nameof(OperatorMenu)}] {error}");
			}
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
			viewportCamera = GetComponentInParent<Camera>();

			if (viewportCamera == null)
				throw new InvalidOperationException(
					$"{nameof(OperatorMenu)} found no Camera on itself or a parent to fit " +
					"to the viewport element.");

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
			localAddressLabel.text = $"This machine: {NetcodeManagement.GetLocalIPv4()}";

			sessionAddressLabel.text = NetcodeManagement.State == NetcodeState.Connected
				? DesktopHostController.GetSessionAddress()
				: "";

			RefreshClientList();
		}

		private void RefreshClientList()
		{
			clientList.Clear();

			NetworkManager manager = NetworkManager.Singleton;
			if (manager == null || !manager.IsListening)
			{
				clientCountLabel.text = "Not hosting";
				return;
			}

			IReadOnlyList<ulong> clientIds = manager.ConnectedClientsIds;
			int playerCount = 0;

			foreach (ulong clientId in clientIds)
			{
				bool isThisServer = clientId == manager.LocalClientId;
				if (!isThisServer)
					playerCount++;

				Label row = new(DescribeClient(clientId, isThisServer));
				row.AddToClassList("client-row");
				clientList.Add(row);
			}

			clientCountLabel.text = playerCount == 1
				? "1 player connected"
				: $"{playerCount} players connected";
		}

		private static string DescribeClient(ulong clientId, bool isThisServer)
		{
			string text = isThisServer ? $"Client {clientId} · this server" : $"Client {clientId}";

			if (!PlayerAvatar.All.TryGetValue(clientId, out PlayerAvatar avatar) || avatar == null)
				return text;

			text += $" · team {avatar.Team} · {avatar.Score} pts";

			if (!avatar.IsAlive)
				text += " · down";

			return text;
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
