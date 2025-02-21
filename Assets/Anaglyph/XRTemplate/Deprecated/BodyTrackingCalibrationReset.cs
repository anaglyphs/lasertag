//using UnityEngine;

//namespace Anaglyph.XRTemplate
//{
//	public class BodyTrackingCalibrationReset : MonoBehaviour
//	{

//		private void Start()
//		{
//			OVRManager.display.RecenteredPose += ResetCalibration;
//		}

//        private void OnDestroy()
//        {
//            OVRManager.display.RecenteredPose -= ResetCalibration;
//        }

//        public void ResetCalibration()
//		{
//			OVRBody.ResetBodyTrackingCalibration();
//		}
//	}
//}
