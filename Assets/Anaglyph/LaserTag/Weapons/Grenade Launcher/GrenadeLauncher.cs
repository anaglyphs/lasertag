using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Weapons.Grenades
{
	/// <summary>
	/// Lobs a few grenades per life. The rounds refill on respawn, not when the weapon
	/// reappears, so dying is the only way to reload.
	/// </summary>
	[RequireComponent(typeof(HandSubject))]
	public class GrenadeLauncher : MonoBehaviour, IWeapon
	{
		private HandSubject hand;

		[SerializeField] private GameObject grenadePrefab;
		[SerializeField] private Transform muzzle;
		[SerializeField] private WeaponVisual visual;
		public GameObject VisualObject => visual.gameObject;
		public UnityEvent onFire = new();

		[SerializeField, Min(1)] private int roundsPerLife = 2;
		[SerializeField, Tooltip("Shown while that many rounds or more are left, one per round.")]
		private GameObject[] roundIndicators = { };

		// see Blaster: the trigger must fall below this before the next shot
		[SerializeField, Range(0, 1)] private float rearmThreshold = 0.2f;
		private InputControl heldTrigger;

		private int rounds;
		public int Rounds
		{
			get => rounds;
			private set
			{
				rounds = value;
				for (int i = 0; i < roundIndicators.Length; i++)
					if (roundIndicators[i] != null)
						roundIndicators[i].SetActive(i < rounds);
			}
		}

		private void Awake()
		{
			TryGetComponent(out hand);
			Rounds = roundsPerLife;
			MainPlayer.Respawned += Refill;
		}

		private void OnDestroy()
		{
			MainPlayer.Respawned -= Refill;
		}

		private void OnEnable()
		{
			hand.Bind(nameof(OnFire), OnFire);
		}

		private void OnDisable()
		{
			hand.Unbind(nameof(OnFire), OnFire);
		}

		private void Refill() => Rounds = roundsPerLife;

		public void OnFire(InputAction.CallbackContext context)
		{
			if (!context.performed || !context.ReadValueAsButton() || heldTrigger != null)
				return;

			heldTrigger = context.control;
			Fire();
		}

		private void Update()
		{
			if (heldTrigger != null && heldTrigger.EvaluateMagnitude() < rearmThreshold)
				heldTrigger = null;
		}

		public void Fire()
		{
			if (Rounds <= 0 || !NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
				return;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				grenadePrefab, muzzle.position, muzzle.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			Rounds--;
			visual.PlayFire();
			onFire.Invoke();
		}
	}
}
