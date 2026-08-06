using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	/// <summary>
	/// Copies the local rig onto the owner's avatar. This runs on onBeforeRender because
	/// that is the last point the tracked poses are updated: NetworkTransform samples the
	/// authority in PreUpdate, so a pose written any earlier in the frame is only a staler
	/// version of the same send.
	/// </summary>
	public class AvatarPose : NetworkBehaviour
	{
		[SerializeField] private PlayerAvatar avatar;

		private void OnValidate()
		{
			TryGetComponent(out avatar);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				Application.onBeforeRender += OnBeforeRender;
		}

		public override void OnNetworkDespawn()
		{
			Application.onBeforeRender -= OnBeforeRender;
		}

		// runs after the tracked pose drivers have written this frame's poses
		[BeforeRenderOrder(1000)]
		private void OnBeforeRender()
		{
			LocalRig rig = LocalRig.Instance;

			if (rig == null)
				return;

			avatar.HeadTransform.SetWorldPose(rig.Head.GetWorldPose());
			avatar.LeftHandTransform.SetWorldPose(rig.LeftHand.GetWorldPose());
			avatar.RightHandTransform.SetWorldPose(rig.RightHand.GetWorldPose());
		}
	}
}
