using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class NumberField : MonoBehaviour
    {
		public UnityEvent<float> OnValueChanged = new();
		public UnityEvent<float> OnEndEdit = new();
		public UnityEvent<float> OnSubmit = new();

		private InputField field;

		private void Awake()
		{
			field = GetComponent<InputField>();
			field.onValueChanged.AddListener(ValueChanged);
			field.onEndEdit.AddListener(EditEnded);
			field.onSubmit.AddListener(Submitted);
		}

		private void ValueChanged(string str) => Parse(str, OnValueChanged);
		private void EditEnded(string str) => Parse(str, OnEndEdit);
		private void Submitted(string str) => Parse(str, OnSubmit);

		private void Parse(string str, UnityEvent<float> callback)
		{
			if (float.TryParse(str, out float result))
				callback.Invoke(result);
		}

		public void SetFloat(float f)
		{
			field.text = f.ToString();
		}
	}
}
