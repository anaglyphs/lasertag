using UnityEngine;
using Unity.Netcode;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class ColocationAnchor : NetworkBehaviour
	{
		public static ColocationAnchor Instance { get; private set; }
		public NetworkedAnchor networkedAnchor { get; private set; }
		public Pose DesiredPose => networkedAnchor.DesiredPose;
		public bool IsAnchored => networkedAnchor.IsAnchored;

		private void Awake()
		{
			networkedAnchor = GetComponent<NetworkedAnchor>();
			Instance = this;
		}

		[Rpc(SendTo.Owner)]
		public void DespawnAndDestroyRpc()
		{
			NetworkObject.Despawn(true);
		}
	}
}
