using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class BackNavButton : MonoBehaviour
    {
		private NavPage navPage;

		private void Awake()
		{
			this.SetComponetFromParent(ref navPage);

			GetComponent<Button>().onClick.AddListener(delegate
			{
				navPage.ParentView.GoBack();
			});
		}
	}
}
