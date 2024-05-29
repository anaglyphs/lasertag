using Unity.Netcode;

namespace LaserTag
{
	public class Deletable : NetworkBehaviour
	{
		private bool shouldDelete;

		public void Delete()
		{
			shouldDelete = true;

			if (NetworkObject.IsOwner)
				NetworkObject.Despawn();
			else
				NetworkObject.RequestOwnership();
		}

		protected override void OnOwnershipChanged(ulong previous, ulong current)
		{
			if(shouldDelete && current == NetworkManager.LocalClientId)
				NetworkObject.Despawn();
		}
	}
}