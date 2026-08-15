using Anaglyph.Debugging;
using UnityEngine;

namespace Anaglyph
{
	public class RenderIfDebug : MonoBehaviour
	{
		private Renderer rend;

		private void Awake()
		{
			rend = GetComponent<Renderer>();
		}

		private void OnEnable()
		{
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			rend.enabled = AnaglyphDebugging.DebugMode;
		}

		private void OnDisable()
		{
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
		}

		private void OnDebugModeChanged(bool on)
		{
			rend.enabled = on;
		}
	}
}