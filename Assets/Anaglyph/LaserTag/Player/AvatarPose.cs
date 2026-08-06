using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	/// <summary>
	/// Copies the local rig onto the owner's avatar. onBeforeRender has the last word because
	/// that is the last point the tracked poses are updated: NetworkTransform samples the
	/// authority in PreUpdate, so a pose written any earlier in the frame is only a staler
	/// version of the same send. Update and LateUpdate write too, so that anything reading a
	/// held weapon's muzzle mid-frame isn't aiming with the previous frame's hand.
	/// </summary>
	public class AvatarPose : NetworkBehaviour
	{
		[SerializeField] private PlayerAvatar avatar;

		// everyone else's copy of this avatar is driven by NetworkTransform instead
		private bool driving;

		private void OnValidate()
		{
			TryGetComponent(out avatar);
		}

		public override void OnNetworkSpawn()
		{
			if (!IsOwner)
				return;

			driving = true;

			// before anything gets a frame of the avatar sitting at the origin
			Apply();
			Application.onBeforeRender += OnBeforeRender;
		}

		public override void OnNetworkDespawn()
		{
			driving = false;
			Application.onBeforeRender -= OnBeforeRender;
		}

		private void Update()
		{
			if (driving) Apply();
		}

		private void LateUpdate()
		{
			if (driving) Apply();
		}

		// runs after the tracked pose drivers have written this frame's poses
		[BeforeRenderOrder(1000)]
		private void OnBeforeRender()
		{
			Apply();
		}

		private void Apply()
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
