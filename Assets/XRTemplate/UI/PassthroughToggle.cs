using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XRTemplate
{
    public class PassthroughToggle : MonoBehaviour
    {
        private Toggle toggle;

		private void Awake()
		{
			toggle = GetComponent<Toggle>();
		}

		private void Start()
		{
			toggle.onValueChanged.AddListener(PassthroughManager.SetPassthrough);
		}

		private void OnEnable()
		{
			toggle.SetIsOnWithoutNotify(PassthroughManager.PassthroughOn);

			PassthroughManager.OnPassthroughChange += toggle.SetIsOnWithoutNotify;
		}

		private void OnDisable()
		{
			PassthroughManager.OnPassthroughChange -= toggle.SetIsOnWithoutNotify;
		}
	}
}
