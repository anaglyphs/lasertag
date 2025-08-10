using Anaglyph.Netcode;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class AprilTagAnchor : NetworkBehaviour
    {
		public static Dictionary<int, AprilTagAnchor> AllAnchors = new();

		public NetworkVariable<int> idSync = new();
		public int ID => idSync.Value;

        public NetworkVariable<NetworkPose> desiredPoseSync = new();
        public Pose DesiredPose => desiredPoseSync.Value;

		public NetworkVariable<bool> isLockedSync = new();
		public bool IsLocked => isLockedSync.Value;

		public Pose GetPose()
		{
			return transform.GetWorldPose();
		}

		public void SetTrackingRelativePose(Pose pose)
		{
			transform.position = MainXROrigin.Transform.worldToLocalMatrix * pose.position;
			transform.rotation = Quaternion.Inverse(MainXROrigin.Transform.rotation) * pose.rotation;
		}

		public override void OnNetworkSpawn()
		{
			AllAnchors.TryAdd(ID, this);
		}

		public override void OnNetworkDespawn()
		{
			AllAnchors.Remove(ID);
		}
	}
}
