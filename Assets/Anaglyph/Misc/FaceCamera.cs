using UnityEngine;

namespace Anaglyph
{
    [DefaultExecutionOrder(30000)]
    public class FaceCamera : MonoBehaviour
    {
		private static Camera mainCamera;

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private void LateUpdate()
		{
			transform.LookAt(mainCamera.transform.position);
		}
	}
}
