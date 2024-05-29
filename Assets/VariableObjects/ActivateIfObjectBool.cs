using UnityEngine;

namespace VariableObjects
{
	public class ActivateIfObjectBool : SuperAwakeBehavior
	{
		[SerializeField] private BoolObject boolObject;

		protected override void SuperAwake()
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