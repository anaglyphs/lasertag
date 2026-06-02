using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class DebugModeToggle : MonoBehaviour
    {
		private Toggle toggle;

		private void Awake()
		{
			toggle = GetComponent<Toggle>();
			Debug.DebugModeChanged += toggle.SetIsOnWithoutNotify;
			toggle.onValueChanged.AddListener(Debug.SetDebugMode);
		}
	}
}
