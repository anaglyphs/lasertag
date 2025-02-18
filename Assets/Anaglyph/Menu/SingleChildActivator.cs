using UnityEngine;

namespace Anaglyph.Menu
{
	public class SingleChildActivator : MonoBehaviour
	{
		[SerializeField] private GameObject initialActiveChild;

		private async void Start()
		{
			int childCount = transform.childCount;
			for (int i = 0; i < childCount; i++)
			{
				GameObject g = transform.GetChild(i).gameObject;

				g.AddComponent<DeactivateSiblingsOnEnable>();
			}

			await Awaitable.EndOfFrameAsync();
			
			if (initialActiveChild != null && initialActiveChild.transform.parent == transform)
				SetActiveChild(initialActiveChild.transform.GetSiblingIndex());
			else
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
}