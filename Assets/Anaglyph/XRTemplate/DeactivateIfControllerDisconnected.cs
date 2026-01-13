using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.XRTemplate
{
	public class DeactivateIfControllerUntracked : MonoBehaviour
	{
		[SerializeField] private InputActionProperty action;

		private void Awake()
		{
			action.action.Enable();
			action.action.performed += OnPerformed;
		}

		private void OnPerformed(InputAction.CallbackContext ctx)
		{
			bool b = ctx.ReadValueAsButton();
			gameObject.SetActive(b);
		}

		private void OnDestroy()
		{
			action.action.Disable();
		}
	}
}