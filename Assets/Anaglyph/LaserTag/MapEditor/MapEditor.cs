using System;
using Anaglyph.LaserTag.Weapons;
using UnityEngine;

namespace Anaglyph.LaserTag.MapEditor
{
	public static class MapEditor
	{
		public static bool IsActive { get; private set; }
		public static event Action<bool> ActiveChanged;

		/// <summary>Someone asked the editor to show its tag registration page.</summary>
		public static event Action TagRegistrationRequested;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			TagRegistrationRequested = null;
			SetActive(false);
		}

		/// <summary>
		/// Opens the editor on its tags page. The session asks for this when its host has no
		/// registered tags and no way to register any itself.
		/// </summary>
		public static void RequestTagRegistration()
		{
			// Opening the editor is what puts the palette on screen to hear the request, so it
			// has to happen before the request goes out.
			SetActive(true);
			TagRegistrationRequested?.Invoke();
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