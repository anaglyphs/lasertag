using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class AprilTagAnchor : NetworkBehaviour
    {
		public NetworkVariable<int> idSync = new();

        public NetworkVariable<NetworkPose> desiredPoseSync = new();
        public Pose DesiredPose => desiredPoseSync.Value;

		public override void OnNetworkSpawn()
		{
			AprilTagColocator.foundTags.Add(idSync.Value, this);
		}
	}
}
