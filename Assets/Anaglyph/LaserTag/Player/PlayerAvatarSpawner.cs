using Anaglyph.Lasertag.Weapons;
using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Owns the local player's avatar lifecycle: one exists exactly while we are
	/// connected, taking part, and have aligned with everyone else at least once.
	/// </summary>
	public class PlayerAvatarSpawner : MonoBehaviour
	{
		public static PlayerAvatarSpawner Instance { get; private set; }

		[SerializeField] private GameObject avatarPrefab;

		private NetworkObject spawned;

		// Latched for the session. Colocation drops out transiently (recentering, sleep,
		// references out of view) and the avatar carries the player's team and score, so
		// losing alignment must not cost them either.
		private bool hasAligned;

		public bool IsParticipating { get; private set; } = true;

		private void Awake()
		{
			Instance = this;

			NetcodeManagement.StateChanged += OnNetworkStateChange;
			SyncBus.Activated += OnSessionStarted;
			ColocationManager.Colocated += OnColocated;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetworkStateChange;
			SyncBus.Activated -= OnSessionStarted;
			ColocationManager.Colocated -= OnColocated;

			if (Instance == this)
				Instance = null;
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
			if (state == NetcodeState.Disconnected)
				hasAligned = false;

			Handle();
		}

		private void OnSessionStarted()
		{
			hasAligned = ColocationManager.IsColocated;
			Handle();
		}

		private void OnColocated(bool isColocated)
		{
			if (!isColocated)
				return;

			hasAligned = true;
			Handle();
		}

		public void SetIsParticipating(bool isParticipating)
		{
			IsParticipating = isParticipating;

			Handle();

			MainPlayer.Instance.Respawn();

			if (!isParticipating) WeaponsManagement.CanFire = false;
		}

		private void Handle()
		{
			bool shouldExist = NetcodeManagement.State == NetcodeState.Connected
				&& IsParticipating && hasAligned;

			if (shouldExist && spawned == null)
				Spawn();
			else if (!shouldExist && spawned != null)
			{
				// a disconnect already tore the avatar down for us
				if (NetcodeManagement.State == NetcodeState.Connected && spawned.IsSpawned)
					spawned.Despawn();

				spawned = null;
			}
		}

		private void Spawn()
		{
			NetworkManager manager = NetworkManager.Singleton;

			if (!manager.IsConnectedClient)
				return;

			spawned = NetworkObject.InstantiateAndSpawn(avatarPrefab,
				manager, manager.LocalClientId, true, true);
		}
	}
}
