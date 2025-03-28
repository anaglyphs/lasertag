using UnityEngine;

namespace Anaglyph.XRTemplate.CameraReader.AprilTags
{
	public class TagTrackerTest : MonoBehaviour
	{
		async void Start()
		{
			while (OVRManager.instance == null)
				await Awaitable.NextFrameAsync();

			PassthroughManager.SetPassthrough(true);
			CameraManager.Instance.Configure(0, 320, 240);
			CameraManager.Instance.StartCapture();
		}
	}
}
