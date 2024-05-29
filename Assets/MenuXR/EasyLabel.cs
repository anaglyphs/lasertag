using UnityEngine;
using UnityEngine.UI;

namespace MenuXR
{
    public class EasyLabel : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnValidate()
        {
            GetComponentInChildren<Text>().text = gameObject.name;
        }
#endif
    }
}