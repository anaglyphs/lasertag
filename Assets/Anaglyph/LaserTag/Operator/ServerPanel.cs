using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.Netcode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;
#endif

namespace Anaglyph.LaserTag.Operator
{
	/// <summary>
	/// Runtime-compatible operations used by the desktop host UI. Keeping these
	/// outside the EditorWindow lets a future desktop player UI use the same host
	/// path without depending on UnityEditor.
	/// </summary>
	public static class DesktopHostController
	{
		public static bool TryStartHost(bool useRelay, bool useAprilTags,
			float aprilTagSizeCm, out string error)
		{
			error = "";

			if (NetcodeManagement.State != NetcodeState.Disconnected)
			{
				error = "The network session is already starting or connected.";
				return false;
			}

			if (NetworkManager.Singleton == null)
			{
				error = "The NetworkManager has not been created yet.";
				return false;
			}

			ColocationManager colocation = ColocationManager.Instance;
			if (colocation == null)
			{
				error = "The ColocationManager has not been created yet.";
				return false;
			}

			if (colocation.TagProvider != null)
				colocation.TagProvider.HostTagSizeCm = Mathf.Max(0f, aprilTagSizeCm);
			else if (useAprilTags)
			{
				error = "AprilTag colocation is selected, but no AprilTag provider is configured.";
				return false;
			}

			colocation.methodHostSetting = useAprilTags
				? ColocationManager.ColocationMethod.AprilTag
				: ColocationManager.ColocationMethod.MetaSharedAnchor;

			PlayerAvatarSpawner.Instance?.SetIsParticipating(false);

			try
			{
				NetcodeManagement.Host(useRelay
					? NetcodeManagement.Protocol.UnityService
					: NetcodeManagement.Protocol.LAN);
				return true;
			}
			catch (Exception exception)
			{
				error = $"Could not start the host: {exception.Message}";
				Debug.LogException(exception);
				return false;
			}
		}

		public static string GetSessionAddress()
		{
			NetworkTransport currentTransport =
				NetworkManager.Singleton?.NetworkConfig?.NetworkTransport;

			if (currentTransport == null)
				return "";

			// DistributedAuthorityTransport is internal, so identify it the same
			// way as MultiplayerMenu rather than casting to an inaccessible type.
			if (string.Equals(currentTransport.GetType().Name,
				    "DistributedAuthorityTransport", StringComparison.Ordinal))
				return $"Relay: {NetcodeManagement.CurrentSessionName}";

			if (currentTransport is UnityTransport unityTransport)
				return $"LAN: {unityTransport.ConnectionData.Address}";

			return currentTransport.GetType().Name;
		}
	}

#if UNITY_EDITOR
	public sealed class ServerWindow : EditorWindow
	{
		private new static DisplayStyle Show(bool show)
		{
			return show ? DisplayStyle.Flex : DisplayStyle.None;
		}

		[MenuItem("Window/Lasertag/Server Menu")]
		private static void ShowWindow()
		{
			ServerWindow window = GetWindow<ServerWindow>("Server Menu");
			window.minSize = new Vector2(320, 200);
		}

		private const string TagSizeSaveKey = "operator.tagSize";
		private float tagSizeCm = 10f;

		private const string UseRelaySaveKey = "operator.useRelay";
		private bool useRelay = false;

		private const string UseAprilTagsSaveKey = "operator.useAprilTags";
		private bool useAprilTags = false;

		private const string PendingHostSaveKey = "operator.pendingHost";

		private MatchSettings settings = MatchSettings.DemoGame();

		private Label roomLabel;

		private PageGroup networkPages;
		private PageGroup matchPages;

		private VisualElement startServerPage;
		private VisualElement connectingPage;
		private VisualElement connectedPage;

		private VisualElement matchSettingsPage;
		private VisualElement matchRunningPage;

		private Label timerLabel;
		private Label scoreGoalLabel;

		private Label[] scoreLabels = new Label[Teams.NumTeams];

