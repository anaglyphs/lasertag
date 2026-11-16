using Anaglyph.Menu;
using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(-1000)]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(PanelInputConfiguration))]
	[RequireComponent(typeof(XRUIToolkitManager))]
	public sealed class UIToolkitMenuInputSetup : MonoBehaviour
	{
		private const string BackActionName = "OnBack";

		private void Awake()
		{
			PanelInputConfiguration configuration =
				GetComponent<PanelInputConfiguration>();
			configuration.processWorldSpaceInput = true;
			configuration.panelInputRedirection =
				PanelInputConfiguration.PanelInputRedirection.Never;
			configuration.autoCreatePanelComponents = true;

			XRUIInputModule inputModule = FindFirstObjectByType<XRUIInputModule>();
			if (inputModule != null)
				inputModule.bypassUIToolkitEvents = false;
		}

		private void OnEnable()
		{
			HandInput.Registered += ListenForBack;
			foreach (HandInput hand in HandInput.Hands.Values)
				ListenForBack(hand);
		}

		private void OnDisable()
		{
			HandInput.Registered -= ListenForBack;
			foreach (HandInput hand in HandInput.Hands.Values)
				if (hand != null && hand.Actions.FindAction(BackActionName) is InputAction back)
					back.performed -= GoBackInRayHoveredMenu;
		}

		// Registered fires on every enable of a hand, so drop any earlier subscription first
		private void ListenForBack(HandInput hand)
		{
			if (hand == null || hand.Actions.FindAction(BackActionName) is not InputAction back) return;
			back.performed -= GoBackInRayHoveredMenu;
			back.performed += GoBackInRayHoveredMenu;
		}

		// back a page in the hovered panel; from a main menu panel's first pages, close the main menu
		private static void GoBackInRayHoveredMenu(InputAction.CallbackContext context)
		{
			foreach (HandInput hand in HandInput.Hands.Values)
			{
				if (hand == null || hand.Actions.FindAction(BackActionName) != context.action) continue;
				UIDocument document = hand.RayHoveredDocument;
				if (document == null ||
				    NavView.TryGoBackUnderPointerIn(document.rootVisualElement) != NavView.PointerBackResult.AllAtFirstPage) return;

				MainMenuController mainMenu = document.GetComponentInParent<MainMenuController>();
				if (mainMenu != null && mainMenu.IsVisible && mainMenu.Gate.Access == GateMenu.AccessState.Granted)
					mainMenu.SetVisible(false);
				return;
			}
		}
	}
}
