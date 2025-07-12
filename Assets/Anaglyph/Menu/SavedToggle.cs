using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class SavedToggle : MonoBehaviour
    {
		[SerializeField] private string key;
		[SerializeField] private bool defaultValue;

		private void Awake()
		{
			TryGetComponent(out Toggle toggle);

			toggle.isOn = (PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) == 1);

			toggle.onValueChanged.AddListener((bool isOn) => PlayerPrefs.SetInt(key, isOn ? 1 : 0));
		}
	}
}
