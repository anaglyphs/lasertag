using UnityEngine;

namespace XRTemplate
{
    public class BodyTrackingCalibrationReset : MonoBehaviour
    {
        public void ResetCalibration()
        {
            OVRBody.ResetBodyTrackingCalibration();
        }
    }
}