		private void OnEnable()
		{
			LoadPrefs();
			CreateUI();

			NetcodeManagement.StateChanged += UpdateHostingPage;
			MatchReferee.StateChanged += UpdateMatchPage;
			MatchReferee.TeamScored += OnTeamScored;
			MatchReferee.TimerTextChanged += OnTimerTextChanged;
			EditorApplication.update += StartPendingHost;

			UpdateHostingPage(NetcodeManagement.State);
			UpdateMatchPage(MatchReferee.State);
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= UpdateHostingPage;
			MatchReferee.StateChanged -= UpdateMatchPage;
			MatchReferee.TeamScored -= OnTeamScored;
			MatchReferee.TimerTextChanged -= OnTimerTextChanged;
			EditorApplication.update -= StartPendingHost;
		}

		private void LoadPrefs()
		{
			tagSizeCm = EditorPrefs.GetFloat(TagSizeSaveKey, tagSizeCm);
			useRelay = EditorPrefs.GetBool(UseRelaySaveKey, useRelay);
			useAprilTags = EditorPrefs.GetBool(UseAprilTagsSaveKey, useAprilTags);
		}

		private void UpdateHostingPage(NetcodeState state)
		{
			switch (state)
			{
				case NetcodeState.Disconnected:
					networkPages.SetActiveElement(startServerPage);
					break;

				case NetcodeState.Connecting:
					networkPages.SetActiveElement(connectingPage);
					break;

				case NetcodeState.Connected:
					networkPages.SetActiveElement(connectedPage);
					roomLabel.text = DesktopHostController.GetSessionAddress();

					break;

				default:
					networkPages.SetActiveElement(null);
					break;
			}
		}

		private void UpdateMatchPage(MatchState state)
		{
			if (state == MatchState.NotPlaying)
			{
				matchPages.SetActiveElement(matchSettingsPage);
			}
			else
			{
				matchPages.SetActiveElement(matchRunningPage);
				UpdateGoalDisplay();
			}
		}

		private void UpdateGoalDisplay()
		{
			bool winByTimer = MatchReferee.Settings.CheckWinByTimer();
			timerLabel.style.display = Show(winByTimer);

			bool winByScore = MatchReferee.Settings.CheckWinByScore();
			scoreGoalLabel.style.display = Show(winByScore);
			scoreGoalLabel.text = $"Playing to {MatchReferee.Settings.scoreTarget}";
		}

		private void OnTimerTextChanged(string timerString)
		{
			timerLabel.text = timerString;
		}

		private void OnTeamScored(byte team, int points)
		{
			Label label = scoreLabels[team];
			label.text = MatchReferee.GetTeamScore(team).ToString();
		}

		private void StartHost()
		{
			if (!DesktopHostController.TryStartHost(
				    useRelay, useAprilTags, tagSizeCm, out string error))
				Debug.LogError($"[{nameof(ServerWindow)}] {error}");
		}

		private void StartPendingHost()
		{
			if (!EditorApplication.isPlaying ||
			    !SessionState.GetBool(PendingHostSaveKey, false) ||
			    NetworkManager.Singleton == null)
				return;

			SessionState.SetBool(PendingHostSaveKey, false);
			StartHost();
		}

