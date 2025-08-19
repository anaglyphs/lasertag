using UnityEngine;
using UnityEngine.UI;
using VariableObjects;

namespace Anaglyph.VariableObjects
{
	public class StringObjectField : MonoBehaviour
	{
		[SerializeField] private StringObject stringObject;
		private InputField field;

		private void Awake()
		{
			field = GetComponent<InputField>();

			field.onValueChanged.AddListener(stringObject.Set);
			stringObject.AddChangeListenerAndCheck(field.SetTextWithoutNotify);
		}

		private void OnDestroy()
		{
			stringObject.onChange -= field.SetTextWithoutNotify;
		}
	}
}
