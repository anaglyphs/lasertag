using System;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class InvokeToggleEventOnEnable : MonoBehaviour
    {
        private Toggle _toggle;

        private void Awake()
        {
            _toggle = GetComponent<Toggle>();
        }

        private void OnEnable()
        {
            _toggle.onValueChanged.Invoke(_toggle.isOn);
        }
    }
}
