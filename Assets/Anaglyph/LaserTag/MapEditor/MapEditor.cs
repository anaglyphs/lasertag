using System;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Weapons;
using UnityEngine;

namespace Anaglyph.LaserTag.MapEditor
{
	public static class MapEditor
	{
		public static bool IsActive { get; private set; }
		public static event Action<bool> ActiveChanged;

		/// <summary>Someone asked the editor to show space alignment and its tag tools.</summary>
		public static event Action TagRegistrationRequested;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			TagRegistrationRequested = null;
			SetActive(false);
		}

		/// <summary>
		/// Opens space alignment with tag tools. The session asks for this when its host has no
		/// registered tags and no way to register any itself.
		/// </summary>
		public static void RequestTagRegistration()
		{
			SetActive(true);
			MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
			TagRegistrationRequested?.Invoke();
		}

		public static void SetActive(bool active)
		{
			if (active == IsActive) return;
			IsActive = active;
			if (!active)
			{
				MapEditorTool.SetMode(MapEditorTool.Mode.Move);
				LaserTagMapCoordinator.Instance?.SaveCurrentMap();
			}

			WeaponSwitcher.Instance?.SetWeaponsActive(!IsActive);

			ActiveChanged?.Invoke(active);
		}
	}
}
