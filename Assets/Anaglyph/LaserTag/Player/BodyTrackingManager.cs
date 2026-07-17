using System.Collections.Generic;
using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
	public class BodyTrackingManager : MonoBehaviour
	{
		// private static string LogPrefix = "[BodyTrackingManager]";

		private readonly List<XRInputSubsystem> xrSubsystems = new();

		private void Start()
		{
			if (Application.isEditor)
				return;

			SubsystemManager.GetSubsystems(xrSubsystems);
			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated += OnRecenter;

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private void OnDestroy()
		{
			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated -= OnRecenter;

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnRecenter(XRInputSubsystem subsystem)
		{
			ResetCalibration();
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			if (state == NetcodeState.Connected)
				ResetCalibration();
		}

		public void ResetCalibration()
		{
			// NO OP for now

			// bool didReset = OVRBody.ResetBodyTrackingCalibration();
			// if (didReset)
			// 	UnityEngine.Debug.Log($"{LogPrefix} reset");
			// else
			// 	UnityEngine.Debug.LogError($"{LogPrefix} failed to reset!");
		}
	}
}