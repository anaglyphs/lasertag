using UnityEngine;

namespace Anaglyph
{
    public class ActivateIfDebug : MonoBehaviour
    {
		private void Awake()
		{
			Anaglyph.DebugModeChanged += gameObject.SetActive;
			gameObject.SetActive(Anaglyph.DebugMode);
		}

		private void OnDestroy()
		{
			Anaglyph.DebugModeChanged -= gameObject.SetActive;
		}
	}
}
