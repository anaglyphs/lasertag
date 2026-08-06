using Anaglyph.Lasertag.Weapons;
using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Owns the local player's avatar lifecycle: one exists exactly while we are
	/// connected and taking part.
	/// </summary>
	public class PlayerAvatarSpawner : MonoBehaviour
	{
		public static PlayerAvatarSpawner Instance { get; private set; }

		[SerializeField] private GameObject avatarPrefab;

		private NetworkObject spawned;

		public bool IsParticipating { get; private set; } = true;

		private void Awake()
		{
			Instance = this;

			NetcodeManagement.StateChanged += OnNetworkStateChange;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetworkStateChange;

			if (Instance == this)
				Instance = null;
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
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
			bool shouldExist = NetcodeManagement.State == NetcodeState.Connected && IsParticipating;

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
