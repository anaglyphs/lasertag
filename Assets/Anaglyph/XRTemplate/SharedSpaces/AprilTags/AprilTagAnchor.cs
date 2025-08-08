using Anaglyph.Netcode;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class AprilTagAnchor : NetworkBehaviour
    {
		public static Dictionary<int, AprilTagAnchor> AllAnchors;

		public NetworkVariable<int> idSync = new();
		public int ID => idSync.Value;

        public NetworkVariable<NetworkPose> desiredPoseSync = new();
        public Pose DesiredPose => desiredPoseSync.Value;

		public NetworkVariable<bool> isLockedSync = new();
		public bool IsLocked => isLockedSync.Value;

		public override void OnNetworkSpawn()
		{
			AllAnchors.TryAdd(idSync.Value, this);
		}

		public override void OnNetworkDespawn()
		{
			AllAnchors.Remove(idSync.Value);
		}
	}
}
