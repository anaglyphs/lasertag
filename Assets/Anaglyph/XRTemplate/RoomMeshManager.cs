using System;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public static class RoomMeshManager
	{
		public static event Action<GameObject> OnSceneMeshLoaded = delegate { };

		public static void LoadRoomMesh()
		{
#if USE_OCULUS_XR_PACKAGE
			LoadRoomMeshOculus();
#endif
		}

		public static void ScanRoomMesh()
		{
#if USE_OCULUS_XR_PACKAGE
			ScanRoomMeshOculus();
#endif
		}

#if USE_OCULUS_XR_PACKAGE
		private static OVRSceneManager oculusSceneManager;

		private static bool GetOculusSceneManager()
		{
			if (oculusSceneManager == null)
			{
				oculusSceneManager = GameObject.FindObjectOfType<OVRSceneManager>();
			}

			return oculusSceneManager != null;
		}

		private static void LoadRoomMeshOculus()
		{
			if (!GetOculusSceneManager()) return;

			oculusSceneManager.SceneCaptureReturnedWithoutError -= LoadRoomMeshOculus;
			oculusSceneManager.NoSceneModelToLoad += ScanRoomMeshOculus;
			oculusSceneManager.LoadSceneModel();

		}

		private static void ScanRoomMeshOculus()
		{
			if (!GetOculusSceneManager()) return;

			oculusSceneManager.NoSceneModelToLoad -= ScanRoomMeshOculus;
			oculusSceneManager.SceneCaptureReturnedWithoutError += LoadRoomMeshWithoutScanOculus;
			oculusSceneManager.RequestSceneCapture();
		}

		private static void LoadRoomMeshWithoutScanOculus()
		{
			oculusSceneManager.SceneCaptureReturnedWithoutError -= LoadRoomMeshWithoutScanOculus;
			oculusSceneManager.LoadSceneModel();

		}
#endif
	}
}
