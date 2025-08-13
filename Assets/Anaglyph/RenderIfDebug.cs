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
			Anaglyph.DebugModeChanged += OnDebugModeChanged;
			renderer.enabled = Anaglyph.DebugMode;
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
