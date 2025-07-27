using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace VariableObjects
{

	[CreateAssetMenu(fileName = "Bool", menuName = "Variable Objects/Bool")]
	[MovedFrom("VariableObjects.ScriptableBool")]
	public class BoolObject : GenericVariableObject<bool>
	{
		[SerializeField] private bool save;
		[SerializeField] private string saveKey;

		protected override void OnEnable()
		{
			if (!save)
				base.OnEnable();
			else
				val = PlayerPrefs.GetInt(saveKey, defaultVal ? 1 : 0) == 1;

			onChange += Save;
		}

		private void Save(bool b)
		{
			PlayerPrefs.SetInt(saveKey, b ? 1 : 0);
		}
	}
}
