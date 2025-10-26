using UnityEngine;
using UnityEngine.UI;

namespace VariableObjects
{
	public class BoolObjectToggle : MonoBehaviour
	{
		[SerializeField] private BoolObject scriptableBool;
		private Toggle toggle;

		private void Awake()
		{
			TryGetComponent(out toggle);

			toggle.onValueChanged.AddListener(scriptableBool.Set);
			scriptableBool.AddChangeListenerAndCheck(toggle.SetIsOnWithoutNotify);
		}

		private void OnDestroy()
		{
			if(toggle)
				scriptableBool.onChange -= toggle.SetIsOnWithoutNotify;
		}
	}
}
