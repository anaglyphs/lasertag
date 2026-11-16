using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Anaglyph.XR.Input
{
	/// <summary>Whether the screen's primary touch is on a screen-space UI Toolkit element: on phones a tap both fires and presses buttons.</summary>
	public static class ScreenSpaceUIHit
	{
		public static bool IsPrimaryTouchOverUI() => PickedUnderPrimaryTouch() != null;

		/// <summary>The screen-space element under the primary touch; null over the view.</summary>
		public static VisualElement PickedUnderPrimaryTouch()
		{
			Touchscreen touchscreen = Touchscreen.current;
			if (touchscreen == null) return null;
			Vector2 screen = touchscreen.primaryTouch.position.ReadValue();
			return Picked(new Vector2(screen.x, Screen.height - screen.y));
		}

		/// <summary><paramref name="screenTopLeft"/> in pixels from the screen's top-left corner, as panels count them.</summary>
		public static bool IsOverUI(Vector2 screenTopLeft) => Picked(screenTopLeft) != null;

		private static VisualElement Picked(Vector2 screenTopLeft)
		{
			foreach (UIDocument document in Object.FindObjectsByType<UIDocument>())
			{
				if (!document.isActiveAndEnabled || document.panelSettings == null ||
				    document.panelSettings.renderMode != PanelRenderMode.ScreenSpaceOverlay) continue;

				IPanel panel = document.rootVisualElement?.panel;
				if (panel == null) continue;
				VisualElement picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, screenTopLeft));
				if (picked != null && picked != document.rootVisualElement && picked != document.rootVisualElement.parent) return picked;
			}

			return null;
		}
	}
}
