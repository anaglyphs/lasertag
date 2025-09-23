using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class ColocationAnchor : NetworkBehaviour
	{
		public static Action<ColocationAnchor> ColocationAnchorLocalized = delegate { };

		public static ColocationAnchor Instance { get; private set; }
		public NetworkedAnchor networkedAnchor { get; private set; }
		public Pose DesiredPose => networkedAnchor.DesiredPose;
		public bool IsLocalized => networkedAnchor.Anchor.Localized;

		private void Awake()
		{
			networkedAnchor = GetComponent<NetworkedAnchor>();
			Instance = this;
		}

		public async override void OnNetworkSpawn()
		{
			OVRManager.display.RecenteredPose += OnRecenter;

			bool localized = await networkedAnchor.Anchor.WhenLocalizedAsync();

			if(localized)
				MetaAnchorColocator.Current.AlignTo(this);
		}

		public override void OnNetworkDespawn()
		{
			if (OVRManager.display != null)
				OVRManager.display.RecenteredPose -= OnRecenter;
		}

		private void OnRecenter()
		{
			MetaAnchorColocator.Current.AlignTo(this);
		}

		[Rpc(SendTo.Owner)]
		public void DespawnAndDestroyRpc()
		{
			NetworkObject.Despawn(true);
		}
	}
}
