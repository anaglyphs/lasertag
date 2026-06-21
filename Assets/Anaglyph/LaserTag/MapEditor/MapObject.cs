using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class MapObject : NetworkBehaviour
	{
		private bool shouldDelete;

		public void Delete()
		{
			shouldDelete = true;

			if (NetworkObject.IsOwner)
			{
				NetworkObject.Despawn();
			}
			else
			{
				if (NetworkObject.IsOwnershipRequestRequired)
					NetworkObject.RequestOwnership();
				else
					NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);
			}
		}

		protected override void OnOwnershipChanged(ulong previous, ulong current)
		{
			if (shouldDelete && current == NetworkManager.LocalClientId)
				NetworkObject.Despawn();
		}
	}
}