using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace VariableObjects
{

	[CreateAssetMenu(fileName = "Bool", menuName = "Variable Objects/Bool")]
	[MovedFrom("VariableObjects.ScriptableBool")]
	public class BoolObject : GenericVariableObject<bool> { }
}
