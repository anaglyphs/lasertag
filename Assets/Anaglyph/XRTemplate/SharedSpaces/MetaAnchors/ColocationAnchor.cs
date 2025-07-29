using UnityEngine;
using Unity.Netcode;
using System;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class ColocationAnchor : NetworkBehaviour
	{
		public static ColocationAnchor Instance { get; private set; }
		public NetworkedAnchor networkedAnchor { get; private set; }
		public Pose DesiredPose => networkedAnchor.DesiredPose;
		public bool IsAnchored => networkedAnchor.IsAnchored;

		public static event Action<bool> OwnershipChanged = delegate { };

		private void Awake()
		{
			networkedAnchor = GetComponent<NetworkedAnchor>();
			Instance = this;
		}

		public override void OnNetworkSpawn() => OwnershipChanged.Invoke(IsOwner);
		public override void OnGainedOwnership() => OwnershipChanged.Invoke(true);
		public override void OnLostOwnership() => OwnershipChanged.Invoke(false);
		public override void OnNetworkDespawn() => OwnershipChanged.Invoke(false);
	}
}
