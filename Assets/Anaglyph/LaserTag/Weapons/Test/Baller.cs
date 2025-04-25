using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
    public class Baller : MonoBehaviour
    {
        [SerializeField] private GameObject ballPrefab;
		[SerializeField] private float speed;

		private void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
				Fire();
		}

		private void Fire()
		{
			GameObject g = Instantiate(ballPrefab, transform.position, transform.rotation);
			g.GetComponent<Rigidbody>().linearVelocity = transform.forward * speed;
		}
	}
}
