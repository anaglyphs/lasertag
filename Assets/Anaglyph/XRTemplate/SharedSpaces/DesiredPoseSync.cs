using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class DesiredPoseSync : NetworkBehaviour, IDesiredPose
	{
		public NetworkVariable<NetworkPose> desiredPoseSync = new(new NetworkPose(Pose.identity));
		public Pose DesiredPose => desiredPoseSync.Value;
	}
}
