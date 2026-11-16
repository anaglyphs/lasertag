#if UNITY_EDITOR
using System;
using System.IO;
using Anaglyph.LaserTag.Interface;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// For <c>-executeMethod</c>: plays as a phone over XR Simulation, renders the phone's screen UI
	/// into portrait and landscape phone-sized textures (menu on each tab, menu hidden, then camera mode), saves
	/// them to Logs/HandheldUISnapshot, and closes the editor.
	/// </summary>
	[InitializeOnLoad]
	public static class HandheldUISnapshot
	{
		private const string RunningKey = "Anaglyph.HandheldUISnapshot.Running";
		private const string SettingsKey = "Anaglyph.HandheldUISnapshot.Settings";
		private const double SettleSeconds = 12;
		private const string MainScene = "Assets/Anaglyph/LaserTag/MainScene.unity";

		/// <summary>A phone to draw for: its portrait size in pixels, density, and safe areas (bottom-left origin) each way up.</summary>
		private readonly struct Device
		{
			public readonly string name;
			public readonly Vector2Int portrait;
			public readonly float pixelsPerUnit;
			public readonly Rect portraitSafeArea, landscapeSafeArea;

			public Device(string name, Vector2Int portrait, float pixelsPerUnit, float pointPixels, float notch, float homePortrait, float homeLandscape)
			{
				this.name = name;
				this.portrait = portrait;
				this.pixelsPerUnit = pixelsPerUnit;
				float n = notch * pointPixels, hp = homePortrait * pointPixels, hl = homeLandscape * pointPixels;
				portraitSafeArea = new Rect(0, hp, portrait.x, portrait.y - hp - n);
				landscapeSafeArea = new Rect(n, hl, portrait.y - 2 * n, portrait.x - hl);
			}

			public Vector2Int Landscape => new(portrait.y, portrait.x);
		}

		private static readonly Device[] Devices =
		{
			// a generic notched phone at 2 pixels per point
			new("phone-2x", new Vector2Int(1080, 2340), 2, 2, 59, 34, 21),
			// 402 × 874 pt at 3x, 460 ppi; insets as the iPhone 17 Pro's, Apple publishes none
			new("iphone-18-pro", new Vector2Int(1206, 2622), 460 / 160f, 3, 62, 34, 21),
		};

		private static Device device = Devices[0];

		private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "HandheldUISnapshot", device.name));
		private const string DeviceKey = "Anaglyph.HandheldUISnapshot.Device";

		private static double playStartedAt = -1;
		private static double nextStepAt;

		static HandheldUISnapshot()
		{
			device = System.Array.Find(Devices, d => d.name == SessionState.GetString(DeviceKey, Devices[0].name));
			if (device.name == null) device = Devices[0];
			if (SessionState.GetBool(RunningKey, false)) EditorApplication.update += Tick;
		}

		/// <summary>-snapshotDevice picks the phone (phone-2x, iphone-18-pro); the first when absent.</summary>
		public static void Run()
		{
			string[] args = Environment.GetCommandLineArgs();
			int at = Array.IndexOf(args, "-snapshotDevice");
			string wanted = at >= 0 && at + 1 < args.Length ? args[at + 1] : Devices[0].name;
			device = Array.Find(Devices, d => d.name == wanted);
			if (device.name == null) device = Devices[0];
			SessionState.SetString(DeviceKey, device.name);
			SessionState.SetString(SettingsKey, $"{PlayModeXRSimulation.Setting.Value},{PlayModePhoneSimulation.Setting.Value},{PlayModeAutoConnect.Setting.Value}");
			PlayModeXRSimulation.Setting.Value = true;
			PlayModePhoneSimulation.Setting.Value = true;
			PlayModeAutoConnect.Setting.Value = false;
			SessionState.SetBool(RunningKey, true);
			Directory.CreateDirectory(Folder);
			UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MainScene);
			EditorApplication.update += Tick;
		}

		private static void Tick()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
			if (!Application.isPlaying)
			{
				if (playStartedAt < 0) EditorApplication.isPlaying = true;
				else Finish();
				return;
			}

			if (playStartedAt < 0)
			{
				playStartedAt = EditorApplication.timeSinceStartup;
				nextStepAt = playStartedAt + SettleSeconds;
			}

			if (EditorApplication.timeSinceStartup < nextStepAt) return;
			nextStepAt = EditorApplication.timeSinceStartup + 1.5;

			HandheldScreenUI ui = UnityEngine.Object.FindAnyObjectByType<HandheldScreenUI>();
			if (ui == null || ui.ScreenPanel == null)
			{
				Debug.LogError($"[{nameof(HandheldUISnapshot)}] no phone UI came up");
				EditorApplication.isPlaying = false;
				return;
			}

			MainMenuController menu = ui.Menu;
			int panels = menu.Panels.Length;
			// shots: each tab in portrait, each tab in landscape, then the menu hidden in both, then camera mode in both
			int shots = 2 * panels + 4;
			bool filming = shot >= 2 * panels + 2;
			if (shot >= shots)
			{
				EditorApplication.isPlaying = false;
				return;
			}

			bool portrait = shot < panels || shot == 2 * panels || shot == 2 * panels + 2;
			int tab = shot < 2 * panels ? shot % panels : -1;
			switch (phase)
			{
				case 0:
					menu.SetVisible(tab >= 0);
					if (tab >= 0) menu.ScreenPanel = tab;
					if (CameraMode.Instance != null) CameraMode.Instance.SetActive(filming);
					break;
				case 1:
					ui.SafeAreaForTests = portrait ? device.portraitSafeArea : device.landscapeSafeArea;
					ui.PixelsPerUnitForTests = device.pixelsPerUnit;
					Render(ui, portrait ? device.portrait : device.Landscape);
					break;
				default:
					Save($"{(tab >= 0 ? "menu-" + menu.Panels[tab].name : filming ? "camera" : "playing")}-{(portrait ? "portrait" : "landscape")}", ui.SafeAreaForTests.Value);
					shot++;
					phase = -1;
					break;
			}

			phase++;
		}

		private static int shot, phase;
		private static RenderTexture target;

		private static void Render(HandheldScreenUI ui, Vector2Int size)
		{
			if (target != null) target.Release();
			target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
			ui.ScreenPanel.targetTexture = target;
			ui.ScreenPanel.clearColor = true;
			ui.ScreenPanel.colorClearValue = new Color(0.25f, 0.3f, 0.35f, 1);
		}

		private static void Save(string name, Rect safeArea)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			Texture2D image = new(target.width, target.height, TextureFormat.RGBA32, false);
			image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
			// the unsafe edges shaded red, to show nothing that matters lies under them
			Color32[] pixels = image.GetPixels32();
			for (int y = 0; y < target.height; y++)
			for (int x = 0; x < target.width; x++)
				if (!safeArea.Contains(new Vector2(x + 0.5f, y + 0.5f)))
				{
					Color32 c = pixels[y * target.width + x];
					pixels[y * target.width + x] = new Color32((byte)((c.r + 255) / 2), (byte)(c.g / 2), (byte)(c.b / 2), 255);
				}
			image.SetPixels32(pixels);
			image.Apply();
			RenderTexture.active = previous;
			File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
			Debug.Log($"[{nameof(HandheldUISnapshot)}] saved {name}");
		}

		private static void Finish()
		{
			EditorApplication.update -= Tick;
			SessionState.EraseBool(RunningKey);
			string[] saved = SessionState.GetString(SettingsKey, "true,false,true").Split(',');
			PlayModeXRSimulation.Setting.Value = bool.Parse(saved[0]);
			PlayModePhoneSimulation.Setting.Value = bool.Parse(saved[1]);
			PlayModeAutoConnect.Setting.Value = bool.Parse(saved[2]);
			Debug.Log($"[{nameof(HandheldUISnapshot)}] done: {Folder}");
			if (Environment.GetCommandLineArgs() is { } args && Array.IndexOf(args, "-quitWhenDone") >= 0) EditorApplication.Exit(0);
		}
	}
}
#endif
