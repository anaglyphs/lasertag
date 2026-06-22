using System;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public static class MapEditor
	{
		public static bool IsActive { get; private set; }
		public static event Action<bool> ActiveChanged;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			SetActive(false);
		}

		public static void SetActive(bool active)
		{
			if (active == IsActive) return;
			IsActive = active;

			WeaponSwitcher.Instance?.SetWeaponsActive(!IsActive);

			ActiveChanged?.Invoke(active);
		}
	}
}