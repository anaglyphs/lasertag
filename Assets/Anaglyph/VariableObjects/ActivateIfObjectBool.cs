using UnityEngine;

namespace VariableObjects
{
	public class ActivateIfObjectBool : MonoBehaviour
	{
		[SerializeField] private BoolObject boolObject;

		private void Awake()
		{
			boolObject.onChange += gameObject.SetActive;
			gameObject.SetActive(boolObject.Value);
		}

		private void OnDestroy()
		{
			boolObject.onChange -= gameObject.SetActive;
		}
	}
}