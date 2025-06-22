using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Anaglyph.Menu
{

	[AttributeUsage(AttributeTargets.Class, Inherited = true)]
	public class EditorSelectionAffectsVisibility : Attribute
	{
	}

#if UNITY_EDITORE
	/// <summary>
	/// This automatically hides all siblings of descendents of components implementing <see cref="IEditorSelectionAffectsVisibility"/>.
	/// This is useful for objects who's activation is usually mutually exclusive (i.e. different pages in a user interface).
	/// </summary>
	[InitializeOnLoad]
	static class EditorSelectionVisibilityHelper
	{
		static SceneVisibilityManager Visibility => SceneVisibilityManager.instance;

		static EditorSelectionVisibilityHelper()
		{
			Selection.selectionChanged -= OnSelectionChanged;
			Selection.selectionChanged += OnSelectionChanged;
		}

		private static void OnSelectionChanged()
		{
			GameObject selectedObject = Selection.activeGameObject;

			if (selectedObject == null)
				return;

			List<Component> hiders = selectedObject.GetComponentsWithAttributeInParent<EditorSelectionAffectsVisibility>(true);

			for (int h = hiders.Count - 1; h >= 0; h--)
			{
				Component hider = hiders[h];

				for (int i = 0; i < hider.transform.childCount; i++)
				{
					Transform child = hider.transform.GetChild(i);

					if (selectedObject.transform.IsChildOf(child))
						Visibility.Show(child.gameObject, true);
					else
						Visibility.Hide(child.gameObject, true);
				}
			}
		}

		private static List<Component> GetComponentsWithAttributeInParent<TAttribute>(this GameObject gameObject, bool includeInactive = true)
			where TAttribute : Attribute
		{
			var result = new List<Component>();
			var current = gameObject.transform;

			while (current != null)
			{
				if (!includeInactive && !current.gameObject.activeInHierarchy)
				{
					current = current.parent;
					continue;
				}

				foreach (var comp in current.GetComponents<Component>())
				{
					if (comp == null) continue;
					if (comp.GetType().GetCustomAttribute<TAttribute>(false) != null)
					{
						result.Add(comp);
					}
				}

				current = current.parent;
			}

			return result;
		}

	}

#endif
}