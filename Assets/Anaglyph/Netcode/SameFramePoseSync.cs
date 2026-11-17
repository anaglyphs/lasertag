using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// Sends the owner's local pose every frame, sampled right before rendering so it leaves
	/// in the frame it was tracked. Everyone else applies it on arrival, unsmoothed. For
	/// tracked objects (head, hands); anything that needs interpolation wants NetworkTransform.
	/// </summary>
	public class SameFramePoseSync : NetworkBehaviour
	{
		private uint sentSequence;
		private uint newestAppliedSequence;

		public override void OnNetworkSpawn()
		{
			ResetSequence();
			Application.onBeforeRender += SendIfOwner;
		}

		public override void OnNetworkDespawn()
		{
			Application.onBeforeRender -= SendIfOwner;
		}

		protected override void OnOwnershipChanged(ulong previous, ulong current)
		{
			ResetSequence();
		}

		private void ResetSequence()
		{
			sentSequence = 0;
			newestAppliedSequence = 0;
		}

		// after every pose writer
		[BeforeRenderOrder(10000)]
		private void SendIfOwner()
		{
			if (!IsOwner || !isActiveAndEnabled) return;

			transform.GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation);
			ReceivePoseRpc(++sentSequence, localPosition, localRotation);
		}

		[Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
		private void ReceivePoseRpc(uint sequence, Vector3 localPosition, Quaternion localRotation)
		{
			// unreliable packets can arrive out of order
			if (sequence <= newestAppliedSequence) return;

			newestAppliedSequence = sequence;
			transform.SetLocalPositionAndRotation(localPosition, localRotation);
		}
	}
}
