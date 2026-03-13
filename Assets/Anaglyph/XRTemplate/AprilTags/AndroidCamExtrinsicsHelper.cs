using System;
using UnityEngine;

namespace Anaglyph.XRTemplate.AprilTags
{
	public static class AndroidCamExtrinsicsHelper
	{
		public static Pose GetCameraExtrinsics(int id)
		{
			using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
			using AndroidJavaObject context = unityPlayer.GetStatic<AndroidJavaObject>("currentContext");

			// using AndroidJavaObject cameraService = context.GetStatic<AndroidJavaObject>("CAMERA_SERVICE");

			AndroidJavaObject cameraManager =
				activity.Call<AndroidJavaObject>("getSystemService", "camera");

			string[] ids = cameraManager.Call<string[]>("getCameraIdList");

			foreach (string cam in ids)
				Debug.Log("Camera ID: " + cam);

			AndroidJavaObject characteristics =
				cameraManager.Call<AndroidJavaObject>("getCameraCharacteristics", id.ToString());

			return ReadExtrinsics(characteristics);
		}

		private static Pose ReadExtrinsics(AndroidJavaObject characteristics)
		{
			using AndroidJavaClass cc = new("android.hardware.camera2.CameraCharacteristics");

			AndroidJavaObject keyRotation = cc.GetStatic<AndroidJavaObject>("LENS_POSE_ROTATION");
			AndroidJavaObject keyTranslation = cc.GetStatic<AndroidJavaObject>("LENS_POSE_TRANSLATION");

			float[] pos = characteristics.Call<float[]>("get", keyTranslation);
			float[] rot = characteristics.Call<float[]>("get", keyRotation);

			if (pos == null || rot == null)
				throw new Exception("Extrinsics not found");

			Vector3 position = new(pos[0], pos[1], -pos[2]);
			Quaternion rotation = Quaternion.Inverse(new Quaternion(-rot[0], -rot[1], rot[2], rot[3])) *
			                      Quaternion.Euler(180, 0, 0);

			return new Pose(position, rotation);
		}
	}
}