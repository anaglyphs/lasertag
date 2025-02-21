using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	/// <summary>
	/// Passthrough management for multiple headset platforms
	/// </summary>
	public static class PassthroughManager
	{
		public static event Action<bool> OnPassthroughChange = delegate { };
		public static bool PassthroughOn { get; private set; }

		public static void SetPassthrough(bool on)
		{
#if USE_OCULUS_XR_PACKAGE
			EnablePassthroughOculus(on);
#endif
		}

#if USE_OCULUS_XR_PACKAGE
		private static void EnablePassthroughOculus(bool on)
		{
			if(OVRManager.instance == null)
			{
				throw new Exception("OVRManager isn't initialized.");
			}

#if UNITY_EDITOR
			EnablePassthroughOculusDelayedInEditor(on);
#else
			PassthroughOn = on;

			OVRManager.instance.isInsightPassthroughEnabled = on;
			OnPassthroughChange.Invoke(on);

			ChangeCameraBackground(on);
#endif
        }

#if UNITY_EDITOR
		private static async void EnablePassthroughOculusDelayedInEditor(bool on)
		{
            OVRManager.instance.isInsightPassthroughEnabled = false;

            await Task.Delay(500);

            PassthroughOn = on;

            OVRManager.instance.isInsightPassthroughEnabled = on;
            OnPassthroughChange.Invoke(on);

            ChangeCameraBackground(on);
        }
#endif

#endif

		private static void ChangeCameraBackground(bool on)
		{
			Camera camera = Camera.main;
			camera.backgroundColor = new Color(0, 0, 0, 0);
			camera.clearFlags = on ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
		}
	}
}
