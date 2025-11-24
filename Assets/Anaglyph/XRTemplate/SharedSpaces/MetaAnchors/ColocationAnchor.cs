using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	[RequireComponent(typeof(OVRSpatialAnchor), typeof(NetworkedAnchor), typeof(WorldLockAnchor))]
	public class ColocationAnchor : NetworkBehaviour
	{
		private readonly NetworkVariable<Pose> targetPoseSync = new();

		private OVRSpatialAnchor anchor;
		public WorldLockAnchor WorldLocker { get; private set; }

		private void Awake()
		{
			anchor = GetComponent<OVRSpatialAnchor>();
			WorldLocker = GetComponent<WorldLockAnchor>();
		}

		public override void OnNetworkSpawn()
		{
			if (!XRSettings.enabled && !anchor.Localized)
				MainXRRig.TrackingSpace.position = new Vector3(0, 1000, 0);

			targetPoseSync.OnValueChanged += delegate { SetTargetPose(); };
			if (IsOwner)
				targetPoseSync.Value = transform.GetWorldPose();
		}

		private void SetTargetPose()
		{
			var t = targetPoseSync.Value;
			var mat = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
			WorldLocker.SetTargetAndAlign(mat);
		}
	}
}