using UnityEngine;

namespace Anaglyph.XRTemplate.DisplayCapture.Barcodes
{
	public class Indicator : MonoBehaviour
	{
		[SerializeField] private LineRenderer lineRenderer;
		public LineRenderer LineRenderer => lineRenderer;

		private Vector3[] offsetPositions = new Vector3[4];

		public void Set(BarcodeTracker.Result result) => Set(result.text, result.corners);

		public void Set(string text, Vector3[] corners)
		{
			Vector3 topCenter = (corners[2] + corners[3]) / 2f;
			transform.position = topCenter;

			Vector3 up = (corners[1] - corners[0]).normalized;
			Vector3 right = (corners[2] - corners[1]).normalized;
			Vector3 normal = -Vector3.Cross(up, right).normalized;

			Vector3 center = (corners[2] + corners[0]) / 2f;

			for (int i = 0; i < 4; i++)
			{
				Vector3 dir = (corners[i] - center).normalized;
				offsetPositions[i] = corners[i] + (dir * 0.1f);
			}

			transform.rotation = Quaternion.LookRotation(normal, up);

			lineRenderer.SetPositions(offsetPositions);
		}
	}
}