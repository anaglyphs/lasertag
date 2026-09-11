using System;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	public static class UIQuery
	{
		public static T Require<T>(VisualElement root, string name) where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");
			return element;
		}
	}
}
