using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AnchorHandle : NetworkBehaviour
	{
		private Guid guid;
		private bool needsCleanup = false;

		public override async void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				Pose p = new(transform.position, transform.rotation);
				guid = await MetaAnchorColocator.Instance.SpawnAnchor(p);

				if (needsCleanup)
					MetaAnchorColocator.Instance.RemoveLiveAnchor(guid);
			}
		}

		public override void OnNetworkDespawn()
		{
			if (guid == Guid.Empty)
				needsCleanup = true;
			else
				MetaAnchorColocator.Instance.RemoveLiveAnchor(guid);
		}
	}
}