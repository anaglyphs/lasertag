using System;
using System.Threading;
using Anaglyph.Input;
using Anaglyph.Lasertag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag.Weapons
{
	public class Automatic : MonoBehaviour
	{
		private HandSubject hand;
		[SerializeField] private string fireAction = "Activate";

		[SerializeField] private GameObject boltPrefab = null;
		[SerializeField] private Transform emitFromTransform = null;
		public UnityEvent onFire = new();

		[SerializeField] private float fireFrequency = 0.1f;
		public float FireFrequency => fireFrequency;

		private bool triggerDown;
		private bool firing;
		public bool IsFiring => firing;

		public event Action<bool> IsFiringChanged = delegate { };

		private void Awake()
		{
			TryGetComponent(out hand);
			hand.Bind(nameof(OnFire), OnFire);
		}

		private void OnEnable()
		{
			hand.Bind(fireAction, OnFire);
		}

		private void OnDisable()
		{
			hand.Unbind(fireAction, OnFire);
		}

		public void OnFire(InputAction.CallbackContext context)
		{
			triggerDown = context.ReadValueAsButton();

			if (triggerDown)
				FireLoop();
		}

		private async void FireLoop()
		{
			CancellationToken ctkn = destroyCancellationToken;

			if (firing) return;
			firing = true;
			IsFiringChanged?.Invoke(true);

			try
			{
				while (triggerDown)
				{
					Fire();
					await Awaitable.WaitForSecondsAsync(fireFrequency, ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				firing = false;
				IsFiringChanged?.Invoke(false);
			}
		}

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
				return;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			onFire.Invoke();
		}
	}
}