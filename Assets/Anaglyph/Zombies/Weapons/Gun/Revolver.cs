using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.Zombies
{
    public class Revolver : MonoBehaviour
    {
		[SerializeField] private GameObject bullet;
		[SerializeField] private Transform emitter;

		public UnityEvent FireEvent = new();

		public void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
			{
				Instantiate(bullet, emitter.position, emitter.rotation);
				FireEvent.Invoke();
			}
		}
	}
}
