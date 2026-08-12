using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	/// <summary>
	/// For visually evaluating the performance of anchors.
	/// I.E. I place these down manually and observe how they drift
	/// </summary>
	public class AnchorTest : NetworkBehaviour
	{
		private AnchorLease anchorLease;

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				MintAnchor();
		}

		private async void MintAnchor()
		{
			AnchorRegistry anchorRegistry = AnchorRegistry.Instance;
			if (anchorRegistry == null || !anchorRegistry.IsAvailable)
				return;

			CancellationToken ctkn = destroyCancellationToken;
			Pose p;
			p.position = transform.position;
			p.rotation = transform.rotation;
			anchorLease = await anchorRegistry.TryMintAsync(p, ctkn);
		}

		public override void OnNetworkDespawn()
		{
			anchorLease?.Dispose();
			anchorLease = null;
		}
	}
}
