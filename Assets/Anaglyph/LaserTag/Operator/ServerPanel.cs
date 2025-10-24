#if UNITY_EDITOR

using System.Text.RegularExpressions;
using Anaglyph.Netcode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.Operator
{
	public sealed class ServerWindow : EditorWindow
	{
		[MenuItem("Lasertag/Server Menu")]
		private static void ShowWindow()
		{
			var window = GetWindow<ServerWindow>("Server Menu");
			window.minSize = new Vector2(320, 200);
		}
		
		private const string TagSizeSaveKey = "Lasertag.tagSize";
		private float tagSizeCm = 10f;

		private const string UseRelaySaveKey = "Lasertag.useRelay";
		private bool useRelay = false;

		private const string UseAprilTagsSaveKey = "Lasertag.useAprilTags";
		private bool useAprilTags = false;

		private const string RoomNameSaveKey = "Lasertag.roomName";
		private string roomName = "";

		private const string IpSaveKey = "Lasertag.ip";
		private string ipAddress = "127.0.0.1";

		private MatchSettings settings = MatchSettings.DemoGame();

		private PageParentElement networkPages;
		private PageParentElement matchPages;

		private VisualElement startServerPage;
		private VisualElement connectingPage;
		private VisualElement connectedPage;

		private VisualElement matchSettingsPage;
		private VisualElement matchRunningPage;

		private void OnEnable()
		{
			LoadPrefs();
			CreateUI();

			NetcodeManagement.StateChanged += UpdateHostingPage;
			MatchReferee.StateChanged += UpdateMatchPage;
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= UpdateHostingPage;
			MatchReferee.StateChanged -= UpdateMatchPage;
		}

		private void LoadPrefs()
		{
			tagSizeCm = EditorPrefs.GetFloat(TagSizeSaveKey, tagSizeCm);
			useRelay = EditorPrefs.GetBool(UseRelaySaveKey, useRelay);
			useAprilTags = EditorPrefs.GetBool(UseAprilTagsSaveKey, useAprilTags);
			roomName = EditorPrefs.GetString(RoomNameSaveKey, roomName);
			ipAddress = EditorPrefs.GetString(IpSaveKey, ipAddress);
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
					break;

				default:
					networkPages.SetActiveElement(null);
					break;
			}
		}

		private void UpdateMatchPage(MatchState state)
		{
			var matchReferee = MatchReferee.Instance;
			if (matchReferee == null)
			{
				matchPages.SetActiveElement(null);
			}
			else
			{
				if (state == MatchState.NotPlaying)
					matchPages.SetActiveElement(matchSettingsPage);
				else
					matchPages.SetActiveElement(matchRunningPage);
			}
		}

		private void StartHost()
		{
			ColocationManager.Instance.HostAprilTagSize = tagSizeCm;
			ColocationManager.Instance.HostColocationMethod = useAprilTags ? ColocationManager.Method.AprilTag : ColocationManager.Method.MetaSharedAnchor;
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

			networkPages = new PageParentElement();
			{
				startServerPage = new VisualElement();
				{
					startServerPage.Add(new Label("Host Settings")
						{ style = { unityFontStyleAndWeight = FontStyle.Bold } });

					var useAprilTagsField = new Toggle("Use AprilTag alignment") { value = useAprilTags };
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
					
					
					var protocolPages = new PageParentElement();
					{
						var lanPage = new VisualElement();
						protocolPages.Add(lanPage);
						{
							var ipField = new TextField("IP") { value = ipAddress };
							ipField.RegisterValueChangedCallback(evt =>
							{
								ipAddress = evt.newValue;
								EditorPrefs.SetString(IpSaveKey, ipAddress);
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
							"If using a Windows hotspot, remember to give Unity an exception in Windows Firewall.")
						{
							style =
							{
								whiteSpace = WhiteSpace.Normal,
							}
						});
				}
				networkPages.Add(startServerPage);


				connectingPage = new VisualElement();
				{
					connectingPage.Add(new Label("Connecting...")
						{ style = { unityFontStyleAndWeight = FontStyle.Bold } });
				}
				networkPages.Add(connectingPage);


				connectedPage = new VisualElement();
				{
					connectedPage.Add(new Label("Hosting") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

					var stopButton = new Button(NetcodeManagement.Disconnect)
					{
						text = "Stop Hosting",
						style = { height = 24 }
					};
					connectedPage.Add(stopButton);


					// match pages
					matchPages = new PageParentElement();
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

							var ppk = new IntegerField("Points Per Kill") { value = settings.pointsPerKill };
							ppk.RegisterValueChangedCallback(evt =>
								settings.pointsPerKill = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(ppk);

							var pps = new IntegerField("Points / s Holding Point")
								{ value = settings.pointsPerSecondHoldingPoint };
							pps.RegisterValueChangedCallback(evt =>
								settings.pointsPerSecondHoldingPoint = (byte)Mathf.Clamp(evt.newValue, 0, 255));
							matchSettingsPage.Add(pps);

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

							var startGame = new Button(() => { MatchReferee.Instance.StartMatchRpc(settings); })
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
						}
						matchPages.Add(matchRunningPage);
					}
					connectedPage.Add(matchPages);
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
