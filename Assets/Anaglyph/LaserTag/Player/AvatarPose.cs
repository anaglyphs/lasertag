using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// Copies the local rig onto the owner's avatar. onBeforeRender has the last word because
	/// that is the last point the tracked poses are updated: NetworkTransform samples the
	/// authority in PreUpdate, so a pose written any earlier in the frame is only a staler
	/// version of the same send. Update and LateUpdate write too, so that anything reading a
	/// held weapon's muzzle mid-frame isn't aiming with the previous frame's hand.
	///
	/// Poses travel in the shared frame: each headset's rig is already aligned to it (drift
	/// corrections included, eased in by the colocator), so the avatar root sits at the
	/// origin on every peer and the owner's world poses are what everyone sees.
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

		private void Awake()
		{
			// set identically on every peer before spawn
			foreach (NetworkTransform networkTransform in GetComponentsInChildren<NetworkTransform>(true))
				if (networkTransform.transform != transform)
					networkTransform.InLocalSpace = true;
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

		// runs after the tracked pose drivers have written this frame's poses
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

			avatar.HeadTransform.SetWorldPose(rig.Head.GetWorldPose());
			avatar.LeftHandTransform.SetWorldPose(rig.LeftHand.GetWorldPose());
			avatar.RightHandTransform.SetWorldPose(rig.RightHand.GetWorldPose());
		}
	}
}
