using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Weapons
{
	public interface IWeapon
	{
		public GameObject VisualObject { get; }
		
		public void OnFire(InputAction.CallbackContext context);
	}
}