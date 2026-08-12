using UnityEngine;

namespace Anaglyph.VariableObjects
{
	public class ActivateIfObjectBool : MonoBehaviour
	{
		[SerializeField] private BoolObject boolObject;

		private void Awake()
		{
			boolObject.Changed += gameObject.SetActive;
			gameObject.SetActive(boolObject.Value);
		}

		private void OnDestroy()
		{
			boolObject.Changed -= gameObject.SetActive;
		}
	}
}