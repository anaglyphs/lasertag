using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class SavedInputField : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private string defaultValue;

        private void Awake()
        {
			TryGetComponent(out InputField field);

            field.text = PlayerPrefs.GetString(key, defaultValue);
            
            field.onValueChanged.AddListener(delegate(string str)
            {
                PlayerPrefs.SetString(key, str);
            });
        }
    }
}
