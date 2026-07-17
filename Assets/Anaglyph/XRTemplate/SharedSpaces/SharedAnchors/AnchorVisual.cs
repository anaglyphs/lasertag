using Anaglyph.Debugging.Visuals;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	[DefaultExecutionOrder(999999)]
	public class AnchorVisual : MonoBehaviour
	{
		private ARAnchor anchor;

		private static Color[] trackingStateColors;

		[RuntimeInitializeOnLoadMethod]
		private static void Init()
		{
			trackingStateColors = new Color[3];

			trackingStateColors[(int)TrackingState.None] = Color.red;
			trackingStateColors[(int)TrackingState.Limited] = Color.yellow;
			trackingStateColors[(int)TrackingState.Tracking] = Color.green;
		}

		private void Awake()
		{
			TryGetComponent(out anchor);
		}

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
			DebugAxisVisual.DrawDebugAxis(transform.position, transform.rotation,
				trackingStateColors[(int)anchor.trackingState]);
		}
	}
}