using UnityEngine;

namespace VariableObjects
{
	[CreateAssetMenu(fileName = "String", menuName = "Variable Objects/String")]
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
