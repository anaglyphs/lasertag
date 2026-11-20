using UnityEngine;

namespace Anaglyph.VariableObjects
{
	[CreateAssetMenu(fileName = "Integer", menuName = "Variable Objects/Integer")]
	public class IntObject : GenericVariableObject<int>
	{
		[SerializeField] private bool save;
		[SerializeField] private string saveKey;

		protected override void OnEnable()
		{
			if (!save)
				base.OnEnable();
			else
				val = PlayerPrefs.GetInt(saveKey, defaultVal);

			Changed += Save;
		}

		private void Save(int b)
		{
			PlayerPrefs.SetInt(saveKey, b);
		}
	}
}
