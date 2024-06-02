using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace VariableObjects
{
	[MovedFrom("VariableObjects.ScriptableBoolToggle")]
	public class BoolObjectToggle : MonoBehaviour
	{
		[SerializeField] private BoolObject scriptableBool;
		private Toggle toggle;

		private void Awake()
		{
			toggle = GetComponent<Toggle>();

			toggle.onValueChanged.AddListener(scriptableBool.Set);
			scriptableBool.AddChangeListenerAndCheck(toggle.SetIsOnWithoutNotify);
		}

		private void OnDestroy()
		{
			scriptableBool.onChange -= toggle.SetIsOnWithoutNotify;
		}
	}
}
