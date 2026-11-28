using System;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	[RequireComponent(typeof(UIToolkitPanelXRSetup))]
	public class MenuCloseBar : MonoBehaviour
	{
		private readonly UIEventBindings bindings = new();

		private void OnEnable()
		{
			MainMenuController menu = GetComponentInParent<MainMenuController>();
			VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
			if (menu == null || root == null)
				throw new InvalidOperationException(
					"MenuCloseBar requires a parent MainMenuController and an enabled UIDocument with a visual tree.");

			bindings.Click(Require<Button>(root, "close-button"), () => menu.SetVisible(false));
		}

		private void OnDisable() => bindings.Dispose();
	}
}
