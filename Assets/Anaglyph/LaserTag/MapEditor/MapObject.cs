using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class MapObject : NetworkBehaviour
	{
		[SerializeField] private bool movable = true;
		public bool Movable => movable;

		private bool shouldDelete;

		private void Awake()
		{
			NetworkObject.DontDestroyWithOwner = true;
			NetworkObject.DestroyWithScene = true;
			NetworkObject.SetSceneObjectStatus(false);

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		public override void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			base.OnDestroy();
		}

		private void Start()
		{
			TrySpawn();
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			TrySpawn();
		}

		private void TrySpawn()
		{
			if (!NetworkObject.IsSpawned && NetworkManager.IsConnectedClient)
				NetworkObject.Spawn();
		}

		public void TryTakeOwnership()
		{
			if (NetworkManager.IsConnectedClient)
			{
				if (NetworkObject.IsOwnershipRequestRequired)
					NetworkObject.RequestOwnership();
				else
					NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);
			}
		}

		public void TryDelete()
		{
			if (!NetworkManager.IsConnectedClient)
			{
				Destroy(gameObject);
				return;
			}

			shouldDelete = true;

			if (NetworkObject.IsOwner)
				NetworkObject.Despawn();
			else
				TryTakeOwnership();
		}

		public bool CanManage()
		{
			return !NetworkManager.IsConnectedClient || NetworkObject.IsOwner;
		}

		protected override void OnOwnershipChanged(ulong previous, ulong current)
		{
			if (shouldDelete && current == NetworkManager.LocalClientId)
				NetworkObject.Despawn();
		}
	}
}