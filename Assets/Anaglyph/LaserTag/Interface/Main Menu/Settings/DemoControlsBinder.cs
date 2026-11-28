using System;
using Anaglyph.LaserTag.EnvSyncing;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.Menu;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// The demo page's controls, which act on everyone in the session: a weapon for all players
	/// and the scanned mesh shown or hidden. Usable in a session while no match is on.
	/// </summary>
	public sealed class DemoControlsBinder : IDisposable
	{
		private readonly VisualElement weapons;
		private readonly Button showDebugMeshForEveryone;
		private readonly Button hideDebugMeshForEveryone;
		private readonly UIEventBindings bindings = new();

		public DemoControlsBinder(VisualElement page)
		{
			weapons = Require<VisualElement>(page, "demo-weapons");
			weapons.Clear();
			WeaponDatabase database = SessionWeaponSwitcher.Instance ? SessionWeaponSwitcher.Instance.Database : null;
			for (int id = 0; database != null && id < database.Count; id++)
			{
				GameObject weapon = database.GetWeapon(id);
				if (weapon == null) continue;
				int weaponId = id;
				Button button = new() { name = $"demo-weapon-{id}", text = weapon.name };
				button.MakeActOnPress();
				bindings.Click(button, () => SessionWeaponSwitcher.Instance?.SwitchEveryone(weaponId));
				weapons.Add(button);
			}

			showDebugMeshForEveryone = Require<Button>(page, "show-debug-mesh-for-everyone");
			hideDebugMeshForEveryone = Require<Button>(page, "hide-debug-mesh-for-everyone");
			bindings.Click(showDebugMeshForEveryone, () => EnvSync.Instance?.SetEnvMeshVisibleEveryone(true));
			bindings.Click(hideDebugMeshForEveryone, () => EnvSync.Instance?.SetEnvMeshVisibleEveryone(false));

			SyncBus.Activated += Refresh;
			SyncBus.Deactivated += Refresh;
			MatchReferee.StateChanged += OnMatchStateChanged;
			Refresh();
		}

		public void Dispose()
		{
			SyncBus.Activated -= Refresh;
			SyncBus.Deactivated -= Refresh;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			bindings.Dispose();
		}

		private void OnMatchStateChanged(MatchState _) => Refresh();

		private void Refresh()
		{
			bool available = SyncBus.Active && MatchReferee.State == MatchState.NotPlaying;
			weapons.SetEnabled(available && SessionWeaponSwitcher.Instance != null);
			showDebugMeshForEveryone.SetEnabled(available);
			hideDebugMeshForEveryone.SetEnabled(available);
		}
	}
}
