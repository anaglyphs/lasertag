#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Anaglyph.Netcode;
using Anaglyph.Permissions;
using Anaglyph.XR.DepthKit.EnvScanning;
using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Anaglyph.LaserTag.Editor
{
	/// Controls for driving the game from the editor while in play mode.
	public class SimulationControls : EditorWindow
	{
		[MenuItem("Window/Lasertag/Simulation Controls")]
		private static void Open() =>
			GetWindow<SimulationControls>("Lasertag Simulation Controls");

		private Vector2 scroll;

		private void OnEnable() => EditorApplication.update += Repaint;
		private void OnDisable() => EditorApplication.update -= Repaint;

		private void OnGUI()
		{
			using EditorGUILayout.ScrollViewScope scrollView = new(scroll);
			scroll = scrollView.scrollPosition;

			EditorGUILayout.LabelField("Networking", EditorStyles.boldLabel);

			DrawToggle("Autoconnect", PlayModeAutoConnect.Setting);

			EditorGUILayout.HelpBox(
				"On play, the main editor hosts over LAN and Multiplayer Play Mode " +
				$"virtual players connect to {PlayModeAutoConnect.LoopbackIP}.",
				MessageType.None);

			SimulatedConnectivity.State = (NetworkState)EditorGUILayout.IntPopup(
				"Simulated connectivity",
				(int)SimulatedConnectivity.State,
				SimulatedConnectivity.Labels,
				SimulatedConnectivity.Values);

			if (Application.isPlaying)
				EditorGUILayout.LabelField("State", NetcodeManagement.State.ToString());

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Permissions", EditorStyles.boldLabel);

			SimulatedPermissions.Enabled = EditorGUILayout.ToggleLeft(
				"Simulate permissions", SimulatedPermissions.Enabled);

			using (new EditorGUI.DisabledScope(!SimulatedPermissions.Enabled))
			{
				SimulatedPermissions.GrantRequests = EditorGUILayout.ToggleLeft(
					"Grant requests", SimulatedPermissions.GrantRequests);

				foreach (SimulatedPermissions.Permission permission in SimulatedPermissions.All)
				{
					EditorGUILayout.LabelField(permission.label);
					EditorGUI.indentLevel++;

					SimulatedPermissions.SetAvailable(permission, EditorGUILayout.ToggleLeft(
						"Available", SimulatedPermissions.IsAvailable(permission)));

					SimulatedPermissions.SetGranted(permission, EditorGUILayout.ToggleLeft(
						"Granted", SimulatedPermissions.IsGranted(permission)));

					EditorGUI.indentLevel--;
				}

				SimulatedPermissions.Vps = (VpsStatus)EditorGUILayout.EnumPopup(
					"VPS", SimulatedPermissions.Vps);
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);

			DrawToggle("Show scanned meshes", PlayModeChunkVisibility.Setting);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);

			DrawToggle("Hide simulation rig from scene view", PlayModeSimulationVisibility.Setting);
			
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);
			
			DrawToggle("Spawn red & blue bases", BaseSpawner.Setting);
		}

		private static void DrawToggle(string label, PlayModeSetting setting)
		{
			bool value = EditorGUILayout.ToggleLeft(label, setting.Value);
			if (value != setting.Value)
				setting.Value = value;
		}
	}

	/// Simulated connectivity for NetworkConnectivityTest, kept in SessionState so
	/// it survives domain reloads.
	[InitializeOnLoad]
	public static class SimulatedConnectivity
	{
		public static readonly string[] Labels = { "No connection", "LAN only", "Full internet" };

		public static readonly int[] Values =
		{
			(int)NetworkState.NoConnection,
			(int)NetworkState.ConnectionLAN,
			(int)NetworkState.ConnectionFullInternet
		};

		private const string Key = "Anaglyph.Lasertag.SimulatedNetworkState";

		static SimulatedConnectivity() => Apply();

		public static NetworkState State
		{
			get => (NetworkState)SessionState.GetInt(
				Key, (int)NetworkState.ConnectionFullInternet);
			set
			{
				SessionState.SetInt(Key, (int)value);
				Apply();
			}
		}

		private static void Apply() =>
			NetworkConnectivityTest.SimulatedNetworkState = State;
	}

	/// Simulated permission state, kept in SessionState so it survives domain
	/// reloads. Runtime permission requests write their result back.
	[InitializeOnLoad]
	public static class SimulatedPermissions
	{
		public readonly struct Permission
		{
			public readonly string label;
			public readonly string id;

			public Permission(string label, string id)
			{
				this.label = label;
				this.id = id;
			}
		}

		public static readonly Permission[] All =
		{
			new("Scene", MetaPermissionChecks.ScenePermission),
			new("Headset camera", MetaPermissionChecks.HeadsetCameraPermission),
			new("Android camera fallback", MetaPermissionChecks.AndroidCameraPermission)
		};

		private const string KeyRoot = "Anaglyph.Lasertag.SimulatedPermissions.";
		private const string EnabledKey = KeyRoot + "Enabled";
		private const string GrantRequestsKey = KeyRoot + "GrantRequests";
		private const string VpsKey = KeyRoot + "Vps";

		static SimulatedPermissions()
		{
			EditorPermissionSimulation.permissionGrantedChanged += OnGrantedChanged;

			EditorPermissionSimulation.enabled = Enabled;
			EditorPermissionSimulation.grantRequests = GrantRequests;
			EditorPermissionSimulation.vpsStatus = Vps;

			foreach (Permission permission in All)
			{
				SetAvailable(permission, IsAvailable(permission));
				SetGranted(permission, IsGranted(permission));
			}
		}

		public static bool Enabled
		{
			get => SessionState.GetBool(EnabledKey, false);
			set
			{
				SessionState.SetBool(EnabledKey, value);
				EditorPermissionSimulation.enabled = value;
			}
		}

		public static bool GrantRequests
		{
			get => SessionState.GetBool(GrantRequestsKey, true);
			set
			{
				SessionState.SetBool(GrantRequestsKey, value);
				EditorPermissionSimulation.grantRequests = value;
			}
		}

		public static VpsStatus Vps
		{
			get => (VpsStatus)SessionState.GetInt(VpsKey, (int)VpsStatus.Disabled);
			set
			{
				SessionState.SetInt(VpsKey, (int)value);
				EditorPermissionSimulation.vpsStatus = value;
			}
		}

		public static bool IsAvailable(Permission permission) =>
			SessionState.GetBool(AvailableKey(permission.id), true);

		public static void SetAvailable(Permission permission, bool available)
		{
			SessionState.SetBool(AvailableKey(permission.id), available);
			EditorPermissionSimulation.SetPermissionAvailable(permission.id, available);
		}

		public static bool IsGranted(Permission permission) =>
			SessionState.GetBool(GrantedKey(permission.id), false);

		public static void SetGranted(Permission permission, bool granted)
		{
			SessionState.SetBool(GrantedKey(permission.id), granted);
			EditorPermissionSimulation.SetPermissionGranted(permission.id, granted);
		}

		private static void OnGrantedChanged(string permission, bool granted) =>
			SessionState.SetBool(GrantedKey(permission), granted);

		private static string AvailableKey(string permission) =>
			KeyRoot + permission + ".Available";

		private static string GrantedKey(string permission) =>
			KeyRoot + permission + ".Granted";
	}

	/// An EditorPrefs bool shared with Multiplayer Play Mode virtual players, which
	/// have no menu bar of their own. Polls so a change in one editor process
	/// reaches the others.
	public class PlayModeSetting
	{
		private const double PollInterval = 1;

		private readonly string prefKey;
		private double nextPoll;

		public PlayModeSetting(string prefKey)
		{
			this.prefKey = prefKey;
			EditorApplication.update += Poll;
		}

		public bool Value
		{
			get => EditorPrefs.GetBool(prefKey, false);
			set
			{
				EditorPrefs.SetBool(prefKey, value);
				Changed?.Invoke(value);
			}
		}

		public event System.Action<bool> Changed;

		private void Poll()
		{
			if (EditorApplication.timeSinceStartup < nextPoll)
				return;

			nextPoll = EditorApplication.timeSinceStartup + PollInterval;
			Changed?.Invoke(Value);
		}
	}

	/// Hosts or connects on entering play mode. Runs in every editor process,
	/// including virtual players.
	[InitializeOnLoad]
	public static class PlayModeAutoConnect
	{
		public const string LoopbackIP = "127.0.0.1";

		public static readonly PlayModeSetting Setting = new("Anaglyph.PlayMode.AutoConnect");

		// Virtual players may enter play mode before the host is listening.
		private const double RetryInterval = 1;
		private const double GiveUpAfter = 30;

		private static double deadline;
		private static double nextAttempt;

		static PlayModeAutoConnect()
		{
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			EditorApplication.update -= Tick;

			if (change != PlayModeStateChange.EnteredPlayMode || !Setting.Value)
				return;

			deadline = EditorApplication.timeSinceStartup + GiveUpAfter;
			nextAttempt = 0;
			EditorApplication.update += Tick;
		}

		private static void Tick()
		{
			if (!Application.isPlaying || EditorApplication.timeSinceStartup > deadline)
			{
				EditorApplication.update -= Tick;
				return;
			}

			// NetworkManager may not have spawned in yet
			if (NetworkManager.Singleton == null)
				return;

			if (NetcodeManagement.State == NetcodeState.Connected)
			{
				EditorApplication.update -= Tick;
				return;
			}

			if (NetcodeManagement.State != NetcodeState.Disconnected ||
			    EditorApplication.timeSinceStartup < nextAttempt)
				return;

			nextAttempt = EditorApplication.timeSinceStartup + RetryInterval;

			if (CurrentPlayer.IsMainEditor)
			{
				NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
				EditorApplication.update -= Tick;
			}
			else
			{
				NetcodeManagement.ConnectLAN(LoopbackIP);
			}
		}
	}

	/// Reveals the environment mesh, which EnvMesher otherwise hides on Awake.
	[InitializeOnLoad]
	public static class PlayModeChunkVisibility
	{
		public static readonly PlayModeSetting Setting = new("Anaglyph.PlayMode.ShowChunks");

		private static EnvMesher appliedTo;
		private static bool appliedValue;

		static PlayModeChunkVisibility()
		{
			Setting.Changed += Apply;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			// SetChunksVisible writes layer masks into the renderer data asset, so
			// leave it the way the project ships it
			if (change == PlayModeStateChange.ExitingPlayMode)
				Apply(false);
		}

		private static void Apply(bool visible)
		{
			EnvMesher mesher = Application.isPlaying ? EnvMesher.Instance : null;

			if (mesher == null)
			{
				appliedTo = null;
				return;
			}

			if (mesher == appliedTo && visible == appliedValue)
				return;

			appliedTo = mesher;
			appliedValue = visible;
			mesher.SetChunksVisible(visible);
		}
	}

	/// Takes XR Simulation's environment and the interaction simulator out of the
	/// scene view, where the simulator's UI panel covers everything behind it.
	/// Scene visibility does not affect the game view.
	[InitializeOnLoad]
	public static class PlayModeSimulationVisibility
	{
		public static readonly PlayModeSetting Setting = new("Anaglyph.PlayMode.HideSimulation");

		// AR Foundation keeps the name internal, so it can only be matched by string.
		// Each environment gets a fresh GUID appended, hence the prefix match.
		private const string EnvironmentSceneNamePrefix = "Simulated Environment Scene";

		private static int appliedSceneHandle;
		private static XRInteractionSimulator appliedSimulator;
		private static bool appliedValue;

		static PlayModeSimulationVisibility()
		{
			Setting.Changed += Apply;
		}

		private static Scene FindEnvironmentScene()
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);

				if (scene.name.StartsWith(EnvironmentSceneNamePrefix, StringComparison.Ordinal))
					return scene;
			}

			return default;
		}

		private static void Apply(bool hide)
		{
			Scene scene = Application.isPlaying ? FindEnvironmentScene() : default;

			XRInteractionSimulator simulator = Application.isPlaying
				? GameObject.FindFirstObjectByType<XRInteractionSimulator>()
				: null;

			// Both are spawned during play, so re-apply whenever a new one shows up
			bool unchanged = hide == appliedValue &&
			                 scene.handle == appliedSceneHandle &&
			                 simulator == appliedSimulator;

			if (unchanged) return;

			appliedValue = hide;
			appliedSceneHandle = scene.handle;
			appliedSimulator = simulator;

			SceneVisibilityManager visibility = SceneVisibilityManager.instance;

			if (scene.IsValid() && scene.isLoaded)
			{
				if (hide) visibility.Hide(scene);
				else visibility.Show(scene);
			}

			if (simulator != null)
			{
				if (hide) visibility.Hide(simulator.gameObject, true);
				else visibility.Show(simulator.gameObject, true);
			}
		}
	}

	// [InitializeOnLoad]
	public static class BaseSpawner
	{
		public static readonly PlayModeSetting Setting = new("Anaglyph.PlayMode.SpawnBases");
		
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Init()
		{
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private static void OnNetcodeStateChanged(NetcodeState state)
		{
			if (!Setting.Value)
				return;
			
			NetworkManager manager = NetworkManager.Singleton;

			if (manager.IsConnectedClient && manager.IsHost)
			{
				IReadOnlyList<NetworkPrefab> prefabList = manager.NetworkConfig.Prefabs.NetworkPrefabsLists[0].PrefabList;
				
				GameObject blueBase = prefabList.FirstOrDefault(x => x.Prefab.name.Equals("Base Blue")).Prefab;
				GameObject redBase = prefabList.FirstOrDefault(x => x.Prefab.name.Equals("Base Red")).Prefab;
				
				NetworkObject.InstantiateAndSpawn(blueBase, manager, manager.LocalClientId, true, false, false, Vector3.right, Quaternion.identity);
				NetworkObject.InstantiateAndSpawn(redBase, manager, manager.LocalClientId, true, false, false, Vector3.left, Quaternion.identity);
			}
		}
	}
}
#endif
