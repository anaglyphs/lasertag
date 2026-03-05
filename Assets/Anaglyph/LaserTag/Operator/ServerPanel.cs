#if UNITY_EDITOR

using Anaglyph.Netcode;
using System.Text.RegularExpressions;
using Anaglyph.XRTemplate.SharedSpaces;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.Operator
{
	public sealed class ServerWindow : EditorWindow
	{
		private new static DisplayStyle Show(bool show)
		{
			return show ? DisplayStyle.Flex : DisplayStyle.None;
		}

		[MenuItem("Window/Lasertag Server Menu")]
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

		private const string RoomNameSaveKey = "operator.roomName";
		private string roomName = "";

		//private const string IpSaveKey = "operator.ip";
		private string ipAddress = "127.0.0.1";

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

		private void Awake()
		{
			string addresses = NetcodeManagement.GetLocalIPv4();
		}

		private void OnEnable()
		{
			LoadPrefs();
			CreateUI();

			NetcodeManagement.StateChanged += UpdateHostingPage;
			MatchReferee.StateChanged += UpdateMatchPage;
			MatchReferee.TeamScored += OnTeamScored;
			MatchReferee.TimerTextChanged += OnTimerTextChanged;

			UpdateHostingPage(NetcodeManagement.State);
			UpdateMatchPage(MatchReferee.State);
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= UpdateHostingPage;
			MatchReferee.StateChanged -= UpdateMatchPage;
			MatchReferee.TeamScored -= OnTeamScored;
			MatchReferee.TimerTextChanged -= OnTimerTextChanged;
		}

		private void LoadPrefs()
		{
			tagSizeCm = EditorPrefs.GetFloat(TagSizeSaveKey, tagSizeCm);
			useRelay = EditorPrefs.GetBool(UseRelaySaveKey, useRelay);
			useAprilTags = EditorPrefs.GetBool(UseAprilTagsSaveKey, useAprilTags);
			roomName = EditorPrefs.GetString(RoomNameSaveKey, roomName);
			// ipAddress = EditorPrefs.GetString(IpSaveKey, ipAddress);
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

					NetworkManager manager = NetworkManager.Singleton;
					UnityTransport transport = (UnityTransport)manager.NetworkConfig.NetworkTransport;

					roomLabel.text = "Room: " + transport.Protocol switch
					{
						UnityTransport.ProtocolType.UnityTransport => transport.ConnectionData.Address,
						UnityTransport.ProtocolType.RelayUnityTransport => NetcodeManagement.CurrentSessionName,
						_ => "ERROR"
					};

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
			UpdateHostingPage(NetcodeManagement.State);
			UpdateMatchPage(MatchReferee.State);

			TagColocator.Instance.tagSizeCmHostSetting = tagSizeCm;
			ColocationManager.Instance.methodHostSetting = useAprilTags
				? ColocationManager.ColocationMethod.AprilTag
				: ColocationManager.ColocationMethod.MetaSharedAnchor;
			MainPlayer.Instance?.SetIsParticipating(false);

			if (useRelay)
			{
				NetcodeManagement.ConnectUnityServices(roomName);
			}
			else
			{
				NetworkManager manager = NetworkManager.Singleton;
				UnityTransport transport = manager.GetComponent<UnityTransport>();
				transport.SetConnectionData(ipAddress, NetcodeManagement.port);

				NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
			}
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


					PageGroup protocolPages = new();
					{
						VisualElement lanPage = new();
						protocolPages.Add(lanPage);
						{
							TextField ipField = new("IP") { value = ipAddress };
							ipField.RegisterValueChangedCallback(evt =>
							{
								ipAddress = evt.newValue;
								// EditorPrefs.SetString(IpSaveKey, ipAddress);
							});
							lanPage.Add(ipField);
						}

						VisualElement relayPage = new();
						protocolPages.Add(relayPage);
						{
							TextField roomNameField = new("Room Name") { value = roomName };
							roomNameField.RegisterValueChangedCallback(evt =>
							{
								roomName = Regex.Replace(evt.newValue, @"[^a-zA-Z0-9]", "");
								roomNameField.SetValueWithoutNotify(roomName);
								EditorPrefs.SetString(RoomNameSaveKey, roomName);
							});
							relayPage.Add(roomNameField);
						}

						// update visibility
						protocolPages.SetActiveElement(useRelay ? relayPage : lanPage);
						useRelayField.RegisterValueChangedCallback(evt =>
						{
							protocolPages.SetActiveElement(useRelay ? relayPage : lanPage);
						});
					}
					startServerPage.Add(protocolPages);

					Button hostButton = new(() =>
					{
						if (EditorApplication.isPlaying)
							StartHost();
						else
							EditorApplication.EnterPlaymode();
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

							Toggle respawnToggle = new("Respawn In Bases") { value = settings.respawnInBases };
							respawnToggle.RegisterValueChangedCallback(evt => settings.respawnInBases = evt.newValue);
							matchSettingsPage.Add(respawnToggle);

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

							EnumField winDropdown = new("Win Condition", settings.winCondition);
							winDropdown.RegisterValueChangedCallback(evt =>
								settings.winCondition = (WinCondition)evt.newValue);
							matchSettingsPage.Add(winDropdown);

							IntegerField timer = new("Timer Seconds") { value = settings.timerSeconds };
							timer.RegisterValueChangedCallback(evt =>
								settings.timerSeconds = Mathf.Max(0, evt.newValue));
							matchSettingsPage.Add(timer);

							IntegerField score = new("Score Target") { value = settings.scoreTarget };
							score.RegisterValueChangedCallback(evt =>
								settings.scoreTarget =
									(short)Mathf.Clamp(evt.newValue, short.MinValue, short.MaxValue));
							matchSettingsPage.Add(score);

							Button startGame = new(() => { MatchReferee.Instance.QueueMatchRpc(settings); })
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

							Button stopGame = new(() => { MatchReferee.Instance.EndMatchRpc(); })
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
}

#endif