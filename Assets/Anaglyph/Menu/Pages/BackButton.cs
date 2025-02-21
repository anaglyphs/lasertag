using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class BackButton : MonoBehaviour
    {
		private NavPage navPage;

		private void Awake()
		{
			navPage = GetComponentInParent<NavPage>(true);

			GetComponent<Button>().onClick.AddListener(delegate
			{
				navPage.ParentView.GoBack();
			});
		}
	}
}
