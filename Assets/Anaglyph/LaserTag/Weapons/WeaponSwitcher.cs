using System.Collections.Generic;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	/// <summary>
	/// Which weapon each hand has chosen. The avatar is what actually holds one, so a player
	/// without an avatar has nothing to fire.
	/// </summary>
	public class WeaponSwitcher : MonoBehaviour
	{
		public static WeaponSwitcher Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => Instance = null;

		[SerializeField] private GameObject defaultWeapon;

		private readonly Dictionary<Handedness, GameObject> selected = new();
		private readonly Dictionary<Handedness, Pose> arrivingFrom = new();

		private readonly HashSet<object> weaponsPutAwayBy = new();

		/// <summary>False while anything has weapons put away, e.g. the map editor or an open menu.</summary>
		public bool WeaponsActive => weaponsPutAwayBy.Count == 0;

		private void Awake()
		{
			Instance = this;
		}

		private void Start() => NetcodeManagement.StateChanged += OnNetcodeStateChanged;

		private void OnEnable()
		{
			if (didStart)
				NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private void OnDisable() => NetcodeManagement.StateChanged -= OnNetcodeStateChanged;

		private void OnDestroy()
		{
			if (Instance == this)
				Instance = null;
		}

		// a weapon picked up in one session shouldn't follow the player into the next
		private void OnNetcodeStateChanged(NetcodeState state)
		{
			if (state == NetcodeState.Disconnected)
				selected.Clear();
		}

		public GameObject GetSelected(Handedness handedness) =>
			selected.TryGetValue(handedness, out GameObject prefab) && prefab != null
				? prefab
				: defaultWeapon;

		/// <param name="arrivingFromWorldPose">Where the weapon's visual starts before flying into the hand.</param>
		public void SwitchWeapon(GameObject prefab, Handedness handedness, Pose? arrivingFromWorldPose = null)
		{
			if (prefab == GetSelected(handedness))
				return;

			selected[handedness] = prefab;

			if (arrivingFromWorldPose is Pose pose) arrivingFrom[handedness] = pose;
			else arrivingFrom.Remove(handedness);
		}

		/// <summary>Where the newly selected weapon should fly in from, once; false to place it directly.</summary>
		public bool TakeArrivalPose(Handedness handedness, out Pose worldPose) =>
			arrivingFrom.Remove(handedness, out worldPose);

		/// <summary>Weapons stay put away until every requester that put them away lets go.</summary>
		public void SetWeaponsPutAway(object requester, bool putAway)
		{
			if (putAway) weaponsPutAwayBy.Add(requester);
			else weaponsPutAwayBy.Remove(requester);
		}
	}
}
