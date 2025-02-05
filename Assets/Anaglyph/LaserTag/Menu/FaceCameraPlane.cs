using UnityEngine;

namespace Anaglyph
{
    [DefaultExecutionOrder(30000)]
    public class FaceCameraPlane : MonoBehaviour
    {
		private static Camera mainCamera;

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private void LateUpdate()
		{
			transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward, Vector3.up);
		}
	}
}
