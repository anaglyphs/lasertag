using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.VariableObjects;
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

		private NavView navView;
		private NavPage playingPage;

		private Label sessionStateLabel;
		private Label sessionAddressLabel;
		private Label localAddressLabel;
		private Button hostButton;
		private Button disconnectButton;

		private Label clientCountLabel;
		private ScrollView clientList;

		private MatchSettingsBinder matchSettings;

		private void OnEnable()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"OperatorMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = NavView.RequireIn(root);
			playingPage = navView.GetPage("playing-page");

			sessionStateLabel = Require<Label>(root, "session-state");
			sessionAddressLabel = Require<Label>(root, "session-address");
			localAddressLabel = Require<Label>(root, "local-address");
			hostButton = Require<Button>(root, "host-button");
			disconnectButton = Require<Button>(root, "disconnect-button");
			clientCountLabel = Require<Label>(root, "client-count");
			clientList = Require<ScrollView>(root, "client-list");

			matchSettings = new MatchSettingsBinder(root);

			hostButton.clicked += StartHosting;
			disconnectButton.clicked += NetcodeManagement.Disconnect;
			matchSettings.StartButton.clicked +=
				() => MatchReferee.Instance?.QueueMatch(matchSettings.Settings);
			Require<Button>(root, "stop-button").clicked +=
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
			navView = null;
		}

		private async void Start()
		{
			// Nothing to host with until the networking prefabs have spawned.
			try
			{
				while (NetworkManager.Singleton == null || ColocationManager.Instance == null)
					await Awaitable.NextFrameAsync(destroyCancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			StartHosting();
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

		private void OnMatchStateChanged(MatchState state)
		{
			navView.SetModalPresented(playingPage, state != MatchState.NotPlaying, 20);
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
