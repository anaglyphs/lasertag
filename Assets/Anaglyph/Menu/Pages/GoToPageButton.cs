using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class GoToPageButton : MonoBehaviour
    {
		public NavigationPage page;

		private void Awake()
		{
			GetComponent<Button>().onClick.AddListener(delegate
			{
				GetComponentInParent<PageNavigationView>(true).GoToPage(page);
			});
		}
	}
}
