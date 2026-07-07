using UnityEngine;

namespace Anaglyph
{
	public class ActivateIfDebug : MonoBehaviour
	{
		private void Awake()
		{
			AnaglyphDebugging.DebugModeChanged += gameObject.SetActive;
			gameObject.SetActive(AnaglyphDebugging.DebugMode);
		}

		private void OnDestroy()
		{
			AnaglyphDebugging.DebugModeChanged -= gameObject.SetActive;
		}
	}
}