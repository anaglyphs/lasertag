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
			Vector3 direction = transform.position - mainCamera.transform.position;
			transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
		}
	}
}