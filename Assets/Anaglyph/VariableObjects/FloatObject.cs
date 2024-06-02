using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace VariableObjects
{
	[CreateAssetMenu(fileName = "Float", menuName = "Variable Objects/Float")]
	[MovedFrom("VariableObjects.ScriptableFloat")]
	public class FloatObject : GenericVariableObject<bool> { }
}
