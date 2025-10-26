using UnityEngine;
using UnityEngine.UI;

namespace VariableObjects
{
	public class BoolObjectToggle : MonoBehaviour
	{
		[SerializeField] private BoolObject scriptableBool;
		private Toggle toggle;

		private void Start()
		{
			toggle = GetComponent<Toggle>();
			scriptableBool.AddChangeListenerAndCheck(OnValueChange);
			toggle.onValueChanged.AddListener(scriptableBool.Set);
		}

		private void OnValueChange(bool b) => toggle.isOn = b;

		private void OnDestroy()
		{
			scriptableBool.onChange -= OnValueChange;
		}
	}
}
