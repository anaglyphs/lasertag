using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	public interface IWeapon
	{
		public GameObject VisualObject { get; }
		
		public void OnFire(InputAction.CallbackContext context);
	}
}