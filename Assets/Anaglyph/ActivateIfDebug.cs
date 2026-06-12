using UnityEngine;

namespace Anaglyph
{
	public class ActivateIfDebug : MonoBehaviour
	{
		private void Awake()
		{
			AnaglyphDebug.DebugModeChanged += gameObject.SetActive;
			gameObject.SetActive(AnaglyphDebug.DebugMode);
		}

		private void OnDestroy()
		{
			AnaglyphDebug.DebugModeChanged -= gameObject.SetActive;
		}
	}
}