using UnityEngine;
using UnityEngine.UI;

namespace VariableObjects
{
	public class StringObjectField : MonoBehaviour
	{
		[SerializeField] private StringObject stringObject;
		private InputField field;

		private void Awake()
		{
			TryGetComponent(out field);

			field.onValueChanged.AddListener(stringObject.Set);
			stringObject.AddChangeListenerAndCheck(field.SetTextWithoutNotify);
		}

		private void OnDestroy()
		{
			if(field)
				stringObject.onChange -= field.SetTextWithoutNotify;
		}
	}
}
