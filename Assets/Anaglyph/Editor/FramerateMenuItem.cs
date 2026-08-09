
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Anaglyph.Editor
{
	/// Caps framerate in every editor process, including Multiplayer Play Mode
	/// virtual players, which have no menu bar but do share EditorPrefs.
	[InitializeOnLoad]
	public static class FramerateMenuItem
	{
		private const string PrefKey = "Anaglyph.TargetFrameRate";

		private const string Menu30 = "Tools/Framerate/30";
		private const string Menu60 = "Tools/Framerate/60";
		private const string MenuUnlimited = "Tools/Framerate/Unlimited";

		private const double SyncInterval = 1;
		private static double nextSync;

		static FramerateMenuItem()
		{
			Apply();
			EditorApplication.update += Sync;
			EditorApplication.playModeStateChanged += _ => Apply();
		}

		private static int Target
		{
			get => EditorPrefs.GetInt(PrefKey, -1);
			set
			{
				EditorPrefs.SetInt(PrefKey, value);
				Apply();
			}
		}

		// Picks up changes made from another editor process
		private static void Sync()
		{
			if (EditorApplication.timeSinceStartup < nextSync)
				return;

			nextSync = EditorApplication.timeSinceStartup + SyncInterval;
			Apply();
		}

		private static void Apply()
		{
			int target = Target;
			Application.targetFrameRate = target;

			// vSync overrides targetFrameRate. Only touch it in play mode,
			// where Unity reverts the change on exit instead of dirtying the asset.
			if (Application.isPlaying && target > 0 && QualitySettings.vSyncCount != 0)
				QualitySettings.vSyncCount = 0;
		}

		[MenuItem(Menu30)]
		private static void Framerate30() => Target = 30;

		[MenuItem(Menu30, true)]
		private static bool Framerate30Validate()
		{
			UnityEditor.Menu.SetChecked(Menu30, Target == 30);
			return true;
		}

		[MenuItem(Menu60)]
		private static void Framerate60() => Target = 60;

		[MenuItem(Menu60, true)]
		private static bool Framerate60Validate()
		{
			UnityEditor.Menu.SetChecked(Menu60, Target == 60);
			return true;
		}

		[MenuItem(MenuUnlimited)]
		private static void FramerateUnlimited() => Target = -1;

		[MenuItem(MenuUnlimited, true)]
		private static bool FramerateUnlimitedValidate()
		{
			UnityEditor.Menu.SetChecked(MenuUnlimited, Target <= 0);
			return true;
		}
	}
}
#endif
