using UnityEngine;

namespace Anaglyph.XRTemplate
{
    public class BodyTrackingCalibrationReset : MonoBehaviour
    {
        public void ResetCalibration()
        {
            OVRBody.ResetBodyTrackingCalibration();
        }
    }
}
