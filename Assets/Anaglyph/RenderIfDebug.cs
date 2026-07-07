using UnityEngine;

namespace Anaglyph
{
	public class RenderIfDebug : MonoBehaviour
	{
		private new Renderer renderer;

		private void Awake()
		{
			renderer = GetComponent<Renderer>();
		}

		private void OnEnable()
		{
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			renderer.enabled = AnaglyphDebugging.DebugMode;
		}

		private void OnDisable()
		{
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
		}

		private void OnDebugModeChanged(bool on)
		{
			renderer.enabled = on;
		}
	}
}