using UnityEngine;
using Utilities.XR;

namespace Anaglyph.Debugging.Visuals
{
	[DefaultExecutionOrder(999999)]
	public class DebugAxisVisual : MonoBehaviour
	{
		public Color color = Color.white;
		public float scale = 1.0f;

		private void Start()
		{
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChange;
			OnDebugModeChange(AnaglyphDebugging.DebugMode);
		}

		private void OnDestroy()
		{
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChange;
		}

		private void OnDebugModeChange(bool on)
		{
			enabled = on;
		}

		private void LateUpdate()
		{
			DrawDebugAxis(transform.position, transform.rotation, color, scale);
		}

		public static void DrawDebugAxis(Vector3 position, Quaternion rotation, Color color, float scale = 1f)
		{
			Vector3 up = position + rotation * Vector3.up * 0.75f * scale;
			Vector3 right = position + rotation * Vector3.right * 0.5f * scale;
			Vector3 forward = position + rotation * Vector3.forward * scale;

			XRGizmos.DrawLine(position, up, color);
			XRGizmos.DrawLine(position, right, color);
			XRGizmos.DrawLine(position, forward, color);
		}
	}
}