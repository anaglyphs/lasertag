#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.Menu
{

	[InitializeOnLoad]
	static class VisibilityHelper
	{
		static SceneVisibilityManager Visibility => SceneVisibilityManager.instance;

		static VisibilityHelper()
		{
			Selection.selectionChanged += delegate
			{
				GameObject selectedObject = Selection.activeGameObject;

				if (selectedObject == null)
					return;

				List<MonoBehaviour> hiders = new();

				GameObject g = selectedObject;

				while (g != null)
				{
					if (g.TryGetComponent(out SingleActiveChild c))
						hiders.Add(c);

					if (g.TryGetComponent(out NavPagesParent n))
						hiders.Add(n);

					g = g.transform.parent?.gameObject;
				}

				for (int h = hiders.Count - 1; h >= 0; h--)
				{
					MonoBehaviour hider = hiders[h];

					for (int i = 0; i < hider.transform.childCount; i++)
					{
						Transform child = hider.transform.GetChild(i);

						if (selectedObject.transform.IsChildOf(child))
							Visibility.Show(child.gameObject, true);
						else
							Visibility.Hide(child.gameObject, true);
					}
				}
			};
		}
	}
}


#endif