		private void CreateUI()
		{
			rootVisualElement.Clear();

			StyleSheet styleSheet = EditorGUIUtility.Load("StyleSheets/DefaultCommonDark.uss") as StyleSheet;
			if (styleSheet != null)
				rootVisualElement.styleSheets.Add(styleSheet);

			rootVisualElement.style.paddingBottom = 6;
			rootVisualElement.style.paddingLeft = 6;
			rootVisualElement.style.paddingRight = 6;
			rootVisualElement.style.paddingTop = 6;

			networkPages = new PageGroup();
			{
				startServerPage = new VisualElement();
				{
					startServerPage.Add(new Label("Host Settings")
						{ style = { unityFontStyleAndWeight = FontStyle.Bold } });

					Toggle useAprilTagsField = new("Use AprilTags") { value = useAprilTags };
					useAprilTagsField.RegisterValueChangedCallback(evt =>
					{
						useAprilTags = evt.newValue;
						EditorPrefs.SetBool(UseAprilTagsSaveKey, useAprilTags);
					});
					startServerPage.Add(useAprilTagsField);

					FloatField tagSizeField = new("AprilTag size (cm)") { value = tagSizeCm };
					tagSizeField.RegisterValueChangedCallback(evt =>
					{
						tagSizeCm = Mathf.Max(0f, evt.newValue);
						EditorPrefs.SetFloat(TagSizeSaveKey, tagSizeCm);
					});
					startServerPage.Add(tagSizeField);

					useAprilTagsField.RegisterValueChangedCallback(evt =>
					{
						tagSizeField.style.display = useAprilTags ? DisplayStyle.Flex : DisplayStyle.None;
					});
					tagSizeField.style.display = useAprilTags ? DisplayStyle.Flex : DisplayStyle.None;

					Toggle useRelayField = new("Use Relay") { value = useRelay };
					useRelayField.RegisterValueChangedCallback(evt =>
					{
						useRelay = evt.newValue;
						EditorPrefs.SetBool(UseRelaySaveKey, useRelay);
					});
					startServerPage.Add(useRelayField);

					Button hostButton = new(() =>
					{
						if (EditorApplication.isPlaying)
							StartHost();
						else
						{
							SessionState.SetBool(PendingHostSaveKey, true);
							EditorApplication.EnterPlaymode();
						}
					})
					{
						text = "Host",
						style = { height = 32 }
					};
					startServerPage.Add(hostButton);

					startServerPage.Add(
						new Label(
							"Don't forget to disable sleep on your server machine!")
						{
							style =
							{
								whiteSpace = WhiteSpace.Normal,
								unityFontStyleAndWeight = FontStyle.Bold
							}
						});

					startServerPage.Add(
						new Label(
							"If using a Windows hotspot, remember to give Unity an exception in Windows Firewall.")
						{
							style =
							{
								whiteSpace = WhiteSpace.Normal
							}
						});
				}
				networkPages.Add(startServerPage);


				connectingPage = new VisualElement();
				{
					connectingPage.Add(new Label("Connecting...")
						{ style = { unityFontStyleAndWeight = FontStyle.Bold } });

					Button stopButton = new(NetcodeManagement.Disconnect)
					{
						text = "Cancel",
						style = { height = 24 }
					};
					connectingPage.Add(stopButton);
				}
				networkPages.Add(connectingPage);


				connectedPage = new VisualElement();
				{
					connectedPage.Add(new Label("Hosting") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

					roomLabel = new Label("<Room>");
					connectedPage.Add(roomLabel);

					Button stopButton = new(NetcodeManagement.Disconnect)
					{
						text = "Stop Hosting",
						style = { height = 24 }
					};
					connectedPage.Add(stopButton);


					// match pages
					matchPages = new PageGroup();
					{
						// match settings menu
						matchSettingsPage = new VisualElement();
						{
							Label matchSettingsLabel = new("Match Settings")
								{ style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } };
							matchSettingsPage.Add(matchSettingsLabel);

							EnumField respawnDropdown = new("Respawn", settings.respawnCondition);
							respawnDropdown.RegisterValueChangedCallback(evt =>
								settings.respawnCondition = (RespawnCondition)evt.newValue);
							matchSettingsPage.Add(respawnDropdown);

							FloatField respawnTime = new("Respawn Seconds") { value = settings.respawnSeconds };
							respawnTime.RegisterValueChangedCallback(evt =>
								settings.respawnSeconds = Mathf.Max(0f, evt.newValue));
							matchSettingsPage.Add(respawnTime);

							FloatField regen = new("Health Regen / s") { value = settings.healthRegenPerSecond };
							regen.RegisterValueChangedCallback(evt =>
								settings.healthRegenPerSecond = Mathf.Max(0f, evt.newValue));
							matchSettingsPage.Add(regen);

							FloatField damage = new("Damage multiplier") { value = settings.damageMultiplier };
							damage.RegisterValueChangedCallback(evt =>
								settings.damageMultiplier = Mathf.Max(0f, evt.newValue));
							matchSettingsPage.Add(damage);

							Toggle spawnZombies = new("Spawn zombies") { value = settings.spawnZombies };
							spawnZombies.RegisterValueChangedCallback(evt =>
								settings.spawnZombies = evt.newValue);
							matchSettingsPage.Add(spawnZombies);

							IntegerField ppk = new("Points / Kill") { value = settings.pointsPerKill };
							ppk.RegisterValueChangedCallback(evt =>
								settings.pointsPerKill = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(ppk);

							IntegerField pps = new("Points / s Holding Point")
								{ value = settings.pointsPerSecondHoldingPoint };
							pps.RegisterValueChangedCallback(evt =>
								settings.pointsPerSecondHoldingPoint = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(pps);

							IntegerField ppf = new("Points / Flag capture")
								{ value = settings.pointsPerFlagCapture };
							ppf.RegisterValueChangedCallback(evt =>
								settings.pointsPerFlagCapture = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(ppf);

							IntegerField ppz = new("Points / Zombie kill")
								{ value = settings.pointsPerZombieKill };
							ppz.RegisterValueChangedCallback(evt =>
								settings.pointsPerZombieKill = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(ppz);

							EnumField winDropdown = new("Win Condition", settings.winCondition);
							winDropdown.RegisterValueChangedCallback(evt =>
								settings.winCondition = (WinCondition)evt.newValue);
							matchSettingsPage.Add(winDropdown);

							IntegerField timer = new("Timer Seconds") { value = settings.roundTimeSeconds };
							timer.RegisterValueChangedCallback(evt =>
								settings.roundTimeSeconds = Mathf.Max(0, evt.newValue));
							matchSettingsPage.Add(timer);

							IntegerField score = new("Score Target") { value = settings.scoreTarget };
							score.RegisterValueChangedCallback(evt =>
								settings.scoreTarget =
									(short)Mathf.Clamp(evt.newValue, short.MinValue, short.MaxValue));
							matchSettingsPage.Add(score);

							IntegerField rounds = new("Rounds") { value = settings.GetNumRounds() };
							rounds.RegisterValueChangedCallback(evt =>
								settings.numRounds = (byte)Mathf.Clamp(evt.newValue, 1, byte.MaxValue));
							matchSettingsPage.Add(rounds);

							Button startGame = new(() => { MatchReferee.Instance?.QueueMatch(settings); })
							{
								text = "Start Game"
							};
							startGame.style.height = 24;
							matchSettingsPage.Add(startGame);
						}
						matchPages.Add(matchSettingsPage);

						matchRunningPage = new VisualElement();
						{
							Label matchRunningLabel = new("Match Running")
								{ style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } };
							matchRunningPage.Add(matchRunningLabel);

							Button stopGame = new(() => { MatchReferee.Instance?.EndMatch(); })
							{
								text = "Stop Game",
								style =
								{
									height = 24
								}
							};
							matchRunningPage.Add(stopGame);

							scoreGoalLabel = new Label("_");
							matchRunningPage.Add(scoreGoalLabel);

							timerLabel = new Label("00:00");
							matchRunningPage.Add(timerLabel);
						}
						matchPages.Add(matchRunningPage);
					}
					connectedPage.Add(matchPages);

					for (byte i = 0; i < Teams.NumTeams; i++)
					{
						StyleColor teamColor = new(Teams.Colors[i]);
						int score = MatchReferee.GetTeamScore(i);
						scoreLabels[i] = new Label(score.ToString()) { style = { color = teamColor } };
						if (i > 0)
							connectedPage.Add(scoreLabels[i]);
					}
				}
				networkPages.Add(connectedPage);
			}
			rootVisualElement.Add(networkPages);


			UpdateHostingPage(NetcodeManagement.State);
			UpdateMatchPage(MatchReferee.State);
		}
	}
#endif
}
