using Anaglyph.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class BodyTrackingManager : MonoBehaviour
	{
		private static string LogPrefix = "[BodyTrackingManager]";

		private void Start()
		{
			OVRManager.display.RecenteredPose += ResetCalibration;
			NetcodeManagement.StateChange += OnNetcodeStateChanged;
		}

		private void OnDestroy()
		{
			if(OVRManager.display != null)
				OVRManager.display.RecenteredPose -= ResetCalibration;

			NetcodeManagement.StateChange -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeManagement.NetworkState state)
		{
			if(state == NetcodeManagement.NetworkState.Connected)
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
