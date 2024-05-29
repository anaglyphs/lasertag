using UnityEngine;
using UnityEngine.UI;

namespace MenuXR
{
    public class SavedTextfield : MonoBehaviour
    {
        [SerializeField] private InputField field;
        [SerializeField] private string key;
        [SerializeField] private string defaultValue;

        private void Start()
        {
            field.text = PlayerPrefs.GetString(key, defaultValue);
            
            field.onValueChanged.AddListener(delegate(string str)
            {
                PlayerPrefs.SetString(key, str);
            });
        }
    }
}
