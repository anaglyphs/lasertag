using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace VariableObjects
{
	[CreateAssetMenu(fileName = "Float", menuName = "Variable Objects/Float")]
	[MovedFrom("VariableObjects.ScriptableFloat")]
	public class FloatObject : GenericVariableObject<float> {

		[SerializeField] private bool save;
		[SerializeField] private string saveKey;

		protected override void OnEnable()
		{
			if (!save)
				base.OnEnable();
			else
				val = PlayerPrefs.GetFloat(saveKey, defaultVal);

			onChange += Save;
		}

		private void Save(float f)
		{
			PlayerPrefs.SetFloat(saveKey, f);
		}

	}
}
