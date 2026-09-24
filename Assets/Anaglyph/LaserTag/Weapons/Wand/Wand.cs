using System.Collections.Generic;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Weapons
{
	[RequireComponent(typeof(HandSubject))]
	public class Wand : MonoBehaviour, IWeapon
	{
		public enum Spell { Fireball, Lightning, Shield }

		[SerializeField] private Transform emitFromTransform;
		[SerializeField] private WeaponVisual visual;
		[SerializeField] private GameObject fireballPrefab;
		[SerializeField] private GameObject lightningPrefab;
		[SerializeField] private GameObject shieldPrefab;
		public UnityEvent onFire = new();

		private static readonly List<Wand> activeWands = new();
		public static IReadOnlyList<Wand> ActiveWands => activeWands;
		public GameObject VisualObject => visual.gameObject;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => activeWands.Clear();

		private void OnEnable() => activeWands.Add(this);
		private void OnDisable() => activeWands.Remove(this);
		public void OnFire(InputAction.CallbackContext context) { }

		public void Cast(Spell spell)
		{
			NetworkManager manager = NetworkManager.Singleton;
			if (!isActiveAndEnabled || manager == null || !manager.IsConnectedClient ||
				manager.ShutdownInProgress || !WeaponsManagement.CanFire)
				return;

			GameObject prefab = spell switch
			{
				Spell.Fireball => fireballPrefab,
				Spell.Lightning => lightningPrefab,
				Spell.Shield => shieldPrefab,
				_ => null
			};

			if (prefab == null || NetworkObjectPool.Instance == null)
				return;

			NetworkObject projectile = NetworkObjectPool.Instance.GetNetworkObject(
				prefab, emitFromTransform.position, emitFromTransform.rotation);
			projectile.SpawnWithOwnership(manager.LocalClientId);
			visual.PlayFire();
			onFire.Invoke();
		}
	}
}
