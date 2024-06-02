using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class OculusSystemKeyboardJankFix : MonoBehaviour, ISelectHandler
{
	private InputField field;

	public void OnSelect(BaseEventData eventData)
	{
		ShowKeyboardWithDelay(500);
	}

	private void Awake()
	{
		field = GetComponent<InputField>();
	}

	private async void ShowKeyboardWithDelay(int ms)
	{
		await Task.Delay(ms);

		if (!TouchScreenKeyboard.visible)
		{
			field.ActivateInputField();
			TouchScreenKeyboard.Open(field.text, TouchScreenKeyboardType.ASCIICapable);
		}
	}
}

