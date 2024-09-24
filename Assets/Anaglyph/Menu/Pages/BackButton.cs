using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class BackButton : MonoBehaviour
    {
		private void Awake()
		{
			GetComponent<Button>().onClick.AddListener(delegate
			{
				GetComponentInParent<PageNavigationView>().GoBack();
			});
		}
	}
}
