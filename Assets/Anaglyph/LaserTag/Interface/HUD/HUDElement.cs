using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface.HUD
{
	/// <summary>
	/// Helpers for HUD behaviours that drive named elements of a UIDocument
	/// found on the same object or a parent.
	/// </summary>
	public static class HUDElement
	{
		public static T Require<T>(Component component, string name)
			where T : VisualElement
		{
			UIDocument document = component.GetComponentInParent<UIDocument>(true);
			if (document == null)
				throw new InvalidOperationException(
					$"{component.GetType().Name} requires a UIDocument on itself or a parent.");

			T element = document.rootVisualElement?.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}

		public static void SetDisplayed(VisualElement element, bool displayed)
		{
			element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
