using UnityEngine;

namespace Anaglyph
{
    public class RenderIfDebug : MonoBehaviour
    {
		private new Renderer renderer;

		private void OnEnable()
		{
			renderer = GetComponent<Renderer>();

			Anaglyph.DebugModeChanged += OnDebugModeChanged;
		}

		private void OnDisable()
		{
			Anaglyph.DebugModeChanged -= OnDebugModeChanged;
		}

		private void OnDebugModeChanged(bool on)
		{
			renderer.enabled = on;
		}
	}
}
