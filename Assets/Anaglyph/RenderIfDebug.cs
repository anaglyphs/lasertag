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
			AnaglyphDebug.DebugModeChanged += OnDebugModeChanged;
			renderer.enabled = AnaglyphDebug.DebugMode;
		}

		private void OnDisable()
		{
			AnaglyphDebug.DebugModeChanged -= OnDebugModeChanged;
		}

		private void OnDebugModeChanged(bool on)
		{
			renderer.enabled = on;
		}
	}
}