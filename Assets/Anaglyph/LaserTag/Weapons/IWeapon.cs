using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	public interface IWeapon
	{
		public void OnFire(InputAction.CallbackContext context);
	}
}