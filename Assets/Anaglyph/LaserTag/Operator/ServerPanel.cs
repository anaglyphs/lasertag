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

		[MenuItem("Lasertag/Server Menu")]
		private static void ShowWindow()
		{
			var window = GetWindow<ServerWindow>("Server Menu");
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
			var addresses = NetcodeManagement.GetLocalIPv4();
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

					var manager = NetworkManager.Singleton;
					var transport = (UnityTransport)manager.NetworkConfig.NetworkTransport;

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
			var winByTimer = MatchReferee.Settings.CheckWinByTimer();
			timerLabel.style.display = Show(winByTimer);

			var winByScore = MatchReferee.Settings.CheckWinByScore();
			scoreGoalLabel.style.display = Show(winByScore);
			scoreGoalLabel.text = $"Playing to {MatchReferee.Settings.scoreTarget}";
		}

		private void OnTimerTextChanged(string timerString)
		{
			timerLabel.text = timerString;
		}

		private void OnTeamScored(byte team, int points)
		{
			var label = scoreLabels[team];
			label.text = MatchReferee.GetTeamScore(team).ToString();
		}

		private void StartHost()
		{
			UpdateHostingPage(NetcodeManagement.State);
			UpdateMatchPage(MatchReferee.State);

			TagColocator.Instance.tagSizeHostSetting = tagSizeCm;
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
				var manager = NetworkManager.Singleton;
				var transport = manager.GetComponent<UnityTransport>();
				transport.SetConnectionData(ipAddress, NetcodeManagement.port);

				NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
			}
		}

		private void CreateUI()
		{
			rootVisualElement.Clear();

			var styleSheet = EditorGUIUtility.Load("StyleSheets/DefaultCommonDark.uss") as StyleSheet;
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

					var useAprilTagsField = new Toggle("Use AprilTags") { value = useAprilTags };
					useAprilTagsField.RegisterValueChangedCallback(evt =>
					{
						useAprilTags = evt.newValue;
						EditorPrefs.SetBool(UseAprilTagsSaveKey, useAprilTags);
					});
					startServerPage.Add(useAprilTagsField);

					var tagSizeField = new FloatField("AprilTag size (cm)") { value = tagSizeCm };
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

					var useRelayField = new Toggle("Use Relay") { value = useRelay };
					useRelayField.RegisterValueChangedCallback(evt =>
					{
						useRelay = evt.newValue;
						EditorPrefs.SetBool(UseRelaySaveKey, useRelay);
					});
					startServerPage.Add(useRelayField);


					var protocolPages = new PageGroup();
					{
						var lanPage = new VisualElement();
						protocolPages.Add(lanPage);
						{
							var ipField = new TextField("IP") { value = ipAddress };
							ipField.RegisterValueChangedCallback(evt =>
							{
								ipAddress = evt.newValue;
								// EditorPrefs.SetString(IpSaveKey, ipAddress);
							});
							lanPage.Add(ipField);
						}

						var relayPage = new VisualElement();
						protocolPages.Add(relayPage);
						{
							var roomNameField = new TextField("Room Name") { value = roomName };
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

					var hostButton = new Button(() =>
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

					var stopButton = new Button(NetcodeManagement.Disconnect)
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

					var stopButton = new Button(NetcodeManagement.Disconnect)
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
							var matchSettingsLabel = new Label("Match Settings")
								{ style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } };
							matchSettingsPage.Add(matchSettingsLabel);

							var respawnToggle = new Toggle("Respawn In Bases") { value = settings.respawnInBases };
							respawnToggle.RegisterValueChangedCallback(evt => settings.respawnInBases = evt.newValue);
							matchSettingsPage.Add(respawnToggle);

							var respawnTime = new FloatField("Respawn Seconds") { value = settings.respawnSeconds };
							respawnTime.RegisterValueChangedCallback(evt =>
								settings.respawnSeconds = Mathf.Max(0f, evt.newValue));
							matchSettingsPage.Add(respawnTime);

							var regen = new FloatField("Health Regen / s") { value = settings.healthRegenPerSecond };
							regen.RegisterValueChangedCallback(evt =>
								settings.healthRegenPerSecond = Mathf.Max(0f, evt.newValue));
							matchSettingsPage.Add(regen);

							var damage = new FloatField("Damage multiplier") { value = settings.damageMultiplier };
							damage.RegisterValueChangedCallback(evt =>
								settings.damageMultiplier = Mathf.Max(0f, evt.newValue));
							matchSettingsPage.Add(damage);

							var ppk = new IntegerField("Points / Kill") { value = settings.pointsPerKill };
							ppk.RegisterValueChangedCallback(evt =>
								settings.pointsPerKill = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(ppk);

							var pps = new IntegerField("Points / s Holding Point")
								{ value = settings.pointsPerSecondHoldingPoint };
							pps.RegisterValueChangedCallback(evt =>
								settings.pointsPerSecondHoldingPoint = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(pps);

							var ppf = new IntegerField("Points / Flag capture")
								{ value = settings.pointsPerFlagCapture };
							ppf.RegisterValueChangedCallback(evt =>
								settings.pointsPerFlagCapture = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(ppf);

							var winDropdown = new EnumField("Win Condition", settings.winCondition);
							winDropdown.RegisterValueChangedCallback(evt =>
								settings.winCondition = (WinCondition)evt.newValue);
							matchSettingsPage.Add(winDropdown);

							var timer = new IntegerField("Timer Seconds") { value = settings.timerSeconds };
							timer.RegisterValueChangedCallback(evt =>
								settings.timerSeconds = Mathf.Max(0, evt.newValue));
							matchSettingsPage.Add(timer);

							var score = new IntegerField("Score Target") { value = settings.scoreTarget };
							score.RegisterValueChangedCallback(evt =>
								settings.scoreTarget =
									(short)Mathf.Clamp(evt.newValue, short.MinValue, short.MaxValue));
							matchSettingsPage.Add(score);

							var startGame = new Button(() => { MatchReferee.Instance.QueueMatchRpc(settings); })
							{
								text = "Start Game"
							};
							startGame.style.height = 24;
							matchSettingsPage.Add(startGame);
						}
						matchPages.Add(matchSettingsPage);

						matchRunningPage = new VisualElement();
						{
							var matchRunningLabel = new Label("Match Running")
								{ style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } };
							matchRunningPage.Add(matchRunningLabel);

							var stopGame = new Button(() => { MatchReferee.Instance.EndMatchRpc(); })
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
						var teamColor = new StyleColor(Teams.Colors[i]);
						var score = MatchReferee.GetTeamScore(i);
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