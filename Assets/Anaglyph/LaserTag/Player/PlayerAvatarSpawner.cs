using Anaglyph.LaserTag.Weapons;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// Keeps one player NetworkObject for the connection. Participation and alignment
	/// control its spatial presence, not its lifetime or synchronized attributes.
	/// </summary>
	public class PlayerAvatarSpawner : MonoBehaviour
	{
		public static PlayerAvatarSpawner Instance { get; private set; }

		[SerializeField] private GameObject avatarPrefab;

		private NetworkObject spawned;

		private bool participating = true;
		public bool IsParticipating => participating && !HeadsetConfiguration.IsOperatorDevice;

		private void Awake()
		{
			Instance = this;

			NetcodeManagement.StateChanged += OnNetworkStateChange;
			SyncBus.Activated += OnSessionStarted;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetworkStateChange;
			SyncBus.Activated -= OnSessionStarted;

			if (Instance == this)
				Instance = null;
		}

		private void OnNetworkStateChange(NetcodeState state) => Handle();
		private void OnSessionStarted() => Handle();
		private void Start() => Handle();

		public void SetIsParticipating(bool isParticipating)
		{
			participating = isParticipating;
			PlayerAvatar.Local?.RefreshParticipation();

			if (!isParticipating) WeaponsManagement.CanFire = false;
		}

		private void Handle()
		{
			bool shouldExist = NetcodeManagement.State == NetcodeState.Connected;

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
