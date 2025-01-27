using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class SimpleDesiredPose : MonoBehaviour, IDesiredPose
    {
        [SerializeField] private Pose desiredPose = Pose.identity;
        public Pose DesiredPose => desiredPose;
    }
}
