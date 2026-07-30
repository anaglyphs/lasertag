using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Input
{
    public class InputBroadcaster : MonoBehaviour
    {
        [SerializeField] private InputActionMap actionMap;

		private void Awake()
		{
			foreach (InputAction action in actionMap.actions)
			{
				action.started += OnActionTriggered;
				action.performed += OnActionTriggered;
				action.canceled += OnActionTriggered;
			}
		}

		private void OnEnable()
		{
			actionMap.Enable();
		}

		private void OnDisable()
		{
			actionMap.Disable();
		}

		private void OnActionTriggered(InputAction.CallbackContext context)
		{
			gameObject.BroadcastMessage(context.action.name, context, 
				SendMessageOptions.DontRequireReceiver);
		}
	}
}
