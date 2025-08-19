using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace VariableObjects
{
	[CreateAssetMenu(fileName = "String", menuName = "Variable Objects/String")]
	[MovedFrom("VariableObjects.ScriptableBool")]
	public class StringObject : GenericVariableObject<string>
	{
		[SerializeField] private bool save;
		[SerializeField] private string saveKey;

		protected override void OnEnable()
		{
			if (!save)
				base.OnEnable();
			else
				val = PlayerPrefs.GetString(saveKey, defaultVal);

			onChange += Save;
		}

		private void Save(string str)
		{
			PlayerPrefs.SetString(saveKey, str);
		}
	}
}
