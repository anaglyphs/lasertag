using Anaglyph.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class BodyTrackingManager : MonoBehaviour
	{
		private static string LogPrefix = "[BodyTrackingManager]";

		private void Start()
		{
			if (Application.isEditor)
				return;
			
			OVRManager.display.RecenteredPose += ResetCalibration;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private void OnDestroy()
		{
			if(OVRManager.display != null)
				OVRManager.display.RecenteredPose -= ResetCalibration;

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			if(state == NetcodeState.Connected)
				ResetCalibration();
		}

		public void ResetCalibration()
		{
			bool didReset = OVRBody.ResetBodyTrackingCalibration();
			if (didReset)
				Debug.Log($"{LogPrefix} reset");
			else
				Debug.LogError($"{LogPrefix} failed to reset!");
		}
	}
}
