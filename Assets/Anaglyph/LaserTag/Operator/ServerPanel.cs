#if UNITY_EDITOR

using Anaglyph.Lasertag;
using Anaglyph.Netcode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ServerWindow : EditorWindow
{
	private const string tagSizeSaveKey = "Lasertag.tagSize";
	private float tagSizeCm = 10f;
	private string ip = "192.168.137.1";
	private MatchSettings settings = MatchSettings.DemoGame();

	[MenuItem("Lasertag/Server Menu")]
	private static void ShowWindow()
	{
		var window = GetWindow<ServerWindow>("Server Menu");
		window.minSize = new Vector2(320, 200);
		window.LoadPrefs();
		window.CreateUI();
	}

	private void OnEnable()
	{
		LoadPrefs();
		CreateUI();
	}

	private void OnDisable()
	{
		SavePrefs();
	}

	private void LoadPrefs()
	{
		tagSizeCm = EditorPrefs.GetFloat(tagSizeSaveKey, 10f);
	}

	private void SavePrefs()
	{
		EditorPrefs.SetFloat(tagSizeSaveKey, Mathf.Max(0f, tagSizeCm));
	}

	private void CreateUI()
	{
		rootVisualElement.Clear();

		var styleSheet = EditorGUIUtility.Load("StyleSheets/DefaultCommonDark.uss") as StyleSheet;
		if (styleSheet != null)
			rootVisualElement.styleSheets.Add(styleSheet);

		if (NetworkManager.Singleton == null)
		{
			rootVisualElement.Add(new Label("Enter playmode!") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
			return;
		}

		var container = new VisualElement();
		container.style.paddingLeft = 6;
		container.style.paddingRight = 6;
		container.style.paddingTop = 6;
		container.style.paddingBottom = 6;
		rootVisualElement.Add(container);

		container.Add(new Label("Host Settings") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
		container.Add(new Label("If using Windows hotspot, give Unity an exception in Windows Firewall."));

		if (!NetworkManager.Singleton.IsListening)
		{
			var tagField = new FloatField("AprilTag size (cm)") { value = tagSizeCm };
			tagField.RegisterValueChangedCallback(evt =>
			{
				tagSizeCm = Mathf.Max(0f, evt.newValue);
				SavePrefs();
			});
			container.Add(tagField);

			var ipField = new TextField("IP") { value = ip };
			ipField.RegisterValueChangedCallback(evt =>
			{
				ip = evt.newValue;
				SavePrefs();
			});
			container.Add(ipField);

			var hostButton = new Button(() =>
			{
				if (!EditorApplication.isPlaying)
					EditorApplication.EnterPlaymode();

				ColocationManager.Instance.HostAprilTagSize = tagSizeCm;
				ColocationManager.Instance.HostColocationMethod = ColocationManager.Method.AprilTag;
				MainPlayer.Instance.SetIsParticipating(false);

				var manager = NetworkManager.Singleton;
				var transport = manager.GetComponent<UnityTransport>();
				transport.SetConnectionData(ip, NetcodeManagement.port);

				NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
			})
			{
				text = "Host"
			};
			hostButton.style.height = 32;
			container.Add(hostButton);
		}
		else
		{
			var stopButton = new Button(() =>
			{
				NetcodeManagement.Disconnect();
			})
			{
				text = "Stop Hosting"
			};
			stopButton.style.height = 24;
			container.Add(stopButton);
		}

		if (MatchReferee.Instance != null && MatchReferee.Instance.IsSpawned)
		{
			container.Add(new Label("Match Settings") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 10 } });

			if (MatchReferee.Instance.State == MatchState.NotPlaying)
			{
				var respawnToggle = new Toggle("Respawn In Bases") { value = settings.respawnInBases };
				respawnToggle.RegisterValueChangedCallback(evt => settings.respawnInBases = evt.newValue);
				container.Add(respawnToggle);

				var respawnTime = new FloatField("Respawn Seconds") { value = settings.respawnSeconds };
				respawnTime.RegisterValueChangedCallback(evt => settings.respawnSeconds = Mathf.Max(0f, evt.newValue));
				container.Add(respawnTime);

				var regen = new FloatField("Health Regen / s") { value = settings.healthRegenPerSecond };
				regen.RegisterValueChangedCallback(evt => settings.healthRegenPerSecond = Mathf.Max(0f, evt.newValue));
				container.Add(regen);

				var ppk = new IntegerField("Points Per Kill") { value = settings.pointsPerKill };
				ppk.RegisterValueChangedCallback(evt => settings.pointsPerKill = (byte)Mathf.Clamp(evt.newValue, 0, 255));
				container.Add(ppk);

				var pps = new IntegerField("Points / s Holding Point") { value = settings.pointsPerSecondHoldingPoint };
				pps.RegisterValueChangedCallback(evt => settings.pointsPerSecondHoldingPoint = (byte)Mathf.Clamp(evt.newValue, 0, 255));
				container.Add(pps);

				var winDropdown = new EnumField("Win Condition", settings.winCondition);
				winDropdown.RegisterValueChangedCallback(evt => settings.winCondition = (WinCondition)evt.newValue);
				container.Add(winDropdown);

				var timer = new IntegerField("Timer Seconds") { value = settings.timerSeconds };
				timer.RegisterValueChangedCallback(evt => settings.timerSeconds = Mathf.Max(0, evt.newValue));
				container.Add(timer);

				var score = new IntegerField("Score Target") { value = settings.scoreTarget };
				score.RegisterValueChangedCallback(evt => settings.scoreTarget = (short)Mathf.Clamp(evt.newValue, short.MinValue, short.MaxValue));
				container.Add(score);

				var startGame = new Button(() =>
				{
					MatchReferee.Instance.StartMatchRpc(settings);
				})
				{
					text = "Start Game"
				};
				startGame.style.height = 24;
				container.Add(startGame);
			}
			else
			{
				var stopGame = new Button(() =>
				{
					MatchReferee.Instance.EndMatchRpc();
				})
				{
					text = "Stop Game"
				};
				stopGame.style.height = 24;
				container.Add(stopGame);
			}
		}
	}
}

#endif
