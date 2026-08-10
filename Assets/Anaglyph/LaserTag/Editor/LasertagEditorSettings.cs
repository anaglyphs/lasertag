#if UNITY_EDITOR
using System;
using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.Netcode;
using Unity.Multiplayer.PlayMode;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Anaglyph.Editor
{
	/// Controls for driving the game from the editor while in play mode.
	public class LasertagEditorSettings : EditorWindow
	{
		[MenuItem("Tools/Play Mode Control")]
		private static void Open() =>
			GetWindow<LasertagEditorSettings>("Play Mode Control");

		private void OnEnable() => EditorApplication.update += Repaint;
		private void OnDisable() => EditorApplication.update -= Repaint;

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Networking", EditorStyles.boldLabel);

			DrawToggle("Autoconnect", PlayModeAutoConnect.Setting);

			EditorGUILayout.HelpBox(
				"On play, the main editor hosts over LAN and Multiplayer Play Mode " +
				$"virtual players connect to {PlayModeAutoConnect.LoopbackIP}.",
				MessageType.None);

			if (Application.isPlaying)
				EditorGUILayout.LabelField("State", NetcodeManagement.State.ToString());

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);

			DrawToggle("Show scanned meshes", PlayModeChunkVisibility.Setting);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Simulation", EditorStyles.boldLabel);

			DrawToggle("Hide simulation rig from scene view", PlayModeSimulationVisibility.Setting);
		}

		private static void DrawToggle(string label, PlayModeSetting setting)
		{
			bool value = EditorGUILayout.ToggleLeft(label, setting.Value);
			if (value != setting.Value)
				setting.Value = value;
		}
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
}
#endif
