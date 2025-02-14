using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class SimpleAnchor : MonoBehaviour, IAnchor
    {
        [SerializeField] private Pose desiredPose = Pose.identity;

        public bool Anchored => true;
        public Pose TrackedPose => transform.GetWorldPose();
        public Pose AnchoredPose => desiredPose;
	}
}
