using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MenuXR
{
	public class SingleChildActivator : SuperAwakeBehavior
	{
		protected override void SuperAwake()
		{
			int childCount = transform.childCount;
			for (int i = 0; i < childCount; i++)
			{
				GameObject g = transform.GetChild(i).gameObject;

				g.AddComponent<DeactivateSiblingsOnEnable>();
			}

			SetActiveChild(0);
		}

		public void DeactivateAllChildren()
		{
			for (int i = 0; i < transform.childCount; i++) 
				transform.GetChild(i).gameObject.SetActive(false);
		}

		public void SetActiveChild(int index)
		{
			transform.GetChild(index).gameObject.SetActive(true);
		}
	}

#if UNITY_EDITOR

	[InitializeOnLoad]
	static class SingleChildActivatorEditorHelper
	{
		static SingleChildActivatorEditorHelper()
		{
			Selection.selectionChanged -= OnEditorSelectionChange;
			Selection.selectionChanged += OnEditorSelectionChange;
		}

		private static void OnEditorSelectionChange()
		{
			GameObject selected = Selection.activeGameObject;
			Transform parent = selected?.transform.parent;

			if (parent?.GetComponent<SingleChildActivator>() == null)
				return;

			for (int i = 0; i < parent.childCount; i++)
			{
				GameObject g = parent.GetChild(i).gameObject;

				g.SetActive(g == selected);
			}

			if (selected != null && selected.transform.parent == parent)
			{
				for (int i = 0; i < parent.childCount; i++)
				{
					GameObject g = parent.GetChild(i).gameObject;

					g.SetActive(g == selected);
				}
			}
		}
	}

#endif
}