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
			Debug.DebugModeChanged += OnDebugModeChanged;
			renderer.enabled = Debug.DebugMode;
		}

		private void OnDisable()
		{
			Debug.DebugModeChanged -= OnDebugModeChanged;
		}

		private void OnDebugModeChanged(bool on)
		{
			renderer.enabled = on;
		}
	}
}
