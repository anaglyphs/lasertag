#if UNITY_EDITOR

using Anaglyph.Lasertag;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using VariableObjects;

public sealed class ServerWindow : EditorWindow
{
	private const string tagSizeSaveKey = "Lasertag.tagSize";
	private float tagSizeCm = 10f;


	[MenuItem("Lasertag/Server Menu")]
	private static void ShowWindow()
	{
		var window = GetWindow<ServerWindow>("Server Menu");
		window.minSize = new Vector2(320, 120);
		window.LoadPrefs();
	}

	private void OnEnable()
	{
		LoadPrefs();
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

	private MatchSettings settings = MatchSettings.DemoGame();
	private string ip = "192.168.137.1";

	private void OnGUI()
	{
		EditorGUILayout.Space();

		if (NetworkManager.Singleton != null)
		{
			EditorGUILayout.LabelField("Host Settings", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("If using windows hotspot, don't forget to give Unity an exception in Windows Firewall!");

			if (!NetworkManager.Singleton.IsListening)
			{
				EditorGUI.BeginChangeCheck();
				tagSizeCm = EditorGUILayout.FloatField("apriltag size (cm)", tagSizeCm);
				if (EditorGUI.EndChangeCheck())
					tagSizeCm = Mathf.Max(0f, tagSizeCm);

				ip = EditorGUILayout.TextField("IP", ip);

				SavePrefs();

				if (GUILayout.Button("Host", GUILayout.Height(32)))
				{
					if (!EditorApplication.isPlaying)
						EditorApplication.EnterPlaymode();

					ColocationManager.Current.AprilTagSizeSetting.Value = tagSizeCm;
					ColocationManager.Current.UseAprilTagColocationSetting.Value = true;
					MainPlayer.Instance.ParticipatingInGamesSetting.Value = false;
					MainPlayer.Instance.spawnAvatar = false;

					NetworkHelper.SetNetworkTransportType("UnityTransport");
					var manager = NetworkManager.Singleton;
					var transport = manager.GetComponent<UnityTransport>();
					transport.SetConnectionData(ip, NetworkHelper.port);

					NetworkHelper.HostLAN();
				}
			}
			else
			{
				if (GUILayout.Button("Stop Hosting", GUILayout.Height(24)))
					NetworkHelper.Disconnect();
			}
		} else
		{
			EditorGUILayout.LabelField("Enter playmode!", EditorStyles.boldLabel);
		}

		if (MatchManager.Instance != null && MatchManager.Instance.IsSpawned)
		{
			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Match Settings", EditorStyles.boldLabel);

			if (MatchManager.MatchState == MatchState.NotPlaying)
			{
				settings.respawnInBases = EditorGUILayout.Toggle("Respawn In Bases", settings.respawnInBases);
				settings.respawnSeconds = Mathf.Max(0f, EditorGUILayout.FloatField("Respawn Seconds", settings.respawnSeconds));
				settings.healthRegenPerSecond = Mathf.Max(0f, EditorGUILayout.FloatField("Health Regen / s", settings.healthRegenPerSecond));

				int ppk = Mathf.Clamp(EditorGUILayout.IntField("Points Per Kill", settings.pointsPerKill), 0, 255);
				settings.pointsPerKill = (byte)ppk;

				int pps = Mathf.Clamp(EditorGUILayout.IntField("Points / s Holding Point", settings.pointsPerSecondHoldingPoint), 0, 255);
				settings.pointsPerSecondHoldingPoint = (byte)pps;

				settings.winCondition = (WinCondition)EditorGUILayout.EnumPopup("Win Condition", settings.winCondition);

				settings.timerSeconds = Mathf.Max(0, EditorGUILayout.IntField("Timer Seconds", settings.timerSeconds));

				int score = Mathf.Clamp(EditorGUILayout.IntField("Score Target", settings.scoreTarget), short.MinValue, short.MaxValue);
				settings.scoreTarget = (short)score;

				if (GUILayout.Button("Start game", GUILayout.Height(24)))
					MatchManager.Instance.QueueStartGameOwnerRpc(settings);
			}
			else
			{
				if (GUILayout.Button("Stop game", GUILayout.Height(24)))
					MatchManager.Instance.EndGameOwnerRpc();
			}
		}
	}

	private static void HandleConnect(float sizeCm)
	{

	}
}

#endif