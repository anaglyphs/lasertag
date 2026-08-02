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
	[RequireComponent(typeof(HandSubject))]
	public class Automatic : MonoBehaviour, IWeapon
	{
		private HandSubject hand;
		private CancellationTokenSource fireLoopCancellation;

		[SerializeField] private GameObject boltPrefab = null;
		[SerializeField] private WeaponView view = null;
		public UnityEvent onFire = new();

		[SerializeField] private float fireFrequency = 0.1f;

		private bool triggerDown;
		private bool firing;

		private void Awake()
		{
			TryGetComponent(out hand);
		}

		private void OnEnable()
		{
			hand.Bind(nameof(OnFire), OnFire);
		}

		private void OnDisable()
		{
			hand.Unbind(nameof(OnFire), OnFire);
			triggerDown = false;
			fireLoopCancellation?.Cancel();
			view.SetFiring(false);
		}

		public void OnFire(InputAction.CallbackContext context)
		{
			triggerDown = context.ReadValueAsButton();

			if (triggerDown)
				FireLoop();
		}

		private async void FireLoop()
		{
			if (firing)
				return;

			firing = true;
			fireLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

			try
			{
				while (triggerDown)
				{
					view.SetFiring(TryFire());
					await Awaitable.WaitForSecondsAsync(fireFrequency, fireLoopCancellation.Token);
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				fireLoopCancellation?.Dispose();
				fireLoopCancellation = null;
				firing = false;
				view.SetFiring(false);
			}
		}

		public void Fire()
		{
			TryFire();
		}

		private bool TryFire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
				return false;

			Transform muzzle = view.Muzzle;
			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, muzzle.position, muzzle.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			view.PlayFire();
			onFire.Invoke();
			return true;
		}
	}
}
