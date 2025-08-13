using UnityEngine;
using UnityEngine.UI;
using VariableObjects;

namespace Anaglyph.VariableObjects
{
    public class FloatObjectField : MonoBehaviour
    {
		[SerializeField] private FloatObject floatObject;
		private InputField field;

		private void Awake()
		{
			field = GetComponent<InputField>();

			field.onValueChanged.AddListener(OnFieldChanged);
			floatObject.AddChangeListenerAndCheck(OnFloatObjectChanged);
		}

		private void OnDestroy()
		{
			floatObject.onChange -= OnFloatObjectChanged;
		}

		private void OnFieldChanged(string str)
		{
			if(float.TryParse(str, out float f))
				floatObject.Value = f;
		}

		private void OnFloatObjectChanged(float f)
		{
			field.SetTextWithoutNotify(f.ToString());
		}
	}
}
