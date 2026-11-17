using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// Copies the local rig onto the owner's avatar. Update runs right after the hands are
	/// placed and before gameplay scripts, so a held weapon's muzzle is current when it fires.
	/// onBeforeRender has the last word because that is the last point the tracked poses are
	/// updated; the pose written there is what gets rendered and sent.
	///
	/// Poses travel in the shared frame: each headset's rig is already aligned to it (drift
	/// corrections included, eased in by the colocator), so the avatar root sits at the
	/// origin on every peer and the owner's world poses are what everyone sees.
	/// </summary>
	[DefaultExecutionOrder(-9998)]
	public class AvatarPose : NetworkBehaviour
	{
		[SerializeField] private PlayerAvatar avatar;

		// everyone else's copy of this avatar is driven by the owner's synced poses instead
		private bool driving;

		private void OnValidate()
		{
			TryGetComponent(out avatar);
		}

		private void PlaceRoot()
		{
			if (!IsSpawned) return;
			transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
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
			PlaceRoot();
			if (driving) Apply();
		}

		private void LateUpdate()
		{
			PlaceRoot();
			if (driving) Apply();
		}

		// runs after the tracked pose drivers and hand movers have written this frame's poses
		[BeforeRenderOrder(1000)]
		private void OnBeforeRender()
		{
			PlaceRoot();
			Apply();
		}

		private void Apply()
		{
			if (!driving || !avatar.HasSpatialPresence) return;
			LocalRig rig = LocalRig.Instance;

			if (rig == null)
				return;

			// on a phone the head is the phone: its tracking is the only body pose there is
			avatar.HeadTransform.SetWorldPose(rig.Head.GetWorldPose());
			avatar.LeftHandTransform.SetWorldPose(rig.LeftHand.GetWorldPose());
			avatar.RightHandTransform.SetWorldPose(rig.RightHand.GetWorldPose());
		}
	}
}
