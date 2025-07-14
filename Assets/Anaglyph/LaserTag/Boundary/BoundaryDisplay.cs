using Anaglyph.XRTemplate;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class BoundaryDisplay : MonoBehaviour
    {
		[SerializeField] private float radius = 20;

		private new Camera camera;

		private void Awake()
		{
			camera = Camera.main;
		}

		private void LateUpdate()
		{
			Vector3 camPos = camera.transform.localPosition;
			Vector3 camPosFlat = new Vector3(camPos.x, 0, camPos.z);

			if (camPosFlat.magnitude == 0)
				return;

			Quaternion rot = Quaternion.LookRotation(camPosFlat, Vector3.up);

			transform.localPosition = (rot * (Vector3.forward * radius)) + (Vector3.up * camPos.y);
			transform.localRotation = rot;
		}
	}
}
