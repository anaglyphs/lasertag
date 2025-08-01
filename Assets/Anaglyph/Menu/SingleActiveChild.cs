using UnityEngine;

namespace Anaglyph.Menu
{
	[HideSiblingsOfSelectedChild]
	public class SingleActiveChild : MonoBehaviour
	{
		[SerializeField] private GameObject initialActiveChild;

		private async void Start()
		{
			int childCount = transform.childCount;
			for (int i = 0; i < childCount; i++)
			{
				GameObject g = transform.GetChild(i).gameObject;

				g.AddComponent<DeactivateSiblings>();
			}

			await Awaitable.EndOfFrameAsync();

			if (initialActiveChild != null && initialActiveChild.transform.parent == transform)
				SetActiveChild(initialActiveChild.transform.GetSiblingIndex());
			else
				SetActiveChild(0);
		}

		public void SetActiveChild(int index)
		{
			for (int i = 0; i < transform.childCount; i++)
				transform.GetChild(i).gameObject.SetActive(i == index);
		}
	}
}