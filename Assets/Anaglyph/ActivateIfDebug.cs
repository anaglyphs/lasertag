using UnityEngine;

namespace Anaglyph
{
	public class ActivateIfDebug : MonoBehaviour
	{
		private void Awake()
		{
			Debug.DebugModeChanged += gameObject.SetActive;
			gameObject.SetActive(Debug.DebugMode);
		}

		private void OnDestroy()
		{
			Debug.DebugModeChanged -= gameObject.SetActive;
		}
	}
}
