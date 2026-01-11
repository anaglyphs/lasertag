using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class DisplayAtDistance : MonoBehaviour
	{
		public float minDistance = 3f;
		private Renderer[] renderers;
		private Camera cam;

		private void OnEnable()
		{
			renderers = GetComponentsInChildren<Renderer>();
			cam = Camera.main;
		}

		private void LateUpdate()
		{
			Vector3 camPos = cam.transform.position;
			bool withinDistance = Vector3.Distance(transform.position, camPos) < minDistance;

			foreach (Renderer renderer in renderers) renderer.enabled = withinDistance;
		}
	}
}